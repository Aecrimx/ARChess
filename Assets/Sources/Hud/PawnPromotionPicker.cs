using System;
using UnityEngine;
using UnityEngine.UI;

// ─────────────────────────────────────────────────────────────────────────────
//  PawnPromotionPicker
//
//  RESPONSIBILITY: Show a piece-choice UI when a pawn reaches the back rank.
//  Calls back to Chess2DInputHandler with the chosen piece so it can complete
//  the move. GameStateManager is never called here — that stays in the
//  input handler.
//
//  SCENE SETUP:
//  Attach to the same GameObject as Chess2DInputHandler (2dCanvas).
//  Assign the four piece sprites in the Inspector.
//  Optionally assign a background sprite and font.
// ─────────────────────────────────────────────────────────────────────────────
public class PawnPromotionPicker : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("Piece Sprites (same set as Chess2DRenderer)")]
    public Sprite whiteQueenSprite;
    public Sprite whiteRookSprite;
    public Sprite whiteBishopSprite;
    public Sprite whiteKnightSprite;
    public Sprite blackQueenSprite;
    public Sprite blackRookSprite;
    public Sprite blackBishopSprite;
    public Sprite blackKnightSprite;

    [Header("Visuals")]
    public Font   uiFont;
    public Color  textColor        = Color.white;
    public Sprite buttonSprite;    // optional 9-sliced background for each choice
    public Color  buttonColor      = new Color(0.15f, 0.15f, 0.15f, 1f);
    public Color  overlayColor     = new Color(0f, 0f, 0f, 0.6f);

    // ── State ─────────────────────────────────────────────────────────────────
    private GameObject     _overlay;
    private Action<Piece>  _onChosen;   // callback to Chess2DInputHandler

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Start()
    {
        BuildUI();
    }

    // ── Public API ────────────────────────────────────────────────────────────
    /// <summary>
    /// Show the promotion picker for the given side.
    /// onChosen is called with the selected Piece once the player taps a choice.
    /// </summary>
    public void Show(bool isWhite, Action<Piece> onChosen)
    {
        _onChosen = onChosen;
        SetupButtons(isWhite);
        _overlay.SetActive(true);
    }

    public void Hide()
    {
        _overlay.SetActive(false);
        _onChosen = null;
    }

    // ── Button callbacks ──────────────────────────────────────────────────────
    private void OnPieceChosen(Piece piece)
    {
        var callback = _onChosen; // save before Hide() nulls it
        Hide();
        callback?.Invoke(piece);
    }

    // ── UI construction ───────────────────────────────────────────────────────
    // Four choices laid out in a horizontal row in the centre of the screen.
    private Image[] _choiceImages = new Image[4];
    private Button[] _choiceButtons = new Button[4];

    private void BuildUI()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) canvas = FindAnyObjectByType<Canvas>();

        // Full-screen dimmer — also blocks clicks on the board
        var overlayGO = new GameObject("PromotionOverlay", typeof(RectTransform), typeof(Image));
        overlayGO.transform.SetParent(canvas.transform, false);
        var overlayRT    = overlayGO.GetComponent<RectTransform>();
        overlayRT.anchorMin = Vector2.zero;
        overlayRT.anchorMax = Vector2.one;
        overlayRT.offsetMin = Vector2.zero;
        overlayRT.offsetMax = Vector2.zero;
        overlayGO.GetComponent<Image>().color = overlayColor;
        overlayGO.GetComponent<Image>().raycastTarget = true;
        _overlay = overlayGO;

        // Label
        var labelGO = new GameObject("Label", typeof(RectTransform), typeof(Text));
        labelGO.transform.SetParent(_overlay.transform, false);
        var labelRT      = labelGO.GetComponent<RectTransform>();
        labelRT.anchorMin = new Vector2(0.1f, 0.55f);
        labelRT.anchorMax = new Vector2(0.9f, 0.70f);
        labelRT.offsetMin = Vector2.zero;
        labelRT.offsetMax = Vector2.zero;
        var labelText        = labelGO.GetComponent<Text>();
        labelText.text       = "Choose promotion piece";
        labelText.alignment  = TextAnchor.MiddleCenter;
        labelText.color      = textColor;
        labelText.fontSize   = 75;
        labelText.fontStyle  = FontStyle.Bold;
        labelText.font       = GetFont();

        // Four piece buttons in a row
        float[] colMins = { 0.02f, 0.265f, 0.51f, 0.755f };
        float[] colMaxs = { 0.245f, 0.49f, 0.735f, 0.98f };

        for (int i = 0; i < 4; i++)
        {
            var btnGO = new GameObject($"Choice_{i}",
                            typeof(RectTransform), typeof(Image), typeof(Button));
            btnGO.transform.SetParent(_overlay.transform, false);
            var btnRT      = btnGO.GetComponent<RectTransform>();
            btnRT.anchorMin = new Vector2(colMins[i], 0.4375f);
            btnRT.anchorMax = new Vector2(colMaxs[i], 0.5625f);
            btnRT.offsetMin = Vector2.zero;
            btnRT.offsetMax = Vector2.zero;

            var img = btnGO.GetComponent<Image>();
            if (buttonSprite != null) { img.sprite = buttonSprite; img.type = Image.Type.Sliced; }
            img.color = buttonColor;

            var btn = btnGO.GetComponent<Button>();
            btn.transition = UnityEngine.UI.Selectable.Transition.ColorTint;

            // Piece image inside the button
            var pieceGO  = new GameObject("PieceIcon", typeof(RectTransform), typeof(Image));
            pieceGO.transform.SetParent(btnGO.transform, false);
            var pieceRT  = pieceGO.GetComponent<RectTransform>();
            pieceRT.anchorMin = new Vector2(0.1f, 0.1f);
            pieceRT.anchorMax = new Vector2(0.9f, 0.9f);
            pieceRT.offsetMin = Vector2.zero;
            pieceRT.offsetMax = Vector2.zero;
            pieceGO.GetComponent<Image>().raycastTarget = false;

            _choiceImages[i]  = pieceGO.GetComponent<Image>();
            _choiceButtons[i] = btn;
        }

        _overlay.SetActive(false);
    }

    // Called by Show() to set the correct sprites and callbacks for white/black
    private void SetupButtons(bool isWhite)
    {
        Piece[]  pieces  = isWhite
            ? new[] { Piece.WhiteQueen, Piece.WhiteRook, Piece.WhiteBishop, Piece.WhiteKnight }
            : new[] { Piece.BlackQueen, Piece.BlackRook, Piece.BlackBishop, Piece.BlackKnight };

        Sprite[] sprites = isWhite
            ? new[] { whiteQueenSprite, whiteRookSprite, whiteBishopSprite, whiteKnightSprite }
            : new[] { blackQueenSprite, blackRookSprite, blackBishopSprite, blackKnightSprite };

        for (int i = 0; i < 4; i++)
        {
            _choiceImages[i].sprite = sprites[i];
            _choiceImages[i].color  = sprites[i] != null ? Color.white : Color.clear;

            _choiceButtons[i].onClick.RemoveAllListeners();
            Piece chosen = pieces[i]; // capture for closure
            _choiceButtons[i].onClick.AddListener(() => OnPieceChosen(chosen));
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private Font GetFont()
    {
        if (uiFont != null) return uiFont;
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }
}