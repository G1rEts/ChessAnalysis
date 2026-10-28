namespace ChessAnalysis.Core.Chess;

public static class OpeningBook
{
    private static readonly HashSet<string> BookPositions = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> OpeningNames = new(StringComparer.OrdinalIgnoreCase);
    private static bool _initialized = false;

    public static int LoadedPositionsCount => BookPositions.Count;
    public static int LoadedOpeningsCount => OpeningNames.Count;

    static OpeningBook()
    {
        Initialize();
    }

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        // 1. Built-in core lines as base fallback
        LoadBuiltinLines();

        // 2. Discover and load Lichess TSV files from Assets/Openings/
        LoadExternalTsvFiles();
    }

    private static void LoadExternalTsvFiles()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] candidateDirs =
        {
            Path.Combine(baseDir, "Assets", "Openings"),
            Path.Combine(baseDir, "..", "..", "..", "Assets", "Openings"),
            Path.Combine(baseDir, "..", "..", "..", "..", "Assets", "Openings"),
            Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Openings"),
            Path.Combine(Directory.GetCurrentDirectory(), "ChessAnalysis", "Assets", "Openings")
        };

        foreach (var dir in candidateDirs)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    var tsvFiles = Directory.GetFiles(dir, "*.tsv", SearchOption.TopDirectoryOnly);
                    foreach (var file in tsvFiles)
                    {
                        LoadLichessTsvFile(file);
                    }
                }
            }
            catch { }
        }
    }

    public static void LoadLichessTsvFile(string filePath)
    {
        if (!File.Exists(filePath)) return;

        try
        {
            using var reader = new StreamReader(filePath);
            string? line;
            bool isHeader = true;

            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Skip header line if present
                if (isHeader && (line.StartsWith("eco\t") || line.StartsWith("ECO\t")))
                {
                    isHeader = false;
                    continue;
                }
                isHeader = false;

                string[] parts = line.Split('\t');
                if (parts.Length < 2) continue;

                string eco = parts[0].Trim();
                string name = parts[1].Trim();
                string fullName = string.IsNullOrEmpty(eco) ? name : $"{eco} {name}";

                // Check if epd/fen column is present (usually column index 4)
                if (parts.Length >= 5 && !string.IsNullOrWhiteSpace(parts[4]))
                {
                    string epd = parts[4].Trim();
                    string norm = NormalizeFen(epd);
                    BookPositions.Add(norm);
                    OpeningNames[norm] = fullName;
                }

                // If UCI moves are present (usually column index 3)
                if (parts.Length >= 4 && !string.IsNullOrWhiteSpace(parts[3]))
                {
                    string uciMoves = parts[3].Trim();
                    ApplyUciLine(uciMoves, fullName);
                }
                // If PGN notation is present (column index 2, like standard Lichess TSV: eco \t name \t pgn)
                else if (parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2]))
                {
                    string pgnMoves = parts[2].Trim();
                    ApplyPgnLine(pgnMoves, fullName);
                }
            }
        }
        catch { }
    }

    private static void ApplyPgnLine(string pgnLine, string fullName)
    {
        try
        {
            var board = new BoardState();
            BookPositions.Add(NormalizeFen(board.ToFen()));

            string[] tokens = pgnLine.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            string lastNorm = "";

            foreach (var rawToken in tokens)
            {
                string tok = rawToken.Trim();
                if (string.IsNullOrEmpty(tok)) continue;
                if (tok.EndsWith(".")) continue;
                if (tok.Contains('.'))
                {
                    int dotIdx = tok.LastIndexOf('.');
                    tok = tok[(dotIdx + 1)..];
                    if (string.IsNullOrEmpty(tok)) continue;
                }

                var move = board.ParseSan(tok);
                if (move.IsEmpty)
                {
                    move = board.ParseSan(tok.TrimEnd('!', '?', '+', '#'));
                }

                if (move.IsEmpty) break;

                board.ApplyMove(move);
                string norm = NormalizeFen(board.ToFen());
                BookPositions.Add(norm);
                lastNorm = norm;
                if (!string.IsNullOrEmpty(fullName))
                {
                    OpeningNames.TryAdd(norm, fullName);
                }
            }

            if (!string.IsNullOrEmpty(lastNorm) && !string.IsNullOrEmpty(fullName))
            {
                OpeningNames[lastNorm] = fullName;
            }
        }
        catch { }
    }

    private static void ApplyUciLine(string uciMoves, string fullName)
    {
        var board = new BoardState();
        BookPositions.Add(NormalizeFen(board.ToFen()));

        string[] moves = uciMoves.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < moves.Length; i++)
        {
            var move = Move.FromUci(moves[i]);
            if (move.IsEmpty) break;
            board.ApplyMove(move);

            string norm = NormalizeFen(board.ToFen());
            BookPositions.Add(norm);
            if (!string.IsNullOrEmpty(fullName))
            {
                OpeningNames.TryAdd(norm, fullName);
            }
            if (i == moves.Length - 1 && !string.IsNullOrEmpty(fullName))
            {
                OpeningNames[norm] = fullName;
            }
        }
    }

    private static void LoadBuiltinLines()
    {
        (string name, string moves)[] commonLines =
        {
            ("C60 Ruy Lopez", "e2e4 e7e5 g1f3 b8c6 f1b5 a7a6 b5a4 g8f6 e1g1 f8e7 f1e1 b7b5 a4b3 d7d6 c2c3 e8g8"),
            ("C50 Italian Game", "e2e4 e7e5 g1f3 b8c6 f1c4 f8c5 c2c3 g8f6 d2d3 d7d6 e1g1 e8g8"),
            ("C45 Scotch Game", "e2e4 e7e5 g1f3 b8c6 d2d4 e5d4 f3d4"),
            ("C42 Petrov Defense", "e2e4 e7e5 g1f3 g8f6 f3e5 d7d6 e5f3 f6e4 d2d4 d6d5"),
            ("B90 Sicilian Defense: Najdorf", "e2e4 c7c5 g1f3 d7d6 d2d4 c5d4 f3d4 g8f6 b1c3 a7a6 f1e2 e7e5"),
            ("B33 Sicilian Defense: Sveshnikov", "e2e4 c7c5 g1f3 b8c6 d2d4 c5d4 f3d4 g8f6 b1c3 e7e5"),
            ("B40 Sicilian Defense: French Variation", "e2e4 c7c5 g1f3 e7e6 d2d4 c5d4 f3d4 a7a6"),
            ("B22 Sicilian Defense: Alapin Variation", "e2e4 c7c5 c2c3 g8f6 e4e5 f6d5 d2d4 c5d4"),
            ("C00 French Defense", "e2e4 e7e6 d2d4 d7d5 b1c3 g8f6 e4e5 f6d7 f2f4 c7c5"),
            ("C15 French Defense: Winawer", "e2e4 e7e6 d2d4 d7d5 b1c3 f8b4 e4e5 c7c5 a2a3 b4c3"),
            ("B10 Caro-Kann Defense", "e2e4 c7c6 d2d4 d7d5 b1c3 d5e4 c3e4 c8f5 e4g3 f5g6"),
            ("B12 Caro-Kann: Advance Variation", "e2e4 c7c6 d2d4 d7d5 e4e5 c8f5 g1f3 e7e6 f1e2 c6c5"),
            ("B01 Scandinavian Defense", "e2e4 d7d5 e4d5 d8d5 b1c3 d5a5 d2d4"),
            ("D37 Queen's Gambit Declined", "d2d4 d7d5 c2c4 e7e6 b1c3 g8f6 c1g5 f8e7 e2e3 e8g8 g1f3"),
            ("D10 Slav Defense", "d2d4 d7d5 c2c4 c7c6 g1f3 g8f6 b1c3 d5c4 a2a4"),
            ("E60 King's Indian Defense", "d2d4 g8f6 c2c4 g7g6 b1c3 f8g7 e2e4 d7d6 g1f3 e8g8 f1e2 e7e5"),
            ("D80 Grunfeld Defense", "d2d4 g8f6 c2c4 g7g6 b1c3 d7d5 c4d5 f6d5 e2e4 d5c3 b2c3"),
            ("E20 Nimzo-Indian Defense", "d2d4 g8f6 c2c4 e7e6 b1c3 f8b4 e2e3 e8g8 f1d3 d7d5"),
            ("E12 Queen's Indian Defense", "d2d4 g8f6 c2c4 e7e6 g1f3 b7b6 g2g3 c8a6 b2b3"),
            ("A10 English Opening", "c2c4 e7e5 b1c3 g8f6 g1f3 b8c6 g2g3 d7d5 c4d5 f6d5"),
            ("A04 Reti Opening", "g1f3 d7d5 g2g3 g8f6 f1g2 c7c6 e1g1")
        };

        foreach (var (name, moves) in commonLines)
        {
            ApplyUciLine(moves, name);
        }
    }

    public static string NormalizeFen(string fen)
    {
        string[] parts = fen.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3)
        {
            return $"{parts[0]} {parts[1]} {parts[2]}";
        }
        return fen.Trim();
    }

    public static bool IsBookPosition(string fen)
    {
        return BookPositions.Contains(NormalizeFen(fen));
    }

    public static string? GetOpeningName(string fen)
    {
        string norm = NormalizeFen(fen);
        return OpeningNames.TryGetValue(norm, out var name) ? name : null;
    }
}
