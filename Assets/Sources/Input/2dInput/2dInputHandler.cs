using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// ─────────────────────────────────────────────────────────────────────────────
//  Chess2DInputHandler
//
//  RESPONSIBILITY: 2D-specific input only.
//  - Owns selection state and legal move list.
//  - Translates UI button clicks into GameStateManager.TryApplyMove() calls.
//  - Tells Chess2DRenderer what to highlight — but never draws pieces itself.
//
//  Attach to the same GameObject as Chess2DRenderer, or any GameObject in
//  the scene. Drag both Chess2DRenderer and the renderer's boardContainer
//  into the Inspector fields.
//
//  Note: 3D AR input will be handled by a completely separate
//  Chess3DInputHandler using raycasting — this class is 2D only.
// ─────────────────────────────────────────────────────────────────────────────
public class Chess2DInputHandler : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("Dependencies")]
    public Chess2DRenderer renderer2D;

    [Header("Highlight Colors")]
    public Color selectedColor  = new Color(0.20f, 0.85f, 0.20f, 0.60f);
    public Color legalMoveColor = new Color(0.20f, 0.60f, 1.00f, 0.50f);
    public Color lastMoveColor  = new Color(1.00f, 0.85f, 0.00f, 0.40f);
    public Color checkColor     = new Color(1.00f, 0.10f, 0.10f, 0.55f);

    // ── Selection state ───────────────────────────────────────────────────────
    private Vector2Int       _selected   = new Vector2Int(-1, -1);
    private List<Vector2Int> _legalMoves = new List<Vector2Int>();

    // Set to true by default so input works before ModeManager is built (KAN-33).
    // ModeManager will explicitly call Activate()/Deactivate() to control this.
    private bool _isActive = true;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Start()
    {
        RegisterButtonCallbacks();
        SubscribeToEvents();
    }

    void OnDestroy() => UnsubscribeFromEvents();

    // ── Activation (called by ModeManager) ───────────────────────────────────
    public void Activate()
    {
        _isActive = true;
        ClearSelection();
    }

    public void Deactivate()
    {
        _isActive = false;
        ClearSelection();
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

    private void HandleMoveMade(MoveRecord move)
    {
        // Renderer has already redrawn pieces.
        // Now apply post-move highlights.
        renderer2D.ClearAllHighlights();
        renderer2D.SetHighlight(move.From, lastMoveColor);
        renderer2D.SetHighlight(move.To,   lastMoveColor);

        var gsm = GameStateManager.Instance;
        if (gsm.IsInCheck(gsm.IsWhiteTurn))
            HighlightKingInCheck(gsm.IsWhiteTurn);
    }

    private void HandleBoardReset()
    {
        ClearSelection();
        // Renderer handles clearing highlights on reset.
    }

    // ── Button registration ───────────────────────────────────────────────────
    // Attaches a Button component to each square Image so clicks are captured.
    private void RegisterButtonCallbacks()
    {
        if (renderer2D == null)
        {
            Debug.LogError("[Chess2DInputHandler] renderer2D is not assigned.");
            return;
        }

        for (int r = 0; r < 8; r++)
        for (int c = 0; c < 8; c++)
        {
            Image hitArea = renderer2D.GetHitArea(r, c);
            if (hitArea == null) continue;

            // Reuse existing Button if one was already added, otherwise add one.
            var btn = hitArea.GetComponent<Button>();
            if (btn == null) btn = hitArea.gameObject.AddComponent<Button>();

            // Disable color tint transition — the hit area is intentionally transparent
            // and ColorTint (the default) can interfere with invisible Images.
            btn.transition = UnityEngine.UI.Selectable.Transition.None;

            // Clear any existing listeners to avoid duplicates on re-registration.
            btn.onClick.RemoveAllListeners();

            int row = r, col = c; // capture for closure
            btn.onClick.AddListener(() => OnSquareClicked(row, col));
        }
    }

    // ── Core click logic ──────────────────────────────────────────────────────
    private void OnSquareClicked(int row, int col)
    {
        if (!_isActive) return;

        var gsm     = GameStateManager.Instance;
        var clicked = new Vector2Int(row, col);

        // ── Case 1: Nothing selected — try to select a piece ─────────────────
        if (_selected.x == -1)
        {
            TrySelect(clicked, gsm);
            return;
        }

        // ── Case 2: Clicked the already-selected square — deselect ───────────
        if (clicked == _selected)
        {
            ClearSelection();
            renderer2D.ClearAllHighlights();
            return;
        }

        // ── Case 3: Clicked another friendly piece — switch selection ─────────
        Piece targetPiece = gsm.Board[row, col];
        bool  isOwnPiece  = targetPiece != Piece.None
                            && (IsWhitePiece(targetPiece) == gsm.IsWhiteTurn);
        if (isOwnPiece)
        {
            TrySelect(clicked, gsm);
            return;
        }

        // ── Case 4: Attempt the move ──────────────────────────────────────────
        // Pawn promotion defaults to Queen for now.
        // TODO: show promotion picker UI (KAN-42) and pass chosen piece.
        Piece promotionChoice = gsm.IsWhiteTurn ? Piece.WhiteQueen : Piece.BlackQueen;
        bool moved = gsm.TryApplyMove(_selected, clicked, promotionChoice);

        if (!moved)
        {
            // Illegal destination — deselect and clear
            ClearSelection();
            renderer2D.ClearAllHighlights();
        }
        // If moved, HandleMoveMade fires via GameEvents and handles highlights.
    }

    // ── Selection helpers ─────────────────────────────────────────────────────
    private void TrySelect(Vector2Int sq, GameStateManager gsm)
    {
        Piece p = gsm.Board[sq.x, sq.y];
        if (p == Piece.None) return;
        if (IsWhitePiece(p) != gsm.IsWhiteTurn) return; // not the player's piece

        _selected   = sq;
        _legalMoves = gsm.GetLegalMoves(sq);

        renderer2D.ClearAllHighlights();
        renderer2D.SetHighlight(sq, selectedColor);
        foreach (var move in _legalMoves)
            renderer2D.SetHighlight(move, legalMoveColor);
    }

    private void ClearSelection()
    {
        _selected = new Vector2Int(-1, -1);
        _legalMoves.Clear();
    }

    // ── Check highlight ───────────────────────────────────────────────────────
    private void HighlightKingInCheck(bool whiteKing)
    {
        Piece king  = whiteKing ? Piece.WhiteKing : Piece.BlackKing;
        Piece[,] board = GameStateManager.Instance.Board;

        for (int r = 0; r < 8; r++)
        for (int c = 0; c < 8; c++)
            if (board[r, c] == king)
                renderer2D.SetHighlight(r, c, checkColor);
    }

    // ── Utility ───────────────────────────────────────────────────────────────
    private static bool IsWhitePiece(Piece p) => (int)p > 0;
}