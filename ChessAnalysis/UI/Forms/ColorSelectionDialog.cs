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
        Size = new Size(520, 310);
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
            Height = 50,
            TextAlign = ContentAlignment.MiddleCenter
        };

        var lblSubtitle = new Label
        {
            Text = "Анализ ошибок и предупреждений будет сфокусирован на вашей игре",
            Font = new Font("Segoe UI", 9f),
            ForeColor = ChessTheme.TextSecondary,
            Dock = DockStyle.Top,
            Height = 24,
            TextAlign = ContentAlignment.MiddleCenter
        };

        // Center Buttons Panel
        var pnlButtons = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 16, 24, 16)
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
        btnWhite.Location = new Point(24, 20);
        btnWhite.Size = new Size(215, 110);

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
        btnBlack.Location = new Point(265, 20);
        btnBlack.Size = new Size(215, 110);

        pnlButtons.Controls.Add(btnWhite);
        pnlButtons.Controls.Add(btnBlack);

        // Bottom skip / cancel panel
        var bottomPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 46,
            BackColor = ChessTheme.PanelDark
        };

        var btnSkip = new RoundedButton
        {
            Text = "Оставить нейтральный вид (Белые снизу)",
            Width = 280,
            Height = 30,
            BorderRadius = 5,
            NormalColor = Color.FromArgb(48, 46, 43),
            HoverColor = Color.FromArgb(62, 60, 56),
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = ChessTheme.TextSecondary,
            Dock = DockStyle.None,
            Location = new Point((520 - 280) / 2, 8)
        };
        btnSkip.Click += (s, e) =>
        {
            SelectedColor = PieceColor.White;
            DialogResult = DialogResult.OK;
            Close();
        };
        bottomPanel.Controls.Add(btnSkip);

        Controls.Add(pnlButtons);
        Controls.Add(lblSubtitle);
        Controls.Add(lblTitle);
        Controls.Add(bottomPanel);
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

        btn.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, btn.Width - 1, btn.Height - 1);
            using var path = CreateRoundedRectanglePath(rect, 10);

            Color bgColor = hovered ? Color.FromArgb(58, 56, 52) : ChessTheme.PanelDark;
            using var bgBrush = new SolidBrush(bgColor);
            e.Graphics.FillPath(bgBrush, path);

            Color borderColor = hovered ? ChessTheme.AccentGreen : ChessTheme.BorderColor;
            using var borderPen = new Pen(borderColor, hovered ? 2f : 1.2f);
            e.Graphics.DrawPath(borderPen, path);

            // Draw Piece Icon if available
            var img = ResourceManager.GetPieceImage(isWhitePiece ? PieceColor.White : PieceColor.Black, PieceType.King);
            if (img != null)
            {
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.DrawImage(img, new Rectangle(16, 25, 48, 48));
            }
        };

        var lblBtnTitle = new Label
        {
            Text = title,
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(72, 22),
            Size = new Size(135, 26),
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand
        };
        lblBtnTitle.Click += (s, e) => onClick();

        var lblPlayer = new Label
        {
            Text = playerInfo,
            Font = new Font("Segoe UI", 9f),
            ForeColor = ChessTheme.TextSecondary,
            Location = new Point(72, 52),
            Size = new Size(135, 48),
            BackColor = Color.Transparent,
            Cursor = Cursors.Hand
        };
        lblPlayer.Click += (s, e) => onClick();

        btn.Controls.Add(lblBtnTitle);
        btn.Controls.Add(lblPlayer);

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
