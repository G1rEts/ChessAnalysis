using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using ChessAnalysis.Core.Analysis;
using ChessAnalysis.UI.Resources;

namespace ChessAnalysis.UI.Controls;

public class MoveReviewCardControl : UserControl
{
    private bool _isCalculating = false;
    private bool _hasData = false;
    private int _hoveredRow = -1; // -1: none, 0: played, 1: best

    // Row 1: Played Move
    public string PlayedScore { get; set; } = "";
    public MoveClassification PlayedClassification { get; set; } = MoveClassification.Good;
    public string PlayedMoveText { get; set; } = "";
    public string PlayedTitle { get; set; } = "";
    public string PlayedContinuation { get; set; } = "";

    // Row 2: Best Alternative Move (if played move was not best)
    public bool HasBestAlternative { get; set; } = false;
    public string BestScore { get; set; } = "";
    public string BestMoveText { get; set; } = "";
    public string BestTitle { get; set; } = "сильнейший ход";
    public string BestContinuation { get; set; } = "";

    // Thinking animation
    private readonly System.Windows.Forms.Timer _animTimer;
    private int _animDotCount = 1;

    public event Action? PlayedRowClicked;
    public event Action? BestRowClicked;

    public bool IsCalculating
    {
        get => _isCalculating;
        set
        {
            _isCalculating = value;
            if (_isCalculating)
            {
                _animTimer.Start();
                Height = 36;
            }
            else
            {
                _animTimer.Stop();
                UpdateControlHeight();
            }
            Invalidate();
        }
    }

    public MoveReviewCardControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = ChessTheme.PanelDarker;
        DoubleBuffered = true;
        Height = 0; // Collapsed by default until data or calculation is set

        _animTimer = new System.Windows.Forms.Timer { Interval = 280 };
        _animTimer.Tick += (s, e) =>
        {
            _animDotCount = (_animDotCount % 3) + 1;
            Invalidate();
        };
    }

    public void ShowCalculating()
    {
        _hasData = false;
        IsCalculating = true;
    }

    public void Clear()
    {
        _isCalculating = false;
        _animTimer.Stop();
        _hasData = false;
        Height = 0;
        Invalidate();
    }

    public void SetReview(
        string playedScore,
        MoveClassification classification,
        string playedMove,
        string playedContinuation,
        bool hasBetter,
        string bestScore = "",
        string bestMove = "",
        string bestContinuation = "")
    {
        _isCalculating = false;
        _animTimer.Stop();
        _hasData = true;

        PlayedScore = playedScore;
        PlayedClassification = classification;
        PlayedMoveText = playedMove;
        PlayedTitle = GetClassificationRussianTitle(classification);
        PlayedContinuation = playedContinuation;

        HasBestAlternative = hasBetter && !string.IsNullOrEmpty(bestMove) && bestMove != playedMove;
        BestScore = bestScore;
        BestMoveText = bestMove;
        BestTitle = "сильнейший ход";
        BestContinuation = bestContinuation;

        UpdateControlHeight();
        Invalidate();
    }

    private void UpdateControlHeight()
    {
        if (!_hasData && !_isCalculating)
        {
            Height = 0;
            Visible = false;
            return;
        }

        Visible = true;
        if (_isCalculating)
        {
            Height = 36;
        }
        else
        {
            Height = HasBestAlternative ? 68 : 36;
        }
    }

    private static string GetClassificationRussianTitle(MoveClassification c) => c switch
    {
        MoveClassification.Brilliant => "блестящий ход",
        MoveClassification.Great => "отличный ход",
        MoveClassification.Best => "лучший ход",
        MoveClassification.Excellent => "великолепный ход",
        MoveClassification.Good => "хороший ход",
        MoveClassification.Book => "теория",
        MoveClassification.Inaccuracy => "неточность",
        MoveClassification.Mistake => "ошибка",
        MoveClassification.MissedWin => "упущенный шанс",
        MoveClassification.Blunder => "зевок",
        _ => "ход"
    };

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_isCalculating || !_hasData) return;

        int row = -1;
        if (e.Y >= 2 && e.Y <= 32)
        {
            row = 0;
        }
        else if (HasBestAlternative && e.Y > 34 && e.Y <= 66)
        {
            row = 1;
        }

        if (_hoveredRow != row)
        {
            _hoveredRow = row;
            Cursor = row >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoveredRow != -1)
        {
            _hoveredRow = -1;
            Cursor = Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_isCalculating || !_hasData) return;

        if (e.Y >= 2 && e.Y <= 32)
        {
            PlayedRowClicked?.Invoke();
        }
        else if (HasBestAlternative && e.Y > 34 && e.Y <= 66)
        {
            BestRowClicked?.Invoke();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Height <= 2) return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        using var bgBrush = new SolidBrush(ChessTheme.PanelDarker);
        g.FillRectangle(bgBrush, ClientRectangle);

        // Bottom divider border
        using var borderPen = new Pen(ChessTheme.BorderColor, 1f);
        g.DrawLine(borderPen, 0, Height - 1, Width, Height - 1);

        if (_isCalculating)
        {
            DrawCalculatingState(g);
            return;
        }

        if (!_hasData) return;

        // Row 1: Played move
        var row1Rect = new Rectangle(4, 2, Width - 8, 30);
        DrawCardRow(g, row1Rect, PlayedScore, PlayedClassification, PlayedMoveText, PlayedTitle, PlayedContinuation, isBest: false, isHovered: (_hoveredRow == 0));

        // Row 2: Best move (if available)
        if (HasBestAlternative)
        {
            var row2Rect = new Rectangle(4, 34, Width - 8, 30);
            DrawCardRow(g, row2Rect, BestScore, MoveClassification.Best, BestMoveText, BestTitle, BestContinuation, isBest: true, isHovered: (_hoveredRow == 1));
        }
    }

    private void DrawCalculatingState(Graphics g)
    {
        string dots = new string('.', _animDotCount);
        string text = $"● Анализ хода{dots}";

        using var font = new Font("Segoe UI", 9f, FontStyle.Italic);
        using var brush = new SolidBrush(Color.FromArgb(170, 168, 164));
        var rect = new Rectangle(12, 0, Width - 24, Height);
        var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
        g.DrawString(text, font, brush, rect, sf);
    }

    private void DrawCardRow(
        Graphics g,
        Rectangle rowRect,
        string score,
        MoveClassification classification,
        string moveText,
        string titleText,
        string continuation,
        bool isBest,
        bool isHovered)
    {
        if (isHovered)
        {
            using var hoverBrush = new SolidBrush(Color.FromArgb(42, 40, 37));
            using var hoverPath = CreateRoundedRect(rowRect, 5);
            g.FillPath(hoverBrush, hoverPath);
        }

        int curX = rowRect.X + 4;
        int centerY = rowRect.Y + rowRect.Height / 2;

        // 1. Score Pill
        if (!string.IsNullOrEmpty(score))
        {
            int pillW = 54;
            int pillH = 20;
            var pillRect = new Rectangle(curX, centerY - pillH / 2, pillW, pillH);
            using var pillPath = CreateRoundedRect(pillRect, 4);

            bool isPositive = !score.StartsWith('-');
            using var pillBg = new SolidBrush(isPositive ? Color.White : Color.FromArgb(45, 43, 40));
            using var pillTextBrush = new SolidBrush(isPositive ? Color.FromArgb(20, 20, 20) : Color.FromArgb(240, 240, 240));

            g.FillPath(pillBg, pillPath);
            if (!isPositive)
            {
                using var pPen = new Pen(Color.FromArgb(75, 73, 68), 1f);
                g.DrawPath(pPen, pillPath);
            }

            using var scoreFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            var sfCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(score, scoreFont, pillTextBrush, pillRect, sfCenter);

            curX += pillW + 8;
        }

        // 2. Badge Icon
        var badgeImg = ResourceManager.GetBadgeImage(classification);
        int badgeSize = 20;
        if (badgeImg != null)
        {
            g.DrawImage(badgeImg, new Rectangle(curX, centerY - badgeSize / 2, badgeSize, badgeSize));
            curX += badgeSize + 8;
        }
        else
        {
            // Fallback star circle for Best move
            if (isBest)
            {
                using var starBg = new SolidBrush(Color.FromArgb(129, 182, 76));
                g.FillEllipse(starBg, curX, centerY - badgeSize / 2, badgeSize, badgeSize);
                using var starFont = new Font("Segoe UI", 9f, FontStyle.Bold);
                using var starBrush = new SolidBrush(Color.White);
                var sfStar = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString("★", starFont, starBrush, new Rectangle(curX, centerY - badgeSize / 2, badgeSize, badgeSize), sfStar);
                curX += badgeSize + 8;
            }
        }

        // 3. Colored Move Title (e.g. "b6 - зевок" or "Cc6 - сильнейший ход")
        Color titleColor = isBest ? Color.FromArgb(129, 182, 76) : classification.GetThemeColor();
        string fullTitle = string.IsNullOrEmpty(titleText)
            ? moveText
            : $"{moveText} - {titleText}";

        using var titleFont = new Font("Segoe UI", 9f, FontStyle.Bold);
        using var titleBrush = new SolidBrush(titleColor);

        var titleSz = g.MeasureString(fullTitle, titleFont);
        g.DrawString(fullTitle, titleFont, titleBrush, curX, centerY - titleSz.Height / 2);
        curX += (int)titleSz.Width + 8;

        // 4. Continuation Line (gray text)
        int rightMargin = rowRect.Right - 26; // reserve 26px for magnifier
        int availContW = Math.Max(0, rightMargin - curX);
        if (availContW > 20 && !string.IsNullOrEmpty(continuation))
        {
            using var contFont = new Font("Segoe UI", 8.5f, FontStyle.Regular);
            var contRect = new Rectangle(curX, rowRect.Y, availContW, rowRect.Height);
            TextRenderer.DrawText(g, continuation, contFont, contRect, Color.FromArgb(180, 178, 175),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        // 5. Magnifier Icon on the far right 🔍
        DrawMagnifierIcon(g, rowRect.Right - 20, centerY);
    }

    private static void DrawMagnifierIcon(Graphics g, int cx, int cy)
    {
        using var pen = new Pen(Color.FromArgb(135, 133, 130), 1.6f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        int radius = 4;
        g.DrawEllipse(pen, cx - radius, cy - radius - 1, radius * 2, radius * 2);
        g.DrawLine(pen, cx + 3, cy + 2, cx + 6, cy + 5);
    }

    private static GraphicsPath CreateRoundedRect(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2f;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
