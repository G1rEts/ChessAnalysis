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
    private int _hoveredCalloutPly = -1;
    private int _hoveredTokenIndex = -1;

    private readonly VScrollBar _vScrollBar;

    public event Action<int>? MoveSelected;
    public event Action<int>? MoveHovered;
    public event Action<int>? CalloutClicked;
    public event Action<int, int>? CalloutTokenClicked;

    public class CalloutMoveToken
    {
        public int TokenIndex { get; set; }
        public string Text { get; set; } = string.Empty;
        public string San { get; set; } = string.Empty;
        public string Uci { get; set; } = string.Empty;
        public int RelativeX { get; set; }
        public int RelativeY { get; set; } = 28;
        public int Width { get; set; }
        public int Height { get; set; } = 26;
        public Rectangle Bounds { get; set; }
    }

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
        public List<CalloutMoveToken> Tokens { get; } = new();
        public PieceColor Color { get; set; }
    }

    public MoveHistoryControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw |
            ControlStyles.Selectable,
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

    public IReadOnlyList<MoveAnalysis> Moves => _moves;

    public void AddMove(MoveAnalysis ma)
    {
        _moves.Add(ma);
        RebuildItems();
        UpdateScroll();
        Invalidate();
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

    public void UpdateAnalyzedMove(MoveAnalysis ma)
    {
        if (ma.PlyIndex >= 0 && ma.PlyIndex < _moves.Count)
        {
            _moves[ma.PlyIndex] = ma;
            RebuildItems();
            UpdateScroll();
            Invalidate();
        }
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

    private int RowHeight => 40;
    private int HeaderHeight => 26;
    private int BreadcrumbHeight => 28;
    private int TopOffset => BreadcrumbHeight + HeaderHeight;
    private int NumColWidth => 46;

    public PieceColor? UserFilterColor { get; set; }

    public bool EnableCallouts { get; set; } = false;

    public void TruncateAfter(int plyIndex)
    {
        if (plyIndex < 0)
        {
            _moves.Clear();
        }
        else if (plyIndex + 1 < _moves.Count)
        {
            _moves.RemoveRange(plyIndex + 1, _moves.Count - (plyIndex + 1));
        }
        _variationMoves.Clear();
        _variationStartPly = -1;
        RebuildItems();
        UpdateScroll();
        Invalidate();
    }

    private bool ShouldShowCallout(MoveAnalysis m) =>
        EnableCallouts &&
        IsMistakeClassification(m.Classification) &&
        (UserFilterColor == null || m.Color == UserFilterColor.Value);

    private void RebuildItems()
    {
        _items.Clear();
        int moveCount = _moves.Count;
        int currentY = 0;

        for (int i = 0; i < moveCount; )
        {
            var wMove = _moves[i];
            int moveNum = wMove.MoveNumber;
            bool wIsBad = ShouldShowCallout(wMove);

            if (wIsBad && i + 1 < moveCount && _moves[i + 1].Color == PieceColor.Black)
            {
                var bMove = _moves[i + 1];
                bool bIsBad = ShouldShowCallout(bMove);

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
                var card1 = CreateCalloutCard(wMove, PieceColor.White, currentY);
                _items.Add(card1);
                currentY += card1.Height;

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
                    var card2 = CreateCalloutCard(bMove, PieceColor.Black, currentY);
                    _items.Add(card2);
                    currentY += card2.Height;
                }

                i += 2;
            }
            else
            {
                MoveAnalysis? bMove = (i + 1 < moveCount && _moves[i + 1].Color == PieceColor.Black) ? _moves[i + 1] : null;
                bool bIsBad = bMove != null && ShouldShowCallout(bMove);

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
                    var card = CreateCalloutCard(wMove, PieceColor.White, currentY);
                    _items.Add(card);
                    currentY += card.Height;
                }
                else if (bIsBad && bMove != null)
                {
                    var card = CreateCalloutCard(bMove, PieceColor.Black, currentY);
                    _items.Add(card);
                    currentY += card.Height;
                }

                i += (bMove != null ? 2 : 1);
            }
        }
    }

    private static CalloutCardItem CreateCalloutCard(MoveAnalysis ma, PieceColor color, int currentY)
    {
        var card = new CalloutCardItem
        {
            PlyIndex = ma.PlyIndex,
            Classification = ma.Classification,
            Title = GetCalloutTitle(ma),
            ContinuationLine = GetContinuationLine(ma),
            Color = color,
            TopY = currentY,
            Height = 64
        };
        card.Tokens.AddRange(GetContinuationTokens(ma));
        return card;
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

    private static List<CalloutMoveToken> GetContinuationTokens(MoveAnalysis ma)
    {
        var tokens = new List<CalloutMoveToken>();
        if (ma.PreEval.Pv.Count > 0)
        {
            var board = new BoardState();
            board.LoadFen(ma.FenBefore);
            int ply = ma.PlyIndex;

            for (int k = 0; k < Math.Min(6, ma.PreEval.Pv.Count); k++)
            {
                string uci = ma.PreEval.Pv[k];
                string san = AnalysisService.ConvertUciToSan(board.ToFen(), uci);
                if (string.IsNullOrEmpty(san)) san = uci;

                int moveNo = (ply / 2) + 1;
                bool isWhite = (ply % 2 == 0);
                string text = (k == 0)
                    ? (isWhite ? $"{moveNo}. {san}" : $"{moveNo}... {san}")
                    : (isWhite ? $"{moveNo}. {san}" : san);

                tokens.Add(new CalloutMoveToken
                {
                    TokenIndex = k,
                    Text = text,
                    San = san,
                    Uci = uci
                });

                var mv = Core.Chess.Move.FromUci(uci);
                if (!mv.IsEmpty) board.ApplyMove(mv);
                ply++;
            }
        }
        else if (!string.IsNullOrEmpty(ma.BestMoveSan) || !string.IsNullOrEmpty(ma.BestMoveUci))
        {
            string san = !string.IsNullOrEmpty(ma.BestMoveSan) ? ma.BestMoveSan : ma.BestMoveUci;
            string text = (ma.Color == PieceColor.White ? $"{ma.MoveNumber}." : $"{ma.MoveNumber}...") + $" {san}";
            tokens.Add(new CalloutMoveToken
            {
                TokenIndex = 0,
                Text = text,
                San = san,
                Uci = ma.BestMoveUci
            });
        }
        return tokens;
    }

    private int GetVariationBannerHeight(int availW)
    {
        if (_variationMoves.Count == 0) return 0;
        string varText = $"Вариант: {string.Join(" ", _variationMoves)}";
        using var font = new Font("Segoe UI", 9f, FontStyle.Italic);
        int textW = Math.Max(100, availW - 32);
        using var bmp = new Bitmap(1, 1);
        using var g = Graphics.FromImage(bmp);
        var sz = g.MeasureString(varText, font, textW);
        return Math.Max(36, (int)Math.Ceiling(sz.Height) + 16);
    }

    private int TotalContentHeight
    {
        get
        {
            int h = _items.Count > 0 ? _items[^1].TopY + _items[^1].Height : 0;
            if (_variationMoves.Count > 0)
            {
                int availW = Math.Max(100, Width - (_vScrollBar.Visible ? _vScrollBar.Width : 0));
                h += GetVariationBannerHeight(availW) + 8;
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
            _vScrollBar.Maximum = Math.Max(0, totalH);
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

        int maxScroll = Math.Max(0, _vScrollBar.Maximum - _vScrollBar.LargeChange + 1);

        if (rowTop < curScroll)
        {
            _vScrollBar.Value = Math.Clamp(rowTop, 0, maxScroll);
        }
        else if (rowBottom > curScroll + viewH)
        {
            _vScrollBar.Value = Math.Clamp(rowBottom - viewH, 0, maxScroll);
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateScroll();
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        if (!Focused && CanFocus)
        {
            Focus();
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_vScrollBar.Visible)
        {
            int maxScroll = Math.Max(0, _vScrollBar.Maximum - _vScrollBar.LargeChange + 1);
            int newVal = _vScrollBar.Value - (e.Delta / 120) * (RowHeight * 2);
            _vScrollBar.Value = Math.Clamp(newVal, 0, maxScroll);
            Invalidate();
        }
    }

    private (int ply, bool isCard, int tokenIndex, Rectangle rect) HitTest(Point pt)
    {
        if (pt.Y < TopOffset) return (-1, false, -1, Rectangle.Empty);

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
                    return (row.WhiteMove.PlyIndex, false, -1, new Rectangle(NumColWidth, drawY, moveColW, row.Height));
                }
            }
            else if (pt.X >= NumColWidth + moveColW && pt.X < availW)
            {
                if (row.BlackMove != null)
                {
                    return (row.BlackMove.PlyIndex, false, -1, new Rectangle(NumColWidth + moveColW, drawY, moveColW, row.Height));
                }
            }
        }
        else if (item is CalloutCardItem card)
        {
            int drawY = TopOffset + card.TopY - scrollY;
            var cardRect = new Rectangle(6, drawY + 2, availW - 12, card.Height - 4);
            int hitToken = -1;
            for (int k = 0; k < card.Tokens.Count; k++)
            {
                var tok = card.Tokens[k];
                var tokBounds = (tok.Width > 0 && tok.Height > 0)
                    ? new Rectangle(cardRect.X + tok.RelativeX, cardRect.Y + tok.RelativeY, tok.Width, tok.Height)
                    : tok.Bounds;
                if (tokBounds.Contains(pt))
                {
                    hitToken = tok.TokenIndex;
                    break;
                }
            }
            return (card.PlyIndex, true, hitToken, cardRect);
        }

        return (-1, false, -1, Rectangle.Empty);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!Focused && CanFocus)
        {
            Focus();
        }

        var (ply, isCard, tokenIdx, _) = HitTest(e.Location);
        bool changed = false;

        if (isCard)
        {
            if (_hoveredCalloutPly != ply || _hoveredTokenIndex != tokenIdx || _hoveredPly != -1)
            {
                _hoveredCalloutPly = ply;
                _hoveredTokenIndex = tokenIdx;
                _hoveredPly = -1;
                Cursor = Cursors.Hand;
                changed = true;
            }
        }
        else if (ply >= 0)
        {
            if (_hoveredPly != ply || _hoveredCalloutPly != -1 || _hoveredTokenIndex != -1)
            {
                _hoveredPly = ply;
                _hoveredCalloutPly = -1;
                _hoveredTokenIndex = -1;
                Cursor = Cursors.Hand;
                changed = true;
                MoveHovered?.Invoke(ply);
            }
        }
        else
        {
            if (_hoveredPly != -1 || _hoveredCalloutPly != -1 || _hoveredTokenIndex != -1)
            {
                _hoveredPly = -1;
                _hoveredCalloutPly = -1;
                _hoveredTokenIndex = -1;
                Cursor = Cursors.Default;
                changed = true;
                MoveHovered?.Invoke(-1);
            }
        }

        if (changed) Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoveredPly != -1 || _hoveredCalloutPly != -1 || _hoveredTokenIndex != -1)
        {
            _hoveredPly = -1;
            _hoveredCalloutPly = -1;
            _hoveredTokenIndex = -1;
            Cursor = Cursors.Default;
            MoveHovered?.Invoke(-1);
            Invalidate();
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;

        var (ply, isCard, tokenIdx, _) = HitTest(e.Location);
        if (isCard && ply >= 0 && ply < _moves.Count)
        {
            if (tokenIdx >= 0)
            {
                CalloutTokenClicked?.Invoke(ply, tokenIdx);
            }
            else
            {
                CalloutClicked?.Invoke(ply);
            }
            Invalidate();
        }
        else if (ply >= 0 && ply < _moves.Count)
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
        using var numFont = new Font("Segoe UI", 10f, FontStyle.Bold);
        using var moveFont = new Font("Segoe UI", 11f, FontStyle.Regular);
        using var moveFontBold = new Font("Segoe UI", 11f, FontStyle.Bold);
        using var textWhiteBrush = new SolidBrush(Color.FromArgb(235, 235, 235));
        using var selectBgBrush = new SolidBrush(Color.FromArgb(59, 91, 136));
        using var hoverBgBrush = new SolidBrush(Color.FromArgb(64, 61, 56));

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
            int varH = GetVariationBannerHeight(availW);
            var varRect = new Rectangle(8, varY, availW - 16, varH);

            using var varBg = new SolidBrush(Color.FromArgb(40, 48, 58));
            using var varBorder = new Pen(Color.FromArgb(70, 95, 130), 1f);
            using var varFont = new Font("Segoe UI", 9f, FontStyle.Italic);
            using var varBrush = new SolidBrush(Color.FromArgb(180, 205, 235));

            g.FillRectangle(varBg, varRect);
            g.DrawRectangle(varBorder, varRect);

            string varText = $"Вариант: {string.Join(" ", _variationMoves)}";
            var sfWrap = new StringFormat
            {
                Alignment = StringAlignment.Near,
                LineAlignment = StringAlignment.Near
            };
            g.DrawString(varText, varFont, varBrush, new RectangleF(varRect.X + 8, varRect.Y + 6, varRect.Width - 16, varRect.Height - 12), sfWrap);
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
            using var hoverGlowBrush = new SolidBrush(Color.FromArgb(75, 71, 65));
            using var hoverBorderPen = new Pen(Color.FromArgb(180, 172, 155), 1.5f);
            g.FillPath(hoverGlowBrush, path);
            g.DrawPath(hoverBorderPen, path);
        }

        // Draw Move Text
        bool isKeyMove = ma.Classification is MoveClassification.Blunder or MoveClassification.Mistake or MoveClassification.Inaccuracy or MoveClassification.Brilliant or MoveClassification.Great;
        var font = isKeyMove ? fontBold : fontNorm;

        int textX = rect.X + 8;
        int textY = rect.Y + (rect.Height - 20) / 2;

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
        bool isCardHovered = (card.PlyIndex == _hoveredCalloutPly);
        Color bgCol = isCardHovered ? Color.FromArgb(52, 50, 45) : Color.FromArgb(31, 30, 27);
        Color borderCol, titleCol, lineCol;
        switch (card.Classification)
        {
            case MoveClassification.Blunder:
            case MoveClassification.MissedWin:
                borderCol = isCardHovered ? Color.FromArgb(250, 85, 85) : Color.FromArgb(180, 50, 50);
                titleCol = Color.FromArgb(255, 130, 130);
                lineCol = Color.FromArgb(240, 230, 225);
                break;
            case MoveClassification.Mistake:
                borderCol = isCardHovered ? Color.FromArgb(255, 155, 55) : Color.FromArgb(190, 110, 35);
                titleCol = Color.FromArgb(255, 180, 95);
                lineCol = Color.FromArgb(240, 230, 220);
                break;
            case MoveClassification.Inaccuracy:
            default:
                borderCol = isCardHovered ? Color.FromArgb(95, 175, 240) : Color.FromArgb(60, 125, 175);
                titleCol = Color.FromArgb(140, 215, 255);
                lineCol = Color.FromArgb(220, 235, 250);
                break;
        }

        using var bgBrush = new SolidBrush(bgCol);
        using var path = CreateRoundedRect(rect, 4);
        g.FillPath(bgBrush, path);

        using var borderPen = new Pen(borderCol, isCardHovered ? 2f : 1f);
        g.DrawPath(borderPen, path);

        // Title line
        using var titleFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        using var titleBrush = new SolidBrush(titleCol);
        g.DrawString(card.Title, titleFont, titleBrush, rect.X + 10, rect.Y + 6);

        // Continuation tokens (interactive chips)
        using var lineFont = new Font("Segoe UI", 10f, FontStyle.Bold);
        using var lineBrush = new SolidBrush(Color.White);
        using var tokenBg = new SolidBrush(Color.FromArgb(48, 56, 68));
        using var tokenBorder = new Pen(Color.FromArgb(80, 95, 120), 1f);
        using var tokenHoverBg = new SolidBrush(Color.FromArgb(65, 115, 185));
        using var tokenHoverPen = new Pen(Color.FromArgb(140, 185, 255), 1.2f);

        int curRelX = 10;
        int relY = 28;
        int tokenH = 26;

        if (card.Tokens.Count > 0)
        {
            for (int i = 0; i < card.Tokens.Count; i++)
            {
                var token = card.Tokens[i];
                var sz = g.MeasureString(token.Text, lineFont);
                int tokenW = (int)Math.Ceiling(sz.Width);

                token.RelativeX = curRelX;
                token.RelativeY = relY;
                token.Width = tokenW + 12;
                token.Height = tokenH;

                var tokenRect = new Rectangle(rect.X + curRelX, rect.Y + relY, token.Width, tokenH);
                token.Bounds = tokenRect;

                bool isTokenHovered = (_hoveredCalloutPly == card.PlyIndex && _hoveredTokenIndex == token.TokenIndex);
                using var chipPath = CreateRoundedRect(tokenRect, 4);
                g.FillPath(isTokenHovered ? tokenHoverBg : tokenBg, chipPath);
                g.DrawPath(isTokenHovered ? tokenHoverPen : tokenBorder, chipPath);

                var sfToken = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(token.Text, lineFont, isTokenHovered ? Brushes.White : lineBrush, tokenRect, sfToken);

                curRelX += token.Width + 6;
                if (rect.X + curRelX > rect.Right - 20) break;
            }
        }
        else if (!string.IsNullOrEmpty(card.ContinuationLine))
        {
            g.DrawString(card.ContinuationLine, lineFont, lineBrush, rect.X + 10, rect.Y + 30);
        }
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
