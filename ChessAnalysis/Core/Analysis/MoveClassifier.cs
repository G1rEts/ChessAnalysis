using System;
using System.Collections.Generic;
using System.Linq;
using ChessAnalysis.Core.Chess;
using ChessAnalysis.Core.Engine;

namespace ChessAnalysis.Core.Analysis;

/// <summary>
/// Единый ультимативный модуль классификации шахматных ходов.
/// Содержит все правила для оценки ходов (G1..G3, N1..N3, B1..B4, M1)
/// и сверку с множественными линиями Stockfish (MultiPV 5).
/// </summary>
public static class MoveClassifier
{
    /// <summary>
    /// Основная функция классификации хода на основе оценки Stockfish и настраиваемых весов.
    /// </summary>
    public static MoveClassification Classify(
        PgnMove pgnMove,
        EngineEvaluation preEval,
        EngineEvaluation postEval,
        double deltaP,
        int legalCount,
        MoveClassificationWeights? weights = null,
        int? playerRating = null)
    {
        weights ??= MoveClassificationWeights.Default;

        // Если в PGN уже есть готовая разметка (Chess.com NAG или c_effect), обязательно берем её!
        if (pgnMove.ExpectedClassification.HasValue)
        {
            return pgnMove.ExpectedClassification.Value;
        }

        // 0. Доставлен мат
        if (pgnMove.San.Contains('#') || (postEval.MateInMoves.HasValue && postEval.MateInMoves.Value == 0))
        {
            if (pgnMove.San.Contains('=') || (preEval.HasSecondBest && Math.Abs(preEval.Centipawns - preEval.SecondBestCentipawns) >= 200))
            {
                return MoveClassification.Great;
            }
            return MoveClassification.Best;
        }

        // 1. Вынужденный ход (M1) - единственный легальный ход в позиции
        if (legalCount == 1)
        {
            return MoveClassification.Forced;
        }

        string playedUci = pgnMove.Move.ToUci();
        bool isTopEngineMove = !string.IsNullOrEmpty(preEval.BestMoveUci) &&
                               !pgnMove.Move.IsEmpty &&
                               string.Equals(playedUci, preEval.BestMoveUci, StringComparison.OrdinalIgnoreCase);

        // Позиция в линиях MultiPV Stockfish
        int? matchedMultiPv = null;
        int matchedLineCpMargin = int.MaxValue;
        if (preEval.Lines.Count > 0)
        {
            for (int i = 0; i < preEval.Lines.Count; i++)
            {
                var line = preEval.Lines[i];
                if (line.Pv.Count > 0 && string.Equals(playedUci, line.Pv[0], StringComparison.OrdinalIgnoreCase))
                {
                    matchedMultiPv = line.MultiPv;
                    int topCp = preEval.Lines[0].Centipawns;
                    matchedLineCpMargin = Math.Abs(topCp - line.Centipawns);
                    break;
                }
            }
        }
        else if (isTopEngineMove)
        {
            matchedMultiPv = 1;
            matchedLineCpMargin = 0;
        }
        else if (preEval.HasSecondBest && string.Equals(playedUci, preEval.SecondBestMoveUci, StringComparison.OrdinalIgnoreCase))
        {
            matchedMultiPv = 2;
            matchedLineCpMargin = Math.Abs(preEval.Centipawns - preEval.SecondBestCentipawns);
        }

        // 2. Теоретический дебютный ход (G3)
        if (pgnMove.MoveNumber <= weights.BookMaxMoveNumber &&
            deltaP <= weights.BookMaxDeltaP &&
            OpeningBook.IsBookPosition(pgnMove.FenAfter))
        {
            return MoveClassification.Book;
        }

        // Расчет изменения ценности позиции со стороны играющего
        double evalForPlayer = pgnMove.Color == PieceColor.White ? postEval.Centipawns : -postEval.Centipawns;
        double preEvalForPlayer = pgnMove.Color == PieceColor.White ? preEval.Centipawns : -preEval.Centipawns;
        double cpLoss = preEvalForPlayer - evalForPlayer;

        // 4. Упущенный шанс / выигрыш (B4 / ✖)
        double playerPreWinProb = pgnMove.Color == PieceColor.White ? preEval.WinProbabilityWhite : (100.0 - preEval.WinProbabilityWhite);
        double playerPostWinProb = pgnMove.Color == PieceColor.White ? postEval.WinProbabilityWhite : (100.0 - postEval.WinProbabilityWhite);

        bool hadWinningAdvantage = preEvalForPlayer >= 200 || playerPreWinProb >= weights.MissedWinPreWinProb ||
                                   (preEval.MateInMoves.HasValue && (pgnMove.Color == PieceColor.White ? preEval.MateInMoves.Value > 0 : preEval.MateInMoves.Value < 0));

        bool largeLoss = deltaP >= weights.MissedWinDeltaP || cpLoss >= 180.0;
        bool notLosing = evalForPlayer >= -120.0 || playerPostWinProb >= 35.0;
        bool notStillCrushing = evalForPlayer < 350.0;

        if (!isTopEngineMove && hadWinningAdvantage && largeLoss && notLosing && notStillCrushing)
        {
            return MoveClassification.MissedWin;
        }

        // 5. Бриллиантовый (G1 / !!)
        bool isMatingForPlayer = postEval.MateInMoves.HasValue &&
            (pgnMove.Color == PieceColor.White ? postEval.MateInMoves.Value > 0 : postEval.MateInMoves.Value < 0);

        int maxPreEvalForBrilliant = weights.BrilliantMaxPreEvalCp;
        int minPostEvalForBrilliant = weights.BrilliantMinPostEvalCp;

        bool preEvalAllowed = preEvalForPlayer <= maxPreEvalForBrilliant || isMatingForPlayer;

        if (isTopEngineMove && preEvalAllowed &&
            (evalForPlayer >= minPostEvalForBrilliant || isMatingForPlayer))
        {
            if (IsSacrifice(pgnMove, out _, playerRating))
            {
                return MoveClassification.Brilliant;
            }
        }

        // 6. Замечательный ход (G2 / !):
        // Единственный спасающий ход, тонкий переломный ход или единственное точное продолжение,
        // где 2-я линия существенно хуже 1-й.
        // Не присуждается на взятиях (captures) - по запросу пользователя: перестать давать за взятия замечательный, а давать лучший.
        if (isTopEngineMove && preEval.HasSecondBest && !pgnMove.Move.IsCapture)
        {
            var boardB = new BoardState();
            boardB.LoadFen(pgnMove.FenBefore);
            var enemyCol = pgnMove.Color == PieceColor.White ? PieceColor.Black : PieceColor.White;

            // Король был под шахом
            bool wasInCheck = boardB.IsInCheck(pgnMove.Color);

            // Ход тяжелой/легкой фигуры из-под прямого боя более слабой фигуры или без защиты
            var movedPieceBefore = boardB[pgnMove.Move.From];
            bool isAttackedPieceRetreating = movedPieceBefore.Value >= 300 &&
                                             boardB.IsSquareAttacked(pgnMove.Move.From, enemyCol) &&
                                             (!boardB.IsSquareAttacked(pgnMove.Move.From, pgnMove.Color) ||
                                              boardB.GetLowestAttackerValue(pgnMove.Move.From, enemyCol) < movedPieceBefore.Value);

            if (!wasInCheck && !isAttackedPieceRetreating && preEvalForPlayer <= 950)
            {
                int scoreDiff = pgnMove.Color == PieceColor.White
                    ? (preEval.Centipawns - preEval.SecondBestCentipawns)
                    : (-preEval.Centipawns - (-preEval.SecondBestCentipawns));

                double secondBestEvalForPlayer = pgnMove.Color == PieceColor.White ? preEval.SecondBestCentipawns : -preEval.SecondBestCentipawns;

                int minDiff = weights.GreatMoveMinScoreDiff;
                int maxSecondLine = weights.GreatMoveMaxSecondLineCp;

                bool secondBestDropsSignificantly = secondBestEvalForPlayer <= maxSecondLine ||
                                                    scoreDiff >= 350 ||
                                                    (preEval.MateInMoves.HasValue && !preEval.SecondBestMateIn.HasValue);

                if (secondBestDropsSignificantly && scoreDiff >= minDiff)
                {
                    return MoveClassification.Great;
                }
            }
        }

        // 7. Если сыгран 1-й ход движка -> Лучший ход (N1 / ★)
        if (isTopEngineMove)
        {
            return MoveClassification.Best;
        }

        // 8. Отличный ход (N2 / 👍):
        // Сверка по MultiPV (линии 2 и 3 с близким отрывом от первой)
        // либо дельта вероятности <= 9.5%, либо потеря пешек <= 140 CP
        bool isMultiPvExcellent = matchedMultiPv.HasValue &&
                                  matchedMultiPv.Value is 2 or 3 &&
                                  matchedLineCpMargin <= weights.MultiPvExcellentCpMargin;

        bool isWinningPosition = preEvalForPlayer >= 350 && evalForPlayer >= 250;

        if (isMultiPvExcellent ||
            (deltaP <= weights.ExcellentMaxDeltaP && (cpLoss <= weights.ExcellentMaxCpLoss || isWinningPosition)))
        {
            return MoveClassification.Excellent;
        }

        // 9. Неплохой ход (N3 / ✓):
        // Сверка по MultiPV (линии 2, 3, 4 с приемлемым отрывом)
        // либо дельта вероятности <= 13.0%, либо потеря пешек <= 240 CP в выигранной позиции
        bool isMultiPvGood = matchedMultiPv.HasValue &&
                             matchedMultiPv.Value is 2 or 3 or 4 &&
                             matchedLineCpMargin <= weights.MultiPvGoodCpMargin;

        if (isMultiPvGood ||
            (deltaP <= weights.GoodMaxDeltaP && (cpLoss <= weights.GoodMaxCpLoss || isWinningPosition)))
        {
            return MoveClassification.Good;
        }

        // 10. Защита от грубых ошибок в подавляюще выигранных позициях (isDecisivelyWinning):
        bool isDecisivelyWinning = preEvalForPlayer >= 750 && evalForPlayer >= 350;
        if (isDecisivelyWinning)
        {
            return MoveClassification.Inaccuracy;
        }

        // 11. Классификация ошибок в остальных позициях
        // Зевок (Blunder) присуждается, только если:
        // 1) ход допускает мат в N ходов для соперника (при условии, что до хода позиция не была безнадежной, например <= -800)
        // 2) ИЛИ отдается фигура ни за что (не являясь жертвой), т.е. зевок материала.
        // Если же позиция просто сильно ухудшилась (например, было +1.4, стало +4.0 у соперника)
        // без зевка фигуры или мата, то это Ошибка (Mistake), а не Зевок!
        bool allowsOpponentMate = postEval.MateInMoves.HasValue &&
            (pgnMove.Color == PieceColor.White ? postEval.MateInMoves.Value < 0 : postEval.MateInMoves.Value > 0);
        bool wasHopelessBefore = preEvalForPlayer <= -800 ||
            (preEval.MateInMoves.HasValue && (pgnMove.Color == PieceColor.White ? preEval.MateInMoves.Value < 0 : preEval.MateInMoves.Value > 0));

        // Катастрофический крах позиции в безнадежную (например с 0.0 до -9.8, оценка хуже -7.50)
        bool isCatastrophicCollapse = !wasHopelessBefore && evalForPlayer <= -750.0;

        bool isTacticalBlunder = (allowsOpponentMate && !wasHopelessBefore) || IsPieceBlundered(pgnMove) || isCatastrophicCollapse;

        double blunderMinCp = (playerRating is >= 1500) ? 250.0 : weights.BlunderMinCpLoss;
        double blunderMinDeltaP = (playerRating is >= 1500) ? 24.0 : 28.0;

        if (deltaP >= blunderMinDeltaP || (cpLoss >= blunderMinCp && deltaP >= 15.0))
        {
            if (isTacticalBlunder)
            {
                return MoveClassification.Blunder;
            }
            return MoveClassification.Mistake;
        }

        double mistakeMinCp = (playerRating is >= 1500) ? 200.0 : weights.MistakeMinCpLoss;
        double mistakeMinDeltaP = (playerRating is >= 1500) ? 12.0 : 14.0;

        if (deltaP >= mistakeMinDeltaP || (cpLoss >= mistakeMinCp && deltaP >= 10.0))
        {
            return MoveClassification.Mistake;
        }

        return MoveClassification.Inaccuracy;
    }

    /// <summary>
    /// Проверяет, была ли в результате хода зевена фигура без компенсации (прямой или косвенный зевок).
    /// </summary>
    public static bool IsPieceBlundered(PgnMove pgnMove)
    {
        if (string.IsNullOrEmpty(pgnMove.FenBefore)) return false;

        var boardBefore = new BoardState();
        boardBefore.LoadFen(pgnMove.FenBefore);

        var movingColor = pgnMove.Color;
        var enemyColor = movingColor == PieceColor.White ? PieceColor.Black : PieceColor.White;

        var movedPiece = boardBefore[pgnMove.Move.From];

        var boardAfter = new BoardState();
        boardAfter.LoadFen(pgnMove.FenBefore);
        boardAfter.ApplyMove(pgnMove.Move);

        var opponentLegalMoves = boardAfter.GenerateLegalMoves();
        if (opponentLegalMoves.Count == 0) return false;

        // 1. Зевок перемещенной фигуры (фигура пошла под бой)
        if (movedPiece.Type is not (PieceType.King or PieceType.None))
        {
            var directCaptures = opponentLegalMoves.Where(m => m.To == pgnMove.Move.To).ToList();
            if (directCaptures.Count > 0)
            {
                int lowestEnemyAttacker = directCaptures.Min(m => boardAfter[m.From].Value);
                bool destDefended = boardAfter.IsSquareAttacked(pgnMove.Move.To, movingColor);
                int capturedVal = pgnMove.Move.IsCapture ? boardBefore[pgnMove.Move.To].Value : 0;

                if (!destDefended)
                {
                    int netLoss = movedPiece.Value - capturedVal;
                    if (netLoss >= 150) return true;
                }
                else if (lowestEnemyAttacker < movedPiece.Value - 100)
                {
                    int netLoss = (movedPiece.Value - lowestEnemyAttacker) - capturedVal;
                    if (netLoss >= 150) return true;
                }
            }
        }

        // 2. Зевок другой фигуры (оставлена без защиты или под боем более слабой фигуры)
        for (int i = 0; i < 64; i++)
        {
            var sq = new Square(i);
            if (sq == pgnMove.Move.To || sq == pgnMove.Move.From) continue;

            var piece = boardAfter[sq];
            if (piece.Color == movingColor && piece.Type is PieceType.Queen or PieceType.Rook or PieceType.Bishop or PieceType.Knight)
            {
                bool attackedBefore = boardBefore.IsSquareAttacked(sq, enemyColor);
                bool defendedBefore = boardBefore.IsSquareAttacked(sq, movingColor);

                // Если фигура уже висела без защиты до этого хода, ход ее не зевал
                if (attackedBefore && !defendedBefore) continue;

                var hangingCaptures = opponentLegalMoves.Where(m => m.To == sq).ToList();
                if (hangingCaptures.Count == 0) continue;

                int lowestAttacker = hangingCaptures.Min(m => boardAfter[m.From].Value);
                bool defendedNow = boardAfter.IsSquareAttacked(sq, movingColor);

                bool becameAttacked = !attackedBefore;
                bool becameUnguarded = defendedBefore && !defendedNow;

                if (!becameAttacked && !becameUnguarded) continue;

                if (!defendedNow)
                {
                    return true;
                }
                else if (lowestAttacker < piece.Value - 100)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Проверка жертвы материала для Бриллиантового (G1) и Замечательного (G2) ходов.
    /// Фиксирует как прямую жертву (ход под бой менее ценной фигуры/без защиты),
    /// так и косвенную (ход, оставляющий другую фигуру ценностью >= 300 под боем).
    /// </summary>
    public static bool IsSacrifice(PgnMove pgnMove, out bool isTemporarySacrifice, int? playerRating = null)
    {
        isTemporarySacrifice = false;

        var boardBefore = new BoardState();
        boardBefore.LoadFen(pgnMove.FenBefore);

        var movingColor = pgnMove.Color;
        var enemyColor = movingColor == PieceColor.White ? PieceColor.Black : PieceColor.White;

        bool wasInCheck = boardBefore.IsInCheck(movingColor);
        var movedPiece = boardBefore[pgnMove.Move.From];
        bool isMoveCapture = pgnMove.Move.IsCapture || !boardBefore[pgnMove.Move.To].IsEmpty || pgnMove.San.Contains('x');

        // Любой ход, когда собственный король находится под шахом (взятие, перекрытие, уход) -
        // это вынужденная защита, а не блестящая жертва.
        if (wasInCheck)
        {
            return false;
        }

        var boardAfter = new BoardState();
        boardAfter.LoadFen(pgnMove.FenBefore);
        boardAfter.ApplyMove(pgnMove.Move);

        // Безопасная тактическая вилка конем / пешкой (двойной удар):
        if (movedPiece.Type is PieceType.Knight or PieceType.Pawn)
        {
            bool destAttackedByEnemyPiece = boardAfter.IsSquareAttacked(pgnMove.Move.To, enemyColor);
            if (!destAttackedByEnemyPiece)
            {
                int forkedTargetsCount = 0;
                for (int i = 0; i < 64; i++)
                {
                    var targetSq = new Square(i);
                    var targetPiece = boardAfter[targetSq];
                    if (targetPiece.Color == enemyColor && (targetPiece.Type is PieceType.Queen or PieceType.Rook or PieceType.Bishop or PieceType.King))
                    {
                        if (boardAfter.IsSquareAttackedBy(targetSq, pgnMove.Move.To))
                        {
                            forkedTargetsCount++;
                        }
                    }
                }

                if (forkedTargetsCount >= 2)
                {
                    return false;
                }
            }
        }

        // Легальные ходы соперника в возникшей позиции
        var opponentLegalMoves = boardAfter.GenerateLegalMoves();

        // -------------------------------------------------------------
        // Категория 1: Прямая жертва перемещаемой фигуры
        // (фигура ходит на поле, где соперник ЛЕГАЛЬНО может её взять
        //  с чистым материальным убытком для игрока)
        // -------------------------------------------------------------
        if (movedPiece.Type is not (PieceType.Pawn or PieceType.King or PieceType.None))
        {
            int movedPieceVal = movedPiece.Value;
            var directLegalCaptures = opponentLegalMoves.Where(m => m.To == pgnMove.Move.To).ToList();

            // Если соперник не может легально побить фигуру (связка, шах, препятствие) — жертвы нет!
            if (directLegalCaptures.Count > 0)
            {
                int lowestEnemyAttacker = directLegalCaptures.Min(m => boardAfter[m.From].Value);
                bool destDefendedByFriendly = boardAfter.IsSquareAttacked(pgnMove.Move.To, movingColor);
                int capturedValue = isMoveCapture ? boardBefore[pgnMove.Move.To].Value : 0;

                // Для ферзя: взятие ферзем на защищенном поле - обычный тактический размен/взятие,
                // никогда не являющееся жертвой (награждается как Лучший).
                // Жертва ферзя возможна:
                // 1) при ходе на незащищенное поле (!destDefendedByFriendly) с чистым дефицитом >= 350
                // 2) при ходе под бой фигуры меньшей ценности (ладья, слон, конь, пешка: lowestEnemyAttacker < 900),
                //    даже если поле защищено дружественной фигурой!
                //    (Классический пример — спёртый мат с жертвой ферзя: 27. Qb8+!! Rxb8 28. Nc7#)
                if (movedPiece.Type == PieceType.Queen)
                {
                    if (capturedValue >= 900)
                    {
                        // Размен ферзей
                    }
                    else if (!destDefendedByFriendly)
                    {
                        int netDeficit = 900 - capturedValue;
                        if (netDeficit >= 350)
                        {
                            return true;
                        }
                    }
                    else if (lowestEnemyAttacker < 900)
                    {
                        int netDeficit = (900 - lowestEnemyAttacker) - capturedValue;
                        if (netDeficit >= 250)
                        {
                            return true;
                        }
                    }
                }
                else if (movedPiece.Type == PieceType.Rook)
                {
                    // Ладья под боем пешки или легкой фигуры
                    if (lowestEnemyAttacker <= 330)
                    {
                        int netDeficit = 500 - capturedValue;
                        if (netDeficit >= 150)
                        {
                            if (capturedValue >= 100) isTemporarySacrifice = true;
                            return true;
                        }
                    }
                    // Ладья ходит на абсолютно незащищенное поле и соперник бьет её бесплатно
                    else if (!destDefendedByFriendly)
                    {
                        int netDeficit = 500 - capturedValue;
                        if (netDeficit >= 150)
                        {
                            return true;
                        }
                    }
                }
                else if (movedPiece.Type is PieceType.Knight or PieceType.Bishop)
                {
                    // Легкая фигура под боем пешки
                    if (lowestEnemyAttacker <= 100)
                    {
                        int netDeficit = movedPieceVal - capturedValue;
                        if (netDeficit >= 150)
                        {
                            if (capturedValue >= 100) isTemporarySacrifice = true;
                            return true;
                        }
                    }
                    // Легкая фигура на незащищенном поле под боем любой фигуры (включая короля/тяжелую)
                    else if (!destDefendedByFriendly)
                    {
                        int netDeficit = movedPieceVal - capturedValue;
                        if (netDeficit >= 150)
                        {
                            return true;
                        }
                    }
                }
            }
        }

        // -------------------------------------------------------------
        // Категория 2: Косвенная жертва (оставление другой ценной фигуры под боем)
        // -------------------------------------------------------------
        if (movedPiece.Type is PieceType.King)
        {
            return false;
        }

        int playedCaptureVal = isMoveCapture ? boardBefore[pgnMove.Move.To].Value : 0;

        for (int i = 0; i < 64; i++)
        {
            var sq = new Square(i);
            if (sq == pgnMove.Move.To || sq == pgnMove.Move.From) continue;

            var piece = boardAfter[sq];
            if (piece.Color == movingColor && piece.Type is PieceType.Queen or PieceType.Rook or PieceType.Bishop or PieceType.Knight)
            {
                // Если сделанный ход взял фигуру равной или большей стоимости, это размен/выигрыш, а не жертва
                if (playedCaptureVal >= piece.Value)
                {
                    continue;
                }

                // Фигура НЕ должна была висеть под боем без защиты ЕЩЕ ДО этого хода!
                bool attackedBefore = boardBefore.IsSquareAttacked(sq, enemyColor);
                bool defendedBefore = boardBefore.IsSquareAttacked(sq, movingColor);
                if (attackedBefore && !defendedBefore)
                {
                    continue;
                }

                // Проверяем, может ли соперник ЛЕГАЛЬНО побить эту фигуру сейчас
                var hangingCaptures = opponentLegalMoves.Where(m => m.To == sq).ToList();
                if (hangingCaptures.Count == 0) continue;

                int lowestAttacker = hangingCaptures.Min(m => boardAfter[m.From].Value);
                bool defendedNow = boardAfter.IsSquareAttacked(sq, movingColor);
                bool attackedNow = true;

                // Жертва создана данным ходом только если фигура стала атакованной или лишилась защиты!
                bool becameAttacked = !attackedBefore && attackedNow;
                bool becameUnguarded = defendedBefore && !defendedNow;
                if (!becameAttacked && !becameUnguarded)
                {
                    continue;
                }

                // 1) Фигура совсем не защищена своими
                if (!defendedNow)
                {
                    int netMaterialRisk = piece.Value - playedCaptureVal;
                    if (netMaterialRisk >= 200)
                    {
                        return true;
                    }
                }

                // 2) Фигура защищена, но атакована фигурой меньшей стоимости
                if (lowestAttacker < piece.Value - 100)
                {
                    int netMaterialRisk = (piece.Value - lowestAttacker) - playedCaptureVal;
                    if (netMaterialRisk >= 150)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }
}
