using ChessAnalysis.Core.Chess;
using ChessAnalysis.Core.Engine;

namespace ChessAnalysis.Core.Analysis;

public class MoveAnalysis
{
    public int PlyIndex { get; set; }
    public int MoveNumber { get; set; }
    public PieceColor Color { get; set; }
    public string San { get; set; } = string.Empty;
    public Move Move { get; set; }
    public string FenBefore { get; set; } = string.Empty;
    public string FenAfter { get; set; } = string.Empty;

    public MoveClassification Classification { get; set; } = MoveClassification.Good;
    public double WinProbLoss { get; set; }
    public double MoveAccuracy { get; set; } = 100.0;

    public EngineEvaluation PreEval { get; set; } = new();
    public EngineEvaluation PostEval { get; set; } = new();

    public string BestMoveSan { get; set; } = string.Empty;
    public string BestMoveUci { get; set; } = string.Empty;
    public Move? BestMove { get; set; }
    public Move? ThreatMove { get; set; }
    public string OpponentThreatSan { get; set; } = string.Empty;
    public string ThreatSan => OpponentThreatSan;
    public string CoachComment { get; set; } = string.Empty;
    public string CoachCommentary => CoachComment;

    public string ScoreDisplay => PostEval.ScoreDisplay;
}

public class GamePhaseAnalysis
{
    public MoveClassification OpeningPerformance { get; set; } = MoveClassification.Good;
    public double OpeningAccuracy { get; set; } = 100.0;

    public MoveClassification MiddlegamePerformance { get; set; } = MoveClassification.Good;
    public double MiddlegameAccuracy { get; set; } = 100.0;

    public MoveClassification? EndgamePerformance { get; set; } // null if not reached ('-')
    public double? EndgameAccuracy { get; set; }
}

public class GameAnalysis
{
    public PgnGame Game { get; set; } = new();
    public List<MoveAnalysis> Moves { get; } = [];

    public double WhiteAccuracy { get; set; }
    public double BlackAccuracy { get; set; }

    public int WhiteEstimatedElo { get; set; } = 800;
    public int BlackEstimatedElo { get; set; } = 800;

    public GamePhaseAnalysis WhitePhases { get; set; } = new();
    public GamePhaseAnalysis BlackPhases { get; set; } = new();

    public Dictionary<MoveClassification, int> WhiteBadgeCounts { get; } = [];
    public Dictionary<MoveClassification, int> BlackBadgeCounts { get; } = [];

    public GameAnalysis()
    {
        foreach (MoveClassification c in Enum.GetValues<MoveClassification>())
        {
            WhiteBadgeCounts[c] = 0;
            BlackBadgeCounts[c] = 0;
        }
    }
}
