using System.Drawing;
using System.Drawing.Drawing2D;
using ChessAnalysis.Core.Analysis;
using ChessAnalysis.Core.Chess;
using ChessAnalysis.Core.Config;
using ChessAnalysis.Core.Engine;
using ChessAnalysis.UI.Controls;
using ChessAnalysis.UI.Forms;
using ChessAnalysis.UI.Resources;

namespace ChessAnalysis;

public partial class Form1 : Form
{
    private readonly string? _initialEnginePath;
    private AppSettings _settings;
    private StockfishEngine? _engine;
    private AnalysisService? _analysisService;
    private CancellationTokenSource? _analysisCts;

    private PgnGame _currentGame = new();
    private GameAnalysis? _currentAnalysis;
    private int _currentPlyIndex = -1;
    private PieceColor _userColor = PieceColor.White;
    private string _rawPgnText = string.Empty;
    private int _analysisDepth = 12;
    private long _analysisNodes = 400_000;

    // --- UI Controls ---
    // Left/Center Viewport (Board + Players + EvalBar)
    private ChessBoardControl _boardControl = null!;
    private EvalBarControl _evalBar = null!;

    // Player Cards (Above and Below Board)
    private Panel _topPlayerBar = null!;
    private Panel _topAvatar = null!;
    private Label _lblTopPlayer = null!;
    private CapturedPiecesControl _topCapturedCtrl = null!;
    private Label _lblTopClock = null!;

    private Panel _bottomPlayerBar = null!;
    private Panel _bottomAvatar = null!;
    private Label _lblBottomPlayer = null!;
    private CapturedPiecesControl _bottomCapturedCtrl = null!;
    private Label _lblBottomClock = null!;

    // Top Header Bar
    private Label _lblLogoTitle = null!;
    private RoundedButton _btnLoadPgn = null!;
    private RoundedButton _btnDownloadUser = null!;
    private ProgressBar _progressBar = null!;

    // Evaluation Graph, Move Review Card, Real-time Computer Lines, Opening Bar
    private GameGraphControl _graphControl = null!;
    private MoveReviewCardControl _moveReviewCard = null!;
    private ComputerLinesControl _computerLines = null!;
    private Panel _pnlOpeningBar = null!;
    private Label _lblOpeningName = null!;

    private bool _isAnalyzingGame = false;
    private EngineEvaluation? _lastPositionEval;

    // Right Sidebar
    private Panel _sidebarContainer = null!;
    private Panel _sidebarHeader = null!;
    private Label _lblSidebarTitle = null!;
    private RoundedButton _btnBackToOverview = null!;
    private RoundedButton _btnShowOverview = null!;
    private ContextMenuStrip _menuSettings = null!;

    // Arrow and Engine Options
    private bool _showBestMoveArrow = true;
    private bool _showThreatArrow = true;

    // View A: Overview Panel (Report)
    private Panel _pnlOverview = null!;
    private Label _lblOverviewWhitePlayer = null!;
    private Label _lblOverviewBlackPlayer = null!;
    private Label _lblWhiteAccuracy = null!;
    private Label _lblBlackAccuracy = null!;
    private Label _lblWhiteElo = null!;
    private Label _lblBlackElo = null!;
    private TableLayoutPanel _tableBadges = null!;
    private TableLayoutPanel _tablePhases = null!;
    private RoundedButton _btnStartReview = null!;

    // Variation Box Control
    private Panel _pnlVariationBox = null!;
    private Label _lblVariationTitle = null!;
    private FlowLayoutPanel _flowVariationMoves = null!;

    // Phase Badges Controls
    private PictureBox _picWhiteOpening = null!;
    private Label _lblWhiteOpeningDash = null!;
    private PictureBox _picBlackOpening = null!;
    private Label _lblBlackOpeningDash = null!;

    private PictureBox _picWhiteMiddlegame = null!;
    private Label _lblWhiteMiddlegameDash = null!;
    private PictureBox _picBlackMiddlegame = null!;
    private Label _lblBlackMiddlegameDash = null!;

    private PictureBox _picWhiteEndgame = null!;
    private Label _lblWhiteEndgameDash = null!;
    private PictureBox _picBlackEndgame = null!;
    private Label _lblBlackEndgameDash = null!;

    private readonly ToolTip _toolTip = new() { InitialDelay = 250, ReshowDelay = 100 };

    // View B: Move-by-Move Review Panel
    private Panel _pnlMoveReview = null!;
    private MoveHistoryControl _moveHistory = null!;

    // Playback Bar in Review
    private RoundedButton _btnFirst = null!;
    private RoundedButton _btnPrev = null!;
    private RoundedButton _btnAutoPlay = null!;
    private RoundedButton _btnNext = null!;
    private RoundedButton _btnLast = null!;
    private System.Windows.Forms.Timer _autoPlayTimer = null!;

    // Reactive Variation Engine Analysis
    private CancellationTokenSource? _variationCts;
    private readonly List<VariationStep> _activeVariation = new();
    private int _activeVariationIndex = -1;
    private int _variationBranchPly = -1;
    private string? _lastBookOpeningName;
    private EngineEvaluation? _currentFenEval;
    private string _currentEvalFen = string.Empty;

    private class VariationStep
    {
        public Move Move { get; set; }
        public string San { get; set; } = string.Empty;
        public string FenBefore { get; set; } = string.Empty;
        public string FenAfter { get; set; } = string.Empty;
        public MoveClassification? Classification { get; set; }
        public EngineEvaluation? PreEval { get; set; }
        public EngineEvaluation? PostEval { get; set; }
    }

    public Form1(string? initialEnginePath = null)
    {
        _initialEnginePath = initialEnginePath;
        _settings = AppSettings.Load();
        InitializeComponent();
        SetupCustomUi();
        SoundService.Initialize();
        OpeningBook.Initialize();
        InitializeEngine();

        // Start with clean empty board position
        _boardControl.Board = new BoardState();
        _evalBar.Reset();
        UpdatePlayerBarLabels("Белые", "Черные");
        _lblTopClock.Text = "";
        _lblBottomClock.Text = "";
        SetOpeningName("Начальная позиция");
        SwitchSidebarView("review");

        // Kick off Stockfish evaluation for the starting board position
        TriggerStartupAnalysis();
    }

    private void SetupCustomUi()
    {
        Text = "Chess Analysis (Stockfish 19) — by Antigravity";
        Icon = ResourceManager.GetAppIcon();
        Size = new Size(1320, 880);
        MinimumSize = new Size(1000, 680);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = ChessTheme.BackgroundDark;
        ForeColor = ChessTheme.TextPrimary;
        Font = new Font("Segoe UI", 9.5f);
        KeyPreview = true;
        KeyDown += OnFormKeyDown;

        // AutoPlay Timer
        _autoPlayTimer = new System.Windows.Forms.Timer { Interval = 1100 };
        _autoPlayTimer.Tick += (s, e) =>
        {
            if (_currentGame.Moves.Count > 0 && _currentPlyIndex < _currentGame.Moves.Count - 1)
            {
                GoToPly(_currentPlyIndex + 1);
            }
            else
            {
                StopAutoPlay();
            }
        };

        // 1. Top Bar Panel (Header with branding and action buttons)
        var topBar = CreateTopBar();

        // 2. Main Area (Left = Board Viewport, Right = Sidebar)
        var mainArea = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ChessTheme.BackgroundDark
        };

        // Right Sidebar (Width 515px)
        _sidebarContainer = CreateRightSidebar();
        _sidebarContainer.Dock = DockStyle.Right;

        // Left Board Viewport (Fills remaining space)
        var boardViewport = CreateBoardViewport();
        boardViewport.Dock = DockStyle.Fill;

        mainArea.Controls.Add(boardViewport);
        mainArea.Controls.Add(_sidebarContainer);

        // WinForms Dock Order: mainArea (Fill) -> topBar (Top)
        Controls.Add(mainArea);
        Controls.Add(topBar);
    }

    #region Top Header Bar (Phase 2)
    private Panel CreateTopBar()
    {
        var topBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 48,
            BackColor = ChessTheme.PanelDarker,
            Padding = new Padding(16, 6, 16, 6)
        };

        topBar.Paint += (s, e) =>
        {
            using var borderPen = new Pen(ChessTheme.BorderColor, 1f);
            e.Graphics.DrawLine(borderPen, 0, topBar.Height - 1, topBar.Width, topBar.Height - 1);
        };

        // Logo & Title Container on Left
        var flowLogo = new FlowLayoutPanel
        {
            Dock = DockStyle.Left,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };

        _lblLogoTitle = new Label
        {
            Text = "♔ ChessAnalysis",
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 3, 0, 0)
        };
        _lblLogoTitle.Click += (s, e) => ResetToStartupScreen();

        var lblBrandPill = new Label
        {
            Text = "by Antigravity",
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = ChessTheme.AccentGreen,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Cursor = Cursors.Hand,
            Margin = new Padding(6, 6, 0, 0)
        };
        lblBrandPill.Click += (s, e) => ResetToStartupScreen();

        var lblSep = new Label
        {
            Text = "•",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Color.FromArgb(120, 118, 115),
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(8, 4, 8, 0)
        };

        flowLogo.Controls.Add(_lblLogoTitle);
        flowLogo.Controls.Add(lblBrandPill);

        // Action Buttons on Right
        var flowButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0)
        };

        _btnLoadPgn = new RoundedButton
        {
            Text = "Загрузить PGN",
            Width = 145,
            Height = 34,
            BorderRadius = 6,
            NormalColor = ChessTheme.AccentGreen,
            HoverColor = ChessTheme.AccentGreenHover,
            PressedColor = Color.FromArgb(100, 150, 55),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.White,
            Margin = new Padding(4, 0, 4, 0)
        };
        _btnLoadPgn.Click += (s, e) => OpenLoadPgnDialog();

        _btnDownloadUser = new RoundedButton
        {
            Text = "Поиск по нику",
            Width = 145,
            Height = 34,
            BorderRadius = 6,
            NormalColor = Color.FromArgb(48, 46, 43),
            HoverColor = Color.FromArgb(62, 60, 56),
            PressedColor = Color.FromArgb(75, 73, 68),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.White,
            Margin = new Padding(4, 0, 0, 0)
        };
        _btnDownloadUser.Click += (s, e) => OpenDownloadUserDialog();

        flowButtons.Controls.Add(_btnLoadPgn);
        flowButtons.Controls.Add(_btnDownloadUser);

        // Sleek 3px analysis progress indicator flush to the bottom edge
        _progressBar = new ProgressBar
        {
            Dock = DockStyle.Bottom,
            Height = 3,
            Style = ProgressBarStyle.Continuous,
            Visible = false
        };

        topBar.Controls.Add(flowButtons);
        topBar.Controls.Add(flowLogo);
        topBar.Controls.Add(_progressBar);

        return topBar;
    }
    #endregion

    #region Board Viewport (Phase 8 EvalBar: 36px)
    private Control CreateBoardViewport()
    {
        var viewport = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ChessTheme.BackgroundDark
        };

        // 1. Top Player Bar
        _topPlayerBar = CreatePlayerBar(isTop: true);

        // 2. Bottom Player Bar
        _bottomPlayerBar = CreatePlayerBar(isTop: false);

        // 3. Eval Bar flush to board (36px width per Phase 8)
        _evalBar = new EvalBarControl
        {
            Width = 36
        };

        // 4. Square Chess Board
        _boardControl = new ChessBoardControl();
        _boardControl.MovePlayed += OnBoardMovePlayed;

        viewport.Controls.Add(_boardControl);
        viewport.Controls.Add(_evalBar);
        viewport.Controls.Add(_topPlayerBar);
        viewport.Controls.Add(_bottomPlayerBar);

        viewport.Resize += (s, e) => LayoutBoardViewport(viewport);
        viewport.Layout += (s, e) => LayoutBoardViewport(viewport);

        return viewport;
    }

    private void LayoutBoardViewport(Control viewport)
    {
        int padX = 14;
        int padY = 8;
        int availW = viewport.ClientSize.Width - padX * 2;
        int availH = viewport.ClientSize.Height - padY * 2;
        if (availW <= 100 || availH <= 100) return;

        int playerBarH = 38;
        int evalBarW = 36;
        int gap = 8;
        int verticalGap = 4;

        // Board is strictly 1:1 square
        int maxBoardH = availH - (playerBarH * 2) - (verticalGap * 2);
        int maxBoardW = availW - evalBarW - gap;
        int boardSize = Math.Max(120, Math.Min(maxBoardH, maxBoardW));

        int totalW = evalBarW + gap + boardSize;
        int totalH = playerBarH + verticalGap + boardSize + verticalGap + playerBarH;

        // Shift board to the left by 1 board square per specification
        int squareSize = boardSize / 8;
        int flushRightX = viewport.ClientSize.Width - totalW - 14;
        int startX = Math.Max(padX, flushRightX - squareSize);
        int startY = padY + Math.Max(0, (availH - totalH) / 2);

        int boardX = startX + evalBarW + gap;
        int boardY = startY + playerBarH + verticalGap;

        _topPlayerBar.SetBounds(startX, startY, totalW, playerBarH);
        _evalBar.SetBounds(startX, boardY, evalBarW, boardSize);
        _boardControl.SetBounds(boardX, boardY, boardSize, boardSize);
        _bottomPlayerBar.SetBounds(startX, boardY + boardSize + verticalGap, totalW, playerBarH);
    }

    private Panel CreatePlayerBar(bool isTop)
    {
        var bar = new Panel
        {
            Height = 38,
            BackColor = ChessTheme.BackgroundDark
        };

        var avatar = new Panel
        {
            Size = new Size(30, 30),
            BackColor = Color.Transparent
        };
        avatar.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var bg = new SolidBrush(Color.FromArgb(45, 43, 40));
            using var pen = new Pen(Color.FromArgb(75, 73, 70), 1.2f);
            e.Graphics.FillEllipse(bg, 1, 1, 28, 28);
            e.Graphics.DrawEllipse(pen, 1, 1, 28, 28);

            bool isWhite = (!isTop && !_boardControl.IsFlipped) || (isTop && _boardControl.IsFlipped);
            var pieceImg = ResourceManager.GetPieceImage(isWhite ? PieceColor.White : PieceColor.Black, PieceType.Pawn);
            if (pieceImg != null)
            {
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                e.Graphics.DrawImage(pieceImg, new Rectangle(5, 5, 20, 20));
            }
        };

        var lblPlayer = new Label
        {
            Text = isTop ? "Черные" : "Белые",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = false,
            AutoEllipsis = true
        };

        var capturedCtrl = new CapturedPiecesControl
        {
            Height = 24
        };

        var clockPill = new Panel
        {
            Size = new Size(88, 32),
            BackColor = ChessTheme.PanelDarker
        };
        clockPill.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = CreateRoundedPath(new Rectangle(0, 0, clockPill.Width - 1, clockPill.Height - 1), 6);
            using var bg = new SolidBrush(Color.FromArgb(32, 30, 28));
            using var pen = new Pen(Color.FromArgb(105, 102, 96), 1.8f);
            e.Graphics.FillPath(bg, path);
            e.Graphics.DrawPath(pen, path);
        };

        var lblClock = new Label
        {
            Text = "",
            Font = new Font("Consolas", 11.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(245, 245, 245),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent
        };
        clockPill.Controls.Add(lblClock);

        bar.Controls.Add(clockPill);
        bar.Controls.Add(capturedCtrl);
        bar.Controls.Add(lblPlayer);
        bar.Controls.Add(avatar);

        void LayoutBar()
        {
            avatar.Location = new Point(0, (bar.Height - avatar.Height) / 2);
            int clockW = 88;
            int clockX = Math.Max(0, bar.Width - clockW);
            clockPill.Location = new Point(clockX, (bar.Height - clockPill.Height) / 2);

            using var g = bar.CreateGraphics();
            var textSz = TextRenderer.MeasureText(g, lblPlayer.Text, lblPlayer.Font);
            int nameW = Math.Min(180, Math.Max(40, textSz.Width + 4));
            lblPlayer.SetBounds(36, (bar.Height - 22) / 2, nameW, 22);

            int captX = 36 + nameW + 8;
            int captW = Math.Max(10, clockX - captX - 8);
            capturedCtrl.SetBounds(captX, (bar.Height - capturedCtrl.Height) / 2, captW, 24);
        }

        bar.Resize += (s, e) => LayoutBar();
        lblPlayer.TextChanged += (s, e) => LayoutBar();

        if (isTop)
        {
            _topAvatar = avatar;
            _lblTopPlayer = lblPlayer;
            _topCapturedCtrl = capturedCtrl;
            _lblTopClock = lblClock;
        }
        else
        {
            _bottomAvatar = avatar;
            _lblBottomPlayer = lblPlayer;
            _bottomCapturedCtrl = capturedCtrl;
            _lblBottomClock = lblClock;
        }

        return bar;
    }
    #endregion

    #region Right Sidebar (Overview & Move Review)
    private Panel CreateRightSidebar()
    {
        var sidebar = new Panel
        {
            Width = 590,
            BackColor = ChessTheme.PanelDark
        };

        sidebar.Paint += (s, e) =>
        {
            using var borderPen = new Pen(ChessTheme.BorderColor, 1.2f);
            e.Graphics.DrawLine(borderPen, 0, 0, 0, sidebar.Height);
        };

        // Settings Gear Context Menu
        _menuSettings = CreateSettingsMenu();

        // 1. Sidebar Header Bar
        _sidebarHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 44,
            BackColor = ChessTheme.PanelDarker,
            Padding = new Padding(10, 6, 10, 6)
        };

        _btnBackToOverview = new RoundedButton
        {
            Text = "← К ходам",
            Width = 105,
            Height = 30,
            BorderRadius = 5,
            NormalColor = Color.FromArgb(48, 46, 43),
            HoverColor = Color.FromArgb(62, 60, 56),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Dock = DockStyle.Left,
            Visible = false
        };
        _btnBackToOverview.Click += (s, e) => SwitchSidebarView("review");

        _btnShowOverview = new RoundedButton
        {
            Text = "⭐ Отчет",
            Width = 96,
            Height = 30,
            BorderRadius = 5,
            NormalColor = Color.FromArgb(48, 46, 43),
            HoverColor = Color.FromArgb(62, 60, 56),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Dock = DockStyle.Left,
            Margin = new Padding(6, 0, 0, 0)
        };
        _btnShowOverview.Click += async (s, e) =>
        {
            if (_isAnalyzingGame)
            {
                SwitchSidebarView("overview");
                return;
            }

            if (_currentAnalysis == null && _currentGame.Moves.Count > 0)
            {
                if (_activeVariation.Count > 0)
                {
                    _activeVariation.Clear();
                    _activeVariationIndex = -1;
                    _variationBranchPly = -1;
                    _moveHistory.ClearVariation();
                    UpdateVariationBoxUi();
                }

                _rawPgnText = _currentGame.ToPgn();
                SwitchSidebarView("overview");
                await StartAnalysisAsync();
                return;
            }

            SwitchSidebarView("overview");
        };

        _lblSidebarTitle = new Label
        {
            Text = "Таблица ходов",
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            ForeColor = Color.White,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };

        _sidebarHeader.Controls.Add(_lblSidebarTitle);
        _sidebarHeader.Controls.Add(_btnShowOverview);
        _sidebarHeader.Controls.Add(_btnBackToOverview);

        // 2. Content Views Container
        var pnlContent = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ChessTheme.PanelDark
        };

        _pnlOverview = CreateOverviewPanel();
        _pnlMoveReview = CreateMoveReviewPanel();

        pnlContent.Controls.Add(_pnlOverview);
        pnlContent.Controls.Add(_pnlMoveReview);

        sidebar.Controls.Add(pnlContent);
        sidebar.Controls.Add(_sidebarHeader);

        return sidebar;
    }

    private void SwitchSidebarView(string viewName)
    {
        _pnlOverview.Visible = (viewName == "overview");
        _pnlMoveReview.Visible = (viewName == "review");

        if (viewName == "overview")
        {
            _lblSidebarTitle.Text = "⭐ Отчет о партии";
            _btnBackToOverview.Visible = true;
            _btnShowOverview.Visible = false;
            _pnlOverview.BringToFront();
        }
        else
        {
            _lblSidebarTitle.Text = "Таблица ходов";
            _btnBackToOverview.Visible = false;
            _btnShowOverview.Visible = true;
            _pnlMoveReview.BringToFront();
        }
    }

    private ContextMenuStrip CreateSettingsMenu()
    {
        var menu = new ContextMenuStrip
        {
            BackColor = ChessTheme.PanelDarker,
            ForeColor = ChessTheme.TextPrimary,
            ShowImageMargin = false,
            Font = new Font("Segoe UI", 9.5f)
        };

        var mAnalyze = new ToolStripMenuItem("▶️  Запустить полный анализ партии");
        mAnalyze.Click += async (s, e) => await StartAnalysisAsync();

        var mCopyPgn = new ToolStripMenuItem("📋  Скопировать PGN");
        mCopyPgn.Click += (s, e) => CopyPgnToClipboard();

        var mFlip = new ToolStripMenuItem("🔄  Перевернуть доску");
        mFlip.Click += (s, e) => ToggleBoardFlip();

        // MultiPV (1..5 computer lines)
        var mMultiPv = new ToolStripMenuItem("📊  Линии анализа Stockfish");
        var pvItems = new List<ToolStripMenuItem>();
        for (int i = 1; i <= 5; i++)
        {
            int lines = i;
            var mi = new ToolStripMenuItem(i == 2 ? $"{i} линии (по умолчанию)" : (i == 1 ? "1 линия" : $"{i} линий"))
            {
                Checked = (i == _settings.MultiPv)
            };
            mi.Click += (s, e) =>
            {
                _settings.MultiPv = lines;
                _settings.Save();
                _computerLines.LineCount = lines;
                _ = _engine?.SetMultiPvAsync(lines);
                foreach (var it in pvItems) it.Checked = false;
                mi.Checked = true;
                if (_currentPlyIndex >= 0 && _currentGame.Moves.Count > 0 && _engine != null)
                {
                    var curFen = _currentPlyIndex < _currentGame.Moves.Count
                        ? _currentGame.Moves[_currentPlyIndex].FenAfter
                        : new BoardState().ToFen();
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var eval = await _engine.AnalyzePositionAsync(curFen, depth: _analysisDepth, nodes: 400_000);
                            Invoke(() =>
                            {
                                var b = new BoardState();
                                b.LoadFen(curFen);
                                _computerLines.SetLines(eval, b);
                            });
                        }
                        catch { }
                    });
                }
            };
            pvItems.Add(mi);
            mMultiPv.DropDownItems.Add(mi);
        }

        // Threads (1..16 threads)
        var mThreads = new ToolStripMenuItem("⚡  Потоки процессора (Threads)");
        var thItems = new List<ToolStripMenuItem>();
        int[] threadChoices = [1, 2, 4, 6, 8, 12, 16];
        foreach (int th in threadChoices)
        {
            int tVal = th;
            var mi = new ToolStripMenuItem($"{tVal} потока/потоков")
            {
                Checked = (tVal == _settings.Threads)
            };
            mi.Click += (s, e) =>
            {
                _settings.Threads = tVal;
                _settings.Save();
                _ = _engine?.SetThreadsAsync(tVal);
                foreach (var it in thItems) it.Checked = false;
                mi.Checked = true;
            };
            thItems.Add(mi);
            mThreads.DropDownItems.Add(mi);
        }

        // Arrow options
        var mArrowsMenu = new ToolStripMenuItem("↗️  Стрелки на доске");
        var mArrowBest = new ToolStripMenuItem("🔵  Сильнейший ход (синяя стрелка)") { CheckOnClick = true, Checked = _showBestMoveArrow };
        mArrowBest.CheckedChanged += (s, e) =>
        {
            _showBestMoveArrow = mArrowBest.Checked;
            RestoreCurrentPlyArrows();
        };

        var mArrowThreat = new ToolStripMenuItem("🔴  Угроза соперника при ошибке (красная стрелка)") { CheckOnClick = true, Checked = _showThreatArrow };
        mArrowThreat.CheckedChanged += (s, e) =>
        {
            _showThreatArrow = mArrowThreat.Checked;
            RestoreCurrentPlyArrows();
        };

        mArrowsMenu.DropDownItems.Add(mArrowBest);
        mArrowsMenu.DropDownItems.Add(mArrowThreat);

        var mSound = new ToolStripMenuItem("🔊  Звук ходов") { CheckOnClick = true, Checked = true };
        mSound.CheckedChanged += (s, e) =>
        {
            SoundService.ToggleMute();
            mSound.Checked = !SoundService.Muted;
        };

        var mSpeed = new ToolStripMenuItem("⚡  Сила и скорость Stockfish");
        var mFast = new ToolStripMenuItem("⚡ Быстрый (~1 сек, 3270 elo) — По умолчанию") { Checked = true };
        var mStandard = new ToolStripMenuItem("🧠 Стандартный (~5 сек, 3430 elo)");
        var mDeep = new ToolStripMenuItem("🔬 Глубокий (~20 сек, 3500+ elo)");

        mFast.Click += (s, e) =>
        {
            _analysisNodes = 1_000_000;
            _analysisDepth = 18;
            mFast.Checked = true;
            mStandard.Checked = false;
            mDeep.Checked = false;
        };
        mStandard.Click += (s, e) =>
        {
            _analysisNodes = 5_000_000;
            _analysisDepth = 22;
            mFast.Checked = false;
            mStandard.Checked = true;
            mDeep.Checked = false;
        };
        mDeep.Click += (s, e) =>
        {
            _analysisNodes = 15_000_000;
            _analysisDepth = 26;
            mFast.Checked = false;
            mStandard.Checked = false;
            mDeep.Checked = true;
        };

        mSpeed.DropDownItems.Add(mFast);
        mSpeed.DropDownItems.Add(mStandard);
        mSpeed.DropDownItems.Add(mDeep);

        var mBrowse = new ToolStripMenuItem("⚙  Указать файл stockfish.exe...");
        mBrowse.Click += (s, e) => BrowseEngineFile();

        menu.Items.Add(mAnalyze);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(mCopyPgn);
        menu.Items.Add(mFlip);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(mMultiPv);
        menu.Items.Add(mThreads);
        menu.Items.Add(mArrowsMenu);
        menu.Items.Add(mSound);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(mSpeed);
        menu.Items.Add(mBrowse);

        return menu;
    }
    #endregion

    #region View 1: Overview Panel (Report)
    private Panel CreateOverviewPanel()
    {
        var pnl = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ChessTheme.PanelDark
        };

        var scrollBody = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            AutoScrollMinSize = new Size(0, 640),
            Padding = new Padding(14, 4, 14, 12),
            BackColor = ChessTheme.PanelDark
        };

        // 1. Players & Accuracy Section (4-column TableLayoutPanel aligned with Badges & Phases)
        var pnlPlayers = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 72,
            ColumnCount = 4,
            RowCount = 2,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 2, 0, 2)
        };
        pnlPlayers.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));
        pnlPlayers.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19f));
        pnlPlayers.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22f));
        pnlPlayers.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19f));

        pnlPlayers.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
        pnlPlayers.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));

        var lblRowPlayers = new Label
        {
            Text = "Шахматисты",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = ChessTheme.TextSecondary,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 0, 0)
        };

        _lblOverviewWhitePlayer = new Label
        {
            Text = "Белые",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Color.White,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };

        var spacerPlayers = new Label { Dock = DockStyle.Fill };

        _lblOverviewBlackPlayer = new Label
        {
            Text = "Черные",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Color.White,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter
        };

        pnlPlayers.Controls.Add(lblRowPlayers, 0, 0);
        pnlPlayers.Controls.Add(_lblOverviewWhitePlayer, 1, 0);
        pnlPlayers.Controls.Add(spacerPlayers, 2, 0);
        pnlPlayers.Controls.Add(_lblOverviewBlackPlayer, 3, 0);

        var lblAccuracyTitle = new Label
        {
            Text = "Точность",
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            ForeColor = Color.White,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 0, 0)
        };

        var whiteAccCard = CreatePlayerScoreCard(isWhitePlayer: true, out _lblWhiteAccuracy);
        var spacerAcc = new Label { Dock = DockStyle.Fill };
        var blackAccCard = CreatePlayerScoreCard(isWhitePlayer: false, out _lblBlackAccuracy);

        pnlPlayers.Controls.Add(lblAccuracyTitle, 0, 1);
        pnlPlayers.Controls.Add(whiteAccCard, 1, 1);
        pnlPlayers.Controls.Add(spacerAcc, 2, 1);
        pnlPlayers.Controls.Add(blackAccCard, 3, 1);

        var div1 = CreateDividerLine();
        div1.Dock = DockStyle.Top;

        // 2. Badge Comparison Table (10 categories, height 380px, 38f per row with comfortable spacing)
        _tableBadges = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 380,
            ColumnCount = 4,
            RowCount = 10,
            BackColor = ChessTheme.PanelDark,
            Padding = new Padding(0, 2, 0, 2)
        };
        _tableBadges.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));
        _tableBadges.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19f));
        _tableBadges.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22f));
        _tableBadges.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19f));

        BuildBadgeTableRows();

        // 3. Rating Section (aligned with badges)
        var divElo = CreateDividerLine();
        divElo.Dock = DockStyle.Top;
        var pnlElo = CreateRatingPanel(out _lblWhiteElo, out _lblBlackElo);

        // 4. Phases Table (Always visible, aligned with columns)
        var divPhases = CreateDividerLine();
        divPhases.Dock = DockStyle.Top;
        _tablePhases = CreatePhasesTable();

        // 5. Bottom Green Button ("Смотреть ходы")
        var pnlBottomBtn = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 52,
            BackColor = ChessTheme.PanelDark,
            Padding = new Padding(14, 4, 14, 6)
        };

        _btnStartReview = new RoundedButton
        {
            Text = "Смотреть ходы партии",
            Dock = DockStyle.Fill,
            BorderRadius = 8,
            NormalColor = ChessTheme.AccentGreen,
            HoverColor = ChessTheme.AccentGreenHover,
            PressedColor = Color.FromArgb(100, 150, 55),
            Font = new Font("Segoe UI", 11f, FontStyle.Bold),
            ForeColor = Color.White
        };
        _btnStartReview.Click += (s, e) =>
        {
            SwitchSidebarView("review");
            GoToPly(_currentPlyIndex >= 0 ? _currentPlyIndex : 0, forceRefresh: true);
        };
        pnlBottomBtn.Controls.Add(_btnStartReview);

        // WinForms Top Dock order: last added is on top
        scrollBody.Controls.Add(_tablePhases);
        scrollBody.Controls.Add(divPhases);
        scrollBody.Controls.Add(pnlElo);
        scrollBody.Controls.Add(divElo);
        scrollBody.Controls.Add(_tableBadges);
        scrollBody.Controls.Add(div1);
        scrollBody.Controls.Add(pnlPlayers);

        pnl.Controls.Add(scrollBody);
        pnl.Controls.Add(pnlBottomBtn);
        pnlBottomBtn.BringToFront();

        return pnl;
    }

    private TableLayoutPanel CreateRatingPanel(out Label lblWhiteElo, out Label lblBlackElo)
    {
        var pnl = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 48,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 2, 0, 2)
        };
        pnl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));
        pnl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19f));
        pnl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22f));
        pnl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19f));
        pnl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var lblEloTitle = new Label
        {
            Text = "Рейтинг",
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            ForeColor = Color.White,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 0, 0)
        };

        var whiteEloCard = CreatePlayerScoreCard(isWhitePlayer: true, out lblWhiteElo);
        var spacer = new Label { Dock = DockStyle.Fill };
        var blackEloCard = CreatePlayerScoreCard(isWhitePlayer: false, out lblBlackElo);

        pnl.Controls.Add(lblEloTitle, 0, 0);
        pnl.Controls.Add(whiteEloCard, 1, 0);
        pnl.Controls.Add(spacer, 2, 0);
        pnl.Controls.Add(blackEloCard, 3, 0);

        return pnl;
    }

    private TableLayoutPanel CreatePhasesTable()
    {
        _tablePhases = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 122,
            ColumnCount = 4,
            RowCount = 3,
            BackColor = ChessTheme.PanelDark,
            Padding = new Padding(0, 2, 0, 2)
        };

        _tablePhases.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));
        _tablePhases.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19f));
        _tablePhases.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22f));
        _tablePhases.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19f));

        for (int r = 0; r < 3; r++)
        {
            _tablePhases.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
        }

        // Row 0: Дебют
        var lblOpening = CreatePhaseTitleLabel("Дебют");
        var cellWOpen = CreatePhaseCell(out _picWhiteOpening, out _lblWhiteOpeningDash);
        var spacerOpen = new Label { Dock = DockStyle.Fill };
        var cellBOpen = CreatePhaseCell(out _picBlackOpening, out _lblBlackOpeningDash);
        _tablePhases.Controls.Add(lblOpening, 0, 0);
        _tablePhases.Controls.Add(cellWOpen, 1, 0);
        _tablePhases.Controls.Add(spacerOpen, 2, 0);
        _tablePhases.Controls.Add(cellBOpen, 3, 0);

        // Row 1: Миттельшпиль
        var lblMiddle = CreatePhaseTitleLabel("Миттельшпиль");
        var cellWMiddle = CreatePhaseCell(out _picWhiteMiddlegame, out _lblWhiteMiddlegameDash);
        var spacerMiddle = new Label { Dock = DockStyle.Fill };
        var cellBMiddle = CreatePhaseCell(out _picBlackMiddlegame, out _lblBlackMiddlegameDash);
        _tablePhases.Controls.Add(lblMiddle, 0, 1);
        _tablePhases.Controls.Add(cellWMiddle, 1, 1);
        _tablePhases.Controls.Add(spacerMiddle, 2, 1);
        _tablePhases.Controls.Add(cellBMiddle, 3, 1);

        // Row 2: Эндшпиль
        var lblEnd = CreatePhaseTitleLabel("Эндшпиль");
        var cellWEnd = CreatePhaseCell(out _picWhiteEndgame, out _lblWhiteEndgameDash);
        var spacerEnd = new Label { Dock = DockStyle.Fill };
        var cellBEnd = CreatePhaseCell(out _picBlackEndgame, out _lblBlackEndgameDash);
        _tablePhases.Controls.Add(lblEnd, 0, 2);
        _tablePhases.Controls.Add(cellWEnd, 1, 2);
        _tablePhases.Controls.Add(spacerEnd, 2, 2);
        _tablePhases.Controls.Add(cellBEnd, 3, 2);

        return _tablePhases;
    }

    private static Label CreatePhaseTitleLabel(string text)
    {
        return new Label
        {
            Text = text,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(220, 218, 215),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 0, 0)
        };
    }

    private static Panel CreatePhaseCell(out PictureBox pic, out Label dash)
    {
        var pnl = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent
        };

        pic = new PictureBox
        {
            Size = new Size(30, 30),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
            Visible = false
        };

        dash = new Label
        {
            Text = "—",
            ForeColor = Color.FromArgb(120, 118, 115),
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent,
            Visible = true
        };

        pnl.Controls.Add(pic);
        pnl.Controls.Add(dash);

        var p = pic;
        void CenterPic()
        {
            p.Location = new Point(Math.Max(0, (pnl.Width - p.Width) / 2), Math.Max(0, (pnl.Height - p.Height) / 2));
        }
        CenterPic();
        pnl.Resize += (s, e) => CenterPic();

        return pnl;
    }

    private void UpdatePhasesData(GameAnalysis analysis)
    {
        SetPhaseBadge(_picWhiteOpening, _lblWhiteOpeningDash, analysis.WhitePhases.OpeningPerformance, "Дебют (Белые)", analysis.WhitePhases.OpeningAccuracy);
        SetPhaseBadge(_picBlackOpening, _lblBlackOpeningDash, analysis.BlackPhases.OpeningPerformance, "Дебют (Черные)", analysis.BlackPhases.OpeningAccuracy);

        SetPhaseBadge(_picWhiteMiddlegame, _lblWhiteMiddlegameDash, analysis.WhitePhases.MiddlegamePerformance, "Миттельшпиль (Белые)", analysis.WhitePhases.MiddlegameAccuracy);
        SetPhaseBadge(_picBlackMiddlegame, _lblBlackMiddlegameDash, analysis.BlackPhases.MiddlegamePerformance, "Миттельшпиль (Черные)", analysis.BlackPhases.MiddlegameAccuracy);

        SetPhaseBadge(_picWhiteEndgame, _lblWhiteEndgameDash, analysis.WhitePhases.EndgamePerformance, "Эндшпиль (Белые)", analysis.WhitePhases.EndgameAccuracy);
        SetPhaseBadge(_picBlackEndgame, _lblBlackEndgameDash, analysis.BlackPhases.EndgamePerformance, "Эндшпиль (Черные)", analysis.BlackPhases.EndgameAccuracy);
    }

    private void ResetPhasesData()
    {
        SetPhaseBadge(_picWhiteOpening, _lblWhiteOpeningDash, null, "", null);
        SetPhaseBadge(_picBlackOpening, _lblBlackOpeningDash, null, "", null);
        SetPhaseBadge(_picWhiteMiddlegame, _lblWhiteMiddlegameDash, null, "", null);
        SetPhaseBadge(_picBlackMiddlegame, _lblBlackMiddlegameDash, null, "", null);
        SetPhaseBadge(_picWhiteEndgame, _lblWhiteEndgameDash, null, "", null);
        SetPhaseBadge(_picBlackEndgame, _lblBlackEndgameDash, null, "", null);
    }

    private void SetPhaseBadge(PictureBox pic, Label dash, MoveClassification? classification, string phaseName, double? accuracy)
    {
        if (classification.HasValue)
        {
            pic.Image = ResourceManager.GetBadgeImage(classification.Value);
            pic.Visible = true;
            dash.Visible = false;
            if (pic.Parent != null)
            {
                pic.Location = new Point(Math.Max(0, (pic.Parent.Width - pic.Width) / 2), Math.Max(0, (pic.Parent.Height - pic.Height) / 2));
            }
            string accStr = accuracy.HasValue ? $" ({accuracy.Value:0.#}%)" : "";
            _toolTip.SetToolTip(pic, $"{phaseName}: {classification.Value.GetTitle()}{accStr}");
        }
        else
        {
            pic.Visible = false;
            pic.Image = null;
            dash.Visible = true;
            _toolTip.SetToolTip(pic, string.Empty);
        }
    }

    private Panel CreatePlayerScoreCard(bool isWhitePlayer, out Label lblScore)
    {
        var card = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = isWhitePlayer ? Color.White : Color.FromArgb(38, 36, 34),
            Margin = new Padding(2, 2, 2, 2)
        };
        card.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = CreateRoundedPath(new Rectangle(0, 0, card.Width - 1, card.Height - 1), 6);
            using var bg = new SolidBrush(isWhitePlayer ? Color.White : Color.FromArgb(38, 36, 34));
            e.Graphics.FillPath(bg, path);
            if (!isWhitePlayer)
            {
                using var borderPen = new Pen(Color.FromArgb(70, 68, 65), 1.2f);
                e.Graphics.DrawPath(borderPen, path);
            }
        };

        lblScore = new Label
        {
            Text = "--.-",
            Font = new Font("Segoe UI", 13.5f, FontStyle.Bold),
            ForeColor = isWhitePlayer ? Color.FromArgb(28, 27, 24) : Color.FromArgb(245, 245, 245),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.Transparent
        };
        card.Controls.Add(lblScore);
        return card;
    }

    private void BuildBadgeTableRows()
    {
        _tableBadges.Controls.Clear();

        MoveClassification[] order =
        {
            MoveClassification.Brilliant,
            MoveClassification.Great,
            MoveClassification.Book,
            MoveClassification.Best,
            MoveClassification.Excellent,
            MoveClassification.Good,
            MoveClassification.Inaccuracy,
            MoveClassification.Mistake,
            MoveClassification.MissedWin,
            MoveClassification.Blunder
        };

        int row = 0;
        foreach (var c in order)
        {
            _tableBadges.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));

            string title = c switch
            {
                MoveClassification.Good => "Неплохой",
                MoveClassification.MissedWin => "Упущенный шанс",
                _ => c.GetTitle()
            };

            var lblName = new Label
            {
                Text = title,
                ForeColor = Color.FromArgb(220, 218, 215),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 0, 0)
            };

            var lblW = new Label
            {
                Text = "0",
                ForeColor = c.GetThemeColor(),
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Name = $"badge_w_{c}"
            };

            var picBadge = new PictureBox
            {
                Size = new Size(28, 28),
                SizeMode = PictureBoxSizeMode.Zoom,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 3, 0, 3),
                Image = ResourceManager.GetBadgeImage(c)
            };

            var lblB = new Label
            {
                Text = "0",
                ForeColor = c.GetThemeColor(),
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Name = $"badge_b_{c}"
            };

            _tableBadges.Controls.Add(lblName, 0, row);
            _tableBadges.Controls.Add(lblW, 1, row);
            _tableBadges.Controls.Add(picBadge, 2, row);
            _tableBadges.Controls.Add(lblB, 3, row);

            row++;
        }
    }
    #endregion

    #region View 2: Move-by-Move Review Panel (Full Moves Table + Large Nav)
    private Panel CreateMoveReviewPanel()
    {
        var pnl = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ChessTheme.PanelDark
        };

        // 1. Evaluation Graph (Top of Sidebar, Height 64px)
        _graphControl = new GameGraphControl
        {
            Dock = DockStyle.Top,
            Height = 64,
            BackColor = ChessTheme.PanelDarker
        };
        _graphControl.PlySelected += ply =>
        {
            GoToPly(ply);
            SwitchSidebarView("review");
        };

        // 2. Move Review Card (Directly above computer lines, matching reference images)
        _moveReviewCard = new MoveReviewCardControl
        {
            Dock = DockStyle.Top
        };
        _moveReviewCard.PlayedRowClicked += () =>
        {
            if (_currentPlyIndex >= 0) GoToPly(_currentPlyIndex);
        };
        _moveReviewCard.BestRowClicked += () =>
        {
            if (_currentPlyIndex >= 0)
            {
                OnCalloutTokenClicked(_currentPlyIndex, 0);
            }
        };

        // 3. Real-time Computer Lines Control (Thick rows, height dynamically updated)
        _computerLines = new ComputerLinesControl
        {
            Dock = DockStyle.Top
        };
        _computerLines.UpdateHeight();
        _computerLines.SettingsClicked += (s, e) =>
        {
            _menuSettings.Show(_computerLines, new Point(10, _computerLines.Height));
        };

        // 4. Dedicated Opening Bar
        _pnlOpeningBar = CreateOpeningBar();

        // 5. Move History Table (Fills ALL available space)
        _moveHistory = new MoveHistoryControl
        {
            Dock = DockStyle.Fill
        };
        _moveHistory.MoveSelected += ply => GoToPly(ply);
        _moveHistory.CalloutClicked += OnCalloutCardClicked;
        _moveHistory.CalloutTokenClicked += OnCalloutTokenClicked;
        _moveHistory.MoveHovered += ply =>
        {
            if (ply >= 0 && _currentAnalysis != null && ply < _currentAnalysis.Moves.Count)
            {
                var ma = _currentAnalysis.Moves[ply];
                _boardControl.ClearArrows();
                _boardControl.AddArrow(ma.Move.From, ma.Move.To, Color.FromArgb(105, 255, 255, 255), width: 14f, hasOutline: true);
                if (_showBestMoveArrow && ma.BestMove.HasValue && ma.BestMove.Value != ma.Move)
                {
                    _boardControl.AddArrow(ma.BestMove.Value.From, ma.BestMove.Value.To, Color.FromArgb(105, 52, 152, 219), width: 14f, hasOutline: true);
                }
            }
            else if (ply == -1)
            {
                RestoreCurrentPlyArrows();
            }
        };

        // 6. Bottom Playback Controls Bar (Large 76x52px and 90x52px vector buttons)
        var pnlPlayback = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 68,
            BackColor = ChessTheme.PanelDarker,
            Padding = new Padding(10, 8, 10, 8)
        };

        _btnFirst = CreateNavButton(ButtonIconType.First, (s, e) => GoToFirst(), 76);
        _btnPrev = CreateNavButton(ButtonIconType.Prev, (s, e) => StepBackward(), 76);
        _btnAutoPlay = CreateNavButton(ButtonIconType.Play, (s, e) => ToggleAutoPlay(), 90);
        _btnNext = CreateNavButton(ButtonIconType.Next, (s, e) => StepForward(), 76);
        _btnLast = CreateNavButton(ButtonIconType.Last, (s, e) => GoToLast(), 76);

        var flowNav = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent
        };
        flowNav.Controls.AddRange(new Control[]
        {
            _btnFirst, _btnPrev, _btnAutoPlay, _btnNext, _btnLast
        });

        // Center the buttons in the playback bar
        pnlPlayback.Resize += (s, e) =>
        {
            int totalBtnW = (76 * 4) + 90 + (8 * 4);
            int pad = Math.Max(0, (pnlPlayback.Width - totalBtnW) / 2);
            flowNav.Padding = new Padding(pad, 0, 0, 0);
        };

        pnlPlayback.Controls.Add(flowNav);

        _pnlVariationBox = CreateVariationBox();

        pnl.Controls.Add(_moveHistory);
        pnl.Controls.Add(pnlPlayback);
        pnl.Controls.Add(_pnlVariationBox);
        pnl.Controls.Add(_pnlOpeningBar);
        pnl.Controls.Add(_computerLines);
        pnl.Controls.Add(_moveReviewCard);
        pnl.Controls.Add(_graphControl);

        return pnl;
    }

    private Panel CreateVariationBox()
    {
        var box = new Panel
        {
            Dock = DockStyle.Top,
            Height = 74,
            BackColor = Color.FromArgb(32, 30, 28),
            Padding = new Padding(10, 6, 10, 6),
            Visible = false
        };

        box.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var borderPen = new Pen(Color.FromArgb(65, 63, 60), 1.2f);
            using var path = CreateRoundedPath(new Rectangle(2, 2, box.Width - 5, box.Height - 5), 6);
            e.Graphics.DrawPath(borderPen, path);
        };

        var pnlHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 26,
            BackColor = Color.Transparent
        };

        _lblVariationTitle = new Label
        {
            Text = "🔀 Отступление от партии",
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = Color.FromArgb(220, 218, 214),
            Dock = DockStyle.Left,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleLeft
        };

        var btnExit = new RoundedButton
        {
            Text = "✕ Вернуться",
            Dock = DockStyle.Right,
            Width = 98,
            Height = 24,
            BorderRadius = 4,
            NormalColor = Color.FromArgb(48, 46, 43),
            HoverColor = Color.FromArgb(65, 63, 60),
            CustomBorderColor = Color.FromArgb(75, 73, 70),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(235, 235, 235)
        };
        btnExit.Click += (s, e) => ExitVariation();

        pnlHeader.Controls.Add(_lblVariationTitle);
        pnlHeader.Controls.Add(btnExit);

        _flowVariationMoves = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 4, 0, 0)
        };

        box.Controls.Add(_flowVariationMoves);
        box.Controls.Add(pnlHeader);

        return box;
    }

    private void ExitVariation()
    {
        int returnPly = _variationBranchPly;
        _activeVariation.Clear();
        _activeVariationIndex = -1;
        _variationBranchPly = -1;
        _moveHistory.ClearVariation();
        _boardControl.IsAtGameEnd = false;
        _pnlVariationBox.Height = 72;
        UpdateVariationBoxUi();
        GoToPly(returnPly);
    }

    private void UpdateVariationBoxUi()
    {
        if (_activeVariation.Count == 0)
        {
            _pnlVariationBox.Visible = false;
            _flowVariationMoves.Controls.Clear();
            _pnlVariationBox.Height = 72;
            return;
        }

        _pnlVariationBox.Visible = true;

        int moveCount = _activeVariation.Count;
        int targetHeight = moveCount <= 4 ? 72 : (moveCount <= 8 ? 104 : 136);
        if (_pnlVariationBox.Height != targetHeight)
        {
            _pnlVariationBox.Height = targetHeight;
        }

        int branchMoveNum = _variationBranchPly >= 0 ? (_variationBranchPly / 2) + 1 : 1;
        bool branchIsWhite = _variationBranchPly < 0 || _variationBranchPly % 2 == 1;

        _lblVariationTitle.Text = $"🔀 Отступление от хода {branchMoveNum}{(!branchIsWhite ? "..." : ".")}";

        // Fast update if button count already matches variation steps: no flicker, no button destruction!
        if (_flowVariationMoves.Controls.Count == _activeVariation.Count)
        {
            for (int i = 0; i < _activeVariation.Count; i++)
            {
                if (_flowVariationMoves.Controls[i] is Button b)
                {
                    bool isActive = (i == _activeVariationIndex);
                    b.BackColor = isActive ? ChessTheme.AccentGreen : Color.FromArgb(46, 44, 41);
                    b.ForeColor = isActive ? Color.White : Color.FromArgb(220, 218, 215);
                    b.Font = new Font("Segoe UI", 8.5f, isActive ? FontStyle.Bold : FontStyle.Regular);
                    b.FlatAppearance.BorderColor = isActive ? ChessTheme.AccentGreen : Color.FromArgb(68, 66, 62);
                }
            }
            return;
        }

        _flowVariationMoves.SuspendLayout();
        _flowVariationMoves.Controls.Clear();

        for (int i = 0; i < _activeVariation.Count; i++)
        {
            int stepIdx = i;
            var step = _activeVariation[i];
            bool isActive = (i == _activeVariationIndex);

            int currentMoveNum = branchMoveNum + ((branchIsWhite ? i : i + 1) / 2);
            bool isWhiteMove = branchIsWhite ? (i % 2 == 0) : (i % 2 == 1);
            string prefix = isWhiteMove ? $"{currentMoveNum}. " : (i == 0 ? $"{currentMoveNum}... " : "");

            var btn = new Button
            {
                Text = prefix + step.San,
                AutoSize = true,
                Height = 26,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 8.5f, isActive ? FontStyle.Bold : FontStyle.Regular),
                BackColor = isActive ? ChessTheme.AccentGreen : Color.FromArgb(46, 44, 41),
                ForeColor = isActive ? Color.White : Color.FromArgb(220, 218, 215),
                Cursor = Cursors.Hand,
                Margin = new Padding(2, 1, 2, 1)
            };
            btn.FlatAppearance.BorderSize = 1;
            btn.FlatAppearance.BorderColor = isActive ? ChessTheme.AccentGreen : Color.FromArgb(68, 66, 62);

            btn.Click += (s, e) =>
            {
                _activeVariationIndex = stepIdx;
                DisplayVariationStep(stepIdx);
                UpdateVariationBoxUi();
            };

            _flowVariationMoves.Controls.Add(btn);
        }

        _flowVariationMoves.ResumeLayout();
    }

    private Panel CreateOpeningBar()
    {
        var bar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 32,
            BackColor = Color.FromArgb(28, 27, 24),
            Padding = new Padding(12, 0, 10, 0)
        };
        bar.Paint += (s, e) =>
        {
            using var borderPen = new Pen(ChessTheme.BorderColor, 1f);
            e.Graphics.DrawLine(borderPen, 0, bar.Height - 1, bar.Width, bar.Height - 1);
        };

        _lblOpeningName = new Label
        {
            Text = "📘 Начальная позиция",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(225, 223, 220),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };

        var lblBookIcon = new Label
        {
            Text = "📖",
            Font = new Font("Segoe UI", 10.5f),
            ForeColor = Color.FromArgb(160, 158, 155),
            Dock = DockStyle.Right,
            Width = 28,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand
        };

        bar.Controls.Add(_lblOpeningName);
        bar.Controls.Add(lblBookIcon);
        return bar;
    }

    private void SetOpeningName(string? opening)
    {
        if (_lblOpeningName == null) return;
        if (string.IsNullOrWhiteSpace(opening))
        {
            if (!string.IsNullOrEmpty(_lastBookOpeningName))
            {
                opening = _lastBookOpeningName;
            }
            else
            {
                opening = "Дебют: Произвольная партия";
            }
        }
        else if (opening != "Начальная позиция" && opening != "Дебют: Произвольная партия")
        {
            _lastBookOpeningName = opening;
        }

        string clean = opening.StartsWith("📘") ? opening : $"📘 {opening}";
        _lblOpeningName.Text = clean;
    }

    private void TriggerStartupAnalysis()
    {
        if (_engine == null) return;
        var startBoard = new BoardState();
        _ = Task.Run(async () =>
        {
            try
            {
                var eval = await _engine.AnalyzePositionAsync(startBoard.ToFen(), depth: _analysisDepth, nodes: 400_000);
                if (_currentPlyIndex == -1 && !_isAnalyzingGame && _currentGame.Moves.Count == 0)
                {
                    Invoke(() =>
                    {
                        if (_currentPlyIndex == -1 && !_isAnalyzingGame && _currentGame.Moves.Count == 0)
                        {
                            _lastPositionEval = eval;
                            _currentFenEval = eval;
                            _currentEvalFen = startBoard.ToFen();
                            _evalBar.SetEvaluation(eval);
                            _computerLines.SetLines(eval, startBoard);
                        }
                    });
                }
            }
            catch { }
        });
    }

    private void PlayBestMovePreview()
    {
        if (_currentAnalysis == null || _currentPlyIndex < 0 || _currentPlyIndex >= _currentAnalysis.Moves.Count)
        {
            return;
        }

        var ma = _currentAnalysis.Moves[_currentPlyIndex];
        if (ma.BestMove.HasValue && !ma.BestMove.Value.IsEmpty)
        {
            var bm = ma.BestMove.Value;
            var board = new BoardState();
            board.LoadFen(ma.FenBefore);
            board.ApplyMove(bm);

            if (bm.IsCapture) SoundService.PlayCapture(); else SoundService.PlayMove();

            _boardControl.IsAtGameEnd = false;
            _boardControl.SetPosition(board, bm.From, bm.To, MoveClassification.Best, animate: true);
            UpdateCapturedPieces(board);

            _boardControl.ClearArrows();
            _boardControl.AddArrow(bm.From, bm.To, Color.FromArgb(204, 129, 182, 76), width: 7.5f, hasOutline: true);
        }
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
        if (pv == null || pv.Count == 0) return "";

        var tempBoard = board.Clone();
        var parts = new List<string>();
        int moveCount = Math.Min(5, pv.Count);

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

    private static RoundedButton CreateNavButton(ButtonIconType iconType, EventHandler onClick, int width)
    {
        var btn = new RoundedButton
        {
            IconType = iconType,
            Width = width,
            Height = 52,
            BorderRadius = 10,
            NormalColor = Color.FromArgb(48, 46, 43),
            HoverColor = Color.FromArgb(64, 62, 58),
            PressedColor = Color.FromArgb(80, 78, 72),
            CustomBorderColor = Color.FromArgb(70, 68, 65),
            Margin = new Padding(4, 0, 4, 0)
        };
        btn.Click += onClick;
        return btn;
    }
    #endregion

    #region Engine & Game Management
    private void InitializeEngine()
    {
        try
        {
            string? enginePath = _initialEnginePath;
            if (string.IsNullOrWhiteSpace(enginePath) || !File.Exists(enginePath))
            {
                var settings = AppSettings.Load();
                enginePath = settings.StockfishPath;
            }
            if (string.IsNullOrWhiteSpace(enginePath) || !File.Exists(enginePath))
            {
                enginePath = StockfishEngine.FindStockfishPath();
            }

            if (!string.IsNullOrEmpty(enginePath) && File.Exists(enginePath))
            {
                _engine = new StockfishEngine(enginePath);
                _analysisService = new AnalysisService(_engine);
                _computerLines.LineCount = _settings.MultiPv;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _engine.StartAsync(threads: _settings.Threads);
                        await _engine.SetMultiPvAsync(_settings.MultiPv);
                    }
                    catch { }
                });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Ошибка инициализации Stockfish:\n" + ex.Message, "Stockfish Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    public void LoadPgnFromText(string pgnText)
    {
        if (string.IsNullOrWhiteSpace(pgnText)) return;
        pgnText = pgnText.Trim().TrimStart('\uFEFF');
        var games = PgnParser.ParseGames(pgnText);
        if (games.Count > 0)
        {
            LoadGame(games[0], pgnText);
        }
        else
        {
            MessageBox.Show("Не удалось распознать партию из указанного текста.", "Ошибка PGN", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    public void ResetToStartupScreen()
    {
        StopAutoPlay();
        _analysisCts?.Cancel();
        _variationCts?.Cancel();
        _isAnalyzingGame = false;
        _progressBar.Visible = false;

        _currentGame = new PgnGame();
        _currentAnalysis = null;
        _currentPlyIndex = -1;
        _rawPgnText = string.Empty;
        _activeVariation.Clear();

        _boardControl.IsFlipped = false;
        _evalBar.IsFlipped = false;
        _moveHistory.UserFilterColor = null;

        UpdatePlayerBarLabels("Белые", "Черные");
        _lblTopClock.Text = "";
        _lblBottomClock.Text = "";
        _lblOverviewWhitePlayer.Text = "Белые";
        _lblOverviewBlackPlayer.Text = "Черные";

        _lblWhiteAccuracy.Text = "--.-";
        _lblBlackAccuracy.Text = "--.-";
        _lblWhiteElo.Text = "---";
        _lblBlackElo.Text = "---";
        ResetPhasesData();

        var startBoard = new BoardState();
        _boardControl.SetPosition(startBoard, Square.None, Square.None, null, animate: false);
        _boardControl.ClearArrows();
        _boardControl.GameResult = null;
        _boardControl.IsAtGameEnd = false;

        _moveHistory.EnableCallouts = false;
        _moveHistory.SetMoves(new List<MoveAnalysis>());
        _graphControl.Clear();
        _moveReviewCard.Visible = false;
        _moveReviewCard.Clear();
        _pnlVariationBox.Visible = false;
        _evalBar.Reset();
        UpdateCapturedPieces(startBoard);
        SetOpeningName("Начальная позиция");
        SwitchSidebarView("review");

        TriggerStartupAnalysis();
    }

    public void LoadGame(PgnGame game, string? rawPgn = null)
    {
        StopAutoPlay();
        _currentGame = game;
        _currentAnalysis = null;
        _currentPlyIndex = -1;
        _rawPgnText = rawPgn ?? string.Empty;

        // Phase 5: Show Color Selection Dialog before loading
        using (var dlg = new ColorSelectionDialog(game.White, game.WhiteElo, game.Black, game.BlackElo))
        {
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _userColor = dlg.SelectedColor;
                bool shouldFlip = (_userColor == PieceColor.Black);
                _boardControl.IsFlipped = shouldFlip;
                _evalBar.IsFlipped = shouldFlip;
                _moveHistory.UserFilterColor = _userColor;
            }
        }

        // Update player bars
        string wPlayer = string.IsNullOrEmpty(game.White) ? "Белые" : game.White;
        string bPlayer = string.IsNullOrEmpty(game.Black) ? "Черные" : game.Black;
        string wElo = string.IsNullOrEmpty(game.WhiteElo) ? "" : $" ({game.WhiteElo})";
        string bElo = string.IsNullOrEmpty(game.BlackElo) ? "" : $" ({game.BlackElo})";

        UpdatePlayerBarLabels(wPlayer + wElo, bPlayer + bElo);

        string opHeader = game.Headers.TryGetValue("Opening", out var openingHeader) && !string.IsNullOrWhiteSpace(openingHeader) ? openingHeader : (!string.IsNullOrEmpty(game.Opening) ? game.Opening : "Партия загружена");
        SetOpeningName(opHeader);

        string clockText = "10:00";
        if (game.Headers.TryGetValue("TimeControl", out var tc) && !string.IsNullOrWhiteSpace(tc))
        {
            string basePart = tc.Contains('+') ? tc.Split('+')[0] : (tc.Contains('/') ? tc.Split('/')[0] : tc);
            if (int.TryParse(basePart, out int sec) && sec > 0)
            {
                int mm = sec / 60;
                int ss = sec % 60;
                clockText = $"{mm}:{ss:D2}";
            }
        }
        _lblTopClock.Text = clockText;
        _lblBottomClock.Text = clockText;

        _lblOverviewWhitePlayer.Text = wPlayer;
        _lblOverviewBlackPlayer.Text = bPlayer;

        _lblWhiteAccuracy.Text = "--.-";
        _lblBlackAccuracy.Text = "--.-";
        _lblWhiteElo.Text = "---";
        _lblBlackElo.Text = "---";
        ResetPhasesData();

        var preliminaryMoves = new List<MoveAnalysis>();
        for (int i = 0; i < game.Moves.Count; i++)
        {
            var m = game.Moves[i];
            preliminaryMoves.Add(new MoveAnalysis
            {
                PlyIndex = i,
                MoveNumber = m.MoveNumber,
                Color = m.Color,
                San = m.San,
                Move = m.Move,
                FenBefore = m.FenBefore,
                FenAfter = m.FenAfter,
                Classification = m.ExpectedClassification ?? MoveClassification.Good
            });
        }

        // If PGN contains pre-annotated moves from Chess.com, calculate phases immediately!
        if (game.Moves.Any(m => m.ExpectedClassification.HasValue))
        {
            var prelimAnalysis = new GameAnalysis { Game = game };
            prelimAnalysis.Moves.AddRange(preliminaryMoves);
            foreach (var pm in preliminaryMoves)
            {
                if (pm.Color == PieceColor.White) prelimAnalysis.WhiteBadgeCounts[pm.Classification]++;
                else prelimAnalysis.BlackBadgeCounts[pm.Classification]++;
            }
            GamePhasesService.CalculatePhases(prelimAnalysis);
            UpdatePhasesData(prelimAnalysis);
            UpdateBadgeCountLabels(prelimAnalysis);
        }

        _moveHistory.SetMoves(preliminaryMoves);
        _graphControl.Clear();

        GoToPly(-1);
        SwitchSidebarView("review");

        // Phase 5: Automatically launch analysis
        _ = StartAnalysisAsync();
    }

    private void UpdatePlayerBarLabels(string whiteFull, string blackFull)
    {
        if (!_boardControl.IsFlipped)
        {
            _lblTopPlayer.Text = blackFull;
            _lblBottomPlayer.Text = whiteFull;
        }
        else
        {
            _lblTopPlayer.Text = whiteFull;
            _lblBottomPlayer.Text = blackFull;
        }

        _topAvatar.Invalidate();
        _bottomAvatar.Invalidate();
    }

    private void UpdateCapturedPieces(BoardState board)
    {
        int wP = 0, wN = 0, wB = 0, wR = 0, wQ = 0;
        int bP = 0, bN = 0, bB = 0, bR = 0, bQ = 0;

        for (int sq = 0; sq < 64; sq++)
        {
            var p = board[new Square(sq)];
            if (p.IsEmpty || p.Type == PieceType.King) continue;
            if (p.Color == PieceColor.White)
            {
                switch (p.Type)
                {
                    case PieceType.Pawn: wP++; break;
                    case PieceType.Knight: wN++; break;
                    case PieceType.Bishop: wB++; break;
                    case PieceType.Rook: wR++; break;
                    case PieceType.Queen: wQ++; break;
                }
            }
            else
            {
                switch (p.Type)
                {
                    case PieceType.Pawn: bP++; break;
                    case PieceType.Knight: bN++; break;
                    case PieceType.Bishop: bB++; break;
                    case PieceType.Rook: bR++; break;
                    case PieceType.Queen: bQ++; break;
                }
            }
        }

        int capByWhiteQ = Math.Max(0, 1 - bQ);
        int capByWhiteR = Math.Max(0, 2 - bR);
        int capByWhiteB = Math.Max(0, 2 - bB);
        int capByWhiteN = Math.Max(0, 2 - bN);
        int capByWhiteP = Math.Max(0, 8 - bP);

        int capByBlackQ = Math.Max(0, 1 - wQ);
        int capByBlackR = Math.Max(0, 2 - wR);
        int capByBlackB = Math.Max(0, 2 - wB);
        int capByBlackN = Math.Max(0, 2 - wN);
        int capByBlackP = Math.Max(0, 8 - wP);

        int whiteMaterial = wP * 1 + wN * 3 + wB * 3 + wR * 5 + wQ * 9;
        int blackMaterial = bP * 1 + bN * 3 + bB * 3 + bR * 5 + bQ * 9;
        int matDiff = whiteMaterial - blackMaterial;

        string whiteDiff = matDiff > 0 ? $"+{matDiff}" : "";
        string blackDiff = matDiff < 0 ? $"+{-matDiff}" : "";

        if (!_boardControl.IsFlipped)
        {
            _bottomCapturedCtrl.SetCaptured(capByWhiteQ, capByWhiteR, capByWhiteB, capByWhiteN, capByWhiteP, PieceColor.Black, whiteDiff);
            _topCapturedCtrl.SetCaptured(capByBlackQ, capByBlackR, capByBlackB, capByBlackN, capByBlackP, PieceColor.White, blackDiff);
        }
        else
        {
            _topCapturedCtrl.SetCaptured(capByWhiteQ, capByWhiteR, capByWhiteB, capByWhiteN, capByWhiteP, PieceColor.Black, whiteDiff);
            _bottomCapturedCtrl.SetCaptured(capByBlackQ, capByBlackR, capByBlackB, capByBlackN, capByBlackP, PieceColor.White, blackDiff);
        }
    }
    #endregion

    #region Navigation & Position Updating
    private void StepBackward()
    {
        if (_activeVariation.Count > 0)
        {
            if (_activeVariationIndex > 0)
            {
                _activeVariationIndex--;
                DisplayVariationStep(_activeVariationIndex);
                UpdateVariationBoxUi();
            }
            else if (_activeVariationIndex == 0)
            {
                // Step back to position BEFORE first move of variation, keeping variation active
                _activeVariationIndex = -1;
                var baseBoard = new BoardState();
                baseBoard.LoadFen(_activeVariation[0].FenBefore);
                _boardControl.SetPosition(baseBoard, Square.None, Square.None, null, animate: true);
                UpdateCapturedPieces(baseBoard);
                _computerLines.ClearLines();
                UpdateVariationBoxUi();
                TriggerVariationEngineAnalysis(_activeVariation[0].FenBefore);
            }
            else
            {
                // Already at -1: exit variation and step backward in base game
                int returnPly = _variationBranchPly;
                _activeVariation.Clear();
                _activeVariationIndex = -1;
                _variationBranchPly = -1;
                _moveHistory.ClearVariation();
                UpdateVariationBoxUi();
                GoToPly(returnPly);
            }
        }
        else
        {
            if (_currentPlyIndex <= -1) return;
            GoToPly(_currentPlyIndex - 1);
        }
    }

    private void StepForward()
    {
        if (_activeVariation.Count > 0)
        {
            if (_activeVariationIndex + 1 < _activeVariation.Count)
            {
                _activeVariationIndex++;
                DisplayVariationStep(_activeVariationIndex);
                UpdateVariationBoxUi();
            }
        }
        else
        {
            if (_currentGame.Moves.Count == 0 || _currentPlyIndex >= _currentGame.Moves.Count - 1)
            {
                return;
            }
            GoToPly(_currentPlyIndex + 1);
        }
    }

    private void GoToFirst()
    {
        _activeVariation.Clear();
        _activeVariationIndex = -1;
        _variationBranchPly = -1;
        _moveHistory.ClearVariation();
        UpdateVariationBoxUi();
        GoToPly(-1);
    }

    private void GoToLast()
    {
        _activeVariation.Clear();
        _activeVariationIndex = -1;
        _variationBranchPly = -1;
        _moveHistory.ClearVariation();
        UpdateVariationBoxUi();
        GoToPly(_currentGame.Moves.Count - 1);
    }

    private void DisplayVariationStep(int index)
    {
        if (index < 0 || index >= _activeVariation.Count) return;
        var step = _activeVariation[index];
        var board = new BoardState();
        board.LoadFen(step.FenAfter);

        _boardControl.IsAtGameEnd = false;
        _boardControl.SetPosition(board, step.Move.From, step.Move.To, step.Classification, animate: false);

        // Ensure capture sound plays if move was a capture or SAN contains 'x'
        var moveForSound = step.Move;
        if ((step.San.Contains('x') || step.Move.IsCapture) && !moveForSound.IsCapture)
        {
            moveForSound = new Move(moveForSound.From, moveForSound.To, moveForSound.Promotion, true, moveForSound.IsCastling, moveForSound.IsEnPassant);
        }
        PlayMoveSound(moveForSound, board);
        UpdateCapturedPieces(board);

        string? op = OpeningBook.GetOpeningName(step.FenAfter);
        if (!string.IsNullOrEmpty(op))
        {
            _lastBookOpeningName = op;
            SetOpeningName(op);
        }
        else if (!string.IsNullOrEmpty(_lastBookOpeningName))
        {
            SetOpeningName(_lastBookOpeningName);
        }

        if (step.PostEval != null)
        {
            _evalBar.SetEvaluation(step.PostEval);
            _computerLines.SetLines(step.PostEval, board);
            _currentFenEval = step.PostEval;
            _currentEvalFen = step.FenAfter;
        }
        else
        {
            TriggerVariationEngineAnalysis(step.FenAfter);
        }

        UpdateVariationBoxUi();
    }

    private void TriggerVariationEngineAnalysis(string fen)
    {
        _variationCts?.Cancel();
        _variationCts = new CancellationTokenSource();
        var ct = _variationCts.Token;

        if (_engine == null || !_engine.IsRunning) return;

        _ = Task.Run(async () =>
        {
            try
            {
                var eval = await _engine.AnalyzePositionAsync(fen, depth: _analysisDepth, nodes: 400_000, ct: ct);
                if (ct.IsCancellationRequested) return;

                Invoke(() =>
                {
                    if (!ct.IsCancellationRequested && _boardControl.Board.ToFen() == fen)
                    {
                        _evalBar.SetEvaluation(eval);
                        _computerLines.SetLines(eval, _boardControl.Board);
                        _currentFenEval = eval;
                        _currentEvalFen = fen;
                    }
                });
            }
            catch { }
        }, ct);
    }

    private static void PlayMoveSound(Move move, BoardState newBoard)
    {
        if (newBoard.IsInCheck(newBoard.SideToMove))
        {
            if (newBoard.GenerateLegalMoves().Count == 0)
            {
                SoundService.PlayGameEnd();
            }
            else
            {
                SoundService.PlayCheck();
            }
        }
        else if (move.IsCastling)
        {
            SoundService.PlayCastle();
        }
        else if (move.Promotion != PieceType.None)
        {
            SoundService.PlayPromote();
        }
        else if (move.IsCapture)
        {
            SoundService.PlayCapture();
        }
        else
        {
            SoundService.PlayMove();
        }
    }

    private void GoToPly(int plyIndex, bool forceRefresh = false)
    {
        if (_currentGame.Moves.Count == 0)
        {
            _boardControl.Board = new BoardState();
            _boardControl.LastMoveFrom = Square.None;
            _boardControl.LastMoveTo = Square.None;
            _boardControl.LastMoveBadge = null;
            _boardControl.IsAtGameEnd = false;
            _evalBar.Reset();
            _moveReviewCard.Clear();
            SetOpeningName("Начальная позиция");
            return;
        }

        plyIndex = Math.Clamp(plyIndex, -1, _currentGame.Moves.Count - 1);
        if (!forceRefresh && _currentPlyIndex == plyIndex && _activeVariation.Count == 0)
        {
            return;
        }

        _variationCts?.Cancel();
        _activeVariation.Clear();
        _activeVariationIndex = -1;
        _variationBranchPly = -1;
        _moveHistory.ClearVariation();
        UpdateVariationBoxUi();
        _currentPlyIndex = plyIndex;

        _boardControl.ClearArrows();

        if (plyIndex == -1)
        {
            var startBoard = new BoardState();
            _boardControl.IsAtGameEnd = false;
            _boardControl.SetPosition(startBoard, Square.None, Square.None, null, animate: false);
            _moveReviewCard.Clear();

            string op = !string.IsNullOrEmpty(_currentGame.Opening)
                ? _currentGame.Opening
                : (OpeningBook.GetOpeningName(startBoard.ToFen()) ?? "Начальная позиция");
            SetOpeningName(op);

            if (_currentAnalysis != null && _currentAnalysis.Moves.Count > 0)
            {
                var firstMa = _currentAnalysis.Moves[0];
                _currentFenEval = firstMa.PreEval;
                _currentEvalFen = startBoard.ToFen();
                _evalBar.SetEvaluation(firstMa.PreEval);
                _computerLines.SetLines(firstMa.PreEval, startBoard);
                if (_showBestMoveArrow && firstMa.BestMove.HasValue && !firstMa.BestMove.Value.IsEmpty)
                {
                    var bm = firstMa.BestMove.Value;
                    _boardControl.AddArrow(bm.From, bm.To, Color.FromArgb(105, 52, 152, 219), width: 14f, hasOutline: true);
                }
            }
            else
            {
                _evalBar.Reset();
                _computerLines.ClearLines();
                if (!_isAnalyzingGame && _engine != null && _engine.IsRunning)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var eval = await _engine.AnalyzePositionAsync(startBoard.ToFen(), depth: _analysisDepth, nodes: 400_000);
                            if (_currentPlyIndex == -1 && !_isAnalyzingGame)
                            {
                                Invoke(() =>
                                {
                                    if (_currentPlyIndex == -1 && !_isAnalyzingGame)
                                    {
                                        _currentFenEval = eval;
                                        _currentEvalFen = startBoard.ToFen();
                                        _evalBar.SetEvaluation(eval);
                                        _computerLines.SetLines(eval, startBoard);
                                    }
                                });
                            }
                        }
                        catch { }
                    });
                }
            }

            _graphControl.SelectedPly = -1;
            UpdateCapturedPieces(startBoard);
            return;
        }

        var move = _currentGame.Moves[plyIndex];
        var board = new BoardState();
        board.LoadFen(move.FenAfter);

        bool isAtEnd = plyIndex == _currentGame.Moves.Count - 1 && _currentGame.Moves.Count > 0 && _activeVariation.Count == 0;

        MoveClassification? badge = null;
        if (_currentAnalysis != null && plyIndex < _currentAnalysis.Moves.Count)
        {
            badge = _currentAnalysis.Moves[plyIndex].Classification;
        }

        // Sound feedback
        if (isAtEnd && !string.IsNullOrWhiteSpace(_currentGame.Result) && _currentGame.Result != "*")
        {
            SoundService.PlayGameEnd();
        }
        else if (badge != MoveClassification.Brilliant)
        {
            PlayMoveSound(move.Move, board);
        }

        // Clock timer display update if available
        if (!string.IsNullOrEmpty(move.Clock))
        {
            if (!_boardControl.IsFlipped)
            {
                if (move.Color == PieceColor.White) _lblBottomClock.Text = move.Clock;
                else _lblTopClock.Text = move.Clock;
            }
            else
            {
                if (move.Color == PieceColor.White) _lblTopClock.Text = move.Clock;
                else _lblBottomClock.Text = move.Clock;
            }
        }
        if (_currentAnalysis != null && plyIndex < _currentAnalysis.Moves.Count)
        {
            var ma = _currentAnalysis.Moves[plyIndex];
            badge = ma.Classification;

            // Integrity check: if board is in checkmate, ensure post-eval reflects exact mate 0 for winning color
            bool isDeliveredMate = board.IsInCheck(board.SideToMove) && board.GenerateLegalMoves().Count == 0;
            if (isDeliveredMate)
            {
                ma.PostEval.MateInMoves = 0;
                ma.PostEval.Centipawns = ma.Color == PieceColor.White ? 100000 : -100000;
            }

            _currentFenEval = ma.PostEval;
            _currentEvalFen = board.ToFen();
            _evalBar.SetEvaluation(ma.PostEval);
            _computerLines.SetLines(ma.PostEval, board);

            bool hasBetter = ma.Classification is MoveClassification.Blunder or MoveClassification.Mistake or MoveClassification.Inaccuracy or MoveClassification.MissedWin;
            string playedScore = FormatEvalPill(ma.PostEval.Centipawns, ma.PostEval.MateInMoves);
            string bestScore = FormatEvalPill(ma.PreEval.Centipawns, ma.PreEval.MateInMoves);
            string bestSan = ma.BestMoveSan;

            var boardBefore = new BoardState();
            boardBefore.LoadFen(ma.FenBefore);
            string playedCont = FormatPvLine(ma.PostEval.Pv, board);
            string bestCont = FormatPvLine(ma.PreEval.Pv, boardBefore);

            _moveReviewCard.Visible = true;
            _moveReviewCard.SetReview(
                playedScore,
                ma.Classification,
                ma.San,
                playedCont,
                hasBetter,
                bestScore,
                bestSan,
                bestCont);

            RestoreCurrentPlyArrows();
        }
        else
        {
            _moveReviewCard.Visible = false;
            _moveReviewCard.Clear();
            if (!_isAnalyzingGame && _engine != null && _engine.IsRunning)
            {
                _variationCts?.Cancel();
                _variationCts = new CancellationTokenSource();
                var ct = _variationCts.Token;
                string fenToAnalyze = board.ToFen();
                int targetPly = plyIndex;
                _computerLines.SetStatus("Анализ позиции...");
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var eval = await _engine.AnalyzePositionAsync(fenToAnalyze, depth: _analysisDepth, nodes: 400_000, ct: ct);
                        if (_currentPlyIndex == targetPly && !_isAnalyzingGame && !ct.IsCancellationRequested)
                        {
                            Invoke(() =>
                            {
                                if (_currentPlyIndex == targetPly && !_isAnalyzingGame)
                                {
                                    _currentFenEval = eval;
                                    _currentEvalFen = fenToAnalyze;
                                    _evalBar.SetEvaluation(eval);
                                    _computerLines.SetLines(eval, board);
                                }
                            });
                        }
                    }
                    catch { }
                });
            }
        }

        _boardControl.GameResult = _currentGame.Result;
        _boardControl.IsAtGameEnd = isAtEnd;
        _boardControl.SetPosition(board, move.Move.From, move.Move.To, badge, animate: true);

        if (isAtEnd)
        {
            _boardControl.TriggerGameEndAnimation();
        }
        _moveHistory.SelectPly(plyIndex);
        _graphControl.SelectedPly = plyIndex;
        UpdateCapturedPieces(board);

        // Update opening badge
        string? openName = !string.IsNullOrEmpty(_currentGame.Opening)
            ? _currentGame.Opening
            : OpeningBook.GetOpeningName(board.ToFen());
        SetOpeningName(openName);
    }

    private void RestoreCurrentPlyArrows()
    {
        _boardControl.ClearArrows();
        if (_currentAnalysis == null || _currentPlyIndex < 0 || _currentPlyIndex >= _currentAnalysis.Moves.Count)
        {
            return;
        }

        var ma = _currentAnalysis.Moves[_currentPlyIndex];

        // 1. Opponent's Threat Arrow (Red)
        if (_showThreatArrow && ma.ThreatMove.HasValue && !ma.ThreatMove.Value.IsEmpty && ma.ThreatMove.Value.From.IsValid && ma.ThreatMove.Value.To.IsValid)
        {
            if (ma.Classification is MoveClassification.Blunder or MoveClassification.Mistake or MoveClassification.Inaccuracy or MoveClassification.MissedWin)
            {
                _boardControl.AddArrow(ma.ThreatMove.Value.From, ma.ThreatMove.Value.To, Color.FromArgb(105, 231, 76, 60), width: 14f, hasOutline: true);
            }
        }

        // 2. Best Move Arrow (Blue)
        if (_showBestMoveArrow && ma.BestMove.HasValue && !ma.BestMove.Value.IsEmpty && ma.BestMove.Value.From.IsValid && ma.BestMove.Value.To.IsValid)
        {
            if (ma.BestMove.Value != ma.Move || ma.Classification is MoveClassification.Brilliant or MoveClassification.Great or MoveClassification.Best)
            {
                _boardControl.AddArrow(ma.BestMove.Value.From, ma.BestMove.Value.To, Color.FromArgb(105, 52, 152, 219), width: 14f, hasOutline: true);
            }
        }
    }
    #endregion

    #region Analysis Execution
    private async Task StartAnalysisAsync()
    {
        if (_analysisService == null || _engine == null)
        {
            MessageBox.Show("Движок Stockfish 19 не запущен.", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (_currentGame.Moves.Count == 0)
        {
            MessageBox.Show("Партия не загружена.", "Внимание", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Cancel any pending reactive position analysis first!
        _variationCts?.Cancel();
        _isAnalyzingGame = true;

        _progressBar.Visible = true;
        _progressBar.Value = 0;

        _analysisCts = new CancellationTokenSource();
        int depth = _analysisDepth;

        var progress = new Progress<AnalysisProgress>(p =>
        {
            _progressBar.Value = Math.Clamp((int)p.Percent, 0, 100);
            if (p.LatestAnalyzedMove != null)
            {
                var ma = p.LatestAnalyzedMove;
                _graphControl.AddMove(ma);
                _moveHistory.UpdateAnalyzedMove(ma);

                if (_currentPlyIndex == ma.PlyIndex)
                {
                    _currentFenEval = ma.PostEval;
                    _currentEvalFen = ma.FenAfter;
                    var bAfter = new BoardState();
                    bAfter.LoadFen(ma.FenAfter);
                    _evalBar.SetEvaluation(ma.PostEval);
                    _computerLines.SetLines(ma.PostEval, bAfter);

                    string playedScore = FormatEvalPill(ma.PostEval.Centipawns, ma.PostEval.MateInMoves);
                    string bestScore = FormatEvalPill(ma.PreEval.Centipawns, ma.PreEval.MateInMoves);
                    var bBefore = new BoardState();
                    bBefore.LoadFen(ma.FenBefore);
                    string playedCont = FormatPvLine(ma.PostEval.Pv, bAfter);
                    string bestCont = FormatPvLine(ma.PreEval.Pv, bBefore);
                    bool hasBetter = ma.Classification is MoveClassification.Blunder or MoveClassification.Mistake or MoveClassification.Inaccuracy or MoveClassification.MissedWin;

                    _moveReviewCard.SetReview(
                        playedScore,
                        ma.Classification,
                        ma.San,
                        playedCont,
                        hasBetter,
                        bestScore,
                        ma.BestMoveSan,
                        bestCont);

                    _boardControl.LastMoveBadge = ma.Classification;
                    RestoreCurrentPlyArrows();
                }
            }
        });

        try
        {
            _graphControl.Clear();

            var analysis = await _analysisService.AnalyzeGameAsync(_currentGame, depth: _analysisDepth, nodes: _analysisNodes, progress: progress, ct: _analysisCts.Token);
            _currentAnalysis = analysis;

            // Update UI with complete review
            _lblWhiteAccuracy.Text = $"{analysis.WhiteAccuracy:0.0}";
            _lblBlackAccuracy.Text = $"{analysis.BlackAccuracy:0.0}";
            _lblWhiteElo.Text = analysis.WhiteEstimatedElo.ToString();
            _lblBlackElo.Text = analysis.BlackEstimatedElo.ToString();

            _moveHistory.EnableCallouts = true;
            _moveHistory.SetMoves(analysis.Moves);
            _graphControl.SetMoves(analysis.Moves);
            _moveReviewCard.Visible = true;

            UpdateBadgeCountLabels(analysis);
            UpdatePhasesData(analysis);

            _isAnalyzingGame = false;
            GoToPly(_currentPlyIndex >= 0 ? _currentPlyIndex : 0, forceRefresh: true);
        }
        catch (OperationCanceledException)
        {
            // Stopped
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка во время анализа:\n{ex.Message}", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _isAnalyzingGame = false;
            _progressBar.Visible = false;
            _analysisCts?.Dispose();
            _analysisCts = null;
        }
    }

    private void UpdateBadgeCountLabels(GameAnalysis analysis)
    {
        foreach (MoveClassification c in Enum.GetValues<MoveClassification>())
        {
            var controlsW = _tableBadges.Controls.Find($"badge_w_{c}", true);
            if (controlsW.Length > 0 && controlsW[0] is Label lblW)
            {
                int countW = analysis.WhiteBadgeCounts[c];
                lblW.Text = countW.ToString();
                lblW.ForeColor = c.GetThemeColor();
            }

            var controlsB = _tableBadges.Controls.Find($"badge_b_{c}", true);
            if (controlsB.Length > 0 && controlsB[0] is Label lblB)
            {
                int countB = analysis.BlackBadgeCounts[c];
                lblB.Text = countB.ToString();
                lblB.ForeColor = c.GetThemeColor();
            }
        }
    }

    private void StopAnalysis()
    {
        _analysisCts?.Cancel();
    }
    #endregion

    #region Dialogs & Actions
    private void OpenLoadPgnDialog()
    {
        using var dlg = new ImportPgnDialog(_rawPgnText);
        if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.PgnText))
        {
            LoadPgnFromText(dlg.PgnText);
        }
    }

    private void OpenDownloadUserDialog()
    {
        using var dlg = new DownloadGameDialog();
        if (dlg.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dlg.SelectedPgn))
        {
            LoadPgnFromText(dlg.SelectedPgn);
        }
    }

    private void CopyPgnToClipboard()
    {
        string pgn = !string.IsNullOrWhiteSpace(_rawPgnText)
            ? _rawPgnText
            : (_currentGame.Moves.Count > 0 ? _currentGame.ToPgn(_currentAnalysis) : string.Empty);

        if (!string.IsNullOrWhiteSpace(pgn))
        {
            Clipboard.SetText(pgn);
            MessageBox.Show("Текст партии (PGN) скопирован в буфер обмена.", "PGN скопирован", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show("Партия ещё не сыграна и не загружена.", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void BrowseEngineFile()
    {
        _settings = AppSettings.Load();
        using var setupDlg = new StockfishSetupDialog(_settings);
        if (setupDlg.ShowDialog(this) == DialogResult.OK)
        {
            _settings = AppSettings.Load();
            if (!string.IsNullOrEmpty(_settings.StockfishPath) && File.Exists(_settings.StockfishPath))
            {
                _engine?.Dispose();
                _engine = new StockfishEngine(_settings.StockfishPath);
                _analysisService = new AnalysisService(_engine);
                _computerLines.LineCount = _settings.MultiPv;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _engine.StartAsync(threads: _settings.Threads);
                        await _engine.SetMultiPvAsync(_settings.MultiPv);
                    }
                    catch { }
                });
                MessageBox.Show($"Движок успешно подключён:\n{Path.GetFileName(_settings.StockfishPath)}", "Stockfish", MessageBoxButtons.OK, MessageBoxIcon.Information);
                TriggerStartupAnalysis();
            }
        }
    }

    private void ToggleBoardFlip()
    {
        _boardControl.IsFlipped = !_boardControl.IsFlipped;
        _evalBar.IsFlipped = _boardControl.IsFlipped;
        _boardControl.Invalidate();
        _evalBar.Invalidate();

        string wPlayer = string.IsNullOrEmpty(_currentGame.White) ? "Белые" : _currentGame.White;
        string bPlayer = string.IsNullOrEmpty(_currentGame.Black) ? "Черные" : _currentGame.Black;
        string wElo = string.IsNullOrEmpty(_currentGame.WhiteElo) ? "" : $" ({_currentGame.WhiteElo})";
        string bElo = string.IsNullOrEmpty(_currentGame.BlackElo) ? "" : $" ({_currentGame.BlackElo})";
        UpdatePlayerBarLabels(wPlayer + wElo, bPlayer + bElo);
        UpdateCapturedPieces(_boardControl.Board);
    }

    private void ToggleAutoPlay()
    {
        if (_autoPlayTimer.Enabled)
        {
            StopAutoPlay();
        }
        else
        {
            _btnAutoPlay.IconType = ButtonIconType.Pause;
            _btnAutoPlay.NormalColor = ChessTheme.AccentBlue;
            _autoPlayTimer.Start();
        }
    }

    private void StopAutoPlay()
    {
        _autoPlayTimer.Stop();
        _btnAutoPlay.IconType = ButtonIconType.Play;
        _btnAutoPlay.NormalColor = Color.FromArgb(48, 46, 43);
    }

    private async void OnBoardMovePlayed(Move move)
    {
        string fenBefore = _boardControl.LastFenBefore;
        if (string.IsNullOrEmpty(fenBefore))
        {
            fenBefore = _boardControl.Board.ToFen();
        }

        var boardBefore = new BoardState();
        boardBefore.LoadFen(fenBefore);

        var newBoard = _boardControl.Board;
        string fenAfter = newBoard.ToFen();

        PlayMoveSound(move, newBoard);
        UpdateCapturedPieces(newBoard);

        // Clear stale badges from previous game plies on exploratory moves
        _boardControl.LastMoveBadge = null;
        _boardControl.IsAtGameEnd = false;

        string uci = move.ToUci();
        string san = AnalysisService.ConvertUciToSan(fenBefore, uci);
        if (string.IsNullOrEmpty(san)) san = uci;

        if (_currentAnalysis == null)
        {
            // FIRST SCREEN (Live Play Mode):
            // If stepped backward, all subsequent moves simply disappear
            if (_currentPlyIndex < _currentGame.Moves.Count - 1)
            {
                int removeStart = _currentPlyIndex + 1;
                _currentGame.Moves.RemoveRange(removeStart, _currentGame.Moves.Count - removeStart);
                _moveHistory.TruncateAfter(_currentPlyIndex);
            }

            _activeVariation.Clear();
            _activeVariationIndex = -1;
            _variationBranchPly = -1;
            _moveHistory.ClearVariation();
            _pnlVariationBox.Visible = false;
            _moveReviewCard.Visible = false;

            var pgnMove = new PgnMove
            {
                MoveNumber = (_currentGame.Moves.Count / 2) + 1,
                Color = boardBefore.SideToMove,
                San = san,
                Move = move,
                FenBefore = fenBefore,
                FenAfter = fenAfter
            };
            _currentGame.Moves.Add(pgnMove);

            var ma = new MoveAnalysis
            {
                PlyIndex = _currentGame.Moves.Count - 1,
                MoveNumber = pgnMove.MoveNumber,
                Color = pgnMove.Color,
                San = pgnMove.San,
                Move = pgnMove.Move,
                FenBefore = pgnMove.FenBefore,
                FenAfter = pgnMove.FenAfter,
                Classification = MoveClassification.Book
            };

            _moveHistory.AddMove(ma);
            _currentPlyIndex = _currentGame.Moves.Count - 1;
            _moveHistory.SelectPly(_currentPlyIndex);
            _rawPgnText = _currentGame.ToPgn();
        }
        else
        {
            // SECOND SCREEN (Analyzed Game Exploration Mode):
            bool isAtEndOfGame = (_activeVariation.Count == 0 && (_currentGame.Moves.Count == 0 || _currentPlyIndex >= _currentGame.Moves.Count - 1));

            if (isAtEndOfGame)
            {
                var pgnMove = new PgnMove
                {
                    MoveNumber = (_currentGame.Moves.Count / 2) + 1,
                    Color = boardBefore.SideToMove,
                    San = san,
                    Move = move,
                    FenBefore = fenBefore,
                    FenAfter = fenAfter
                };
                _currentGame.Moves.Add(pgnMove);

                var ma = new MoveAnalysis
                {
                    PlyIndex = _currentGame.Moves.Count - 1,
                    MoveNumber = pgnMove.MoveNumber,
                    Color = pgnMove.Color,
                    San = pgnMove.San,
                    Move = pgnMove.Move,
                    FenBefore = pgnMove.FenBefore,
                    FenAfter = pgnMove.FenAfter,
                    Classification = MoveClassification.Book
                };

                _moveHistory.AddMove(ma);
                _currentPlyIndex = _currentGame.Moves.Count - 1;
                _moveHistory.SelectPly(_currentPlyIndex);
                _rawPgnText = _currentGame.ToPgn();
            }
            else if (_activeVariation.Count == 0 && _currentPlyIndex < _currentGame.Moves.Count - 1 &&
                     _currentGame.Moves[_currentPlyIndex + 1].Move == move)
            {
                GoToPly(_currentPlyIndex + 1);
                return;
            }
            else
            {
                if (_activeVariation.Count == 0 || _activeVariationIndex < 0)
                {
                    _variationBranchPly = _currentPlyIndex;
                    _activeVariation.Clear();
                    _activeVariation.Add(new VariationStep
                    {
                        Move = move,
                        San = san,
                        FenBefore = fenBefore,
                        FenAfter = fenAfter
                    });
                    _activeVariationIndex = 0;
                }
                else
                {
                    if (_activeVariationIndex + 1 < _activeVariation.Count)
                    {
                        _activeVariation.RemoveRange(_activeVariationIndex + 1, _activeVariation.Count - (_activeVariationIndex + 1));
                    }
                    _activeVariation.Add(new VariationStep
                    {
                        Move = move,
                        San = san,
                        FenBefore = fenBefore,
                        FenAfter = fenAfter
                    });
                    _activeVariationIndex = _activeVariation.Count - 1;
                }

                _moveHistory.SetVariation(_variationBranchPly, _activeVariation.Select(v => v.San));
                UpdateVariationBoxUi();
            }
        }

        string? op = OpeningBook.GetOpeningName(fenAfter);
        if (!string.IsNullOrEmpty(op))
        {
            _lastBookOpeningName = op;
            SetOpeningName(op);
        }
        else if (!string.IsNullOrEmpty(_lastBookOpeningName))
        {
            SetOpeningName(_lastBookOpeningName);
        }

        _variationCts?.Cancel();
        _variationCts = new CancellationTokenSource();
        var ct = _variationCts.Token;

        if (_engine == null || !_engine.IsRunning) return;

        // Show thinking animation on Move Review Card only if in analyzed review mode
        if (_currentAnalysis != null)
        {
            _moveReviewCard.Visible = true;
            _moveReviewCard.ShowCalculating();
        }
        else
        {
            _moveReviewCard.Visible = false;
        }
        _computerLines.SetStatus("Анализ позиции...");

        try
        {
            EngineEvaluation preEval;
            if (_currentFenEval != null && _currentEvalFen == fenBefore)
            {
                preEval = _currentFenEval;
            }
            else
            {
                preEval = await _engine.AnalyzePositionAsync(fenBefore, depth: _analysisDepth, nodes: 400_000, ct: ct);
            }

            var postEval = await _engine.AnalyzePositionAsync(fenAfter, depth: _analysisDepth, nodes: 600_000, ct: ct);
            if (ct.IsCancellationRequested) return;

            _currentFenEval = postEval;
            _currentEvalFen = fenAfter;

            if (_currentAnalysis != null)
            {
                bool isTopMove = string.Equals(uci, preEval.BestMoveUci, StringComparison.OrdinalIgnoreCase);

                double preWinWhite = preEval.WinProbabilityWhite;
                double postWinWhite = postEval.WinProbabilityWhite;
                PieceColor moveColor = boardBefore.SideToMove;
                double deltaP = isTopMove
                    ? 0.0
                    : (moveColor == PieceColor.White
                        ? Math.Max(0.0, preWinWhite - postWinWhite)
                        : Math.Max(0.0, (100.0 - preWinWhite) - (100.0 - postWinWhite)));

                int moveNum = (_currentPlyIndex >= 0 ? (_currentPlyIndex / 2) + 1 : 1) + (_activeVariationIndex / 2);
                int? userRating = moveColor == PieceColor.White
                    ? (int.TryParse(_currentGame.WhiteElo, out int wr) ? wr : null)
                    : (int.TryParse(_currentGame.BlackElo, out int br) ? br : null);
                var classification = AnalysisService.ClassifySingleMove(move, san, moveColor, moveNum, fenBefore, fenAfter, preEval, postEval, playerRating: userRating);
                if (isTopMove && classification is MoveClassification.Blunder or MoveClassification.Mistake or MoveClassification.Inaccuracy)
                {
                    classification = MoveClassification.Best;
                }

                if (_activeVariationIndex >= 0 && _activeVariationIndex < _activeVariation.Count)
                {
                    _activeVariation[_activeVariationIndex].Classification = classification;
                    _activeVariation[_activeVariationIndex].PreEval = preEval;
                    _activeVariation[_activeVariationIndex].PostEval = postEval;
                }

                _boardControl.LastMoveBadge = classification;

                string playedScore = FormatEvalPill(postEval.Centipawns, postEval.MateInMoves);
                string bestScore = FormatEvalPill(preEval.Centipawns, preEval.MateInMoves);
                string bestMoveSan = !string.IsNullOrEmpty(preEval.BestMoveUci)
                    ? AnalysisService.ConvertUciToSan(fenBefore, preEval.BestMoveUci)
                    : "";

                bool hasBetter = classification is MoveClassification.Blunder or MoveClassification.Mistake or MoveClassification.Inaccuracy or MoveClassification.MissedWin;

                string playedCont = FormatPvLine(postEval.Pv, newBoard);
                string bestCont = FormatPvLine(preEval.Pv, boardBefore);

                _moveReviewCard.Visible = true;
                _moveReviewCard.SetReview(
                    playedScore,
                    classification,
                    san,
                    playedCont,
                    hasBetter,
                    bestScore,
                    bestMoveSan,
                    bestCont);
            }
            else
            {
                _moveReviewCard.Visible = false;
                _boardControl.LastMoveBadge = null;
            }

            _evalBar.SetEvaluation(postEval);
            _computerLines.SetLines(postEval, newBoard);

            _boardControl.ClearArrows();
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private void OnCalloutCardClicked(int plyIndex)
    {
        OnCalloutTokenClicked(plyIndex, 0);
    }

    private void OnCalloutTokenClicked(int plyIndex, int tokenIndex)
    {
        if (_currentAnalysis == null || plyIndex < 0 || plyIndex >= _currentAnalysis.Moves.Count) return;

        _boardControl.IsAtGameEnd = false;

        // If this variation is ALREADY active for this ply, jump directly to step without reloading
        if (_variationBranchPly == plyIndex - 1 && _activeVariation.Count > 0)
        {
            int targetIdx = Math.Clamp(tokenIndex, 0, _activeVariation.Count - 1);
            _activeVariationIndex = targetIdx;
            DisplayVariationStep(targetIdx);
            UpdateVariationBoxUi();
            return;
        }

        var ma = _currentAnalysis.Moves[plyIndex];
        var pv = ma.PreEval.Pv.Count > 0
            ? ma.PreEval.Pv
            : (!string.IsNullOrEmpty(ma.PreEval.BestMoveUci)
                ? new List<string> { ma.PreEval.BestMoveUci }
                : (ma.BestMove.HasValue && !ma.BestMove.Value.IsEmpty ? new List<string> { ma.BestMove.Value.ToUci() } : new List<string>()));
        if (pv.Count == 0) return;

        var board = new BoardState();
        board.LoadFen(ma.FenBefore);

        _activeVariation.Clear();
        _variationBranchPly = plyIndex - 1;

        int totalPvMoves = Math.Min(8, pv.Count);
        for (int k = 0; k < totalPvMoves; k++)
        {
            string uci = pv[k];
            var mv = Core.Chess.Move.FromUci(uci);
            if (mv.IsEmpty) break;

            string fenBefore = board.ToFen();
            string san = AnalysisService.ConvertUciToSan(fenBefore, uci);
            if (string.IsNullOrEmpty(san)) san = uci;

            // Generate full legal move with IsCapture, IsCastling, IsEnPassant flags
            var legalMoves = board.GenerateLegalMoves();
            var matchedLegalMove = legalMoves.FirstOrDefault(m => m.From == mv.From && m.To == mv.To && m.Promotion == mv.Promotion);
            if (!matchedLegalMove.IsEmpty)
            {
                mv = matchedLegalMove;
            }
            else
            {
                bool isCap = san.Contains('x') || !board[mv.To].IsEmpty || (mv.To == board.EnPassantSquare && board[mv.From].Type == PieceType.Pawn);
                bool isCastle = board[mv.From].Type == PieceType.King && Math.Abs(mv.From.File - mv.To.File) >= 2;
                bool isEp = mv.To == board.EnPassantSquare && board[mv.From].Type == PieceType.Pawn;
                mv = new Move(mv.From, mv.To, mv.Promotion, isCap, isCastle, isEp);
            }

            board.ApplyMove(mv);
            string fenAfter = board.ToFen();

            _activeVariation.Add(new VariationStep
            {
                Move = mv,
                San = san,
                FenBefore = fenBefore,
                FenAfter = fenAfter,
                Classification = (k == 0) ? MoveClassification.Best : null
            });
        }

        if (_activeVariation.Count == 0) return;

        _activeVariationIndex = Math.Clamp(tokenIndex, 0, _activeVariation.Count - 1);
        _currentPlyIndex = plyIndex - 1;

        _moveHistory.SetVariation(_variationBranchPly, _activeVariation.Select(v => v.San));
        UpdateVariationBoxUi();
        DisplayVariationStep(_activeVariationIndex);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Left:
                if (_activeVariation.Count == 0 && _currentPlyIndex <= -1)
                {
                    return true;
                }
                StepBackward();
                return true;
            case Keys.Right:
                if (_activeVariation.Count == 0 && (_currentGame.Moves.Count == 0 || _currentPlyIndex >= _currentGame.Moves.Count - 1))
                {
                    return true;
                }
                StepForward();
                return true;
            case Keys.Home:
                GoToFirst();
                return true;
            case Keys.End:
                GoToLast();
                return true;
            case Keys.F:
                ToggleBoardFlip();
                return true;
            case Keys.Space:
                ToggleAutoPlay();
                return true;
            case Keys.Escape:
                if (_activeVariation.Count > 0)
                {
                    ExitVariation();
                    return true;
                }
                break;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        // Handled globally in ProcessCmdKey
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        StopAutoPlay();
        StopAnalysis();
        _engine?.Dispose();
        base.OnFormClosing(e);
    }
    #endregion

    #region Helpers
    private static Panel CreateDividerLine()
    {
        var line = new Panel
        {
            Height = 1,
            BackColor = Color.FromArgb(50, 48, 45),
            Margin = new Padding(0, 6, 0, 6)
        };
        return line;
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
    #endregion
}
