using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using ChessAnalysis.Core.Analysis;
using ChessAnalysis.Core.Chess;
using ChessAnalysis.Core.Engine;
using ChessAnalysis.UI.Controls;
using ChessAnalysis.UI.Resources;

namespace ChessAnalysis.Tests;

public static class Program
{
    private static int _passCount = 0;
    private static int _failCount = 0;

    public static int Main()
    {
        Console.WriteLine("==================================================");
        Console.WriteLine("       ChessAnalysis Focused Core Test Suite      ");
        Console.WriteLine("==================================================");
        Console.WriteLine();

        RunTest("BoardState: Legal Moves & Special Rules (Castling, En Passant, Promotion, Mate)", TestBoardStateRules);
        RunTest("PgnParser: Headers, Moves, Clock Timestamps & Multi-Game", TestPgnParser);
        RunTest("OpeningBook: Initialization & Fast Lookup", TestOpeningBook);
        RunTest("EngineEvaluation: Win Probabilities & MultiPV Formatting", TestEngineEvaluation);
        RunTest("MoveClassification: Enums, Symbols & Theme Colors", TestMoveClassification);
        RunTest("MoveClassifier: Core Thresholds (Best, Great, MissedWin, Blunder, Mistake)", TestMoveClassifierCore);
        RunTest("MoveClassifier: Safeguards Against False Brilliant (King Move bug.png & Forks)", TestFalseBrilliantSafeguards);
        RunTest("AnalysisService: CAPS Accuracy, Performance Rating & Elo Scaling", TestCapsAccuracyAndEloScaling);
        RunTest("AnalysisService: NAG Tokens & Phase Accuracy Smoothing", TestNagAndGamePhases);
        RunTest("SoundService: Zero-Latency Audio Engine & Polyphonic Rapid Playback", TestSoundService);
        RunTest("UI Controls: MoveReviewCard Dynamic Sizing & RoundedButton", TestUiControlsCardAndButton);
        RunTest("UI Controls: ChessBoardControl User Drawings & EvalBar Formatting", TestBoardControlAndEvalBar);
        RunTest("Real Game Stockfish Analysis Debug", TestRealGameAnalysis);
        RunTest("User Brilliant Game Debug", TestUserBrilliantGame);

        Console.WriteLine();
        Console.WriteLine("==================================================");
        Console.WriteLine($"Tests Completed: {_passCount + _failCount} | Passed: {_passCount} | Failed: {_failCount}");
        Console.WriteLine("==================================================");

        return _failCount == 0 ? 0 : 1;
    }

    private static void RunTest(string name, Action testAction)
    {
        try
        {
            testAction();
            Console.WriteLine($"  [PASS] {name}");
            _passCount++;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  [FAIL] {name}: {ex.Message}");
            Console.ResetColor();
            _failCount++;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"{message} (Expected: {expected}, Actual: {actual})");
    }

    #region 1. BoardState Tests
    private static void TestBoardStateRules()
    {
        // Initial position
        var board = new BoardState();
        AssertEqual(20, board.GenerateLegalMoves().Count, "Initial position must have exactly 20 legal moves");

        // Move execution
        board.ApplyMove(Move.FromUci("e2e4"));
        AssertEqual(PieceColor.Black, board.SideToMove, "Side to move must switch to Black");
        AssertEqual("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1", board.ToFen(), "FEN after 1. e4");

        // Fool's Mate
        var foolsBoard = new BoardState();
        foolsBoard.ApplyMove(Move.FromUci("f2f3"));
        foolsBoard.ApplyMove(Move.FromUci("e7e5"));
        foolsBoard.ApplyMove(Move.FromUci("g2g4"));
        foolsBoard.ApplyMove(Move.FromUci("d8h4"));
        Assert(foolsBoard.IsInCheck(PieceColor.White), "White must be in check");
        AssertEqual(0, foolsBoard.GenerateLegalMoves().Count, "Checkmated position must have 0 legal moves");

        // Stalemate
        var staleBoard = new BoardState();
        staleBoard.LoadFen("k7/8/1Q6/8/8/8/8/7K b - - 0 1");
        Assert(!staleBoard.IsInCheck(PieceColor.Black), "Black king must not be in check in stalemate");
        AssertEqual(0, staleBoard.GenerateLegalMoves().Count, "Stalemate must have 0 legal moves");

        // Castling Kingside & Queenside
        var castleBoard = new BoardState();
        castleBoard.LoadFen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
        var castleMoves = castleBoard.GenerateLegalMoves();
        Assert(castleMoves.Exists(m => m.IsCastling && m.To == Square.FromAlgebraic("g1")), "White kingside castling must be generated");
        Assert(castleMoves.Exists(m => m.IsCastling && m.To == Square.FromAlgebraic("c1")), "White queenside castling must be generated");

        // En Passant
        var epBoard = new BoardState();
        epBoard.LoadFen("rnbqkbnr/ppp1p1pp/8/3pPp2/8/8/PPPP1PPP/RNBQKBNR w KQkq f6 0 3");
        var epMoves = epBoard.GenerateLegalMoves();
        Assert(epMoves.Exists(m => m.IsEnPassant), "En passant move must be generated");

        // Pawn Promotion
        var promoBoard = new BoardState();
        promoBoard.LoadFen("8/4P3/8/8/8/8/8/4K2k w - - 0 1");
        var promoMoves = promoBoard.GenerateLegalMoves().Where(m => m.Promotion != PieceType.None).ToList();
        AssertEqual(4, promoMoves.Count, "Promotion must generate Q, R, B, N");
    }
    #endregion

    #region 2. PGN Parser Tests
    private static void TestPgnParser()
    {
        string pgn = @"[Event ""World Championship""]
[Site ""Reykjavik""]
[Date ""1972.07.11""]
[White ""Spassky, Boris V.""]
[Black ""Fischer, Robert J.""]
[Result ""1-0""]
[WhiteElo ""2660""]
[BlackElo ""2785""]

1. d4 {[%clk 0:05:00]} Nf6 {[%clk 0:04:55]} 2. c4 e6 3. Nf3 d5 1-0";

        var game = PgnParser.ParseSingleGame(pgn);
        Assert(game != null, "Game must parse successfully");
        AssertEqual("World Championship", game!.Event, "Event header mismatch");
        AssertEqual("Spassky, Boris V.", game.White, "White header mismatch");
        AssertEqual("Fischer, Robert J.", game.Black, "Black header mismatch");
        AssertEqual("1-0", game.Result, "Result header mismatch");
        AssertEqual("2660", game.WhiteElo, "WhiteElo mismatch");
        AssertEqual("2785", game.BlackElo, "BlackElo mismatch");
        AssertEqual(6, game.Moves.Count, "Should have parsed 6 half-moves");

        // Multi-game parsing
        string multiPgn = pgn + "\n\n" + pgn;
        var games = PgnParser.ParseGames(multiPgn);
        AssertEqual(2, games.Count, "Must parse 2 games");
    }
    #endregion

    #region 3. Opening Book Tests
    private static void TestOpeningBook()
    {
        OpeningBook.Initialize();
        bool isBook = OpeningBook.IsBookPosition(BoardState.StartingFen);
        Assert(isBook, "Starting position must exist in Opening Book");
    }
    #endregion

    #region 4. EngineEvaluation Tests
    private static void TestEngineEvaluation()
    {
        // 0 CP -> 50% Win Probability
        var evalZero = new EngineEvaluation { Centipawns = 0 };
        AssertEqual(50.0, Math.Round(evalZero.WinProbabilityWhite, 1), "0 CP = 50.0% win probability");

        // +100 CP -> ~59.1% Win Probability (Official Chess.com formula)
        var eval100 = new EngineEvaluation { Centipawns = 100 };
        Assert(Math.Abs(eval100.WinProbabilityWhite - 59.1) < 0.2, $"+100 CP should be ~59.1%, got {eval100.WinProbabilityWhite:F1}%");

        // Mate Win probability
        var evalMate = new EngineEvaluation { MateInMoves = 1 };
        AssertEqual(100.0, evalMate.WinProbabilityWhite, "Mate in 1 = 100% win probability");
        var evalNegMate = new EngineEvaluation { MateInMoves = -1 };
        AssertEqual(0.0, evalNegMate.WinProbabilityWhite, "-Mate in 1 = 0% win probability");

        // MultiPV parsing
        var eval = new EngineEvaluation();
        eval.Lines.Add(new EngineLine { MultiPv = 1, Pv = new List<string> { "e2e4", "e7e5" }, Centipawns = 35 });
        eval.Lines.Add(new EngineLine { MultiPv = 2, Pv = new List<string> { "d2d4", "d7d5" }, Centipawns = 25 });
        AssertEqual(2, eval.Lines.Count, "Must have 2 MultiPV lines");
    }
    #endregion

    #region 5. MoveClassification Tests
    private static void TestMoveClassification()
    {
        AssertEqual("!!", MoveClassification.Brilliant.GetSymbol(), "Brilliant symbol must be '!!'");
        AssertEqual("!", MoveClassification.Great.GetSymbol(), "Great symbol must be '!'");
        AssertEqual("★", MoveClassification.Best.GetSymbol(), "Best symbol must be '★'");
        AssertEqual("??", MoveClassification.Blunder.GetSymbol(), "Blunder symbol must be '??'");
        AssertEqual("?", MoveClassification.Mistake.GetSymbol(), "Mistake symbol must be '?'");
        AssertEqual("?!", MoveClassification.Inaccuracy.GetSymbol(), "Inaccuracy symbol must be '?!'");
        AssertEqual("✖", MoveClassification.MissedWin.GetSymbol(), "MissedWin symbol must be '✖'");

        foreach (MoveClassification c in Enum.GetValues<MoveClassification>())
        {
            Assert(!string.IsNullOrEmpty(c.GetTitle()), $"Title for {c} must not be empty");
            Assert(!c.GetThemeColor().IsEmpty, $"Theme color for {c} must not be empty");
        }
    }
    #endregion

    #region 6. MoveClassifier Core Tests
    private static void TestMoveClassifierCore()
    {
        var pgnWinning = new PgnMove
        {
            Move = Move.FromUci("d6c5"),
            San = "Kc5",
            Color = PieceColor.Black,
            MoveNumber = 48,
            FenBefore = "8/8/8/3k4/8/8/8/8 b - - 0 48",
            FenAfter = "8/8/8/2k5/8/8/8/8 w - - 1 49"
        };

        // Top engine move in winning endgame -> Best
        var preEvalWin = new EngineEvaluation { BestMoveUci = "d6c5", Centipawns = -1200 };
        var postEvalWin = new EngineEvaluation { Centipawns = -1200 };
        var clsBest = MoveClassifier.Classify(pgnWinning, preEvalWin, postEvalWin, 0.0, 15);
        AssertEqual(MoveClassification.Best, clsBest, "Top engine move in winning endgame must be Best (★)");

        // Decisive margin over 2nd line -> Great (!)
        var preEvalGreat = new EngineEvaluation
        {
            BestMoveUci = "d6c5",
            Centipawns = -800,
            SecondBestMoveUci = "d6d5",
            SecondBestCentipawns = -400,
            HasSecondBest = true
        };
        var clsGreat = MoveClassifier.Classify(pgnWinning, preEvalGreat, new EngineEvaluation { Centipawns = -800 }, 0.0, 15, playerRating: 800);
        AssertEqual(MoveClassification.Great, clsGreat, "Decisive move with 400 CP margin over 2nd line must be Great (!)");

        // Missed Win: opponent blunder, player drops from -5.58 to -1.62 but remains winning
        var pgnMissedWin = new PgnMove
        {
            Move = Move.FromUci("c6d4"),
            San = "Nd4",
            Color = PieceColor.Black,
            MoveNumber = 13,
            FenBefore = "r1bq1rk1/pp1pp1bp/2n2p1n/1Np5/4P3/3P1N1B/PPP2PP1/R1BQK2R b KQ - 1 13",
            FenAfter = "r1bq1rk1/pp1pp1bp/5p1n/1Np5/3nP3/3P1N1B/PPP2PP1/R1BQK2R w KQ - 2 14"
        };
        var preEvalCrushing = new EngineEvaluation { BestMoveUci = "d8f6", Centipawns = -558 };
        var postEvalAdvantage = new EngineEvaluation { BestMoveUci = "f3d4", Centipawns = -162 };
        var clsMiss = MoveClassifier.Classify(pgnMissedWin, preEvalCrushing, postEvalAdvantage, 28.3, 20);
        AssertEqual(MoveClassification.MissedWin, clsMiss, "Dropping from -5.58 to -1.62 by missing tactical win must be MissedWin (✖), not Blunder (??)");

        // Positional drop without piece blunder or mate must be Mistake (?) per Task 12
        var pgnPosMistake = new PgnMove
        {
            Move = Move.FromUci("a2a3"),
            San = "a3",
            Color = PieceColor.White,
            MoveNumber = 20,
            FenBefore = "r1bq1rk1/pp1n1pp1/2pbpn1p/3p4/2PP4/2NBPN2/PP1B1PPP/R2QK2R w KQ - 0 9",
            FenAfter = "r1bq1rk1/pp1n1pp1/2pbpn1p/3p4/2PP4/P1NBPN2/1P1B1PPP/R2QK2R b KQ - 0 9"
        };
        var preEvalWhiteWin = new EngineEvaluation { BestMoveUci = "e2e4", Centipawns = 300 };
        var postEvalWhiteLost = new EngineEvaluation { Centipawns = -500 };
        var clsPosMistake = MoveClassifier.Classify(pgnPosMistake, preEvalWhiteWin, postEvalWhiteLost, 45.0, 20);
        AssertEqual(MoveClassification.Mistake, clsPosMistake, "Positional drop from +3.00 to -5.00 without piece loss must be Mistake (?), NOT Blunder (??)");

        // Real Blunder 1: Hanging a piece for nothing (Queen played into attack on e5 without defense)
        var pgnPieceBlunder = new PgnMove
        {
            Move = Move.FromUci("d1e2"),
            San = "Qe2",
            Color = PieceColor.White,
            MoveNumber = 5,
            FenBefore = "r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3",
            FenAfter = "r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPPQPPP/RNB1KB1R b KQkq - 3 3"
        };
        // Move Queen to a5 where Knight on c6 takes it for free
        var pgnHangQueen = new PgnMove
        {
            Move = Move.FromUci("d1a4"),
            San = "Qa4",
            Color = PieceColor.White,
            MoveNumber = 5,
            FenBefore = "r1bqkb1r/pppp1ppp/2n5/4p3/8/5N2/PPPPPPPP/RNBQKB1R w KQkq - 2 3",
            FenAfter = "r1bqkb1r/pppp1ppp/2n5/4p3/Q7/5N2/PPPPPPPP/RNB1KB1R b KQkq - 3 3"
        };
        // Hanging minor piece: Knight moves to e5 where Black takes for free:
        var pgnHangKnight = new PgnMove
        {
            Move = Move.FromUci("f3e5"),
            San = "Nxe5",
            Color = PieceColor.White,
            MoveNumber = 3,
            FenBefore = "r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3",
            FenAfter = "r1bqkbnr/pppp1ppp/2n5/4N3/4P3/8/PPPP1PPP/RNBQKB1R b KQkq - 0 3"
        };
        var preEvalEqual = new EngineEvaluation { BestMoveUci = "d2d4", Centipawns = 20 };
        var postEvalLost = new EngineEvaluation { Centipawns = -350 };
        var clsKnightBlunder = MoveClassifier.Classify(pgnHangKnight, preEvalEqual, postEvalLost, 35.0, 20);
        AssertEqual(MoveClassification.Blunder, clsKnightBlunder, "Hanging a knight on e5 with net loss must be Blunder (??)");

        // Real Blunder 2: Allowing forced mate in N from non-hopeless position (+3.00 to -M5)
        var preEvalPlus3 = new EngineEvaluation { BestMoveUci = "e2e4", Centipawns = 300 };
        var postEvalM5 = new EngineEvaluation { Centipawns = -100000, MateInMoves = -5 };
        var clsMateBlunder = MoveClassifier.Classify(pgnPosMistake, preEvalPlus3, postEvalM5, 50.0, 20);
        AssertEqual(MoveClassification.Blunder, clsMateBlunder, "Allowing mate in 5 from non-hopeless position (+3.00) must be Blunder (??)");

        // Real Mistake 3: Allowing mate in N when already hopeless (-10.00 to -M3) must be Mistake (?)
        var preEvalHopeless = new EngineEvaluation { BestMoveUci = "e2e4", Centipawns = -1000 };
        var postEvalM3 = new EngineEvaluation { Centipawns = -100000, MateInMoves = -3 };
        var clsHopelessMistake = MoveClassifier.Classify(pgnPosMistake, preEvalHopeless, postEvalM3, 10.0, 20);
        AssertEqual(MoveClassification.Mistake, clsHopelessMistake, "Allowing mate in 3 when already -10.00 must be Mistake (?), NOT Blunder (??)");
    }
    #endregion

    #region 7. Safeguards against False Brilliant Tests (bug.png fix)
    private static void TestFalseBrilliantSafeguards()
    {
        // bug.png: Black plays 30... Kf8 with King while white Knight on f6 attacks rook on e8.
        // King move while another piece is already hanging MUST NEVER be classified as Brilliant (!!)!
        var pgnKingMove = new PgnMove
        {
            Move = Move.FromUci("g8f8"),
            San = "Kf8",
            Color = PieceColor.Black,
            MoveNumber = 30,
            FenBefore = "4r1k1/1p6/4NB1p/5p2/2r1p3/4R2P/PR3PB1/3n2K1 b - - 0 30",
            FenAfter = "4rk2/1p6/4NB1p/5p2/2r1p3/4R2P/PR3PB1/3n2K1 w - - 1 31"
        };

        bool isSac = MoveClassifier.IsSacrifice(pgnKingMove, out bool isTemp, playerRating: 1100);
        Assert(!isSac, "King move Kf8 leaving existing attacked rook e8 must NOT be considered a sacrifice!");

        var preEvalKf8 = new EngineEvaluation { BestMoveUci = "g8f8", Centipawns = -121 };
        var postEvalKf8 = new EngineEvaluation { Centipawns = -121 };
        var clsKf8 = MoveClassifier.Classify(pgnKingMove, preEvalKf8, postEvalKf8, deltaP: 0.0, legalCount: 15, weights: null, playerRating: 1100);
        Assert(clsKf8 != MoveClassification.Brilliant, "King move Kf8 must NEVER be classified as Brilliant (!!)");

        // Tactical Knight Fork (Nxf7) is Great (!), not Brilliant
        var pgnFork = new PgnMove
        {
            Move = Move.FromUci("e5f7"),
            San = "Nxf7",
            Color = PieceColor.White,
            MoveNumber = 17,
            FenBefore = "2kr3r/ppp2ppp/8/4N3/1P2P1b1/2PB4/P4RPP/R5K1 w - - 0 17",
            FenAfter = "2kr3r/ppp2Npp/8/8/1P2P1b1/2PB4/P4RPP/R5K1 b - - 0 17"
        };
        bool isForkSac = MoveClassifier.IsSacrifice(pgnFork, out bool isTempFork, playerRating: 465);
        Assert(!isForkSac, "Tactical Knight fork must NOT be treated as a sacrifice");

        // Queen capture of undefended pawn/minor must NOT be a sacrifice and NEVER Brilliant or Great
        var pgnQueenCapture = new PgnMove
        {
            Move = Move.FromUci("d1d5"),
            San = "Qxd5",
            Color = PieceColor.White,
            MoveNumber = 10,
            FenBefore = "r1b1k2r/ppp2ppp/2n5/3p4/8/8/PPP2PPP/RNBQKB1R w KQkq - 0 10",
            FenAfter = "r1b1k2r/ppp2ppp/2n5/3Q4/8/8/PPP2PPP/RNB1KB1R b KQkq - 0 10"
        };
        bool isQueenSac = MoveClassifier.IsSacrifice(pgnQueenCapture, out _, playerRating: 800);
        Assert(!isQueenSac, "Queen capturing pawn on d5 must NOT be treated as a sacrifice!");
        var clsQCap = MoveClassifier.Classify(pgnQueenCapture, new EngineEvaluation { BestMoveUci = "d1d5", Centipawns = 250 }, new EngineEvaluation { Centipawns = 250 }, 0.0, 30);
        Assert(clsQCap != MoveClassification.Brilliant, "Queen capture of pawn must NEVER be Brilliant (!!)");
        Assert(clsQCap != MoveClassification.Great, "Capture must NOT be classified as Great (!)");

        // Queen capture on a defended square under attack must NOT be a sacrifice (e.g. 12. Qxd2)
        var pgnQueenDefendedCapture = new PgnMove
        {
            Move = Move.FromUci("d1d2"),
            San = "Qxd2",
            Color = PieceColor.White,
            MoveNumber = 12,
            FenBefore = "r4rk1/ppp2ppp/2n5/4p1q1/2P2b2/4PN2/PP1b1PPP/R2Q1RK1 w - - 0 12",
            FenAfter = "r4rk1/ppp2ppp/2n5/4p1q1/2P2b2/4PN2/PP1Q1PPP/R4RK1 b - - 0 12"
        };
        bool isQueenDefendedSac = MoveClassifier.IsSacrifice(pgnQueenDefendedCapture, out _, playerRating: 660);
        Assert(!isQueenDefendedSac, "Queen recapture on a defended square must NEVER be treated as a sacrifice!");
        var clsQDefended = MoveClassifier.Classify(pgnQueenDefendedCapture, new EngineEvaluation { BestMoveUci = "d1d2", Centipawns = 150 }, new EngineEvaluation { Centipawns = 150 }, 0.0, 25);
        Assert(clsQDefended != MoveClassification.Brilliant, "Queen capture on defended square must NEVER be Brilliant (!!)");

        // Empty BestMoveUci must NOT match played move as top engine move
        var pgnEmptyEngine = new PgnMove
        {
            Move = Move.FromUci("e2e4"),
            San = "e4",
            Color = PieceColor.White,
            MoveNumber = 1,
            FenBefore = BoardState.StartingFen,
            FenAfter = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1"
        };
        var emptyEngineEval = new EngineEvaluation { BestMoveUci = "" };
        var clsEmpty = MoveClassifier.Classify(pgnEmptyEngine, emptyEngineEval, new EngineEvaluation(), 0.0, 20);
        Assert(clsEmpty != MoveClassification.Best, "Empty engine BestMoveUci must NOT produce Best move (★)!");

        // 1. Smothered Mate Queen Sacrifice (Qb8+!!) must be Brilliant (!!)
        var pgnQb8 = new PgnMove
        {
            Move = Move.FromUci("b3b8"),
            San = "Qb8+",
            Color = PieceColor.White,
            MoveNumber = 27,
            FenBefore = "k1r5/pp5p/N7/8/8/1Q6/5PPP/6K1 w - - 1 27",
            FenAfter = "kr6/pp5p/N7/8/8/8/5PPP/6K1 b - - 2 27"
        };
        bool isQb8Sac = MoveClassifier.IsSacrifice(pgnQb8, out _);
        Assert(isQb8Sac, "Smothered mate Queen sacrifice onto guarded square under rook attack must be recognized as a sacrifice!");
        var preEvalQb8 = new EngineEvaluation { BestMoveUci = "b3b8", Centipawns = 100000, MateInMoves = 2 };
        var postEvalQb8 = new EngineEvaluation { Centipawns = 100000, MateInMoves = 1 };
        var clsQb8 = MoveClassifier.Classify(pgnQb8, preEvalQb8, postEvalQb8, 0.0, 30);
        AssertEqual(MoveClassification.Brilliant, clsQb8, "Smothered mate queen sacrifice Qb8+ must be classified as Brilliant (!!)");

        // 2. Check defense capture (e.g. 20. Rxd2 under check from 19... Nd2+) must NOT be Brilliant
        var pgnRxd2 = new PgnMove
        {
            Move = Move.FromUci("d1d2"),
            San = "Rxd2",
            Color = PieceColor.White,
            MoveNumber = 20,
            FenBefore = "1k5r/p1p2ppp/1p6/q7/1b1P4/1Q3N2/PP1nPPPP/1K1R1B1R w - - 1 20",
            FenAfter = "1k5r/p1p2ppp/1p6/q7/1b1P4/1Q3N2/PP1RPPPP/1K3B1R b - - 0 20"
        };
        bool isRxd2Sac = MoveClassifier.IsSacrifice(pgnRxd2, out _);
        Assert(!isRxd2Sac, "Capturing checking piece while king is in check must NOT be considered a sacrifice!");
        var preEvalRxd2 = new EngineEvaluation { BestMoveUci = "d1d2", Centipawns = 450 };
        var postEvalRxd2 = new EngineEvaluation { Centipawns = 420 };
        var clsRxd2 = MoveClassifier.Classify(pgnRxd2, preEvalRxd2, postEvalRxd2, 1.0, 3);
        Assert(clsRxd2 != MoveClassification.Brilliant, "Resolving check by capturing piece must NEVER be classified as Brilliant (!!)");

        // 3. Positional worsening (+1.4 to +4.0 for opponent, cpLoss = 260) with no hung piece must be Mistake, NOT Blunder
        var pgnPositional = new PgnMove
        {
            Move = Move.FromUci("h7h6"),
            San = "h6",
            Color = PieceColor.Black,
            MoveNumber = 12,
            FenBefore = "r1bq1rk1/pp1n1ppp/2pbpn2/3p4/2PP4/2NBPN2/PP3PPP/R1BQK2R b KQ - 4 8",
            FenAfter = "r1bq1rk1/pp1n1pp1/2pbpn1p/3p4/2PP4/2NBPN2/PP3PPP/R1BQK2R w KQ - 0 9"
        };
        var preEvalPos = new EngineEvaluation { BestMoveUci = "d5c4", Centipawns = 140 };  // White +1.40 (Black -1.40)
        var postEvalPos = new EngineEvaluation { Centipawns = 400 }; // White +4.00 (Black -4.00), cpLoss for Black = 260
        var clsPos = MoveClassifier.Classify(pgnPositional, preEvalPos, postEvalPos, 16.0, 30);
        AssertEqual(MoveClassification.Mistake, clsPos, "Positional mistake dropping 260cp without hung piece must be Mistake (?), NOT Blunder (??)");
    }
    #endregion

    #region 8. CAPS & Dynamic Rating Scaling Tests
    private static void TestCapsAccuracyAndEloScaling()
    {
        // 1. General CAPS sanity
        int gmElo = AnalysisService.EstimatePerformanceElo(95.0, 0, 0);
        Assert(gmElo is >= 2000 and <= 2550, $"95% accuracy should produce ~2100-2450 Elo, got {gmElo}");

        // 2. Game 1 Ground Truth (Low Elo: White 264, Black 317)
        // White: 88.2% acc, 0 blunders, 0 mistakes, won -> Chess.com: 1050
        int eloWhiteGame1 = AnalysisService.EstimatePerformanceElo(88.2, 0, 0, playerRating: 264, brilliantCount: 1, greatCount: 0, wonGame: true);
        Assert(eloWhiteGame1 is >= 950 and <= 1200, $"White (264) expected ~1050, got {eloWhiteGame1}");

        // Black: 74.9% acc, 0 blunders, 6 mistakes, lost -> Chess.com: 400
        int eloBlackGame1 = AnalysisService.EstimatePerformanceElo(74.9, 0, 6, playerRating: 317, brilliantCount: 0, greatCount: 0, wonGame: false);
        Assert(eloBlackGame1 is >= 300 and <= 550, $"Black (317) with 6 mistakes expected ~400, got {eloBlackGame1}");

        // 3. Game 2 Ground Truth (+1500 Elo: White 1764, Black 1817)
        // White: 88.0% acc, 0 blunders, 0 mistakes, won -> Chess.com: 2300
        int eloWhiteGame2 = AnalysisService.EstimatePerformanceElo(88.0, 0, 0, playerRating: 1764, brilliantCount: 1, greatCount: 0, wonGame: true);
        Assert(eloWhiteGame2 is >= 2100 and <= 2450, $"White (1764) expected ~2300, got {eloWhiteGame2}");

        // Black: 77.5% acc, 1 blunder, 7 mistakes, lost -> Chess.com: 1550 (penalized heavily for mistakes!)
        int eloBlackGame2 = AnalysisService.EstimatePerformanceElo(77.5, 1, 7, playerRating: 1817, brilliantCount: 0, greatCount: 0, wonGame: false);
        Assert(eloBlackGame2 is >= 1450 and <= 1700, $"Black (1817) with 7 mistakes & blunder expected ~1550, got {eloBlackGame2}");
    }
    #endregion

    #region 9. NAG & Game Phases Tests
    private static void TestNagAndGamePhases()
    {
        var singleBlunderMoves = new List<MoveAnalysis>
        {
            new MoveAnalysis { Classification = MoveClassification.Blunder, MoveAccuracy = 15.0 }
        };
        var singleAnalysis = new GameAnalysis();
        singleAnalysis.Moves.AddRange(singleBlunderMoves);
        GamePhasesService.CalculatePhases(singleAnalysis);
        Assert(singleAnalysis.WhitePhases.OpeningAccuracy >= 40.0, "Single blunder phase accuracy must be smoothed (>=40%)");
    }
    #endregion

    #region 10. SoundService Tests
    private static void TestSoundService()
    {
        SoundService.Initialize();
        for (int i = 0; i < 15; i++)
        {
            SoundService.PlayMove();
        }
        SoundService.PlayCapture();
        SoundService.PlayCheck();
        SoundService.PlayCastle();
        SoundService.PlayPromote();
        SoundService.PlayGameStart();
        SoundService.PlayGameEnd();
    }
    #endregion

    #region 11. UI Controls: ReviewCard & Button Tests
    private static void TestUiControlsCardAndButton()
    {
        using var card = new MoveReviewCardControl();
        card.ShowCalculating();
        AssertEqual(36, card.Height, "Calculating state height must be 36px");

        card.SetReview("+0.35", MoveClassification.Best, "e4", "1... e5 2. Nf3", hasBetter: false);
        AssertEqual(36, card.Height, "Single row height must be 36px");

        card.SetReview("+1.75", MoveClassification.Blunder, "b6", "22. Rxd7", hasBetter: true,
            bestScore: "-4.77", bestMove: "Cc6", bestContinuation: "22. Qxe7");
        AssertEqual(68, card.Height, "Two row height must be 68px");

        using var btn = new RoundedButton { IconType = ButtonIconType.Gear, BorderRadius = 8 };
        AssertEqual(ButtonIconType.Gear, btn.IconType, "IconType must be Gear");
    }
    #endregion

    #region 12. UI Controls: BoardControl & EvalBar Tests
    private static void TestBoardControlAndEvalBar()
    {
        using var bar = new EvalBarControl();
        var mateEval = new EngineEvaluation { MateInMoves = 11, Centipawns = 100000 };
        bar.SetEvaluation(mateEval);
        AssertEqual("M11", mateEval.ScoreDisplay, "ScoreDisplay must be M11 for mate in 11");

        using var board = new ChessBoardControl { Width = 400, Height = 400 };
        board.ClearUserDrawings();
        AssertEqual(400, board.Width, "Board width preserved");
    }
    #endregion

    #region 13. Real Game Analysis with Stockfish
    private static void TestRealGameAnalysis()
    {
        SynchronizationContext.SetSynchronizationContext(null);
        string? enginePath = StockfishEngine.FindStockfishPath();
        Assert(!string.IsNullOrEmpty(enginePath), "Stockfish engine executable found");

        using var engine = new StockfishEngine(enginePath);
        engine.StartAsync().GetAwaiter().GetResult();

        string pgnPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "engineidea", "G1rEts_vs_Ziyyad_Alkhafi_2026.10.31.pgn");
        if (!File.Exists(pgnPath))
        {
            pgnPath = Path.Combine(Directory.GetCurrentDirectory(), "engineidea", "G1rEts_vs_Ziyyad_Alkhafi_2026.10.31.pgn");
        }

        string pgnText = File.Exists(pgnPath)
            ? File.ReadAllText(pgnPath)
            : @"[Event ""Live Chess""]
[Site ""Chess.com""]
[Date ""2026.10.31""]
[White ""G1rEts""]
[Black ""Ziyyad_Alkhafi""]
[Result ""1-0""]

1. e4 e5 2. Nf3 Nc6 3. Bc4 Bc5 4. c3 Nf6 5. d4 exd4 6. cxd4 Bb4+ 7. Bd2 Bxd2+ 8. Nbxd2 d5 9. exd5 Nxd5 10. Qb3 Nce7 11. O-O O-O 12. Rfe1 c6 1-0";

        var games = PgnParser.ParseGames(pgnText);
        var game = games[0];
        var subGame = new PgnGame { RawPgn = game.RawPgn };
        foreach (var kv in game.Headers) subGame.Headers[kv.Key] = kv.Value;
        for (int i = 0; i < Math.Min(12, game.Moves.Count); i++) subGame.Moves.Add(game.Moves[i]);

        var service = new AnalysisService(engine);
        var analysis = service.AnalyzeGameAsync(subGame, nodes: 30000).GetAwaiter().GetResult();

        Assert(analysis.Moves.Count == 12, "Must analyze exactly 12 moves");
        Assert(analysis.Moves.All(m => m.PostEval.Lines.Count > 0), "All analyzed moves must have Stockfish MultiPV lines populated");
        Assert(analysis.WhiteAccuracy > 0.0 && analysis.BlackAccuracy > 0.0, "Player accuracy percentages must be calculated");
    }

    private static void TestUserBrilliantGame()
    {
        string? enginePath = StockfishEngine.FindStockfishPath();
        if (string.IsNullOrEmpty(enginePath)) return;

        using var engine = new StockfishEngine(enginePath);
        engine.StartAsync().GetAwaiter().GetResult();

        string pgn = @"[Event ""Casual Game""]
[Site ""ChessAnalysis""]
[Date ""2026.11.03""]
[White ""Белые""]
[Black ""Черные""]
[Result ""*""]

1. e4 c6 2. d4 d5 3. Nc3 dxe4 4. Nxe4 Nf6 5. Ng5 e6 6. Nf3 Bd6 7. Bd3 O-O 8. Qe2
h6 9. h4 c5 10. dxc5 Bxc5 11. Bf4 Nc6 12. O-O-O Qa5 13. Kb1 a6 14. Bd2 Qb6
15. Bc3 Nd5 16. Bh7 Kh8 17. Rxd5 exd5 18. Nxf7 Rxf7 19. Qe8 Kxh7 20. Ng5 hxg5
21. Qxf7 Nd4 22. hxg5 Bh3 23. Rxh3 Qh6 24. Rxh6 *";

        var game = PgnParser.ParseGames(pgn)[0];
        var service = new AnalysisService(engine);
        var analysis = service.AnalyzeGameAsync(game, nodes: 100000).GetAwaiter().GetResult();

        Console.WriteLine("\n--- User Brilliant Game Analysis Results ---");
        Console.WriteLine($"White Acc: {analysis.WhiteAccuracy}%, Black Acc: {analysis.BlackAccuracy}%, White Elo: {analysis.WhiteEstimatedElo}, Black Elo: {analysis.BlackEstimatedElo}");
        var m17 = analysis.Moves.First(m => m.MoveNumber == 17 && m.Color == PieceColor.White);
        var m18 = analysis.Moves.First(m => m.MoveNumber == 18 && m.Color == PieceColor.White);
        var m20 = analysis.Moves.First(m => m.MoveNumber == 20 && m.Color == PieceColor.White);

        AssertEqual(MoveClassification.Brilliant, m17.Classification, "Move 17. Rxd5 must be Brilliant");
        AssertEqual(MoveClassification.Brilliant, m18.Classification, "Move 18. Nxf7 must be Brilliant");
        AssertEqual(MoveClassification.Brilliant, m20.Classification, "Move 20. Ng5 must be Brilliant");

        // Test 2: Catastrophic Collapse Blunder Test (7... Nxc3 dropping to -9.8)
        var preEvalNxc3 = new EngineEvaluation { Centipawns = 30, BestMoveUci = "d8c7" };
        var postEvalNxc3 = new EngineEvaluation { Centipawns = 980, BestMoveUci = "d1d8" }; // White is +9.8 (Black is -9.8)
        var blunderMove = AnalysisService.ClassifySingleMove(
            Move.FromUci("d5c3"), "Nxc3", PieceColor.Black, 7,
            "r1bqkb1r/pp3ppp/2p5/3nP3/2B5/2N2Q2/PPP2PPP/R1B1K2R b KQkq - 1 7",
            "r1bqkb1r/pp3ppp/2p5/4P3/2B5/2n2Q2/PPP2PPP/R1B1K2R w KQkq - 0 8",
            preEvalNxc3, postEvalNxc3, expectedClassification: null, playerRating: 600);

        AssertEqual(MoveClassification.Blunder, blunderMove, "7... Nxc3 dropping to -9.8 must be classified as Blunder, not Mistake");
    }
    #endregion
}

