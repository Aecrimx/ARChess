using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class ChessMoveInteractionController : IDisposable
{
    public readonly struct HighlightPalette
    {
        public HighlightPalette(Color selected, Color legalMove, Color lastMove, Color check)
        {
            Selected = selected;
            LegalMove = legalMove;
            LastMove = lastMove;
            Check = check;
        }

        public Color Selected { get; }
        public Color LegalMove { get; }
        public Color LastMove { get; }
        public Color Check { get; }
    }

    private readonly ChessBoardRendererBase _renderer;
    private readonly PawnPromotionPicker _promotionPicker;
    private readonly HighlightPalette _palette;

    private readonly List<Vector2Int> _legalMoves = new List<Vector2Int>();
    private Vector2Int _selected = InvalidSquare;
    private Vector2Int _hoveredSquare = InvalidSquare;
    private Vector2Int _promotionFrom = InvalidSquare;
    private Vector2Int _promotionTo = InvalidSquare;

    private ChessNetworkProxy _localProxy;
    private bool _localPlayerIsWhite = true;
    private bool _subscribed;

    private static readonly Vector2Int InvalidSquare = new Vector2Int(-1, -1);

    public ChessMoveInteractionController(
        ChessBoardRendererBase renderer,
        PawnPromotionPicker promotionPicker,
        HighlightPalette palette)
    {
        _renderer = renderer;
        _promotionPicker = promotionPicker;
        _palette = palette;
        Subscribe();
    }

    public void Dispose()
    {
        Unsubscribe();
    }

    public void SetContext(bool localPlayerIsWhite, ChessNetworkProxy proxy)
    {
        _localPlayerIsWhite = localPlayerIsWhite;
        _localProxy = proxy;
    }

    public void Activate()
    {
        RefreshBoardState();
    }

    public void Deactivate()
    {
        ClearSelection();
    }

    public void OnSquarePointerDown(Vector2Int square)
    {
        _hoveredSquare = square;
        OnSquareClicked(square);
    }

    public void OnSquarePointerEnter(Vector2Int square)
    {
        _hoveredSquare = square;
    }

    public void OnSquarePointerUp(Vector2Int square)
    {
        if (_hoveredSquare != _selected && square.x >= 0)
        {
            OnSquareClicked(square);
        }
    }

    public void RefreshBoardState()
    {
        ClearSelection();
        _renderer.ClearAllHighlights();

        var gsm = GameStateManager.Instance;
        if (gsm == null)
        {
            return;
        }

        if (gsm.MoveHistory.Count > 0)
        {
            MoveRecord lastMove = gsm.MoveHistory[gsm.MoveHistory.Count - 1];
            _renderer.SetHighlight(lastMove.From, _palette.LastMove);
            _renderer.SetHighlight(lastMove.To, _palette.LastMove);
        }

        if (gsm.IsInCheck(gsm.IsWhiteTurn))
        {
            HighlightKingInCheck(gsm.IsWhiteTurn);
        }
    }

    private void Subscribe()
    {
        if (_subscribed)
        {
            return;
        }

        GameEvents.OnMoveMade += HandleMoveMade;
        GameEvents.OnBoardReset += HandleBoardReset;
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
        _subscribed = false;
    }

    private void HandleMoveMade(MoveRecord move)
    {
        ClearSelection();
        _renderer.ClearAllHighlights();
        _renderer.SetHighlight(move.From, _palette.LastMove);
        _renderer.SetHighlight(move.To, _palette.LastMove);

        var gsm = GameStateManager.Instance;
        if (gsm != null && gsm.IsInCheck(gsm.IsWhiteTurn))
        {
            HighlightKingInCheck(gsm.IsWhiteTurn);
        }
    }

    private void HandleBoardReset()
    {
        RefreshBoardState();
    }

    private void OnSquareClicked(Vector2Int clicked)
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null)
        {
            return;
        }

        if (gsm.IsNetworked)
        {
            bool myTurn = (_localPlayerIsWhite && gsm.IsWhiteTurn) || (!_localPlayerIsWhite && !gsm.IsWhiteTurn);
            if (!myTurn)
            {
                return;
            }
        }

        if (_selected.x == -1)
        {
            TrySelect(clicked, gsm);
            return;
        }

        if (clicked == _selected)
        {
            ClearSelection();
            _renderer.ClearAllHighlights();
            return;
        }

        Piece targetPiece = gsm.Board[clicked.x, clicked.y];
        bool isOwnPiece = targetPiece != Piece.None && IsWhitePiece(targetPiece) == gsm.IsWhiteTurn;
        if (isOwnPiece)
        {
            TrySelect(clicked, gsm);
            return;
        }

        if (IsPromotion(gsm, _selected, clicked))
        {
            _promotionFrom = _selected;
            _promotionTo = clicked;
            ClearSelection();
            _renderer.ClearAllHighlights();

            if (_promotionPicker != null)
            {
                _promotionPicker.Show(gsm.IsWhiteTurn, OnPromotionChosen);
            }
            else
            {
                ExecuteMove(_promotionFrom, _promotionTo, gsm.IsWhiteTurn ? Piece.WhiteQueen : Piece.BlackQueen, gsm);
            }
        }
        else
        {
            ExecuteMove(_selected, clicked, Piece.WhiteQueen, gsm);
        }
    }

    private void TrySelect(Vector2Int square, GameStateManager gsm)
    {
        Piece piece = gsm.Board[square.x, square.y];
        if (piece == Piece.None)
        {
            return;
        }

        if (IsWhitePiece(piece) != gsm.IsWhiteTurn)
        {
            return;
        }

        _selected = square;
        _legalMoves.Clear();
        _legalMoves.AddRange(gsm.GetLegalMoves(square));

        _renderer.ClearAllHighlights();
        _renderer.SetHighlight(square, _palette.Selected);
        foreach (Vector2Int move in _legalMoves)
        {
            _renderer.SetHighlight(move, _palette.LegalMove);
        }
    }

    private void ClearSelection()
    {
        _selected = InvalidSquare;
        _hoveredSquare = InvalidSquare;
        _legalMoves.Clear();
    }

    private void OnPromotionChosen(Piece chosen)
    {
        if (_promotionFrom.x == -1)
        {
            return;
        }

        ExecuteMove(_promotionFrom, _promotionTo, chosen, GameStateManager.Instance);
        _promotionFrom = InvalidSquare;
        _promotionTo = InvalidSquare;
    }

    private void ExecuteMove(Vector2Int from, Vector2Int to, Piece promotion, GameStateManager gsm)
    {
        if (gsm == null)
        {
            return;
        }

        if (gsm.IsNetworked && _localProxy != null)
        {
            bool submitted;
            bool clearHighlightsAfterSubmit = true;

            if (_localProxy.isServer)
            {
                _localProxy.CmdRequestMove(from.x, from.y, to.x, to.y, (int)promotion);
                submitted = true;
            }
            else
            {
                submitted = _localProxy.TrySubmitPredictedLocalMove(from, to, promotion);
                clearHighlightsAfterSubmit = false;
            }

            if (submitted)
            {
                ClearSelection();
                if (clearHighlightsAfterSubmit)
                {
                    _renderer.ClearAllHighlights();
                }
            }

            return;
        }

        bool moved = gsm.TryApplyMove(from, to, promotion);
        if (!moved)
        {
            ClearSelection();
            _renderer.ClearAllHighlights();
        }
    }

    private void HighlightKingInCheck(bool whiteKing)
    {
        var gsm = GameStateManager.Instance;
        if (gsm == null)
        {
            return;
        }

        Piece king = whiteKing ? Piece.WhiteKing : Piece.BlackKing;
        Piece[,] board = gsm.Board;

        for (int row = 0; row < 8; row++)
        {
            for (int col = 0; col < 8; col++)
            {
                if (board[row, col] == king)
                {
                    _renderer.SetHighlight(new Vector2Int(row, col), _palette.Check);
                }
            }
        }
    }

    private static bool IsPromotion(GameStateManager gsm, Vector2Int from, Vector2Int to)
    {
        Piece piece = gsm.Board[from.x, from.y];
        return (piece == Piece.WhitePawn && to.x == 7) || (piece == Piece.BlackPawn && to.x == 0);
    }

    private static bool IsWhitePiece(Piece piece) => (int)piece > 0;
}
