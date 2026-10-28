using System.Media;

namespace ChessAnalysis.UI.Resources;

public static class SoundService
{
    private static bool _initialized = false;
    private static bool _muted = false;

    private static byte[]? _moveWavBytes;
    private static byte[]? _captureWavBytes;

    public static bool Muted
    {
        get => _muted;
        set => _muted = value;
    }

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] searchDirs =
        {
            Path.Combine(baseDir, "Assets", "Sounds"),
            Path.Combine(baseDir, "..", "..", "..", "Assets", "Sounds"),
            Path.Combine(baseDir, "..", "..", "..", "..", "Assets", "Sounds"),
            Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Sounds"),
            Path.Combine(Directory.GetCurrentDirectory(), "ChessAnalysis", "Assets", "Sounds")
        };

        foreach (var dir in searchDirs)
        {
            if (Directory.Exists(dir))
            {
                string m = Path.Combine(dir, "move-self.wav");
                string c = Path.Combine(dir, "capture.wav");

                if (File.Exists(m) && _moveWavBytes == null)
                {
                    try { _moveWavBytes = File.ReadAllBytes(m); } catch { }
                }
                if (File.Exists(c) && _captureWavBytes == null)
                {
                    try { _captureWavBytes = File.ReadAllBytes(c); } catch { }
                }
            }
        }
    }

    public static void PlayMove()
    {
        if (_muted || _moveWavBytes == null) return;
        Task.Run(() =>
        {
            try
            {
                using var ms = new MemoryStream(_moveWavBytes);
                using var sp = new SoundPlayer(ms);
                sp.Play();
            }
            catch { }
        });
    }

    public static void PlayCapture()
    {
        if (_muted || _captureWavBytes == null) return;
        Task.Run(() =>
        {
            try
            {
                using var ms = new MemoryStream(_captureWavBytes);
                using var sp = new SoundPlayer(ms);
                sp.Play();
            }
            catch { }
        });
    }

    public static void ToggleMute()
    {
        _muted = !_muted;
    }
}
