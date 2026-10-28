using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using ChessAnalysis.Core.Analysis;
using ChessAnalysis.Core.Chess;

namespace ChessAnalysis.UI.Resources;

public static class ResourceManager
{
    private static readonly Dictionary<string, Image> PieceImageCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<MoveClassification, Image> BadgeImageCache = new();
    private static readonly HashSet<string> SearchedDirectories = new(StringComparer.OrdinalIgnoreCase);

    static ResourceManager()
    {
        DiscoverAssets();
    }

    public static void DiscoverAssets()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] searchDirs =
        {
            Path.Combine(baseDir, "Assets"),
            Path.Combine(baseDir, "..", "..", "..", "Assets"),
            Path.Combine(baseDir, "..", "..", "..", "..", "Assets"),
            Path.Combine(Directory.GetCurrentDirectory(), "Assets"),
            Path.Combine(Directory.GetCurrentDirectory(), "ChessAnalysis", "Assets")
        };

        foreach (var dir in searchDirs)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    LoadPiecesFrom(Path.Combine(dir, "Pieces"));
                    LoadBadgesFrom(Path.Combine(dir, "Badges"));
                }
            }
            catch { }
        }
    }

    private static void LoadPiecesFrom(string piecesDir)
    {
        if (!Directory.Exists(piecesDir)) return;

        (string key, string[] possibleFiles)[] mappings =
        {
            ("wP", new[] { "wP.png", "white_pawn.png", "wp.png" }),
            ("wN", new[] { "wN.png", "white_knight.png", "wn.png" }),
            ("wB", new[] { "wB.png", "white_bishop.png", "wb.png" }),
            ("wR", new[] { "wR.png", "white_rook.png", "wr.png" }),
            ("wQ", new[] { "wQ.png", "white_queen.png", "wq.png" }),
            ("wK", new[] { "wK.png", "white_king.png", "wk.png" }),

            ("bP", new[] { "bP.png", "black_pawn.png", "bp.png" }),
            ("bN", new[] { "bN.png", "black_knight.png", "bn.png" }),
            ("bB", new[] { "bB.png", "black_bishop.png", "bb.png" }),
            ("bR", new[] { "bR.png", "black_rook.png", "br.png" }),
            ("bQ", new[] { "bQ.png", "black_queen.png", "bq.png" }),
            ("bK", new[] { "bK.png", "black_king.png", "bk.png" }),
        };

        foreach (var (key, possibleFiles) in mappings)
        {
            if (PieceImageCache.ContainsKey(key)) continue;

            foreach (var fn in possibleFiles)
            {
                string path = Path.Combine(piecesDir, fn);
                if (File.Exists(path))
                {
                    try
                    {
                        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
                        PieceImageCache[key] = Image.FromStream(fs);
                        break;
                    }
                    catch { }
                }
            }
        }
    }

    private static void LoadBadgesFrom(string badgesDir)
    {
        if (!Directory.Exists(badgesDir)) return;

        (MoveClassification c, string[] possibleFiles)[] mappings =
        {
            (MoveClassification.Brilliant, new[] { "brilliant.png", "brilliant_move.png" }),
            (MoveClassification.Great, new[] { "great.png", "remarkable.png", "great_move.png" }),
            (MoveClassification.Best, new[] { "best.png", "best_move.png" }),
            (MoveClassification.Excellent, new[] { "excellent.png", "excellent_move.png" }),
            (MoveClassification.Good, new[] { "good.png", "good_move.png" }),
            (MoveClassification.Book, new[] { "book.png", "theoretical.png", "theory.png", "book_move.png" }),
            (MoveClassification.Forced, new[] { "forced.png", "forced_move.png" }),
            (MoveClassification.Inaccuracy, new[] { "inaccuracy.png" }),
            (MoveClassification.Mistake, new[] { "mistake.png" }),
            (MoveClassification.Blunder, new[] { "blunder.png" }),
            (MoveClassification.MissedWin, new[] { "missed_win.png", "missedwin.png" }),
        };

        foreach (var (classification, possibleFiles) in mappings)
        {
            if (BadgeImageCache.ContainsKey(classification)) continue;

            foreach (var fn in possibleFiles)
            {
                string path = Path.Combine(badgesDir, fn);
                if (File.Exists(path))
                {
                    try
                    {
                        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
                        BadgeImageCache[classification] = Image.FromStream(fs);
                        break;
                    }
                    catch { }
                }
            }
        }
    }

    public static Image? GetBadgeImage(MoveClassification c)
    {
        return BadgeImageCache.TryGetValue(c, out var img) ? img : null;
    }

    public static Image? GetPieceImage(PieceColor color, PieceType type)
    {
        char ch = type switch
        {
            PieceType.Pawn => 'P',
            PieceType.Knight => 'N',
            PieceType.Bishop => 'B',
            PieceType.Rook => 'R',
            PieceType.Queen => 'Q',
            PieceType.King => 'K',
            _ => ' '
        };
        if (ch == ' ') return null;
        string key = $"{(color == PieceColor.White ? 'w' : 'b')}{ch}";
        return PieceImageCache.TryGetValue(key, out var img) ? img : null;
    }

    public static int LoadedPiecesCount => PieceImageCache.Count;
    public static int LoadedBadgesCount => BadgeImageCache.Count;
    public static bool HasPiece(string key) => PieceImageCache.ContainsKey(key);
    public static bool HasBadge(MoveClassification c) => BadgeImageCache.ContainsKey(c);

    public static void DrawPiece(Graphics g, Piece piece, Rectangle destRect)
    {
        if (piece.IsEmpty) return;

        string key = $"{(piece.Color == PieceColor.White ? 'w' : 'b')}{piece.ToChar().ToString().ToUpperInvariant()}";

        if (PieceImageCache.TryGetValue(key, out var img))
        {
            // Draw user PNG with high-quality bicubic interpolation
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            
            // Add subtle padding (6%)
            int pad = Math.Max(2, (int)(destRect.Width * 0.06));
            var paddedRect = new Rectangle(
                destRect.X + pad,
                destRect.Y + pad,
                destRect.Width - pad * 2,
                destRect.Height - pad * 2);

            g.DrawImage(img, paddedRect);
        }
    }

    public static void DrawCornerBadge(Graphics g, MoveClassification badge, Rectangle squareRect)
    {
        // User badge PNGs are 40x40 px
        int badgeSize = Math.Min(40, Math.Max(22, (int)(squareRect.Width * 0.42f)));
        int badgeX = squareRect.Right - badgeSize - 2;
        int badgeY = squareRect.Top + 2;
        var badgeRect = new Rectangle(badgeX, badgeY, badgeSize, badgeSize);

        if (BadgeImageCache.TryGetValue(badge, out var img))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(img, badgeRect);
        }
    }

    public static Icon GetAppIcon()
    {
        try
        {
            using var bmp = new Bitmap(48, 48);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using var bgBrush = new SolidBrush(Color.FromArgb(24, 23, 21));
                g.FillRectangle(bgBrush, 0, 0, 48, 48);

                using var pen = new Pen(Color.FromArgb(129, 182, 76), 2f);
                g.DrawRectangle(pen, 1, 1, 45, 45);

                if (PieceImageCache.TryGetValue("wN", out var knightImg) || PieceImageCache.TryGetValue("wK", out knightImg))
                {
                    g.DrawImage(knightImg, new Rectangle(4, 4, 40, 40));
                }
                else
                {
                    using var font = new Font("Segoe UI", 22f, FontStyle.Bold);
                    using var brush = new SolidBrush(Color.FromArgb(245, 245, 245));
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString("♔", font, brush, new RectangleF(0, 0, 48, 48), sf);
                }
            }
            return Icon.FromHandle(bmp.GetHicon());
        }
        catch
        {
            return SystemIcons.Application;
        }
    }
}

