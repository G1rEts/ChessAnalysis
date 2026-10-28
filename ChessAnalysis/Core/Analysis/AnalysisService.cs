using ChessAnalysis.Core.Chess;
using ChessAnalysis.Core.Engine;

namespace ChessAnalysis.Core.Analysis;

public class AnalysisProgress
{
    public int CurrentPly { get; set; }
    public int TotalPlies { get; set; }
    public string CurrentMoveSan { get; set; } = string.Empty;
    public double Percent => TotalPlies > 0 ? (double)CurrentPly / TotalPlies * 100.0 : 0.0;
    public MoveAnalysis? LatestAnalyzedMove { get; set; }
}

public class AnalysisService
{
    private readonly StockfishEngine _engine;

    public AnalysisService(StockfishEngine engine)
    {
        _engine = engine;
    }

    public async Task<GameAnalysis> AnalyzeGameAsync(
        PgnGame game,
        int depth = 14,
        IProgress<AnalysisProgress>? progress = null,
        CancellationToken ct = default)
    {
        var gameAnalysis = new GameAnalysis { Game = game };
        if (game.Moves.Count == 0) return gameAnalysis;

        await _engine.NewGameAsync();

        EngineEvaluation? cachedPreEval = null;

        for (int i = 0; i < game.Moves.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var pgnMove = game.Moves[i];

            // 1. Evaluate Pre-Move Position
            EngineEvaluation preEval;
            if (cachedPreEval != null)
            {
                preEval = cachedPreEval;
            }
            else
            {
                preEval = await _engine.AnalyzePositionAsync(pgnMove.FenBefore, depth: null, nodes: 600000, ct: ct);
            }

            // 2. Evaluate Post-Move Position (reused for next move)
            var postEval = await _engine.AnalyzePositionAsync(pgnMove.FenAfter, depth: null, nodes: 600000, ct: ct);
            cachedPreEval = postEval; // Can reuse for next move's FenBefore

            // 3. Compute Win Probability Loss (Delta P)
            double preWinWhite = preEval.WinProbabilityWhite;
            double postWinWhite = postEval.WinProbabilityWhite;

            double deltaP;
            if (pgnMove.Color == PieceColor.White)
            {
                // Loss for White
                deltaP = Math.Max(0.0, preWinWhite - postWinWhite);
            }
            else
            {
                // Loss for Black
                double preWinBlack = 100.0 - preWinWhite;
                double postWinBlack = 100.0 - postWinWhite;
                deltaP = Math.Max(0.0, preWinBlack - postWinBlack);
            }

            // 4. CAPS Accuracy Formula
            double accuracy = 100.0;
            if (deltaP > 0.01)
            {
                accuracy = Math.Clamp(103.1668 * Math.Exp(-0.04354 * deltaP) - 3.1669, 0.0, 100.0);
            }

            // 5. Best Move & Opponent Threat Move
            string bestMoveSan = ConvertUciToSan(pgnMove.FenBefore, preEval.BestMoveUci);
            var bestMove = Move.FromUci(preEval.BestMoveUci);

            string threatSan = ConvertUciToSan(pgnMove.FenAfter, postEval.BestMoveUci);
            var threatMove = Move.FromUci(postEval.BestMoveUci);

            // 6. Check if position had only 1 legal move (Forced)
            var boardBefore = new BoardState();
            boardBefore.LoadFen(pgnMove.FenBefore);
            int legalCount = boardBefore.GenerateLegalMoves().Count;

            // 7. Move Classification
            var classification = ClassifyMove(pgnMove, preEval, postEval, deltaP, legalCount);

            // 8. Opening Name if in book
            string? openingName = OpeningBook.GetOpeningName(pgnMove.FenAfter);

            // 9. Coach Commentary with tactical reason
            string comment = GenerateCommentary(classification, pgnMove.San, bestMoveSan, threatSan, deltaP, openingName);

            var moveAnalysis = new MoveAnalysis
            {
                PlyIndex = i,
                MoveNumber = pgnMove.MoveNumber,
                Color = pgnMove.Color,
                San = pgnMove.San,
                Move = pgnMove.Move,
                FenBefore = pgnMove.FenBefore,
                FenAfter = pgnMove.FenAfter,
                Classification = classification,
                WinProbLoss = deltaP,
                MoveAccuracy = accuracy,
                PreEval = preEval,
                PostEval = postEval,
                BestMoveUci = preEval.BestMoveUci,
                BestMoveSan = bestMoveSan,
                BestMove = bestMove.IsEmpty ? null : bestMove,
                ThreatMove = threatMove.IsEmpty ? null : threatMove,
                OpponentThreatSan = threatSan,
                CoachComment = comment
            };

            gameAnalysis.Moves.Add(moveAnalysis);

            // Update badge counts
            if (pgnMove.Color == PieceColor.White)
                gameAnalysis.WhiteBadgeCounts[classification]++;
            else
                gameAnalysis.BlackBadgeCounts[classification]++;

            // Report progress
            progress?.Report(new AnalysisProgress
            {
                CurrentPly = i + 1,
                TotalPlies = game.Moves.Count,
                CurrentMoveSan = pgnMove.San,
                LatestAnalyzedMove = moveAnalysis
            });
        }

        // Calculate overall accuracy with blunder/mistake weighting matching Chess.com CAPS
        var whiteMoves = gameAnalysis.Moves.Where(m => m.Color == PieceColor.White).ToList();
        var blackMoves = gameAnalysis.Moves.Where(m => m.Color == PieceColor.Black).ToList();

        gameAnalysis.WhiteAccuracy = CalculatePlayerAccuracy(whiteMoves);
        gameAnalysis.BlackAccuracy = CalculatePlayerAccuracy(blackMoves);

        int whiteBlunders = whiteMoves.Count(m => m.Classification is MoveClassification.Blunder or MoveClassification.MissedWin);
        int whiteMistakes = whiteMoves.Count(m => m.Classification == MoveClassification.Mistake);
        int blackBlunders = blackMoves.Count(m => m.Classification is MoveClassification.Blunder or MoveClassification.MissedWin);
        int blackMistakes = blackMoves.Count(m => m.Classification == MoveClassification.Mistake);

        gameAnalysis.WhiteEstimatedElo = EstimatePerformanceElo(gameAnalysis.WhiteAccuracy, whiteBlunders, whiteMistakes);
        gameAnalysis.BlackEstimatedElo = EstimatePerformanceElo(gameAnalysis.BlackAccuracy, blackBlunders, blackMistakes);

        return gameAnalysis;
    }

    private static double CalculatePlayerAccuracy(List<MoveAnalysis> moves)
    {
        if (moves.Count == 0) return 100.0;
        double baseAcc = moves.Average(m => m.MoveAccuracy);
        int blunders = moves.Count(m => m.Classification is MoveClassification.Blunder or MoveClassification.MissedWin);
        int mistakes = moves.Count(m => m.Classification == MoveClassification.Mistake);

        double penalty = ((blunders * 5.5) + (mistakes * 2.2)) / moves.Count * 22.0;
        return Math.Round(Math.Clamp(baseAcc - penalty, 15.0, 99.8), 1);
    }

    public static int EstimatePerformanceElo(double accuracy, int blunders, int mistakes)
    {
        // Calibrated to Chess.com: 56.1% -> ~350, 67.3% -> ~850, 85% -> ~1500, 95% -> ~2100
        double elo = (accuracy - 48.0) * 44.0;
        elo -= (blunders * 40.0) + (mistakes * 15.0);
        int rounded = (int)(Math.Round(elo / 50.0) * 50);
        return Math.Clamp(rounded, 150, 2850);
    }

    private static MoveClassification ClassifyMove(
        PgnMove pgnMove,
        EngineEvaluation preEval,
        EngineEvaluation postEval,
        double deltaP,
        int legalCount)
    {
        // 0. Checkmate delivered!
        if (pgnMove.San.Contains('#') || (postEval.MateInMoves.HasValue && postEval.MateInMoves.Value == 0))
        {
            return MoveClassification.Best;
        }

        // 1. Forced move: only 1 legal move in position
        if (legalCount == 1)
        {
            return MoveClassification.Forced;
        }

        string playedUci = pgnMove.Move.ToUci();
        bool isTopEngineMove = string.Equals(playedUci, preEval.BestMoveUci, StringComparison.OrdinalIgnoreCase);

        // 2. Opening Book
        if (pgnMove.MoveNumber <= 15 && OpeningBook.IsBookPosition(pgnMove.FenAfter))
        {
            return MoveClassification.Book;
        }

        // 3. Missed Win: Player had decisive win (>80% win prob or >= +2.5 / mate), but threw it away (loss >= 18%)
        double playerPreWinProb = pgnMove.Color == PieceColor.White ? preEval.WinProbabilityWhite : (100.0 - preEval.WinProbabilityWhite);
        double playerPostWinProb = pgnMove.Color == PieceColor.White ? postEval.WinProbabilityWhite : (100.0 - postEval.WinProbabilityWhite);
        if (playerPreWinProb >= 80.0 && deltaP >= 18.0 && playerPostWinProb <= 65.0)
        {
            return MoveClassification.MissedWin;
        }

        double evalForPlayer = pgnMove.Color == PieceColor.White ? postEval.Centipawns : -postEval.Centipawns;
        double preEvalForPlayer = pgnMove.Color == PieceColor.White ? preEval.Centipawns : -preEval.Centipawns;

        // 4. Sacrifice classification (Brilliant vs Great)
        // Per Arslanov Chess:
        // "Замечательный (!): временная жертва — материал сразу отыгрывается — или верная жертва, которую трудность не сделала блестящей."
        // "Блестящий (!!): верная жертва материала, которая не отыгрывается немедленно и сохраняет позиционный/тактический перевес."
        if (isTopEngineMove && preEvalForPlayer <= 500 && (evalForPlayer >= 100 || (postEval.MateInMoves.HasValue && postEval.MateInMoves.Value > 0)))
        {
            if (IsSacrifice(pgnMove, out bool isTemporarySacrifice))
            {
                if (isTemporarySacrifice)
                {
                    // Material is immediately won back or tactical exchange (-3 +5) -> Great (!)
                    return MoveClassification.Great;
                }
                else
                {
                    // Real piece sacrifice maintaining advantage -> Brilliant (!!)
                    return MoveClassification.Brilliant;
                }
            }
        }

        // 5. Great Move (🎯):
        // Only winning or only saving move in a critical position (second best move is noticeably worse)
        if (isTopEngineMove && preEval.HasSecondBest)
        {
            int scoreDiff = pgnMove.Color == PieceColor.White
                ? (preEval.Centipawns - preEval.SecondBestCentipawns)
                : (-preEval.Centipawns - (-preEval.SecondBestCentipawns));

            double secondBestEvalForPlayer = pgnMove.Color == PieceColor.White ? preEval.SecondBestCentipawns : -preEval.SecondBestCentipawns;
            bool secondBestFails = secondBestEvalForPlayer <= 150 || (scoreDiff >= 200 && preEvalForPlayer <= 500);

            if (secondBestFails && (scoreDiff >= 150 || (preEval.MateInMoves.HasValue && !preEval.SecondBestMateIn.HasValue)))
            {
                return MoveClassification.Great;
            }
        }

        // 6. Standard thresholds based on win probability loss (Delta P) per Arslanov Chess:
        if (isTopEngineMove || deltaP < 1.0)
            return MoveClassification.Best;

        if (deltaP <= 5.0)
            return MoveClassification.Excellent;

        if (deltaP <= 10.0)
            return MoveClassification.Good;

        if (deltaP <= 20.0)
            return MoveClassification.Inaccuracy;

        if (deltaP <= 30.0)
            return MoveClassification.Mistake;

        return MoveClassification.Blunder;
    }

    private static bool IsSacrifice(PgnMove pgnMove, out bool isTemporarySacrifice)
    {
        isTemporarySacrifice = false;

        var boardBefore = new BoardState();
        boardBefore.LoadFen(pgnMove.FenBefore);

        var movingColor = pgnMove.Color;
        var enemyColor = movingColor == PieceColor.White ? PieceColor.Black : PieceColor.White;

        bool wasInCheck = boardBefore.IsInCheck(movingColor);
        var movedPiece = boardBefore[pgnMove.Move.From];

        // Only major or minor pieces (Knight, Bishop, Rook, Queen) can be sacrificed
        if (movedPiece.Type is PieceType.Pawn or PieceType.King or PieceType.None)
            return false;

        int movedPieceVal = movedPiece.Value;

        // If the move is a capture:
        if (pgnMove.Move.IsCapture)
        {
            var capturedPiece = boardBefore[pgnMove.Move.To];
            // If we captured an equal or more valuable piece (e.g. Knight takes Knight/Rook), this is an equal trade or winning capture, NOT a sacrifice!
            if (capturedPiece.Value >= movedPieceVal - 50)
            {
                return false;
            }

            // Exchange sacrifice (e.g. Rook takes Bishop/Knight, or Piece takes Pawn):
            if (movedPieceVal - capturedPiece.Value >= 180)
            {
                // If it's a minor piece taking a pawn (e.g. Bxf7+ Kxf7 Ng5+), it's a temporary tactical sacrifice
                if (movedPiece.Type is PieceType.Bishop or PieceType.Knight && capturedPiece.Type == PieceType.Pawn)
                {
                    isTemporarySacrifice = true;
                }
                return true;
            }
            return false;
        }

        var boardAfter = new BoardState();
        boardAfter.LoadFen(pgnMove.FenBefore);
        boardAfter.ApplyMove(pgnMove.Move);

        // Check if destination square is attacked by enemy
        bool destAttackedByEnemy = boardAfter.IsSquareAttacked(pgnMove.Move.To, enemyColor);
        if (!destAttackedByEnemy)
            return false;

        bool destDefendedByFriendly = boardAfter.IsSquareAttacked(pgnMove.Move.To, movingColor);

        // Interposing a defended piece while in check is basic defense, not a sacrifice
        if (wasInCheck && destDefendedByFriendly)
            return false;

        int lowestEnemyAttacker = boardAfter.GetLowestAttackerValue(pgnMove.Move.To, enemyColor);

        // If the enemy can capture our piece with a lesser piece (e.g. Pawn captures Knight, or Minor captures Queen/Rook)
        if (lowestEnemyAttacker < movedPieceVal)
        {
            // If the piece is defended and will be recaptured immediately, check if it's a temporary sacrifice
            if (destDefendedByFriendly)
            {
                isTemporarySacrifice = true;
            }
            return true;
        }

        // If left completely hanging (undefended)
        if (!destDefendedByFriendly)
        {
            return true;
        }

        return false;
    }

    public static string ConvertUciToSan(string fen, string uci)
    {
        if (string.IsNullOrWhiteSpace(uci)) return string.Empty;
        var board = new BoardState();
        board.LoadFen(fen);
        var move = Move.FromUci(uci);
        if (move.IsEmpty) return uci;

        var p = board[move.From];
        if (p.Type == PieceType.King)
        {
            if (move.From == Square.FromAlgebraic("e1") && move.To == Square.FromAlgebraic("g1")) return "O-O";
            if (move.From == Square.FromAlgebraic("e1") && move.To == Square.FromAlgebraic("c1")) return "O-O-O";
            if (move.From == Square.FromAlgebraic("e8") && move.To == Square.FromAlgebraic("g8")) return "O-O";
            if (move.From == Square.FromAlgebraic("e8") && move.To == Square.FromAlgebraic("c8")) return "O-O-O";
        }

        // Try to generate simple SAN notation
        string prefix = p.Type switch
        {
            PieceType.Knight => "N",
            PieceType.Bishop => "B",
            PieceType.Rook => "R",
            PieceType.Queen => "Q",
            PieceType.King => "K",
            _ => ""
        };

        if (p.Type == PieceType.Pawn && move.From.File != move.To.File)
        {
            prefix = ((char)('a' + move.From.File)).ToString();
        }

        string capture = !board[move.To].IsEmpty || move.To == board.EnPassantSquare ? "x" : "";
        string dest = move.To.ToAlgebraic();

        string promo = move.Promotion switch
        {
            PieceType.Queen => "=Q",
            PieceType.Rook => "=R",
            PieceType.Bishop => "=B",
            PieceType.Knight => "=N",
            _ => ""
        };

        return $"{prefix}{capture}{dest}{promo}";
    }

    private static string GenerateCommentary(
        MoveClassification c,
        string playedSan,
        string bestMoveSan,
        string threatSan,
        double deltaP,
        string? openingName = null) => c switch
    {
        MoveClassification.Brilliant => string.IsNullOrEmpty(threatSan)
            ? "Блестящий ход! Жертва материала ведет к решающей атаке."
            : $"Блестящий ход! Жертва материала. На возможный ответ соперника {threatSan} у вас есть победное продолжение.",

        MoveClassification.Great =>
            "Замечательный ход! Единственное сильное спасающее или выигрывающее продолжение в острой позиции.",

        MoveClassification.Best => playedSan.Contains('#')
            ? $"Мат! Партия завершена победой ({playedSan})."
            : $"Лучший ход {playedSan}! Компьютер одобряет этот ход как сильнейший вариант продолжения.",

        MoveClassification.Excellent =>
            $"Отличный ход! Ход {playedSan} сохраняет позиционный перевес и практически равен первой линии.",

        MoveClassification.Good =>
            $"Хороший ход. Надежное и прочное продолжение.",

        MoveClassification.Book => string.IsNullOrEmpty(openingName)
            ? $"Теоретический ход ({playedSan}). Развитие фигур по дебютной теории."
            : $"Теоретический ход ({playedSan}) в дебюте: {openingName}.",

        MoveClassification.Forced =>
            $"Вынужденный ход ({playedSan}). Единственное возможное легальное продолжение в данной позиции.",

        MoveClassification.Inaccuracy => string.IsNullOrEmpty(bestMoveSan)
            ? "Небольшая неточность."
            : (string.IsNullOrEmpty(threatSan)
                ? $"Неточность. Сильнее было сыграть {bestMoveSan}."
                : $"Неточность. Лучше было сыграть {bestMoveSan}, так как ход {playedSan} позволяет сопернику ответить {threatSan}."),

        MoveClassification.Mistake => string.IsNullOrEmpty(bestMoveSan)
            ? "Заметная ошибка."
            : (string.IsNullOrEmpty(threatSan)
                ? $"Ошибка. Сильнее было сыграть {bestMoveSan}."
                : $"Ошибка! Ход {playedSan} отдает инициативу. Соперник может ответить {threatSan}. Следовало сыграть {bestMoveSan}."),

        MoveClassification.Blunder => string.IsNullOrEmpty(bestMoveSan)
            ? "Грубый зевок!"
            : (string.IsNullOrEmpty(threatSan)
                ? $"Грубый зевок! Вы теряете перевес. Следовало сыграть {bestMoveSan}."
                : $"Грубый зевок! Ход {playedSan} ведет к тяжелым потерям: соперник отвечает {threatSan}. Сильнейшим ходом было {bestMoveSan}."),

        MoveClassification.MissedWin => string.IsNullOrEmpty(bestMoveSan)
            ? "Упущен решающий выигрыш!"
            : $"Упущен выигрыш! В позиции был решающий перевес, который приносил ход {bestMoveSan}.",

        _ => "Сделан ход."
    };
}
