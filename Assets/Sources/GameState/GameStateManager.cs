using System;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────
//  Piece enum
//  Positive = White, Negative = Black, 0 = None
// ─────────────────────────────────────────────
public enum Piece
{
    None         =  0,
    WhitePawn    =  1,
    WhiteKnight  =  2,
    WhiteBishop  =  3,
    WhiteRook    =  4,
    WhiteQueen   =  5,
    WhiteKing    =  6,
    BlackPawn    = -1,
    BlackKnight  = -2,
    BlackBishop  = -3,
    BlackRook    = -4,
    BlackQueen   = -5,
    BlackKing    = -6
}

// ─────────────────────────────────────────────
//  Serialisable snapshot — used for mode switching
// ─────────────────────────────────────────────
[Serializable]
public class GameSnapshot
{
    public int[]        FlatBoard            = new int[64];
    public bool         IsWhiteTurn;
    public bool         WhiteCanCastleKingside;
    public bool         WhiteCanCastleQueenside;
    public bool         BlackCanCastleKingside;
    public bool         BlackCanCastleQueenside;
    public int          EnPassantCol;          // -1 = none
    public int          EnPassantRow;
    public List<int>    CapturedByWhite      = new List<int>();
    public List<int>    CapturedByBlack      = new List<int>();
    public List<string> MoveHistory          = new List<string>();
    public GameResult   Result;
}

// ─────────────────────────────────────────────
//  Move record
// ─────────────────────────────────────────────
public struct MoveRecord
{
    public Vector2Int From;
    public Vector2Int To;
    public Piece      PieceMoved;
    public Piece      PieceCaptured;
    public bool       WasCastle;
    public bool       WasEnPassant;
    public Piece      PromotionPiece;   // None if not a promotion
    public string     Notation;
}

// ─────────────────────────────────────────────
//  Game result
// ─────────────────────────────────────────────
public enum GameResult
{
    Ongoing,
    WhiteWins,
    BlackWins,
    Stalemate,
    DrawByRepetition,
    DrawByFiftyMoveRule
}

// ─────────────────────────────────────────────
//  Events the renderers can subscribe to
// ─────────────────────────────────────────────
public static class GameEvents
{
    public static event Action<MoveRecord>  OnMoveMade;
    public static event Action<GameResult>  OnGameOver;
    public static event Action              OnBoardReset;
    public static event Action<bool>        OnTurnChanged;   // true = white's turn

    public static void RaiseMoveMade(MoveRecord m)  => OnMoveMade?.Invoke(m);
    public static void RaiseGameOver(GameResult r)  => OnGameOver?.Invoke(r);
    public static void RaiseBoardReset()            => OnBoardReset?.Invoke();
    public static void RaiseTurnChanged(bool white) => OnTurnChanged?.Invoke(white);
}

// ─────────────────────────────────────────────
//  GameStateManager
//  Attach to a persistent GameObject (e.g. "GameManager").
//  This class owns ALL chess state. Renderers must never
//  modify the board directly — always call the public API.
// ─────────────────────────────────────────────
public class GameStateManager : MonoBehaviour
{
    // ── Singleton ───────────────────────────────
    public static GameStateManager Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        InitBoard();
    }

    // ── Board state ─────────────────────────────
    public Piece[,] Board { get; private set; } = new Piece[8, 8];

    // ── Turn & castling flags ───────────────────
    public bool IsWhiteTurn             { get; private set; } = true;
    public bool WhiteCanCastleKingside  { get; private set; } = true;
    public bool WhiteCanCastleQueenside { get; private set; } = true;
    public bool BlackCanCastleKingside  { get; private set; } = true;
    public bool BlackCanCastleQueenside { get; private set; } = true;

    // ── En passant ──────────────────────────────
    // Stores the square a pawn jumped over (target of an en-passant capture).
    // (-1,-1) = no en-passant available this turn.
    public Vector2Int EnPassantTarget { get; private set; } = new Vector2Int(-1, -1);

    // ── History & captured pieces ───────────────
    public List<MoveRecord> MoveHistory    { get; private set; } = new List<MoveRecord>();
    public List<Piece>      CapturedByWhite { get; private set; } = new List<Piece>();
    public List<Piece>      CapturedByBlack { get; private set; } = new List<Piece>();

    // ── Game result ─────────────────────────────
    public GameResult Result { get; private set; } = GameResult.Ongoing;

    // ── 50-move rule counter ─────────────────────
    private int _halfMoveClock = 0;

    // ── Repetition tracking ─────────────────────
    private Dictionary<string, int> _positionCounts = new Dictionary<string, int>();

    // ════════════════════════════════════════════
    //  PUBLIC API
    // ════════════════════════════════════════════

    /// <summary>Reset to the standard starting position.</summary>
    public void InitBoard()
    {
        Board = new Piece[8, 8];

        Piece[] backRank =
        {
            Piece.WhiteRook, Piece.WhiteKnight, Piece.WhiteBishop, Piece.WhiteQueen,
            Piece.WhiteKing, Piece.WhiteBishop, Piece.WhiteKnight, Piece.WhiteRook
        };

        for (int col = 0; col < 8; col++)
        {
            Board[0, col] = backRank[col];
            Board[1, col] = Piece.WhitePawn;
            Board[6, col] = Piece.BlackPawn;
            Board[7, col] = (Piece)(-(int)backRank[col]);
        }

        IsWhiteTurn             = true;
        WhiteCanCastleKingside  = true;
        WhiteCanCastleQueenside = true;
        BlackCanCastleKingside  = true;
        BlackCanCastleQueenside = true;
        EnPassantTarget         = new Vector2Int(-1, -1);
        Result                  = GameResult.Ongoing;
        _halfMoveClock          = 0;

        MoveHistory.Clear();
        CapturedByWhite.Clear();
        CapturedByBlack.Clear();
        _positionCounts.Clear();
        RecordPosition();

        GameEvents.RaiseBoardReset();
    }

    /// <summary>
    /// Returns true if the move is legal and applies it,
    /// updating all state and firing GameEvents.
    /// promotionChoice is only needed when a pawn reaches the back rank.
    /// </summary>
    public bool TryApplyMove(Vector2Int from, Vector2Int to,
                             Piece promotionChoice = Piece.WhiteQueen)
    {
        if (Result != GameResult.Ongoing) return false;

        var legalMoves = GetLegalMoves(from);
        if (!legalMoves.Contains(to)) return false;

        ApplyMoveInternal(from, to, promotionChoice);
        return true;
    }

    /// <summary>All squares the piece on <paramref name="from"/> can legally move to.</summary>
    public List<Vector2Int> GetLegalMoves(Vector2Int from)
    {
        var moves = new List<Vector2Int>();
        Piece piece = Board[from.x, from.y];

        if (piece == Piece.None) return moves;
        if (IsWhiteTurn != IsWhite(piece)) return moves;

        var candidates = GetPseudoLegalMoves(from, piece);

        foreach (var to in candidates)
        {
            if (!MoveLeavesKingInCheck(from, to))
                moves.Add(to);
        }

        return moves;
    }

    /// <summary>Is the current side's king in check?</summary>
    public bool IsInCheck(bool white)
    {
        Vector2Int kingPos = FindKing(white);
        return IsSquareAttackedBy(kingPos, !white);
    }

    // ════════════════════════════════════════════
    //  SNAPSHOT (mode switching)
    // ════════════════════════════════════════════

    public GameSnapshot TakeSnapshot()
    {
        var snap = new GameSnapshot
        {
            IsWhiteTurn             = IsWhiteTurn,
            WhiteCanCastleKingside  = WhiteCanCastleKingside,
            WhiteCanCastleQueenside = WhiteCanCastleQueenside,
            BlackCanCastleKingside  = BlackCanCastleKingside,
            BlackCanCastleQueenside = BlackCanCastleQueenside,
            EnPassantCol            = EnPassantTarget.x,
            EnPassantRow            = EnPassantTarget.y,
            Result                  = Result
        };

        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
                snap.FlatBoard[r * 8 + c] = (int)Board[r, c];

        foreach (var p in CapturedByWhite) snap.CapturedByWhite.Add((int)p);
        foreach (var p in CapturedByBlack) snap.CapturedByBlack.Add((int)p);

        foreach (var m in MoveHistory)
            snap.MoveHistory.Add(m.Notation);

        return snap;
    }

    public void RestoreSnapshot(GameSnapshot snap)
    {
        Board = new Piece[8, 8];
        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
                Board[r, c] = (Piece)snap.FlatBoard[r * 8 + c];

        IsWhiteTurn             = snap.IsWhiteTurn;
        WhiteCanCastleKingside  = snap.WhiteCanCastleKingside;
        WhiteCanCastleQueenside = snap.WhiteCanCastleQueenside;
        BlackCanCastleKingside  = snap.BlackCanCastleKingside;
        BlackCanCastleQueenside = snap.BlackCanCastleQueenside;
        EnPassantTarget         = new Vector2Int(snap.EnPassantCol, snap.EnPassantRow);
        Result                  = snap.Result;

        CapturedByWhite.Clear();
        foreach (var p in snap.CapturedByWhite) CapturedByWhite.Add((Piece)p);

        CapturedByBlack.Clear();
        foreach (var p in snap.CapturedByBlack) CapturedByBlack.Add((Piece)p);

        // MoveHistory is notation-only after restore (full MoveRecord not needed for display)
        MoveHistory.Clear();
        foreach (var n in snap.MoveHistory)
            MoveHistory.Add(new MoveRecord { Notation = n });
    }

    // ════════════════════════════════════════════
    //  INTERNAL MOVE APPLICATION
    // ════════════════════════════════════════════

    private void ApplyMoveInternal(Vector2Int from, Vector2Int to, Piece promotionChoice)
    {
        Piece moving   = Board[from.x, from.y];
        Piece captured = Board[to.x, to.y];
        bool  isWhite  = IsWhite(moving);

        var record = new MoveRecord
        {
            From          = from,
            To            = to,
            PieceMoved    = moving,
            PieceCaptured = captured
        };

        // ── En passant capture ───────────────────
        if ((moving == Piece.WhitePawn || moving == Piece.BlackPawn) && to == EnPassantTarget)
        {
            int capturedRow = from.x;
            captured = Board[capturedRow, to.y];
            Board[capturedRow, to.y] = Piece.None;
            record.WasEnPassant = true;
            record.PieceCaptured = captured;
        }

        // ── Castle ───────────────────────────────
        if (moving == Piece.WhiteKing || moving == Piece.BlackKing)
        {
            int colDiff = to.y - from.y;
            if (Mathf.Abs(colDiff) == 2)
            {
                record.WasCastle = true;
                int rookFromCol = colDiff > 0 ? 7 : 0;
                int rookToCol   = colDiff > 0 ? to.y - 1 : to.y + 1;
                Board[from.x, rookToCol]   = Board[from.x, rookFromCol];
                Board[from.x, rookFromCol] = Piece.None;
            }
        }

        // ── Apply the move ───────────────────────
        Board[to.x, to.y]     = moving;
        Board[from.x, from.y] = Piece.None;

        // ── Pawn promotion ───────────────────────
        bool isPromotion = false;
        if (moving == Piece.WhitePawn && to.x == 7)
        {
            // Ensure promotion piece is white
            Board[to.x, to.y] = ToWhite(promotionChoice);
            record.PromotionPiece = Board[to.x, to.y];
            isPromotion = true;
        }
        else if (moving == Piece.BlackPawn && to.x == 0)
        {
            Board[to.x, to.y] = ToBlack(promotionChoice);
            record.PromotionPiece = Board[to.x, to.y];
            isPromotion = true;
        }

        // ── Capture list ─────────────────────────
        if (captured != Piece.None)
        {
            if (isWhite) CapturedByWhite.Add(captured);
            else         CapturedByBlack.Add(captured);
        }

        // ── Update castling rights ───────────────
        UpdateCastlingRights(moving, from);

        // ── Update en-passant target ─────────────
        EnPassantTarget = new Vector2Int(-1, -1);
        if ((moving == Piece.WhitePawn || moving == Piece.BlackPawn)
            && Mathf.Abs(to.x - from.x) == 2)
        {
            EnPassantTarget = new Vector2Int((from.x + to.x) / 2, from.y);
        }

        // ── 50-move rule clock ───────────────────
        bool isPawnMove = moving == Piece.WhitePawn || moving == Piece.BlackPawn;
        _halfMoveClock = (isPawnMove || captured != Piece.None) ? 0 : _halfMoveClock + 1;

        // ── Notation (basic algebraic) ───────────
        record.Notation = BuildNotation(record, isPromotion);
        MoveHistory.Add(record);

        // ── Flip turn ────────────────────────────
        IsWhiteTurn = !IsWhiteTurn;

        // ── Fire events ──────────────────────────
        GameEvents.RaiseMoveMade(record);
        GameEvents.RaiseTurnChanged(IsWhiteTurn);

        // ── Check game-over conditions ───────────
        RecordPosition();
        CheckGameOver();
    }

    // ════════════════════════════════════════════
    //  PSEUDO-LEGAL MOVE GENERATION
    // ════════════════════════════════════════════

    private List<Vector2Int> GetPseudoLegalMoves(Vector2Int from, Piece piece)
    {
        var moves = new List<Vector2Int>();
        bool white = IsWhite(piece);
        int  r = from.x, c = from.y;

        switch (piece)
        {
            case Piece.WhitePawn:
                AddPawnMoves(moves, r, c, true);
                break;
            case Piece.BlackPawn:
                AddPawnMoves(moves, r, c, false);
                break;

            case Piece.WhiteKnight:
            case Piece.BlackKnight:
                AddKnightMoves(moves, r, c, white);
                break;

            case Piece.WhiteBishop:
            case Piece.BlackBishop:
                AddSlidingMoves(moves, r, c, white, false, true);
                break;

            case Piece.WhiteRook:
            case Piece.BlackRook:
                AddSlidingMoves(moves, r, c, white, true, false);
                break;

            case Piece.WhiteQueen:
            case Piece.BlackQueen:
                AddSlidingMoves(moves, r, c, white, true, true);
                break;

            case Piece.WhiteKing:
            case Piece.BlackKing:
                AddKingMoves(moves, r, c, white);
                break;
        }

        return moves;
    }

    private void AddPawnMoves(List<Vector2Int> moves, int r, int c, bool white)
    {
        int dir      = white ? 1 : -1;
        int startRow = white ? 1 : 6;

        // One step forward
        int nr = r + dir;
        if (InBounds(nr, c) && Board[nr, c] == Piece.None)
        {
            moves.Add(new Vector2Int(nr, c));
            // Two steps from starting row
            int nr2 = r + 2 * dir;
            if (r == startRow && Board[nr2, c] == Piece.None)
                moves.Add(new Vector2Int(nr2, c));
        }

        // Diagonal captures
        foreach (int dc in new[] { -1, 1 })
        {
            int nc = c + dc;
            if (!InBounds(nr, nc)) continue;
            Piece target = Board[nr, nc];
            bool isEnPassant = new Vector2Int(nr, nc) == EnPassantTarget;
            if (isEnPassant || (target != Piece.None && IsWhite(target) != white))
                moves.Add(new Vector2Int(nr, nc));
        }
    }

    private void AddKnightMoves(List<Vector2Int> moves, int r, int c, bool white)
    {
        int[] dr = { 2, 2,-2,-2, 1, 1,-1,-1 };
        int[] dc = { 1,-1, 1,-1, 2,-2, 2,-2 };
        for (int i = 0; i < 8; i++)
        {
            int nr = r + dr[i], nc = c + dc[i];
            if (InBounds(nr, nc))
            {
                Piece t = Board[nr, nc];
                if (t == Piece.None || IsWhite(t) != white)
                    moves.Add(new Vector2Int(nr, nc));
            }
        }
    }

    private void AddSlidingMoves(List<Vector2Int> moves, int r, int c, bool white,
                                  bool rook, bool bishop)
    {
        int[][] dirs = GetSlidingDirs(rook, bishop);
        foreach (var d in dirs)
        {
            int nr = r + d[0], nc = c + d[1];
            while (InBounds(nr, nc))
            {
                Piece t = Board[nr, nc];
                if (t == Piece.None)
                {
                    moves.Add(new Vector2Int(nr, nc));
                }
                else
                {
                    if (IsWhite(t) != white) moves.Add(new Vector2Int(nr, nc));
                    break;
                }
                nr += d[0]; nc += d[1];
            }
        }
    }

    private void AddKingMoves(List<Vector2Int> moves, int r, int c, bool white)
    {
        for (int dr = -1; dr <= 1; dr++)
        for (int dc = -1; dc <= 1; dc++)
        {
            if (dr == 0 && dc == 0) continue;
            int nr = r + dr, nc = c + dc;
            if (!InBounds(nr, nc)) continue;
            Piece t = Board[nr, nc];
            if (t == Piece.None || IsWhite(t) != white)
                moves.Add(new Vector2Int(nr, nc));
        }

        // Castling
        int backRank = white ? 0 : 7;
        if (r != backRank || c != 4) return;

        // Kingside
        if ((white ? WhiteCanCastleKingside : BlackCanCastleKingside)
            && Board[backRank, 5] == Piece.None
            && Board[backRank, 6] == Piece.None
            && !IsSquareAttackedBy(new Vector2Int(backRank, 4), !white)
            && !IsSquareAttackedBy(new Vector2Int(backRank, 5), !white)
            && !IsSquareAttackedBy(new Vector2Int(backRank, 6), !white))
        {
            moves.Add(new Vector2Int(backRank, 6));
        }

        // Queenside
        if ((white ? WhiteCanCastleQueenside : BlackCanCastleQueenside)
            && Board[backRank, 3] == Piece.None
            && Board[backRank, 2] == Piece.None
            && Board[backRank, 1] == Piece.None
            && !IsSquareAttackedBy(new Vector2Int(backRank, 4), !white)
            && !IsSquareAttackedBy(new Vector2Int(backRank, 3), !white)
            && !IsSquareAttackedBy(new Vector2Int(backRank, 2), !white))
        {
            moves.Add(new Vector2Int(backRank, 2));
        }
    }

    // ════════════════════════════════════════════
    //  CHECK DETECTION
    // ════════════════════════════════════════════

    private bool MoveLeavesKingInCheck(Vector2Int from, Vector2Int to)
    {
        // Simulate move on a scratch board
        Piece[,] scratch = (Piece[,])Board.Clone();
        Piece moving = scratch[from.x, from.y];
        bool  white  = IsWhite(moving);

        // En passant removal
        if ((moving == Piece.WhitePawn || moving == Piece.BlackPawn) && to == EnPassantTarget)
            scratch[from.x, to.y] = Piece.None;

        scratch[to.x, to.y]     = moving;
        scratch[from.x, from.y] = Piece.None;

        Vector2Int kingPos = FindKingOnBoard(scratch, white);
        return IsSquareAttackedByOnBoard(scratch, kingPos, !white);
    }

    private bool IsSquareAttackedBy(Vector2Int square, bool byWhite)
        => IsSquareAttackedByOnBoard(Board, square, byWhite);

    private bool IsSquareAttackedByOnBoard(Piece[,] board, Vector2Int sq, bool byWhite)
    {
        int r = sq.x, c = sq.y;

        // Knights
        int[] dr = { 2, 2,-2,-2, 1, 1,-1,-1 };
        int[] dc = { 1,-1, 1,-1, 2,-2, 2,-2 };
        for (int i = 0; i < 8; i++)
        {
            int nr = r + dr[i], nc = c + dc[i];
            if (InBounds(nr, nc))
            {
                Piece t = board[nr, nc];
                Piece knight = byWhite ? Piece.WhiteKnight : Piece.BlackKnight;
                if (t == knight) return true;
            }
        }

        // Sliding pieces (rook/queen for orthogonal, bishop/queen for diagonal)
        int[][] rookDirs   = { new[]{1,0}, new[]{-1,0}, new[]{0,1}, new[]{0,-1} };
        int[][] bishopDirs = { new[]{1,1}, new[]{1,-1}, new[]{-1,1}, new[]{-1,-1} };

        Piece ourRook   = byWhite ? Piece.WhiteRook   : Piece.BlackRook;
        Piece ourBishop = byWhite ? Piece.WhiteBishop : Piece.BlackBishop;
        Piece ourQueen  = byWhite ? Piece.WhiteQueen  : Piece.BlackQueen;

        foreach (var d in rookDirs)
        {
            int nr = r + d[0], nc = c + d[1];
            while (InBounds(nr, nc))
            {
                Piece t = board[nr, nc];
                if (t != Piece.None)
                {
                    if (t == ourRook || t == ourQueen) return true;
                    break;
                }
                nr += d[0]; nc += d[1];
            }
        }

        foreach (var d in bishopDirs)
        {
            int nr = r + d[0], nc = c + d[1];
            while (InBounds(nr, nc))
            {
                Piece t = board[nr, nc];
                if (t != Piece.None)
                {
                    if (t == ourBishop || t == ourQueen) return true;
                    break;
                }
                nr += d[0]; nc += d[1];
            }
        }

        // Pawns
        int pawnDir = byWhite ? -1 : 1; // direction FROM which attacking pawn comes
        Piece pawn = byWhite ? Piece.WhitePawn : Piece.BlackPawn;
        foreach (int pdc in new[] { -1, 1 })
        {
            int nr = r + pawnDir, nc = c + pdc;
            if (InBounds(nr, nc) && board[nr, nc] == pawn) return true;
        }

        // King
        Piece king = byWhite ? Piece.WhiteKing : Piece.BlackKing;
        for (int kdr = -1; kdr <= 1; kdr++)
        for (int kdc = -1; kdc <= 1; kdc++)
        {
            if (kdr == 0 && kdc == 0) continue;
            int nr = r + kdr, nc = c + kdc;
            if (InBounds(nr, nc) && board[nr, nc] == king) return true;
        }

        return false;
    }

    // ════════════════════════════════════════════
    //  GAME-OVER DETECTION
    // ════════════════════════════════════════════

    private void CheckGameOver()
    {
        // Fifty-move rule
        if (_halfMoveClock >= 100)
        {
            Result = GameResult.DrawByFiftyMoveRule;
            GameEvents.RaiseGameOver(Result);
            return;
        }

        // Threefold repetition
        string posKey = BuildPositionKey();
        if (_positionCounts.TryGetValue(posKey, out int count) && count >= 3)
        {
            Result = GameResult.DrawByRepetition;
            GameEvents.RaiseGameOver(Result);
            return;
        }

        // Checkmate / stalemate — does the current side have any legal move?
        bool hasLegalMove = false;
        outer:
        for (int r = 0; r < 8 && !hasLegalMove; r++)
        for (int c = 0; c < 8 && !hasLegalMove; c++)
        {
            Piece p = Board[r, c];
            if (p == Piece.None || IsWhite(p) != IsWhiteTurn) continue;
            var from = new Vector2Int(r, c);
            foreach (var to in GetPseudoLegalMoves(from, p))
            {
                if (!MoveLeavesKingInCheck(from, to))
                {
                    hasLegalMove = true;
                    goto outer;
                }
            }
        }

        if (!hasLegalMove)
        {
            bool inCheck = IsInCheck(IsWhiteTurn);
            if (inCheck)
                Result = IsWhiteTurn ? GameResult.BlackWins : GameResult.WhiteWins;
            else
                Result = GameResult.Stalemate;

            GameEvents.RaiseGameOver(Result);
        }
    }

    // ════════════════════════════════════════════
    //  HELPERS
    // ════════════════════════════════════════════

    private void UpdateCastlingRights(Piece moving, Vector2Int from)
    {
        if (moving == Piece.WhiteKing)
        {
            WhiteCanCastleKingside  = false;
            WhiteCanCastleQueenside = false;
        }
        else if (moving == Piece.BlackKing)
        {
            BlackCanCastleKingside  = false;
            BlackCanCastleQueenside = false;
        }
        else if (moving == Piece.WhiteRook)
        {
            if (from == new Vector2Int(0, 0)) WhiteCanCastleQueenside = false;
            if (from == new Vector2Int(0, 7)) WhiteCanCastleKingside  = false;
        }
        else if (moving == Piece.BlackRook)
        {
            if (from == new Vector2Int(7, 0)) BlackCanCastleQueenside = false;
            if (from == new Vector2Int(7, 7)) BlackCanCastleKingside  = false;
        }
    }

    private Vector2Int FindKing(bool white) => FindKingOnBoard(Board, white);

    private Vector2Int FindKingOnBoard(Piece[,] board, bool white)
    {
        Piece king = white ? Piece.WhiteKing : Piece.BlackKing;
        for (int r = 0; r < 8; r++)
        for (int c = 0; c < 8; c++)
            if (board[r, c] == king) return new Vector2Int(r, c);
        return new Vector2Int(-1, -1); // Should never happen
    }

    private void RecordPosition()
    {
        string key = BuildPositionKey();
        if (_positionCounts.ContainsKey(key)) _positionCounts[key]++;
        else _positionCounts[key] = 1;
    }

    private string BuildPositionKey()
    {
        // Compact FEN-like key for repetition detection
        var sb = new System.Text.StringBuilder(80);
        for (int r = 0; r < 8; r++)
        for (int c = 0; c < 8; c++)
            sb.Append((int)Board[r, c]).Append(',');

        sb.Append(IsWhiteTurn ? 'w' : 'b');
        sb.Append(WhiteCanCastleKingside  ? 'K' : '-');
        sb.Append(WhiteCanCastleQueenside ? 'Q' : '-');
        sb.Append(BlackCanCastleKingside  ? 'k' : '-');
        sb.Append(BlackCanCastleQueenside ? 'q' : '-');
        sb.Append(EnPassantTarget.x).Append(EnPassantTarget.y);
        return sb.ToString();
    }

    private string BuildNotation(MoveRecord r, bool isPromotion)
    {
        string cols = "abcdefgh";
        string from = $"{cols[r.From.y]}{r.From.x + 1}";
        string to   = $"{cols[r.To.y]}{r.To.x + 1}";
        string promo = isPromotion ? $"={PieceLetter(r.PromotionPiece)}" : "";
        return $"{from}{to}{promo}";
    }

    private static string PieceLetter(Piece p) => p switch
    {
        Piece.WhiteQueen  or Piece.BlackQueen  => "Q",
        Piece.WhiteRook   or Piece.BlackRook   => "R",
        Piece.WhiteBishop or Piece.BlackBishop => "B",
        Piece.WhiteKnight or Piece.BlackKnight => "N",
        _ => ""
    };

    private static bool InBounds(int r, int c) => r >= 0 && r < 8 && c >= 0 && c < 8;

    private static bool IsWhite(Piece p) => (int)p > 0;

    private static Piece ToWhite(Piece p)
    {
        int v = Mathf.Abs((int)p);
        return (Piece)v;
    }

    private static Piece ToBlack(Piece p)
    {
        int v = -Mathf.Abs((int)p);
        return (Piece)v;
    }

    private static int[][] GetSlidingDirs(bool rook, bool bishop)
    {
        var dirs = new List<int[]>();
        if (rook)
        {
            dirs.Add(new[]{ 1, 0});
            dirs.Add(new[]{-1, 0});
            dirs.Add(new[]{ 0, 1});
            dirs.Add(new[]{ 0,-1});
        }
        if (bishop)
        {
            dirs.Add(new[]{ 1, 1});
            dirs.Add(new[]{ 1,-1});
            dirs.Add(new[]{-1, 1});
            dirs.Add(new[]{-1,-1});
        }
        return dirs.ToArray();
    }

    // ════════════════════════════════════════════
    //  DEBUG
    // ════════════════════════════════════════════

    [ContextMenu("Print Board to Console")]
    public void PrintBoard()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"  a  b  c  d  e  f  g  h");
        for (int r = 7; r >= 0; r--)
        {
            sb.Append(r + 1).Append(' ');
            for (int c = 0; c < 8; c++)
            {
                int v = (int)Board[r, c];
                sb.Append(v.ToString("+0;-0; 0")).Append(' ');
            }
            sb.AppendLine();
        }
        sb.AppendLine($"Turn: {(IsWhiteTurn ? "White" : "Black")}  |  Result: {Result}");
        Debug.Log(sb.ToString());
    }
}