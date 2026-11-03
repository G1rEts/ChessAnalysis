using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using ChessAnalysis.Core.Chess;

namespace ChessAnalysis.Core.Engine;

public class StockfishEngine : IDisposable
{
    private Process? _process;
    private StreamWriter? _input;
    private readonly SemaphoreSlim _ioSemaphore = new(1, 1);
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
            Path.Combine(Directory.GetCurrentDirectory(), "ChessAnalysis", "stockfish", "stockfish-windows-x86-64-universal.exe"),

            // Global standard drive C paths
            @"C:\stockfish\stockfish-windows-x86-64-universal.exe",
            @"C:\stockfish\stockfish.exe",
            @"C:\stockfish-windows-x86-64-universal.exe",
            @"C:\stockfish.exe",
            @"C:\Engines\stockfish.exe"
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

        // Try standard drive C directory
        try
        {
            if (Directory.Exists(@"C:\stockfish"))
            {
                var files = Directory.GetFiles(@"C:\stockfish", "stockfish*.exe", SearchOption.AllDirectories);
                if (files.Length > 0) return files[0];
            }
        }
        catch { }

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

    /// <summary>
    /// Проверяет работоспособность UCI-движка по указанному пути.
    /// Отправляет команду "uci" и ожидает ответ "uciok".
    /// </summary>
    public static bool ValidateEngine(string path, out string engineName, out string error)
    {
        engineName = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            error = "Файл не существует или путь не указан.";
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = path,
                WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardInputEncoding = new UTF8Encoding(false),
                StandardOutputEncoding = new UTF8Encoding(false)
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                error = "Не удалось запустить процесс.";
                return false;
            }

            process.StandardInput.WriteLine("uci");
            process.StandardInput.Flush();

            string foundName = Path.GetFileName(path);
            bool gotUciOk = false;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 3500)
            {
                string? line = process.StandardOutput.ReadLine();
                if (line == null) break;
                line = line.Trim();

                if (line.StartsWith("id name ", StringComparison.OrdinalIgnoreCase))
                {
                    foundName = line.Substring(8).Trim();
                }
                else if (line.Equals("uciok", StringComparison.OrdinalIgnoreCase))
                {
                    gotUciOk = true;
                    break;
                }
            }

            try
            {
                process.StandardInput.WriteLine("quit");
                process.StandardInput.Flush();
                if (!process.WaitForExit(500))
                {
                    process.Kill();
                }
            }
            catch { }

            if (gotUciOk)
            {
                engineName = foundName;
                return true;
            }
            else
            {
                error = "Движок не ответил 'uciok' на запрос UCI протокола.";
                return false;
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public async Task StartAsync(int threads = 4, int hashMb = 128)
    {
        await _ioSemaphore.WaitAsync();
        try
        {
            await StartInternalAsync(threads, hashMb);
        }
        finally
        {
            _ioSemaphore.Release();
        }
    }

    private async Task StartInternalAsync(int threads = 4, int hashMb = 128)
    {
        if (string.IsNullOrWhiteSpace(EnginePath) || !File.Exists(EnginePath))
        {
            throw new FileNotFoundException($"Файл Stockfish не найден по пути: {EnginePath ?? "(не указан)"}");
        }

        StopInternal();

        string workDir = Path.GetDirectoryName(EnginePath) ?? string.Empty;
#if ANDROID || __ANDROID__
        string androidAppDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(androidAppDir) || !Directory.Exists(androidAppDir))
        {
            androidAppDir = "/data/user/0/com.antigravity.chessanalysis/files";
        }
        if (Directory.Exists(androidAppDir))
        {
            workDir = androidAppDir;
        }
#endif

        var psi = new ProcessStartInfo
        {
            FileName = EnginePath,
            WorkingDirectory = workDir,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false)
        };

        _process = Process.Start(psi) ?? throw new InvalidOperationException("Не удалось запустить процесс Stockfish.");
        _input = _process.StandardInput;

        SendCommandDirect("uci");
        bool gotUciOk = await WaitForResponseDirectAsync("uciok", 5000);
        if (!gotUciOk)
        {
            int exitCode = -1;
            bool exited = true;
            string stdErr = string.Empty;
            try
            {
                exited = _process.HasExited;
                if (exited) exitCode = _process.ExitCode;
                stdErr = await _process.StandardError.ReadToEndAsync();
            }
            catch { }
            throw new InvalidOperationException($"Движок Stockfish не ответил 'uciok' за 5 сек (exited={exited}, code={exitCode}, err={stdErr}, path={EnginePath})");
        }

        // Configure NNUE EvalFile if network file is located on device
        string[] potentialNnueDirs =
        [
            workDir,
            Path.GetDirectoryName(EnginePath) ?? string.Empty,
            "/data/user/0/com.antigravity.chessanalysis/files"
        ];

        string? foundNnue = null;
        foreach (var dir in potentialNnueDirs)
        {
            if (string.IsNullOrEmpty(dir)) continue;
            string p = Path.Combine(dir, "nn-1a298aa575a0.nnue");
            if (File.Exists(p))
            {
                foundNnue = p;
                break;
            }
        }

        if (!string.IsNullOrEmpty(foundNnue))
        {
            SendCommandDirect($"setoption name EvalFile value {foundNnue}");
        }

        SendCommandDirect($"setoption name Threads value {Math.Max(1, threads)}");
        SendCommandDirect($"setoption name Hash value {Math.Max(16, hashMb)}");
        SendCommandDirect("setoption name MultiPV value 5");
        SendCommandDirect("isready");
        bool gotReadyOk = await WaitForResponseDirectAsync("readyok", 4000);
        if (!gotReadyOk)
        {
            string stdErr = string.Empty;
            try { stdErr = await _process.StandardError.ReadToEndAsync(); } catch { }
            throw new InvalidOperationException($"Движок Stockfish не ответил 'readyok' за 4 сек (err={stdErr}).");
        }
    }

    public async Task NewGameAsync()
    {
        await _ioSemaphore.WaitAsync();
        try
        {
            SendCommandDirect("ucinewgame");
            SendCommandDirect("setoption name MultiPV value 5");
            SendCommandDirect("isready");
            await WaitForResponseDirectAsync("readyok", 2000);
        }
        finally
        {
            _ioSemaphore.Release();
        }
    }

    public async Task<EngineEvaluation> AnalyzePositionAsync(
        string fen,
        int? depth = null,
        long? nodes = null,
        int? movetimeMs = null,
        CancellationToken ct = default)
    {
        await _ioSemaphore.WaitAsync(ct);
        try
        {
            if (_process == null || _process.HasExited || _input == null)
            {
                await StartInternalAsync();
            }

            if (string.IsNullOrWhiteSpace(fen))
            {
                fen = BoardState.StartingFen;
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

            SendCommandDirect($"position fen {fen}");
            SendCommandDirect(goCmd);

            int bestCpSide = 0;
            int? bestMateSide = null;
            int secondCpSide = 0;
            int? secondMateSide = null;
            bool hasSecond = false;
            string bestMoveUci = string.Empty;
            string secondMoveUci = string.Empty;
            var pvMoves = new List<string>();
            var linesDict = new Dictionary<int, EngineLine>();

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    string? line = await _process!.StandardOutput.ReadLineAsync(ct);
                    if (line == null)
                    {
                        if (_process.HasExited)
                        {
                            throw new InvalidOperationException($"Процесс Stockfish неожиданно завершился во время анализа (код {_process.ExitCode}).");
                        }
                        break;
                    }

                    if (line.StartsWith("bestmove"))
                    {
                        string[] parts = line.Split(' ');
                        if (parts.Length > 1 && parts[1] != "(none)")
                        {
                            bestMoveUci = parts[1];
                        }
                        else if (parts.Length > 1 && parts[1] == "(none)")
                        {
                            var checkBoard = new BoardState();
                            checkBoard.LoadFen(fen);
                            if (checkBoard.IsInCheck(checkBoard.SideToMove))
                            {
                                // Current side to move is in checkmate!
                                bestMateSide = 0;
                                bestCpSide = -100000;
                            }
                            else if (checkBoard.GenerateLegalMoves().Count == 0)
                            {
                                // Stalemate
                                bestMateSide = null;
                                bestCpSide = 0;
                            }
                        }
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
                                cp = m > 0 ? 100000 : -100000;
                            }
                        }

                        int pvIdx = line.IndexOf(" pv ");
                        List<string> currentLinePv = pvIdx >= 0
                            ? line[(pvIdx + 4)..].Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList()
                            : new List<string>();

                        linesDict[multipv] = new EngineLine
                        {
                            MultiPv = multipv,
                            Centipawns = isWhiteTurn ? cp : -cp,
                            MateInMoves = mate.HasValue ? (isWhiteTurn ? mate.Value : -mate.Value) : null,
                            Pv = currentLinePv
                        };

                        if (multipv == 1)
                        {
                            bestCpSide = cp;
                            bestMateSide = mate;
                            pvMoves = currentLinePv;
                        }
                        else if (multipv == 2)
                        {
                            secondCpSide = cp;
                            secondMateSide = mate;
                            hasSecond = true;
                            if (currentLinePv.Count > 0) secondMoveUci = currentLinePv[0];
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Engine search cancelled: send stop, drain stream and synchronize with isready
                try
                {
                    if (_process is { HasExited: false } && _input != null)
                    {
                        await _input.WriteLineAsync("stop");
                        await _input.FlushAsync();

                        using var drainCts = new CancellationTokenSource(600);
                        while (!drainCts.IsCancellationRequested)
                        {
                            string? drainLine = await _process.StandardOutput.ReadLineAsync(drainCts.Token);
                            if (drainLine == null || drainLine.StartsWith("bestmove")) break;
                        }

                        // Synchronize UCI state
                        SendCommandDirect("isready");
                        await WaitForResponseDirectAsync("readyok", 1500);
                    }
                }
                catch { }

                throw;
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

            eval.Lines.Clear();
            foreach (var kvp in linesDict.OrderBy(k => k.Key))
            {
                eval.Lines.Add(kvp.Value);
            }

            return eval;
        }
        finally
        {
            _ioSemaphore.Release();
        }
    }

    public async Task SetMultiPvAsync(int lines)
    {
        await _ioSemaphore.WaitAsync();
        try
        {
            SendCommandDirect($"setoption name MultiPV value {Math.Clamp(lines, 1, 5)}");
            SendCommandDirect("isready");
            await WaitForResponseDirectAsync("readyok", 1500);
        }
        finally
        {
            _ioSemaphore.Release();
        }
    }

    public async Task SetThreadsAsync(int threads)
    {
        await _ioSemaphore.WaitAsync();
        try
        {
            SendCommandDirect($"setoption name Threads value {Math.Max(1, threads)}");
            SendCommandDirect("isready");
            await WaitForResponseDirectAsync("readyok", 1500);
        }
        finally
        {
            _ioSemaphore.Release();
        }
    }

    public async Task SetHashAsync(int hashMb)
    {
        await _ioSemaphore.WaitAsync();
        try
        {
            SendCommandDirect($"setoption name Hash value {Math.Max(16, hashMb)}");
            SendCommandDirect("isready");
            await WaitForResponseDirectAsync("readyok", 1500);
        }
        finally
        {
            _ioSemaphore.Release();
        }
    }

    private void SendCommandDirect(string cmd)
    {
        if (_input == null) return;
        _input.WriteLine(cmd);
        _input.Flush();
    }

    private async Task<bool> WaitForResponseDirectAsync(string expectedText, int timeoutMs)
    {
        if (_process == null) return false;

        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        Task<string?>? pendingRead = null;

        while (DateTime.UtcNow < deadline)
        {
            if (_process.HasExited) return false;

            pendingRead ??= _process.StandardOutput.ReadLineAsync();
            int remainingMs = (int)Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds);
            var delayTask = Task.Delay(remainingMs);

            var completed = await Task.WhenAny(pendingRead, delayTask);
            if (completed == pendingRead)
            {
                string? line = await pendingRead;
                pendingRead = null;
                if (line == null)
                {
                    // Stream closed / EOF
                    return false;
                }
                if (line.Contains(expectedText, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else
            {
                // Timeout reached
                break;
            }
        }

        return false;
    }

    private void StopInternal()
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

    public void Stop()
    {
        _ioSemaphore.Wait(1000);
        try
        {
            StopInternal();
        }
        finally
        {
            _ioSemaphore.Release();
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        Stop();
        _ioSemaphore.Dispose();
        GC.SuppressFinalize(this);
    }
}
