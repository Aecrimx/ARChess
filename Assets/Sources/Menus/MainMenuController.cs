using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

// ─────────────────────────────────────────────────────────────────────────────
//  MainMenuController
//
//  RESPONSIBILITY: Main menu UI and navigation.
//  Builds the menu entirely in code — no prefabs needed.
//
//  SCENE SETUP:
//  1. Create a new scene called MainMenu (build index 0).
//  2. Add a Canvas (Screen Space - Overlay, Scale With Screen Size).
//  3. Attach this script to the Canvas.
//  4. Assign fonts, sprites and colors in the Inspector.
//
//  GAME MODES passed to the game scene via PlayerPrefs:
//    "GameMode" = "vsAI"       → single player vs AI
//    "GameMode" = "local2P"    → local two player
// ─────────────────────────────────────────────────────────────────────────────
public class MainMenuController : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("Visuals")]
    public Font   uiFont;
    public Sprite buttonSprite;
    public Sprite logoSprite;           // optional — your game logo/title image
    public Color  backgroundColor   = new Color(0.08f, 0.08f, 0.12f, 1f);
    public Color  buttonColor       = new Color(0.20f, 0.20f, 0.30f, 1f);
    public Color  buttonHoverColor  = new Color(0.30f, 0.30f, 0.45f, 1f);
    public Color  titleColor        = Color.white;
    public Color  buttonTextColor   = Color.white;

    [Header("Scene Names")]
    public string gameSceneName = "SampleScene";  // rename if you rename your game scene

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Start()
    {
        SetBackground();
        BuildUI();
    }

    // ── Background ────────────────────────────────────────────────────────────
    private void SetBackground()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        cam.clearFlags  = CameraClearFlags.SolidColor;
        cam.backgroundColor = backgroundColor;
    }

    // ── UI construction ───────────────────────────────────────────────────────
    private void BuildUI()
    {
        Canvas canvas = GetComponent<Canvas>();
        if (canvas == null) { Debug.LogError("[MainMenuController] No Canvas found."); return; }

        // ── Logo or title text ────────────────────────────────────────────────
        if (logoSprite != null)
        {
            var logoGO  = new GameObject("Logo", typeof(RectTransform), typeof(Image));
            logoGO.transform.SetParent(canvas.transform, false);
            var logoRT  = logoGO.GetComponent<RectTransform>();
            logoRT.anchorMin        = new Vector2(0.1f, 0.65f);
            logoRT.anchorMax        = new Vector2(0.9f, 0.90f);
            logoRT.offsetMin        = Vector2.zero;
            logoRT.offsetMax        = Vector2.zero;
            var logoImg             = logoGO.GetComponent<Image>();
            logoImg.sprite          = logoSprite;
            logoImg.preserveAspect  = true;
            logoImg.raycastTarget   = false;
        }
        else
        {
            // Fallback: text title
            MakeText("Title", canvas.transform,
                     new Vector2(0.05f, 0.68f), new Vector2(0.95f, 0.88f),
                     "AR Chess", 90, FontStyle.Bold, titleColor, TextAnchor.MiddleCenter);
        }

        // ── Subtitle ──────────────────────────────────────────────────────────
        MakeText("Subtitle", canvas.transform,
                 new Vector2(0.1f, 0.60f), new Vector2(0.9f, 0.68f),
                 "Choose a mode to play", 36, FontStyle.Normal,
                 new Color(0.7f, 0.7f, 0.7f, 1f), TextAnchor.MiddleCenter);

        // ── Buttons ───────────────────────────────────────────────────────────
        var vsAI = MakeButton("BtnVsAI", canvas.transform,
                              new Vector2(0.1f, 0.46f), new Vector2(0.9f, 0.58f),
                              "Play vs AI");
        vsAI.onClick.AddListener(OnVsAIClicked);

        var local = MakeButton("BtnLocal", canvas.transform,
                               new Vector2(0.1f, 0.32f), new Vector2(0.9f, 0.44f),
                               "Local 2 Player");
        local.onClick.AddListener(OnLocal2PClicked);

        var settings = MakeButton("BtnSettings", canvas.transform,
                                  new Vector2(0.1f, 0.18f), new Vector2(0.9f, 0.30f),
                                  "Settings");
        settings.onClick.AddListener(OnSettingsClicked);

        // ── Version label ─────────────────────────────────────────────────────
        MakeText("Version", canvas.transform,
                 new Vector2(0.0f, 0.0f), new Vector2(1.0f, 0.06f),
                 $"v0.1", 24, FontStyle.Normal,
                 new Color(0.4f, 0.4f, 0.4f, 1f), TextAnchor.MiddleCenter);
    }

    // ── Button callbacks ──────────────────────────────────────────────────────
    private void OnVsAIClicked()
    {
        PlayerPrefs.SetString("GameMode", "vsAI");
        PlayerPrefs.Save();
        SceneManager.LoadScene(gameSceneName);
    }

    private void OnLocal2PClicked()
    {
        PlayerPrefs.SetString("GameMode", "local2P");
        PlayerPrefs.Save();
        SceneManager.LoadScene(gameSceneName);
    }

    private void OnSettingsClicked()
    {
        // TODO: build settings screen (KAN-41)
        Debug.Log("[MainMenuController] Settings not yet implemented.");
    }

    // ── UI helpers ────────────────────────────────────────────────────────────
    private Button MakeButton(string name, Transform parent,
                              Vector2 anchorMin, Vector2 anchorMax, string label)
    {
        var go  = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt  = go.GetComponent<RectTransform>();
        rt.anchorMin  = anchorMin;
        rt.anchorMax  = anchorMax;
        rt.offsetMin  = Vector2.zero;
        rt.offsetMax  = Vector2.zero;

        var img = go.GetComponent<Image>();
        if (buttonSprite != null) { img.sprite = buttonSprite; img.type = Image.Type.Sliced; }
        img.color = buttonColor;

        var btn = go.GetComponent<Button>();
        btn.transition = UnityEngine.UI.Selectable.Transition.ColorTint;
        var colors          = btn.colors;
        colors.highlightedColor = buttonHoverColor;
        colors.pressedColor     = new Color(buttonColor.r * 0.7f,
                                            buttonColor.g * 0.7f,
                                            buttonColor.b * 0.7f, 1f);
        btn.colors = colors;

        MakeText($"{name}Label", go.transform,
                 Vector2.zero, Vector2.one,
                 label, 48, FontStyle.Bold, buttonTextColor, TextAnchor.MiddleCenter);

        return btn;
    }

    private void MakeText(string name, Transform parent,
                          Vector2 anchorMin, Vector2 anchorMax,
                          string content, int fontSize, FontStyle style,
                          Color color, TextAnchor alignment)
    {
        var go  = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var rt  = go.GetComponent<RectTransform>();
        rt.anchorMin  = anchorMin;
        rt.anchorMax  = anchorMax;
        rt.offsetMin  = Vector2.zero;
        rt.offsetMax  = Vector2.zero;
        var txt         = go.GetComponent<Text>();
        txt.text        = content;
        txt.fontSize    = fontSize;
        txt.fontStyle   = style;
        txt.color       = color;
        txt.alignment   = alignment;
        txt.font        = GetFont();
        txt.raycastTarget = false;
    }

    private Font GetFont()
    {
        if (uiFont != null) return uiFont;
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }
}