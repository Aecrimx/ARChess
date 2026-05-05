using Mirror;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  ChessNetworkProxy
//
//  RESPONSIBILITY: Per-player networked bridge between Mirror and local game
//  logic.  One instance is spawned by LanNetworkManager for each connection
//  and survives the lobby → ChessScene transition inside DontDestroyOnLoad.
//
//  AUTHORITY MODEL:
//  • Commands  (CmdRequestMove)  — run on the server, sent by the owning client.
//  • ClientRpc (RpcApplyMove)    — run on all clients except the server itself.
//  • ClientRpc (RpcGameStarted)  — run on all clients, tells them their colour.
//  • ClientRpc (RpcTimerSync)    — run on all clients to update clock display.
//
//  SCENE SETUP:
//  1. Create a prefab from an empty GameObject, attach this script.
//  2. Assign it as the playerPrefab in LanNetworkManager.
// ─────────────────────────────────────────────────────────────────────────────
public class ChessNetworkProxy : NetworkBehaviour
{
    // ── Synced state ──────────────────────────────────────────────────────────
    [SyncVar] public bool   IsWhite;
    [SyncVar] public string PlayerName = "Player";

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    public override void OnStartLocalPlayer()
    {
        // Inform the server of our player name
        CmdSetName(GameModeManager.Instance != null
            ? GameModeManager.Instance.LocalPlayerName
            : "Player");
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  COMMANDS  (client → server)
    // ════════════════════════════════════════════════════════════════════════════

    [Command]
    public void CmdSetName(string name)
    {
        PlayerName = name;   // SyncVar — propagates to all clients automatically
    }

    /// <summary>
    /// The local player calls this to request a move.
    /// The server validates, applies and relays the move to the remote client.
    /// </summary>
    [Command]
    public void CmdRequestMove(int fromRow, int fromCol, int toRow, int toCol, int promotionPiece)
    {
        var gsm  = GameStateManager.Instance;
        if (gsm == null) return;

        // Authoritative colour check — prevent a client from moving on the wrong turn
        bool myTurn = (IsWhite && gsm.IsWhiteTurn) || (!IsWhite && !gsm.IsWhiteTurn);
        if (!myTurn)
        {
            Debug.LogWarning("[ChessNetworkProxy] Move rejected: not this player's turn.");
            return;
        }

        var from  = new UnityEngine.Vector2Int(fromRow, fromCol);
        var to    = new UnityEngine.Vector2Int(toRow, toCol);
        bool ok   = gsm.TryApplyMove(from, to, (Piece)promotionPiece);

        if (ok)
        {
            // Relay the validated move to the remote client (skip the server itself)
            RpcApplyMove(fromRow, fromCol, toRow, toCol, promotionPiece);
        }
    }

    // ════════════════════════════════════════════════════════════════════════════
    //  CLIENT RPCs  (server → all clients)
    // ════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tells all clients to apply a move that the server has already validated.
    /// The server skips this to avoid double-applying.
    /// </summary>
    [ClientRpc]
    public void RpcApplyMove(int fromRow, int fromCol, int toRow, int toCol, int promotionPiece)
    {
        // Server already applied the move; only the remote client needs to apply it.
        if (NetworkServer.active) return;

        var gsm = GameStateManager.Instance;
        if (gsm == null) return;

        gsm.TryApplyMove(
            new UnityEngine.Vector2Int(fromRow, fromCol),
            new UnityEngine.Vector2Int(toRow, toCol),
            (Piece)promotionPiece);
    }

    /// <summary>
    /// Called at game start (and after rematch) to tell each client their colour
    /// and set up the HUD perspective.
    /// </summary>
    [ClientRpc]
    public void RpcGameStarted(bool isWhite)
    {
        IsWhite = isWhite;   // redundant with SyncVar but guarantees timing

        // Set HUD perspective
        var hud = FindAnyObjectByType<Sources.Hud.GameplayHUDController>();
        if (hud != null)
            hud.SetLocalPlayerIsWhite(isWhite);

        // Set input handler so only the correct colour's taps are forwarded
        var input = FindAnyObjectByType<Chess2DInputHandler>();
        if (input != null)
        {
            input.LocalPlayerIsWhite = isWhite;
            input.Activate();
        }

        // Start clock on client (non-authoritative — just sets the display)
        if (!NetworkServer.active && ChessClock.Instance != null)
            ChessClock.Instance.StartClock(
                GameStateManager.Instance != null
                    ? GameStateManager.Instance.WhiteTimeRemaining
                    : float.MaxValue,
                isAuthority: false);

        Debug.Log($"[ChessNetworkProxy] Game started. LocalPlayerIsWhite={isWhite}");
    }

    /// <summary>
    /// Periodic timer sync from the server.  Only the client side acts on it.
    /// </summary>
    [ClientRpc]
    public void RpcTimerSync(float whiteSeconds, float blackSeconds)
    {
        if (NetworkServer.active) return;   // host clock is already authoritative
        ChessClock.Instance?.SetTimesFromServer(whiteSeconds, blackSeconds);
    }
}
