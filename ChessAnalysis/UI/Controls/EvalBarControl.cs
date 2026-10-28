using System.Drawing;
using System.Drawing.Drawing2D;
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

        _animTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _animTimer.Tick += (s, e) =>
        {
            if (Math.Abs(_targetWhitePercent - _currentWhitePercent) > 0.4)
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
            // White at bottom
            blackRect = new Rectangle(barRect.X, barRect.Y, w, blackHeight);
            whiteRect = new Rectangle(barRect.X, barRect.Y + blackHeight, w, whiteHeight);
        }
        else
        {
            // Black at bottom
            whiteRect = new Rectangle(barRect.X, barRect.Y, w, whiteHeight);
            blackRect = new Rectangle(barRect.X, barRect.Y + whiteHeight, w, blackHeight);
        }

        int radius = 4;
        using var clipPath = new GraphicsPath();
        clipPath.AddArc(barRect.X, barRect.Y, radius * 2, radius * 2, 180, 90);
        clipPath.AddArc(barRect.Right - radius * 2, barRect.Y, radius * 2, radius * 2, 270, 90);
        clipPath.AddArc(barRect.Right - radius * 2, barRect.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
        clipPath.AddArc(barRect.X, barRect.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
        clipPath.CloseFigure();

        var oldClip = g.Clip;
        g.SetClip(clipPath);

        using var blackBrush = new SolidBrush(Color.FromArgb(33, 32, 29));
        using var whiteBrush = new SolidBrush(Color.FromArgb(255, 255, 255));

        g.FillRectangle(blackBrush, blackRect);
        g.FillRectangle(whiteBrush, whiteRect);

        g.ResetClip();

        // Border
        using var borderPen = new Pen(Color.FromArgb(70, 68, 65), 1.2f);
        g.DrawPath(borderPen, clipPath);

        // Draw Evaluation Text
        using var font = new Font("Segoe UI", 8f, FontStyle.Bold);
        var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        bool whiteAdvantage = _currentWhitePercent >= 50.0;
        Color textColor = whiteAdvantage ? Color.FromArgb(25, 24, 22) : Color.FromArgb(250, 250, 250);
        using var textBrush = new SolidBrush(textColor);

        int textY = whiteAdvantage
            ? (!IsFlipped ? h - 22 : 4)
            : (!IsFlipped ? 4 : h - 22);

        var textRect = new Rectangle(barRect.X, textY, w, 16);
        string displayStr = _evalText.StartsWith("+") ? _evalText[1..] : _evalText;
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
