using UnityEngine;
using UnityEngine.UI;

// ─────────────────────────────────────────────────────────────────────────────
//  Chess2DRenderer
//
//  RESPONSIBILITY: Drawing only. No input or selection logic.
//
//  SCENE SETUP:
//  1. Canvas (Screen Space - Overlay, Scale With Screen Size)
//     └── Board (RectTransform — square, centred, e.g. 640x640)
//           └── BoardImage  ← assign your board PNG here in Inspector
//  2. Attach this script to the Canvas (or Board) GameObject.
//  3. Assign fields in Inspector:
//       boardContainer  → the Board RectTransform
//       boardImage      → the Image component showing the board PNG
//       pieceSprites    → 12 sprites (see order below)
//
//  HOW THE LAYERS WORK:
//  boardImage        — your PNG, sits at the bottom, visible
//  _hitAreas[r,c]    — invisible transparent Images, raycastTarget=true
//                      so Chess2DInputHandler can receive clicks
//  _highlights[r,c]  — coloured overlays driven by Chess2DInputHandler
//  _pieces[r,c]      — piece sprites on top
//
//  The hit areas and highlights are sized and positioned to line up with
//  the squares on your PNG. If your board has a border, set
//  boardBorderFraction in the Inspector to the fraction of the board size
//  taken up by the border on each side (e.g. 0.05 for a 5% border).
// ─────────────────────────────────────────────────────────────────────────────
public class Chess2DRenderer : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("Board")]
    public RectTransform boardContainer;

    [Tooltip("The Image component that displays the board PNG.")]
    public Image boardImage;

    [Tooltip("Fraction of the board size used by the border on each side. " +
             "0 = no border. 0.05 = 5% border on each edge.")]
    [Range(0f, 0.2f)]
    public float boardBorderFraction = 0f;

    [Header("Piece Sprites")]
    // Order: WPawn WKnight WBishop WRook WQueen WKing
    //        BPawn BKnight BBishop BRook BQueen BKing
    public Sprite[] pieceSprites = new Sprite[12];

    // ── Internal layers ───────────────────────────────────────────────────────
    private Image[,] _hitAreas   = new Image[8, 8];
    private Image[,] _highlights = new Image[8, 8];
    private Image[,] _pieces     = new Image[8, 8];

    // Computed from boardContainer size and borderFraction
    private float _cellSize;
    private float _boardOffset; // pixel offset from board edge to first square

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Start()
    {
        ComputeLayout();
        BuildOverlayGrid();
        SubscribeToEvents();
        RedrawPieces();
    }

    void OnDestroy() => UnsubscribeFromEvents();

    // ── Activation (called by ModeManager) ───────────────────────────────────
    public void Activate()
    {
        boardContainer.gameObject.SetActive(true);
        ClearAllHighlights();
        RedrawPieces();
    }

    public void Deactivate()
    {
        boardContainer.gameObject.SetActive(false);
    }

    // ── Layout calculation ────────────────────────────────────────────────────
    private void ComputeLayout()
    {
        float boardSize  = boardContainer.rect.width;
        _boardOffset     = boardSize * boardBorderFraction;
        float playArea   = boardSize - _boardOffset * 2f;
        _cellSize        = playArea / 8f;
    }

    // ── Overlay grid construction (runs once) ─────────────────────────────────
    // Three layers sit on top of boardImage, aligned to the board squares.
    private void BuildOverlayGrid()
    {
        for (int r = 0; r < 8; r++)
        for (int c = 0; c < 8; c++)
        {
            // Layer 1: invisible hit area — clickable but not visible
            _hitAreas[r, c] = MakeImage($"Hit_{r}{c}", boardContainer);
            PlaceCell(_hitAreas[r, c].rectTransform, r, c);
            _hitAreas[r, c].color         = Color.clear;
            _hitAreas[r, c].raycastTarget = true;

            // Layer 2: highlight overlay — coloured by input handler
            _highlights[r, c] = MakeImage($"Hi_{r}{c}", boardContainer);
            PlaceCell(_highlights[r, c].rectTransform, r, c);
            _highlights[r, c].color         = Color.clear;
            _highlights[r, c].raycastTarget = false;

            // Layer 3: piece sprite
            _pieces[r, c] = MakeImage($"Pc_{r}{c}", boardContainer);
            PlaceCell(_pieces[r, c].rectTransform, r, c);
            _pieces[r, c].color         = Color.clear;
            _pieces[r, c].raycastTarget = false;
        }
    }

    // ── Event wiring ──────────────────────────────────────────────────────────
    private void SubscribeToEvents()
    {
        GameEvents.OnMoveMade   += HandleMoveMade;
        GameEvents.OnBoardReset += HandleBoardReset;
    }

    private void UnsubscribeFromEvents()
    {
        GameEvents.OnMoveMade   -= HandleMoveMade;
        GameEvents.OnBoardReset -= HandleBoardReset;
    }

    private void HandleMoveMade(MoveRecord move) => RedrawPieces();

    private void HandleBoardReset()
    {
        ClearAllHighlights();
        RedrawPieces();
    }

    // ── Piece drawing ─────────────────────────────────────────────────────────
    public void RedrawPieces()
    {
        if (GameStateManager.Instance == null) return;
        Piece[,] board = GameStateManager.Instance.Board;

        for (int r = 0; r < 8; r++)
        for (int c = 0; c < 8; c++)
        {
            Piece p = board[r, c];
            if (p == Piece.None)
            {
                _pieces[r, c].sprite = null;
                _pieces[r, c].color  = Color.clear;
            }
            else
            {
                _pieces[r, c].sprite = GetSprite(p);
                _pieces[r, c].color  = Color.white;
            }
        }
    }

    // ── Public highlight API (called by Chess2DInputHandler) ──────────────────
    public void ClearAllHighlights()
    {
        for (int r = 0; r < 8; r++)
        for (int c = 0; c < 8; c++)
            _highlights[r, c].color = Color.clear;
    }

    public void SetHighlight(Vector2Int sq, Color color) =>
        SetHighlight(sq.x, sq.y, color);

    public void SetHighlight(int row, int col, Color color)
    {
        if (row < 0 || row > 7 || col < 0 || col > 7) return;
        _highlights[row, col].color = color;
    }

    /// <summary>Exposes hit area Images so Chess2DInputHandler can add Button components.</summary>
    public Image GetHitArea(int row, int col) => _hitAreas[row, col];

    // ── Cell placement ────────────────────────────────────────────────────────
    private void PlaceCell(RectTransform rt, int row, int col)
    {
        rt.anchorMin        = Vector2.zero;
        rt.anchorMax        = Vector2.zero;
        rt.pivot            = Vector2.zero;
        rt.sizeDelta        = new Vector2(_cellSize, _cellSize);
        rt.anchoredPosition = new Vector2(
            _boardOffset + col * _cellSize,
            _boardOffset + row * _cellSize
        );
    }

    // ── Utility ───────────────────────────────────────────────────────────────
    private static Image MakeImage(string name, RectTransform parent)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        return go.GetComponent<Image>();
    }

    // ── Sprite lookup ─────────────────────────────────────────────────────────
    private Sprite GetSprite(Piece p)
    {
        int i = p switch
        {
            Piece.WhitePawn   => 0,  Piece.WhiteKnight => 1,
            Piece.WhiteBishop => 2,  Piece.WhiteRook   => 3,
            Piece.WhiteQueen  => 4,  Piece.WhiteKing   => 5,
            Piece.BlackPawn   => 6,  Piece.BlackKnight => 7,
            Piece.BlackBishop => 8,  Piece.BlackRook   => 9,
            Piece.BlackQueen  => 10, Piece.BlackKing   => 11,
            _                 => -1
        };
        return (i >= 0 && i < pieceSprites.Length) ? pieceSprites[i] : null;
    }
}