namespace ChessAnalysis.Core.Chess;

public enum PieceType : byte
{
    None = 0,
    Pawn = 1,
    Knight = 2,
    Bishop = 3,
    Rook = 4,
    Queen = 5,
    King = 6
}

public enum PieceColor : byte
{
    White = 0,
    Black = 1
}

public readonly record struct Square(int Index)
{
    public static readonly Square None = new(-1);

    public int File => Index >= 0 && Index < 64 ? Index % 8 : -1;
    public int Rank => Index >= 0 && Index < 64 ? Index / 8 : -1;
    public bool IsValid => Index >= 0 && Index < 64;

    public Square(int file, int rank) : this(rank * 8 + file) { }

    public static Square FromAlgebraic(string s)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Length < 2) return None;
        int file = char.ToLowerInvariant(s[0]) - 'a';
        int rank = s[1] - '1';
        if (file is >= 0 and < 8 && rank is >= 0 and < 8)
        {
            return new Square(file, rank);
        }
        return None;
    }

    public string ToAlgebraic()
    {
        if (!IsValid) return "-";
        char f = (char)('a' + File);
        char r = (char)('1' + Rank);
        return $"{f}{r}";
    }

    public override string ToString() => ToAlgebraic();

    public static implicit operator int(Square s) => s.Index;
    public static implicit operator Square(int index) => new(index);
}

public readonly record struct Piece(PieceType Type, PieceColor Color)
{
    public static readonly Piece None = new(PieceType.None, PieceColor.White);

    public bool IsEmpty => Type == PieceType.None;

    public int Value => Type switch
    {
        PieceType.Pawn => 100,
        PieceType.Knight => 320,
        PieceType.Bishop => 330,
        PieceType.Rook => 500,
        PieceType.Queen => 900,
        PieceType.King => 20000,
        _ => 0
    };

    public char ToChar()
    {
        char c = Type switch
        {
            PieceType.Pawn => 'P',
            PieceType.Knight => 'N',
            PieceType.Bishop => 'B',
            PieceType.Rook => 'R',
            PieceType.Queen => 'Q',
            PieceType.King => 'K',
            _ => '.'
        };
        return Color == PieceColor.Black ? char.ToLowerInvariant(c) : c;
    }

    public static Piece FromChar(char c)
    {
        PieceColor color = char.IsUpper(c) ? PieceColor.White : PieceColor.Black;
        PieceType type = char.ToUpperInvariant(c) switch
        {
            'P' => PieceType.Pawn,
            'N' => PieceType.Knight,
            'B' => PieceType.Bishop,
            'R' => PieceType.Rook,
            'Q' => PieceType.Queen,
            'K' => PieceType.King,
            _ => PieceType.None
        };
        return type == PieceType.None ? None : new Piece(type, color);
    }

    public override string ToString() => IsEmpty ? "." : $"{Color} {Type}";
}
