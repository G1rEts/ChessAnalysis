using System.Text;

namespace ChessAnalysis.Core.Chess;

[Flags]
public enum CastlingRights : byte
{
    None = 0,
    WhiteKingSide = 1,
    WhiteQueenSide = 2,
    BlackKingSide = 4,
    BlackQueenSide = 8,
    WhiteAll = WhiteKingSide | WhiteQueenSide,
    BlackAll = BlackKingSide | BlackQueenSide,
    All = WhiteAll | BlackAll
}

public class BoardState
{
    public const string StartingFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    private readonly Piece[] _squares = new Piece[64];
    public PieceColor SideToMove { get; set; } = PieceColor.White;
    public CastlingRights Castling { get; set; } = CastlingRights.All;
    public Square EnPassantSquare { get; set; } = Square.None;
    public int HalfmoveClock { get; set; } = 0;
    public int FullmoveNumber { get; set; } = 1;

    public BoardState()
    {
        LoadFen(StartingFen);
    }

    public Piece this[Square square]
    {
        get => square.IsValid ? _squares[square.Index] : Piece.None;
        set
        {
            if (square.IsValid)
            {
                _squares[square.Index] = value;
            }
        }
    }

    public Piece this[int file, int rank]
    {
        get => this[new Square(file, rank)];
        set => this[new Square(file, rank)] = value;
    }

    public BoardState Clone()
    {
        var clone = new BoardState();
        Array.Copy(_squares, clone._squares, 64);
        clone.SideToMove = SideToMove;
        clone.Castling = Castling;
        clone.EnPassantSquare = EnPassantSquare;
        clone.HalfmoveClock = HalfmoveClock;
        clone.FullmoveNumber = FullmoveNumber;
        return clone;
    }

    public void LoadFen(string fen)
    {
        Array.Fill(_squares, Piece.None);
        Castling = CastlingRights.None;
        EnPassantSquare = Square.None;
        HalfmoveClock = 0;
        FullmoveNumber = 1;

        if (string.IsNullOrWhiteSpace(fen)) return;

        string[] parts = fen.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1) return;

        // 1. Piece placement
        string[] ranks = parts[0].Split('/');
        if (ranks.Length == 8)
        {
            for (int r = 7; r >= 0; r--)
            {
                string rankStr = ranks[7 - r];
                int file = 0;
                foreach (char c in rankStr)
                {
                    if (char.IsDigit(c))
                    {
                        file += c - '0';
                    }
                    else
                    {
                        if (file < 8)
                        {
                            _squares[r * 8 + file] = Piece.FromChar(c);
                            file++;
                        }
                    }
                }
            }
        }

        // 2. Active color
        if (parts.Length > 1)
        {
            SideToMove = parts[1].Equals("b", StringComparison.OrdinalIgnoreCase)
                ? PieceColor.Black
                : PieceColor.White;
        }

        // 3. Castling availability
        if (parts.Length > 2)
        {
            string castlingStr = parts[2];
            if (castlingStr.Contains('K')) Castling |= CastlingRights.WhiteKingSide;
            if (castlingStr.Contains('Q')) Castling |= CastlingRights.WhiteQueenSide;
            if (castlingStr.Contains('k')) Castling |= CastlingRights.BlackKingSide;
            if (castlingStr.Contains('q')) Castling |= CastlingRights.BlackQueenSide;
        }

        // 4. En passant target square
        if (parts.Length > 3 && parts[3] != "-")
        {
            EnPassantSquare = Square.FromAlgebraic(parts[3]);
        }

        // 5. Halfmove clock
        if (parts.Length > 4 && int.TryParse(parts[4], out int hm))
        {
            HalfmoveClock = hm;
        }

        // 6. Fullmove number
        if (parts.Length > 5 && int.TryParse(parts[5], out int fm))
        {
            FullmoveNumber = fm;
        }
    }

    public string ToFen()
    {
        var sb = new StringBuilder();

        // 1. Piece placement
        for (int r = 7; r >= 0; r--)
        {
            int emptyCount = 0;
            for (int f = 0; f < 8; f++)
            {
                var piece = _squares[r * 8 + f];
                if (piece.IsEmpty)
                {
                    emptyCount++;
                }
                else
                {
                    if (emptyCount > 0)
                    {
                        sb.Append(emptyCount);
                        emptyCount = 0;
                    }
                    sb.Append(piece.ToChar());
                }
            }
            if (emptyCount > 0)
            {
                sb.Append(emptyCount);
            }
            if (r > 0) sb.Append('/');
        }

        // 2. Active color
        sb.Append(SideToMove == PieceColor.White ? " w " : " b ");

        // 3. Castling
        var castlingSb = new StringBuilder();
        if ((Castling & CastlingRights.WhiteKingSide) != 0) castlingSb.Append('K');
        if ((Castling & CastlingRights.WhiteQueenSide) != 0) castlingSb.Append('Q');
        if ((Castling & CastlingRights.BlackKingSide) != 0) castlingSb.Append('k');
        if ((Castling & CastlingRights.BlackQueenSide) != 0) castlingSb.Append('q');
        sb.Append(castlingSb.Length > 0 ? castlingSb.ToString() : "-");

        // 4. En passant
        sb.Append(' ');
        sb.Append(EnPassantSquare.IsValid ? EnPassantSquare.ToAlgebraic() : "-");

        // 5 & 6. Clocks
        sb.Append($" {HalfmoveClock} {FullmoveNumber}");

        return sb.ToString();
    }

    public Square FindKing(PieceColor color)
    {
        for (int i = 0; i < 64; i++)
        {
            if (_squares[i].Type == PieceType.King && _squares[i].Color == color)
            {
                return new Square(i);
            }
        }
        return Square.None;
    }

    public bool IsSquareAttacked(Square sq, PieceColor attackingColor)
    {
        if (!sq.IsValid) return false;

        // Pawns
        int pawnRankDir = attackingColor == PieceColor.White ? -1 : 1;
        int attackRank = sq.Rank + pawnRankDir;
        if (attackRank is >= 0 and < 8)
        {
            if (sq.File > 0)
            {
                var p = _squares[attackRank * 8 + (sq.File - 1)];
                if (p.Type == PieceType.Pawn && p.Color == attackingColor) return true;
            }
            if (sq.File < 7)
            {
                var p = _squares[attackRank * 8 + (sq.File + 1)];
                if (p.Type == PieceType.Pawn && p.Color == attackingColor) return true;
            }
        }

        // Knights
        int[] knightOffsets = { -17, -15, -10, -6, 6, 10, 15, 17 };
        foreach (int off in knightOffsets)
        {
            int targetIdx = sq.Index + off;
            if (targetIdx is >= 0 and < 64)
            {
                int df = Math.Abs((targetIdx % 8) - sq.File);
                int dr = Math.Abs((targetIdx / 8) - sq.Rank);
                if (df * dr == 2)
                {
                    var p = _squares[targetIdx];
                    if (p.Type == PieceType.Knight && p.Color == attackingColor) return true;
                }
            }
        }

        // Kings
        for (int dr = -1; dr <= 1; dr++)
        {
            for (int df = -1; df <= 1; df++)
            {
                if (df == 0 && dr == 0) continue;
                int f = sq.File + df;
                int r = sq.Rank + dr;
                if (f is >= 0 and < 8 && r is >= 0 and < 8)
                {
                    var p = _squares[r * 8 + f];
                    if (p.Type == PieceType.King && p.Color == attackingColor) return true;
                }
            }
        }

        // Bishop / Queen rays
        (int df, int dr)[] diagDirs = { (1, 1), (1, -1), (-1, 1), (-1, -1) };
        foreach (var (df, dr) in diagDirs)
        {
            int f = sq.File + df;
            int r = sq.Rank + dr;
            while (f is >= 0 and < 8 && r is >= 0 and < 8)
            {
                var p = _squares[r * 8 + f];
                if (!p.IsEmpty)
                {
                    if (p.Color == attackingColor && (p.Type == PieceType.Bishop || p.Type == PieceType.Queen))
                        return true;
                    break;
                }
                f += df;
                r += dr;
            }
        }

        // Rook / Queen rays
        (int df, int dr)[] straightDirs = { (1, 0), (-1, 0), (0, 1), (0, -1) };
        foreach (var (df, dr) in straightDirs)
        {
            int f = sq.File + df;
            int r = sq.Rank + dr;
            while (f is >= 0 and < 8 && r is >= 0 and < 8)
            {
                var p = _squares[r * 8 + f];
                if (!p.IsEmpty)
                {
                    if (p.Color == attackingColor && (p.Type == PieceType.Rook || p.Type == PieceType.Queen))
                        return true;
                    break;
                }
                f += df;
                r += dr;
            }
        }

        return false;
    }

    public int GetLowestAttackerValue(Square sq, PieceColor attackingColor)
    {
        if (!sq.IsValid) return int.MaxValue;
        int minVal = int.MaxValue;

        // Pawns (100)
        int pawnRankDir = attackingColor == PieceColor.White ? -1 : 1;
        int attackRank = sq.Rank + pawnRankDir;
        if (attackRank is >= 0 and < 8)
        {
            if (sq.File > 0 && _squares[attackRank * 8 + (sq.File - 1)] is { Type: PieceType.Pawn } p1 && p1.Color == attackingColor)
                minVal = Math.Min(minVal, p1.Value);
            if (sq.File < 7 && _squares[attackRank * 8 + (sq.File + 1)] is { Type: PieceType.Pawn } p2 && p2.Color == attackingColor)
                minVal = Math.Min(minVal, p2.Value);
        }

        // Knights (320)
        int[] knightOffsets = { -17, -15, -10, -6, 6, 10, 15, 17 };
        foreach (int off in knightOffsets)
        {
            int targetIdx = sq.Index + off;
            if (targetIdx is >= 0 and < 64)
            {
                int df = Math.Abs((targetIdx % 8) - sq.File);
                int dr = Math.Abs((targetIdx / 8) - sq.Rank);
                if (df * dr == 2)
                {
                    var p = _squares[targetIdx];
                    if (p.Type == PieceType.Knight && p.Color == attackingColor)
                        minVal = Math.Min(minVal, p.Value);
                }
            }
        }

        // Bishop / Queen rays
        (int df, int dr)[] diagDirs = { (1, 1), (1, -1), (-1, 1), (-1, -1) };
        foreach (var (df, dr) in diagDirs)
        {
            int f = sq.File + df;
            int r = sq.Rank + dr;
            while (f is >= 0 and < 8 && r is >= 0 and < 8)
            {
                var p = _squares[r * 8 + f];
                if (!p.IsEmpty)
                {
                    if (p.Color == attackingColor && (p.Type == PieceType.Bishop || p.Type == PieceType.Queen))
                        minVal = Math.Min(minVal, p.Value);
                    break;
                }
                f += df;
                r += dr;
            }
        }

        // Rook / Queen rays
        (int df, int dr)[] straightDirs = { (1, 0), (-1, 0), (0, 1), (0, -1) };
        foreach (var (df, dr) in straightDirs)
        {
            int f = sq.File + df;
            int r = sq.Rank + dr;
            while (f is >= 0 and < 8 && r is >= 0 and < 8)
            {
                var p = _squares[r * 8 + f];
                if (!p.IsEmpty)
                {
                    if (p.Color == attackingColor && (p.Type == PieceType.Rook || p.Type == PieceType.Queen))
                        minVal = Math.Min(minVal, p.Value);
                    break;
                }
                f += df;
                r += dr;
            }
        }

        // Kings
        for (int dr = -1; dr <= 1; dr++)
        {
            for (int df = -1; df <= 1; df++)
            {
                if (df == 0 && dr == 0) continue;
                int f = sq.File + df;
                int r = sq.Rank + dr;
                if (f is >= 0 and < 8 && r is >= 0 and < 8)
                {
                    var p = _squares[r * 8 + f];
                    if (p.Type == PieceType.King && p.Color == attackingColor)
                        minVal = Math.Min(minVal, p.Value);
                }
            }
        }

        return minVal;
    }

    /// <summary>
    /// Возвращает список полей, с которых фигуры указанного цвета атакуют заданную клетку sq.
    /// </summary>
    public List<Square> GetAttackingSquares(Square sq, PieceColor attackingColor)
    {
        var list = new List<Square>(4);
        if (!sq.IsValid) return list;

        // Pawns
        int pawnRankDir = attackingColor == PieceColor.White ? -1 : 1;
        int attackRank = sq.Rank + pawnRankDir;
        if (attackRank is >= 0 and < 8)
        {
            if (sq.File > 0 && _squares[attackRank * 8 + (sq.File - 1)] is { Type: PieceType.Pawn } p1 && p1.Color == attackingColor)
                list.Add(new Square(sq.File - 1, attackRank));
            if (sq.File < 7 && _squares[attackRank * 8 + (sq.File + 1)] is { Type: PieceType.Pawn } p2 && p2.Color == attackingColor)
                list.Add(new Square(sq.File + 1, attackRank));
        }

        // Knights
        int[] knightOffsets = { -17, -15, -10, -6, 6, 10, 15, 17 };
        foreach (int off in knightOffsets)
        {
            int targetIdx = sq.Index + off;
            if (targetIdx is >= 0 and < 64)
            {
                int df = Math.Abs((targetIdx % 8) - sq.File);
                int dr = Math.Abs((targetIdx / 8) - sq.Rank);
                if (df * dr == 2 && _squares[targetIdx] is { Type: PieceType.Knight } pk && pk.Color == attackingColor)
                {
                    list.Add(new Square(targetIdx));
                }
            }
        }

        // Bishop / Queen rays
        (int df, int dr)[] diagDirs = { (1, 1), (1, -1), (-1, 1), (-1, -1) };
        foreach (var (df, dr) in diagDirs)
        {
            int f = sq.File + df;
            int r = sq.Rank + dr;
            while (f is >= 0 and < 8 && r is >= 0 and < 8)
            {
                var p = _squares[r * 8 + f];
                if (!p.IsEmpty)
                {
                    if (p.Color == attackingColor && (p.Type == PieceType.Bishop || p.Type == PieceType.Queen))
                        list.Add(new Square(f, r));
                    break;
                }
                f += df;
                r += dr;
            }
        }

        // Rook / Queen rays
        (int df, int dr)[] straightDirs = { (1, 0), (-1, 0), (0, 1), (0, -1) };
        foreach (var (df, dr) in straightDirs)
        {
            int f = sq.File + df;
            int r = sq.Rank + dr;
            while (f is >= 0 and < 8 && r is >= 0 and < 8)
            {
                var p = _squares[r * 8 + f];
                if (!p.IsEmpty)
                {
                    if (p.Color == attackingColor && (p.Type == PieceType.Rook || p.Type == PieceType.Queen))
                        list.Add(new Square(f, r));
                    break;
                }
                f += df;
                r += dr;
            }
        }

        return list;
    }

    /// <summary>
    /// Проверяет, атакует ли фигура, находящаяся на attackerSq, поле targetSq.
    /// </summary>
    public bool IsSquareAttackedBy(Square targetSq, Square attackerSq)
    {
        if (!targetSq.IsValid || !attackerSq.IsValid) return false;
        var p = _squares[attackerSq.Index];
        if (p.IsEmpty) return false;

        int df = Math.Abs(targetSq.File - attackerSq.File);
        int dr = Math.Abs(targetSq.Rank - attackerSq.Rank);

        switch (p.Type)
        {
            case PieceType.Pawn:
                int pawnForward = p.Color == PieceColor.White ? 1 : -1;
                return (targetSq.Rank - attackerSq.Rank == pawnForward) && (df == 1);

            case PieceType.Knight:
                return df * dr == 2;

            case PieceType.King:
                return df <= 1 && dr <= 1 && (df != 0 || dr != 0);

            case PieceType.Bishop:
                if (df != dr || df == 0) return false;
                return IsRayClear(attackerSq, targetSq);

            case PieceType.Rook:
                if (df != 0 && dr != 0) return false;
                return IsRayClear(attackerSq, targetSq);

            case PieceType.Queen:
                if (df != dr && df != 0 && dr != 0) return false;
                return IsRayClear(attackerSq, targetSq);

            default:
                return false;
        }
    }

    private bool IsRayClear(Square from, Square to)
    {
        int stepF = Math.Sign(to.File - from.File);
        int stepR = Math.Sign(to.Rank - from.Rank);
        int f = from.File + stepF;
        int r = from.Rank + stepR;
        while (f != to.File || r != to.Rank)
        {
            if (!_squares[r * 8 + f].IsEmpty) return false;
            f += stepF;
            r += stepR;
        }
        return true;
    }

    public bool IsInCheck(PieceColor color)
    {
        var kingSquare = FindKing(color);
        if (!kingSquare.IsValid) return false;
        var enemy = color == PieceColor.White ? PieceColor.Black : PieceColor.White;
        return IsSquareAttacked(kingSquare, enemy);
    }

    public bool IsCheckmate() => IsInCheck(SideToMove) && GenerateLegalMoves().Count == 0;
    public bool IsStalemate() => !IsInCheck(SideToMove) && GenerateLegalMoves().Count == 0;

    public List<Move> GenerateLegalMoves()
    {
        var pseudo = GeneratePseudoLegalMoves();
        var legal = new List<Move>(pseudo.Count);

        foreach (var m in pseudo)
        {
            var next = Clone();
            next.ApplyMoveInternal(m);
            if (!next.IsInCheck(SideToMove))
            {
                legal.Add(m);
            }
        }

        return legal;
    }

    public void ApplyMove(Move move)
    {
        ApplyMoveInternal(move);
    }

    private void ApplyMoveInternal(Move move)
    {
        var movingPiece = _squares[move.From.Index];
        var targetPiece = _squares[move.To.Index];

        bool isPawnMove = movingPiece.Type == PieceType.Pawn;
        bool isCapture = !targetPiece.IsEmpty || (isPawnMove && move.To == EnPassantSquare);

        // Reset or increment halfmove clock
        if (isPawnMove || isCapture)
            HalfmoveClock = 0;
        else
            HalfmoveClock++;

        if (SideToMove == PieceColor.Black)
            FullmoveNumber++;

        // Handle En Passant capture removal
        if (isPawnMove && move.To == EnPassantSquare)
        {
            int captureRank = SideToMove == PieceColor.White ? move.To.Rank - 1 : move.To.Rank + 1;
            _squares[captureRank * 8 + move.To.File] = Piece.None;
        }

        // Handle Castling king move
        if (movingPiece.Type == PieceType.King && Math.Abs(move.To.File - move.From.File) == 2)
        {
            int rank = move.From.Rank;
            if (move.To.File == 6) // King side
            {
                _squares[rank * 8 + 5] = _squares[rank * 8 + 7];
                _squares[rank * 8 + 7] = Piece.None;
            }
            else if (move.To.File == 2) // Queen side
            {
                _squares[rank * 8 + 3] = _squares[rank * 8 + 0];
                _squares[rank * 8 + 0] = Piece.None;
            }
        }

        // Update castling rights if King moves
        if (movingPiece.Type == PieceType.King)
        {
            if (SideToMove == PieceColor.White)
                Castling &= ~CastlingRights.WhiteAll;
            else
                Castling &= ~CastlingRights.BlackAll;
        }

        // Update castling rights if Rook moves
        if (movingPiece.Type == PieceType.Rook)
        {
            if (move.From.Index == 0) Castling &= ~CastlingRights.WhiteQueenSide;
            else if (move.From.Index == 7) Castling &= ~CastlingRights.WhiteKingSide;
            else if (move.From.Index == 56) Castling &= ~CastlingRights.BlackQueenSide;
            else if (move.From.Index == 63) Castling &= ~CastlingRights.BlackKingSide;
        }

        // Update castling rights if Rook is captured
        if (move.To.Index == 0) Castling &= ~CastlingRights.WhiteQueenSide;
        else if (move.To.Index == 7) Castling &= ~CastlingRights.WhiteKingSide;
        else if (move.To.Index == 56) Castling &= ~CastlingRights.BlackQueenSide;
        else if (move.To.Index == 63) Castling &= ~CastlingRights.BlackKingSide;

        // Set en passant square if pawn advances two squares
        if (isPawnMove && Math.Abs(move.To.Rank - move.From.Rank) == 2)
        {
            int epRank = (move.From.Rank + move.To.Rank) / 2;
            EnPassantSquare = new Square(move.From.File, epRank);
        }
        else
        {
            EnPassantSquare = Square.None;
        }

        // Move piece on board
        _squares[move.From.Index] = Piece.None;
        if (move.Promotion != PieceType.None)
        {
            _squares[move.To.Index] = new Piece(move.Promotion, SideToMove);
        }
        else
        {
            _squares[move.To.Index] = movingPiece;
        }

        // Switch side to move
        SideToMove = SideToMove == PieceColor.White ? PieceColor.Black : PieceColor.White;
    }

    private List<Move> GeneratePseudoLegalMoves()
    {
        var list = new List<Move>(40);
        var color = SideToMove;
        var enemy = color == PieceColor.White ? PieceColor.Black : PieceColor.White;

        for (int i = 0; i < 64; i++)
        {
            var p = _squares[i];
            if (p.IsEmpty || p.Color != color) continue;

            var from = new Square(i);

            switch (p.Type)
            {
                case PieceType.Pawn:
                    GeneratePawnMoves(from, color, list);
                    break;
                case PieceType.Knight:
                    GenerateKnightMoves(from, color, list);
                    break;
                case PieceType.Bishop:
                    GenerateSlidingMoves(from, color, list, true, false);
                    break;
                case PieceType.Rook:
                    GenerateSlidingMoves(from, color, list, false, true);
                    break;
                case PieceType.Queen:
                    GenerateSlidingMoves(from, color, list, true, true);
                    break;
                case PieceType.King:
                    GenerateKingMoves(from, color, enemy, list);
                    break;
            }
        }

        return list;
    }

    private void GeneratePawnMoves(Square from, PieceColor color, List<Move> list)
    {
        int forward = color == PieceColor.White ? 1 : -1;
        int startRank = color == PieceColor.White ? 1 : 6;
        int promoRank = color == PieceColor.White ? 7 : 0;

        // 1 step forward
        int oneStepRank = from.Rank + forward;
        if (oneStepRank is >= 0 and < 8)
        {
            var targetOne = new Square(from.File, oneStepRank);
            if (_squares[targetOne.Index].IsEmpty)
            {
                if (oneStepRank == promoRank)
                {
                    AddPromotions(from, targetOne, false, list);
                }
                else
                {
                    list.Add(new Move(from, targetOne));

                    // 2 steps forward
                    if (from.Rank == startRank)
                    {
                        var targetTwo = new Square(from.File, from.Rank + 2 * forward);
                        if (_squares[targetTwo.Index].IsEmpty)
                        {
                            list.Add(new Move(from, targetTwo));
                        }
                    }
                }
            }
        }

        // Captures
        int[] captureFiles = { from.File - 1, from.File + 1 };
        foreach (int cf in captureFiles)
        {
            if (cf is >= 0 and < 8 && oneStepRank is >= 0 and < 8)
            {
                var toSq = new Square(cf, oneStepRank);
                var targetP = _squares[toSq.Index];
                if (!targetP.IsEmpty && targetP.Color != color)
                {
                    if (oneStepRank == promoRank)
                    {
                        AddPromotions(from, toSq, true, list);
                    }
                    else
                    {
                        list.Add(new Move(from, toSq, PieceType.None, IsCapture: true));
                    }
                }
                else if (toSq == EnPassantSquare)
                {
                    list.Add(new Move(from, toSq, PieceType.None, IsCapture: true, IsEnPassant: true));
                }
            }
        }
    }

    private static void AddPromotions(Square from, Square to, bool isCapture, List<Move> list)
    {
        list.Add(new Move(from, to, PieceType.Queen, IsCapture: isCapture));
        list.Add(new Move(from, to, PieceType.Rook, IsCapture: isCapture));
        list.Add(new Move(from, to, PieceType.Bishop, IsCapture: isCapture));
        list.Add(new Move(from, to, PieceType.Knight, IsCapture: isCapture));
    }

    private void GenerateKnightMoves(Square from, PieceColor color, List<Move> list)
    {
        int[] knightOffsets = { -17, -15, -10, -6, 6, 10, 15, 17 };
        foreach (int off in knightOffsets)
        {
            int toIdx = from.Index + off;
            if (toIdx is >= 0 and < 64)
            {
                int df = Math.Abs((toIdx % 8) - from.File);
                int dr = Math.Abs((toIdx / 8) - from.Rank);
                if (df * dr == 2)
                {
                    var target = _squares[toIdx];
                    if (target.IsEmpty)
                        list.Add(new Move(from, new Square(toIdx)));
                    else if (target.Color != color)
                        list.Add(new Move(from, new Square(toIdx), IsCapture: true));
                }
            }
        }
    }

    private void GenerateSlidingMoves(Square from, PieceColor color, List<Move> list, bool diagonals, bool straights)
    {
        var dirs = new List<(int df, int dr)>(8);
        if (diagonals)
        {
            dirs.Add((1, 1)); dirs.Add((1, -1)); dirs.Add((-1, 1)); dirs.Add((-1, -1));
        }
        if (straights)
        {
            dirs.Add((1, 0)); dirs.Add((-1, 0)); dirs.Add((0, 1)); dirs.Add((0, -1));
        }

        foreach (var (df, dr) in dirs)
        {
            int f = from.File + df;
            int r = from.Rank + dr;
            while (f is >= 0 and < 8 && r is >= 0 and < 8)
            {
                var toSq = new Square(f, r);
                var target = _squares[toSq.Index];
                if (target.IsEmpty)
                {
                    list.Add(new Move(from, toSq));
                }
                else
                {
                    if (target.Color != color)
                        list.Add(new Move(from, toSq, IsCapture: true));
                    break;
                }
                f += df;
                r += dr;
            }
        }
    }

    private void GenerateKingMoves(Square from, PieceColor color, PieceColor enemy, List<Move> list)
    {
        for (int dr = -1; dr <= 1; dr++)
        {
            for (int df = -1; df <= 1; df++)
            {
                if (df == 0 && dr == 0) continue;
                int f = from.File + df;
                int r = from.Rank + dr;
                if (f is >= 0 and < 8 && r is >= 0 and < 8)
                {
                    var toSq = new Square(f, r);
                    var target = _squares[toSq.Index];
                    if (target.IsEmpty)
                        list.Add(new Move(from, toSq));
                    else if (target.Color != color)
                        list.Add(new Move(from, toSq, IsCapture: true));
                }
            }
        }

        // Castling
        int rank = color == PieceColor.White ? 0 : 7;
        if (from.Rank == rank && from.File == 4 && !IsSquareAttacked(from, enemy))
        {
            var ksFlag = color == PieceColor.White ? CastlingRights.WhiteKingSide : CastlingRights.BlackKingSide;
            var qsFlag = color == PieceColor.White ? CastlingRights.WhiteQueenSide : CastlingRights.BlackQueenSide;

            // Kingside (e1-g1 / e8-g8)
            if ((Castling & ksFlag) != 0)
            {
                if (_squares[rank * 8 + 5].IsEmpty && _squares[rank * 8 + 6].IsEmpty)
                {
                    if (!IsSquareAttacked(new Square(5, rank), enemy) && !IsSquareAttacked(new Square(6, rank), enemy))
                    {
                        list.Add(new Move(from, new Square(6, rank), IsCastling: true));
                    }
                }
            }

            // Queenside (e1-c1 / e8-c8)
            if ((Castling & qsFlag) != 0)
            {
                if (_squares[rank * 8 + 1].IsEmpty && _squares[rank * 8 + 2].IsEmpty && _squares[rank * 8 + 3].IsEmpty)
                {
                    if (!IsSquareAttacked(new Square(3, rank), enemy) && !IsSquareAttacked(new Square(2, rank), enemy))
                    {
                        list.Add(new Move(from, new Square(2, rank), IsCastling: true));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Finds the legal move corresponding to standard algebraic notation (SAN) like 'e4', 'Nf3', 'O-O', 'exd5', 'Nbd7', 'Qh5#'.
    /// </summary>
    public Move ParseSan(string san)
    {
        if (string.IsNullOrWhiteSpace(san)) return Move.Empty;

        // Clean checks, mates, annotations
        string clean = san.Trim().TrimEnd('+', '#', '!', '?', ' ').Replace('–', '-').Replace('—', '-');

        var legalMoves = GenerateLegalMoves();

        // 1. Castling (handles ASCII O-O, digit 0-0, and Cyrillic О-О)
        string normCastling = clean.Replace('\u041E', 'O').Replace('\u043E', 'O').Replace('0', 'O');
        if (normCastling == "O-O")
        {
            int rank = SideToMove == PieceColor.White ? 0 : 7;
            return legalMoves.FirstOrDefault(m => m.IsCastling && m.To.File == 6 && m.To.Rank == rank);
        }
        if (normCastling == "O-O-O")
        {
            int rank = SideToMove == PieceColor.White ? 0 : 7;
            return legalMoves.FirstOrDefault(m => m.IsCastling && m.To.File == 2 && m.To.Rank == rank);
        }

        // 2. Promotion detection (e.g. e8=Q, e8Q, exd8=N)
        var promo = PieceType.None;
        int eqIdx = clean.IndexOf('=');
        if (eqIdx >= 0 && eqIdx + 1 < clean.Length)
        {
            promo = Piece.FromChar(clean[eqIdx + 1]).Type;
            clean = clean[..eqIdx];
        }
        else if (char.IsLetter(clean[^1]) && "QRBNqrbn".Contains(clean[^1]) && clean.Length > 2 && char.IsDigit(clean[^2]))
        {
            promo = Piece.FromChar(clean[^1]).Type;
            clean = clean[..^1];
        }

        // 3. Piece type
        PieceType movingPieceType = PieceType.Pawn;
        if ("NBRQK".Contains(clean[0]))
        {
            movingPieceType = Piece.FromChar(clean[0]).Type;
            clean = clean[1..];
        }

        // Clean capture 'x'
        clean = clean.Replace("x", "");

        if (clean.Length < 2) return Move.Empty;

        // Last 2 characters are destination square
        string destStr = clean[^2..];
        var destSq = Square.FromAlgebraic(destStr);
        if (!destSq.IsValid) return Move.Empty;

        // Disambiguation
        string disambig = clean[..^2];
        char? disambigFile = null;
        char? disambigRank = null;
        foreach (char c in disambig)
        {
            if (c is >= 'a' and <= 'h') disambigFile = c;
            else if (c is >= '1' and <= '8') disambigRank = c;
        }

        // Match candidates
        var candidates = legalMoves.Where(m =>
            m.To == destSq &&
            _squares[m.From.Index].Type == movingPieceType &&
            (promo == PieceType.None || m.Promotion == promo)
        ).ToList();

        if (disambigFile.HasValue)
        {
            int f = disambigFile.Value - 'a';
            candidates = candidates.Where(m => m.From.File == f).ToList();
        }
        if (disambigRank.HasValue)
        {
            int r = disambigRank.Value - '1';
            candidates = candidates.Where(m => m.From.Rank == r).ToList();
        }

        return candidates.Count > 0 ? candidates[0] : Move.Empty;
    }
}
