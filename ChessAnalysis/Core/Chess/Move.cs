namespace ChessAnalysis.Core.Chess;

public readonly record struct Move(
    Square From,
    Square To,
    PieceType Promotion = PieceType.None,
    bool IsCapture = false,
    bool IsCastling = false,
    bool IsEnPassant = false)
{
    public static readonly Move Empty = new(Square.None, Square.None);

    public bool IsEmpty => !From.IsValid || !To.IsValid;

    public string ToUci()
    {
        if (IsEmpty) return "0000";
        string promo = Promotion switch
        {
            PieceType.Queen => "q",
            PieceType.Rook => "r",
            PieceType.Bishop => "b",
            PieceType.Knight => "n",
            _ => ""
        };
        return $"{From.ToAlgebraic()}{To.ToAlgebraic()}{promo}";
    }

    public static Move FromUci(string uci)
    {
        if (string.IsNullOrWhiteSpace(uci) || uci.Length < 4) return Empty;
        var from = Square.FromAlgebraic(uci[..2]);
        var to = Square.FromAlgebraic(uci.Substring(2, 2));
        var promo = PieceType.None;
        if (uci.Length >= 5)
        {
            promo = char.ToLowerInvariant(uci[4]) switch
            {
                'q' => PieceType.Queen,
                'r' => PieceType.Rook,
                'b' => PieceType.Bishop,
                'n' => PieceType.Knight,
                _ => PieceType.None
            };
        }
        return new Move(from, to, promo);
    }

    public override string ToString() => ToUci();
}
