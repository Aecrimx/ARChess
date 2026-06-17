using System.Collections.Generic;
using System.Text;
using UnityEngine;

public static class ChessNotationExporter
{
    public static string ToFen(this GameStateManager gsm)
    {
        if (gsm == null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(96);
        Piece[,] board = gsm.Board;

        for (int row = 7; row >= 0; row--)
        {
            int emptyCount = 0;
            for (int col = 0; col < 8; col++)
            {
                Piece piece = board[row, col];
                if (piece == Piece.None)
                {
                    emptyCount++;
                    continue;
                }

                if (emptyCount > 0)
                {
                    sb.Append(emptyCount);
                    emptyCount = 0;
                }

                sb.Append(PieceToFenChar(piece));
            }

            if (emptyCount > 0)
            {
                sb.Append(emptyCount);
            }

            if (row > 0)
            {
                sb.Append('/');
            }
        }

        sb.Append(gsm.IsWhiteTurn ? " w " : " b ");
        sb.Append(BuildCastlingRights(gsm));
        sb.Append(' ');
        sb.Append(SquareName(gsm.EnPassantTarget));
        sb.Append(' ');
        sb.Append(gsm.HalfMoveClock);
        sb.Append(' ');
        sb.Append((gsm.MoveHistory.Count / 2) + 1);

        return sb.ToString();
    }

    public static List<string> GetMoveHistoryUci(this GameStateManager gsm)
    {
        var moves = new List<string>();
        if (gsm == null)
        {
            return moves;
        }

        foreach (MoveRecord move in gsm.MoveHistory)
        {
            string uci = ToUci(move);
            if (!string.IsNullOrEmpty(uci))
            {
                moves.Add(uci);
            }
        }

        return moves;
    }

    public static string ToUci(this MoveRecord move)
    {
        if (!string.IsNullOrWhiteSpace(move.Notation))
        {
            return NormalizeMoveText(move.Notation);
        }

        string promotion = move.PromotionPiece == Piece.None
            ? string.Empty
            : PieceToPromotionChar(move.PromotionPiece).ToString();

        return $"{SquareName(move.From)}{SquareName(move.To)}{promotion}";
    }

    public static AiGameReviewRequest BuildReviewRequest(this GameStateManager gsm, string playerColor)
    {
        return new AiGameReviewRequest
        {
            PlayerColor = playerColor,
            MovesUci = gsm.GetMoveHistoryUci(),
            FinalFen = gsm.ToFen()
        };
    }

    public static string GetReviewPlayerColor(bool localPlayerIsWhite)
    {
        var gmm = GameModeManager.Instance;
        if (gmm != null && gmm.CurrentMode == GameModeManager.GameMode.Local2Player)
        {
            return "both";
        }

        return localPlayerIsWhite ? "white" : "black";
    }

    private static string NormalizeMoveText(string moveText)
    {
        return moveText.Trim().Replace("=", string.Empty).ToLowerInvariant();
    }

    private static string BuildCastlingRights(GameStateManager gsm)
    {
        var rights = new StringBuilder(4);
        if (gsm.WhiteCanCastleKingside) rights.Append('K');
        if (gsm.WhiteCanCastleQueenside) rights.Append('Q');
        if (gsm.BlackCanCastleKingside) rights.Append('k');
        if (gsm.BlackCanCastleQueenside) rights.Append('q');
        return rights.Length == 0 ? "-" : rights.ToString();
    }

    private static string SquareName(Vector2Int square)
    {
        if (square.x < 0 || square.y < 0 || square.x > 7 || square.y > 7)
        {
            return "-";
        }

        char file = (char)('a' + square.y);
        int rank = square.x + 1;
        return $"{file}{rank}";
    }

    private static char PieceToFenChar(Piece piece) => piece switch
    {
        Piece.WhitePawn => 'P',
        Piece.WhiteKnight => 'N',
        Piece.WhiteBishop => 'B',
        Piece.WhiteRook => 'R',
        Piece.WhiteQueen => 'Q',
        Piece.WhiteKing => 'K',
        Piece.BlackPawn => 'p',
        Piece.BlackKnight => 'n',
        Piece.BlackBishop => 'b',
        Piece.BlackRook => 'r',
        Piece.BlackQueen => 'q',
        Piece.BlackKing => 'k',
        _ => '1'
    };

    private static char PieceToPromotionChar(Piece piece) => piece switch
    {
        Piece.WhiteQueen or Piece.BlackQueen => 'q',
        Piece.WhiteRook or Piece.BlackRook => 'r',
        Piece.WhiteBishop or Piece.BlackBishop => 'b',
        Piece.WhiteKnight or Piece.BlackKnight => 'n',
        _ => 'q'
    };
}
