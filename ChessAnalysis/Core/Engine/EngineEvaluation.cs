namespace ChessAnalysis.Core.Engine;

public class EngineEvaluation
{
    public int Depth { get; set; }
    public int Centipawns { get; set; } // Relative to White: positive = White is winning, negative = Black is winning
    public int? MateInMoves { get; set; } // +3 = White mates in 3, -2 = Black mates in 2
    public string BestMoveUci { get; set; } = string.Empty;
    public string BestMoveSan { get; set; } = string.Empty;
    public List<string> Pv { get; } = new();

    // MultiPV support to see difference between 1st and 2nd best move
    public int SecondBestCentipawns { get; set; }
    public int? SecondBestMateIn { get; set; }
    public string SecondBestMoveUci { get; set; } = string.Empty;
    public bool HasSecondBest { get; set; }

    public bool IsMate => MateInMoves.HasValue;

    public string ScoreDisplay
    {
        get
        {
            if (MateInMoves.HasValue)
            {
                int m = MateInMoves.Value;
                if (m == 0) return Centipawns >= 0 ? "#" : "-#";
                return m > 0 ? $"M{m}" : $"-M{Math.Abs(m)}";
            }
            double pawn = Centipawns / 100.0;
            return pawn >= 0 ? $"+{pawn:0.00}" : $"{pawn:0.00}";
        }
    }

    /// <summary>
    /// Converts Centipawns / Mate to White's Win Probability percentage (0.0 to 100.0%)
    /// Formula: P(win) = 100 / (1 + 10^(-cp / 400))
    /// </summary>
    public double WinProbabilityWhite
    {
        get
        {
            if (MateInMoves.HasValue)
            {
                if (MateInMoves.Value == 0) return Centipawns >= 0 ? 100.0 : 0.0;
                return MateInMoves.Value > 0 ? 100.0 : 0.0;
            }
            // Sigmoid win probability model
            return 100.0 / (1.0 + Math.Pow(10.0, -Centipawns / 400.0));
        }
    }
}
