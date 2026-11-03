using System.Text;
using System.Text.RegularExpressions;
using ChessAnalysis.Core.Analysis;

namespace ChessAnalysis.Core.Chess;

public static partial class PgnParser
{
    public static PgnGame Parse(string pgnText) => ParseSingleGame(pgnText) ?? new PgnGame();

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

        var game = new PgnGame { RawPgn = rawGame };
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
        if (game.Headers.TryGetValue("FEN", out var customFen) && !string.IsNullOrWhiteSpace(customFen))
        {
            board.LoadFen(customFen);
            currentMoveNum = board.FullmoveNumber;
        }
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

                    // Extract [%c_effect ... type;...]
                    var effectMatches = Regex.Matches(commentContent, @"type;([A-Za-z]+)", RegexOptions.IgnoreCase);
                    foreach (Match em in effectMatches)
                    {
                        string effectType = em.Groups[1].Value.ToLowerInvariant();
                        if (lastMove.ExpectedClassification == null)
                        {
                            lastMove.ExpectedClassification = effectType switch
                            {
                                "brilliant" => MoveClassification.Brilliant,
                                "greatfind" or "great" => MoveClassification.Great,
                                "best" => MoveClassification.Best,
                                "excellent" => MoveClassification.Excellent,
                                "good" => MoveClassification.Good,
                                "book" => MoveClassification.Book,
                                "forced" => MoveClassification.Forced,
                                "inaccuracy" => MoveClassification.Inaccuracy,
                                "mistake" => MoveClassification.Mistake,
                                "blunder" => MoveClassification.Blunder,
                                "miss" or "missedwin" => MoveClassification.MissedWin,
                                _ => null
                            };
                        }

                        if (effectType is "resignwhite" or "resignblack")
                        {
                            game.Headers["TerminationType"] = "Resignation";
                        }
                        else if (effectType is "checkmatewhite" or "checkmateblack")
                        {
                            game.Headers["TerminationType"] = "Checkmate";
                        }
                        else if (effectType is "drawwhite" or "drawblack")
                        {
                            game.Headers["TerminationType"] = "Draw";
                        }
                    }
                }
                continue;
            }

            // Handle NAGs ($1, $2, $3, $4, $5, $6, $9, etc.)
            if (tok.StartsWith("$"))
            {
                if (int.TryParse(tok[1..], out int nag) && lastMove != null && lastMove.ExpectedClassification == null)
                {
                    lastMove.ExpectedClassification = nag switch
                    {
                        1 => MoveClassification.Great,
                        2 => MoveClassification.Mistake,
                        3 => MoveClassification.Brilliant,
                        4 => MoveClassification.Blunder,
                        5 => MoveClassification.Good,
                        6 => MoveClassification.Inaccuracy,
                        9 => MoveClassification.MissedWin,
                        _ => null
                    };
                }
                continue;
            }

            // Check for game termination
            if (tok is "1-0" or "0-1" or "1/2-1/2" or "*")
            {
                if (!game.Headers.ContainsKey("Result") || game.Headers["Result"] == "*")
                {
                    game.Headers["Result"] = tok;
                }
                break;
            }

            // Pure dots like "...", "..", "."
            if (tok.Length > 0 && tok.All(c => c == '.'))
            {
                continue;
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

            tok = tok.TrimStart('.');

            // Extract user annotation tag like "-G1", "-N2", "-B3"
            MoveClassification? expectedClass = null;
            string? annotationTag = null;
            var tagMatch = Regex.Match(tok, @"-(G[1-3]|N[1-3]|B[1-4]|M1)$", RegexOptions.IgnoreCase);
            if (tagMatch.Success)
            {
                string tag = tagMatch.Groups[1].Value.ToUpperInvariant();
                annotationTag = tag;
                expectedClass = tag switch
                {
                    "G1" => MoveClassification.Brilliant,
                    "G2" => MoveClassification.Great,
                    "G3" => MoveClassification.Book,
                    "N1" => MoveClassification.Best,
                    "N2" => MoveClassification.Excellent,
                    "N3" => MoveClassification.Good,
                    "B1" => MoveClassification.Inaccuracy,
                    "B2" => MoveClassification.Mistake,
                    "B3" => MoveClassification.Blunder,
                    "B4" => MoveClassification.MissedWin,
                    "M1" => MoveClassification.Forced,
                    _ => null
                };
                tok = tok[..tagMatch.Index];
            }

            // Try to parse SAN move
            string cleanSan = tok;
            string fenBefore = board.ToFen();
            var move = board.ParseSan(cleanSan);
            if (move.IsEmpty)
            {
                // If it fails, try trimming any trailing annotation characters like ! or ?
                move = board.ParseSan(cleanSan.TrimEnd('!', '?', '+', '#'));
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
                    San = cleanSan,
                    Move = move,
                    FenBefore = fenBefore,
                    FenAfter = fenAfter,
                    ExpectedClassification = expectedClass,
                    AnnotationTag = annotationTag
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
