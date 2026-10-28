using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using ChessAnalysis.Core.Engine;
using ChessAnalysis.UI.Resources;

namespace ChessAnalysis.UI.Controls;

public class EvalBarControl : Control
{
    private double _targetWhitePercent = 50.0;
    private double _currentWhitePercent = 50.0;
    private string _evalText = "0.0";
    private bool _isMate = false;
    private readonly System.Windows.Forms.Timer _animTimer;

    public bool IsFlipped { get; set; } = false;

    public EvalBarControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = ChessTheme.BackgroundDark;
        Width = 36;

        _animTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _animTimer.Tick += (s, e) =>
        {
            if (Math.Abs(_targetWhitePercent - _currentWhitePercent) > 0.3)
            {
                _currentWhitePercent += (_targetWhitePercent - _currentWhitePercent) * 0.25;
                Invalidate();
            }
            else
            {
                _currentWhitePercent = _targetWhitePercent;
                _animTimer.Stop();
                Invalidate();
            }
        };
    }

    public void SetEvaluation(EngineEvaluation eval)
    {
        _isMate = eval.IsMate;
        _evalText = eval.ScoreDisplay;

        // Calculate win probability percent (0..100)
        _targetWhitePercent = eval.WinProbabilityWhite;
        _animTimer.Start();
        Invalidate();
    }

    public void Reset()
    {
        _targetWhitePercent = 50.0;
        _currentWhitePercent = 50.0;
        _evalText = "0.0";
        _isMate = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        int w = Width - 2;
        int h = Height - 2;
        if (w <= 4 || h <= 4) return;

        var barRect = new Rectangle(1, 1, w, h);

        // White height in pixels
        double pct = Math.Clamp(_currentWhitePercent / 100.0, 0.02, 0.98);
        int whiteHeight = (int)(h * pct);
        int blackHeight = h - whiteHeight;

        Rectangle whiteRect, blackRect;
        if (!IsFlipped)
        {
            // Standard: Black at top, White at bottom
            blackRect = new Rectangle(barRect.X, barRect.Y, w, blackHeight);
            whiteRect = new Rectangle(barRect.X, barRect.Y + blackHeight, w, whiteHeight);
        }
        else
        {
            // Flipped: White at top, Black at bottom
            whiteRect = new Rectangle(barRect.X, barRect.Y, w, whiteHeight);
            blackRect = new Rectangle(barRect.X, barRect.Y + whiteHeight, w, blackHeight);
        }

        int radius = 5;
        using var clipPath = new GraphicsPath();
        clipPath.AddArc(barRect.X, barRect.Y, radius * 2, radius * 2, 180, 90);
        clipPath.AddArc(barRect.Right - radius * 2, barRect.Y, radius * 2, radius * 2, 270, 90);
        clipPath.AddArc(barRect.Right - radius * 2, barRect.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
        clipPath.AddArc(barRect.X, barRect.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
        clipPath.CloseFigure();

        var oldClip = g.Clip;
        g.SetClip(clipPath);

        using var blackBrush = new SolidBrush(Color.FromArgb(38, 36, 34));
        using var whiteBrush = new SolidBrush(Color.FromArgb(245, 245, 245));

        g.FillRectangle(blackBrush, blackRect);
        g.FillRectangle(whiteBrush, whiteRect);

        g.ResetClip();

        // Border
        using var borderPen = new Pen(Color.FromArgb(75, 73, 70), 1.2f);
        g.DrawPath(borderPen, clipPath);

        // Draw Evaluation Text (9.5pt Bold, legible on the winning side)
        using var font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        bool whiteAdvantage = _currentWhitePercent >= 50.0;
        Color textColor = whiteAdvantage ? Color.FromArgb(28, 27, 25) : Color.FromArgb(245, 245, 245);
        using var textBrush = new SolidBrush(textColor);

        int textY = whiteAdvantage
            ? (!IsFlipped ? h - 28 : 6)
            : (!IsFlipped ? 6 : h - 28);

        var textRect = new Rectangle(barRect.X, textY, w, 22);
        string displayStr = _evalText;
        if (displayStr.StartsWith("+"))
        {
            displayStr = displayStr[1..];
        }
        g.DrawString(displayStr, font, textBrush, textRect, sf);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _animTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
