using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

// ─────────────────────────────────────────────────────────────────────────────
//  GameOverOverlay
//
//  Displays a full-screen overlay when the game ends.
//  Subscribes to GameEvents.OnGameOver and shows the result with two buttons:
//  Rematch (resets the board) and Main Menu (loads scene index 0).
//
//  SCENE SETUP:
//  Attach this script to your 2dCanvas (or any persistent GameObject).
//  It builds the overlay UI entirely in code — no prefab needed.
//  The overlay is hidden on Start and shown only when OnGameOver fires.
// ─────────────────────────────────────────────────────────────────────────────
public class GameOverOverlay : MonoBehaviour
{
    // ── Optional Inspector overrides ──────────────────────────────────────────
    [Header("Visuals")]
    public Font uiFont;           // drag your font asset here in the Inspector
    public Sprite buttonSprite;   // optional — drag a 9-sliced button sprite here
    public Sprite panelSprite;    // optional — drag a 9-sliced panel sprite here
    public Color overlayBackground = new Color(0f, 0f, 0f, 0.75f);
    public Color panelBackground   = new Color(0.12f, 0.12f, 0.12f, 1f);
    public Color buttonColor       = new Color(0.20f, 0.60f, 0.30f, 1f);
    public Color buttonTextColor   = Color.white;
    public Color resultTextColor   = Color.white;

    // ── References (built at runtime) ─────────────────────────────────────────
    private GameObject _overlay;
    private Text       _resultText;
    private Text       _resultSubText;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Start()
    {
        BuildOverlay();
        GameEvents.OnGameOver   += HandleGameOver;
        GameEvents.OnBoardReset += HandleBoardReset;
    }

    void OnDestroy()
    {
        GameEvents.OnGameOver   -= HandleGameOver;
        GameEvents.OnBoardReset -= HandleBoardReset;
    }

    // ── Event handlers ────────────────────────────────────────────────────────
    private void HandleGameOver(GameResult result)
    {
        var messages = ResultMessage(result);
        _resultText.text = messages[0];
        _resultSubText.text = messages[1];
        _overlay.SetActive(true);

        // Block input handler while overlay is showing
        var input = GetComponent<Chess2DInputHandler>();
        if (input != null) input.Deactivate();
    }

    private void HandleBoardReset()
    {
        _overlay.SetActive(false);

        // Re-enable input
        var input = GetComponent<Chess2DInputHandler>();
        if (input != null) input.Activate();
    }

    // ── Button callbacks ──────────────────────────────────────────────────────
    private void OnRematchClicked()
    {
        GameStateManager.Instance.InitBoard();
        // HandleBoardReset() fires via GameEvents.OnBoardReset
    }

    private void OnMainMenuClicked()
    {
        // Loads scene at build index 0 (set up your main menu scene there)
        // TODO: replace with your main menu scene name once built (KAN-41)
        SceneManager.LoadScene(0);
    }

    // ── UI construction ───────────────────────────────────────────────────────
    private void BuildOverlay()
    {
        // Find the Canvas to parent under
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null) { Debug.LogError("[GameOverOverlay] No Canvas found."); return; }

        // Full-screen dimmer
        _overlay = MakeImage("GameOverOverlay", canvas.transform,
                             new Color(0,0,0,0), Vector2.zero, Vector2.one);
        _overlay.GetComponent<Image>().color = overlayBackground;
        _overlay.GetComponent<Image>().raycastTarget = true; // block clicks on board

        // Centre panel
        var panel = MakeImage("Panel", _overlay.transform,
                              panelBackground, new Vector2(0.2f, 0.3f), new Vector2(0.8f, 0.7f), panelSprite);

        // Result text
        var resultGO  = new GameObject("ResultText", typeof(RectTransform), typeof(Text));
        resultGO.transform.SetParent(panel.transform, false);
        var resultRT  = resultGO.GetComponent<RectTransform>();
        resultRT.anchorMin        = new Vector2(0.05f, 0.75f);
        resultRT.anchorMax        = new Vector2(0.95f, 0.95f);
        resultRT.offsetMin        = Vector2.zero;
        resultRT.offsetMax        = Vector2.zero;
        _resultText               = resultGO.GetComponent<Text>();
        _resultText.alignment     = TextAnchor.MiddleCenter;
        _resultText.color         = resultTextColor;
        _resultText.fontSize      = 75;
        _resultText.fontStyle     = FontStyle.Bold;
        _resultText.font          = GetFont();

        // Result sub-text
        var subTextGO = new GameObject("ResultSubText", typeof(RectTransform), typeof(Text));
        subTextGO.transform.SetParent(panel.transform, false);
        var subTextRT = subTextGO.GetComponent<RectTransform>();
        subTextRT.anchorMin = new Vector2(0.05f, 0.55f);
        subTextRT.anchorMax = new Vector2(0.95f, 0.75f);
        subTextRT.offsetMin = Vector2.zero;
        subTextRT.offsetMax = Vector2.zero;
        _resultSubText = subTextGO.GetComponent<Text>();
        _resultSubText.alignment = TextAnchor.MiddleCenter;
        _resultSubText.color = resultTextColor;
        _resultSubText.fontSize = 50;
        _resultSubText.fontStyle = FontStyle.Normal;
        _resultSubText.font = GetFont();

        // Rematch button
        var rematch = MakeButton("Rematch", panel.transform,
                                 new Vector2(0.05f, 0.2f), new Vector2(0.475f, 0.35f),
                                 "Rematch", buttonColor, buttonTextColor, GetFont(), buttonSprite);
        rematch.onClick.AddListener(OnRematchClicked);

        // Main menu button
        var menu = MakeButton("MainMenu", panel.transform,
                              new Vector2(0.525f, 0.2f), new Vector2(0.95f, 0.35f),
                              "Main Menu", buttonColor, buttonTextColor, GetFont(), buttonSprite);
        menu.onClick.AddListener(OnMainMenuClicked);

        _overlay.SetActive(false);
    }

    // ── Result message ────────────────────────────────────────────────────────
    private static string[] ResultMessage(GameResult result) => result switch
    {
        GameResult.WhiteWins          => new string[] { "Checkmate!", "White wins" },
        GameResult.BlackWins          => new string[] { "Checkmate!", "Black wins" },
        GameResult.Stalemate          => new string[] { "Stalemate!", "Draw" },
        GameResult.DrawByRepetition   => new string[] { "Draw!", "Threefold repetition" },
        GameResult.DrawByFiftyMoveRule => new string[] { "Draw!", "50-move rule" },
        _                             => new string[] { "Game Over", "Unexpected result" }
    };

    // ── UI helpers ────────────────────────────────────────────────────────────
    private static GameObject MakeImage(string name, Transform parent,
                                        Color color, Vector2 anchorMin, Vector2 anchorMax,
                                        Sprite sprite = null)
    {
        var go  = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rt  = go.GetComponent<RectTransform>();
        rt.anchorMin  = anchorMin;
        rt.anchorMax  = anchorMax;
        rt.offsetMin  = Vector2.zero;
        rt.offsetMax  = Vector2.zero;
        var img = go.GetComponent<Image>();
        img.color = color;
        if (sprite != null)
        {
            img.sprite = sprite;
            img.type   = Image.Type.Sliced;  // uses 9-slice so corners stay sharp
        }
        return go;
    }

    // ── Font helper ───────────────────────────────────────────────────────────
    private Font GetFont()
    {
        if (uiFont != null) return uiFont;
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    private static Button MakeButton(string name, Transform parent,
                                     Vector2 anchorMin, Vector2 anchorMax,
                                     string label, Color bgColor, Color textColor, Font font,
                                     Sprite sprite = null)
    {
        var go  = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt  = go.GetComponent<RectTransform>();
        rt.anchorMin  = anchorMin;
        rt.anchorMax  = anchorMax;
        rt.offsetMin  = Vector2.zero;
        rt.offsetMax  = Vector2.zero;
        var img = go.GetComponent<Image>();
        img.color  = bgColor;            // tints the sprite
        if (sprite != null)
        {
            img.sprite = sprite;
            img.type   = Image.Type.Sliced;  // uses 9-slice so corners stay sharp
        }

        var btn = go.GetComponent<Button>();
        btn.transition = UnityEngine.UI.Selectable.Transition.ColorTint;

        // Label
        var textGO  = new GameObject("Label", typeof(RectTransform), typeof(Text));
        textGO.transform.SetParent(go.transform, false);
        var textRT  = textGO.GetComponent<RectTransform>();
        textRT.anchorMin  = Vector2.zero;
        textRT.anchorMax  = Vector2.one;
        textRT.offsetMin  = Vector2.zero;
        textRT.offsetMax  = Vector2.zero;
        var text          = textGO.GetComponent<Text>();
        text.text         = label;
        text.alignment    = TextAnchor.MiddleCenter;
        text.color        = textColor;
        text.fontSize     = 50;
        text.fontStyle    = FontStyle.Normal;
        text.font         = font;

        return btn;
    }
}