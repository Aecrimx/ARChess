using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

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
    public Chess2DRenderer     renderer2D;
    public PawnPromotionPicker promotionPicker;

    [Header("Highlight Colors")]
    public Color selectedColor  = new Color(0.20f, 0.85f, 0.20f, 0.60f);
    public Color legalMoveColor = new Color(0.20f, 0.60f, 1.00f, 0.50f);
    public Color lastMoveColor  = new Color(1.00f, 0.85f, 0.00f, 0.40f);
    public Color checkColor     = new Color(1.00f, 0.10f, 0.10f, 0.55f);

    // ── Selection state ───────────────────────────────────────────────────────
    private Vector2Int       _selected   = new Vector2Int(-1, -1);
    private List<Vector2Int> _legalMoves = new List<Vector2Int>();

    // Pending promotion — stored while picker is open
    private Vector2Int _promotionFrom = new Vector2Int(-1, -1);
    private Vector2Int _promotionTo   = new Vector2Int(-1, -1);

    // Drag and Drop state
    private Vector2Int _hoveredSquare = new Vector2Int(-1, -1);
    private bool       _isDragging    = false;

    // Set to true by default so input works before ModeManager is built (KAN-33).
    // ModeManager will explicitly call Activate()/Deactivate() to control this.
    private bool _isActive = true;

    // ── LAN ───────────────────────────────────────────────────────────────────
    // Set by ChessNetworkProxy.RpcGameStarted. True = local player controls white.
    // In local modes this is always true (both players share the same device).
    public bool LocalPlayerIsWhite { get; set; } = true;

    // Cached reference to this client's ChessNetworkProxy (set on Start in LAN mode).
    private ChessNetworkProxy _localProxy;

    // ── Lifecycle ─────────────────────────────────────────────────────────────
    void Start()
    {
        RegisterButtonCallbacks();
        SubscribeToEvents();

        // Cache the local player's proxy when in LAN mode
        if (GameModeManager.Instance != null && GameModeManager.Instance.IsLan)
        {
            // The local player's proxy has isLocalPlayer == true
            foreach (var proxy in FindObjectsByType<ChessNetworkProxy>(FindObjectsSortMode.None))
            {
                if (proxy.isLocalPlayer) { _localProxy = proxy; break; }
            }
        }
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
    // Attaches custom event triggers to each square Image to support clicking and dragging.
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

            // Remove standard buttons if any
            var btn = hitArea.GetComponent<Button>();
            if (btn != null) Destroy(btn);

            var trigger = hitArea.GetComponent<SquareInputTrigger>();
            if (trigger == null) trigger = hitArea.gameObject.AddComponent<SquareInputTrigger>();

            trigger.row = r;
            trigger.col = c;
            trigger.handler = this;
        }
    }

    // ── Custom Input Events ───────────────────────────────────────────────────
    public void OnSquarePointerDown(int row, int col)
    {
        if (!_isActive) return;
        _isDragging = true;
        _hoveredSquare = new Vector2Int(row, col);

        // Process this as a standard selection/click
        OnSquareClicked(row, col);
    }

    public void OnSquarePointerEnter(int row, int col)
    {
        if (!_isActive) return;
        _hoveredSquare = new Vector2Int(row, col);
    }

    public void OnSquarePointerUp(int row, int col)
    {
        if (!_isActive) return;
        
        _isDragging = false;
        
        // If we dropped on a square we didn't start on, try to move there.
        // It uses the latest hovered square.
        if (_hoveredSquare != _selected && _hoveredSquare.x != -1)
        {
            OnSquareClicked(_hoveredSquare.x, _hoveredSquare.y);
        }
    }

    // ── Core click logic ──────────────────────────────────────────────────────
    private void OnSquareClicked(int row, int col)
    {
        if (!_isActive) return;

        var gsm     = GameStateManager.Instance;
        var clicked = new Vector2Int(row, col);

        // ── LAN: only act on the local player's turn ──────────────────────────
        if (gsm.IsNetworked)
        {
            bool myTurn = (LocalPlayerIsWhite && gsm.IsWhiteTurn)
                       || (!LocalPlayerIsWhite && !gsm.IsWhiteTurn);
            if (!myTurn) return;
        }

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
        if (IsPromotion(gsm, _selected, clicked))
        {
            _promotionFrom = _selected;
            _promotionTo   = clicked;
            ClearSelection();
            renderer2D.ClearAllHighlights();

            bool isWhite = gsm.IsWhiteTurn;
            promotionPicker.Show(isWhite, OnPromotionChosen);
        }
        else
        {
            ExecuteMove(_selected, clicked, Piece.WhiteQueen, gsm);
        }
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

    // ── Promotion handling ────────────────────────────────────────────────────
    private void OnPromotionChosen(Piece chosen)
    {
        if (_promotionFrom.x == -1) return;

        ExecuteMove(_promotionFrom, _promotionTo, chosen, GameStateManager.Instance);

        _promotionFrom = new Vector2Int(-1, -1);
        _promotionTo   = new Vector2Int(-1, -1);
    }

    private static bool IsPromotion(GameStateManager gsm, Vector2Int from, Vector2Int to)
    {
        Piece p = gsm.Board[from.x, from.y];
        return (p == Piece.WhitePawn && to.x == 7)
            || (p == Piece.BlackPawn && to.x == 0);
    }

    // ── Move execution (local or via network) ─────────────────────────────────
    private void ExecuteMove(Vector2Int from, Vector2Int to, Piece promotion,
                             GameStateManager gsm)
    {
        if (gsm.IsNetworked && _localProxy != null)
        {
            // LAN: send command to server — server validates and relays
            _localProxy.CmdRequestMove(
                from.x, from.y, to.x, to.y, (int)promotion);
            ClearSelection();
            renderer2D.ClearAllHighlights();
        }
        else
        {
            // Local: apply directly
            bool moved = gsm.TryApplyMove(from, to, promotion);
            if (!moved)
            {
                ClearSelection();
                renderer2D.ClearAllHighlights();
            }
        }
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

// ─────────────────────────────────────────────────────────────────────────────
//  SquareInputTrigger
//  Captures Drag and Drop pointer events on squares and forwards to Handler.
// ─────────────────────────────────────────────────────────────────────────────
public class SquareInputTrigger : MonoBehaviour, IPointerDownHandler, IPointerEnterHandler, IPointerUpHandler
{
    [HideInInspector] public int row;
    [HideInInspector] public int col;
    [HideInInspector] public Chess2DInputHandler handler;

    public void OnPointerDown(PointerEventData eventData) => handler.OnSquarePointerDown(row, col);
    public void OnPointerEnter(PointerEventData eventData) => handler.OnSquarePointerEnter(row, col);
    public void OnPointerUp(PointerEventData eventData) => handler.OnSquarePointerUp(row, col);
}