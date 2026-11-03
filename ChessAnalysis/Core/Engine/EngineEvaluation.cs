namespace ChessAnalysis.Core.Engine;

public class EngineEvaluation
{
    public int Depth { get; set; }
    public int Centipawns { get; set; } // Relative to White: positive = White is winning, negative = Black is winning
    public int? MateInMoves { get; set; } // +3 = White mates in 3, -2 = Black mates in 2
    public string BestMoveUci { get; set; } = string.Empty;
    public string BestMoveSan { get; set; } = string.Empty;
    public List<string> Pv { get; } = new();

    // MultiPV lines collection (1..5)
    public List<EngineLine> Lines { get; } = new();

    // MultiPV support to see difference between 1st and 2nd best move
    public int SecondBestCentipawns { get; set; }
    public int? SecondBestMateIn { get; set; }
    public string SecondBestMoveUci { get; set; } = string.Empty;
    private bool _hasSecondBest;
    public bool HasSecondBest
    {
        get => _hasSecondBest || !string.IsNullOrEmpty(SecondBestMoveUci) || Lines.Count >= 2;
        set => _hasSecondBest = value;
    }

    public bool IsMate => MateInMoves.HasValue;

    public string ScoreDisplay
    {
        get
        {
            if (MateInMoves.HasValue)
            {
                int m = MateInMoves.Value;
                if (m == 0) return Centipawns > 0 ? "#" : "-#";
                return m > 0 ? $"M{m}" : $"-M{Math.Abs(m)}";
            }
            double pawn = Centipawns / 100.0;
            if (Math.Abs(pawn) < 0.005) return "0.00";
            return pawn >= 0
                ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"+{pawn:0.00}")
                : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{pawn:0.00}");
        }
    }

    /// <summary>
    /// Converts Centipawns / Mate to White's Win Probability percentage (0.0 to 100.0%)
    /// Official Chess.com formula: P(win) = 100 / (1 + e^(-0.00368208 * cp))
    /// </summary>
    public double WinProbabilityWhite
    {
        get
        {
            if (MateInMoves.HasValue)
            {
                if (MateInMoves.Value == 0) return Centipawns > 0 ? 100.0 : 0.0;
                return MateInMoves.Value > 0 ? 100.0 : 0.0;
            }
            // Official Chess.com logistic regression model (capped at +/-10 pawns)
            double clampedCp = Math.Clamp((double)Centipawns, -1000.0, 1000.0);
            return 100.0 / (1.0 + Math.Exp(-0.00368208 * clampedCp));
        }
    }
}

public class EngineLine
{
    public int MultiPv { get; set; } = 1;
    public int Centipawns { get; set; }
    public int? MateInMoves { get; set; }
    public List<string> Pv { get; set; } = new();
    public string BestMoveUci => Pv.Count > 0 ? Pv[0] : string.Empty;
}
