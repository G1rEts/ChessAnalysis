using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using ChessAnalysis.Core.Analysis;
using ChessAnalysis.Core.Chess;
using ChessAnalysis.UI.Forms;
using ChessAnalysis.UI.Resources;

namespace ChessAnalysis.UI.Controls;

public readonly record struct BoardArrow(Square From, Square To, Color Color, float Width = 14f, bool HasOutline = false);

public class ChessBoardControl : Control
{
    public bool ShowArrows { get; set; } = true;
    private BoardState _board = new();
    private Square _lastMoveFrom = Square.None;
    private Square _lastMoveTo = Square.None;
    private MoveClassification? _lastMoveBadge = null;

    private Square _selectedSquare = Square.None;
    private List<Move> _legalMovesFromSelected = [];
    private readonly List<BoardArrow> _arrows = [];

    // User interactive drawings (Right Mouse Button: Drag = Arrow, Click = Circle, LMB = Clear)
    private readonly List<BoardArrow> _userArrows = [];
    private readonly HashSet<Square> _userCircles = [];
    private Square _rmbDownSquare = Square.None;
    private Square _rmbCurrentSquare = Square.None;
    private bool _isRmbDragging = false;

    // Pill Popup Animation ("Сильный ход!" / "Блестящий ход!")
    private bool _pillActive = false;
    private MoveClassification _pillBadge;
    private Square _pillSquare = Square.None;
    private readonly System.Diagnostics.Stopwatch _pillStopwatch = new();
    private readonly System.Windows.Forms.Timer _pillTimer;

    // Mate Badge Animation (0.5s transition from full square to corner)
    private bool _mateActive = false;
    private readonly System.Diagnostics.Stopwatch _mateStopwatch = new();
    private readonly System.Windows.Forms.Timer _mateTimer;

    // Drag and drop state
    private bool _isDragging = false;
    private Square _dragSquare = Square.None;
    private Point _dragPosition = Point.Empty;

    public string LastFenBefore { get; private set; } = string.Empty;

    public bool IsFlipped { get; set; } = false;
    public bool Interactive { get; set; } = true;

    public string? GameResult { get; set; }
    public bool IsAtGameEnd { get; set; } = false;

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

        _mateTimer = new System.Windows.Forms.Timer { Interval = 15 };
        _mateTimer.Tick += (s, e) =>
        {
            if (_mateActive)
            {
                if (_mateStopwatch.ElapsedMilliseconds > 500)
                {
                    _mateActive = false;
                    _mateTimer.Stop();
                }
                Invalidate();
            }
            else
            {
                _mateTimer.Stop();
            }
        };
    }

    public void TriggerGameEndAnimation()
    {
        _mateActive = true;
        _mateStopwatch.Restart();
        _mateTimer.Start();
        Invalidate();
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

        bool isCheckmate = _board.IsInCheck(_board.SideToMove) && _board.GenerateLegalMoves().Count == 0;
        bool isStalemate = !_board.IsInCheck(_board.SideToMove) && _board.GenerateLegalMoves().Count == 0;
        bool isGameEnding = isCheckmate || isStalemate || (IsAtGameEnd && !string.IsNullOrWhiteSpace(GameResult) && GameResult != "*");

        if (animate && isGameEnding)
        {
            _mateActive = true;
            _mateStopwatch.Restart();
            _mateTimer.Start();
        }
        else if (!isGameEnding)
        {
            _mateActive = false;
            _mateTimer.Stop();
        }

        Invalidate();
    }

    public void ClearArrows()
    {
        _arrows.Clear();
        Invalidate();
    }

    public void ClearUserDrawings()
    {
        _userArrows.Clear();
        _userCircles.Clear();
        _rmbDownSquare = Square.None;
        _rmbCurrentSquare = Square.None;
        _isRmbDragging = false;
        Invalidate();
    }

    public void AddArrow(Square from, Square to, Color color, float width = 14f, bool hasOutline = false)
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

        if (e.Button == MouseButtons.Right)
        {
            var sq = PointToSquare(e.Location);
            if (sq.IsValid)
            {
                _rmbDownSquare = sq;
                _rmbCurrentSquare = sq;
                _isRmbDragging = false;
            }
            return;
        }

        if (e.Button != MouseButtons.Left) return;

        // LMB clears any custom annotations (like Chess.com/Lichess)
        if (_userArrows.Count > 0 || _userCircles.Count > 0)
        {
            _userArrows.Clear();
            _userCircles.Clear();
            Invalidate();
        }

        var clickedSq = PointToSquare(e.Location);
        SquareClicked?.Invoke(clickedSq);

        if (!Interactive) return;

        if (_selectedSquare.IsValid)
        {
            if (clickedSq == _selectedSquare)
            {
                // Clicking on the selected square starts drag if held or prepares for drag
                _isDragging = true;
                _dragSquare = clickedSq;
                _dragPosition = e.Location;
                Invalidate();
                return;
            }

            // Check if clicked square is a target for a legal move
            Core.Chess.Move targetMove = Core.Chess.Move.Empty;
            bool isLegalTarget = false;
            foreach (var m in _legalMovesFromSelected)
            {
                if (m.To == clickedSq)
                {
                    targetMove = m;
                    isLegalTarget = true;
                    break;
                }
            }

            if (isLegalTarget && !targetMove.IsEmpty)
            {
                ExecutePlayerMove(targetMove);
                return;
            }
        }

        // Select piece of side to move
        var piece = _board[clickedSq];
        if (!piece.IsEmpty && piece.Color == _board.SideToMove)
        {
            _selectedSquare = clickedSq;
            _legalMovesFromSelected = [.. _board.GenerateLegalMoves()
                .Where(m => m.From == clickedSq)];

            _isDragging = true;
            _dragSquare = clickedSq;
            _dragPosition = e.Location;
        }
        else
        {
            _selectedSquare = Square.None;
            _legalMovesFromSelected.Clear();
            _isDragging = false;
            _dragSquare = Square.None;
        }

        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if ((e.Button & MouseButtons.Right) != 0 && _rmbDownSquare.IsValid)
        {
            var curSq = PointToSquare(e.Location);
            if (curSq.IsValid && curSq != _rmbDownSquare)
            {
                _isRmbDragging = true;
                _rmbCurrentSquare = curSq;
                Invalidate();
            }
            return;
        }

        if (_isDragging && _dragSquare.IsValid)
        {
            var boardRect = GetBoardRect();
            // Restrict dragged piece strictly within board boundaries so it cannot leave the board
            int clampedX = Math.Clamp(e.X, boardRect.Left, boardRect.Right);
            int clampedY = Math.Clamp(e.Y, boardRect.Top, boardRect.Bottom);
            _dragPosition = new Point(clampedX, clampedY);
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button == MouseButtons.Right)
        {
            var dropSq = PointToSquare(e.Location);
            if (_rmbDownSquare.IsValid)
            {
                if (_isRmbDragging && dropSq.IsValid && dropSq != _rmbDownSquare)
                {
                    // Toggle custom user arrow
                    var arrow = new BoardArrow(_rmbDownSquare, dropSq, Color.FromArgb(180, 243, 156, 18), 14f, HasOutline: true);
                    int idx = _userArrows.FindIndex(a => a.From == _rmbDownSquare && a.To == dropSq);
                    if (idx >= 0) _userArrows.RemoveAt(idx);
                    else _userArrows.Add(arrow);
                }
                else if (!_isRmbDragging && _rmbDownSquare.IsValid)
                {
                    // Toggle custom highlight circle
                    if (!_userCircles.Remove(_rmbDownSquare))
                    {
                        _userCircles.Add(_rmbDownSquare);
                    }
                }
            }
            _rmbDownSquare = Square.None;
            _rmbCurrentSquare = Square.None;
            _isRmbDragging = false;
            Invalidate();
            return;
        }

        if (e.Button != MouseButtons.Left) return;

        if (_isDragging)
        {
            _isDragging = false;
            var dropSq = PointToSquare(e.Location);

            if (dropSq.IsValid && dropSq != _dragSquare && _selectedSquare == _dragSquare)
            {
                var targetMove = _legalMovesFromSelected.FirstOrDefault(m => m.To == dropSq);
                if (!targetMove.IsEmpty)
                {
                    ExecutePlayerMove(targetMove);
                    _dragSquare = Square.None;
                    Invalidate();
                    return;
                }
            }

            _dragSquare = Square.None;
            Invalidate();
        }
    }

    private void ExecutePlayerMove(Core.Chess.Move targetMove)
    {
        // Double verification against legal moves of the board
        if (_board.GenerateLegalMoves().Any(lm => lm.From == targetMove.From && lm.To == targetMove.To))
        {
            bool isCap = targetMove.IsCapture || !_board[targetMove.To].IsEmpty || targetMove.To == _board.EnPassantSquare;
            var piece = _board[targetMove.From];
            var promo = PieceType.None;
            if (targetMove.Promotion != PieceType.None || (piece.Type == PieceType.Pawn && (targetMove.To.Rank == 0 || targetMove.To.Rank == 7)))
            {
                using var dlg = new PromotionSelectionDialog(piece.Color);
                if (dlg.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    promo = dlg.SelectedPiece;
                }
                else
                {
                    promo = PieceType.Queen;
                }
            }
            targetMove = targetMove with { Promotion = promo, IsCapture = isCap };

            _lastMoveFrom = targetMove.From;
            _lastMoveTo = targetMove.To;
            _lastMoveBadge = null; // Clear stale badges on manual moves!
            _selectedSquare = Square.None;
            _legalMovesFromSelected.Clear();
            _isDragging = false;
            _dragSquare = Square.None;
            LastFenBefore = _board.ToFen();
            _board.ApplyMove(targetMove);
            Invalidate();

            MovePlayed?.Invoke(targetMove);
        }
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

        // King squares highlighting for Checkmate, Resignation & Stalemate
        bool isCheckmate = _board.IsInCheck(_board.SideToMove) && _board.GenerateLegalMoves().Count == 0;
        bool isStalemate = !_board.IsInCheck(_board.SideToMove) && _board.GenerateLegalMoves().Count == 0;
        bool isResignation = IsAtGameEnd && !isCheckmate && !isStalemate && (GameResult is "1-0" or "0-1");
        bool isPgnDraw = IsAtGameEnd && !isCheckmate && !isStalemate && (GameResult is "1/2-1/2" or "0-0" or "½-½");

        var sideToMove = _board.SideToMove;
        var enemySide = sideToMove == PieceColor.White ? PieceColor.Black : PieceColor.White;
        var kingToMoveSq = _board.FindKing(sideToMove);
        var enemyKingSq = _board.FindKing(enemySide);

        var whiteKingSq = _board.FindKing(PieceColor.White);
        var blackKingSq = _board.FindKing(PieceColor.Black);

        if (isCheckmate)
        {
            // Loser king square: red highlight
            if (kingToMoveSq.IsValid)
            {
                using var loseBrush = new SolidBrush(Color.FromArgb(160, 235, 75, 75));
                g.FillRectangle(loseBrush, GetSquareRect(kingToMoveSq, boardRect));
            }
            // Winner king square: green highlight
            if (enemyKingSq.IsValid)
            {
                using var winBrush = new SolidBrush(Color.FromArgb(160, 129, 182, 76));
                g.FillRectangle(winBrush, GetSquareRect(enemyKingSq, boardRect));
            }
        }
        else if (isResignation)
        {
            var winKing = GameResult == "1-0" ? whiteKingSq : blackKingSq;
            var loseKing = GameResult == "1-0" ? blackKingSq : whiteKingSq;
            if (winKing.IsValid)
            {
                using var winBrush = new SolidBrush(Color.FromArgb(160, 129, 182, 76));
                g.FillRectangle(winBrush, GetSquareRect(winKing, boardRect));
            }
            if (loseKing.IsValid)
            {
                using var loseBrush = new SolidBrush(Color.FromArgb(160, 235, 75, 75));
                g.FillRectangle(loseBrush, GetSquareRect(loseKing, boardRect));
            }
        }
        else if (isStalemate || isPgnDraw)
        {
            // Stalemate / Draw: both king squares 30% black fill
            using var staleBrush = new SolidBrush(Color.FromArgb(77, 0, 0, 0));
            if (whiteKingSq.IsValid) g.FillRectangle(staleBrush, GetSquareRect(whiteKingSq, boardRect));
            if (blackKingSq.IsValid) g.FillRectangle(staleBrush, GetSquareRect(blackKingSq, boardRect));
        }

        // 3. User Highlight Circles (Right Click)
        foreach (var cSq in _userCircles)
        {
            var cr = GetSquareRect(cSq, boardRect);
            if (!cr.IsEmpty)
            {
                using var cPen = new Pen(Color.FromArgb(195, 235, 130, 30), Math.Max(3f, sqSize * 0.08f));
                int inset = (int)(sqSize * 0.09f);
                g.DrawEllipse(cPen, cr.X + inset, cr.Y + inset, cr.Width - inset * 2, cr.Height - inset * 2);
            }
        }

        // 4. Draw Legal Move Dots / Capture Rings
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

        // 5. Draw Tactical Arrows
        if (ShowArrows)
        {
            foreach (var arrow in _arrows)
            {
                DrawArrow(g, arrow, boardRect);
            }
        }

        // 6. Draw User Right-Click Interactive Arrows & Preview
        foreach (var arrow in _userArrows)
        {
            DrawArrow(g, arrow, boardRect);
        }
        if (_isRmbDragging && _rmbDownSquare.IsValid && _rmbCurrentSquare.IsValid && _rmbDownSquare != _rmbCurrentSquare)
        {
            var previewArrow = new BoardArrow(_rmbDownSquare, _rmbCurrentSquare, Color.FromArgb(160, 243, 156, 18), 14f, HasOutline: true);
            DrawArrow(g, previewArrow, boardRect);
        }

        // 5. Draw Pieces
        for (int i = 0; i < 64; i++)
        {
            var sq = new Square(i);
            var piece = _board[sq];
            if (!piece.IsEmpty)
            {
                // Skip rendering on source square while being actively dragged
                if (_isDragging && sq == _dragSquare)
                {
                    continue;
                }
                var sqRect = GetSquareRect(sq, boardRect);
                ResourceManager.DrawPiece(g, piece, sqRect);
            }
        }

        // Draw Dragged Piece centered under cursor with elevation shadow
        if (_isDragging && _dragSquare.IsValid)
        {
            var piece = _board[_dragSquare];
            if (!piece.IsEmpty)
            {
                int pSize = (int)(sqSize * 1.08f);
                int px = _dragPosition.X - pSize / 2;
                int py = _dragPosition.Y - pSize / 2;
                var dragPieceRect = new Rectangle(px, py, pSize, pSize);

                // Subtle lift drop shadow
                using (var shadowBrush = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
                {
                    g.FillEllipse(shadowBrush, px + 4, py + 8, pSize - 8, pSize - 12);
                }

                ResourceManager.DrawPiece(g, piece, dragPieceRect);
            }
        }

        // 6. Draw Animated Pill or Corner Badge
        DrawAnimatedPillOrBadge(g, boardRect);

        // 7. Draw King Badges (Checkmate, Resignation, Stalemate/Draw, Victory)
        DrawKingBadges(g, boardRect);
    }

    private void DrawKingBadges(Graphics g, Rectangle boardRect)
    {
        var sideToMove = _board.SideToMove;
        var enemySide = sideToMove == PieceColor.White ? PieceColor.Black : PieceColor.White;
        var kingToMoveSq = _board.FindKing(sideToMove);
        var enemyKingSq = _board.FindKing(enemySide);

        int legalCount = _board.GenerateLegalMoves().Count;
        bool isCheckmate = _board.IsInCheck(sideToMove) && legalCount == 0;
        bool isStalemate = !_board.IsInCheck(sideToMove) && legalCount == 0;

        // A. On-board Checkmate (with smooth 0.5s transition animation from full square to corner)
        if (isCheckmate)
        {
            if (_mateActive && _mateStopwatch.ElapsedMilliseconds <= 500)
            {
                float t = Math.Clamp(_mateStopwatch.ElapsedMilliseconds / 500.0f, 0f, 1f);
                float ease = (float)(1.0 - Math.Pow(1.0 - t, 3)); // Cubic ease out

                float sqSize = boardRect.Width / 8.0f;
                float startSize = sqSize * 0.90f;
                float endSize = Math.Max(24f, sqSize * 0.40f);
                float curSize = startSize + (endSize - startSize) * ease;

                // Animate checkmate badge on loser king
                if (kingToMoveSq.IsValid)
                {
                    var kRect = GetSquareRect(kingToMoveSq, boardRect);
                    float startX = kRect.X + (sqSize - startSize) / 2f;
                    float startY = kRect.Y + (sqSize - startSize) / 2f;
                    float endX = kRect.Right - endSize * 0.75f;
                    float endY = kRect.Top - endSize * 0.25f;

                    float curX = startX + (endX - startX) * ease;
                    float curY = startY + (endY - startY) * ease;

                    var img = ResourceManager.GetCheckmateBadge();
                    if (img != null) g.DrawImage(img, new RectangleF(curX, curY, curSize, curSize));
                }

                // Animate victory crown/badge on winner king
                if (enemyKingSq.IsValid)
                {
                    var eRect = GetSquareRect(enemyKingSq, boardRect);
                    float startX = eRect.X + (sqSize - startSize) / 2f;
                    float startY = eRect.Y + (sqSize - startSize) / 2f;
                    float endX = eRect.Right - endSize * 0.75f;
                    float endY = eRect.Top - endSize * 0.25f;

                    float curX = startX + (endX - startX) * ease;
                    float curY = startY + (endY - startY) * ease;

                    var img = ResourceManager.GetWinBadge();
                    if (img != null) g.DrawImage(img, new RectangleF(curX, curY, curSize, curSize));
                }
            }
            else
            {
                if (kingToMoveSq.IsValid)
                {
                    var kingRect = GetSquareRect(kingToMoveSq, boardRect);
                    ResourceManager.DrawCheckmateBadge(g, kingRect, boardRect);
                }
                if (enemyKingSq.IsValid)
                {
                    var enemyRect = GetSquareRect(enemyKingSq, boardRect);
                    ResourceManager.DrawWinBadge(g, enemyRect, boardRect);
                }
            }
            return;
        }

        // B. Stalemate / Draw (On-board or PGN Draw)
        bool isDrawResult = IsAtGameEnd && (GameResult is "1/2-1/2" or "0-0" or "½-½");
        if (isStalemate || isDrawResult)
        {
            var whiteKingSq = _board.FindKing(PieceColor.White);
            var blackKingSq = _board.FindKing(PieceColor.Black);

            if (_mateActive && _mateStopwatch.ElapsedMilliseconds <= 500)
            {
                float t = Math.Clamp(_mateStopwatch.ElapsedMilliseconds / 500.0f, 0f, 1f);
                float ease = (float)(1.0 - Math.Pow(1.0 - t, 3));

                float sqSize = boardRect.Width / 8.0f;
                float startSize = sqSize * 0.90f;
                float endSize = Math.Max(24f, sqSize * 0.40f);
                float curSize = startSize + (endSize - startSize) * ease;

                var img = ResourceManager.GetDrawBadge();
                if (img != null)
                {
                    if (whiteKingSq.IsValid)
                    {
                        var wRect = GetSquareRect(whiteKingSq, boardRect);
                        float startX = wRect.X + (sqSize - startSize) / 2f;
                        float startY = wRect.Y + (sqSize - startSize) / 2f;
                        float endX = wRect.Right - endSize * 0.75f;
                        float endY = wRect.Top - endSize * 0.25f;
                        float curX = startX + (endX - startX) * ease;
                        float curY = startY + (endY - startY) * ease;
                        g.DrawImage(img, new RectangleF(curX, curY, curSize, curSize));
                    }
                    if (blackKingSq.IsValid)
                    {
                        var bRect = GetSquareRect(blackKingSq, boardRect);
                        float startX = bRect.X + (sqSize - startSize) / 2f;
                        float startY = bRect.Y + (sqSize - startSize) / 2f;
                        float endX = bRect.Right - endSize * 0.75f;
                        float endY = bRect.Top - endSize * 0.25f;
                        float curX = startX + (endX - startX) * ease;
                        float curY = startY + (endY - startY) * ease;
                        g.DrawImage(img, new RectangleF(curX, curY, curSize, curSize));
                    }
                }
            }
            else
            {
                if (whiteKingSq.IsValid)
                {
                    ResourceManager.DrawDrawBadge(g, GetSquareRect(whiteKingSq, boardRect), boardRect);
                }
                if (blackKingSq.IsValid)
                {
                    ResourceManager.DrawDrawBadge(g, GetSquareRect(blackKingSq, boardRect), boardRect);
                }
            }
            return;
        }

        // C. Game End from PGN (Resignation, Timeout, Draw by agreement/rules)
        if (IsAtGameEnd && !string.IsNullOrWhiteSpace(GameResult) && !isCheckmate && !isStalemate)
        {
            var whiteKingSq = _board.FindKing(PieceColor.White);
            var blackKingSq = _board.FindKing(PieceColor.Black);

            bool isWhiteWin = GameResult == "1-0";
            bool isBlackWin = GameResult == "0-1";
            bool isDraw = GameResult is "1/2-1/2" or "0-0" or "½-½";

            if (isWhiteWin || isBlackWin)
            {
                var winnerSq = isWhiteWin ? whiteKingSq : blackKingSq;
                var loserSq = isWhiteWin ? blackKingSq : whiteKingSq;

                if (_mateActive && _mateStopwatch.ElapsedMilliseconds <= 500)
                {
                    float t = Math.Clamp(_mateStopwatch.ElapsedMilliseconds / 500.0f, 0f, 1f);
                    float ease = (float)(1.0 - Math.Pow(1.0 - t, 3));

                    float sqSize = boardRect.Width / 8.0f;
                    float startSize = sqSize * 0.90f;
                    float endSize = Math.Max(24f, sqSize * 0.40f);
                    float curSize = startSize + (endSize - startSize) * ease;

                    if (loserSq.IsValid)
                    {
                        var lRect = GetSquareRect(loserSq, boardRect);
                        float curX = lRect.X + (sqSize - startSize) / 2f + (lRect.Right - endSize * 0.75f - (lRect.X + (sqSize - startSize) / 2f)) * ease;
                        float curY = lRect.Y + (sqSize - startSize) / 2f + (lRect.Top - endSize * 0.25f - (lRect.Y + (sqSize - startSize) / 2f)) * ease;
                        var img = ResourceManager.GetResignBadge();
                        if (img != null) g.DrawImage(img, new RectangleF(curX, curY, curSize, curSize));
                    }
                    if (winnerSq.IsValid)
                    {
                        var wRect = GetSquareRect(winnerSq, boardRect);
                        float curX = wRect.X + (sqSize - startSize) / 2f + (wRect.Right - endSize * 0.75f - (wRect.X + (sqSize - startSize) / 2f)) * ease;
                        float curY = wRect.Y + (sqSize - startSize) / 2f + (wRect.Top - endSize * 0.25f - (wRect.Y + (sqSize - startSize) / 2f)) * ease;
                        var img = ResourceManager.GetWinBadge();
                        if (img != null) g.DrawImage(img, new RectangleF(curX, curY, curSize, curSize));
                    }
                }
                else
                {
                    if (winnerSq.IsValid)
                    {
                        var wRect = GetSquareRect(winnerSq, boardRect);
                        ResourceManager.DrawWinBadge(g, wRect, boardRect);
                    }
                    if (loserSq.IsValid)
                    {
                        var lRect = GetSquareRect(loserSq, boardRect);
                        ResourceManager.DrawResignBadge(g, lRect, boardRect);
                    }
                }
            }
            else if (isDraw)
            {
                if (whiteKingSq.IsValid)
                {
                    var wRect = GetSquareRect(whiteKingSq, boardRect);
                    ResourceManager.DrawDrawBadge(g, wRect, boardRect);
                }
                if (blackKingSq.IsValid)
                {
                    var bRect = GetSquareRect(blackKingSq, boardRect);
                    ResourceManager.DrawDrawBadge(g, bRect, boardRect);
                }
            }
        }
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
        // Arrowhead size scaled with width
        float headLen = Math.Clamp(sqSize * 0.44f, 22f, 36f);
        float headHalfWidth = headLen * 0.70f;

        // Pull back tip slightly so it points cleanly into target square
        float endX = x2 - ux * (sqSize * 0.15f);
        float endY = y2 - uy * (sqSize * 0.15f);

        float baseHeadX = endX - ux * headLen;
        float baseHeadY = endY - uy * headLen;

        PointF[] headPoints =
        [
            new(endX, endY),
            new(baseHeadX + nx * headHalfWidth, baseHeadY + ny * headHalfWidth),
            new(baseHeadX - nx * headHalfWidth, baseHeadY - ny * headHalfWidth)
        ];

        // Arrows twice as thick (default 14f) and twice as transparent (alpha = 105 instead of 204)
        float arrowWidth = Math.Max(12f, arrow.Width);
        int alpha = 105;
        Color arrowColor = Color.FromArgb(alpha, arrow.Color.R, arrow.Color.G, arrow.Color.B);

        if (arrow.HasOutline)
        {
            using var outlinePen = new Pen(Color.FromArgb(45, 20, 20, 25), arrowWidth + 3f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Flat
            };
            g.DrawLine(outlinePen, x1, y1, baseHeadX, baseHeadY);

            using var outlineBrush = new SolidBrush(Color.FromArgb(45, 20, 20, 25));
            g.FillPolygon(outlineBrush, headPoints);
        }

        using var brush = new SolidBrush(arrowColor);
        using var pen = new Pen(arrowColor, arrowWidth)
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
        const int badgeSize = 40;
        int targetX = destSqRect.Right - (badgeSize / 2);
        int targetY = destSqRect.Top - (badgeSize / 2);

        if (!boardRect.IsEmpty)
        {
            if (targetX + badgeSize > boardRect.Right - 2) targetX = boardRect.Right - badgeSize - 2;
            if (targetY < boardRect.Top + 2) targetY = boardRect.Top + 2;
            if (targetX < boardRect.Left + 2) targetX = boardRect.Left + 2;
            if (targetY + badgeSize > boardRect.Bottom - 2) targetY = boardRect.Bottom - badgeSize - 2;
        }

        if (_pillActive && (_lastMoveBadge == MoveClassification.Brilliant || _lastMoveBadge == MoveClassification.Great))
        {
            long ms = _pillStopwatch.ElapsedMilliseconds;
            if (ms > 750)
            {
                _pillActive = false;
                _pillTimer.Stop();
                ResourceManager.DrawCornerBadge(g, _lastMoveBadge.Value, destSqRect, boardRect);
                return;
            }

            string pillText = _lastMoveBadge == MoveClassification.Brilliant ? "Блестящий ход!" : "Замечательный ход!";
            Color textColor = _lastMoveBadge == MoveClassification.Brilliant
                ? Color.FromArgb(22, 160, 133)
                : Color.FromArgb(74, 136, 199);

            float fontSize = 11.5f;
            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold);
            SizeF textSize = g.MeasureString(pillText, font);

            // Овал пропорциональный увеличенному шрифту с щедрым запасом (36px), чтобы текст никогда не обрезался
            float initWidth = (float)Math.Ceiling(textSize.Width + 36f);
            float initHeight = 32f;
            float initX = Math.Clamp(destSqRect.X + (destSqRect.Width - initWidth) / 2f, boardRect.Left + 6, boardRect.Right - initWidth - 6);
            float initY = Math.Max(boardRect.Top + 6, destSqRect.Y - initHeight * 0.75f);

            float curX, curY, curW, curH;
            float textAlpha = 1.0f;
            float badgeAlpha = 0.0f;

            if (ms <= 100)
            {
                // Плавное появление и всплытие (0..100ms) без сжатия ширины
                float t = ms / 100f;
                curW = initWidth;
                curH = initHeight;
                curX = initX;
                curY = initY + (1f - t) * 4f;
                textAlpha = t;
            }
            else if (ms <= 450)
            {
                // Удержание овала ровно 0.35 секунд (350ms, 100..450ms) в статичном читаемом виде
                curX = initX;
                curY = initY;
                curW = initWidth;
                curH = initHeight;
                textAlpha = 1.0f;
            }
            else
            {
                // Плавное складывание овала к углу клетки в круглый значок (450..750ms)
                float t = (ms - 450) / 300f;
                float smoothT = (float)(t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2);

                curX = initX + (targetX - initX) * smoothT;
                curY = initY + (targetY - initY) * smoothT;
                curW = initWidth + (badgeSize - initWidth) * smoothT;
                curH = initHeight + (badgeSize - initHeight) * smoothT;

                // Текст плавно и быстро исчезает в начале складывания (за первые 70ms),
                // чтобы он никогда не ужимался и не обрезался по краям
                textAlpha = Math.Clamp(1.0f - (ms - 450) / 70f, 0f, 1f);
                badgeAlpha = Math.Clamp((smoothT - 0.25f) * 1.5f, 0f, 1f);
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
            if (textAlpha > 0.05f && curW >= textSize.Width + 10f)
            {
                int textAlphaByte = (int)(textAlpha * 255);
                using var textBrush = new SolidBrush(Color.FromArgb(textAlphaByte, textColor));
                var sf = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center,
                    FormatFlags = StringFormatFlags.NoWrap
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
                    ResourceManager.DrawCornerBadge(g, _lastMoveBadge.Value, destSqRect, boardRect);
                }
            }
        }
        else
        {
            // Static Corner Badge
            ResourceManager.DrawCornerBadge(g, _lastMoveBadge.Value, destSqRect, boardRect);
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
