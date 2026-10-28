using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using ChessAnalysis.Core.Chess;

namespace ChessAnalysis.Core.Network;

public class DownloadedGameSummary
{
    public string Source { get; set; } = string.Empty;
    public string WhiteUser { get; set; } = string.Empty;
    public string BlackUser { get; set; } = string.Empty;
    public string WhiteRating { get; set; } = string.Empty;
    public string BlackRating { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string TimeControl { get; set; } = string.Empty;
    public string PgnText { get; set; } = string.Empty;

    public string DisplayTitle =>
        $"{Date} | {WhiteUser} ({WhiteRating}) vs {BlackUser} ({BlackRating}) — {Result} [{TimeControl}]";
}

public class GameDownloadService
{
    private static readonly HttpClient _httpClient = new();

    static GameDownloadService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.Clear();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ChessAnalysisApp", "1.0"));
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
    }

    public async Task<List<DownloadedGameSummary>> FetchChessComGamesAsync(string username, int maxCount = 20)
    {
        var list = new List<DownloadedGameSummary>();
        if (string.IsNullOrWhiteSpace(username)) return list;

        string cleanUser = username.Trim().ToLowerInvariant();

        // 1. Get archives
        string archivesUrl = $"https://api.chess.com/pub/player/{cleanUser}/games/archives";
        var response = await _httpClient.GetAsync(archivesUrl);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Игрок '{username}' не найден на Chess.com (HTTP {(int)response.StatusCode})");
        }

        string json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("archives", out var archivesEl) || archivesEl.GetArrayLength() == 0)
        {
            return list;
        }

        var archiveUrls = archivesEl.EnumerateArray()
            .Select(x => x.GetString())
            .Where(x => !string.IsNullOrEmpty(x))
            .ToList();

        // Iterate recent archives backwards
        for (int a = archiveUrls.Count - 1; a >= 0 && list.Count < maxCount; a--)
        {
            string? monthUrl = archiveUrls[a];
            if (string.IsNullOrEmpty(monthUrl)) continue;

            var monthResp = await _httpClient.GetAsync(monthUrl);
            if (!monthResp.IsSuccessStatusCode) continue;

            string monthJson = await monthResp.Content.ReadAsStringAsync();
            using var monthDoc = JsonDocument.Parse(monthJson);
            if (!monthDoc.RootElement.TryGetProperty("games", out var gamesEl)) continue;

            var gamesList = gamesEl.EnumerateArray().Reverse().ToList();
            foreach (var g in gamesList)
            {
                if (list.Count >= maxCount) break;

                string pgn = g.TryGetProperty("pgn", out var pgnEl) ? (pgnEl.GetString() ?? "") : "";
                if (string.IsNullOrWhiteSpace(pgn)) continue;

                string whiteUser = "";
                string whiteRating = "";
                if (g.TryGetProperty("white", out var wEl))
                {
                    whiteUser = wEl.TryGetProperty("username", out var u) ? (u.GetString() ?? "") : "";
                    whiteRating = wEl.TryGetProperty("rating", out var r) ? r.GetInt32().ToString() : "";
                }

                string blackUser = "";
                string blackRating = "";
                if (g.TryGetProperty("black", out var bEl))
                {
                    blackUser = bEl.TryGetProperty("username", out var u) ? (u.GetString() ?? "") : "";
                    blackRating = bEl.TryGetProperty("rating", out var r) ? r.GetInt32().ToString() : "";
                }

                string timeControl = g.TryGetProperty("time_class", out var tc) ? (tc.GetString() ?? "") : "";
                string result = "*";
                if (g.TryGetProperty("white", out var wRes) && wRes.TryGetProperty("result", out var rEl))
                {
                    string r = rEl.GetString() ?? "";
                    if (r == "win") result = "1-0";
                    else if (r is "agreed" or "repetition" or "stalemate" or "insufficient") result = "1/2-1/2";
                    else result = "0-1";
                }

                // Date
                string date = "";
                if (g.TryGetProperty("end_time", out var endTimeEl))
                {
                    long unix = endTimeEl.GetInt64();
                    date = DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("dd.MM.yyyy HH:mm");
                }

                list.Add(new DownloadedGameSummary
                {
                    Source = "Chess.com",
                    WhiteUser = whiteUser,
                    BlackUser = blackUser,
                    WhiteRating = whiteRating,
                    BlackRating = blackRating,
                    Result = result,
                    Date = date,
                    TimeControl = timeControl,
                    PgnText = pgn
                });
            }
        }

        return list;
    }

    public async Task<List<DownloadedGameSummary>> FetchLichessGamesAsync(string username, int maxCount = 20)
    {
        var list = new List<DownloadedGameSummary>();
        if (string.IsNullOrWhiteSpace(username)) return list;

        string cleanUser = username.Trim();
        string url = $"https://lichess.org/api/games/user/{cleanUser}?max={maxCount}&pgnInBody=true&clocks=false&evals=false";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Clear();
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-chess-pgn"));

        var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Игрок '{username}' не найден на Lichess (HTTP {(int)response.StatusCode})");
        }

        string rawPgn = await response.Content.ReadAsStringAsync();
        var parsedGames = PgnParser.ParseGames(rawPgn);

        foreach (var g in parsedGames)
        {
            list.Add(new DownloadedGameSummary
            {
                Source = "Lichess",
                WhiteUser = g.White,
                BlackUser = g.Black,
                WhiteRating = g.WhiteElo,
                BlackRating = g.BlackElo,
                Result = g.Result,
                Date = g.Date,
                TimeControl = g.TimeControl,
                PgnText = FormatSingleGamePgn(g)
            });
        }

        return list;
    }

    private static string FormatSingleGamePgn(PgnGame game)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var kvp in game.Headers)
        {
            sb.AppendLine($"[{kvp.Key} \"{kvp.Value}\"]");
        }
        sb.AppendLine();
        for (int i = 0; i < game.Moves.Count; i++)
        {
            var m = game.Moves[i];
            if (m.Color == PieceColor.White)
            {
                sb.Append($"{m.MoveNumber}. {m.San} ");
            }
            else
            {
                sb.Append($"{m.San} ");
            }
        }
        sb.AppendLine(game.Result);
        return sb.ToString();
    }
}
