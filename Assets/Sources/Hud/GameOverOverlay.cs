using UnityEngine;
using UnityEngine.UI;

public class GameOverOverlay : MonoBehaviour
{
    [Header("Visuals")]
    public Font uiFont;
    public Sprite buttonSprite;
    public Sprite panelSprite;
    public Color overlayBackground = new Color(0f, 0f, 0f, 0.75f);
    public Color panelBackground = new Color(0.12f, 0.12f, 0.12f, 1f);
    public Color buttonColor = new Color(0.20f, 0.60f, 0.30f, 1f);
    public Color buttonTextColor = Color.white;
    public Color resultTextColor = Color.white;

    private GameObject _overlay;
    private Text _resultText;
    private Text _resultSubText;
    private GameObject _rematchButtonGO;

    private void Start()
    {
        BuildOverlay();
        GameEvents.OnGameOver += HandleGameOver;
        GameEvents.OnBoardReset += HandleBoardReset;
    }

    private void OnDestroy()
    {
        GameEvents.OnGameOver -= HandleGameOver;
        GameEvents.OnBoardReset -= HandleBoardReset;
    }

    private void HandleGameOver(GameResult result)
    {
        string[] messages = ResultMessage(result);
        _resultText.text = messages[0];
        _resultSubText.text = messages[1];

        if (_rematchButtonGO != null)
        {
            _rematchButtonGO.SetActive(result != GameResult.OpponentDisconnected);
        }

        _overlay.SetActive(true);
        ChessViewModeController.EnsureInScene()?.SetActiveInputEnabled(false);
    }

    private void HandleBoardReset()
    {
        _overlay.SetActive(false);
        ChessViewModeController.EnsureInScene()?.SetActiveInputEnabled(true);
    }

    private void OnRematchClicked()
    {
        var gmm = GameModeManager.Instance;
        if (gmm != null && gmm.IsLan)
        {
            if (gmm.IsLanHost)
            {
                LanNetworkManager.Instance?.RequestRematch();
            }
            else
            {
                if (_resultText != null)
                {
                    _resultText.text = "Waiting for host...";
                }

                if (_resultSubText != null)
                {
                    _resultSubText.text = string.Empty;
                }
            }

            return;
        }

        GameStateManager.Instance?.InitBoard();
    }

    private void OnMainMenuClicked()
    {
        GameModeManager.Instance?.ExitCurrentGameToMainMenu();
    }

    private void BuildOverlay()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            canvas = FindAnyObjectByType<Canvas>();
        }

        if (canvas == null)
        {
            Debug.LogError("[GameOverOverlay] No Canvas found.");
            return;
        }

        _overlay = MakeImage("GameOverOverlay", canvas.transform, overlayBackground, Vector2.zero, Vector2.one);
        _overlay.GetComponent<Image>().raycastTarget = true;

        GameObject panel = MakeImage("Panel", _overlay.transform, panelBackground, new Vector2(0.2f, 0.3f), new Vector2(0.8f, 0.7f), panelSprite);

        _resultText = MakeText("ResultText", panel.transform, new Vector2(0.05f, 0.75f), new Vector2(0.95f, 0.95f), 75, FontStyle.Bold);
        _resultSubText = MakeText("ResultSubText", panel.transform, new Vector2(0.05f, 0.55f), new Vector2(0.95f, 0.75f), 50, FontStyle.Normal);

        Button rematch = MakeButton("Rematch", panel.transform, new Vector2(0.05f, 0.2f), new Vector2(0.475f, 0.35f), "Rematch");
        rematch.onClick.AddListener(OnRematchClicked);
        _rematchButtonGO = rematch.gameObject;

        Button menu = MakeButton("MainMenu", panel.transform, new Vector2(0.525f, 0.2f), new Vector2(0.95f, 0.35f), "Main Menu");
        menu.onClick.AddListener(OnMainMenuClicked);

        _overlay.SetActive(false);
    }

    private Text MakeText(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, int fontSize, FontStyle style)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Text text = go.GetComponent<Text>();
        text.alignment = TextAnchor.MiddleCenter;
        text.color = resultTextColor;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.font = GetFont();
        return text;
    }

    private static string[] ResultMessage(GameResult result) => result switch
    {
        GameResult.WhiteWins => new[] { "Checkmate!", "White wins" },
        GameResult.BlackWins => new[] { "Checkmate!", "Black wins" },
        GameResult.Stalemate => new[] { "Stalemate!", "Draw" },
        GameResult.DrawByRepetition => new[] { "Draw!", "Threefold repetition" },
        GameResult.DrawByFiftyMoveRule => new[] { "Draw!", "50-move rule" },
        GameResult.WhiteWinsOnTime => new[] { "Time's Up!", "White wins on time" },
        GameResult.BlackWinsOnTime => new[] { "Time's Up!", "Black wins on time" },
        GameResult.OpponentDisconnected => new[] { "Disconnected", "Opponent left the match" },
        _ => new[] { "Game Over", "Unexpected result" }
    };

    private static GameObject MakeImage(string name, Transform parent, Color color, Vector2 anchorMin, Vector2 anchorMax, Sprite sprite = null)
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

    private Font GetFont()
    {
        return uiFont != null ? uiFont : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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
        image.color = buttonColor;
        if (buttonSprite != null)
        {
            image.sprite = buttonSprite;
            image.type = Image.Type.Sliced;
        }

        Button button = go.GetComponent<Button>();

        Text text = MakeText("Label", go.transform, Vector2.zero, Vector2.one, 50, FontStyle.Normal);
        text.text = label;
        text.color = buttonTextColor;

        return button;
    }
}
