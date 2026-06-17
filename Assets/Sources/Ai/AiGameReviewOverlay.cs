using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public sealed class AiGameReviewOverlay : MonoBehaviour
{
    private GameObject _overlay;
    private Text _titleText;
    private Text _bodyText;
    private RectTransform _bodyContentRect;
    private Button _retryButton;
    private Action _onBackToResult;
    private AiGameReviewRequest _currentRequest;
    private Coroutine _requestRoutine;

    private Font _font;
    private Sprite _buttonSprite;
    private Sprite _panelSprite;
    private Color _overlayBackground;
    private Color _panelBackground;
    private Color _buttonColor;
    private Color _buttonTextColor;
    private Color _textColor;

    public void Build(
        Transform parent,
        Font font,
        Sprite buttonSprite,
        Sprite panelSprite,
        Color overlayBackground,
        Color panelBackground,
        Color buttonColor,
        Color buttonTextColor,
        Color textColor)
    {
        _font = font;
        _buttonSprite = buttonSprite;
        _panelSprite = panelSprite;
        _overlayBackground = overlayBackground;
        _panelBackground = panelBackground;
        _buttonColor = buttonColor;
        _buttonTextColor = buttonTextColor;
        _textColor = textColor;

        _overlay = MakeImage("AiGameReviewOverlay", parent, _overlayBackground, Vector2.zero, Vector2.one);
        _overlay.GetComponent<Image>().raycastTarget = true;

        GameObject panel = MakeImage(
            "ReviewPanel",
            _overlay.transform,
            _panelBackground,
            new Vector2(0.08f, 0.08f),
            new Vector2(0.92f, 0.92f),
            _panelSprite);

        _titleText = MakeText(
            "ReviewTitle",
            panel.transform,
            new Vector2(0.06f, 0.84f),
            new Vector2(0.94f, 0.96f),
            54,
            FontStyle.Bold,
            TextAnchor.MiddleCenter);
        _titleText.text = "Game Review";

        BuildScrollableBody(panel.transform);

        _retryButton = MakeButton(
            "RetryReview",
            panel.transform,
            new Vector2(0.06f, 0.06f),
            new Vector2(0.31f, 0.15f),
            "Retry");
        _retryButton.onClick.AddListener(RequestReview);

        Button back = MakeButton(
            "BackToResult",
            panel.transform,
            new Vector2(0.375f, 0.06f),
            new Vector2(0.625f, 0.15f),
            "Back");
        back.onClick.AddListener(BackToResult);

        Button menu = MakeButton(
            "ReviewMainMenu",
            panel.transform,
            new Vector2(0.69f, 0.06f),
            new Vector2(0.94f, 0.15f),
            "Main Menu");
        menu.onClick.AddListener(() => GameModeManager.Instance?.ExitCurrentGameToMainMenu());

        _overlay.SetActive(false);
    }

    public void Show(AiGameReviewRequest request, Action onBackToResult)
    {
        _currentRequest = request;
        _onBackToResult = onBackToResult;

        if (_overlay != null)
        {
            _overlay.SetActive(true);
        }

        RequestReview();
    }

    public void Hide()
    {
        if (_requestRoutine != null)
        {
            StopCoroutine(_requestRoutine);
            _requestRoutine = null;
        }

        if (_overlay != null)
        {
            _overlay.SetActive(false);
        }
    }

    private void RequestReview()
    {
        if (_currentRequest == null)
        {
            SetError("No game state is available for review.");
            return;
        }

        if (_requestRoutine != null)
        {
            StopCoroutine(_requestRoutine);
        }

        _requestRoutine = StartCoroutine(RequestReviewRoutine());
    }

    private IEnumerator RequestReviewRoutine()
    {
        SetLoading();

        yield return AiCoachClient.EnsureInScene().ReviewGame(
            _currentRequest,
            review =>
            {
                _requestRoutine = null;
                SetReview(string.IsNullOrWhiteSpace(review) ? "No review was returned." : review);
            },
            error =>
            {
                _requestRoutine = null;
                Debug.LogWarning($"[AiGameReviewOverlay] {error}");
                SetError(error);
            });
    }

    private void BackToResult()
    {
        Hide();
        _onBackToResult?.Invoke();
    }

    private void SetLoading()
    {
        if (_titleText != null)
        {
            _titleText.text = "Reviewing game";
        }

        if (_retryButton != null)
        {
            _retryButton.interactable = false;
        }

        SetBodyText("Asking the coach to review the key moments...");
    }

    private void SetReview(string review)
    {
        if (_titleText != null)
        {
            _titleText.text = "Game Review";
        }

        if (_retryButton != null)
        {
            _retryButton.interactable = true;
        }

        SetBodyText(review);
    }

    private void SetError(string error)
    {
        if (_titleText != null)
        {
            _titleText.text = "Review unavailable";
        }

        if (_retryButton != null)
        {
            _retryButton.interactable = true;
        }

        SetBodyText(string.IsNullOrWhiteSpace(error)
            ? "The AI review endpoint could not be reached."
            : error);
    }

    private void SetBodyText(string text)
    {
        if (_bodyText == null)
        {
            return;
        }

        _bodyText.text = text;
        Canvas.ForceUpdateCanvases();

        if (_bodyContentRect != null)
        {
            float height = Mathf.Max(520f, _bodyText.preferredHeight + 32f);
            _bodyContentRect.sizeDelta = new Vector2(0f, height);
        }
    }

    private void BuildScrollableBody(Transform parent)
    {
        GameObject scrollRoot = MakeImage(
            "ReviewScroll",
            parent,
            new Color(0f, 0f, 0f, 0.22f),
            new Vector2(0.06f, 0.19f),
            new Vector2(0.94f, 0.80f));

        GameObject viewport = MakeImage(
            "Viewport",
            scrollRoot.transform,
            new Color(1f, 1f, 1f, 0.02f),
            Vector2.zero,
            Vector2.one);
        Mask mask = viewport.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        GameObject content = new GameObject("Content", typeof(RectTransform), typeof(Text));
        content.transform.SetParent(viewport.transform, false);
        _bodyContentRect = content.GetComponent<RectTransform>();
        _bodyContentRect.anchorMin = new Vector2(0f, 1f);
        _bodyContentRect.anchorMax = new Vector2(1f, 1f);
        _bodyContentRect.pivot = new Vector2(0.5f, 1f);
        _bodyContentRect.offsetMin = new Vector2(18f, 0f);
        _bodyContentRect.offsetMax = new Vector2(-18f, 0f);
        _bodyContentRect.sizeDelta = new Vector2(0f, 520f);

        _bodyText = content.GetComponent<Text>();
        _bodyText.alignment = TextAnchor.UpperLeft;
        _bodyText.color = _textColor;
        _bodyText.fontSize = 30;
        _bodyText.font = _font;
        _bodyText.horizontalOverflow = HorizontalWrapMode.Wrap;
        _bodyText.verticalOverflow = VerticalWrapMode.Overflow;

        ScrollRect scrollRect = scrollRoot.AddComponent<ScrollRect>();
        scrollRect.viewport = viewport.GetComponent<RectTransform>();
        scrollRect.content = _bodyContentRect;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
    }

    private Text MakeText(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
        int fontSize, FontStyle style, TextAnchor alignment)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Text text = go.GetComponent<Text>();
        text.alignment = alignment;
        text.color = _textColor;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.font = _font;
        return text;
    }

    private GameObject MakeImage(string name, Transform parent, Color color, Vector2 anchorMin, Vector2 anchorMax, Sprite sprite = null)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = go.GetComponent<Image>();
        image.color = color;
        if (sprite != null)
        {
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
        }

        return go;
    }

    private Button MakeButton(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, string label)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = go.GetComponent<Image>();
        image.color = _buttonColor;
        if (_buttonSprite != null)
        {
            image.sprite = _buttonSprite;
            image.type = Image.Type.Sliced;
        }

        Button button = go.GetComponent<Button>();

        Text text = MakeText("Label", go.transform, Vector2.zero, Vector2.one, 30, FontStyle.Normal, TextAnchor.MiddleCenter);
        text.text = label;
        text.color = _buttonTextColor;

        return button;
    }
}
