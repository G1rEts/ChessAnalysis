using System.Text;
using System.Text.RegularExpressions;

namespace ChessAnalysis.Core.Chess;

public static partial class PgnParser
{
    public static List<PgnGame> ParseGames(string pgnText)
    {
        var games = new List<PgnGame>();
        if (string.IsNullOrWhiteSpace(pgnText)) return games;

        // Normalize line endings
        string normalized = pgnText.Replace("\r\n", "\n").Replace('\r', '\n');
        string[] rawGames = Regex.Split(normalized, @"(?m)^(?=\[Event\s)", RegexOptions.Multiline);

        foreach (var raw in rawGames)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var game = ParseSingleGame(raw);
            if (game != null && (game.Moves.Count > 0 || game.Headers.Count > 0))
            {
                games.Add(game);
            }
        }

        // If no [Event] header was found, try parsing as a single game
        if (games.Count == 0)
        {
            var single = ParseSingleGame(normalized);
            if (single != null && single.Moves.Count > 0)
            {
                games.Add(single);
            }
        }

        return games;
    }

    public static PgnGame? ParseSingleGame(string rawGame)
    {
        if (string.IsNullOrWhiteSpace(rawGame)) return null;

        var game = new PgnGame();
        var lines = rawGame.Split('\n');
        var movesBuilder = new StringBuilder();

        bool inHeaders = true;
        foreach (var line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("[") && trimmed.EndsWith("]") && inHeaders)
            {
                int firstSpace = trimmed.IndexOf(' ');
                if (firstSpace > 1)
                {
                    string key = trimmed[1..firstSpace].Trim();
                    int firstQuote = trimmed.IndexOf('"', firstSpace);
                    int lastQuote = trimmed.LastIndexOf('"');
                    if (firstQuote >= 0 && lastQuote > firstQuote)
                    {
                        string val = trimmed.Substring(firstQuote + 1, lastQuote - firstQuote - 1);
                        game.Headers[key] = val;
                    }
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(trimmed) && inHeaders && !trimmed.StartsWith("["))
                {
                    inHeaders = false;
                }
                if (!inHeaders)
                {
                    movesBuilder.Append(' ').Append(trimmed);
                }
            }
        }

        ParseMoves(movesBuilder.ToString(), game);
        return game;
    }

    private static void ParseMoves(string movesText, PgnGame game)
    {
        if (string.IsNullOrWhiteSpace(movesText)) return;

        // Remove recursive variations ( ... )
        while (movesText.Contains('('))
        {
            string prev = movesText;
            movesText = Regex.Replace(movesText, @"\([^\(\)]*\)", " ");
            if (movesText == prev) break;
        }

        // Tokenize into words and { comments }
        var matches = Regex.Matches(movesText, @"\{[^\}]*\}|\$\d+|[^\s\{\}]+");

        var board = new BoardState();
        int currentMoveNum = 1;
        PgnMove? lastMove = null;

        foreach (Match match in matches)
        {
            string tok = match.Value.Trim();
            if (string.IsNullOrEmpty(tok)) continue;

            // Handle comment block { ... }
            if (tok.StartsWith("{") && tok.EndsWith("}"))
            {
                if (lastMove != null)
                {
                    string commentContent = tok[1..^1].Trim();
                    lastMove.Comment = commentContent;

                    // Extract [%clk 0:10:00]
                    var clkMatch = Regex.Match(commentContent, @"\[%clk\s+([\d:\.]+)\]", RegexOptions.IgnoreCase);
                    if (clkMatch.Success)
                    {
                        string rawClk = clkMatch.Groups[1].Value;
                        // Format: e.g. "0:09:58" -> "09:58"
                        if (rawClk.StartsWith("0:") && rawClk.Length >= 7)
                        {
                            rawClk = rawClk[2..];
                        }
                        // Drop milliseconds if .0: "09:58.0" -> "09:58"
                        if (rawClk.EndsWith(".0"))
                        {
                            rawClk = rawClk[..^2];
                        }
                        lastMove.Clock = rawClk;
                    }
                }
                continue;
            }

            // Skip NAGs
            if (tok.StartsWith("$")) continue;

            // Check for game termination
            if (tok is "1-0" or "0-1" or "1/2-1/2" or "*")
            {
                if (!game.Headers.ContainsKey("Result") || game.Headers["Result"] == "*")
                {
                    game.Headers["Result"] = tok;
                }
                break;
            }

            // Move numbers like "1.", "1...", "24."
            if (tok.EndsWith("."))
            {
                string numPart = tok.TrimEnd('.');
                if (int.TryParse(numPart, out int num))
                {
                    currentMoveNum = num;
                }
                continue;
            }

            if (tok.Contains('.'))
            {
                int dotIdx = tok.LastIndexOf('.');
                string numPart = tok[..dotIdx].TrimEnd('.');
                if (int.TryParse(numPart, out int num))
                {
                    currentMoveNum = num;
                }
                tok = tok[(dotIdx + 1)..];
                if (string.IsNullOrEmpty(tok)) continue;
            }

            // Try to parse SAN move
            string fenBefore = board.ToFen();
            var move = board.ParseSan(tok);
            if (move.IsEmpty)
            {
                // If it fails, try trimming any trailing annotation characters like ! or ?
                move = board.ParseSan(tok.TrimEnd('!', '?', '+', '#'));
            }

            if (!move.IsEmpty)
            {
                var side = board.SideToMove;
                board.ApplyMove(move);
                string fenAfter = board.ToFen();

                var pgnMove = new PgnMove
                {
                    MoveNumber = currentMoveNum,
                    Color = side,
                    San = tok,
                    Move = move,
                    FenBefore = fenBefore,
                    FenAfter = fenAfter
                };

                game.Moves.Add(pgnMove);
                lastMove = pgnMove;

                if (side == PieceColor.Black)
                {
                    currentMoveNum++;
                }
            }
        }
    }
}
