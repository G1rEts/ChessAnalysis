using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using ChessAnalysis.Core.Chess;
using ChessAnalysis.UI.Controls;
using ChessAnalysis.UI.Resources;

namespace ChessAnalysis.UI.Forms;

/// <summary>
/// Dialog for selecting pawn promotion piece (Queen, Rook, Bishop, Knight).
/// </summary>
public class PromotionSelectionDialog : Form
{
    public PieceType SelectedPiece { get; private set; } = PieceType.Queen;

    public PromotionSelectionDialog(PieceColor color)
    {
        Text = "Превращение пешки";
        ClientSize = new Size(460, 190);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = ChessTheme.BackgroundDark;
        ForeColor = ChessTheme.TextPrimary;
        Font = new Font("Segoe UI", 9.5f);
        Icon = ResourceManager.GetAppIcon();

        var lblTitle = new Label
        {
            Text = "Выберите фигуру для превращения",
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            ForeColor = Color.White,
            Dock = DockStyle.Top,
            Height = 40,
            TextAlign = ContentAlignment.MiddleCenter
        };

        var pnlButtons = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 8, 16, 16)
        };

        var pieces = new[]
        {
            (PieceType.Queen, "Ферзь"),
            (PieceType.Rook, "Ладья"),
            (PieceType.Bishop, "Слон"),
            (PieceType.Knight, "Конь")
        };

        int btnWidth = 100;
        int btnHeight = 110;
        int spacing = 8;
        int startX = (ClientSize.Width - (4 * btnWidth + 3 * spacing)) / 2;

        for (int i = 0; i < pieces.Length; i++)
        {
            var (type, title) = pieces[i];
            var btn = CreatePieceButton(color, type, title);
            btn.Location = new Point(startX + i * (btnWidth + spacing), 10);
            btn.Size = new Size(btnWidth, btnHeight);
            pnlButtons.Controls.Add(btn);
        }

        Controls.Add(pnlButtons);
        Controls.Add(lblTitle);
    }

    private Control CreatePieceButton(PieceColor color, PieceType type, string title)
    {
        var btn = new Panel
        {
            Cursor = Cursors.Hand,
            BackColor = ChessTheme.PanelDark
        };

        bool isHovered = false;

        btn.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, btn.Width - 1, btn.Height - 1);

            using var path = CreateRoundedPath(rect, 8);
            using var bgBrush = new SolidBrush(isHovered ? Color.FromArgb(52, 50, 46) : ChessTheme.PanelDark);
            using var borderPen = new Pen(isHovered ? ChessTheme.AccentGreen : ChessTheme.BorderColor, isHovered ? 2f : 1.2f);

            e.Graphics.FillPath(bgBrush, path);
            e.Graphics.DrawPath(borderPen, path);

            var img = ResourceManager.GetPieceImage(color, type);
            if (img != null)
            {
                int imgSize = 56;
                int imgX = (btn.Width - imgSize) / 2;
                int imgY = 12;
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.DrawImage(img, imgX, imgY, imgSize, imgSize);
            }

            using var font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            using var textBrush = new SolidBrush(isHovered ? Color.White : ChessTheme.TextPrimary);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            e.Graphics.DrawString(title, font, textBrush, new RectangleF(0, 74, btn.Width, 28), sf);
        };

        btn.MouseEnter += (s, e) => { isHovered = true; btn.Invalidate(); };
        btn.MouseLeave += (s, e) => { isHovered = false; btn.Invalidate(); };
        btn.Click += (s, e) =>
        {
            SelectedPiece = type;
            DialogResult = DialogResult.OK;
            Close();
        };

        return btn;
    }

    private static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
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
