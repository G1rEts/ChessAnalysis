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

public class AnalysisService(StockfishEngine engine)
{
    private readonly StockfishEngine _engine = engine;

    public async Task<GameAnalysis> AnalyzeGameAsync(
        PgnGame game,
        int? depth = null,
        long? nodes = 400000,
        IProgress<AnalysisProgress>? progress = null,
        CancellationToken ct = default)
    {
        var gameAnalysis = new GameAnalysis { Game = game };
        if (game.Moves.Count == 0) return gameAnalysis;

        int? wRating = int.TryParse(game.WhiteElo, out int wr) && wr > 0 ? wr : null;
        int? bRating = int.TryParse(game.BlackElo, out int br) && br > 0 ? br : null;

        await _engine.NewGameAsync();

        EngineEvaluation? cachedPreEval = null;

        for (int i = 0; i < game.Moves.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            var pgnMove = game.Moves[i];

            if (string.IsNullOrWhiteSpace(pgnMove.FenBefore))
            {
                pgnMove.FenBefore = BoardState.StartingFen;
            }

            // 1. Evaluate Pre-Move Position
            EngineEvaluation preEval;
            if (cachedPreEval != null)
            {
                preEval = cachedPreEval;
            }
            else
            {
                preEval = await _engine.AnalyzePositionAsync(pgnMove.FenBefore, depth: depth, nodes: nodes, ct: ct);
            }

            // Guarantee FenAfter is computed if missing
            if (string.IsNullOrWhiteSpace(pgnMove.FenAfter))
            {
                var tempB = new BoardState();
                tempB.LoadFen(pgnMove.FenBefore);
                tempB.ApplyMove(pgnMove.Move);
                pgnMove.FenAfter = tempB.ToFen();
            }

            // 2. Evaluate Post-Move Position (reused for next move)
            var postEval = await _engine.AnalyzePositionAsync(pgnMove.FenAfter, depth: depth, nodes: nodes, ct: ct);

            // If the move delivered checkmate, guarantee exact mate 0 evaluation and winning sign
            var postBoard = new BoardState();
            postBoard.LoadFen(pgnMove.FenAfter);
            bool isDeliveredMate = (postBoard.IsInCheck(postBoard.SideToMove) && postBoard.GenerateLegalMoves().Count == 0)
                || pgnMove.San.EndsWith('#') || pgnMove.San.EndsWith('‡');
            if (isDeliveredMate)
            {
                postEval.MateInMoves = 0;
                postEval.Centipawns = pgnMove.Color == PieceColor.White ? 100000 : -100000;
            }

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

            // 4. Best Move & Opponent Threat Move
            string bestMoveSan = ConvertUciToSan(pgnMove.FenBefore, preEval.BestMoveUci);
            var bestMove = Move.FromUci(preEval.BestMoveUci);

            string threatSan = ConvertUciToSan(pgnMove.FenAfter, postEval.BestMoveUci);
            var threatMove = Move.FromUci(postEval.BestMoveUci);

            // 5. Check if position had only 1 legal move (Forced)
            var boardBefore = new BoardState();
            boardBefore.LoadFen(pgnMove.FenBefore);
            int legalCount = boardBefore.GenerateLegalMoves().Count;

            // 6. Move Classification (с приоритетом аннотации из PGN)
            int? playerRating = pgnMove.Color == PieceColor.White ? wRating : bRating;
            var classification = pgnMove.ExpectedClassification ?? ClassifyMove(pgnMove, preEval, postEval, deltaP, legalCount, playerRating);

            // 7. CAPS Accuracy Formula с калибровкой по классификации хода
            double accuracy = 100.0;
            if (pgnMove.ExpectedClassification.HasValue)
            {
                accuracy = classification switch
                {
                    MoveClassification.Brilliant or MoveClassification.Great or MoveClassification.Best or MoveClassification.Book or MoveClassification.Forced => 100.0,
                    MoveClassification.Excellent => 95.0,
                    MoveClassification.Good => 85.0,
                    MoveClassification.Inaccuracy => 68.0,
                    MoveClassification.Mistake => 48.0,
                    MoveClassification.MissedWin => 28.0,
                    MoveClassification.Blunder => 18.0,
                    _ => 100.0
                };
            }
            else
            {
                if (deltaP > 0.01)
                {
                    accuracy = Math.Clamp(103.1668 * Math.Exp(-0.04354 * deltaP) - 3.1669, 10.0, 100.0);
                }

                // Ограничиваем точность ходов по их качеству, чтобы зевки в выигранных/проигранных позициях не получали 80-99%
                accuracy = classification switch
                {
                    MoveClassification.Brilliant or MoveClassification.Great or MoveClassification.Best or MoveClassification.Book or MoveClassification.Forced => Math.Max(accuracy, 97.0),
                    MoveClassification.Excellent => Math.Clamp(accuracy, 84.0, 94.0),
                    MoveClassification.Good => Math.Clamp(accuracy, 64.0, 82.0),
                    MoveClassification.Inaccuracy => Math.Clamp(accuracy, 35.0, 64.0),
                    MoveClassification.Mistake => Math.Clamp(accuracy, 18.0, 44.0),
                    MoveClassification.MissedWin => Math.Clamp(accuracy, 12.0, 25.0),
                    MoveClassification.Blunder => Math.Clamp(accuracy, 2.0, 15.0),
                    _ => accuracy
                };
            }

            // 8. Opening Name if in book
            string? openingName = OpeningBook.GetOpeningName(pgnMove.FenAfter);

            // 9. Coach Commentary with tactical reason
            string comment = GenerateCommentary(classification, pgnMove.San, bestMoveSan, threatSan, openingName);

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

        int whiteBlunders = whiteMoves.Count(m => m.Classification == MoveClassification.Blunder);
        int whiteMistakes = whiteMoves.Count(m => m.Classification == MoveClassification.Mistake);
        int whiteMissedWins = whiteMoves.Count(m => m.Classification == MoveClassification.MissedWin);
        int whiteBrilliant = whiteMoves.Count(m => m.Classification == MoveClassification.Brilliant);
        int whiteGreat = whiteMoves.Count(m => m.Classification == MoveClassification.Great);

        int blackBlunders = blackMoves.Count(m => m.Classification == MoveClassification.Blunder);
        int blackMistakes = blackMoves.Count(m => m.Classification == MoveClassification.Mistake);
        int blackMissedWins = blackMoves.Count(m => m.Classification == MoveClassification.MissedWin);
        int blackBrilliant = blackMoves.Count(m => m.Classification == MoveClassification.Brilliant);
        int blackGreat = blackMoves.Count(m => m.Classification == MoveClassification.Great);

        bool whiteWon = game.Result == "1-0";
        bool blackWon = game.Result == "0-1";

        gameAnalysis.WhiteEstimatedElo = EstimatePerformanceElo(gameAnalysis.WhiteAccuracy, whiteBlunders, whiteMistakes, wRating, whiteBrilliant, whiteGreat, whiteWon, whiteMissedWins);
        gameAnalysis.BlackEstimatedElo = EstimatePerformanceElo(gameAnalysis.BlackAccuracy, blackBlunders, blackMistakes, bRating, blackBrilliant, blackGreat, blackWon, blackMissedWins);

        // Compute Phase Analysis (Opening, Middlegame, Endgame)
        GamePhasesService.CalculatePhases(gameAnalysis);

        return gameAnalysis;
    }

    public static void CalculateGamePhases(GameAnalysis gameAnalysis)
    {
        GamePhasesService.CalculatePhases(gameAnalysis);
    }

    private static double CalculatePlayerAccuracy(List<MoveAnalysis> moves)
    {
        if (moves.Count == 0) return 100.0;
        var nonBookMoves = moves.Where(m => m.Classification != MoveClassification.Book).ToList();
        var evalMoves = nonBookMoves.Count > 0 ? nonBookMoves : moves;

        double baseAcc = evalMoves.Average(m => m.MoveAccuracy);
        int blunders = evalMoves.Count(m => m.Classification is MoveClassification.Blunder or MoveClassification.MissedWin);
        int mistakes = evalMoves.Count(m => m.Classification == MoveClassification.Mistake);
        int inaccuracies = evalMoves.Count(m => m.Classification == MoveClassification.Inaccuracy);

        double penalty = ((blunders * 7.5) + (mistakes * 3.8) + (inaccuracies * 1.2)) / evalMoves.Count * 20.0;
        return Math.Round(Math.Clamp(baseAcc - penalty, 10.0, 99.8), 1);
    }

    public static int EstimatePerformanceElo(
        double accuracy,
        int blunders,
        int mistakes,
        int? playerRating = null,
        int brilliantCount = 0,
        int greatCount = 0,
        bool wonGame = false,
        int missedWins = 0)
    {
        // Базовый расчет по точности CAPS с умеренной калибровкой
        double baseElo;
        if (accuracy < 55.0)
        {
            // Диапазон новичка (200 - 500)
            baseElo = 200.0 + Math.Max(0.0, accuracy - 45.0) * 28.0;
        }
        else if (accuracy < 72.0)
        {
            // 55..72% точности: средний (480 -> 900)
            double t = (accuracy - 55.0) / 17.0;
            baseElo = 480.0 + t * 420.0;
        }
        else if (accuracy < 85.0)
        {
            // 72..85% точности: клубный/продвинутый (900 -> 1380)
            double t = (accuracy - 72.0) / 13.0;
            baseElo = 900.0 + t * 480.0;
        }
        else
        {
            // 85..100% точности: мастер / гроссмейстер (1380 -> 2550)
            double t = (accuracy - 85.0) / 15.0;
            baseElo = 1380.0 + t * 1100.0;
        }

        // Корректировка по ошибкам и блестящим ходам
        double baseMistakePenalty = mistakes * 15.0;
        if (mistakes >= 6)
        {
            baseMistakePenalty += (mistakes - 5) * 15.0;
        }

        double effectiveMissedPenalty = (blunders > 0 || mistakes > 0 || accuracy < 70.0)
            ? (missedWins * 25.0)
            : 0.0;

        double baseBlunderPenalty = (blunders * 38.0) + Math.Max(0, blunders - 2) * 35.0;

        baseElo -= baseBlunderPenalty;
        baseElo -= effectiveMissedPenalty;
        baseElo -= baseMistakePenalty;
        baseElo += (brilliantCount * 50.0);
        baseElo += (greatCount * 30.0);
        if (wonGame) baseElo += 40.0;
        else baseElo -= 40.0;

        // Если в PGN указан рейтинг игрока (WhiteElo / BlackElo),
        // привязываем оценку к ожидаемому уровню этого игрока (как на Chess.com)
        if (playerRating.HasValue && playerRating.Value > 0)
        {
            double r = playerRating.Value;
            // Реалистичная ожидаемая точность: ~54% для 250, ~70% для 1200, ~77% для 1800, ~88% для 2500
            double expectedAcc = Math.Clamp(50.0 + (r / 65.0), 50.0, 92.0);
            double deltaAcc = accuracy - expectedAcc;

            // Чем выше рейтинг игрока, тем строже штраф за грубые ошибки и зевки
            double ratingScale = Math.Clamp(r / 1000.0, 0.65, 1.50);

            double blunderPenalty = ((blunders * 40.0) + Math.Max(0, blunders - 2) * 40.0) * ratingScale;
            double missedPenalty = ((blunders > 0 || mistakes > 0 || accuracy < 70.0)
                ? (missedWins * 30.0)
                : 0.0) * ratingScale;

            double mistakePenalty = (mistakes * 14.0) * ratingScale;
            if (mistakes >= 6)
            {
                mistakePenalty += ((mistakes - 5) * 14.0) * ratingScale;
            }

            double effectiveDeltaAcc = (mistakes >= 6) ? Math.Min(deltaAcc, 8.0) : deltaAcc;
            // При высоком рейтинге безупречная игра дарит сильный бонус к перформансу (+500 ELO при 88% у 1800)
            double deltaWeight = r >= 1500 ? 32.0 : 20.0;
            double playerBasedElo = r + (effectiveDeltaAcc * deltaWeight)
                                    - blunderPenalty
                                    - missedPenalty
                                    - mistakePenalty
                                    + (brilliantCount * 50.0)
                                    + (greatCount * 40.0)
                                    + (wonGame ? 50.0 : 0.0);

            if (blunders == 0 && mistakes == 0 && accuracy >= 70.0)
            {
                playerBasedElo += 80.0;
            }

            baseElo = playerBasedElo;
        }
        else
        {
            baseElo += 40.0;
        }

        int rounded = (int)(Math.Round(baseElo / 50.0) * 50);
        return Math.Clamp(rounded, 100, 2900);
    }

    public static MoveClassification ClassifySingleMove(
        Move move,
        string san,
        PieceColor color,
        int moveNumber,
        string fenBefore,
        string fenAfter,
        EngineEvaluation preEval,
        EngineEvaluation postEval,
        MoveClassification? expectedClassification = null,
        int? playerRating = null)
    {
        var pgnMove = new PgnMove
        {
            Move = move,
            San = san,
            Color = color,
            MoveNumber = moveNumber,
            FenBefore = fenBefore,
            FenAfter = fenAfter,
            ExpectedClassification = expectedClassification
        };

        double preWinWhite = preEval.WinProbabilityWhite;
        double postWinWhite = postEval.WinProbabilityWhite;
        double deltaP = color == PieceColor.White
            ? Math.Max(0.0, preWinWhite - postWinWhite)
            : Math.Max(0.0, (100.0 - preWinWhite) - (100.0 - postWinWhite));

        var boardBefore = new BoardState();
        boardBefore.LoadFen(fenBefore);
        int legalCount = boardBefore.GenerateLegalMoves().Count;

        return ClassifyMove(pgnMove, preEval, postEval, deltaP, legalCount, playerRating);
    }

    private static MoveClassification ClassifyMove(
        PgnMove pgnMove,
        EngineEvaluation preEval,
        EngineEvaluation postEval,
        double deltaP,
        int legalCount,
        int? playerRating = null)
    {
        // Priority 1: If the move already has an annotated classification or NAG ($1..$6, -G1..-B4), respect it!
        if (pgnMove.ExpectedClassification.HasValue)
        {
            return pgnMove.ExpectedClassification.Value;
        }

        return MoveClassifier.Classify(pgnMove, preEval, postEval, deltaP, legalCount, weights: null, playerRating: playerRating);
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
            $"Неплохой ход. Надежное продолжение.",

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
