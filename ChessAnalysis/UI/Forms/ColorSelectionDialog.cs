using System.Drawing;
using System.Drawing.Drawing2D;
using ChessAnalysis.Core.Chess;
using ChessAnalysis.UI.Controls;
using ChessAnalysis.UI.Resources;

namespace ChessAnalysis.UI.Forms;

public class ColorSelectionDialog : Form
{
    public PieceColor SelectedColor { get; private set; } = PieceColor.White;

    public ColorSelectionDialog(string whitePlayer, string? whiteElo, string blackPlayer, string? blackElo)
    {
        Text = "Выбор стороны партии";
        Size = new Size(520, 230);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = ChessTheme.BackgroundDark;
        ForeColor = ChessTheme.TextPrimary;
        Font = new Font("Segoe UI", 9.5f);

        // Header Title
        var lblTitle = new Label
        {
            Text = "За какую сторону вы играли?",
            Font = new Font("Segoe UI", 13.5f, FontStyle.Bold),
            ForeColor = Color.White,
            Dock = DockStyle.Top,
            Height = 44,
            TextAlign = ContentAlignment.MiddleCenter
        };

        var lblSubtitle = new Label
        {
            Text = "Анализ ошибок и вариантов будет сфокусирован на вашей игре",
            Font = new Font("Segoe UI", 9f),
            ForeColor = ChessTheme.TextSecondary,
            Dock = DockStyle.Top,
            Height = 22,
            TextAlign = ContentAlignment.MiddleCenter
        };

        // Center Buttons Panel
        var pnlButtons = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 8, 24, 16)
        };

        string wText = string.IsNullOrWhiteSpace(whitePlayer) ? "Белые" : whitePlayer;
        if (!string.IsNullOrWhiteSpace(whiteElo)) wText += $" ({whiteElo})";

        string bText = string.IsNullOrWhiteSpace(blackPlayer) ? "Черные" : blackPlayer;
        if (!string.IsNullOrWhiteSpace(blackElo)) bText += $" ({blackElo})";

        var btnWhite = CreateColorButton(
            title: "♔ Белые",
            playerInfo: wText,
            isWhitePiece: true,
            onClick: () =>
            {
                SelectedColor = PieceColor.White;
                DialogResult = DialogResult.OK;
                Close();
            });
        btnWhite.Location = new Point(24, 12);
        btnWhite.Size = new Size(220, 105);

        var btnBlack = CreateColorButton(
            title: "♚ Черные",
            playerInfo: bText,
            isWhitePiece: false,
            onClick: () =>
            {
                SelectedColor = PieceColor.Black;
                DialogResult = DialogResult.OK;
                Close();
            });
        btnBlack.Location = new Point(260, 12);
        btnBlack.Size = new Size(220, 105);

        pnlButtons.Controls.Add(btnWhite);
        pnlButtons.Controls.Add(btnBlack);

        Controls.Add(pnlButtons);
        Controls.Add(lblSubtitle);
        Controls.Add(lblTitle);
    }

    private Control CreateColorButton(string title, string playerInfo, bool isWhitePiece, Action onClick)
    {
        var btn = new Panel
        {
            Cursor = Cursors.Hand,
            BackColor = ChessTheme.PanelDark
        };

        bool hovered = false;
        btn.MouseEnter += (s, e) => { hovered = true; btn.Invalidate(); };
        btn.MouseLeave += (s, e) => { hovered = false; btn.Invalidate(); };
        btn.Click += (s, e) => onClick();
        btn.MouseUp += (s, e) => { if (e.Button == MouseButtons.Left) onClick(); };

        btn.Paint += (s, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            var rect = new Rectangle(0, 0, btn.Width - 1, btn.Height - 1);
            using var path = CreateRoundedRectanglePath(rect, 10);

            Color bgColor = hovered ? Color.FromArgb(58, 56, 52) : ChessTheme.PanelDark;
            using var bgBrush = new SolidBrush(bgColor);
            g.FillPath(bgBrush, path);

            Color borderColor = hovered ? ChessTheme.AccentGreen : ChessTheme.BorderColor;
            using var borderPen = new Pen(borderColor, hovered ? 2f : 1.2f);
            g.DrawPath(borderPen, path);

            // Draw Piece Icon
            var img = ResourceManager.GetPieceImage(isWhitePiece ? PieceColor.White : PieceColor.Black, PieceType.King);
            if (img != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(img, new Rectangle(14, 25, 48, 48));
            }

            // Draw Title (Bold)
            using var titleFont = new Font("Segoe UI", 12f, FontStyle.Bold);
            using var titleBrush = new SolidBrush(Color.White);
            var titleRect = new RectangleF(68, 18, btn.Width - 74, 28);
            g.DrawString(title, titleFont, titleBrush, titleRect);

            // Draw Player Info
            using var playerFont = new Font("Segoe UI", 9f);
            using var playerBrush = new SolidBrush(ChessTheme.TextSecondary);
            using var sf = new StringFormat
            {
                Trimming = StringTrimming.EllipsisWord,
                FormatFlags = StringFormatFlags.LineLimit
            };
            var playerRect = new RectangleF(68, 48, btn.Width - 74, 50);
            g.DrawString(playerInfo, playerFont, playerBrush, playerRect, sf);
        };

        return btn;
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
