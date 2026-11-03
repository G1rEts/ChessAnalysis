using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using ChessAnalysis.Core.Config;
using ChessAnalysis.Core.Engine;
using ChessAnalysis.UI.Controls;
using ChessAnalysis.UI.Resources;

namespace ChessAnalysis.UI.Forms;

public class StockfishSetupDialog : Form
{
    private readonly AppSettings _settings;
    private readonly TextBox _txtPath;
    private readonly Label _lblStatus;
    private readonly RoundedButton _btnValidate;
    private readonly RoundedButton _btnLaunch;
    private readonly NumericUpDown _numPv;
    private readonly NumericUpDown _numThreads;
    private bool _isValidated;

    public string SelectedPath => _txtPath.Text.Trim();

    public StockfishSetupDialog(AppSettings settings)
    {
        _settings = settings;

        Text = "Chess Analysis — Настройка Stockfish";
        ClientSize = new Size(680, 335);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(32, 30, 27);
        ForeColor = Color.FromArgb(240, 240, 240);
        Icon = ResourceManager.GetAppIcon();

        // 1. Верхняя панель с заголовком
        var pnlHeader = new Panel
        {
            Dock = DockStyle.Top,
            Height = 85,
            BackColor = Color.FromArgb(24, 23, 21),
            Padding = new Padding(24, 16, 24, 10)
        };

        var lblTitle = new Label
        {
            Text = "Шахматный анализ — Настройка Stockfish",
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(24, 14)
        };

        var lblSubtitle = new Label
        {
            Text = "Для работы анализа, расчета точности и подсказок ходов необходим движок Stockfish.\nУкажите папку с движком или выберите файл stockfish.exe:",
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(180, 178, 174),
            Location = new Point(24, 42),
            Size = new Size(630, 38)
        };

        pnlHeader.Controls.Add(lblTitle);
        pnlHeader.Controls.Add(lblSubtitle);

        // 2. Основная рабочая область
        var pnlBody = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 16, 24, 12)
        };

        var lblPrompt = new Label
        {
            Text = "Путь к движку Stockfish:",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(220, 220, 220),
            Location = new Point(24, 14),
            AutoSize = true
        };

        _txtPath = new TextBox
        {
            Location = new Point(24, 38),
            Size = new Size(440, 27),
            Font = new Font("Segoe UI", 10f),
            BackColor = Color.FromArgb(45, 43, 40),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        _txtPath.TextChanged += (s, e) =>
        {
            _isValidated = false;
            UpdateStatus("Нажмите «Проверить движок», чтобы протестировать выбранный файл.", Color.FromArgb(170, 168, 164));
        };

        var btnFolder = new RoundedButton
        {
            Text = "Папка...",
            Location = new Point(474, 36),
            Size = new Size(95, 30),
            NormalColor = Color.FromArgb(50, 48, 44),
            CustomBorderColor = Color.FromArgb(70, 68, 64),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            BorderRadius = 5
        };
        btnFolder.Click += (s, e) => BrowseFolder();

        var btnFile = new RoundedButton
        {
            Text = "Файл...",
            Location = new Point(577, 36),
            Size = new Size(80, 30),
            NormalColor = Color.FromArgb(50, 48, 44),
            CustomBorderColor = Color.FromArgb(70, 68, 64),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            BorderRadius = 5
        };
        btnFile.Click += (s, e) => BrowseFile();

        _btnValidate = new RoundedButton
        {
            Text = "Проверить движок",
            Location = new Point(24, 76),
            Size = new Size(185, 32),
            NormalColor = Color.FromArgb(58, 56, 52),
            CustomBorderColor = Color.FromArgb(85, 83, 78),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            BorderRadius = 5
        };
        _btnValidate.Click += (s, e) => RunValidation(showSuccessMessage: false);

        _lblStatus = new Label
        {
            Location = new Point(220, 81),
            Size = new Size(435, 24),
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(170, 168, 164),
            Text = "Ожидание проверки..."
        };

        // MultiPV & Threads
        var lblPv = new Label
        {
            Text = "Линии анализа (MultiPV):",
            Location = new Point(24, 122),
            Size = new Size(185, 24),
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(200, 200, 200)
        };
        _numPv = new NumericUpDown
        {
            Location = new Point(215, 120),
            Size = new Size(60, 27),
            Minimum = 1,
            Maximum = 5,
            Value = Math.Clamp(_settings.MultiPv, 1, 5),
            BackColor = Color.FromArgb(45, 43, 40),
            ForeColor = Color.White
        };

        var lblThreads = new Label
        {
            Text = "Потоки процессора (Threads):",
            Location = new Point(310, 122),
            Size = new Size(205, 24),
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(200, 200, 200)
        };
        _numThreads = new NumericUpDown
        {
            Location = new Point(525, 120),
            Size = new Size(60, 27),
            Minimum = 1,
            Maximum = 32,
            Value = Math.Clamp(_settings.Threads, 1, 32),
            BackColor = Color.FromArgb(45, 43, 40),
            ForeColor = Color.White
        };

        pnlBody.Controls.Add(lblPrompt);
        pnlBody.Controls.Add(_txtPath);
        pnlBody.Controls.Add(btnFolder);
        pnlBody.Controls.Add(btnFile);
        pnlBody.Controls.Add(_btnValidate);
        pnlBody.Controls.Add(_lblStatus);
        pnlBody.Controls.Add(lblPv);
        pnlBody.Controls.Add(_numPv);
        pnlBody.Controls.Add(lblThreads);
        pnlBody.Controls.Add(_numThreads);

        // 3. Нижняя панель действий
        var pnlBottom = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 60,
            BackColor = Color.FromArgb(24, 23, 21),
            Padding = new Padding(24, 11, 24, 11)
        };

        _btnLaunch = new RoundedButton
        {
            Text = "Запустить программу",
            Location = new Point(420, 11),
            Size = new Size(236, 38),
            NormalColor = Color.FromArgb(129, 182, 76),
            HoverColor = Color.FromArgb(145, 200, 85),
            CustomBorderColor = Color.FromArgb(145, 200, 85),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            BorderRadius = 6
        };
        _btnLaunch.Click += (s, e) => OnLaunchClicked();

        var btnSkip = new RoundedButton
        {
            Text = "Пропустить",
            Location = new Point(285, 11),
            Size = new Size(125, 38),
            NormalColor = Color.FromArgb(42, 40, 37),
            CustomBorderColor = Color.FromArgb(65, 63, 59),
            ForeColor = Color.FromArgb(180, 178, 174),
            Font = new Font("Segoe UI", 9.5f),
            BorderRadius = 6
        };
        btnSkip.Click += (s, e) =>
        {
            _settings.IsSetupCompleted = true;
            _settings.Save();
            DialogResult = DialogResult.OK;
            Close();
        };

        pnlBottom.Controls.Add(btnSkip);
        pnlBottom.Controls.Add(_btnLaunch);

        Controls.Add(pnlBody);
        Controls.Add(pnlBottom);
        Controls.Add(pnlHeader);

        // Инициализация значения пути
        if (!string.IsNullOrWhiteSpace(_settings.StockfishPath))
        {
            _txtPath.Text = _settings.StockfishPath;
        }

        Shown += (s, e) =>
        {
            if (!string.IsNullOrWhiteSpace(_txtPath.Text) && File.Exists(_txtPath.Text))
            {
                RunValidation(showSuccessMessage: false);
            }
        };
    }

    private void BrowseFolder()
    {
        using var fbd = new FolderBrowserDialog
        {
            Description = "Выберите папку, содержащую stockfish.exe",
            UseDescriptionForTitle = true
        };

        if (!string.IsNullOrEmpty(_txtPath.Text))
        {
            try
            {
                string? dir = Path.GetDirectoryName(_txtPath.Text);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    fbd.SelectedPath = dir;
                }
            }
            catch { }
        }

        if (fbd.ShowDialog(this) == DialogResult.OK)
        {
            string chosenDir = fbd.SelectedPath;
            var files = Directory.GetFiles(chosenDir, "stockfish*.exe", SearchOption.AllDirectories);
            if (files.Length > 0)
            {
                _txtPath.Text = files[0];
                RunValidation(showSuccessMessage: false);
            }
            else
            {
                MessageBox.Show(
                    $"В выбранной папке не найден исполняемый файл stockfish*.exe:\n{chosenDir}",
                    "Stockfish не найден",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
    }

    private void BrowseFile()
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "Исполняемые файлы Stockfish (*stockfish*.exe;stockfish.exe)|*stockfish*.exe;stockfish.exe|Все файлы (*.exe)|*.exe",
            Title = "Выберите файл движка Stockfish"
        };

        if (!string.IsNullOrEmpty(_txtPath.Text))
        {
            try
            {
                string? dir = Path.GetDirectoryName(_txtPath.Text);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    ofd.InitialDirectory = dir;
                }
            }
            catch { }
        }

        if (ofd.ShowDialog(this) == DialogResult.OK)
        {
            _txtPath.Text = ofd.FileName;
            RunValidation(showSuccessMessage: false);
        }
    }

    private bool RunValidation(bool showSuccessMessage)
    {
        string path = _txtPath.Text.Trim();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            UpdateStatus("✗ Файл не найден по указанному пути.", Color.FromArgb(231, 76, 60));
            return false;
        }

        UpdateStatus("... Проверка UCI протокола...", Color.FromArgb(241, 196, 15));
        Cursor = Cursors.WaitCursor;

        try
        {
            bool ok = StockfishEngine.ValidateEngine(path, out string engineName, out string error);
            Cursor = Cursors.Default;

            if (ok)
            {
                _isValidated = true;
                UpdateStatus($"✓ Движок исправен: {engineName}", Color.FromArgb(129, 182, 76));
                if (showSuccessMessage)
                {
                    MessageBox.Show(
                        $"Движок успешно проверен и готов к работе:\n{engineName}",
                        "Проверка завершена",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                return true;
            }
            else
            {
                _isValidated = false;
                UpdateStatus($"✗ Ошибка: {error}", Color.FromArgb(231, 76, 60));
                return false;
            }
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    private void OnLaunchClicked()
    {
        string path = _txtPath.Text.Trim();
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            if (!_isValidated)
            {
                bool ok = RunValidation(showSuccessMessage: false);
                if (!ok)
                {
                    var res = MessageBox.Show(
                        "Движок Stockfish не ответил на проверку UCI.\nВы уверены, что хотите запустить программу без активного анализа?",
                        "Внимание",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);
                    if (res != DialogResult.Yes) return;
                }
            }

            _settings.StockfishPath = path;
            _settings.MultiPv = (int)_numPv.Value;
            _settings.Threads = (int)_numThreads.Value;
            _settings.IsSetupCompleted = true;
            _settings.Save();

            DialogResult = DialogResult.OK;
            Close();
        }
        else
        {
            var res = MessageBox.Show(
                "Путь к Stockfish не указан или файл не существует.\nЗапустить программу без анализа движком?",
                "Stockfish не указан",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (res == DialogResult.Yes)
            {
                _settings.MultiPv = (int)_numPv.Value;
                _settings.Threads = (int)_numThreads.Value;
                _settings.IsSetupCompleted = true;
                _settings.Save();
                DialogResult = DialogResult.OK;
                Close();
            }
        }
    }

    private void UpdateStatus(string text, Color color)
    {
        _lblStatus.Text = text;
        _lblStatus.ForeColor = color;
    }
}
