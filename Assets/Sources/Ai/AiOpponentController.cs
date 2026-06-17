using System.Collections;
using UnityEngine;

public sealed class AiOpponentController : MonoBehaviour
{
    public const string DifficultyPrefKey = "AiDifficulty";
    public const string DefaultDifficulty = "normal";

    private bool _subscribed;
    private bool _requestInFlight;
    private int _requestVersion;

    public static AiOpponentController EnsureInScene()
    {
        AiOpponentController existing = FindAnyObjectByType<AiOpponentController>();
        if (existing != null)
        {
            return existing;
        }

        GameObject host = GameStateManager.Instance != null
            ? GameStateManager.Instance.gameObject
            : new GameObject("AiOpponentController");

        return host.AddComponent<AiOpponentController>();
    }

    private void OnEnable()
    {
        Subscribe();
    }

    private void Start()
    {
        Subscribe();
        TryRequestAiMove();
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (_subscribed)
        {
            return;
        }

        GameEvents.OnTurnChanged += HandleTurnChanged;
        GameEvents.OnBoardReset += HandleBoardReset;
        GameEvents.OnGameOver += HandleGameOver;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
        {
            return;
        }

        GameEvents.OnTurnChanged -= HandleTurnChanged;
        GameEvents.OnBoardReset -= HandleBoardReset;
        GameEvents.OnGameOver -= HandleGameOver;
        _subscribed = false;
    }

    private void HandleTurnChanged(bool isWhiteTurn)
    {
        if (!isWhiteTurn)
        {
            TryRequestAiMove();
        }
    }

    private void HandleBoardReset()
    {
        _requestVersion++;
        _requestInFlight = false;
        TryRequestAiMove();
    }

    private void HandleGameOver(GameResult _)
    {
        _requestVersion++;
        _requestInFlight = false;
    }

    private void TryRequestAiMove()
    {
        var gsm = GameStateManager.Instance;
        if (!IsAiGameActive(gsm) || gsm.IsWhiteTurn || _requestInFlight)
        {
            return;
        }

        _requestInFlight = true;
        int requestVersion = ++_requestVersion;

        var payload = new AiMoveRequest
        {
            Fen = gsm.ToFen(),
            Difficulty = GetDifficulty()
        };

        StartCoroutine(RequestAiMove(requestVersion, payload));
    }

    private IEnumerator RequestAiMove(int requestVersion, AiMoveRequest payload)
    {
        yield return AiCoachClient.EnsureInScene().AiMove(
            payload,
            response =>
            {
                if (requestVersion != _requestVersion)
                {
                    return;
                }

                ApplyAiMove(response);
            },
            error =>
            {
                if (requestVersion != _requestVersion)
                {
                    return;
                }

                _requestInFlight = false;
                Debug.LogWarning($"[AiOpponentController] {error}");
            });
    }

    private void ApplyAiMove(AiMoveResponse response)
    {
        _requestInFlight = false;

        var gsm = GameStateManager.Instance;
        if (!IsAiGameActive(gsm) || gsm.IsWhiteTurn)
        {
            return;
        }

        string bestMove = response?.BestMove;
        if (!TryParseUciMove(bestMove, gsm.IsWhiteTurn, out Vector2Int from, out Vector2Int to, out Piece promotion))
        {
            Debug.LogWarning($"[AiOpponentController] AI returned an invalid move: {bestMove}");
            return;
        }

        if (!gsm.TryApplyMove(from, to, promotion))
        {
            Debug.LogWarning($"[AiOpponentController] AI move was rejected by local rules: {bestMove}");
        }
    }

    public static bool TryParseUciMove(
        string uci,
        bool isWhiteMove,
        out Vector2Int from,
        out Vector2Int to,
        out Piece promotion)
    {
        from = new Vector2Int(-1, -1);
        to = new Vector2Int(-1, -1);
        promotion = isWhiteMove ? Piece.WhiteQueen : Piece.BlackQueen;

        if (string.IsNullOrWhiteSpace(uci))
        {
            return false;
        }

        string move = uci.Trim().ToLowerInvariant();
        if (move.Length != 4 && move.Length != 5)
        {
            return false;
        }

        if (!TryParseSquare(move[0], move[1], out from) ||
            !TryParseSquare(move[2], move[3], out to))
        {
            return false;
        }

        if (move.Length == 5)
        {
            return TryParsePromotion(move[4], isWhiteMove, out promotion);
        }

        return true;
    }

    private static bool TryParseSquare(char file, char rank, out Vector2Int square)
    {
        square = new Vector2Int(-1, -1);

        int col = file - 'a';
        int row = rank - '1';
        if (row < 0 || row > 7 || col < 0 || col > 7)
        {
            return false;
        }

        square = new Vector2Int(row, col);
        return true;
    }

    private static bool TryParsePromotion(char piece, bool isWhiteMove, out Piece promotion)
    {
        promotion = Piece.None;
        switch (piece)
        {
            case 'q':
                promotion = isWhiteMove ? Piece.WhiteQueen : Piece.BlackQueen;
                return true;
            case 'r':
                promotion = isWhiteMove ? Piece.WhiteRook : Piece.BlackRook;
                return true;
            case 'b':
                promotion = isWhiteMove ? Piece.WhiteBishop : Piece.BlackBishop;
                return true;
            case 'n':
                promotion = isWhiteMove ? Piece.WhiteKnight : Piece.BlackKnight;
                return true;
            default:
                return false;
        }
    }

    private static bool IsAiGameActive(GameStateManager gsm)
    {
        var gmm = GameModeManager.Instance;
        return gmm != null &&
               gmm.IsVsAI &&
               gsm != null &&
               gsm.Result == GameResult.Ongoing;
    }

    private static string GetDifficulty()
    {
        string difficulty = GameModeManager.Instance != null
            ? GameModeManager.Instance.AiDifficulty
            : PlayerPrefs.GetString(DifficultyPrefKey, DefaultDifficulty);

        return NormalizeDifficulty(difficulty);
    }

    public static string NormalizeDifficulty(string difficulty)
    {
        string normalized = string.IsNullOrWhiteSpace(difficulty)
            ? DefaultDifficulty
            : difficulty.Trim().ToLowerInvariant();

        return normalized == "easy" || normalized == "normal" || normalized == "hard"
            ? normalized
            : DefaultDifficulty;
    }
}
