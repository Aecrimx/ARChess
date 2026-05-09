using System.Collections.Generic;
using Mirror;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  LanNetworkManager
//
//  RESPONSIBILITY: Manage Mirror connections, colour assignment, scene
//  transitions, and rematch logic for LAN games.
//
//  SCENE SETUP:
//  1. Add a GameObject "LanNetworkManager" to the MainMenu scene.
//  2. Attach this script + LanDiscovery to it.
//  3. Assign playerPrefab → ChessNetworkProxy prefab.
//  4. Set onlineScene → "ChessScene" (exact scene name).
//  5. Mirror's NetworkManager handles DontDestroyOnLoad automatically.
// ─────────────────────────────────────────────────────────────────────────────
public class LanNetworkManager : NetworkManager
{
    // Convenient typed singleton (Mirror already provides NetworkManager.singleton)
    public static LanNetworkManager Instance =>
        singleton as LanNetworkManager;

    // ── Config set by HostLobbyPanel before StartHost() ───────────────────────
    [HideInInspector] public string  HostPlayerName   = "Player";
    [HideInInspector] public float   TimerSeconds     = float.MaxValue;

    // The chess scene to load when both players are ready.
    // NOTE: leave Mirror's built-in 'Online Scene' field BLANK in the Inspector
    // so Mirror does NOT auto-transition on StartHost(). We control scene changes.
    [Header("Chess Settings")]
    public string gameSceneName = "ChessScene";

    // ── Connection tracking ───────────────────────────────────────────────────
    // Index 0 = host connection, Index 1 = remote client connection.
    private readonly List<NetworkConnectionToClient> _connOrder =
        new List<NetworkConnectionToClient>();

    // Colour assignment (host gets this, client gets the opposite)
    private bool _hostIsWhite;

    // ── Timer sync ────────────────────────────────────────────────────────────
    private float _timerSyncInterval = 2f;
    private float _timerSyncTimer    = 0f;

    // ── Client Ready State ────────────────────────────────────────────────────
    private int _readyCount = 0;

    // ════════════════════════════════════════════════════════════════════════════
    //  SERVER CALLBACKS
    // ════════════════════════════════════════════════════════════════════════════

    public override void OnServerConnect(NetworkConnectionToClient conn)
    {
        base.OnServerConnect(conn);
        // Reject more than 2 players
        if (numPlayers >= 2)
        {
            conn.Disconnect();
        }
    }

    public override void OnServerAddPlayer(NetworkConnectionToClient conn)
    {
        base.OnServerAddPlayer(conn);   // spawns the playerPrefab
        _connOrder.Add(conn);

        Debug.Log($"[LanNetworkManager] Player joined. Total: {_connOrder.Count}");

        if (_connOrder.Count == 2)
        {
            // Randomise colours
            _hostIsWhite = Random.value >= 0.5f;

            // Assign SyncVar IsWhite on each proxy before scene change
            for (int i = 0; i < _connOrder.Count; i++)
            {
                var proxy = _connOrder[i].identity?.GetComponent<ChessNetworkProxy>();
                if (proxy != null)
                    proxy.IsWhite = (i == 0) == _hostIsWhite;
            }

            // Load the game scene for all clients (manual — Mirror's onlineScene is left blank)
            _readyCount = 0;
            ServerChangeScene(gameSceneName);
        }
    }

    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        _connOrder.Remove(conn);
        base.OnServerDisconnect(conn);

        // If a player leaves mid-game, end the game
        if (GameStateManager.Instance != null &&
            GameStateManager.Instance.Result == GameResult.Ongoing)
        {
            GameStateManager.Instance.ForceGameOver(GameResult.OpponentDisconnected);
        }
    }

    public override void OnServerSceneChanged(string sceneName)
    {
        base.OnServerSceneChanged(sceneName);

        if (sceneName != gameSceneName) return;

        // Ensure IsNetworked is true on the host's GameStateManager early
        if (GameStateManager.Instance != null)
        {
            GameStateManager.Instance.IsNetworked = true;
        }
    }

    public override void OnServerReady(NetworkConnectionToClient conn)
    {
        base.OnServerReady(conn);

        // We only care about ready messages when in the game scene
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != gameSceneName) return;

        _readyCount++;
        Debug.Log($"[LanNetworkManager] Client ready ({_readyCount}/2).");

        if (_readyCount == 2)
        {
            Debug.Log("[LanNetworkManager] Both clients ready — starting game");
            
            if (GameStateManager.Instance != null)
            {
                GameStateManager.Instance.IsNetworked = true;
                GameStateManager.Instance.InitBoard(TimerSeconds);
            }

            if (ChessClock.Instance != null)
                ChessClock.Instance.StartClock(TimerSeconds, isAuthority: true);

            for (int i = 0; i < _connOrder.Count; i++)
            {
                var identity = _connOrder[i].identity;
                if (identity == null) continue;
                var proxy = identity.GetComponent<ChessNetworkProxy>();
                proxy?.TargetGameStarted(_connOrder[i], isWhite: (i == 0) == _hostIsWhite, TimerSeconds);
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  CLIENT CALLBACKS
    // ════════════════════════════════════════════════════════════════════════════

    public override void OnClientConnect()
    {
        base.OnClientConnect();
        // Player name will be sent via ChessNetworkProxy SyncVar after spawn
    }

    public override void OnClientDisconnect()
    {
        base.OnClientDisconnect();
        Debug.Log("[LanNetworkManager] Disconnected from server.");
        
        // If we are still in the game scene, the host dropped
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == gameSceneName)
        {
            if (GameStateManager.Instance != null &&
                GameStateManager.Instance.Result == GameResult.Ongoing)
            {
                GameStateManager.Instance.ForceGameOver(GameResult.OpponentDisconnected);
            }
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  REMATCH (called by GameOverOverlay on the host)
    // ════════════════════════════════════════════════════════════════════════════

    [Server]
    public void RequestRematch()
    {
        _hostIsWhite = Random.value >= 0.5f;

        if (GameStateManager.Instance != null)
            GameStateManager.Instance.InitBoard(TimerSeconds);

        if (ChessClock.Instance != null)
            ChessClock.Instance.StartClock(TimerSeconds, isAuthority: true);

        // Notify proxies of new colours via TargetRpc (one colour per player)
        for (int i = 0; i < _connOrder.Count; i++)
        {
            var identity = _connOrder[i].identity;
            if (identity == null) continue;
            var proxy = identity.GetComponent<ChessNetworkProxy>();
            proxy?.TargetGameStarted(_connOrder[i], isWhite: (i == 0) == _hostIsWhite, TimerSeconds);
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  TIMER SYNC (server → client, periodic)
    // ════════════════════════════════════════════════════════════════════════════

    public override void Update()
    {
        if (!NetworkServer.active) return;
        if (GameStateManager.Instance == null) return;
        if (GameStateManager.Instance.Result != GameResult.Ongoing) return;

        _timerSyncTimer += Time.deltaTime;
        if (_timerSyncTimer < _timerSyncInterval) return;
        _timerSyncTimer = 0f;

        // Broadcast current times to all proxies
        foreach (var conn in _connOrder)
        {
            var identity = conn.identity;
            if (identity == null) continue;   // guard: destroyed during scene transitions
            var proxy = identity.GetComponent<ChessNetworkProxy>();
            proxy?.RpcTimerSync(
                GameStateManager.Instance.WhiteTimeRemaining,
                GameStateManager.Instance.BlackTimeRemaining);
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  HELPERS
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Clean disconnect usable from GameOverOverlay or any UI button.
    /// Works whether this device is host or client.
    /// </summary>
    public void Disconnect()
    {
        if (NetworkServer.active) StopHost();
        else                      StopClient();
    }
}
