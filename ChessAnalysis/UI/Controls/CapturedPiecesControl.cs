using System.Drawing;
using System.Drawing.Drawing2D;
using ChessAnalysis.Core.Chess;
using ChessAnalysis.UI.Resources;

namespace ChessAnalysis.UI.Controls;

public class CapturedPiecesControl : Control
{
    private readonly List<(PieceColor Color, PieceType Type)> _pieces = new();
    private string _materialDiff = string.Empty;

    public CapturedPiecesControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.SupportsTransparentBackColor,
            true);

        BackColor = Color.Transparent;
        Height = 24;
    }

    public void SetCaptured(int q, int r, int b, int n, int p, PieceColor pieceColor, string materialDiff)
    {
        _pieces.Clear();
        _materialDiff = materialDiff;

        // Order captured pieces: Q, R, B, N, P
        for (int i = 0; i < q; i++) _pieces.Add((pieceColor, PieceType.Queen));
        for (int i = 0; i < r; i++) _pieces.Add((pieceColor, PieceType.Rook));
        for (int i = 0; i < b; i++) _pieces.Add((pieceColor, PieceType.Bishop));
        for (int i = 0; i < n; i++) _pieces.Add((pieceColor, PieceType.Knight));
        for (int i = 0; i < p; i++) _pieces.Add((pieceColor, PieceType.Pawn));

        Invalidate();
    }

    public void Clear()
    {
        _pieces.Clear();
        _materialDiff = string.Empty;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        if (_pieces.Count == 0 && string.IsNullOrEmpty(_materialDiff)) return;

        int curX = 0;
        int iconSize = 16;
        int y = (Height - iconSize) / 2;

        PieceType prevType = PieceType.None;
        foreach (var (color, type) in _pieces)
        {
            var img = ResourceManager.GetPieceImage(color, type);
            if (img != null)
            {
                g.DrawImage(img, new Rectangle(curX, y, iconSize, iconSize));
            }

            // Stagger identical pieces slightly for compact Chess.com look
            int step = (type == PieceType.Pawn && prevType == PieceType.Pawn) ? 9 : 14;
            curX += step;
            prevType = type;
        }

        if (!string.IsNullOrEmpty(_materialDiff))
        {
            curX += 4;
            using var font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            using var brush = new SolidBrush(Color.FromArgb(175, 173, 169));
            var sf = new StringFormat { LineAlignment = StringAlignment.Center };
            g.DrawString(_materialDiff, font, brush, new RectangleF(curX, 0, 45, Height), sf);
        }
    }
}
