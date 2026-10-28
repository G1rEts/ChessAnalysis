using System.Diagnostics;
using System.Text.RegularExpressions;
using ChessAnalysis.Core.Chess;

namespace ChessAnalysis.Core.Engine;

public class StockfishEngine : IDisposable
{
    private Process? _process;
    private StreamWriter? _input;
    private readonly object _lock = new();
    private bool _isDisposed;

    public string? EnginePath { get; private set; }
    public bool IsRunning => _process is { HasExited: false };

    public StockfishEngine(string? customPath = null)
    {
        EnginePath = FindStockfishPath(customPath);
    }

    public static string? FindStockfishPath(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
        {
            return Path.GetFullPath(customPath);
        }

        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] searchPaths =
        {
            // Relative to bin/Debug/...
            Path.Combine(baseDir, "stockfish", "stockfish-windows-x86-64-universal.exe"),
            Path.Combine(baseDir, "stockfish-windows-x86-64-universal.exe"),
            Path.Combine(baseDir, "Engines", "stockfish.exe"),
            Path.Combine(baseDir, "stockfish.exe"),

            // Check parent directories up to project root
            Path.Combine(baseDir, "..", "..", "..", "stockfish", "stockfish-windows-x86-64-universal.exe"),
            Path.Combine(baseDir, "..", "..", "..", "stockfish.exe"),
            Path.Combine(baseDir, "..", "..", "..", "..", "stockfish", "stockfish-windows-x86-64-universal.exe"),

            // Direct workspace search
            Path.Combine(Directory.GetCurrentDirectory(), "stockfish", "stockfish-windows-x86-64-universal.exe"),
            Path.Combine(Directory.GetCurrentDirectory(), "ChessAnalysis", "stockfish", "stockfish-windows-x86-64-universal.exe")
        };

        foreach (var p in searchPaths)
        {
            try
            {
                string full = Path.GetFullPath(p);
                if (File.Exists(full))
                {
                    return full;
                }
            }
            catch { }
        }

        // Try wildcard search in project directory
        try
        {
            string current = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 5; i++)
            {
                var files = Directory.GetFiles(current, "stockfish*.exe", SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    return files[0];
                }
                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }
        }
        catch { }

        return null;
    }

    public async Task StartAsync(int threads = 4, int hashMb = 128)
    {
        if (string.IsNullOrWhiteSpace(EnginePath) || !File.Exists(EnginePath))
        {
            throw new FileNotFoundException($"Файл Stockfish не найден по пути: {EnginePath ?? "(не указан)"}");
        }

        Stop();

        var psi = new ProcessStartInfo
        {
            FileName = EnginePath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        _process = Process.Start(psi) ?? throw new InvalidOperationException("Не удалось запустить процесс Stockfish.");
        _input = _process.StandardInput;

        await SendCommandAsync("uci");
        await WaitForResponseAsync("uciok", 5000);

        await SendCommandAsync($"setoption name Threads value {Math.Max(1, threads)}");
        await SendCommandAsync($"setoption name Hash value {Math.Max(16, hashMb)}");
        await SendCommandAsync("setoption name MultiPV value 2");
        await SendCommandAsync("isready");
        await WaitForResponseAsync("readyok", 3000);
    }

    public async Task NewGameAsync()
    {
        await SendCommandAsync("ucinewgame");
        await SendCommandAsync("isready");
        await WaitForResponseAsync("readyok", 2000);
    }

    public async Task<EngineEvaluation> AnalyzePositionAsync(
        string fen,
        int? depth = null,
        long? nodes = null,
        int? movetimeMs = null,
        CancellationToken ct = default)
    {
        if (_process == null || _process.HasExited || _input == null)
        {
            await StartAsync();
        }

        var eval = new EngineEvaluation { Depth = depth ?? 14 };
        string[] fenParts = fen.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool isWhiteTurn = fenParts.Length < 2 || fenParts[1] != "b";

        string goCmd = "go";
        if (nodes.HasValue)
        {
            goCmd += $" nodes {nodes.Value}";
        }
        else if (movetimeMs.HasValue)
        {
            goCmd += $" movetime {movetimeMs.Value}";
        }
        else if (depth.HasValue)
        {
            goCmd += $" depth {depth.Value}";
        }
        else
        {
            // Default: 600,000 nodes budget (~100-150ms), matching Arslanov Chess analysis volume!
            goCmd += " nodes 600000";
        }

        lock (_lock)
        {
            _input!.WriteLine($"position fen {fen}");
            _input.WriteLine(goCmd);
            _input.Flush();
        }

        var tcs = new TaskCompletionSource<bool>();
        using var reg = ct.Register(() =>
        {
            try
            {
                lock (_lock)
                {
                    _input?.WriteLine("stop");
                    _input?.Flush();
                }
            }
            catch { }
            tcs.TrySetCanceled(ct);
        });

        int bestCpSide = 0;
        int? bestMateSide = null;
        int secondCpSide = 0;
        int? secondMateSide = null;
        bool hasSecond = false;
        string bestMoveUci = string.Empty;
        string secondMoveUci = string.Empty;
        var pvMoves = new List<string>();

        while (!tcs.Task.IsCompleted)
        {
            string? line = await _process!.StandardOutput.ReadLineAsync(ct);
            if (line == null) break;

            if (line.StartsWith("bestmove"))
            {
                string[] parts = line.Split(' ');
                if (parts.Length > 1 && parts[1] != "(none)")
                {
                    bestMoveUci = parts[1];
                }
                else if (parts.Length > 1 && parts[1] == "(none)" && bestCpSide == 0 && !bestMateSide.HasValue)
                {
                    var checkBoard = new BoardState();
                    checkBoard.LoadFen(fen);
                    if (checkBoard.IsInCheck(checkBoard.SideToMove))
                    {
                        // Current side to move is in checkmate!
                        bestMateSide = 0;
                        bestCpSide = -100000;
                    }
                }
                tcs.TrySetResult(true);
                break;
            }

            if (line.StartsWith("info ") && line.Contains(" score "))
            {
                var depthMatch = Regex.Match(line, @"\bdepth\s+(\d+)");
                if (depthMatch.Success && int.TryParse(depthMatch.Groups[1].Value, out int d))
                {
                    eval.Depth = d;
                }

                int multipv = 1;
                var mpMatch = Regex.Match(line, @"\bmultipv\s+(\d+)");
                if (mpMatch.Success)
                {
                    int.TryParse(mpMatch.Groups[1].Value, out multipv);
                }

                int cp = 0;
                int? mate = null;

                var cpMatch = Regex.Match(line, @"\bscore\s+cp\s+(-?\d+)");
                if (cpMatch.Success)
                {
                    int.TryParse(cpMatch.Groups[1].Value, out cp);
                }

                var mateMatch = Regex.Match(line, @"\bscore\s+mate\s+(-?\d+)");
                if (mateMatch.Success)
                {
                    if (int.TryParse(mateMatch.Groups[1].Value, out int m))
                    {
                        mate = m;
                    }
                }

                if (multipv == 1)
                {
                    bestCpSide = cp;
                    bestMateSide = mate;

                    int pvIdx = line.IndexOf(" pv ");
                    if (pvIdx >= 0)
                    {
                        pvMoves = line[(pvIdx + 4)..].Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
                    }
                }
                else if (multipv == 2)
                {
                    secondCpSide = cp;
                    secondMateSide = mate;
                    hasSecond = true;

                    int pvIdx = line.IndexOf(" pv ");
                    if (pvIdx >= 0)
                    {
                        var parts = line[(pvIdx + 4)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 0) secondMoveUci = parts[0];
                    }
                }
            }
        }

        // Convert Stockfish side-to-move score to White's absolute score
        eval.Centipawns = isWhiteTurn ? bestCpSide : -bestCpSide;
        eval.MateInMoves = bestMateSide.HasValue
            ? (isWhiteTurn ? bestMateSide.Value : -bestMateSide.Value)
            : null;

        eval.SecondBestCentipawns = isWhiteTurn ? secondCpSide : -secondCpSide;
        eval.SecondBestMateIn = secondMateSide.HasValue
            ? (isWhiteTurn ? secondMateSide.Value : -secondMateSide.Value)
            : null;
        eval.SecondBestMoveUci = secondMoveUci;
        eval.HasSecondBest = hasSecond;

        eval.BestMoveUci = bestMoveUci;
        eval.Pv.Clear();
        eval.Pv.AddRange(pvMoves);

        return eval;
    }

    private async Task SendCommandAsync(string cmd)
    {
        if (_input == null) return;
        await _input.WriteLineAsync(cmd);
        await _input.FlushAsync();
    }

    private async Task<bool> WaitForResponseAsync(string expectedText, int timeoutMs)
    {
        if (_process == null) return false;
        using var cts = new CancellationTokenSource(timeoutMs);

        try
        {
            while (!cts.IsCancellationRequested)
            {
                string? line = await _process.StandardOutput.ReadLineAsync(cts.Token);
                if (line != null && line.Contains(expectedText))
                {
                    return true;
                }
            }
        }
        catch (OperationCanceledException) { }

        return false;
    }

    public void Stop()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _input?.WriteLine("quit");
                _input?.Flush();
                if (!_process.WaitForExit(1000))
                {
                    _process.Kill();
                }
            }
        }
        catch { }
        finally
        {
            _process?.Dispose();
            _process = null;
            _input = null;
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }
}
