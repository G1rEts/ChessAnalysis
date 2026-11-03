using System.Drawing;
using ChessAnalysis.UI.Controls;
using ChessAnalysis.UI.Resources;

namespace ChessAnalysis.UI.Forms;

public class ImportPgnDialog : Form
{
    private readonly TextBox _txtPgn;
    private readonly RoundedButton _btnImport;
    private readonly RoundedButton _btnOpenFile;
    private readonly RoundedButton _btnCancel;

    public string PgnText => _txtPgn.Text;

    public ImportPgnDialog(string? initialPgn = null)
    {
        Text = "Загрузить партию (PGN)";
        Size = new Size(620, 460);
        MinimumSize = new Size(500, 380);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = ChessTheme.BackgroundDark;
        ForeColor = ChessTheme.TextPrimary;
        Font = new Font("Segoe UI", 9.5f);

        var topPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 48,
            Padding = new Padding(16, 12, 16, 8),
            BackColor = ChessTheme.PanelDark
        };

        var lblHeader = new Label
        {
            Text = "Вставьте текст PGN или откройте файл с диска:",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Color.White,
            Dock = DockStyle.Fill
        };
        topPanel.Controls.Add(lblHeader);

        var centerPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16, 12, 16, 12),
            BackColor = ChessTheme.BackgroundDark
        };

        _txtPgn = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = ChessTheme.PanelDarker,
            ForeColor = ChessTheme.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 9.5f),
            Text = initialPgn ?? string.Empty
        };
        _txtPgn.KeyDown += (s, e) =>
        {
            if (e.Control && e.KeyCode == Keys.A)
            {
                _txtPgn.SelectAll();
                e.SuppressKeyPress = true;
            }
        };
        centerPanel.Controls.Add(_txtPgn);

        var bottomPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            BackColor = ChessTheme.PanelDark
        };

        _btnOpenFile = new RoundedButton
        {
            Text = "📂 Открыть файл...",
            Width = 160,
            Height = 36,
            BorderRadius = 6,
            NormalColor = Color.FromArgb(48, 46, 43),
            HoverColor = Color.FromArgb(62, 60, 56),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold)
        };
        _btnOpenFile.Click += (s, e) => OpenFileDialog();

        _btnCancel = new RoundedButton
        {
            Text = "Отмена",
            Width = 95,
            Height = 36,
            BorderRadius = 6,
            NormalColor = Color.FromArgb(48, 46, 43),
            HoverColor = Color.FromArgb(62, 60, 56),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold)
        };
        _btnCancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };

        _btnImport = new RoundedButton
        {
            Text = "✔ Загрузить",
            Width = 140,
            Height = 36,
            BorderRadius = 6,
            NormalColor = ChessTheme.AccentGreen,
            HoverColor = ChessTheme.AccentGreenHover,
            PressedColor = Color.FromArgb(100, 150, 55),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.White
        };
        _btnImport.Click += (s, e) =>
        {
            if (string.IsNullOrWhiteSpace(_txtPgn.Text))
            {
                MessageBox.Show("Пожалуйста, введите или загрузите текст PGN.", "Пустой ввод", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };

        void LayoutBottomButtons()
        {
            _btnCancel.Location = new Point(bottomPanel.ClientSize.Width - _btnCancel.Width - 16, 10);
            _btnImport.Location = new Point(_btnCancel.Left - _btnImport.Width - 10, 10);
            _btnOpenFile.Location = new Point(16, 10);
        }

        bottomPanel.Resize += (s, e) => LayoutBottomButtons();
        bottomPanel.Controls.Add(_btnOpenFile);
        bottomPanel.Controls.Add(_btnImport);
        bottomPanel.Controls.Add(_btnCancel);
        LayoutBottomButtons();

        Controls.Add(centerPanel);
        Controls.Add(topPanel);
        Controls.Add(bottomPanel);

        AcceptButton = _btnImport;
        CancelButton = _btnCancel;
    }

    private void OpenFileDialog()
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "PGN файлы (*.pgn)|*.pgn|Текстовые файлы (*.txt)|*.txt|Все файлы (*.*)|*.*",
            Title = "Выберите PGN-файл партии"
        };
        if (ofd.ShowDialog(this) == DialogResult.OK)
        {
            try
            {
                _txtPgn.Text = File.ReadAllText(ofd.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось прочитать файл:\n" + ex.Message, "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
