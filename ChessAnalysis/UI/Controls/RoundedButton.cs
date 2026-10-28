using System.Drawing;
using System.Drawing.Drawing2D;
using ChessAnalysis.UI.Resources;

namespace ChessAnalysis.UI.Controls;

public class RoundedButton : Button
{
    private bool _isHovered = false;
    private bool _isPressed = false;

    public int BorderRadius { get; set; } = 8;
    public Color NormalColor { get; set; } = ChessTheme.PanelDarker;
    public Color HoverColor { get; set; } = Color.FromArgb(55, 53, 49);
    public Color PressedColor { get; set; } = Color.FromArgb(28, 27, 25);
    public Color CustomBorderColor { get; set; } = Color.FromArgb(65, 63, 60);
    public bool Show3DBevel { get; set; } = true;

    public RoundedButton()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw,
            true);

        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _isHovered = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _isHovered = false;
        _isPressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        base.OnMouseDown(mevent);
        if (mevent.Button == MouseButtons.Left)
        {
            _isPressed = true;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        base.OnMouseUp(mevent);
        _isPressed = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        int r = BorderRadius;
        int bevel = (Show3DBevel && !_isPressed) ? 3 : 0;
        int yOffset = _isPressed ? 2 : 0;

        var btnRect = new Rectangle(0, yOffset, Width - 1, Height - 1 - bevel);

        if (btnRect.Width <= r || btnRect.Height <= r) return;

        Color currentBg = !Enabled ? Color.FromArgb(40, 39, 37) : (_isPressed ? PressedColor : (_isHovered ? HoverColor : NormalColor));

        // 1. Draw 3D Bottom Shadow Bevel if enabled
        if (Show3DBevel && !_isPressed && Enabled)
        {
            var bevelRect = new Rectangle(0, bevel, Width - 1, Height - 1);
            using var bevelPath = CreateRoundedRectanglePath(bevelRect, r);
            Color shadowColor = Color.FromArgb(
                Math.Max(0, NormalColor.R - 35),
                Math.Max(0, NormalColor.G - 35),
                Math.Max(0, NormalColor.B - 35));
            using var bevelBrush = new SolidBrush(shadowColor);
            g.FillPath(bevelBrush, bevelPath);
        }

        // 2. Draw Button Surface
        using var btnPath = CreateRoundedRectanglePath(btnRect, r);
        using var surfaceBrush = new SolidBrush(currentBg);
        g.FillPath(surfaceBrush, btnPath);

        // 3. Draw Soft Border
        if (CustomBorderColor != Color.Transparent)
        {
            using var borderPen = new Pen(CustomBorderColor, 1.2f);
            g.DrawPath(borderPen, btnPath);
        }

        // 4. Draw Text and Image
        Color textColor = !Enabled ? Color.FromArgb(120, 118, 115) : ForeColor;
        using var textBrush = new SolidBrush(textColor);
        var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        if (Image != null)
        {
            int imgSize = Math.Min(btnRect.Height - 8, Image.Width);
            int imgX = (btnRect.Width - (imgSize + (string.IsNullOrEmpty(Text) ? 0 : 8) + (int)g.MeasureString(Text, Font).Width)) / 2;
            int imgY = btnRect.Y + (btnRect.Height - imgSize) / 2;
            g.DrawImage(Image, new Rectangle(imgX, imgY, imgSize, imgSize));

            if (!string.IsNullOrEmpty(Text))
            {
                var textRect = new Rectangle(imgX + imgSize + 6, btnRect.Y, btnRect.Width - (imgX + imgSize + 6), btnRect.Height);
                sf.Alignment = StringAlignment.Near;
                g.DrawString(Text, Font, textBrush, textRect, sf);
            }
        }
        else
        {
            g.DrawString(Text, Font, textBrush, btnRect, sf);
        }
    }

    private static GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
