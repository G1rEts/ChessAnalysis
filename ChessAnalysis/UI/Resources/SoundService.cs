using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace ChessAnalysis.UI.Resources;

/// <summary>
/// Высокопроизводительный звуковой движок шахматных звуков.
/// Поддерживает полифоническое наложение звуков без прерывания и задержек
/// (как на Chess.com при быстром листании ходов).
/// При отсутствии аудиоустройств или ошибках мягко переключается на PlaySound.
/// </summary>
public static class SoundService
{
    private static bool _initialized = false;
    private static bool _muted = false;

    // Сэмплы PCM (44100 Hz, 16-bit stereo)
    private static readonly Dictionary<string, short[]> _pcmBuffers = new(StringComparer.OrdinalIgnoreCase);
    // Оригинальные WAV байты для fallback PlaySound
    private static readonly Dictionary<string, byte[]> _rawWavBuffers = new(StringComparer.OrdinalIgnoreCase);

    private static readonly object _lock = new();
    private static readonly List<ActiveVoice> _activeVoices = new();

    private static IntPtr _hWaveOut = IntPtr.Zero;
    private static bool _useWaveOut = false;
    private static Thread? _mixerThread;
    private static volatile bool _running = false;
    private static AutoResetEvent? _wakeEvent;
    private static AutoResetEvent? _bufferEvent;

    private const int BufferCount = 6;
    private const int BufferSamples = 4096; // 2048 стерео-сэмпла (~46.4 мс при 44.1 кГц)
    private const int BufferBytes = BufferSamples * sizeof(short); // 8192 байт
    private static readonly AudioBuffer[] _buffers = new AudioBuffer[BufferCount];

    private class ActiveVoice
    {
        public short[] Samples { get; }
        public int Position;

        public ActiveVoice(short[] samples)
        {
            Samples = samples;
            Position = 0;
        }
    }

    private class AudioBuffer
    {
        public IntPtr DataPtr;
        public IntPtr HdrPtr;
        public bool InFlight;
    }

    #region Win32 waveOut P/Invoke
    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEFORMATEX
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEHDR
    {
        public IntPtr lpData;
        public uint dwBufferLength;
        public uint dwBytesRecorded;
        public IntPtr dwUser;
        public uint dwFlags;
        public uint dwLoops;
        public IntPtr lpNext;
        public IntPtr reserved;
    }

    private const int WHDR_DONE = 0x00000001;
    private const int WHDR_PREPARED = 0x00000002;
    private const int CALLBACK_EVENT = 0x00050000;
    private const int WAVE_MAPPER = -1;

    [DllImport("winmm.dll")]
    private static extern int waveOutOpen(out IntPtr hWaveOut, int uDeviceID, ref WAVEFORMATEX lpFormat, IntPtr dwCallback, IntPtr dwInstance, int fdwOpen);

    [DllImport("winmm.dll")]
    private static extern int waveOutPrepareHeader(IntPtr hWaveOut, IntPtr lpWaveHdr, int uSize);

    [DllImport("winmm.dll")]
    private static extern int waveOutWrite(IntPtr hWaveOut, IntPtr lpWaveHdr, int uSize);

    [DllImport("winmm.dll")]
    private static extern int waveOutUnprepareHeader(IntPtr hWaveOut, IntPtr lpWaveHdr, int uSize);

    [DllImport("winmm.dll")]
    private static extern int waveOutReset(IntPtr hWaveOut);

    [DllImport("winmm.dll")]
    private static extern int waveOutClose(IntPtr hWaveOut);

    // Fallback PlaySound
    [DllImport("winmm.dll", SetLastError = true)]
    private static extern bool PlaySound(byte[]? pszSound, IntPtr hmod, uint fdwSound);

    private const uint SND_ASYNC = 0x0001;
    private const uint SND_NODEFAULT = 0x0002;
    private const uint SND_MEMORY = 0x0004;
    #endregion

    public static bool Muted
    {
        get => _muted;
        set => _muted = value;
    }

    private static readonly string[] SoundNames =
    {
        "move-self",
        "capture",
        "move-check",
        "castle",
        "promote",
        "game-start",
        "game-end"
    };

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            string soundDir = FindSoundsDirectory();
            if (string.IsNullOrEmpty(soundDir) || !Directory.Exists(soundDir)) return;

            foreach (var name in SoundNames)
            {
                string wavPath = Path.Combine(soundDir, $"{name}.wav");
                if (File.Exists(wavPath))
                {
                    try
                    {
                        byte[] bytes = File.ReadAllBytes(wavPath);
                        _rawWavBuffers[name] = bytes;
                        short[] samples = ExtractPcmSamples(bytes);
                        if (samples.Length > 0)
                        {
                            _pcmBuffers[name] = samples;
                        }
                    }
                    catch { }
                }
            }

            InitWaveOutEngine();
        }
        catch { }
    }

    private static short[] ExtractPcmSamples(byte[] wavBytes)
    {
        // Ищем чанк "data"
        int dataIndex = -1;
        for (int i = 12; i < wavBytes.Length - 8; i++)
        {
            if (wavBytes[i] == 'd' && wavBytes[i + 1] == 'a' && wavBytes[i + 2] == 't' && wavBytes[i + 3] == 'a')
            {
                dataIndex = i;
                break;
            }
        }

        if (dataIndex < 0) return Array.Empty<short>();

        int dataLength = BitConverter.ToInt32(wavBytes, dataIndex + 4);
        int pcmStart = dataIndex + 8;
        int sampleCount = Math.Min(dataLength, wavBytes.Length - pcmStart) / sizeof(short);

        short[] samples = new short[sampleCount];
        Buffer.BlockCopy(wavBytes, pcmStart, samples, 0, sampleCount * sizeof(short));
        return samples;
    }

    private static void InitWaveOutEngine()
    {
        try
        {
            _wakeEvent = new AutoResetEvent(false);
            _bufferEvent = new AutoResetEvent(false);

            var fmt = new WAVEFORMATEX
            {
                wFormatTag = 1, // WAVE_FORMAT_PCM
                nChannels = 2,
                nSamplesPerSec = 44100,
                nAvgBytesPerSec = 44100 * 2 * sizeof(short),
                nBlockAlign = 2 * sizeof(short),
                wBitsPerSample = 16,
                cbSize = 0
            };

            int openRes = waveOutOpen(out _hWaveOut, WAVE_MAPPER, ref fmt, _bufferEvent.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CALLBACK_EVENT);
            if (openRes != 0 || _hWaveOut == IntPtr.Zero)
            {
                _useWaveOut = false;
                return;
            }

            int hdrSize = Marshal.SizeOf<WAVEHDR>();
            for (int i = 0; i < BufferCount; i++)
            {
                var buf = new AudioBuffer
                {
                    DataPtr = Marshal.AllocHGlobal(BufferBytes),
                    HdrPtr = Marshal.AllocHGlobal(hdrSize),
                    InFlight = false
                };

                var hdr = new WAVEHDR
                {
                    lpData = buf.DataPtr,
                    dwBufferLength = (uint)BufferBytes,
                    dwFlags = 0
                };
                Marshal.StructureToPtr(hdr, buf.HdrPtr, false);
                _buffers[i] = buf;
            }

            _running = true;
            _mixerThread = new Thread(MixerLoop)
            {
                Name = "SoundService Mixer",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            _mixerThread.Start();
            _useWaveOut = true;

            AppDomain.CurrentDomain.ProcessExit += (s, e) => Dispose();
        }
        catch
        {
            _useWaveOut = false;
        }
    }

    private static void MixerLoop()
    {
        short[] mixBuf = new short[BufferSamples];
        int hdrSize = Marshal.SizeOf<WAVEHDR>();

        while (_running)
        {
            bool hasActive;
            lock (_lock)
            {
                hasActive = _activeVoices.Count > 0;
            }

            // Проверяем завершение буферов
            int inFlightCount = 0;
            for (int i = 0; i < BufferCount; i++)
            {
                var buf = _buffers[i];
                if (buf.InFlight)
                {
                    var hdr = Marshal.PtrToStructure<WAVEHDR>(buf.HdrPtr);
                    if ((hdr.dwFlags & WHDR_DONE) != 0)
                    {
                        waveOutUnprepareHeader(_hWaveOut, buf.HdrPtr, hdrSize);
                        buf.InFlight = false;
                    }
                    else
                    {
                        inFlightCount++;
                    }
                }
            }

            if (hasActive)
            {
                // Если есть свободный буфер для записи — микшируем порцию звука
                for (int i = 0; i < BufferCount && inFlightCount < BufferCount - 1; i++)
                {
                    var buf = _buffers[i];
                    if (!buf.InFlight)
                    {
                        Array.Clear(mixBuf, 0, mixBuf.Length);

                        lock (_lock)
                        {
                            for (int s = 0; s < BufferSamples; s++)
                            {
                                int mixedSample = 0;
                                for (int v = 0; v < _activeVoices.Count; v++)
                                {
                                    var voice = _activeVoices[v];
                                    if (voice.Position < voice.Samples.Length)
                                    {
                                        mixedSample += voice.Samples[voice.Position++];
                                    }
                                }
                                mixBuf[s] = (short)Math.Clamp(mixedSample, short.MinValue, short.MaxValue);
                            }

                            _activeVoices.RemoveAll(v => v.Position >= v.Samples.Length);
                        }

                        Marshal.Copy(mixBuf, 0, buf.DataPtr, BufferSamples);

                        var hdr = new WAVEHDR
                        {
                            lpData = buf.DataPtr,
                            dwBufferLength = (uint)BufferBytes,
                            dwFlags = 0
                        };
                        Marshal.StructureToPtr(hdr, buf.HdrPtr, false);
                        waveOutPrepareHeader(_hWaveOut, buf.HdrPtr, hdrSize);
                        waveOutWrite(_hWaveOut, buf.HdrPtr, hdrSize);
                        buf.InFlight = true;
                        inFlightCount++;
                    }
                }

                // Ждем сигнала от сыгравшего буфера или следующего звука
                WaitHandle.WaitAny(new WaitHandle[] { _bufferEvent!, _wakeEvent! }, 25);
            }
            else
            {
                if (inFlightCount > 0)
                {
                    // Доигрываем остатки буферов
                    _bufferEvent!.WaitOne(20);
                }
                else
                {
                    // Звуков нет: спим без нагрузки на процессор
                    _wakeEvent!.WaitOne();
                }
            }
        }
    }

    private static string FindSoundsDirectory()
    {
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
            try
            {
                if (Directory.Exists(dir))
                {
                    return Path.GetFullPath(dir);
                }
            }
            catch { }
        }
        return string.Empty;
    }

    private static void PlayAlias(string name)
    {
        if (_muted) return;

        if (!_initialized)
        {
            Initialize();
        }

        try
        {
            if (_useWaveOut && _pcmBuffers.TryGetValue(name, out var samples))
            {
                lock (_lock)
                {
                    // При сверхбыстром листании ходов (стрелками 20-30 раз/сек)
                    // удерживаем до 5 одновременных звуков ходов, обеспечивая
                    // плотное красивое полифоническое наложение без каши и перегруза
                    if (_activeVoices.Count >= 5)
                    {
                        _activeVoices.RemoveAt(0);
                    }
                    _activeVoices.Add(new ActiveVoice(samples));
                }

                _wakeEvent?.Set();
                return;
            }

            // Fallback: обычный PlaySound при отсутствии PCM или waveOut
            if (_rawWavBuffers.TryGetValue(name, out var bytes))
            {
                PlaySound(bytes, IntPtr.Zero, SND_ASYNC | SND_MEMORY | SND_NODEFAULT);
            }
        }
        catch { }
    }

    public static void PlayMove() => PlayAlias("move-self");
    public static void PlayCapture() => PlayAlias("capture");
    public static void PlayCheck() => PlayAlias("move-check");
    public static void PlayCastle() => PlayAlias("castle");
    public static void PlayPromote() => PlayAlias("promote");
    public static void PlayGameStart() => PlayAlias("game-start");
    public static void PlayGameEnd() => PlayAlias("game-end");

    public static void ToggleMute()
    {
        _muted = !_muted;
    }

    public static void Dispose()
    {
        _running = false;
        try
        {
            _wakeEvent?.Set();
            _bufferEvent?.Set();
            _mixerThread?.Join(100);

            if (_hWaveOut != IntPtr.Zero)
            {
                waveOutReset(_hWaveOut);
                int hdrSize = Marshal.SizeOf<WAVEHDR>();
                for (int i = 0; i < BufferCount; i++)
                {
                    var buf = _buffers[i];
                    if (buf != null)
                    {
                        if (buf.InFlight)
                        {
                            waveOutUnprepareHeader(_hWaveOut, buf.HdrPtr, hdrSize);
                        }
                        if (buf.DataPtr != IntPtr.Zero) Marshal.FreeHGlobal(buf.DataPtr);
                        if (buf.HdrPtr != IntPtr.Zero) Marshal.FreeHGlobal(buf.HdrPtr);
                    }
                }
                waveOutClose(_hWaveOut);
                _hWaveOut = IntPtr.Zero;
            }
        }
        catch { }
    }
}
