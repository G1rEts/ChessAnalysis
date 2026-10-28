using System.Drawing;
using System.Drawing.Drawing2D;
using ChessAnalysis.Core.Analysis;
using ChessAnalysis.Core.Chess;
using ChessAnalysis.Core.Engine;
using ChessAnalysis.UI.Controls;
using ChessAnalysis.UI.Forms;
using ChessAnalysis.UI.Resources;

namespace ChessAnalysis;

public partial class Form1 : Form
{
    private StockfishEngine? _engine;
    private AnalysisService? _analysisService;
    private CancellationTokenSource? _analysisCts;

    private PgnGame _currentGame = new();
    private GameAnalysis? _currentAnalysis;
    private int _currentPlyIndex = -1;
    private PieceColor _userColor = PieceColor.White;
    private string _rawPgnText = string.Empty;
    private int _analysisDepth = 14;

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
    private Label _lblGameTitle = null!;
    private RoundedButton _btnLoadPgn = null!;
    private RoundedButton _btnDownloadUser = null!;
    private ProgressBar _progressBar = null!;

    // Full-Width Evaluation Graph (Across Top)
    private GameGraphControl _graphControl = null!;

    // Move Review Variation Banner
    private Panel _pnlVariationBanner = null!;
    private Label _lblVariationTitle = null!;
    private RoundedButton _btnExitVariation = null!;

    // Right Sidebar
    private Panel _sidebarContainer = null!;
    private Panel _sidebarHeader = null!;
    private Label _lblSidebarTitle = null!;
    private RoundedButton _btnBackToOverview = null!;
    private RoundedButton _btnShowOverview = null!;
    private RoundedButton _btnQuickAnalyze = null!;
    private RoundedButton _btnSettingsGear = null!;
    private ContextMenuStrip _menuSettings = null!;

    // View A: Overview Panel (Report)
    private Panel _pnlOverview = null!;
    private Label _lblOverviewWhitePlayer = null!;
    private Label _lblOverviewBlackPlayer = null!;
    private Label _lblWhiteAccuracy = null!;
    private Label _lblBlackAccuracy = null!;
    private Label _lblWhiteElo = null!;
    private Label _lblBlackElo = null!;
    private TableLayoutPanel _tableBadges = null!;
    private RoundedButton _btnStartReview = null!;

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
    private readonly List<string> _variationMoves = new();

    public Form1()
    {
        InitializeComponent();
        SetupCustomUi();
        SoundService.Initialize();
        Task.Run(() => OpeningBook.Initialize());
        InitializeEngine();

        // Start with clean empty board position
        _boardControl.Board = new BoardState();
        _evalBar.Reset();
        _lblGameTitle.Text = "Загрузите партию";
        UpdatePlayerBarLabels("Белые", "Черные");
        _lblTopClock.Text = "";
        _lblBottomClock.Text = "";
        SwitchSidebarView("review");
    }

    private void SetupCustomUi()
    {
        Text = "Chess Analysis (Stockfish 19) — by Antigravity";
        Icon = ResourceManager.GetAppIcon();
        Size = new Size(1360, 880);
        MinimumSize = new Size(1100, 720);
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

        // 1. Top Bar Panel (Header with 2 action buttons)
        var topBar = CreateTopBar();

        // 2. Full-Width Evaluation Graph
        _graphControl = new GameGraphControl
        {
            Dock = DockStyle.Top,
            Height = 72,
            BackColor = ChessTheme.PanelDarker
        };
        _graphControl.PlySelected += ply =>
        {
            GoToPly(ply);
            SwitchSidebarView("review");
        };

        // 3. Main Area (Left = Board Viewport, Right = Sidebar)
        var mainArea = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = ChessTheme.BackgroundDark
        };

        // Right Sidebar (Width 490px per Phase 6)
        _sidebarContainer = CreateRightSidebar();
        _sidebarContainer.Dock = DockStyle.Right;

        // Left Board Viewport (Fills remaining space)
        var boardViewport = CreateBoardViewport();
        boardViewport.Dock = DockStyle.Fill;

        mainArea.Controls.Add(boardViewport);
        mainArea.Controls.Add(_sidebarContainer);

        // WinForms Dock Order: mainArea (Fill) -> graph (Top) -> topBar (Top)
        Controls.Add(mainArea);
        Controls.Add(_graphControl);
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

        // Logo Title on Left
        _lblLogoTitle = new Label
        {
            Text = "♔ ChessAnalysis",
            Font = new Font("Segoe UI", 12f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Dock = DockStyle.Left,
            TextAlign = ContentAlignment.MiddleLeft,
            Cursor = Cursors.Hand
        };
        _lblLogoTitle.Click += (s, e) => SwitchSidebarView("review");

        // Game Match / Opening Title
        _lblGameTitle = new Label
        {
            Text = "Загрузите партию",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(190, 190, 190),
            AutoSize = true,
            Dock = DockStyle.Left,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(16, 0, 0, 0)
        };

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
            Width = 140,
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
            Width = 140,
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
        topBar.Controls.Add(_lblGameTitle);
        topBar.Controls.Add(_lblLogoTitle);
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
        int padX = 16;
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

        int startX = padX + Math.Max(0, (availW - totalW) / 2);
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
            AutoSize = true
        };

        var capturedCtrl = new CapturedPiecesControl
        {
            Height = 24,
            Width = 220
        };

        var clockPill = new Panel
        {
            Size = new Size(76, 28),
            BackColor = ChessTheme.PanelDarker
        };
        clockPill.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = CreateRoundedPath(new Rectangle(0, 0, clockPill.Width - 1, clockPill.Height - 1), 6);
            using var bg = new SolidBrush(ChessTheme.PanelDarker);
            using var pen = new Pen(Color.FromArgb(70, 68, 65), 1.2f);
            e.Graphics.FillPath(bg, path);
            e.Graphics.DrawPath(pen, path);
        };

        var lblClock = new Label
        {
            Text = "",
            Font = new Font("Consolas", 10.5f, FontStyle.Bold),
            ForeColor = Color.White,
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
            lblPlayer.Location = new Point(36, (bar.Height - lblPlayer.Height) / 2);
            capturedCtrl.Location = new Point(lblPlayer.Right + 8, (bar.Height - capturedCtrl.Height) / 2);
            clockPill.Location = new Point(Math.Max(capturedCtrl.Right + 8, bar.Width - clockPill.Width), (bar.Height - clockPill.Height) / 2);
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
            Width = 490,
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
            Width = 86,
            Height = 30,
            BorderRadius = 5,
            NormalColor = Color.FromArgb(48, 46, 43),
            HoverColor = Color.FromArgb(62, 60, 56),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            Dock = DockStyle.Left,
            Visible = false
        };
        _btnBackToOverview.Click += (s, e) => SwitchSidebarView("review");

        _btnQuickAnalyze = new RoundedButton
        {
            Text = "🔍",
            Width = 32,
            Height = 30,
            BorderRadius = 6,
            NormalColor = ChessTheme.AccentGreen,
            HoverColor = ChessTheme.AccentGreenHover,
            PressedColor = Color.FromArgb(100, 150, 55),
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            ForeColor = Color.White,
            Dock = DockStyle.Left,
            Margin = new Padding(0, 0, 8, 0)
        };
        var ttAnalyze = new ToolTip();
        ttAnalyze.SetToolTip(_btnQuickAnalyze, "Запустить анализ партии движком Stockfish");
        _btnQuickAnalyze.Click += async (s, e) => await StartAnalysisAsync();

        _btnShowOverview = new RoundedButton
        {
            Text = "⭐ Отчет",
            Width = 74,
            Height = 30,
            BorderRadius = 5,
            NormalColor = Color.FromArgb(48, 46, 43),
            HoverColor = Color.FromArgb(62, 60, 56),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            Dock = DockStyle.Left,
            Margin = new Padding(6, 0, 0, 0)
        };
        _btnShowOverview.Click += (s, e) => SwitchSidebarView("overview");

        _btnSettingsGear = new RoundedButton
        {
            Text = "⚙",
            Width = 32,
            Height = 30,
            BorderRadius = 6,
            NormalColor = Color.FromArgb(48, 46, 43),
            HoverColor = Color.FromArgb(62, 60, 56),
            PressedColor = Color.FromArgb(75, 73, 68),
            Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
            ForeColor = Color.White,
            Dock = DockStyle.Right
        };
        var ttGear = new ToolTip();
        ttGear.SetToolTip(_btnSettingsGear, "Настройки, звук, стрелки и PGN");
        _btnSettingsGear.Click += (s, e) =>
        {
            _menuSettings.Show(_btnSettingsGear, new Point(0, _btnSettingsGear.Height));
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
        _sidebarHeader.Controls.Add(_btnQuickAnalyze);
        _sidebarHeader.Controls.Add(_btnBackToOverview);
        _sidebarHeader.Controls.Add(_btnSettingsGear);

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
            _btnQuickAnalyze.Visible = false;
            _btnShowOverview.Visible = false;
            _pnlOverview.BringToFront();
        }
        else
        {
            _lblSidebarTitle.Text = "Таблица ходов";
            _btnBackToOverview.Visible = false;
            _btnQuickAnalyze.Visible = true;
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

        var mCopyPgn = new ToolStripMenuItem("📋  Скопировать PGN");
        mCopyPgn.Click += (s, e) => CopyPgnToClipboard();

        var mFlip = new ToolStripMenuItem("🔄  Перевернуть доску");
        mFlip.Click += (s, e) => ToggleBoardFlip();

        var mArrows = new ToolStripMenuItem("↗️  Стрелки подсказок") { CheckOnClick = true, Checked = true };
        mArrows.CheckedChanged += (s, e) =>
        {
            _boardControl.ShowArrows = mArrows.Checked;
            _boardControl.Invalidate();
        };

        var mSound = new ToolStripMenuItem("🔊  Звук ходов") { CheckOnClick = true, Checked = true };
        mSound.CheckedChanged += (s, e) =>
        {
            SoundService.ToggleMute();
            mSound.Checked = !SoundService.Muted;
        };

        var mDepth = new ToolStripMenuItem("📊  Глубина анализа Stockfish");
        int[] depths = [10, 12, 14, 16, 18, 20];
        foreach (int d in depths)
        {
            var item = new ToolStripMenuItem($"Глубина {d}" + (d == _analysisDepth ? " (текущая)" : ""))
            {
                Checked = (d == _analysisDepth)
            };
            int sel = d;
            item.Click += (s, e) =>
            {
                _analysisDepth = sel;
                foreach (ToolStripMenuItem mi in mDepth.DropDownItems)
                {
                    mi.Checked = false;
                }
                item.Checked = true;
            };
            mDepth.DropDownItems.Add(item);
        }

        var mBrowse = new ToolStripMenuItem("⚙  Указать файл stockfish.exe...");
        mBrowse.Click += (s, e) => BrowseEngineFile();

        menu.Items.Add(mCopyPgn);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(mFlip);
        menu.Items.Add(mArrows);
        menu.Items.Add(mSound);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(mDepth);
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
            Padding = new Padding(14, 8, 14, 8),
            BackColor = ChessTheme.PanelDark
        };

        // Players & Accuracy Section
        var pnlPlayers = new Panel
        {
            Dock = DockStyle.Top,
            Height = 78,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 4, 0, 8)
        };

        var lblRowPlayers = new Label
        {
            Text = "Шахматисты",
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = ChessTheme.TextSecondary,
            Location = new Point(0, 4),
            AutoSize = true
        };

        _lblOverviewWhitePlayer = new Label
        {
            Text = "Белые",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(145, 4),
            Size = new Size(110, 20),
            TextAlign = ContentAlignment.MiddleCenter
        };

        _lblOverviewBlackPlayer = new Label
        {
            Text = "Черные",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(275, 4),
            Size = new Size(110, 20),
            TextAlign = ContentAlignment.MiddleCenter
        };

        pnlPlayers.Controls.Add(lblRowPlayers);
        pnlPlayers.Controls.Add(_lblOverviewWhitePlayer);
        pnlPlayers.Controls.Add(_lblOverviewBlackPlayer);

        var lblAccuracyTitle = new Label
        {
            Text = "Точность",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(0, 36),
            AutoSize = true
        };

        var whiteAccCard = CreateWhiteScoreCard(out _lblWhiteAccuracy);
        whiteAccCard.Location = new Point(155, 30);
        whiteAccCard.Size = new Size(95, 38);

        var blackAccCard = CreateWhiteScoreCard(out _lblBlackAccuracy);
        blackAccCard.Location = new Point(285, 30);
        blackAccCard.Size = new Size(95, 38);

        pnlPlayers.Controls.Add(lblAccuracyTitle);
        pnlPlayers.Controls.Add(whiteAccCard);
        pnlPlayers.Controls.Add(blackAccCard);

        var div1 = CreateDividerLine();
        div1.Dock = DockStyle.Top;

        // Badge Comparison Table (10 categories)
        _tableBadges = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 340,
            ColumnCount = 4,
            RowCount = 10,
            BackColor = ChessTheme.PanelDark,
            Padding = new Padding(0, 4, 0, 4)
        };
        _tableBadges.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));
        _tableBadges.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18f));
        _tableBadges.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22f));
        _tableBadges.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18f));

        BuildBadgeTableRows();

        var div2 = CreateDividerLine();
        div2.Dock = DockStyle.Top;

        // Estimated ELO Row
        var pnlElo = new Panel
        {
            Dock = DockStyle.Top,
            Height = 60,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 8, 0, 8)
        };

        var lblEloTitle = new Label
        {
            Text = "Рейтинг",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.White,
            Location = new Point(0, 16),
            AutoSize = true
        };

        var whiteEloCard = CreateWhiteScoreCard(out _lblWhiteElo);
        whiteEloCard.Location = new Point(155, 10);
        whiteEloCard.Size = new Size(95, 36);

        var blackEloCard = CreateWhiteScoreCard(out _lblBlackElo);
        blackEloCard.Location = new Point(285, 10);
        blackEloCard.Size = new Size(95, 36);

        pnlElo.Controls.Add(lblEloTitle);
        pnlElo.Controls.Add(whiteEloCard);
        pnlElo.Controls.Add(blackEloCard);

        // Bottom Green Button ("Смотреть ходы")
        var pnlBottomBtn = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 60,
            BackColor = ChessTheme.PanelDark,
            Padding = new Padding(14, 6, 14, 8)
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
            GoToPly(_currentPlyIndex >= 0 ? _currentPlyIndex : 0);
        };
        pnlBottomBtn.Controls.Add(_btnStartReview);

        scrollBody.Controls.Add(pnlElo);
        scrollBody.Controls.Add(div2);
        scrollBody.Controls.Add(_tableBadges);
        scrollBody.Controls.Add(div1);
        scrollBody.Controls.Add(pnlPlayers);

        pnl.Controls.Add(scrollBody);
        pnl.Controls.Add(pnlBottomBtn);

        return pnl;
    }

    private Panel CreateWhiteScoreCard(out Label lblScore)
    {
        var card = new Panel
        {
            BackColor = Color.White
        };
        card.Paint += (s, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = CreateRoundedPath(new Rectangle(0, 0, card.Width - 1, card.Height - 1), 6);
            using var bg = new SolidBrush(Color.White);
            e.Graphics.FillPath(bg, path);
        };

        lblScore = new Label
        {
            Text = "--.-",
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            ForeColor = Color.FromArgb(33, 32, 29),
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
            _tableBadges.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));

            string title = c switch
            {
                MoveClassification.Good => "Хороший",
                MoveClassification.MissedWin => "Упущенный шанс",
                _ => c.GetTitle()
            };

            var lblName = new Label
            {
                Text = title,
                ForeColor = Color.FromArgb(220, 218, 215),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            var lblW = new Label
            {
                Text = "0",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Name = $"badge_w_{c}"
            };

            var picBadge = new PictureBox
            {
                Size = new Size(32, 32),
                SizeMode = PictureBoxSizeMode.Zoom,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 1, 0, 1),
                Image = ResourceManager.GetBadgeImage(c)
            };

            var lblB = new Label
            {
                Text = "0",
                ForeColor = Color.White,
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

        // Variation Banner
        _pnlVariationBanner = new Panel
        {
            Dock = DockStyle.Top,
            Height = 36,
            BackColor = Color.FromArgb(32, 45, 60),
            Padding = new Padding(10, 4, 10, 4),
            Visible = false
        };

        _lblVariationTitle = new Label
        {
            Text = "Вариант: ...",
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = Color.FromArgb(170, 215, 255),
            Dock = DockStyle.Left,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = true
        };

        _btnExitVariation = new RoundedButton
        {
            Text = "Вернуться в партию",
            Width = 145,
            Height = 28,
            BorderRadius = 5,
            NormalColor = Color.FromArgb(48, 56, 68),
            HoverColor = Color.FromArgb(64, 75, 92),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            Dock = DockStyle.Right
        };
        _btnExitVariation.Click += (s, e) => ExitVariation();

        _pnlVariationBanner.Controls.Add(_lblVariationTitle);
        _pnlVariationBanner.Controls.Add(_btnExitVariation);

        // Move History Table (Fills ALL available space, 15-16 visible rows)
        _moveHistory = new MoveHistoryControl
        {
            Dock = DockStyle.Fill
        };
        _moveHistory.MoveSelected += ply => GoToPly(ply);
        _moveHistory.MoveHovered += ply =>
        {
            if (ply >= 0 && _currentAnalysis != null && ply < _currentAnalysis.Moves.Count)
            {
                var ma = _currentAnalysis.Moves[ply];
                _boardControl.ClearArrows();
                _boardControl.AddArrow(ma.Move.From, ma.Move.To, Color.FromArgb(240, 255, 255, 255), width: 7f, hasOutline: true);
                if (ma.BestMove.HasValue && ma.BestMove.Value != ma.Move)
                {
                    _boardControl.AddArrow(ma.BestMove.Value.From, ma.BestMove.Value.To, Color.FromArgb(210, 28, 28, 32), width: 8f);
                }
            }
            else if (ply == -1)
            {
                RestoreCurrentPlyArrows();
            }
        };

        // Bottom Playback Controls Bar (Phase 7: Enlarged 70px x 48px buttons)
        var pnlPlayback = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 62,
            BackColor = ChessTheme.PanelDarker,
            Padding = new Padding(10, 7, 10, 7)
        };

        _btnFirst = CreateNavButton("|<", (s, e) => GoToPly(-1), 70);
        _btnPrev = CreateNavButton("<", (s, e) => GoToPly(_currentPlyIndex - 1), 70);
        _btnAutoPlay = CreateNavButton("▶", (s, e) => ToggleAutoPlay(), 80);
        _btnNext = CreateNavButton(">", (s, e) => GoToPly(_currentPlyIndex + 1), 70);
        _btnLast = CreateNavButton(">|", (s, e) => GoToPly(_currentGame.Moves.Count - 1), 70);

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
            int totalBtnW = (70 * 4) + 80 + (8 * 4);
            int pad = Math.Max(0, (pnlPlayback.Width - totalBtnW) / 2);
            flowNav.Padding = new Padding(pad, 0, 0, 0);
        };

        pnlPlayback.Controls.Add(flowNav);

        pnl.Controls.Add(_pnlVariationBanner);
        pnl.Controls.Add(pnlPlayback);
        pnl.Controls.Add(_moveHistory);
        _moveHistory.BringToFront();

        return pnl;
    }

    private static RoundedButton CreateNavButton(string text, EventHandler onClick, int width)
    {
        var btn = new RoundedButton
        {
            Text = text,
            Width = width,
            Height = 48,
            BorderRadius = 10,
            NormalColor = Color.FromArgb(48, 46, 43),
            HoverColor = Color.FromArgb(64, 62, 58),
            PressedColor = Color.FromArgb(80, 78, 72),
            Font = new Font("Segoe UI", 13.5f, FontStyle.Bold),
            ForeColor = Color.White,
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
            string? enginePath = StockfishEngine.FindStockfishPath();
            if (!string.IsNullOrEmpty(enginePath) && File.Exists(enginePath))
            {
                _engine = new StockfishEngine(enginePath);
                _analysisService = new AnalysisService(_engine);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Ошибка инициализации Stockfish:\n" + ex.Message, "Stockfish Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    public void LoadPgnFromText(string pgnText)
    {
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

        string op = game.Headers.TryGetValue("Opening", out var openingHeader) && !string.IsNullOrWhiteSpace(openingHeader) ? $" • {openingHeader}" : "";
        _lblGameTitle.Text = $"{wPlayer} — {bPlayer}{op}";

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
                Classification = MoveClassification.Good
            });
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
    private void GoToPly(int plyIndex)
    {
        _variationCts?.Cancel();
        _variationMoves.Clear();
        _moveHistory.ClearVariation();

        if (_currentGame.Moves.Count == 0)
        {
            _boardControl.Board = new BoardState();
            _boardControl.LastMoveFrom = Square.None;
            _boardControl.LastMoveTo = Square.None;
            _boardControl.LastMoveBadge = null;
            _evalBar.Reset();
            return;
        }

        if (_variationMoves.Count > 0)
        {
            _variationCts?.Cancel();
            _variationMoves.Clear();
            _pnlVariationBanner.Visible = false;
            _moveHistory.ClearVariation();
        }

        plyIndex = Math.Clamp(plyIndex, -1, _currentGame.Moves.Count - 1);
        _currentPlyIndex = plyIndex;

        _boardControl.ClearArrows();

        if (plyIndex == -1)
        {
            var startBoard = new BoardState();
            _boardControl.SetPosition(startBoard, Square.None, Square.None, null, animate: false);
            _evalBar.Reset();

            _graphControl.SelectedPly = -1;
            UpdateCapturedPieces(startBoard);
            return;
        }

        var move = _currentGame.Moves[plyIndex];
        var board = new BoardState();
        board.LoadFen(move.FenAfter);

        // Sound feedback
        if (move.San.Contains('x'))
        {
            SoundService.PlayCapture();
        }
        else
        {
            SoundService.PlayMove();
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

        MoveClassification? badge = null;
        if (_currentAnalysis != null && plyIndex < _currentAnalysis.Moves.Count)
        {
            var ma = _currentAnalysis.Moves[plyIndex];
            badge = ma.Classification;
            _evalBar.SetEvaluation(ma.PostEval);

            RestoreCurrentPlyArrows();
        }

        _boardControl.SetPosition(board, move.Move.From, move.Move.To, badge, animate: true);
        _moveHistory.SelectPly(plyIndex);
        _graphControl.SelectedPly = plyIndex;
        UpdateCapturedPieces(board);
    }

    private void RestoreCurrentPlyArrows()
    {
        _boardControl.ClearArrows();
        if (_currentAnalysis == null || _currentPlyIndex < 0 || _currentPlyIndex >= _currentAnalysis.Moves.Count) return;

        var ma = _currentAnalysis.Moves[_currentPlyIndex];
        if (ma.Classification is MoveClassification.Blunder or MoveClassification.Mistake or MoveClassification.Inaccuracy or MoveClassification.MissedWin)
        {
            if (ma.ThreatMove.HasValue && !ma.ThreatMove.Value.IsEmpty)
            {
                _boardControl.AddArrow(ma.ThreatMove.Value.From, ma.ThreatMove.Value.To, Color.FromArgb(220, 231, 76, 60), width: 7f, hasOutline: true);
            }
        }
        else if (ma.Classification is MoveClassification.Brilliant or MoveClassification.Great or MoveClassification.Best)
        {
            if (ma.ThreatMove.HasValue && !ma.ThreatMove.Value.IsEmpty)
            {
                _boardControl.AddArrow(ma.ThreatMove.Value.From, ma.ThreatMove.Value.To, Color.FromArgb(200, 129, 182, 76), width: 6.5f, hasOutline: true);
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

        _btnQuickAnalyze.Enabled = false;
        _progressBar.Visible = true;
        _progressBar.Value = 0;

        _analysisCts = new CancellationTokenSource();
        int depth = _analysisDepth;

        var progress = new Progress<AnalysisProgress>(p =>
        {
            _progressBar.Value = Math.Clamp((int)p.Percent, 0, 100);
            if (p.LatestAnalyzedMove != null)
            {
                _graphControl.AddMove(p.LatestAnalyzedMove);
                if (p.CurrentPly - 1 == _currentPlyIndex)
                {
                    GoToPly(_currentPlyIndex);
                }
            }
        });

        try
        {
            _graphControl.Clear();

            var analysis = await _analysisService.AnalyzeGameAsync(_currentGame, depth, progress, _analysisCts.Token);
            _currentAnalysis = analysis;

            // Update UI with complete review
            _lblWhiteAccuracy.Text = $"{analysis.WhiteAccuracy:0.0}";
            _lblBlackAccuracy.Text = $"{analysis.BlackAccuracy:0.0}";
            _lblWhiteElo.Text = analysis.WhiteEstimatedElo.ToString();
            _lblBlackElo.Text = analysis.BlackEstimatedElo.ToString();

            _moveHistory.SetMoves(analysis.Moves);
            _graphControl.SetMoves(analysis.Moves);

            UpdateBadgeCountLabels(analysis);

            GoToPly(_currentPlyIndex >= 0 ? _currentPlyIndex : 0);
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
            _btnQuickAnalyze.Enabled = true;
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
                lblW.ForeColor = countW > 0 ? Color.FromArgb(240, 240, 240) : Color.FromArgb(120, 118, 115);
            }

            var controlsB = _tableBadges.Controls.Find($"badge_b_{c}", true);
            if (controlsB.Length > 0 && controlsB[0] is Label lblB)
            {
                int countB = analysis.BlackBadgeCounts[c];
                lblB.Text = countB.ToString();
                lblB.ForeColor = countB > 0 ? Color.FromArgb(240, 240, 240) : Color.FromArgb(120, 118, 115);
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
        if (!string.IsNullOrWhiteSpace(_rawPgnText))
        {
            Clipboard.SetText(_rawPgnText);
            MessageBox.Show("Текст партии (PGN) скопирован в буфер обмена.", "PGN скопирован", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show("Партия ещё не загружена.", "Информация", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void BrowseEngineFile()
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
            Title = "Укажите файл stockfish.exe"
        };
        if (ofd.ShowDialog(this) == DialogResult.OK)
        {
            _engine?.Dispose();
            _engine = new StockfishEngine(ofd.FileName);
            _analysisService = new AnalysisService(_engine);
            MessageBox.Show($"Движок успешно подключён:\n{Path.GetFileName(ofd.FileName)}", "Stockfish", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            _btnAutoPlay.Text = "⏸";
            _btnAutoPlay.NormalColor = ChessTheme.AccentBlue;
            _autoPlayTimer.Start();
        }
    }

    private void StopAutoPlay()
    {
        _autoPlayTimer.Stop();
        _btnAutoPlay.Text = "▶";
        _btnAutoPlay.NormalColor = Color.FromArgb(48, 46, 43);
    }

    private async void OnBoardMovePlayed(Move move)
    {
        var curBoard = _boardControl.Board;
        bool isCapture = !curBoard[move.To].IsEmpty || (move.To == curBoard.EnPassantSquare && curBoard[move.From].Type == PieceType.Pawn);

        if (isCapture)
        {
            SoundService.PlayCapture();
        }
        else
        {
            SoundService.PlayMove();
        }

        string fenBefore = curBoard.ToFen();
        string uci = move.ToUci();
        string san = AnalysisService.ConvertUciToSan(fenBefore, uci);
        if (string.IsNullOrEmpty(san)) san = uci;

        var newBoard = curBoard.Clone();
        newBoard.ApplyMove(move);
        _boardControl.SetPosition(newBoard, move.From, move.To, null);
        UpdateCapturedPieces(newBoard);

        _variationMoves.Add(san);
        _moveHistory.SetVariation(_currentPlyIndex, _variationMoves);

        _pnlVariationBanner.Visible = true;
        _lblVariationTitle.Text = $"Вариант: {string.Join(" ", _variationMoves)}";

        _variationCts?.Cancel();
        _variationCts = new CancellationTokenSource();
        var ct = _variationCts.Token;

        if (_engine == null || !_engine.IsRunning) return;

        try
        {
            var preEval = await _engine.AnalyzePositionAsync(fenBefore, depth: null, nodes: 600000, ct: ct);
            if (ct.IsCancellationRequested) return;

            var postEval = await _engine.AnalyzePositionAsync(newBoard.ToFen(), depth: null, nodes: 600000, ct: ct);
            if (ct.IsCancellationRequested) return;

            bool isTop = string.Equals(uci, preEval.BestMoveUci, StringComparison.OrdinalIgnoreCase);

            double preWin = curBoard.SideToMove == PieceColor.White ? preEval.WinProbabilityWhite : (100.0 - preEval.WinProbabilityWhite);
            double postWin = curBoard.SideToMove == PieceColor.White ? postEval.WinProbabilityWhite : (100.0 - postEval.WinProbabilityWhite);
            double deltaP = Math.Max(0.0, preWin - postWin);

            MoveClassification classification;
            if (isTop || deltaP < 1.0) classification = MoveClassification.Best;
            else if (deltaP <= 5.0) classification = MoveClassification.Excellent;
            else if (deltaP <= 10.0) classification = MoveClassification.Good;
            else if (deltaP <= 20.0) classification = MoveClassification.Inaccuracy;
            else if (deltaP <= 30.0) classification = MoveClassification.Mistake;
            else classification = MoveClassification.Blunder;

            _evalBar.SetEvaluation(postEval);
            _boardControl.SetPosition(newBoard, move.From, move.To, classification);

            _boardControl.ClearArrows();
            if (!string.IsNullOrEmpty(postEval.BestMoveUci))
            {
                var bm = Core.Chess.Move.FromUci(postEval.BestMoveUci);
                if (!bm.IsEmpty)
                {
                    _boardControl.AddArrow(bm.From, bm.To, Color.FromArgb(200, 129, 182, 76), width: 7f, hasOutline: true);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch
        {
            // Ignored
        }
    }

    private void ExitVariation()
    {
        _variationCts?.Cancel();
        _variationMoves.Clear();
        _pnlVariationBanner.Visible = false;
        _moveHistory.ClearVariation();
        if (_currentGame.Moves.Count > 0 && _currentPlyIndex >= 0 && _currentPlyIndex < _currentGame.Moves.Count)
        {
            GoToPly(_currentPlyIndex);
        }
        else
        {
            GoToPly(-1);
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Left:
                GoToPly(_currentPlyIndex - 1);
                return true;
            case Keys.Right:
                GoToPly(_currentPlyIndex + 1);
                return true;
            case Keys.Home:
                GoToPly(-1);
                return true;
            case Keys.End:
                GoToPly(_currentGame.Moves.Count - 1);
                return true;
            case Keys.F:
                ToggleBoardFlip();
                return true;
            case Keys.Space:
                ToggleAutoPlay();
                return true;
            case Keys.Escape:
                if (_variationMoves.Count > 0)
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
