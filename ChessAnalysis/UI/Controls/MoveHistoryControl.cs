using System.Drawing;
using System.Drawing.Drawing2D;
using ChessAnalysis.Core.Analysis;
using ChessAnalysis.Core.Chess;
using ChessAnalysis.UI.Resources;

namespace ChessAnalysis.UI.Controls;

public class MoveHistoryControl : UserControl
{
    private readonly List<MoveAnalysis> _moves = new();
    private readonly List<HistoryItem> _items = new();
    private readonly List<string> _variationMoves = new();
    private int _variationStartPly = -1;
    private int _selectedPly = -1;
    private int _hoveredPly = -1;

    private readonly VScrollBar _vScrollBar;

    public event Action<int>? MoveSelected;
    public event Action<int>? MoveHovered;

    private abstract class HistoryItem
    {
        public int TopY { get; set; }
        public int Height { get; set; }
    }

    private class MoveRowItem : HistoryItem
    {
        public int MoveNumber { get; set; }
        public MoveAnalysis? WhiteMove { get; set; }
        public MoveAnalysis? BlackMove { get; set; }
        public bool WhiteShowsEllipsis { get; set; }
        public bool BlackShowsEllipsis { get; set; }
    }

    private class CalloutCardItem : HistoryItem
    {
        public int PlyIndex { get; set; }
        public MoveClassification Classification { get; set; }
        public string Title { get; set; } = string.Empty;
        public string ContinuationLine { get; set; } = string.Empty;
        public PieceColor Color { get; set; }
    }

    public MoveHistoryControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = ChessTheme.PanelDark;
        DoubleBuffered = true;

        _vScrollBar = new VScrollBar
        {
            Dock = DockStyle.Right,
            Width = 14,
            Visible = false
        };
        _vScrollBar.ValueChanged += (s, e) => Invalidate();
        Controls.Add(_vScrollBar);
    }

    public void SetMoves(IEnumerable<MoveAnalysis> moves)
    {
        _moves.Clear();
        _moves.AddRange(moves);
        _variationMoves.Clear();
        _variationStartPly = -1;
        RebuildItems();
        UpdateScroll();
        Invalidate();
    }

    public void SetVariation(int startPly, IEnumerable<string> variationSans)
    {
        _variationStartPly = startPly;
        _variationMoves.Clear();
        _variationMoves.AddRange(variationSans);
        UpdateScroll();
        Invalidate();
    }

    public void ClearVariation()
    {
        _variationStartPly = -1;
        _variationMoves.Clear();
        UpdateScroll();
        Invalidate();
    }

    public void Clear()
    {
        _moves.Clear();
        _items.Clear();
        _variationMoves.Clear();
        _variationStartPly = -1;
        _selectedPly = -1;
        _hoveredPly = -1;
        UpdateScroll();
        Invalidate();
    }

    public void SelectPly(int ply)
    {
        _selectedPly = ply;
        EnsurePlyVisible(ply);
        Invalidate();
    }

    private int RowHeight => 28;
    private int HeaderHeight => 24;
    private int BreadcrumbHeight => 28;
    private int TopOffset => BreadcrumbHeight + HeaderHeight;
    private int NumColWidth => 40;

    private void RebuildItems()
    {
        _items.Clear();
        int moveCount = _moves.Count;
        int currentY = 0;

        for (int i = 0; i < moveCount; )
        {
            var wMove = _moves[i];
            int moveNum = wMove.MoveNumber;
            bool wIsBad = IsMistakeClassification(wMove.Classification);

            if (wIsBad && i + 1 < moveCount && _moves[i + 1].Color == PieceColor.Black)
            {
                var bMove = _moves[i + 1];
                bool bIsBad = IsMistakeClassification(bMove.Classification);

                // Row 1: White move, Black has ellipsis "..."
                var row1 = new MoveRowItem
                {
                    MoveNumber = moveNum,
                    WhiteMove = wMove,
                    BlackShowsEllipsis = true,
                    TopY = currentY,
                    Height = RowHeight
                };
                _items.Add(row1);
                currentY += RowHeight;

                // Callout for White blunder
                var card1 = new CalloutCardItem
                {
                    PlyIndex = wMove.PlyIndex,
                    Classification = wMove.Classification,
                    Title = GetCalloutTitle(wMove),
                    ContinuationLine = GetContinuationLine(wMove),
                    Color = PieceColor.White,
                    TopY = currentY,
                    Height = 44
                };
                _items.Add(card1);
                currentY += 44;

                // Row 2: White has ellipsis "...", Black has the played move
                var row2 = new MoveRowItem
                {
                    MoveNumber = moveNum,
                    WhiteShowsEllipsis = true,
                    BlackMove = bMove,
                    TopY = currentY,
                    Height = RowHeight
                };
                _items.Add(row2);
                currentY += RowHeight;

                if (bIsBad)
                {
                    var card2 = new CalloutCardItem
                    {
                        PlyIndex = bMove.PlyIndex,
                        Classification = bMove.Classification,
                        Title = GetCalloutTitle(bMove),
                        ContinuationLine = GetContinuationLine(bMove),
                        Color = PieceColor.Black,
                        TopY = currentY,
                        Height = 44
                    };
                    _items.Add(card2);
                    currentY += 44;
                }

                i += 2;
            }
            else
            {
                MoveAnalysis? bMove = (i + 1 < moveCount && _moves[i + 1].Color == PieceColor.Black) ? _moves[i + 1] : null;
                bool bIsBad = bMove != null && IsMistakeClassification(bMove.Classification);

                var row = new MoveRowItem
                {
                    MoveNumber = moveNum,
                    WhiteMove = wMove,
                    BlackMove = bMove,
                    TopY = currentY,
                    Height = RowHeight
                };
                _items.Add(row);
                currentY += RowHeight;

                if (wIsBad)
                {
                    var card = new CalloutCardItem
                    {
                        PlyIndex = wMove.PlyIndex,
                        Classification = wMove.Classification,
                        Title = GetCalloutTitle(wMove),
                        ContinuationLine = GetContinuationLine(wMove),
                        Color = PieceColor.White,
                        TopY = currentY,
                        Height = 44
                    };
                    _items.Add(card);
                    currentY += 44;
                }
                else if (bIsBad && bMove != null)
                {
                    var card = new CalloutCardItem
                    {
                        PlyIndex = bMove.PlyIndex,
                        Classification = bMove.Classification,
                        Title = GetCalloutTitle(bMove),
                        ContinuationLine = GetContinuationLine(bMove),
                        Color = PieceColor.Black,
                        TopY = currentY,
                        Height = 44
                    };
                    _items.Add(card);
                    currentY += 44;
                }

                i += (bMove != null ? 2 : 1);
            }
        }
    }

    private static bool IsMistakeClassification(MoveClassification c) =>
        c is MoveClassification.Blunder or MoveClassification.Mistake or MoveClassification.Inaccuracy or MoveClassification.MissedWin;

    private static string GetCalloutTitle(MoveAnalysis ma)
    {
        string best = string.IsNullOrEmpty(ma.BestMoveSan) ? "лучший ход" : ma.BestMoveSan;
        return ma.Classification switch
        {
            MoveClassification.Blunder => $"Зевок. Лучше было {best}.",
            MoveClassification.Mistake => $"Ошибка. Лучше было {best}.",
            MoveClassification.Inaccuracy => $"Неточность. Лучше было {best}.",
            MoveClassification.MissedWin => $"Упущенная победа. Лучше было {best}.",
            _ => $"Лучше было {best}."
        };
    }

    private static string GetContinuationLine(MoveAnalysis ma)
    {
        if (ma.PreEval.Pv.Count > 0)
        {
            var moves = new List<string>();
            var board = new BoardState();
            board.LoadFen(ma.FenBefore);

            int ply = ma.PlyIndex;
            for (int k = 0; k < Math.Min(6, ma.PreEval.Pv.Count); k++)
            {
                string uci = ma.PreEval.Pv[k];
                string san = AnalysisService.ConvertUciToSan(board.ToFen(), uci);
                int moveNo = (ply / 2) + 1;
                bool isWhite = (ply % 2 == 0);

                if (k == 0)
                {
                    moves.Add(isWhite ? $"{moveNo}. {san}" : $"{moveNo}... {san}");
                }
                else if (isWhite)
                {
                    moves.Add($"{moveNo}. {san}");
                }
                else
                {
                    moves.Add(san);
                }

                var mv = Core.Chess.Move.FromUci(uci);
                if (!mv.IsEmpty) board.ApplyMove(mv);
                ply++;
            }
            return string.Join(" ", moves);
        }

        return !string.IsNullOrEmpty(ma.BestMoveSan)
            ? $"{(ma.Color == PieceColor.White ? $"{ma.MoveNumber}." : $"{ma.MoveNumber}...")} {ma.BestMoveSan}"
            : "—";
    }

    private int TotalContentHeight
    {
        get
        {
            int h = _items.Count > 0 ? _items[^1].TopY + _items[^1].Height : 0;
            if (_variationMoves.Count > 0)
            {
                h += 34; // variation banner
            }
            return h;
        }
    }

    private void UpdateScroll()
    {
        int totalH = TotalContentHeight;
        int clientH = Height - TopOffset;

        if (clientH > 0 && totalH > clientH)
        {
            _vScrollBar.Visible = true;
            _vScrollBar.Maximum = Math.Max(0, totalH - clientH + RowHeight);
            _vScrollBar.LargeChange = Math.Max(1, clientH);
            _vScrollBar.SmallChange = Math.Max(1, RowHeight);
        }
        else
        {
            _vScrollBar.Visible = false;
            _vScrollBar.Value = 0;
        }
    }

    private void EnsurePlyVisible(int ply)
    {
        if (ply < 0 || ply >= _moves.Count || !_vScrollBar.Visible) return;

        var targetItem = _items.OfType<MoveRowItem>().FirstOrDefault(r =>
            (r.WhiteMove != null && r.WhiteMove.PlyIndex == ply) ||
            (r.BlackMove != null && r.BlackMove.PlyIndex == ply));

        if (targetItem == null) return;

        int rowTop = targetItem.TopY;
        int rowBottom = rowTop + targetItem.Height;

        int curScroll = _vScrollBar.Value;
        int viewH = Math.Max(1, Height - TopOffset);

        if (rowTop < curScroll)
        {
            _vScrollBar.Value = Math.Max(0, rowTop);
        }
        else if (rowBottom > curScroll + viewH)
        {
            _vScrollBar.Value = Math.Min(_vScrollBar.Maximum, rowBottom - viewH);
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateScroll();
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_vScrollBar.Visible)
        {
            int newVal = _vScrollBar.Value - (e.Delta / 120) * (RowHeight * 2);
            _vScrollBar.Value = Math.Clamp(newVal, _vScrollBar.Minimum, Math.Max(0, _vScrollBar.Maximum - _vScrollBar.LargeChange + 1));
            Invalidate();
        }
    }

    private (int ply, Rectangle rect) HitTestPly(Point pt)
    {
        if (pt.Y < TopOffset) return (-1, Rectangle.Empty);

        int scrollY = _vScrollBar.Visible ? _vScrollBar.Value : 0;
        int availW = Width - (_vScrollBar.Visible ? _vScrollBar.Width : 0);
        int moveColW = Math.Max(10, (availW - NumColWidth) / 2);

        int targetY = pt.Y - TopOffset + scrollY;

        var item = _items.FirstOrDefault(it => targetY >= it.TopY && targetY < it.TopY + it.Height);
        if (item is MoveRowItem row)
        {
            int drawY = TopOffset + row.TopY - scrollY;
            if (pt.X >= NumColWidth && pt.X < NumColWidth + moveColW)
            {
                if (row.WhiteMove != null)
                {
                    return (row.WhiteMove.PlyIndex, new Rectangle(NumColWidth, drawY, moveColW, row.Height));
                }
            }
            else if (pt.X >= NumColWidth + moveColW && pt.X < availW)
            {
                if (row.BlackMove != null)
                {
                    return (row.BlackMove.PlyIndex, new Rectangle(NumColWidth + moveColW, drawY, moveColW, row.Height));
                }
            }
        }
        else if (item is CalloutCardItem card)
        {
            int drawY = TopOffset + card.TopY - scrollY;
            return (card.PlyIndex, new Rectangle(NumColWidth, drawY, availW - NumColWidth, card.Height));
        }

        return (-1, Rectangle.Empty);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var (ply, _) = HitTestPly(e.Location);
        if (ply != _hoveredPly)
        {
            _hoveredPly = ply;
            MoveHovered?.Invoke(ply);
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoveredPly != -1)
        {
            _hoveredPly = -1;
            MoveHovered?.Invoke(-1);
            Invalidate();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;

        var (ply, _) = HitTestPly(e.Location);
        if (ply >= 0 && ply < _moves.Count)
        {
            _selectedPly = ply;
            MoveSelected?.Invoke(ply);
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width <= 10 || Height <= TopOffset + 2) return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        int availW = Math.Max(10, Width - (_vScrollBar.Visible ? _vScrollBar.Width : 0));
        int moveColW = Math.Max(10, (availW - NumColWidth) / 2);
        int scrollY = _vScrollBar.Visible ? _vScrollBar.Value : 0;

        // 1. Draw Breadcrumb Bar (Top)
        DrawBreadcrumbs(g, availW);

        // 2. Draw Table Header
        int headerY = BreadcrumbHeight;
        using var headerBg = new SolidBrush(ChessTheme.PanelDarker);
        g.FillRectangle(headerBg, 0, headerY, availW, HeaderHeight);

        using var headerPen = new Pen(ChessTheme.BorderColor, 1);
        g.DrawLine(headerPen, 0, headerY + HeaderHeight, availW, headerY + HeaderHeight);

        using var headerFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        using var headerBrush = new SolidBrush(ChessTheme.TextSecondary);

        var sfCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        var sfLeft = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };

        g.DrawString("#", headerFont, headerBrush, new Rectangle(0, headerY, NumColWidth, HeaderHeight), sfCenter);
        g.DrawString("Белые", headerFont, headerBrush, new Rectangle(NumColWidth + 8, headerY, moveColW - 8, HeaderHeight), sfLeft);
        g.DrawString("Черные", headerFont, headerBrush, new Rectangle(NumColWidth + moveColW + 8, headerY, moveColW - 8, HeaderHeight), sfLeft);

        // 3. Clip for moves scroll area
        var oldClip = g.Clip;
        int clipH = Math.Max(1, Height - TopOffset - 1);
        g.SetClip(new Rectangle(0, TopOffset + 1, availW, clipH));

        using var rowAltBrush = new SolidBrush(Color.FromArgb(33, 31, 28));
        using var rowNormBrush = new SolidBrush(ChessTheme.PanelDark);
        using var numBrush = new SolidBrush(Color.FromArgb(145, 143, 139));
        using var numFont = new Font("Segoe UI", 9f, FontStyle.Regular);
        using var moveFont = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        using var moveFontBold = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        using var textWhiteBrush = new SolidBrush(Color.FromArgb(235, 235, 235));
        using var selectBgBrush = new SolidBrush(Color.FromArgb(59, 91, 136));
        using var hoverBgBrush = new SolidBrush(Color.FromArgb(52, 50, 47));

        int visibleBottom = scrollY + clipH;

        foreach (var item in _items)
        {
            if (item.TopY + item.Height < scrollY || item.TopY > visibleBottom) continue;

            int drawY = TopOffset + item.TopY - scrollY;

            if (item is MoveRowItem row)
            {
                // Alternating row background
                g.FillRectangle((row.MoveNumber % 2 == 1) ? rowAltBrush : rowNormBrush, 0, drawY, availW, row.Height);

                // Move Number
                if (!row.WhiteShowsEllipsis)
                {
                    g.DrawString($"{row.MoveNumber}.", numFont, numBrush, new Rectangle(0, drawY, NumColWidth, row.Height), sfCenter);
                }

                // White Cell
                if (row.WhiteMove != null)
                {
                    var cellRect = new Rectangle(NumColWidth + 2, drawY + 2, moveColW - 4, row.Height - 4);
                    DrawMoveCell(g, row.WhiteMove, row.WhiteMove.PlyIndex, cellRect, moveFont, moveFontBold, textWhiteBrush, selectBgBrush, hoverBgBrush);
                }
                else if (row.WhiteShowsEllipsis)
                {
                    g.DrawString("...", numFont, numBrush, new Rectangle(NumColWidth + 8, drawY, moveColW - 8, row.Height), sfLeft);
                }

                // Black Cell
                if (row.BlackMove != null)
                {
                    var cellRect = new Rectangle(NumColWidth + moveColW + 2, drawY + 2, moveColW - 4, row.Height - 4);
                    DrawMoveCell(g, row.BlackMove, row.BlackMove.PlyIndex, cellRect, moveFont, moveFontBold, textWhiteBrush, selectBgBrush, hoverBgBrush);
                }
                else if (row.BlackShowsEllipsis)
                {
                    g.DrawString("...", numFont, numBrush, new Rectangle(NumColWidth + moveColW + 8, drawY, moveColW - 8, row.Height), sfLeft);
                }
            }
            else if (item is CalloutCardItem card)
            {
                var cardRect = new Rectangle(6, drawY + 2, availW - 12, card.Height - 4);
                DrawInlineCalloutCard(g, card, cardRect);
            }
        }

        // 4. Draw Variation line banner if active
        if (_variationMoves.Count > 0)
        {
            int totalItemH = _items.Count > 0 ? _items[^1].TopY + _items[^1].Height : 0;
            int varY = TopOffset + totalItemH - scrollY + 4;
            var varRect = new Rectangle(8, varY, availW - 16, 26);

            using var varBg = new SolidBrush(Color.FromArgb(40, 48, 58));
            using var varBorder = new Pen(Color.FromArgb(70, 95, 130), 1f);
            using var varFont = new Font("Segoe UI", 8.5f, FontStyle.Italic);
            using var varBrush = new SolidBrush(Color.FromArgb(180, 205, 235));

            g.FillRectangle(varBg, varRect);
            g.DrawRectangle(varBorder, varRect);

            string varText = $"Вариант: {string.Join(" ", _variationMoves)}";
            g.DrawString(varText, varFont, varBrush, new Rectangle(varRect.X + 8, varRect.Y, varRect.Width - 16, varRect.Height), sfLeft);
        }

        g.Clip = oldClip;
    }

    private void DrawBreadcrumbs(Graphics g, int availW)
    {
        var breadcrumbRect = new Rectangle(0, 0, availW, BreadcrumbHeight);
        using var bgBrush = new SolidBrush(Color.FromArgb(24, 23, 20));
        g.FillRectangle(bgBrush, breadcrumbRect);

        using var borderPen = new Pen(ChessTheme.BorderColor, 1);
        g.DrawLine(borderPen, 0, BreadcrumbHeight - 1, availW, BreadcrumbHeight - 1);

        if (_moves.Count == 0) return;

        var tokens = new List<string>();
        for (int i = 0; i < Math.Min(12, _moves.Count); i++)
        {
            var m = _moves[i];
            if (m.Color == PieceColor.White)
            {
                tokens.Add($"{m.MoveNumber}. {m.San}");
            }
            else
            {
                tokens.Add(m.San);
            }
        }
        if (_moves.Count > 12) tokens.Add("...");

        string breadcrumbText = string.Join(" ", tokens);
        using var font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        using var brush = new SolidBrush(Color.FromArgb(170, 168, 164));
        var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };

        g.DrawString(breadcrumbText, font, brush, new Rectangle(8, 0, availW - 24, BreadcrumbHeight), sf);

        // Dropdown indicator triangle matching Screenshot 4
        using var arrowBrush = new SolidBrush(Color.FromArgb(140, 138, 135));
        PointF[] arrow =
        {
            new(availW - 14, 11),
            new(availW - 6, 11),
            new(availW - 10, 16)
        };
        g.FillPolygon(arrowBrush, arrow);
    }

    private void DrawMoveCell(
        Graphics g,
        MoveAnalysis ma,
        int ply,
        Rectangle rect,
        Font fontNorm,
        Font fontBold,
        Brush textBrush,
        Brush selectBrush,
        Brush hoverBrush)
    {
        bool isSelected = (ply == _selectedPly);
        bool isHovered = (ply == _hoveredPly);

        if (isSelected)
        {
            using var path = CreateRoundedRect(rect, 4);
            g.FillPath(selectBrush, path);
        }
        else if (isHovered)
        {
            using var path = CreateRoundedRect(rect, 4);
            g.FillPath(hoverBrush, path);
        }

        // Draw Move Text
        bool isKeyMove = ma.Classification is MoveClassification.Blunder or MoveClassification.Mistake or MoveClassification.Inaccuracy or MoveClassification.Brilliant or MoveClassification.Great;
        var font = isKeyMove ? fontBold : fontNorm;

        int textX = rect.X + 6;
        int textY = rect.Y + (rect.Height - 16) / 2;

        Brush curBrush = textBrush;
        SolidBrush? keyBrush = null;
        if (isKeyMove && !isSelected)
        {
            keyBrush = new SolidBrush(ma.Classification.GetThemeColor());
            curBrush = keyBrush;
        }

        g.DrawString(ma.San, font, curBrush, textX, textY);
        keyBrush?.Dispose();

        // Draw Badge Icon next to SAN
        var badgeImg = ResourceManager.GetBadgeImage(ma.Classification);
        if (badgeImg != null && ma.Classification != MoveClassification.Good && ma.Classification != MoveClassification.Book)
        {
            var sz = g.MeasureString(ma.San, font);
            int badgeX = (int)(textX + sz.Width + 2);
            int badgeY = rect.Y + (rect.Height - 16) / 2;

            if (badgeX + 16 <= rect.Right - 36)
            {
                g.DrawImage(badgeImg, new Rectangle(badgeX, badgeY, 16, 16));
            }
        }

        // Draw Eval Pill on the right of the cell (matching Screenshot 4: +0.2, -0.4)
        string evalStr = ma.ScoreDisplay;
        if (!string.IsNullOrEmpty(evalStr))
        {
            using var evalFont = new Font("Segoe UI", 8f, FontStyle.Regular);
            using var evalBrush = new SolidBrush(Color.FromArgb(145, 143, 140));
            var sfFar = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
            g.DrawString(evalStr, evalFont, evalBrush, new Rectangle(rect.Right - 46, rect.Y, 42, rect.Height), sfFar);
        }
    }

    private void DrawInlineCalloutCard(Graphics g, CalloutCardItem card, Rectangle rect)
    {
        // Colors matching Screenshot 4
        Color bgCol, borderCol, titleCol, lineCol;
        switch (card.Classification)
        {
            case MoveClassification.Blunder:
            case MoveClassification.MissedWin:
                bgCol = Color.FromArgb(48, 22, 22);
                borderCol = Color.FromArgb(160, 48, 48);
                titleCol = Color.FromArgb(240, 100, 100);
                lineCol = Color.FromArgb(210, 180, 180);
                break;
            case MoveClassification.Mistake:
                bgCol = Color.FromArgb(48, 34, 18);
                borderCol = Color.FromArgb(170, 95, 30);
                titleCol = Color.FromArgb(245, 150, 70);
                lineCol = Color.FromArgb(220, 195, 170);
                break;
            case MoveClassification.Inaccuracy:
            default:
                bgCol = Color.FromArgb(20, 36, 48);
                borderCol = Color.FromArgb(40, 105, 155);
                titleCol = Color.FromArgb(90, 180, 240);
                lineCol = Color.FromArgb(175, 205, 230);
                break;
        }

        using var bgBrush = new SolidBrush(bgCol);
        using var path = CreateRoundedRect(rect, 4);
        g.FillPath(bgBrush, path);

        using var borderPen = new Pen(borderCol, 1f);
        g.DrawPath(borderPen, path);

        // Title line (e.g. "Blunder. Be6 was best.")
        using var titleFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        using var titleBrush = new SolidBrush(titleCol);
        g.DrawString(card.Title, titleFont, titleBrush, rect.X + 8, rect.Y + 4);

        // Continuation line (e.g. "7... Be6 8. Qd2 Bg7 9. Qe2...")
        using var lineFont = new Font("Segoe UI", 8f, FontStyle.Regular);
        using var lineBrush = new SolidBrush(lineCol);
        g.DrawString(card.ContinuationLine, lineFont, lineBrush, rect.X + 8, rect.Y + 22);
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
