using System.Collections;
using TMPro;
using UnityEngine;

public sealed class AiMoveCoachController : MonoBehaviour
{
    private const string WaitingText = "Coach is watching your moves.";
    private const string LoadingText = "Coach is thinking...";
    private const string ErrorText = "Coach is unavailable right now.";

    private GameObject _bubbleRoot;
    private TMP_Text _bubbleText;
    private string _previousFen;
    private int _requestVersion;
    private bool _hudAllowed = true;
    private bool _subscribed;

    public void Bind(GameObject bubbleRoot, TMP_Text bubbleText)
    {
        _bubbleRoot = bubbleRoot;
        _bubbleText = bubbleText;
        ResetState();
    }

    public void SetHudAllowed(bool allowed)
    {
        _hudAllowed = allowed;
        RefreshVisibility();
    }

    private void OnEnable()
    {
        Subscribe();
        ResetState();
    }

    private void Start()
    {
        Subscribe();
        ResetState();
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

        GameEvents.OnMoveMade += HandleMoveMade;
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

        GameEvents.OnMoveMade -= HandleMoveMade;
        GameEvents.OnBoardReset -= HandleBoardReset;
        GameEvents.OnGameOver -= HandleGameOver;
        _subscribed = false;
    }

    private void HandleBoardReset()
    {
        ResetState();
    }

    private void HandleGameOver(GameResult _)
    {
        RefreshVisibility();
    }

    private void ResetState()
    {
        _previousFen = GameStateManager.Instance != null ? GameStateManager.Instance.ToFen() : string.Empty;
        SetBubbleText(WaitingText);
        RefreshVisibility();
    }

    private void HandleMoveMade(MoveRecord move)
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null)
        {
            return;
        }

        string fenAfter = gsm.ToFen();
        if (!IsLiveCoachEnabled())
        {
            _previousFen = fenAfter;
            return;
        }

        bool playerMove = IsWhitePiece(move.PieceMoved);
        if (!playerMove)
        {
            _previousFen = fenAfter;
            return;
        }

        string fenBefore = _previousFen;
        _previousFen = fenAfter;
        if (string.IsNullOrEmpty(fenBefore))
        {
            return;
        }

        int requestVersion = ++_requestVersion;
        SetBubbleText(LoadingText);

        var payload = new AiAnalyzeMoveRequest
        {
            FenBefore = fenBefore,
            FenAfter = fenAfter,
            MovePlayed = move.ToUci(),
            PlayerColor = "white",
            MoveNumber = Mathf.Max(1, (gsm.MoveHistory.Count + 1) / 2)
        };

        StartCoroutine(RequestFeedback(requestVersion, payload));
    }

    private IEnumerator RequestFeedback(int requestVersion, AiAnalyzeMoveRequest payload)
    {
        yield return AiCoachClient.EnsureInScene().AnalyzeMove(
            payload,
            feedback =>
            {
                if (requestVersion == _requestVersion)
                {
                    SetBubbleText(string.IsNullOrWhiteSpace(feedback) ? "No feedback available." : feedback);
                }
            },
            error =>
            {
                if (requestVersion == _requestVersion)
                {
                    Debug.LogWarning($"[AiMoveCoachController] {error}");
                    SetBubbleText(ErrorText);
                }
            });
    }

    private void RefreshVisibility()
    {
        if (_bubbleRoot != null)
        {
            _bubbleRoot.SetActive(IsLiveCoachEnabled());
        }
    }

    private bool IsLiveCoachEnabled()
    {
        var gmm = GameModeManager.Instance;
        var gsm = GameStateManager.Instance;
        return _hudAllowed &&
               gmm != null &&
               gmm.IsVsAI &&
               (gsm == null || gsm.Result == GameResult.Ongoing);
    }

    private void SetBubbleText(string text)
    {
        if (_bubbleText != null)
        {
            _bubbleText.text = text;
        }
    }

    private static bool IsWhitePiece(Piece piece) => (int)piece > 0;
}
