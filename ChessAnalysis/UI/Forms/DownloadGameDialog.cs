using System.Drawing;
using ChessAnalysis.Core.Network;
using ChessAnalysis.UI.Controls;
using ChessAnalysis.UI.Resources;

namespace ChessAnalysis.UI.Forms;

public class DownloadGameDialog : Form
{
    private readonly TextBox _txtUsername;
    private readonly ComboBox _comboPlatform;
    private readonly RoundedButton _btnSearch;
    private readonly ListBox _listGames;
    private readonly RoundedButton _btnLoadMore;
    private readonly RoundedButton _btnSelect;
    private readonly RoundedButton _btnCancel;
    private readonly Label _lblStatus;
    private readonly Panel _bottomPanel;
    private readonly GameDownloadService _downloadService = new();

    private List<DownloadedGameSummary> _games = new();
    public string? SelectedPgn { get; private set; }
    public string? GameTitle { get; private set; }

    public DownloadGameDialog()
    {
        Text = "Скачать партии по никнейму (Chess.com / Lichess)";
        Size = new Size(860, 530);
        MinimumSize = new Size(750, 420);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = ChessTheme.BackgroundDark;
        ForeColor = ChessTheme.TextPrimary;
        Font = new Font("Segoe UI", 9.5f);

        // Header Panel
        var topPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 85,
            Padding = new Padding(12),
            BackColor = ChessTheme.PanelDark
        };

        var lblPlatform = new Label
        {
            Text = "Платформа:",
            Location = new Point(12, 12),
            AutoSize = true,
            ForeColor = ChessTheme.TextSecondary
        };

        _comboPlatform = new ComboBox
        {
            Location = new Point(12, 34),
            Width = 140,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = ChessTheme.PanelDarker,
            ForeColor = ChessTheme.TextPrimary,
            FlatStyle = FlatStyle.Flat
        };
        _comboPlatform.Items.AddRange(new object[] { "Chess.com", "Lichess" });
        _comboPlatform.SelectedIndex = 0;

        var lblUser = new Label
        {
            Text = "Никнейм игрока:",
            Location = new Point(165, 12),
            AutoSize = true,
            ForeColor = ChessTheme.TextSecondary
        };

        _txtUsername = new TextBox
        {
            Location = new Point(165, 34),
            Width = 260,
            BackColor = ChessTheme.PanelDarker,
            ForeColor = ChessTheme.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 10.5f)
        };
        _txtUsername.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                _btnSearch?.PerformClick();
            }
        };

        _btnSearch = new RoundedButton
        {
            Text = "🔍 Найти партии",
            Location = new Point(440, 31),
            Width = 150,
            Height = 34,
            BorderRadius = 6,
            NormalColor = ChessTheme.AccentBlue,
            HoverColor = Color.FromArgb(105, 160, 255),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold)
        };
        _btnSearch.Click += async (s, e) => await SearchGamesAsync();

        topPanel.Controls.AddRange(new Control[] { lblPlatform, _comboPlatform, lblUser, _txtUsername, _btnSearch });

        // Center List Box
        _listGames = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = ChessTheme.PanelDarker,
            ForeColor = ChessTheme.TextPrimary,
            BorderStyle = BorderStyle.None,
            ItemHeight = 28,
            DrawMode = DrawMode.OwnerDrawFixed,
            Font = new Font("Segoe UI", 9.5f)
        };
        _listGames.DrawItem += DrawGameListItem;
        _listGames.DoubleClick += (s, e) => SelectGame();

        // Bottom Panel
        _bottomPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 55,
            Padding = new Padding(12),
            BackColor = ChessTheme.PanelDark
        };

        _lblStatus = new Label
        {
            Text = "Введите ник и нажмите «Найти партии»",
            Location = new Point(12, 10),
            Size = new Size(240, 35),
            AutoSize = false,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = ChessTheme.TextSecondary
        };

        _btnLoadMore = new RoundedButton
        {
            Text = "📥 Догрузить 25 партий",
            Width = 190,
            Height = 34,
            BorderRadius = 6,
            NormalColor = Color.FromArgb(48, 46, 43),
            HoverColor = Color.FromArgb(62, 60, 56),
            CustomBorderColor = Color.FromArgb(75, 73, 68),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Visible = false
        };
        _btnLoadMore.Click += async (s, e) => await LoadMoreGamesAsync();

        _btnCancel = new RoundedButton
        {
            Text = "Отмена",
            Width = 90,
            Height = 34,
            BorderRadius = 6,
            NormalColor = Color.FromArgb(48, 46, 43),
            HoverColor = Color.FromArgb(62, 60, 56),
            Font = new Font("Segoe UI", 9f, FontStyle.Regular)
        };
        _btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        _btnSelect = new RoundedButton
        {
            Text = "Открыть для анализа",
            Width = 230,
            Height = 34,
            BorderRadius = 6,
            NormalColor = ChessTheme.AccentGreen,
            HoverColor = ChessTheme.AccentGreenHover,
            Font = new Font("Segoe UI", 9.2f, FontStyle.Bold),
            Enabled = false
        };
        _btnSelect.Click += (s, e) => SelectGame();

        _bottomPanel.Resize += (s, e) => LayoutBottomButtons();

        _bottomPanel.Controls.Add(_lblStatus);
        _bottomPanel.Controls.Add(_btnLoadMore);
        _bottomPanel.Controls.Add(_btnSelect);
        _bottomPanel.Controls.Add(_btnCancel);
        _btnLoadMore.BringToFront();
        _btnSelect.BringToFront();
        _btnCancel.BringToFront();
        LayoutBottomButtons();

        Controls.Add(_listGames);
        Controls.Add(topPanel);
        Controls.Add(_bottomPanel);
    }

    private void LayoutBottomButtons()
    {
        _btnCancel.Location = new Point(_bottomPanel.ClientSize.Width - _btnCancel.Width - 16, 10);
        _btnSelect.Location = new Point(_btnCancel.Left - _btnSelect.Width - 10, 10);
        _btnLoadMore.Location = new Point(_btnSelect.Left - _btnLoadMore.Width - 10, 10);
        int maxStatusW = (_btnLoadMore.Visible ? _btnLoadMore.Left : _btnSelect.Left) - 24;
        _lblStatus.Width = Math.Max(50, maxStatusW);
    }

    private async Task SearchGamesAsync()
    {
        string username = _txtUsername.Text.Trim();
        if (string.IsNullOrEmpty(username))
        {
            username = "G1rEts";
            _txtUsername.Text = "G1rEts";
        }

        _btnSearch.Enabled = false;
        _btnSelect.Enabled = false;
        _lblStatus.Text = "Загрузка партий из API...";
        _listGames.Items.Clear();
        _games.Clear();

        try
        {
            bool isChessCom = _comboPlatform.SelectedIndex == 0;
            if (isChessCom)
            {
                _games = await _downloadService.FetchChessComGamesAsync(username, 25);
            }
            else
            {
                _games = await _downloadService.FetchLichessGamesAsync(username, 25);
            }

            if (_games.Count == 0)
            {
                _lblStatus.Text = $"Партий для '{username}' не найдено.";
                _btnLoadMore.Visible = false;
            }
            else
            {
                _lblStatus.Text = $"Найдено партий: {_games.Count}. Выберите нужную.";
                foreach (var g in _games)
                {
                    _listGames.Items.Add(g.DisplayTitle);
                }
                _listGames.SelectedIndex = 0;
                _btnSelect.Enabled = true;
                _btnLoadMore.Visible = true;
                _btnLoadMore.Enabled = true;
            }
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "Ошибка: " + ex.Message;
            MessageBox.Show($"Не удалось получить партии:\n{ex.Message}", "Ошибка загрузки", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnSearch.Enabled = true;
            _btnLoadMore.BringToFront();
            LayoutBottomButtons();
        }
    }

    private async Task LoadMoreGamesAsync()
    {
        string username = _txtUsername.Text.Trim();
        if (string.IsNullOrEmpty(username)) return;

        _btnLoadMore.Enabled = false;
        _lblStatus.Text = "Загрузка следующей порции партий...";

        try
        {
            bool isChessCom = _comboPlatform.SelectedIndex == 0;
            int skip = _games.Count;
            List<DownloadedGameSummary> moreGames;
            if (isChessCom)
            {
                moreGames = await _downloadService.FetchChessComGamesAsync(username, 25, skip: skip);
            }
            else
            {
                moreGames = await _downloadService.FetchLichessGamesAsync(username, 25, skip: skip);
            }

            if (moreGames.Count == 0)
            {
                _lblStatus.Text = $"Все партии загружены (всего: {_games.Count}).";
                _btnLoadMore.Visible = false;
            }
            else
            {
                _games.AddRange(moreGames);
                foreach (var g in moreGames)
                {
                    _listGames.Items.Add(g.DisplayTitle);
                }
                _lblStatus.Text = $"Загружено партий: {_games.Count}. Выберите нужную.";
                _btnLoadMore.Visible = true;
                _btnLoadMore.Enabled = true;
            }
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "Ошибка: " + ex.Message;
        }
        finally
        {
            _btnLoadMore.BringToFront();
            LayoutBottomButtons();
            if (_btnLoadMore.Visible) _btnLoadMore.Enabled = true;
        }
    }

    private void SelectGame()
    {
        int idx = _listGames.SelectedIndex;
        if (idx >= 0 && idx < _games.Count)
        {
            SelectedPgn = _games[idx].PgnText;
            GameTitle = _games[idx].DisplayTitle;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    private void DrawGameListItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _games.Count) return;

        bool isSelected = (e.State & DrawItemState.Selected) != 0;
        using var bgBrush = new SolidBrush(isSelected ? ChessTheme.AccentBlue : (e.Index % 2 == 0 ? ChessTheme.PanelDark : ChessTheme.PanelDarker));
        e.Graphics.FillRectangle(bgBrush, e.Bounds);

        var game = _games[e.Index];
        string text = game.DisplayTitle;

        using var font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        using var textBrush = new SolidBrush(isSelected ? Color.White : ChessTheme.TextPrimary);

        var textRect = new Rectangle(e.Bounds.X + 8, e.Bounds.Y + 4, e.Bounds.Width - 16, e.Bounds.Height - 8);
        var sf = new StringFormat { LineAlignment = StringAlignment.Center };
        e.Graphics.DrawString(text, font, textBrush, textRect, sf);
    }
}
