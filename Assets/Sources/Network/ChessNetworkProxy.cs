using System.Collections;
using Mirror;
using UnityEngine;

// —————————————————————————————————————————————————————————————————————————————
//  ChessNetworkProxy
//
//  RESPONSIBILITY: Per-player networked bridge between Mirror and local game
//  logic. One instance is spawned by LanNetworkManager for each connection
//  and survives the lobby -> ChessScene transition inside DontDestroyOnLoad.
//
//  AUTHORITY MODEL:
//  • Commands  (CmdRequestMove)  — run on the server, sent by the owning client.
//  • ClientRpc (RpcApplyMove)    — run on all clients except the server itself.
//  • TargetRpc (TargetGameStarted) — configures each client with their colour.
//  • ClientRpc (RpcTimerSync)    — updates the client clock display from the host.
// —————————————————————————————————————————————————————————————————————————————
public class ChessNetworkProxy : NetworkBehaviour
{
    [SyncVar] public bool IsWhite;
    [SyncVar] public string PlayerName = "Player";

    private Coroutine _pendingGameStartRoutine;
    private GameSnapshot _pendingPredictedSnapshot;
    private bool _hasPendingPredictedMove;
    private Vector2Int _pendingPredictedFrom = new Vector2Int(-1, -1);
    private Vector2Int _pendingPredictedTo = new Vector2Int(-1, -1);
    private Piece _pendingPredictedPromotion = Piece.None;

    public override void OnStartLocalPlayer()
    {
        CmdSetName(GameModeManager.Instance != null
            ? GameModeManager.Instance.LocalPlayerName
            : "Player");

        if (GameModeManager.Instance != null && GameModeManager.Instance.IsLan)
        {
            if (GameStateManager.Instance != null)
                GameStateManager.Instance.IsNetworked = true;
        }
    }

    [Command]
    public void CmdSetName(string name)
    {
        PlayerName = name;
    }

    [Command]
    public void CmdReportSceneReady()
    {
        LanNetworkManager.Instance?.NotifyClientSceneReady(connectionToClient);
    }

    [Command]
    public void CmdRequestMove(int fromRow, int fromCol, int toRow, int toCol, int promotionPiece)
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null) return;

        bool myTurn = (IsWhite && gsm.IsWhiteTurn) || (!IsWhite && !gsm.IsWhiteTurn);
        if (!myTurn)
        {
            Debug.LogWarning("[ChessNetworkProxy] Move rejected: not this player's turn.");
            TargetMoveRejected(connectionToClient, fromRow, fromCol, toRow, toCol, promotionPiece);
            return;
        }

        var from = new Vector2Int(fromRow, fromCol);
        var to = new Vector2Int(toRow, toCol);
        bool ok = gsm.TryApplyMove(from, to, (Piece)promotionPiece);

        if (ok)
        {
            RpcApplyMove(fromRow, fromCol, toRow, toCol, promotionPiece);
        }
        else
        {
            Debug.LogWarning("[ChessNetworkProxy] Move rejected by server validation.");
            TargetMoveRejected(connectionToClient, fromRow, fromCol, toRow, toCol, promotionPiece);
        }
    }

    [ClientRpc]
    public void RpcApplyMove(int fromRow, int fromCol, int toRow, int toCol, int promotionPiece)
    {
        if (NetworkServer.active) return;

        if (MatchesPendingPredictedMove(fromRow, fromCol, toRow, toCol, promotionPiece))
        {
            ClearPendingPredictedMove();
            return;
        }

        var gsm = GameStateManager.Instance;
        if (gsm == null) return;

        gsm.TryApplyMove(
            new Vector2Int(fromRow, fromCol),
            new Vector2Int(toRow, toCol),
            (Piece)promotionPiece);
    }

    [TargetRpc]
    private void TargetMoveRejected(NetworkConnectionToClient conn,
        int fromRow, int fromCol, int toRow, int toCol, int promotionPiece)
    {
        if (!MatchesPendingPredictedMove(fromRow, fromCol, toRow, toCol, promotionPiece))
            return;

        RestorePendingPredictedSnapshot();
        Debug.LogWarning("[ChessNetworkProxy] Predicted move was rejected by the server and rolled back.");
    }

    [TargetRpc]
    public void TargetGameStarted(NetworkConnectionToClient conn, bool isWhite, float timerSeconds)
    {
        BeginLocalGameStart(isWhite, timerSeconds);
    }

    public void BeginLocalGameStart(bool isWhite, float timerSeconds)
    {
        if (_pendingGameStartRoutine != null)
            StopCoroutine(_pendingGameStartRoutine);

        _pendingGameStartRoutine = StartCoroutine(ApplyGameStartedWhenReady(isWhite, timerSeconds));
    }

    public bool TrySubmitPredictedLocalMove(Vector2Int from, Vector2Int to, Piece promotion)
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null || !gsm.IsNetworked)
            return false;

        if (_hasPendingPredictedMove)
            return false;

        GameSnapshot snapshot = gsm.TakeSnapshot();
        if (!gsm.TryApplyMove(from, to, promotion))
            return false;

        _pendingPredictedSnapshot = snapshot;
        _hasPendingPredictedMove = true;
        _pendingPredictedFrom = from;
        _pendingPredictedTo = to;
        _pendingPredictedPromotion = promotion;

        CmdRequestMove(from.x, from.y, to.x, to.y, (int)promotion);
        return true;
    }

    [ClientRpc]
    public void RpcTimerSync(float whiteSeconds, float blackSeconds)
    {
        if (NetworkServer.active) return;
        ChessClock.Instance?.SetTimesFromServer(whiteSeconds, blackSeconds);
    }

    private IEnumerator ApplyGameStartedWhenReady(bool isWhite, float timerSeconds)
    {
        IsWhite = isWhite;

        float timeoutAt = Time.unscaledTime + 5f;
        GameStateManager gsm = null;
        Chess2DInputHandler input = null;
        Sources.Hud.GameplayHUDController hud = null;
        Chess2DRenderer renderer = null;

        while (Time.unscaledTime < timeoutAt)
        {
            gsm ??= GameStateManager.Instance;
            input ??= FindAnyObjectByType<Chess2DInputHandler>();
            hud ??= FindAnyObjectByType<Sources.Hud.GameplayHUDController>();
            renderer ??= FindAnyObjectByType<Chess2DRenderer>();

            if (gsm != null && input != null && hud != null && renderer != null && ChessClock.Instance != null)
                break;

            yield return null;
        }

        gsm ??= GameStateManager.Instance;
        input ??= FindAnyObjectByType<Chess2DInputHandler>();
        hud ??= FindAnyObjectByType<Sources.Hud.GameplayHUDController>();
        renderer ??= FindAnyObjectByType<Chess2DRenderer>();

        if (gsm == null)
        {
            Debug.LogWarning("[ChessNetworkProxy] GameStateManager not ready for LAN start.");
            _pendingGameStartRoutine = null;
            yield break;
        }

        gsm.IsNetworked = true;

        if (!NetworkServer.active)
            gsm.InitBoard(timerSeconds);

        if (renderer != null)
        {
            renderer.SetPerspective(isWhite);
            renderer.Activate();
            renderer.RedrawPieces();
        }
        else
        {
            Debug.LogWarning("[ChessNetworkProxy] Chess2DRenderer not found.");
        }

        if (hud != null)
            hud.SetLocalPlayerIsWhite(isWhite);
        else
            Debug.LogWarning("[ChessNetworkProxy] GameplayHUDController not found.");

        if (input != null)
        {
            input.LocalPlayerIsWhite = isWhite;
            input.SetLocalProxy(this);
            input.Activate();
        }
        else
        {
            Debug.LogWarning("[ChessNetworkProxy] Chess2DInputHandler not found.");
        }

        if (!NetworkServer.active && ChessClock.Instance != null)
            ChessClock.Instance.StartClock(gsm.WhiteTimeRemaining, isAuthority: false);

        Debug.Log($"[ChessNetworkProxy] TargetGameStarted -> isWhite={isWhite}  " +
                  $"IsNetworked={gsm.IsNetworked}  proxy={name}");

        _pendingGameStartRoutine = null;
    }

    private bool MatchesPendingPredictedMove(int fromRow, int fromCol, int toRow, int toCol, int promotionPiece)
    {
        return _hasPendingPredictedMove &&
               _pendingPredictedFrom.x == fromRow &&
               _pendingPredictedFrom.y == fromCol &&
               _pendingPredictedTo.x == toRow &&
               _pendingPredictedTo.y == toCol &&
               (int)_pendingPredictedPromotion == promotionPiece;
    }

    private void ClearPendingPredictedMove()
    {
        _pendingPredictedSnapshot = null;
        _hasPendingPredictedMove = false;
        _pendingPredictedFrom = new Vector2Int(-1, -1);
        _pendingPredictedTo = new Vector2Int(-1, -1);
        _pendingPredictedPromotion = Piece.None;
    }

    private void RestorePendingPredictedSnapshot()
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null || _pendingPredictedSnapshot == null)
        {
            ClearPendingPredictedMove();
            return;
        }

        gsm.RestoreSnapshot(_pendingPredictedSnapshot);
        gsm.IsNetworked = true;

        FindAnyObjectByType<Chess2DRenderer>()?.RedrawPieces();
        FindAnyObjectByType<Chess2DRenderer>()?.ClearAllHighlights();
        FindAnyObjectByType<Sources.Hud.CapturedPiecesController>()?.RefreshFromGameState();
        GameEvents.RaiseTurnChanged(gsm.IsWhiteTurn);

        var input = FindAnyObjectByType<Chess2DInputHandler>();
        if (input != null)
        {
            input.LocalPlayerIsWhite = IsWhite;
            input.SetLocalProxy(this);
            input.Activate();
        }

        ClearPendingPredictedMove();
    }
}
