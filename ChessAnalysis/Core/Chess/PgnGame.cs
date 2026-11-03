using ChessAnalysis.Core.Analysis;

namespace ChessAnalysis.Core.Chess;

public class PgnMove
{
    public int MoveNumber { get; set; }
    public PieceColor Color { get; set; }
    public bool IsWhite => Color == PieceColor.White;
    public string San { get; set; } = string.Empty;
    public Move Move { get; set; }
    public string FenBefore { get; set; } = string.Empty;
    public string FenAfter { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public string Clock { get; set; } = string.Empty;
    public MoveClassification? ExpectedClassification { get; set; }
    public string? AnnotationTag { get; set; }

    public override string ToString() => Color == PieceColor.White ? $"{MoveNumber}. {San}" : $"{MoveNumber}... {San}";
}

public class PgnGame
{
    public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<PgnMove> Moves { get; } = new();
    public string RawPgn { get; set; } = string.Empty;

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
    public string Opening => Headers.GetValueOrDefault("Opening", "");

    public string Title => $"{White} ({WhiteElo}) vs {Black} ({BlackElo}) - {Result}";

    public string ToPgn(GameAnalysis? analysis = null)
    {
        var sb = new System.Text.StringBuilder();

        var headers = new Dictionary<string, string>(Headers, StringComparer.OrdinalIgnoreCase);
        if (!headers.ContainsKey("Event")) headers["Event"] = "Casual Game";
        if (!headers.ContainsKey("Site")) headers["Site"] = "ChessAnalysis";
        if (!headers.ContainsKey("Date")) headers["Date"] = DateTime.UtcNow.ToString("yyyy.MM.dd");
        if (!headers.ContainsKey("White")) headers["White"] = White;
        if (!headers.ContainsKey("Black")) headers["Black"] = Black;
        if (!headers.ContainsKey("Result")) headers["Result"] = Result;

        foreach (var (k, v) in headers)
        {
            sb.AppendLine($"[{k} \"{v}\"]");
        }
        sb.AppendLine();

        int lineLen = 0;
        for (int i = 0; i < Moves.Count; i++)
        {
            var m = Moves[i];
            string glyph = "";
            if (analysis != null && i < analysis.Moves.Count)
            {
                glyph = analysis.Moves[i].Classification.GetSymbol();
            }

            string moveStr = m.IsWhite ? $"{m.MoveNumber}. {m.San}{glyph}" : $"{m.San}{glyph}";

            if (lineLen + moveStr.Length + 1 > 80)
            {
                sb.AppendLine();
                lineLen = 0;
            }
            else if (lineLen > 0)
            {
                sb.Append(' ');
                lineLen++;
            }

            sb.Append(moveStr);
            lineLen += moveStr.Length;
        }

        if (Moves.Count > 0)
        {
            string res = Result;
            if (lineLen + res.Length + 1 > 80)
            {
                sb.AppendLine();
                sb.Append(res);
            }
            else
            {
                sb.Append(' ');
                sb.Append(res);
            }
        }
        sb.AppendLine();

        return sb.ToString();
    }
}
