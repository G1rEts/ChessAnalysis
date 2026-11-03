using System;
using System.Collections.Generic;
using System.Linq;
using ChessAnalysis.Core.Chess;

namespace ChessAnalysis.Core.Analysis;

/// <summary>
/// Сервис анализа этапов партии (Дебют, Миттельшпиль, Эндшпиль)
/// с вычислением точности и присвоением бейджиков качества игры.
/// </summary>
public static class GamePhasesService
{
    public static void CalculatePhases(GameAnalysis gameAnalysis)
    {
        var moves = gameAnalysis.Moves;
        if (moves.Count == 0) return;

        // 1. Определение границы окончания дебюта:
        // Дебют длится до последнего теоретического хода из книги (до 24 полухода),
        // либо как минимум 10-14 полуходов (но не более половины всей партии при коротких играх).
        int maxOpeningPlies = Math.Min(24, Math.Max(10, moves.Count / 2));
        int openingEndPly = 0;
        for (int i = 0; i < Math.Min(maxOpeningPlies, moves.Count); i++)
        {
            if (moves[i].Classification == MoveClassification.Book)
            {
                openingEndPly = i + 1;
            }
        }
        openingEndPly = Math.Max(openingEndPly, Math.Min(12, moves.Count));
        if (moves.Count > 16 && openingEndPly > moves.Count - 6)
        {
            openingEndPly = moves.Count - 6;
        }

        // 2. Определение начала эндшпиля:
        // Начинается, когда у обеих сторон нет ферзей или суммарный непешечный материал <= 13 очков на сторону.
        int endgameStartPly = -1;
        for (int i = openingEndPly; i < moves.Count; i++)
        {
            var board = new BoardState();
            board.LoadFen(moves[i].FenAfter);
            if (IsEndgamePosition(board))
            {
                endgameStartPly = i;
                break;
            }
        }

        var whiteMoves = moves.Where(m => m.Color == PieceColor.White).ToList();
        var blackMoves = moves.Where(m => m.Color == PieceColor.Black).ToList();

        // 3. Расчет для Белых
        var wOpen = whiteMoves.Where(m => m.PlyIndex < openingEndPly).ToList();
        var wMid = (endgameStartPly >= 0)
            ? whiteMoves.Where(m => m.PlyIndex >= openingEndPly && m.PlyIndex < endgameStartPly).ToList()
            : whiteMoves.Where(m => m.PlyIndex >= openingEndPly).ToList();
        var wEnd = (endgameStartPly >= 0)
            ? whiteMoves.Where(m => m.PlyIndex >= endgameStartPly).ToList()
            : new List<MoveAnalysis>();

        var (wOpenPerf, wOpenAcc) = EvaluatePhase(wOpen);
        var (wMidPerf, wMidAcc) = EvaluatePhase(wMid);
        var (wEndPerf, wEndAcc) = EvaluatePhase(wEnd);

        gameAnalysis.WhitePhases.OpeningPerformance = wOpenPerf ?? MoveClassification.Good;
        gameAnalysis.WhitePhases.OpeningAccuracy = wOpenAcc;
        gameAnalysis.WhitePhases.MiddlegamePerformance = wMidPerf ?? MoveClassification.Good;
        gameAnalysis.WhitePhases.MiddlegameAccuracy = wMidAcc;
        gameAnalysis.WhitePhases.EndgamePerformance = wEnd.Count > 0 ? wEndPerf : null;
        gameAnalysis.WhitePhases.EndgameAccuracy = wEnd.Count > 0 ? wEndAcc : null;

        // 4. Расчет для Черных
        var bOpen = blackMoves.Where(m => m.PlyIndex < openingEndPly).ToList();
        var bMid = (endgameStartPly >= 0)
            ? blackMoves.Where(m => m.PlyIndex >= openingEndPly && m.PlyIndex < endgameStartPly).ToList()
            : blackMoves.Where(m => m.PlyIndex >= openingEndPly).ToList();
        var bEnd = (endgameStartPly >= 0)
            ? blackMoves.Where(m => m.PlyIndex >= endgameStartPly).ToList()
            : new List<MoveAnalysis>();

        var (bOpenPerf, bOpenAcc) = EvaluatePhase(bOpen);
        var (bMidPerf, bMidAcc) = EvaluatePhase(bMid);
        var (bEndPerf, bEndAcc) = EvaluatePhase(bEnd);

        gameAnalysis.BlackPhases.OpeningPerformance = bOpenPerf ?? MoveClassification.Good;
        gameAnalysis.BlackPhases.OpeningAccuracy = bOpenAcc;
        gameAnalysis.BlackPhases.MiddlegamePerformance = bMidPerf ?? MoveClassification.Good;
        gameAnalysis.BlackPhases.MiddlegameAccuracy = bMidAcc;
        gameAnalysis.BlackPhases.EndgamePerformance = bEnd.Count > 0 ? bEndPerf : null;
        gameAnalysis.BlackPhases.EndgameAccuracy = bEnd.Count > 0 ? bEndAcc : null;
    }

    private static (MoveClassification? perf, double accuracy) EvaluatePhase(List<MoveAnalysis> phaseMoves)
    {
        if (phaseMoves.Count == 0) return (null, 100.0);

        // Используем байесовское сглаживание (2 базовых хода по 75%)
        // чтобы единичный зевок на короткой дистанции не обрушивал точность этапа до 0.76%!
        double sumAcc = phaseMoves.Sum(m =>
        {
            if (m.MoveAccuracy > 15.0) return m.MoveAccuracy;
            return m.Classification switch
            {
                MoveClassification.Brilliant or MoveClassification.Great or MoveClassification.Best or MoveClassification.Book or MoveClassification.Forced => 100.0,
                MoveClassification.Excellent => 95.0,
                MoveClassification.Good => 85.0,
                MoveClassification.Inaccuracy => 68.0,
                MoveClassification.Mistake => 48.0,
                MoveClassification.Blunder => 20.0,
                MoveClassification.MissedWin => 30.0,
                _ => 80.0
            };
        });

        double avgAcc = (sumAcc + 150.0) / (phaseMoves.Count + 2);
        avgAcc = Math.Clamp(avgAcc, 25.0, 100.0);

        int blunders = phaseMoves.Count(m => m.Classification == MoveClassification.Blunder);
        int missedWins = phaseMoves.Count(m => m.Classification == MoveClassification.MissedWin);
        int mistakes = phaseMoves.Count(m => m.Classification == MoveClassification.Mistake);
        int greatOrBest = phaseMoves.Count(m => m.Classification is MoveClassification.Brilliant or MoveClassification.Great or MoveClassification.Best);

        MoveClassification perf;

        // В этапах партии Chess.com использует следующие бейджи:
        // 👍 Отличный (Excellent), ✓ Неплохой (Good), ?! Неточность (Inaccuracy), ? Ошибка (Mistake), ?? Зевок (Blunder).
        // Топовым бейджем этапа всегда является Отличный (👍), а не Лучший (★).
        if (avgAcc >= 76.0)
        {
            if (blunders == 0 && mistakes == 0)
            {
                perf = MoveClassification.Excellent;
            }
            else
            {
                perf = MoveClassification.Good;
            }
        }
        else if (avgAcc >= 60.0)
        {
            if (blunders <= 1 && mistakes <= 1)
            {
                perf = MoveClassification.Good;
            }
            else
            {
                perf = MoveClassification.Inaccuracy;
            }
        }
        else if (avgAcc >= 45.0)
        {
            if (blunders <= 2 && mistakes <= 2)
            {
                perf = MoveClassification.Inaccuracy;
            }
            else
            {
                perf = MoveClassification.Mistake;
            }
        }
        else if (avgAcc >= 30.0)
        {
            perf = MoveClassification.Mistake;
        }
        else
        {
            perf = MoveClassification.Blunder;
        }

        return (perf, Math.Round(avgAcc, 1));
    }

    private static bool IsEndgamePosition(BoardState board)
    {
        int whiteQueens = 0;
        int blackQueens = 0;
        int whiteMinorMajorValue = 0;
        int blackMinorMajorValue = 0;

        for (int i = 0; i < 64; i++)
        {
            var p = board[new Square(i)];
            if (p.IsEmpty || p.Type is PieceType.Pawn or PieceType.King) continue;

            if (p.Color == PieceColor.White)
            {
                if (p.Type == PieceType.Queen) whiteQueens++;
                else whiteMinorMajorValue += p.Value;
            }
            else
            {
                if (p.Type == PieceType.Queen) blackQueens++;
                else blackMinorMajorValue += p.Value;
            }
        }

        // Эндшпиль наступает, если:
        // 1. У обеих сторон нет ферзей
        // 2. Либо у каждой стороны ферзь + не более 1 легкой фигуры (ценность <= 300)
        if (whiteQueens == 0 && blackQueens == 0) return true;
        if (whiteQueens <= 1 && blackQueens <= 1 && whiteMinorMajorValue <= 350 && blackMinorMajorValue <= 350) return true;

        return false;
    }
}
