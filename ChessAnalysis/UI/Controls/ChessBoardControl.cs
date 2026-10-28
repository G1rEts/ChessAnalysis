using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using ChessAnalysis.Core.Analysis;
using ChessAnalysis.Core.Chess;
using ChessAnalysis.UI.Resources;

namespace ChessAnalysis.UI.Controls;

public readonly record struct BoardArrow(Square From, Square To, Color Color, float Width = 6f, bool HasOutline = false);

public class ChessBoardControl : Control
{
    public bool ShowArrows { get; set; } = true;
    private BoardState _board = new();
    private Square _lastMoveFrom = Square.None;
    private Square _lastMoveTo = Square.None;
    private MoveClassification? _lastMoveBadge = null;

    private Square _selectedSquare = Square.None;
    private List<Move> _legalMovesFromSelected = new();
    private readonly List<BoardArrow> _arrows = new();

    // Pill Popup Animation ("Сильный ход!" / "Блестящий ход!")
    private bool _pillActive = false;
    private MoveClassification _pillBadge;
    private Square _pillSquare = Square.None;
    private readonly System.Diagnostics.Stopwatch _pillStopwatch = new();
    private readonly System.Windows.Forms.Timer _pillTimer;

    public bool IsFlipped { get; set; } = false;
    public bool Interactive { get; set; } = true;

    public List<BoardArrow> Arrows => _arrows;

    public event Action<Move>? MovePlayed;
    public event Action<Square>? SquareClicked;

    public BoardState Board
    {
        get => _board;
        set
        {
            _board = value ?? new BoardState();
            _selectedSquare = Square.None;
            _legalMovesFromSelected.Clear();
            Invalidate();
        }
    }

    public Square LastMoveFrom
    {
        get => _lastMoveFrom;
        set { _lastMoveFrom = value; Invalidate(); }
    }

    public Square LastMoveTo
    {
        get => _lastMoveTo;
        set { _lastMoveTo = value; Invalidate(); }
    }

    public MoveClassification? LastMoveBadge
    {
        get => _lastMoveBadge;
        set { _lastMoveBadge = value; Invalidate(); }
    }

    public ChessBoardControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.UserPaint |
            ControlStyles.ResizeRedraw,
            true);

        BackColor = ChessTheme.BackgroundDark;

        _pillTimer = new System.Windows.Forms.Timer { Interval = 15 };
        _pillTimer.Tick += (s, e) =>
        {
            if (_pillActive)
            {
                if (_pillStopwatch.ElapsedMilliseconds > 650)
                {
                    _pillActive = false;
                    _pillTimer.Stop();
                }
                Invalidate();
            }
            else
            {
                _pillTimer.Stop();
            }
        };
    }

    public void SetPosition(BoardState board, Square from = default, Square to = default, MoveClassification? badge = null, bool animate = true)
    {
        _board = board;
        _lastMoveFrom = from;
        _lastMoveTo = to;
        _lastMoveBadge = badge;
        _selectedSquare = Square.None;
        _legalMovesFromSelected.Clear();

        if (animate && to.IsValid && (badge == MoveClassification.Brilliant || badge == MoveClassification.Great))
        {
            _pillActive = true;
            _pillBadge = badge.Value;
            _pillSquare = to;
            _pillStopwatch.Restart();
            _pillTimer.Start();
        }
        else
        {
            _pillActive = false;
            _pillTimer.Stop();
        }

        Invalidate();
    }

    public void ClearArrows()
    {
        _arrows.Clear();
        Invalidate();
    }

    public void AddArrow(Square from, Square to, Color color, float width = 6f, bool hasOutline = false)
    {
        _arrows.Add(new BoardArrow(from, to, color, width, hasOutline));
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    private Rectangle GetBoardRect()
    {
        int size = Math.Min(Width, Height);
        int margin = 1;
        int boardSize = Math.Max(64, size - margin * 2);
        int x = (Width - boardSize) / 2;
        int y = (Height - boardSize) / 2;
        return new Rectangle(x, y, boardSize, boardSize);
    }

    private Square PointToSquare(Point pt)
    {
        var boardRect = GetBoardRect();
        if (!boardRect.Contains(pt)) return Square.None;

        float sqSize = boardRect.Width / 8.0f;
        int col = (int)((pt.X - boardRect.X) / sqSize);
        int row = (int)((pt.Y - boardRect.Y) / sqSize);

        if (col is < 0 or > 7 || row is < 0 or > 7) return Square.None;

        int file = IsFlipped ? 7 - col : col;
        int rank = IsFlipped ? row : 7 - row;

        return new Square(file, rank);
    }

    private Rectangle GetSquareRect(Square sq, Rectangle boardRect)
    {
        if (!sq.IsValid) return Rectangle.Empty;

        float sqSize = boardRect.Width / 8.0f;
        int col = IsFlipped ? 7 - sq.File : sq.File;
        int row = IsFlipped ? sq.Rank : 7 - sq.Rank;

        int x = (int)(boardRect.X + col * sqSize);
        int y = (int)(boardRect.Y + row * sqSize);
        int w = (int)(boardRect.X + (col + 1) * sqSize) - x;
        int h = (int)(boardRect.Y + (row + 1) * sqSize) - y;

        return new Rectangle(x, y, w, h);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;

        var clickedSq = PointToSquare(e.Location);
        SquareClicked?.Invoke(clickedSq);

        if (!Interactive) return;

        if (_selectedSquare.IsValid)
        {
            if (clickedSq == _selectedSquare)
            {
                // Clicking on the selected square deselects it
                _selectedSquare = Square.None;
                _legalMovesFromSelected.Clear();
                Invalidate();
                return;
            }

            // Check if clicked square is a target for a legal move
            var targetMove = _legalMovesFromSelected.FirstOrDefault(m => m.To == clickedSq);
            if (!targetMove.IsEmpty)
            {
                // Auto promote to Queen if not specified
                if (targetMove.Promotion != PieceType.None)
                {
                    targetMove = targetMove with { Promotion = PieceType.Queen };
                }

                if (targetMove.IsCapture || !_board[targetMove.To].IsEmpty || targetMove.To == _board.EnPassantSquare)
                {
                    SoundService.PlayCapture();
                }
                else
                {
                    SoundService.PlayMove();
                }

                MovePlayed?.Invoke(targetMove);
                _selectedSquare = Square.None;
                _legalMovesFromSelected.Clear();
                Invalidate();
                return;
            }
        }

        // Select piece of side to move
        var piece = _board[clickedSq];
        if (!piece.IsEmpty && piece.Color == _board.SideToMove)
        {
            _selectedSquare = clickedSq;
            _legalMovesFromSelected = _board.GenerateLegalMoves()
                .Where(m => m.From == clickedSq)
                .ToList();
        }
        else
        {
            _selectedSquare = Square.None;
            _legalMovesFromSelected.Clear();
        }

        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var boardRect = GetBoardRect();

        // 1. Draw outer border/frame
        using (var borderPen = new Pen(ChessTheme.BorderColor, 2))
        {
            g.DrawRectangle(borderPen, boardRect.X - 1, boardRect.Y - 1, boardRect.Width + 2, boardRect.Height + 2);
        }

        float sqSize = boardRect.Width / 8.0f;

        // 2. Draw Squares (White / Dark-Gray theme)
        using var lightBrush = new SolidBrush(ChessTheme.LightSquare);
        using var darkBrush = new SolidBrush(ChessTheme.DarkSquare);
        using var fromHighlightBrush = new SolidBrush(ChessTheme.MoveHighlightFrom);
        using var toHighlightBrush = new SolidBrush(ChessTheme.MoveHighlightTo);
        using var selectBrush = new SolidBrush(ChessTheme.SelectedSquare);

        for (int r = 0; r < 8; r++)
        {
            for (int f = 0; f < 8; f++)
            {
                var sq = new Square(f, r);
                var sqRect = GetSquareRect(sq, boardRect);

                bool isLight = (f + r) % 2 != 0;
                g.FillRectangle(isLight ? lightBrush : darkBrush, sqRect);

                // Highlight Last Move (tinted with classification badge color if available)
                if (sq == _lastMoveFrom || sq == _lastMoveTo)
                {
                    if (_lastMoveBadge.HasValue)
                    {
                        Color themeCol = _lastMoveBadge.Value.GetThemeColor();
                        int alpha = sq == _lastMoveTo ? 100 : 65;
                        using var badgeBrush = new SolidBrush(Color.FromArgb(alpha, themeCol));
                        g.FillRectangle(badgeBrush, sqRect);
                    }
                    else
                    {
                        g.FillRectangle(sq == _lastMoveFrom ? fromHighlightBrush : toHighlightBrush, sqRect);
                    }
                }

                // Highlight Selected Piece
                if (sq == _selectedSquare)
                {
                    g.FillRectangle(selectBrush, sqRect);
                }

                // Draw Coordinates on border squares
                DrawCoordinates(g, sq, sqRect, isLight);
            }
        }

        // 3. Draw Legal Move Dots / Capture Rings
        if (_selectedSquare.IsValid && _legalMovesFromSelected.Count > 0)
        {
            using var dotBrush = new SolidBrush(ChessTheme.LegalMoveDot);
            using var ringPen = new Pen(ChessTheme.LegalMoveDot, Math.Max(2, sqSize * 0.08f));

            foreach (var m in _legalMovesFromSelected)
            {
                var targetRect = GetSquareRect(m.To, boardRect);
                if (_board[m.To].IsEmpty && m.To != _board.EnPassantSquare)
                {
                    // Center dot
                    int dotSize = (int)(sqSize * 0.28f);
                    int dx = targetRect.X + (targetRect.Width - dotSize) / 2;
                    int dy = targetRect.Y + (targetRect.Height - dotSize) / 2;
                    g.FillEllipse(dotBrush, dx, dy, dotSize, dotSize);
                }
                else
                {
                    // Capture outer ring
                    int inset = (int)(sqSize * 0.12f);
                    var ringRect = new Rectangle(
                        targetRect.X + inset,
                        targetRect.Y + inset,
                        targetRect.Width - inset * 2,
                        targetRect.Height - inset * 2);
                    g.DrawEllipse(ringPen, ringRect);
                }
            }
        }

        // 4. Draw Tactical Arrows
        if (ShowArrows)
        {
            foreach (var arrow in _arrows)
            {
                DrawArrow(g, arrow, boardRect);
            }
        }

        // 5. Draw Pieces
        for (int i = 0; i < 64; i++)
        {
            var sq = new Square(i);
            var piece = _board[sq];
            if (!piece.IsEmpty)
            {
                var sqRect = GetSquareRect(sq, boardRect);
                ResourceManager.DrawPiece(g, piece, sqRect);
            }
        }

        // 6. Draw Animated Pill or Corner Badge
        DrawAnimatedPillOrBadge(g, boardRect);
    }

    private void DrawArrow(Graphics g, BoardArrow arrow, Rectangle boardRect)
    {
        if (!arrow.From.IsValid || !arrow.To.IsValid) return;

        var fromRect = GetSquareRect(arrow.From, boardRect);
        var toRect = GetSquareRect(arrow.To, boardRect);

        float x1 = fromRect.X + fromRect.Width / 2f;
        float y1 = fromRect.Y + fromRect.Height / 2f;
        float x2 = toRect.X + toRect.Width / 2f;
        float y2 = toRect.Y + toRect.Height / 2f;

        float dx = x2 - x1;
        float dy = y2 - y1;
        float dist = (float)Math.Sqrt(dx * dx + dy * dy);
        if (dist < 4f) return;

        float ux = dx / dist;
        float uy = dy / dist;
        float nx = -uy;
        float ny = ux;

        float sqSize = boardRect.Width / 8f;
        float headLen = Math.Clamp(sqSize * 0.38f, 16f, 28f);
        float headHalfWidth = headLen * 0.65f;

        // Pull back tip slightly so it points cleanly into target square
        float endX = x2 - ux * (sqSize * 0.15f);
        float endY = y2 - uy * (sqSize * 0.15f);

        float baseHeadX = endX - ux * headLen;
        float baseHeadY = endY - uy * headLen;

        PointF[] headPoints =
        {
            new(endX, endY),
            new(baseHeadX + nx * headHalfWidth, baseHeadY + ny * headHalfWidth),
            new(baseHeadX - nx * headHalfWidth, baseHeadY - ny * headHalfWidth)
        };

        // Outline / drop shadow for bright or explicitly outlined arrows
        bool isBright = arrow.Color.R > 200 && arrow.Color.G > 200 && arrow.Color.B > 200;
        if (isBright || arrow.HasOutline)
        {
            using var outlinePen = new Pen(Color.FromArgb(170, 20, 20, 25), arrow.Width + 2.5f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Flat
            };
            g.DrawLine(outlinePen, x1, y1, baseHeadX, baseHeadY);

            using var outlineBrush = new SolidBrush(Color.FromArgb(170, 20, 20, 25));
            g.FillPolygon(outlineBrush, headPoints);
        }

        using var brush = new SolidBrush(arrow.Color);
        using var pen = new Pen(arrow.Color, arrow.Width)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Flat
        };

        // Shaft
        g.DrawLine(pen, x1, y1, baseHeadX, baseHeadY);

        // Arrowhead
        g.FillPolygon(brush, headPoints);
    }

    private void DrawAnimatedPillOrBadge(Graphics g, Rectangle boardRect)
    {
        if (!_lastMoveTo.IsValid || !_lastMoveBadge.HasValue) return;

        var destSqRect = GetSquareRect(_lastMoveTo, boardRect);
        int badgeSize = Math.Min(40, Math.Max(22, (int)(destSqRect.Width * 0.42f)));
        int targetX = destSqRect.Right - badgeSize - 2;
        int targetY = destSqRect.Top + 2;

        if (_pillActive && (_lastMoveBadge == MoveClassification.Brilliant || _lastMoveBadge == MoveClassification.Great))
        {
            long ms = _pillStopwatch.ElapsedMilliseconds;
            if (ms > 600)
            {
                _pillActive = false;
                _pillTimer.Stop();
                ResourceManager.DrawCornerBadge(g, _lastMoveBadge.Value, destSqRect);
                return;
            }

            string pillText = _lastMoveBadge == MoveClassification.Brilliant ? "Блестящий ход!" : "Сильный ход!";
            Color textColor = _lastMoveBadge == MoveClassification.Brilliant
                ? Color.FromArgb(22, 160, 133)
                : Color.FromArgb(74, 136, 199);

            float initWidth = Math.Max(180f, destSqRect.Width * 1.8f);
            float initHeight = 36f;
            float initX = Math.Clamp(destSqRect.X + (destSqRect.Width - initWidth) / 2f, boardRect.Left + 6, boardRect.Right - initWidth - 6);
            float initY = Math.Max(boardRect.Top + 6, destSqRect.Y - initHeight * 0.7f);

            float curX, curY, curW, curH;
            float textAlpha = 1.0f;
            float badgeAlpha = 0.0f;

            if (ms <= 120)
            {
                // Quick punchy Pop In (0..120ms)
                float t = ms / 120f;
                float scale = 0.6f + 0.4f * t;
                curW = initWidth * scale;
                curH = initHeight * scale;
                curX = initX + (initWidth - curW) / 2f;
                curY = initY + (initHeight - curH) / 2f;
                textAlpha = t;
            }
            else if (ms <= 320)
            {
                // Brief readable hold (120..320ms)
                curX = initX;
                curY = initY;
                curW = initWidth;
                curH = initHeight;
                textAlpha = 1.0f;
            }
            else
            {
                // Smooth collapse and fold towards corner into PNG badge (320..600ms)
                float t = (ms - 320) / 280f;
                float smoothT = (float)(t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2);

                curX = initX + (targetX - initX) * smoothT;
                curY = initY + (targetY - initY) * smoothT;
                curW = initWidth + (badgeSize - initWidth) * smoothT;
                curH = initHeight + (badgeSize - initHeight) * smoothT;

                textAlpha = Math.Clamp(1.0f - smoothT * 2.5f, 0f, 1f);
                badgeAlpha = Math.Clamp((smoothT - 0.35f) * 1.6f, 0f, 1f);
            }

            var curRect = new RectangleF(curX, curY, curW, curH);

            // Shadow
            using var shadowBrush = new SolidBrush(Color.FromArgb(90, 0, 0, 0));
            var shadowRect = new RectangleF(curX + 2, curY + 3, curW, curH);
            FillRoundedPill(g, shadowBrush, shadowRect, curH / 2f);

            // White capsule background
            using var bgBrush = new SolidBrush(Color.FromArgb(250, 252, 255));
            FillRoundedPill(g, bgBrush, curRect, curH / 2f);

            // Border
            using var borderPen = new Pen(Color.FromArgb(215, 225, 235), 1.5f);
            DrawRoundedPill(g, borderPen, curRect, curH / 2f);

            // Text
            if (textAlpha > 0.05f)
            {
                int textAlphaByte = (int)(textAlpha * 255);
                using var textBrush = new SolidBrush(Color.FromArgb(textAlphaByte, textColor));
                using var font = new Font("Segoe UI", Math.Max(7, curH * 0.42f), FontStyle.Bold);
                var sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                g.DrawString(pillText, font, textBrush, curRect, sf);
            }

            // Badge icon fading in as pill morphs
            if (badgeAlpha > 0.05f)
            {
                var targetBadgeRect = new Rectangle((int)curX, (int)curY, (int)curW, (int)curH);
                var badgeImg = ResourceManager.GetBadgeImage(_lastMoveBadge.Value);
                if (badgeImg != null)
                {
                    g.DrawImage(badgeImg, targetBadgeRect);
                }
                else
                {
                    ResourceManager.DrawCornerBadge(g, _lastMoveBadge.Value, destSqRect);
                }
            }
        }
        else
        {
            // Static Corner Badge
            ResourceManager.DrawCornerBadge(g, _lastMoveBadge.Value, destSqRect);
        }
    }

    private static void FillRoundedPill(Graphics g, Brush brush, RectangleF rect, float radius)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        radius = Math.Min(radius, Math.Min(rect.Width / 2f, rect.Height / 2f));
        using var path = GetPillPath(rect, radius);
        g.FillPath(brush, path);
    }

    private static void DrawRoundedPill(Graphics g, Pen pen, RectangleF rect, float radius)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;
        radius = Math.Min(radius, Math.Min(rect.Width / 2f, rect.Height / 2f));
        using var path = GetPillPath(rect, radius);
        g.DrawPath(pen, path);
    }

    private static GraphicsPath GetPillPath(RectangleF r, float radius)
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

    private void DrawCoordinates(Graphics g, Square sq, Rectangle sqRect, bool isLightSquare)
    {
        // Rank numbers on the left-most column
        int leftColFile = IsFlipped ? 7 : 0;
        int bottomRowRank = IsFlipped ? 7 : 0;

        Color textColor = isLightSquare ? Color.FromArgb(120, 124, 134) : Color.FromArgb(190, 194, 202);
        using var brush = new SolidBrush(textColor);
        using var font = new Font("Segoe UI", Math.Max(8, sqRect.Height * 0.16f), FontStyle.Bold);

        if (sq.File == leftColFile)
        {
            string rankStr = (sq.Rank + 1).ToString();
            g.DrawString(rankStr, font, brush, sqRect.X + 2, sqRect.Y + 2);
        }

        if (sq.Rank == bottomRowRank)
        {
            string fileStr = ((char)('a' + sq.File)).ToString();
            var sf = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Far };
            g.DrawString(fileStr, font, brush, sqRect.Right - 2, sqRect.Bottom - 2, sf);
        }
    }
}
