using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using ChessAnalysis.UI.Resources;

namespace ChessAnalysis.UI.Controls;

public enum ButtonIconType
{
    None,
    Play,
    Pause,
    First,
    Prev,
    Next,
    Last,
    Gear,
    Search
}

public class RoundedButton : Button
{
    private bool _isHovered = false;
    private bool _isPressed = false;

    public int BorderRadius { get; set; } = 8;
    public Color NormalColor { get; set; } = ChessTheme.PanelDarker;
    public Color HoverColor { get; set; } = Color.FromArgb(55, 53, 49);
    public Color PressedColor { get; set; } = Color.FromArgb(28, 27, 25);
    public Color CustomBorderColor { get; set; } = Color.FromArgb(65, 63, 60);
    public Color ActiveBorderColor { get; set; } = Color.FromArgb(88, 144, 255);
    public Color IconColor { get; set; } = Color.FromArgb(220, 220, 220);
    public ButtonIconType IconType { get; set; } = ButtonIconType.None;
    public bool IsActive { get; set; } = false;
    public bool Show3DBevel { get; set; } = false;

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
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        int r = BorderRadius;
        var btnRect = new Rectangle(0, 0, Width - 1, Height - 1);

        if (btnRect.Width <= r || btnRect.Height <= r) return;

        // Pre-paint parent background to eliminate any black or green corner artifacts
        Color parentBg = Parent?.BackColor ?? ChessTheme.PanelDark;
        if (parentBg == Color.Transparent && Parent?.Parent != null)
        {
            parentBg = Parent.Parent.BackColor;
        }
        if (parentBg != Color.Transparent)
        {
            using var parentBrush = new SolidBrush(parentBg);
            g.FillRectangle(parentBrush, ClientRectangle);
        }

        Color currentBg = !Enabled
            ? Color.FromArgb(40, 39, 37)
            : (_isPressed ? PressedColor : (_isHovered ? HoverColor : NormalColor));

        // 1. Draw Button Surface
        using var btnPath = CreateRoundedRectanglePath(btnRect, r);
        using var surfaceBrush = new SolidBrush(currentBg);
        g.FillPath(surfaceBrush, btnPath);

        // 2. Draw Soft Border (or Active Border if IsActive)
        if (IsActive)
        {
            using var activePen = new Pen(ActiveBorderColor, 2f);
            g.DrawPath(activePen, btnPath);
        }
        else if (CustomBorderColor != Color.Transparent)
        {
            using var borderPen = new Pen(CustomBorderColor, 1.2f);
            g.DrawPath(borderPen, btnPath);
        }

        // Clip region to exact rounded path
        try
        {
            using var regPath = CreateRoundedRectanglePath(new Rectangle(0, 0, Width, Height), r);
            Region = new Region(regPath);
        }
        catch { }

        // 3. Draw Vector Icon if configured
        if (IconType != ButtonIconType.None)
        {
            DrawVectorIcon(g, btnRect);
            return;
        }

        // 4. Draw Image if present
        if (Image != null)
        {
            int imgSize = Math.Min(btnRect.Height - 8, Image.Width);
            int imgX = (btnRect.Width - imgSize) / 2;
            int imgY = (btnRect.Height - imgSize) / 2;

            if (!string.IsNullOrEmpty(Text))
            {
                var textSz = TextRenderer.MeasureText(Text, Font);
                imgX = (btnRect.Width - (imgSize + 8 + textSz.Width)) / 2;
                g.DrawImage(Image, new Rectangle(imgX, imgY, imgSize, imgSize));

                var textRect = new Rectangle(imgX + imgSize + 8, 0, btnRect.Width - (imgX + imgSize + 8) - 6, btnRect.Height);
                Color textColor = !Enabled ? Color.FromArgb(120, 118, 115) : ForeColor;
                TextRenderer.DrawText(g, Text, Font, textRect, textColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
            else
            {
                g.DrawImage(Image, new Rectangle(imgX, imgY, imgSize, imgSize));
            }
            return;
        }

        // 5. Draw Text with generous bounds and clear rendering
        if (!string.IsNullOrEmpty(Text))
        {
            Color textColor = !Enabled ? Color.FromArgb(120, 118, 115) : ForeColor;
            var paddedRect = new Rectangle(4, 0, Math.Max(0, btnRect.Width - 8), btnRect.Height);
            TextRenderer.DrawText(g, Text, Font, paddedRect, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    private void DrawVectorIcon(Graphics g, Rectangle bounds)
    {
        Color color = !Enabled ? Color.FromArgb(120, 118, 115) : (_isHovered ? Color.White : IconColor);
        using var brush = new SolidBrush(color);
        using var pen = new Pen(color, 2.5f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        float cx = bounds.X + bounds.Width / 2f;
        float cy = bounds.Y + bounds.Height / 2f;

        switch (IconType)
        {
            case ButtonIconType.Play:
            {
                float size = Math.Min(bounds.Width, bounds.Height) * 0.32f;
                PointF[] triangle =
                {
                    new(cx - size * 0.45f, cy - size * 0.65f),
                    new(cx + size * 0.65f, cy),
                    new(cx - size * 0.45f, cy + size * 0.65f)
                };
                g.FillPolygon(brush, triangle);
                break;
            }

            case ButtonIconType.Pause:
            {
                float h = Math.Min(bounds.Width, bounds.Height) * 0.36f;
                float barW = 3.5f;
                float gap = 4.5f;
                g.FillRectangle(brush, cx - gap - barW, cy - h / 2f, barW, h);
                g.FillRectangle(brush, cx + gap, cy - h / 2f, barW, h);
                break;
            }

            case ButtonIconType.Prev:
            {
                // Smooth chevron matching reference image (кнопка вид.png)
                float arm = Math.Min(bounds.Width, bounds.Height) * 0.20f;
                using var thickPen = new Pen(color, 3.2f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                    LineJoin = LineJoin.Round
                };
                PointF[] chevron =
                {
                    new(cx + arm * 0.6f, cy - arm),
                    new(cx - arm * 0.6f, cy),
                    new(cx + arm * 0.6f, cy + arm)
                };
                g.DrawLines(thickPen, chevron);
                break;
            }

            case ButtonIconType.Next:
            {
                // Smooth chevron matching reference image (кнопка вид.png)
                float arm = Math.Min(bounds.Width, bounds.Height) * 0.20f;
                using var thickPen = new Pen(color, 3.2f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                    LineJoin = LineJoin.Round
                };
                PointF[] chevron =
                {
                    new(cx - arm * 0.6f, cy - arm),
                    new(cx + arm * 0.6f, cy),
                    new(cx - arm * 0.6f, cy + arm)
                };
                g.DrawLines(thickPen, chevron);
                break;
            }

            case ButtonIconType.First:
            {
                float size = Math.Min(bounds.Width, bounds.Height) * 0.28f;
                float barX = cx - size * 0.8f;
                float barH = size * 1.3f;
                g.FillRectangle(brush, barX, cy - barH / 2f, 3.2f, barH);

                PointF[] triangle =
                {
                    new(cx + size * 0.7f, cy - size * 0.65f),
                    new(barX + 4f, cy),
                    new(cx + size * 0.7f, cy + size * 0.65f)
                };
                g.FillPolygon(brush, triangle);
                break;
            }

            case ButtonIconType.Last:
            {
                float size = Math.Min(bounds.Width, bounds.Height) * 0.28f;
                float barX = cx + size * 0.8f;
                float barH = size * 1.3f;
                g.FillRectangle(brush, barX - 3.2f, cy - barH / 2f, 3.2f, barH);

                PointF[] triangle =
                {
                    new(cx - size * 0.7f, cy - size * 0.65f),
                    new(barX - 4f, cy),
                    new(cx - size * 0.7f, cy + size * 0.65f)
                };
                g.FillPolygon(brush, triangle);
                break;
            }

            case ButtonIconType.Gear:
            {
                // Smooth crisp gear
                float outerR = Math.Min(bounds.Width, bounds.Height) * 0.28f;
                float innerR = outerR * 0.55f;
                int teeth = 8;
                using var gearPath = new GraphicsPath();

                for (int i = 0; i < teeth; i++)
                {
                    double a0 = (i * 2 * Math.PI) / teeth;
                    double a1 = ((i + 0.35) * 2 * Math.PI) / teeth;
                    double a2 = ((i + 0.65) * 2 * Math.PI) / teeth;
                    double a3 = ((i + 1.0) * 2 * Math.PI) / teeth;

                    float p0x = cx + (float)(innerR * Math.Cos(a0));
                    float p0y = cy + (float)(innerR * Math.Sin(a0));
                    float p1x = cx + (float)(outerR * Math.Cos(a1));
                    float p1y = cy + (float)(outerR * Math.Sin(a1));
                    float p2x = cx + (float)(outerR * Math.Cos(a2));
                    float p2y = cy + (float)(outerR * Math.Sin(a2));
                    float p3x = cx + (float)(innerR * Math.Cos(a3));
                    float p3y = cy + (float)(innerR * Math.Sin(a3));

                    if (i == 0) gearPath.AddLine(p0x, p0y, p1x, p1y);
                    else gearPath.AddLine(p0x, p0y, p1x, p1y);
                    gearPath.AddLine(p1x, p1y, p2x, p2y);
                    gearPath.AddLine(p2x, p2y, p3x, p3y);
                }
                gearPath.CloseFigure();
                gearPath.AddEllipse(cx - innerR * 0.45f, cy - innerR * 0.45f, innerR * 0.9f, innerR * 0.9f);
                g.FillPath(brush, gearPath);
                break;
            }

            case ButtonIconType.Search:
            {
                float radius = Math.Min(bounds.Width, bounds.Height) * 0.16f;
                float lensX = cx - 2f;
                float lensY = cy - 2f;
                using var lensPen = new Pen(color, 2.2f);
                g.DrawEllipse(lensPen, lensX - radius, lensY - radius, radius * 2, radius * 2);
                g.DrawLine(lensPen, lensX + radius * 0.7f, lensY + radius * 0.7f, cx + radius * 1.7f, cy + radius * 1.7f);
                break;
            }

            default:
                break;
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
