using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using ChessAnalysis.Core.Analysis;
using ChessAnalysis.Core.Chess;

namespace ChessAnalysis.UI.Resources;

public static class ResourceManager
{
    private static readonly Dictionary<string, Image> PieceImageCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<MoveClassification, Image> BadgeImageCache = [];
    private static readonly HashSet<string> SearchedDirectories = new(StringComparer.OrdinalIgnoreCase);

    static ResourceManager()
    {
        DiscoverAssets();
    }

    public static void DiscoverAssets()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] searchDirs =
        [
            Path.Combine(baseDir, "Assets"),
            Path.Combine(baseDir, "..", "..", "..", "Assets"),
            Path.Combine(baseDir, "..", "..", "..", "..", "Assets"),
            Path.Combine(Directory.GetCurrentDirectory(), "Assets"),
            Path.Combine(Directory.GetCurrentDirectory(), "ChessAnalysis", "Assets")
        ];

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
        [
            ("wP", ["wP.png", "white_pawn.png", "wp.png"]),
            ("wN", ["wN.png", "white_knight.png", "wn.png"]),
            ("wB", ["wB.png", "white_bishop.png", "wb.png"]),
            ("wR", ["wR.png", "white_rook.png", "wr.png"]),
            ("wQ", ["wQ.png", "white_queen.png", "wq.png"]),
            ("wK", ["wK.png", "white_king.png", "wk.png"]),

            ("bP", ["bP.png", "black_pawn.png", "bp.png"]),
            ("bN", ["bN.png", "black_knight.png", "bn.png"]),
            ("bB", ["bB.png", "black_bishop.png", "bb.png"]),
            ("bR", ["bR.png", "black_rook.png", "br.png"]),
            ("bQ", ["bQ.png", "black_queen.png", "bq.png"]),
            ("bK", ["bK.png", "black_king.png", "bk.png"]),
        ];

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
        [
            (MoveClassification.Brilliant, ["brilliant.png", "brilliant_move.png"]),
            (MoveClassification.Great, ["great.png", "remarkable.png", "great_move.png"]),
            (MoveClassification.Best, ["best.png", "best_move.png"]),
            (MoveClassification.Excellent, ["excellent.png", "excellent_move.png"]),
            (MoveClassification.Good, ["good.png", "good_move.png"]),
            (MoveClassification.Book, ["book.png", "theoretical.png", "theory.png", "book_move.png"]),
            (MoveClassification.Forced, ["forced.png", "forced_move.png"]),
            (MoveClassification.Inaccuracy, ["inaccuracy.png"]),
            (MoveClassification.Mistake, ["mistake.png"]),
            (MoveClassification.Blunder, ["blunder.png"]),
            (MoveClassification.MissedWin, ["missed_win.png", "missedwin.png"]),
        ];

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
        LoadSpecialBadge(badgesDir, "checkmate.png", ref _checkmateBadge);
        LoadSpecialBadge(badgesDir, "resign.png", ref _resignBadge);
        LoadSpecialBadge(badgesDir, "stalemate.png", ref _drawBadge);
        LoadSpecialBadge(badgesDir, "win.png", ref _winBadge);
    }

    private static void LoadSpecialBadge(string dir, string fileName, ref Image? target)
    {
        string path = Path.Combine(dir, fileName);
        if (target == null && File.Exists(path))
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
                target = Image.FromStream(fs);
            }
            catch { }
        }
    }

    private static Image? _checkmateBadge;
    private static Image? _resignBadge;
    private static Image? _drawBadge;
    private static Image? _winBadge;

    public static Image? GetCheckmateBadge() => _checkmateBadge;
    public static Image? GetResignBadge() => _resignBadge;
    public static Image? GetDrawBadge() => _drawBadge;
    public static Image? GetWinBadge() => _winBadge;

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

    public static void DrawCornerBadge(Graphics g, MoveClassification badge, Rectangle squareRect, Rectangle boardRect = default)
    {
        const int badgeSize = 40;
        // Centered on the top-right intersection of the square matching screenshot
        int badgeX = squareRect.Right - (badgeSize / 2);
        int badgeY = squareRect.Top - (badgeSize / 2);

        // Clamp to board boundary so edge squares (e.g. file h or rank 8) don't get clipped
        if (!boardRect.IsEmpty)
        {
            if (badgeX + badgeSize > boardRect.Right - 2) badgeX = boardRect.Right - badgeSize - 2;
            if (badgeY < boardRect.Top + 2) badgeY = boardRect.Top + 2;
            if (badgeX < boardRect.Left + 2) badgeX = boardRect.Left + 2;
            if (badgeY + badgeSize > boardRect.Bottom - 2) badgeY = boardRect.Bottom - badgeSize - 2;
        }

        var badgeRect = new Rectangle(badgeX, badgeY, badgeSize, badgeSize);

        if (BadgeImageCache.TryGetValue(badge, out var img))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(img, badgeRect);
        }
    }

    public static void DrawBadgeOnSquare(Graphics g, Image? img, Rectangle squareRect, Rectangle boardRect = default)
    {
        if (img == null) return;
        const int badgeSize = 40;
        int badgeX = squareRect.Right - (badgeSize / 2);
        int badgeY = squareRect.Top - (badgeSize / 2);

        if (!boardRect.IsEmpty)
        {
            if (badgeX + badgeSize > boardRect.Right - 2) badgeX = boardRect.Right - badgeSize - 2;
            if (badgeY < boardRect.Top + 2) badgeY = boardRect.Top + 2;
            if (badgeX < boardRect.Left + 2) badgeX = boardRect.Left + 2;
            if (badgeY + badgeSize > boardRect.Bottom - 2) badgeY = boardRect.Bottom - badgeSize - 2;
        }

        var badgeRect = new Rectangle(badgeX, badgeY, badgeSize, badgeSize);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(img, badgeRect);
    }

    public static void DrawCheckmateBadge(Graphics g, Rectangle squareRect, Rectangle boardRect = default) =>
        DrawBadgeOnSquare(g, GetCheckmateBadge(), squareRect, boardRect);

    public static void DrawResignBadge(Graphics g, Rectangle squareRect, Rectangle boardRect = default) =>
        DrawBadgeOnSquare(g, GetResignBadge(), squareRect, boardRect);

    public static void DrawDrawBadge(Graphics g, Rectangle squareRect, Rectangle boardRect = default) =>
        DrawBadgeOnSquare(g, GetDrawBadge(), squareRect, boardRect);

    public static void DrawWinBadge(Graphics g, Rectangle squareRect, Rectangle boardRect = default) =>
        DrawBadgeOnSquare(g, GetWinBadge(), squareRect, boardRect);

    public static Icon GetAppIcon()
    {
        try
        {
            string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "app.ico");
            if (File.Exists(icoPath))
            {
                return new Icon(icoPath);
            }

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

    public static void GenerateAppIcoFile(string outputPath)
    {
        int[] sizes = [256, 64, 48, 32, 16];
        var pngDataList = new List<byte[]>();

        string piecePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Pieces", "wN.png");
        if (!File.Exists(piecePath))
        {
            piecePath = Path.Combine(Directory.GetCurrentDirectory(), "ChessAnalysis", "Assets", "Pieces", "wN.png");
        }
        if (!File.Exists(piecePath))
        {
            piecePath = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Pieces", "wN.png");
        }
        Image? knightImg = File.Exists(piecePath) ? Image.FromFile(piecePath) : null;

        foreach (int size in sizes)
        {
            using var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                using var bgBrush = new SolidBrush(Color.FromArgb(24, 23, 21));
                g.FillRectangle(bgBrush, 0, 0, size, size);

                float penWidth = Math.Max(1.5f, size * 0.045f);
                using var pen = new Pen(Color.FromArgb(129, 182, 76), penWidth);
                float inset = penWidth / 2f + (size * 0.02f);
                g.DrawRectangle(pen, inset, inset, size - inset * 2, size - inset * 2);

                if (knightImg != null)
                {
                    int pad = (int)(size * 0.10f);
                    g.DrawImage(knightImg, new Rectangle(pad, pad, size - pad * 2, size - pad * 2));
                }
                else
                {
                    using var font = new Font("Segoe UI", size * 0.55f, FontStyle.Bold);
                    using var brush = new SolidBrush(Color.FromArgb(245, 245, 245));
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString("♔", font, brush, new RectangleF(0, 0, size, size), sf);
                }
            }

            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            pngDataList.Add(ms.ToArray());
        }

        knightImg?.Dispose();

        string? dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        // Header: Reserved (0), Type (1 = Icon), Count
        bw.Write((ushort)0);
        bw.Write((ushort)1);
        bw.Write((ushort)sizes.Length);

        uint offset = (uint)(6 + sizes.Length * 16);

        // Directory entries
        for (int i = 0; i < sizes.Length; i++)
        {
            int size = sizes[i];
            byte[] bytes = pngDataList[i];

            bw.Write((byte)(size == 256 ? 0 : size));
            bw.Write((byte)(size == 256 ? 0 : size));
            bw.Write((byte)0);
            bw.Write((byte)0);
            bw.Write((ushort)1);
            bw.Write((ushort)32);
            bw.Write((uint)bytes.Length);
            bw.Write(offset);

            offset += (uint)bytes.Length;
        }

        // Image data
        for (int i = 0; i < sizes.Length; i++)
        {
            bw.Write(pngDataList[i]);
        }
    }
}

