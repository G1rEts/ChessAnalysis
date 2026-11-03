using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;
using ChessAnalysis.Core.Analysis;
using ChessAnalysis.Core.Chess;
using ChessAnalysis.Core.Engine;
using ChessAnalysis.UI.Resources;

namespace ChessAnalysis.UI.Controls;

public class ComputerLinesControl : UserControl
{
    public class ComputerLineEntry
    {
        public string Eval { get; set; } = "";
        public string Continuation { get; set; } = "";
    }

    private readonly RoundedButton _btnGear = new()
    {
        IconType = ButtonIconType.Gear,
        Size = new Size(32, 32),
        BorderRadius = 6,
        NormalColor = Color.FromArgb(48, 46, 43),
        HoverColor = Color.FromArgb(64, 62, 58),
        PressedColor = Color.FromArgb(32, 30, 28),
        CustomBorderColor = Color.FromArgb(70, 68, 65)
    };

    private readonly List<ComputerLineEntry> _lines = new();
    private string _statusText = "depth=14 | Stockfish 19 Lite";
    private int _lineCount = 2;

    public event EventHandler? SettingsClicked;

    public int LineCount
    {
        get => _lineCount;
        set
        {
            _lineCount = Math.Clamp(value, 1, 5);
            UpdateHeight();
            Invalidate();
        }
    }

    public ComputerLinesControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = ChessTheme.PanelDarker;
        DoubleBuffered = true;

        UpdateHeight();

        _btnGear.Click += (s, e) => SettingsClicked?.Invoke(this, e);

        var tt = new ToolTip();
        tt.SetToolTip(_btnGear, "Настройки: звук, стрелки, PGN, движок");

        Controls.Add(_btnGear);
    }

    public void UpdateHeight()
    {
        // Thicker lines giving breathing room and clear readability
        Height = Math.Max(56, 22 + _lineCount * 29 + 6);
    }

    public void SetStatus(string status)
    {
        _statusText = status;
        Invalidate();
    }

    public void ClearLines()
    {
        _lines.Clear();
        _lines.Add(new ComputerLineEntry { Eval = "0.0", Continuation = "Ожидание хода..." });
        Invalidate();
    }

    public void SetLines(EngineEvaluation eval, BoardState currentBoard)
    {
        if (eval == null)
        {
            ClearLines();
            return;
        }

        _statusText = $"depth={eval.Depth} | Stockfish 19 Lite";
        _lines.Clear();

        if (eval.Lines.Count > 0)
        {
            int take = Math.Min(_lineCount, eval.Lines.Count);
            for (int i = 0; i < take; i++)
            {
                var el = eval.Lines[i];
                string pill = FormatEvalPill(el.Centipawns, el.MateInMoves);
                string text = FormatPvLine(el.Pv, currentBoard);
                _lines.Add(new ComputerLineEntry { Eval = pill, Continuation = text });
            }
        }
        else
        {
            // Fallback for single/double legacy fields
            string l1Eval = FormatEvalPill(eval.Centipawns, eval.MateInMoves);
            string l1Text = FormatPvLine(eval.Pv, currentBoard);
            _lines.Add(new ComputerLineEntry { Eval = l1Eval, Continuation = l1Text });

            if (_lineCount > 1 && eval.HasSecondBest)
            {
                string l2Eval = FormatEvalPill(eval.SecondBestCentipawns, eval.SecondBestMateIn);
                string l2Text = !string.IsNullOrEmpty(eval.SecondBestMoveUci)
                    ? AnalysisService.ConvertUciToSan(currentBoard.ToFen(), eval.SecondBestMoveUci)
                    : "";
                _lines.Add(new ComputerLineEntry { Eval = l2Eval, Continuation = l2Text });
            }
        }

        Invalidate();
    }

    private static string FormatEvalPill(int centipawns, int? mateIn)
    {
        if (mateIn.HasValue)
        {
            int m = mateIn.Value;
            if (m == 0) return "#";
            return m > 0 ? $"M{m}" : $"-M{Math.Abs(m)}";
        }
        double val = centipawns / 100.0;
        if (Math.Abs(val) < 0.005) return "0.00";
        return val >= 0
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"+{val:0.00}")
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{val:0.00}");
    }

    private static string FormatPvLine(List<string> pv, BoardState board)
    {
        if (pv == null || pv.Count == 0) return "—";

        var tempBoard = board.Clone();
        var parts = new List<string>();
        int moveCount = Math.Min(6, pv.Count);

        for (int i = 0; i < moveCount; i++)
        {
            string uci = pv[i];
            string san = AnalysisService.ConvertUciToSan(tempBoard.ToFen(), uci);
            if (string.IsNullOrEmpty(san)) san = uci;

            int moveNum = tempBoard.FullmoveNumber;
            bool isWhite = tempBoard.SideToMove == PieceColor.White;

            if (i == 0)
            {
                parts.Add(isWhite ? $"{moveNum}. {san}" : $"{moveNum}... {san}");
            }
            else if (isWhite)
            {
                parts.Add($"{moveNum}. {san}");
            }
            else
            {
                parts.Add(san);
            }

            var mv = Core.Chess.Move.FromUci(uci);
            if (mv.IsEmpty) break;
            tempBoard.ApplyMove(mv);
        }

        return string.Join(" ", parts) + (pv.Count > moveCount ? "..." : "");
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_btnGear != null)
        {
            _btnGear.Location = new Point(8, (Height - _btnGear.Height) / 2);
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        using var bgBrush = new SolidBrush(ChessTheme.PanelDarker);
        g.FillRectangle(bgBrush, ClientRectangle);

        using var borderPen = new Pen(ChessTheme.BorderColor, 1f);
        g.DrawLine(borderPen, 0, Height - 1, Width, Height - 1);

        int contentX = _btnGear.Right + 10;
        int availW = Width - contentX - 8;
        if (availW < 50) return;

        // Status header
        using var statusFont = new Font("Segoe UI", 7.5f, FontStyle.Regular);
        using var statusBrush = new SolidBrush(Color.FromArgb(145, 143, 140));
        g.DrawString(_statusText, statusFont, statusBrush, contentX, 3);

        int startY = 22;
        int rowH = 26;

        for (int i = 0; i < _lines.Count && i < _lineCount; i++)
        {
            var line = _lines[i];
            int y = startY + i * (rowH + 3);
            DrawLineRow(g, contentX, y, availW, line.Eval, line.Continuation, isFirst: (i == 0));
        }
    }

    private static void DrawLineRow(Graphics g, int x, int y, int maxW, string evalText, string movesText, bool isFirst)
    {
        int pillW = 54;
        int pillH = 22;

        if (!string.IsNullOrEmpty(evalText))
        {
            var pillRect = new Rectangle(x, y, pillW, pillH);
            using var pillPath = CreateRoundedRect(pillRect, 4);

            bool isPositive = !evalText.StartsWith('-');
            using var pillBg = new SolidBrush(isPositive ? Color.White : Color.FromArgb(42, 40, 37));
            using var pillTextBrush = new SolidBrush(isPositive ? Color.FromArgb(24, 23, 20) : Color.FromArgb(240, 240, 240));

            g.FillPath(pillBg, pillPath);
            if (!isPositive)
            {
                using var pPen = new Pen(Color.FromArgb(75, 73, 68), 1f);
                g.DrawPath(pPen, pillPath);
            }

            using var pillFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            var sfCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(evalText, pillFont, pillTextBrush, pillRect, sfCenter);
        }

        int textX = x + pillW + 10;
        int chevronW = 20;
        int textW = Math.Max(10, maxW - (pillW + 10) - chevronW);

        using var moveFont = new Font("Segoe UI", 9f, isFirst ? FontStyle.Bold : FontStyle.Regular);
        var textColor = isFirst ? Color.FromArgb(245, 245, 245) : Color.FromArgb(195, 193, 190);
        var textRect = new Rectangle(textX, y, textW, pillH);
        TextRenderer.DrawText(g, movesText, moveFont, textRect, textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        // Expand chevron ▼ at the right end matching reference
        int chevronX = x + maxW - 14;
        int chevronY = y + pillH / 2;
        using var chevronBrush = new SolidBrush(Color.FromArgb(140, 138, 135));
        PointF[] chevron =
        {
            new(chevronX - 4, chevronY - 2),
            new(chevronX + 4, chevronY - 2),
            new(chevronX, chevronY + 3)
        };
        g.FillPolygon(chevronBrush, chevron);
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
