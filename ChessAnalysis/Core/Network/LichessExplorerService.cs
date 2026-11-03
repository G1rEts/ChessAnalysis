using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using ChessAnalysis.Core.Chess;

namespace ChessAnalysis.Core.Network;

public class LichessExplorerMove
{
    public string Uci { get; set; } = string.Empty;
    public string San { get; set; } = string.Empty;
    public Square From => Uci.Length >= 4 ? Square.FromAlgebraic(Uci[0..2]) : Square.None;
    public Square To => Uci.Length >= 4 ? Square.FromAlgebraic(Uci[2..4]) : Square.None;
    public long WhiteWins { get; set; }
    public long Draws { get; set; }
    public long BlackWins { get; set; }
    public long TotalGames => WhiteWins + Draws + BlackWins;
    public double WinRateWhite => TotalGames > 0 ? (WhiteWins + 0.5 * Draws) / TotalGames : 0.0;
    public double WinRateBlack => TotalGames > 0 ? (BlackWins + 0.5 * Draws) / TotalGames : 0.0;
}

public class LichessExplorerResult
{
    public string? Eco { get; set; }
    public string? OpeningName { get; set; }
    public List<LichessExplorerMove> Moves { get; set; } = new();

    public LichessExplorerMove? MostPopularMove => Moves.Count > 0 ? Moves[0] : null;

    public LichessExplorerMove? GetStrongestMove(PieceColor sideToMove)
    {
        if (Moves.Count == 0) return null;
        if (Moves.Count == 1) return Moves[0];

        // Filter out moves that have very few games compared to the main line (threshold: 5% of top move games or >= 20 games)
        long minGames = Math.Max(20, (long)(Moves[0].TotalGames * 0.05));
        var candidates = Moves.Where(m => m.TotalGames >= minGames).ToList();
        if (candidates.Count == 0) candidates = Moves;

        return sideToMove == PieceColor.White
            ? candidates.OrderByDescending(m => m.WinRateWhite).FirstOrDefault()
            : candidates.OrderByDescending(m => m.WinRateBlack).FirstOrDefault();
    }
}

public static class LichessExplorerService
{
    private static readonly HttpClient _httpClient = new();
    private static readonly ConcurrentDictionary<string, LichessExplorerResult?> _cache = new(StringComparer.OrdinalIgnoreCase);

    static LichessExplorerService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.Clear();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ChessAnalysisApp", "1.0"));
        _httpClient.Timeout = TimeSpan.FromSeconds(5);
    }

    public static async Task<LichessExplorerResult?> GetPositionMovesAsync(string fen, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fen)) return null;

        // Simplify FEN to board + turn + castling + ep
        string normFen = NormalizeFen(fen);
        if (_cache.TryGetValue(normFen, out var cached))
        {
            return cached;
        }

        try
        {
            // First try the comprehensive Lichess database without rating filter so all amateur and master games are included
            string url = $"https://explorer.lichess.ovh/lichess?fen={Uri.EscapeDataString(normFen)}&moves=5";
            using var response = await _httpClient.GetAsync(url, ct);

            string json;
            if (response.IsSuccessStatusCode)
            {
                json = await response.Content.ReadAsStringAsync(ct);
            }
            else
            {
                // Fallback to the Masters database
                string mastersUrl = $"https://explorer.lichess.ovh/masters?fen={Uri.EscapeDataString(normFen)}&moves=5";
                using var mastersResp = await _httpClient.GetAsync(mastersUrl, ct);
                if (!mastersResp.IsSuccessStatusCode)
                {
                    _cache[normFen] = null;
                    return null;
                }
                json = await mastersResp.Content.ReadAsStringAsync(ct);
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var result = new LichessExplorerResult();
            if (root.TryGetProperty("opening", out var openEl) && openEl.ValueKind == JsonValueKind.Object)
            {
                if (openEl.TryGetProperty("eco", out var ecoProp)) result.Eco = ecoProp.GetString();
                if (openEl.TryGetProperty("name", out var nameProp)) result.OpeningName = nameProp.GetString();
            }

            if (root.TryGetProperty("moves", out var movesEl) && movesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var mEl in movesEl.EnumerateArray())
                {
                    string uci = mEl.TryGetProperty("uci", out var u) ? (u.GetString() ?? "") : "";
                    string san = mEl.TryGetProperty("san", out var s) ? (s.GetString() ?? "") : "";
                    long w = mEl.TryGetProperty("white", out var we) && we.TryGetInt64(out var wv) ? wv : 0;
                    long d = mEl.TryGetProperty("draws", out var de) && de.TryGetInt64(out var dv) ? dv : 0;
                    long b = mEl.TryGetProperty("black", out var be) && be.TryGetInt64(out var bv) ? bv : 0;

                    if (!string.IsNullOrEmpty(uci))
                    {
                        result.Moves.Add(new LichessExplorerMove
                        {
                            Uci = uci,
                            San = san,
                            WhiteWins = w,
                            Draws = d,
                            BlackWins = b
                        });
                    }
                }
            }

            _cache[normFen] = result;
            return result;
        }
        catch
        {
            // Network unavailable or timeout -> safely return null
            return null;
        }
    }

    private static string NormalizeFen(string fen)
    {
        if (string.IsNullOrWhiteSpace(fen)) return string.Empty;
        var parts = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // Standard FEN requires 6 fields for Lichess API: piece-placement active-color castling en-passant halfmove fullmove
        return parts.Length >= 4 ? $"{parts[0]} {parts[1]} {parts[2]} {parts[3]} 0 1" : fen;
    }
}
