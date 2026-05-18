using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

// -----------------------------------------------------------------------------
//  LanNetworkManager
//
//  RESPONSIBILITY: Manage Mirror connections, colour assignment, scene
//  transitions, and rematch logic for LAN games.
//
//  SCENE SETUP:
//  1. Add a GameObject "LanNetworkManager" to the MainMenu scene.
//  2. Attach this script + LanDiscovery to it.
//  3. Assign playerPrefab -> ChessNetworkProxy prefab.
//  4. Set onlineScene -> "ChessScene" (exact scene name).
//  5. Mirror's NetworkManager handles DontDestroyOnLoad automatically.
// -----------------------------------------------------------------------------
public class LanNetworkManager : NetworkManager
{
    // Convenient typed singleton (Mirror already provides NetworkManager.singleton)
    public static LanNetworkManager Instance =>
        singleton as LanNetworkManager;

    // Config set by HostLobbyPanel before StartHost()
    [HideInInspector] public string HostPlayerName = "Player";
    [HideInInspector] public float TimerSeconds = float.MaxValue;

    // The chess scene to load when both players are ready.
    // NOTE: leave Mirror's built-in 'Online Scene' field BLANK in the Inspector
    // so Mirror does NOT auto-transition on StartHost(). We control scene changes.
    [Header("Chess Settings")]
    public string gameSceneName = "ChessScene";

    // Current player connections. Do not infer host/client role from list order;
    // Mirror may not add the local host connection first on every platform.
    private readonly List<NetworkConnectionToClient> _connOrder =
        new List<NetworkConnectionToClient>();

    // Colour assignment (host gets this, client gets the opposite)
    private bool _hostIsWhite;

    // Timer sync
    private float _timerSyncInterval = 2f;
    private float _timerSyncTimer = 0f;

    // Explicit scene-ready startup state
    private readonly HashSet<int> _sceneReadyConnections = new HashSet<int>();
    private bool _matchStartedForScene = false;

    public override void OnStartServer()
    {
        base.OnStartServer();
        ResetSessionState();
    }

    public override void OnStopServer()
    {
        ResetSessionState();
        GetComponent<LanDiscovery>()?.StopDiscovery();
        base.OnStopServer();
    }

    public override void OnStopClient()
    {
        _timerSyncTimer = 0f;
        GetComponent<LanDiscovery>()?.StopDiscovery();
        base.OnStopClient();
    }

    // =========================================================================
    //  SERVER CALLBACKS
    // =========================================================================

    public override void OnServerConnect(NetworkConnectionToClient conn)
    {
        base.OnServerConnect(conn);

        // Reject more than 2 players
        if (numPlayers >= 2)
            conn.Disconnect();
    }

    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        base.OnServerAddPlayer(conn); // spawns the playerPrefab
        _connOrder.Add(conn);

        Debug.Log($"[LanNetworkManager] Player joined. Total: {_connOrder.Count}");

        if (_connOrder.Count == 2)
        {
            // Keep host = white and client = black so turn order and board
            // perspective stay predictable across devices.
            _hostIsWhite = true;
            _matchStartedForScene = false;
            _sceneReadyConnections.Clear();

            // Assign SyncVar IsWhite on each proxy before scene change.
            ApplyConnectionAssignments();
            GetComponent<LanDiscovery>()?.StopDiscovery();

            // Load the game scene for all clients (manual - Mirror's onlineScene is left blank)
            ServerChangeScene(gameSceneName);
        }
    }

    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        _connOrder.Remove(conn);
        base.OnServerDisconnect(conn);

        // Ignore delayed shutdown callbacks once we've already returned to menu
        // or switched into a non-LAN mode.
        if (IsActiveLanMatch() &&
            GameStateManager.Instance != null &&
            GameStateManager.Instance.Result == GameResult.Ongoing)
        {
            GameStateManager.Instance.ForceGameOver(GameResult.OpponentDisconnected);
        }
    }

    public override void OnServerSceneChanged(string sceneName)
    {
        base.OnServerSceneChanged(sceneName);

        if (sceneName != gameSceneName)
            return;

        // Ensure IsNetworked is true on the host's GameStateManager early
        if (GameStateManager.Instance != null)
            GameStateManager.Instance.IsNetworked = true;
    }

    // =========================================================================
    //  CLIENT CALLBACKS
    // =========================================================================

    public override void OnClientConnect()
    {
        base.OnClientConnect();
        // Player name will be sent via ChessNetworkProxy SyncVar after spawn
    }

    public override void OnClientDisconnect()
    {
        base.OnClientDisconnect();
        Debug.Log("[LanNetworkManager] Disconnected from server.");

        // Ignore delayed shutdown callbacks once we've already returned to menu
        // or switched into a non-LAN mode.
        if (IsActiveLanMatch() &&
            GameStateManager.Instance != null &&
            GameStateManager.Instance.Result == GameResult.Ongoing)
        {
            GameStateManager.Instance.ForceGameOver(GameResult.OpponentDisconnected);
        }
    }

    public override void OnClientSceneChanged()
    {
        base.OnClientSceneChanged();

        if (SceneManager.GetActiveScene().name != gameSceneName)
            return;

        StartCoroutine(ReportSceneReadyWhenLocalPlayerExists());
    }

    // =========================================================================
    //  REMATCH (called by GameOverOverlay on the host)
    // =========================================================================

    [Server]
    public void RequestRematch()
    {
        _hostIsWhite = true;

        if (GameStateManager.Instance != null)
            GameStateManager.Instance.InitBoard(TimerSeconds);

        if (ChessClock.Instance != null)
            ChessClock.Instance.StartClock(TimerSeconds, isAuthority: true);

        // Notify proxies of new colours via TargetRpc (one colour per player)
        ApplyConnectionAssignments();
        for (int i = 0; i < _connOrder.Count; i++)
        {
            var identity = _connOrder[i].identity;
            if (identity == null)
                continue;

            var proxy = identity.GetComponent<ChessNetworkProxy>();
            bool isWhite = IsWhiteForConnection(_connOrder[i]);
            proxy?.TargetGameStarted(_connOrder[i], isWhite, TimerSeconds);
            Debug.Log($"[LanNetworkManager] Rematch -> conn={_connOrder[i].connectionId} host={IsHostConnection(_connOrder[i])} isWhite={isWhite}");
        }
    }

    // =========================================================================
    //  TIMER SYNC (server -> client, periodic)
    // =========================================================================

    public override void Update()
    {
        if (!NetworkServer.active) return;
        if (GameStateManager.Instance == null) return;
        if (GameStateManager.Instance.Result != GameResult.Ongoing) return;

        _timerSyncTimer += Time.deltaTime;
        if (_timerSyncTimer < _timerSyncInterval) return;
        _timerSyncTimer = 0f;

        // One ClientRpc fan-outs to all connected clients, so sending it once is enough.
        if (_connOrder.Count == 0) return;

        var firstIdentity = _connOrder[0].identity;
        if (firstIdentity == null) return;

        var firstProxy = firstIdentity.GetComponent<ChessNetworkProxy>();
        firstProxy?.RpcTimerSync(
            GameStateManager.Instance.WhiteTimeRemaining,
            GameStateManager.Instance.BlackTimeRemaining);
    }

    // =========================================================================
    //  HELPERS
    // =========================================================================

    /// <summary>
    /// Clean disconnect usable from GameOverOverlay or any UI button.
    /// Works whether this device is host or client.
    /// </summary>
    public void Disconnect()
    {
        GetComponent<LanDiscovery>()?.StopDiscovery();
        if (NetworkServer.active) StopHost();
        else StopClient();
    }

    private void ResetSessionState()
    {
        _connOrder.Clear();
        _timerSyncTimer = 0f;
        _hostIsWhite = true;
        _sceneReadyConnections.Clear();
        _matchStartedForScene = false;
    }

    [Server]
    public void NotifyClientSceneReady(NetworkConnectionToClient conn)
    {
        if (conn == null) return;
        if (SceneManager.GetActiveScene().name != gameSceneName) return;

        if (_sceneReadyConnections.Add(conn.connectionId))
        {
            Debug.Log($"[LanNetworkManager] Scene-ready from connection {conn.connectionId} ({_sceneReadyConnections.Count}/2).");
        }

        if (_matchStartedForScene) return;
        if (_connOrder.Count < 2 || _sceneReadyConnections.Count < 2) return;

        _matchStartedForScene = true;

        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.IsNetworked = true;
            GameStateManager.Instance.InitBoard(TimerSeconds);
        }

        if (ChessClock.Instance != null)
            ChessClock.Instance.StartClock(TimerSeconds, isAuthority: true);

        ApplyConnectionAssignments();
        for (int i = 0; i < _connOrder.Count; i++)
        {
            var identity = _connOrder[i].identity;
            if (identity == null)
                continue;

            var proxy = identity.GetComponent<ChessNetworkProxy>();
            bool isWhite = IsWhiteForConnection(_connOrder[i]);
            proxy?.TargetGameStarted(_connOrder[i], isWhite, TimerSeconds);
            Debug.Log($"[LanNetworkManager] Scene-ready start -> conn={_connOrder[i].connectionId} host={IsHostConnection(_connOrder[i])} isWhite={isWhite}");
        }
    }

    private System.Collections.IEnumerator ReportSceneReadyWhenLocalPlayerExists()
    {
        float timeoutAt = Time.unscaledTime + 5f;
        while (Time.unscaledTime < timeoutAt)
        {
            if (NetworkClient.localPlayer != null)
                break;

            yield return null;
        }

        if (NetworkClient.localPlayer == null)
        {
            Debug.LogWarning("[LanNetworkManager] Local player not ready after scene change.");
            yield break;
        }

        var proxy = NetworkClient.localPlayer.GetComponent<ChessNetworkProxy>();
        if (proxy == null)
        {
            Debug.LogWarning("[LanNetworkManager] Local player proxy missing after scene change.");
            yield break;
        }

        proxy.CmdReportSceneReady();
    }

    private void ApplyConnectionAssignments()
    {
        foreach (NetworkConnectionToClient conn in _connOrder)
        {
            if (conn == null)
                continue;

            var proxy = conn.identity?.GetComponent<ChessNetworkProxy>();
            if (proxy == null)
                continue;

            bool isWhite = IsWhiteForConnection(conn);
            proxy.IsWhite = isWhite;

            Debug.Log($"[LanNetworkManager] Assign colour -> conn={conn.connectionId} host={IsHostConnection(conn)} isWhite={isWhite}");
        }
    }

    private bool IsWhiteForConnection(NetworkConnectionToClient conn)
    {
        return IsHostConnection(conn) ? _hostIsWhite : !_hostIsWhite;
    }

    private bool IsActiveLanMatch()
    {
        return SceneManager.GetActiveScene().name == gameSceneName &&
               GameModeManager.Instance != null &&
               GameModeManager.Instance.IsLan;
    }

    private static bool IsHostConnection(NetworkConnectionToClient conn)
    {
        return conn != null &&
               (conn is LocalConnectionToClient || conn.connectionId == NetworkConnection.LocalConnectionId);
    }
}
