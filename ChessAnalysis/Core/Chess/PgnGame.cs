namespace ChessAnalysis.Core.Chess;

public class PgnMove
{
    public int MoveNumber { get; set; }
    public PieceColor Color { get; set; }
    public string San { get; set; } = string.Empty;
    public Move Move { get; set; }
    public string FenBefore { get; set; } = string.Empty;
    public string FenAfter { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public string Clock { get; set; } = string.Empty;

    public override string ToString() => Color == PieceColor.White ? $"{MoveNumber}. {San}" : $"{MoveNumber}... {San}";
}

public class PgnGame
{
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<PgnMove> Moves { get; } = new();

    public string White => Headers.GetValueOrDefault("White", "Белые");
    public string Black => Headers.GetValueOrDefault("Black", "Черные");
    public string WhiteElo => Headers.GetValueOrDefault("WhiteElo", "");
    public string BlackElo => Headers.GetValueOrDefault("BlackElo", "");
    public string Result => Headers.GetValueOrDefault("Result", "*");
    public string Date => Headers.GetValueOrDefault("Date", "");
    public string Event => Headers.GetValueOrDefault("Event", "");
    public string Site => Headers.GetValueOrDefault("Site", "");
    public string TimeControl => Headers.GetValueOrDefault("TimeControl", "");
    public string Eco => Headers.GetValueOrDefault("ECO", "");

    public string Title => $"{White} ({WhiteElo}) vs {Black} ({BlackElo}) - {Result}";
}
