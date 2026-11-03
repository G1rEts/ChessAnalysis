using System;
using System.IO;
using System.Windows.Forms;
using ChessAnalysis.Core.Analysis;
using ChessAnalysis.Core.Config;
using ChessAnalysis.UI.Forms;

namespace ChessAnalysis
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--test")
            {
                RunSelfTest();
                return;
            }

            ApplicationConfiguration.Initialize();

            var settings = AppSettings.Load();
            bool needSetup = !settings.IsSetupCompleted ||
                             string.IsNullOrWhiteSpace(settings.StockfishPath) ||
                             !File.Exists(settings.StockfishPath);

            if (needSetup)
            {
                using var setupDlg = new StockfishSetupDialog(settings);
                if (setupDlg.ShowDialog() != DialogResult.OK)
                {
                    return;
                }
            }

            Application.Run(new Form1(settings.StockfishPath));
        }

        static void RunSelfTest()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("[TEST] Starting self-test...");

            // 0. Assets check
            UI.Resources.ResourceManager.DiscoverAssets();
            Console.WriteLine($"[TEST] Loaded pieces: {UI.Resources.ResourceManager.LoadedPiecesCount}/12");
            Console.WriteLine($"[TEST] Loaded badges: {UI.Resources.ResourceManager.LoadedBadgesCount}/11");
            Core.Chess.OpeningBook.Initialize();
            Console.WriteLine($"[TEST] Loaded Lichess openings: {Core.Chess.OpeningBook.LoadedOpeningsCount} openings, {Core.Chess.OpeningBook.LoadedPositionsCount} book positions");

            // 1. Path discovery
            string? enginePath = Core.Engine.StockfishEngine.FindStockfishPath();
            Console.WriteLine($"[TEST] Stockfish engine path found: {enginePath ?? "NULL"}");
            if (string.IsNullOrEmpty(enginePath) || !File.Exists(enginePath))
            {
                Console.WriteLine("[TEST] ERROR: Stockfish path not found!");
                Environment.Exit(1);
            }

            // 2. Engine start & evaluation
            using var engine = new Core.Engine.StockfishEngine(enginePath);
            Console.WriteLine("[TEST] Starting engine process...");
            engine.StartAsync().GetAwaiter().GetResult();
            Console.WriteLine("[TEST] Engine started successfully. Evaluating starting position...");
            var eval = engine.AnalyzePositionAsync(Core.Chess.BoardState.StartingFen, depth: 10).GetAwaiter().GetResult();
            Console.WriteLine($"[TEST] Depth: {eval.Depth}, Score: {eval.ScoreDisplay}, BestMove: {eval.BestMoveUci}");

            // 3. PGN parsing
            string pgn = "1. e4 e5 2. Nf3 Nc6 3. Bc4 Bc5";
            var games = Core.Chess.PgnParser.ParseGames(pgn);
            Console.WriteLine($"[TEST] Parsed games count: {games.Count}, Moves count: {games[0].Moves.Count}");

            // 4. Analysis service
            var analysisSvc = new Core.Analysis.AnalysisService(engine);
            var analysis = analysisSvc.AnalyzeGameAsync(games[0], depth: 8).GetAwaiter().GetResult();
            Console.WriteLine($"[TEST] Game analysis complete! Moves analyzed: {analysis.Moves.Count}");
            foreach (var m in analysis.Moves)
            {
                Console.WriteLine($"  Move {m.MoveNumber}.{m.San} -> {m.Classification.GetTitle()} ({m.Classification.GetSymbol()}), Score: {m.ScoreDisplay}, Best: {m.BestMoveSan}");
                Console.WriteLine($"    Coach: {m.CoachComment}");
            }
            Console.WriteLine($"[TEST] White Accuracy: {analysis.WhiteAccuracy:0.0}%, Black Accuracy: {analysis.BlackAccuracy:0.0}%");

            // 5. Form1 UI layout test
            var form = new Form1();
            form.CreateControl();
            Console.WriteLine("[TEST] Form1 UI instantiated and laid out successfully!");
            form.Dispose();

            Console.WriteLine("[TEST] ALL TESTS PASSED SUCCESSFULLY!");
        }
    }
}
