using System.Drawing;
using System.Drawing.Drawing2D;
using ChessAnalysis.Core.Analysis;
using ChessAnalysis.UI.Resources;

namespace ChessAnalysis.UI.Controls;

public class GameGraphControl : Control
{
    private readonly List<MoveAnalysis> _moves = new();
    private int _selectedPly = -1;
    private int _hoveredPly = -1;

    public event Action<int>? PlySelected;

    public int SelectedPly
    {
        get => _selectedPly;
        set
        {
            if (_selectedPly != value)
            {
                _selectedPly = value;
                Invalidate();
            }
        }
    }

    public GameGraphControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = ChessTheme.PanelDark;
    }

    public void SetMoves(IEnumerable<MoveAnalysis> moves)
    {
        _moves.Clear();
        _moves.AddRange(moves);
        _selectedPly = _moves.Count > 0 ? 0 : -1;
        Invalidate();
    }

    public void AddMove(MoveAnalysis move)
    {
        _moves.Add(move);
        Invalidate();
    }

    public void Clear()
    {
        _moves.Clear();
        _selectedPly = -1;
        _hoveredPly = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        SelectPlyAtPoint(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (e.Button == MouseButtons.Left)
        {
            SelectPlyAtPoint(e.X);
        }
        else
        {
            int ply = XToPly(e.X);
            if (ply != _hoveredPly)
            {
                _hoveredPly = ply;
                Invalidate();
            }
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoveredPly != -1)
        {
            _hoveredPly = -1;
            Invalidate();
        }
    }

    private void SelectPlyAtPoint(int mouseX)
    {
        int ply = XToPly(mouseX);
        if (ply >= 0 && ply < _moves.Count)
        {
            _selectedPly = ply;
            PlySelected?.Invoke(ply);
            Invalidate();
        }
    }

    private int XToPly(int x)
    {
        if (_moves.Count == 0) return -1;
        int padding = 10;
        int graphWidth = Width - padding * 2;
        if (graphWidth <= 0) return -1;

        float step = (float)graphWidth / Math.Max(1, _moves.Count - 1);
        int ply = (int)Math.Round((x - padding) / step);
        return Math.Clamp(ply, 0, _moves.Count - 1);
    }

    private float PlyToX(int ply, int padding, int graphWidth)
    {
        if (_moves.Count <= 1) return padding;
        float step = (float)graphWidth / (_moves.Count - 1);
        return padding + ply * step;
    }

    private float EvalToY(double winProbWhite, int centerY, int graphHalfHeight)
    {
        // winProbWhite: 0 (Black 100%) to 100 (White 100%), 50 is center
        double norm = (winProbWhite - 50.0) / 50.0; // -1.0 to 1.0
        return (float)(centerY - norm * (graphHalfHeight - 6));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int padding = 10;
        int graphWidth = Width - padding * 2;
        int graphHeight = Height - 8;
        int centerY = Height / 2;
        int halfH = graphHeight / 2;

        // 1. Background Frame
        using var bgBrush = new SolidBrush(ChessTheme.PanelDark);
        g.FillRectangle(bgBrush, ClientRectangle);

        // Center line (0.00 eval)
        using var centerPen = new Pen(Color.FromArgb(80, 255, 255, 255), 1)
        {
            DashStyle = DashStyle.Dash
        };
        g.DrawLine(centerPen, padding, centerY, Width - padding, centerY);

        if (_moves.Count < 2) return;

        // 2. Build curve points
        var points = new PointF[_moves.Count];
        for (int i = 0; i < _moves.Count; i++)
        {
            float x = PlyToX(i, padding, graphWidth);
            float y = EvalToY(_moves[i].PostEval.WinProbabilityWhite, centerY, halfH);
            points[i] = new PointF(x, y);
        }

        // Fill White Advantage Area (above center)
        using (var pathWhite = new GraphicsPath())
        {
            pathWhite.AddLine(points[0].X, centerY, points[0].X, points[0].Y);
            for (int i = 1; i < points.Length; i++) pathWhite.AddLine(points[i - 1], points[i]);
            pathWhite.AddLine(points[^1].X, points[^1].Y, points[^1].X, centerY);
            pathWhite.CloseFigure();

            using var whiteFill = new SolidBrush(Color.FromArgb(50, 240, 242, 245));
            g.FillPath(whiteFill, pathWhite);
        }

        // 3. Draw Main Curve Line
        using (var curvePen = new Pen(Color.FromArgb(220, 225, 235), 2f))
        {
            g.DrawLines(curvePen, points);
        }

        // 4. Draw Badges as colored markers on critical moves (Brilliant, Great, Blunder, Mistake, MissedWin)
        for (int i = 0; i < _moves.Count; i++)
        {
            var m = _moves[i];
            if (m.Classification is MoveClassification.Brilliant or MoveClassification.Great or
                MoveClassification.Blunder or MoveClassification.MissedWin or MoveClassification.Mistake)
            {
                var pt = points[i];
                using var dotBrush = new SolidBrush(m.Classification.GetThemeColor());
                using var dotPen = new Pen(Color.White, 1.5f);

                float r = m.Classification is MoveClassification.Brilliant or MoveClassification.Blunder ? 4.5f : 3.5f;
                g.FillEllipse(dotBrush, pt.X - r, pt.Y - r, r * 2, r * 2);
                g.DrawEllipse(dotPen, pt.X - r, pt.Y - r, r * 2, r * 2);
            }
        }

        // 5. Selected Ply Vertical Line
        if (_selectedPly >= 0 && _selectedPly < _moves.Count)
        {
            float selX = points[_selectedPly].X;
            using var selPen = new Pen(ChessTheme.AccentBlue, 2f);
            g.DrawLine(selPen, selX, 4, selX, Height - 4);

            // Cursor Circle
            using var cursorBrush = new SolidBrush(ChessTheme.AccentBlue);
            using var cursorWhitePen = new Pen(Color.White, 2f);
            g.FillEllipse(cursorBrush, selX - 5, points[_selectedPly].Y - 5, 10, 10);
            g.DrawEllipse(cursorWhitePen, selX - 5, points[_selectedPly].Y - 5, 10, 10);
        }
    }
}
