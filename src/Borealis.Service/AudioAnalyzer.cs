using System.Numerics;
using System.Runtime.InteropServices;
using Borealis.Core.Diagnostics;
using Borealis.Core.Lighting;

namespace Borealis.Service;

/// <summary>
/// Hört mit, was der PC abspielt (WASAPI-Loopback auf dem Standard-Wiedergabegerät, wie der LightingService, §5),
/// und analysiert im eigenen Thread etwa 90-mal pro Sekunde (FFT über 1024 Samples, Vorschub 512):
/// Pegel, 16 Frequenzbänder, Bass/Mitten/Höhen, Spitzenwerte, Klangschwerpunkt, Beats mit anpassender Schwelle,
/// Tempo (BPM) und „Drops“. Lautstärkeunabhängig durch automatische Verstärkung. Die Effekte lesen nur das Ergebnis.
/// </summary>
sealed class AudioAnalyzer : IDisposable
{
    public const int BandCount = 16;
    const int FftSize = 1024, Hop = 512, HistoryLength = 64;

    Thread? thread;
    volatile bool stop;
    bool loggedStart, loggedError;
    int sampleRate = 48000;

    // Nur im Audio-Thread benutzt
    readonly float[] window = new float[FftSize];
    int windowFill, windowPos, sinceAnalysis;
    readonly double[] prevMagnitudes = new double[FftSize / 2];
    readonly double[] bandMax = new double[BandCount];
    readonly double[] lowHistory = new double[HistoryLength];
    int historyPos, historyCount;
    double levelMax = 1e-4, fluxAverage, lastLow;
    long lastBeatTick, lastDropTick, lastSampleTick;
    readonly List<long> beatTicks = [];

    // Ergebnis für die Effekte (unter resultLock)
    readonly object resultLock = new();
    readonly double[] bands = new double[BandCount];
    readonly double[] bandPeaks = new double[BandCount];
    double level, levelPeak, bass, mid, treble, centroid, dropValue;
    long beatCount, dropTick;
    double bpm;

    public void EnsureRunning()
    {
        if (thread != null) return;
        thread = new Thread(CaptureLoop) { IsBackground = true, Name = "Borealis-Audio" };
        thread.Start();
    }

    void CaptureLoop()
    {
        while (!stop)
        {
            try { CaptureSession(); }
            catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidOperationException)
            {
                if (!loggedError) Log.Warn("Audio-Mitschnitt nicht möglich: " + ex.Message);
                loggedError = true;
            }
            for (int i = 0; i < 30 && !stop; i++) Thread.Sleep(100);
        }
    }

    void CaptureSession()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        // eRender = 0, eConsole = 0
        Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0, 0, out var device));
        var iidClient = typeof(IAudioClient).GUID;
        Marshal.ThrowExceptionForHR(device!.Activate(ref iidClient, 23, IntPtr.Zero, out var clientObj));
        var client = (IAudioClient)clientObj;
        Marshal.ThrowExceptionForHR(client.GetMixFormat(out var format));
        try
        {
            int channels = Marshal.ReadInt16(format, 2);
            sampleRate = Marshal.ReadInt32(format, 4);
            int bits = Marshal.ReadInt16(format, 14);
            int tag = (ushort)Marshal.ReadInt16(format, 0);
            bool isFloat = tag == 3 || (tag == 0xFFFE && bits == 32 && Marshal.PtrToStructure<Guid>(format + 24) == FloatSubtype);
            // AUDCLNT_SHAREMODE_SHARED = 0, AUDCLNT_STREAMFLAGS_LOOPBACK = 0x20000, Puffer 100 ms
            Marshal.ThrowExceptionForHR(client.Initialize(0, 0x20000, 1_000_000, 0, format, IntPtr.Zero));
            var iidCapture = typeof(IAudioCaptureClient).GUID;
            Marshal.ThrowExceptionForHR(client.GetService(ref iidCapture, out var captureObj));
            var capture = (IAudioCaptureClient)captureObj;
            Marshal.ThrowExceptionForHR(client.Start());
            if (!loggedStart) Log.Info($"Audio-Mitschnitt läuft ({sampleRate} Hz, {channels} Kanäle, {(isFloat ? "Float" : bits + " Bit")}).");
            loggedStart = true;
            loggedError = false;
            lastSampleTick = Environment.TickCount64;
            try
            {
                while (!stop)
                {
                    Thread.Sleep(5);
                    bool got = false;
                    while (capture.GetNextPacketSize(out uint packet) == 0 && packet > 0)
                    {
                        Marshal.ThrowExceptionForHR(capture.GetBuffer(out var data, out uint frames, out uint flags, out _, out _));
                        Feed(data, (int)frames, channels, bits, isFloat, silent: (flags & 0x2) != 0);
                        capture.ReleaseBuffer(frames);
                        got = true;
                    }
                    // In Stille liefert Loopback nichts: Nullen nachschieben, damit alles sauber abklingt
                    long now = Environment.TickCount64;
                    if (got) lastSampleTick = now;
                    else if (now - lastSampleTick >= 20)
                    {
                        FeedSilence((int)((now - lastSampleTick) * sampleRate / 1000));
                        lastSampleTick = now;
                    }
                }
            }
            finally
            {
                client.Stop();
                Marshal.ReleaseComObject(capture);
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(format);
            Marshal.ReleaseComObject(client);
            Marshal.ReleaseComObject(device);
            Marshal.ReleaseComObject(enumerator);
        }
    }

    unsafe void Feed(IntPtr data, int frames, int channels, int bits, bool isFloat, bool silent)
    {
        for (int f = 0; f < frames; f++)
        {
            float mono = 0;
            if (!silent)
            {
                for (int c = 0; c < channels; c++)
                {
                    int idx = f * channels + c;
                    mono += isFloat ? ((float*)data)[idx] : bits == 16 ? ((short*)data)[idx] / 32768f : 0;
                }
                mono /= Math.Max(1, channels);
            }
            Push(mono);
        }
    }

    void FeedSilence(int samples)
    {
        for (int i = 0; i < Math.Min(samples, sampleRate); i++) Push(0);
    }

    void Push(float sample)
    {
        // Ringpuffer; das Fenster wird erst bei der Analyse in zeitlicher Reihenfolge gelesen
        window[windowPos] = sample;
        windowPos = (windowPos + 1) % FftSize;
        if (windowFill < FftSize) windowFill++;
        if (windowFill == FftSize && ++sinceAnalysis >= Hop)
        {
            sinceAnalysis = 0;
            Analyze();
        }
    }

    /// <summary>Eine Analyse (alle ~10,7 ms bei 48 kHz).</summary>
    void Analyze()
    {
        long now = Environment.TickCount64;
        double dt = (double)Hop / sampleRate;
        var buf = new Complex[FftSize];
        double rms = 0;
        for (int i = 0; i < FftSize; i++)
        {
            float x = window[(windowPos + i) % FftSize];
            rms += x * x;
            buf[i] = new Complex(x * Hann[i], 0);
        }
        rms = Math.Sqrt(rms / FftSize);
        Fft(buf);

        // Spektraler Fluss (für Drops) und Bassenergie (für Beats) aus den Rohwerten
        double flux = 0, low = 0, weighted = 0, total = 0;
        double binHz = (double)sampleRate / FftSize;
        for (int k = 1; k < FftSize / 2; k++)
        {
            double m = buf[k].Magnitude;
            flux += Math.Max(0, m - prevMagnitudes[k]);
            prevMagnitudes[k] = m;
            double hz = k * binHz;
            if (hz >= 40 && hz <= 160) low += m * m;
            if (hz >= 60 && hz <= 12000) { weighted += m * Math.Log2(hz / 60); total += m; }
        }

        // Bänder logarithmisch 40 Hz … 16 kHz, automatisch verstärkt
        double maxDecay = Math.Exp(-dt * 0.12);
        double release = Math.Exp(-dt * 9);
        double peakFall = dt * 0.6;
        var newBands = new double[BandCount];
        for (int b = 0; b < BandCount; b++)
        {
            double lo = 40 * Math.Pow(16000.0 / 40, (double)b / BandCount);
            double hi = 40 * Math.Pow(16000.0 / 40, (double)(b + 1) / BandCount);
            int k0 = Math.Max(1, (int)(lo / binHz)), k1 = Math.Max(k0 + 1, (int)(hi / binHz));
            double e = 0;
            for (int k = k0; k < k1 && k < FftSize / 2; k++) e += buf[k].Magnitude;
            e /= k1 - k0;
            bandMax[b] = Math.Max(Math.Max(e, bandMax[b] * maxDecay), 2e-4);
            newBands[b] = Math.Sqrt(Math.Clamp(e / bandMax[b], 0, 1));
        }
        levelMax = Math.Max(Math.Max(rms, levelMax * maxDecay), 1e-4);
        double newLevel = Math.Sqrt(Math.Clamp(rms / levelMax, 0, 1));
        bool audible = rms > 0.002;

        // Beat: Bassenergie über Mittelwert + 1,4·Streuung der letzten ~0,7 s, steigend, höchstens alle 270 ms
        double mean = 0, var2 = 0;
        for (int i = 0; i < historyCount; i++) mean += lowHistory[i];
        mean /= Math.Max(1, historyCount);
        for (int i = 0; i < historyCount; i++) var2 += (lowHistory[i] - mean) * (lowHistory[i] - mean);
        double std = Math.Sqrt(var2 / Math.Max(1, historyCount));
        bool isBeat = audible && historyCount > 20 && low > mean + 1.4 * std && low > mean * 1.3 && low > lastLow && now - lastBeatTick > 270;
        lowHistory[historyPos] = low;
        historyPos = (historyPos + 1) % HistoryLength;
        historyCount = Math.Min(historyCount + 1, HistoryLength);
        lastLow = low;
        if (isBeat)
        {
            lastBeatTick = now;
            lock (beatTicks)
            {
                beatTicks.Add(now);
                if (beatTicks.Count > 12) beatTicks.RemoveAt(0);
            }
        }

        // Drop: Fluss weit über seinem Langzeitmittel bei hohem Pegel, höchstens alle 2 s
        fluxAverage += (flux - fluxAverage) * dt / 4;
        bool isDrop = audible && newLevel > 0.6 && flux > fluxAverage * 3 && now - lastDropTick > 2000 && fluxAverage > 0;
        if (isDrop) lastDropTick = now;

        lock (resultLock)
        {
            for (int b = 0; b < BandCount; b++)
            {
                bands[b] = newBands[b] > bands[b] ? newBands[b] : bands[b] * release + newBands[b] * (1 - release);
                bandPeaks[b] = Math.Max(bands[b], bandPeaks[b] - peakFall);
            }
            level = newLevel > level ? newLevel : level * release + newLevel * (1 - release);
            levelPeak = Math.Max(level, levelPeak - peakFall);
            bass = (bands[0] + bands[1] + bands[2] + bands[3]) / 4;
            mid = (bands[5] + bands[6] + bands[7] + bands[8] + bands[9]) / 5;
            treble = (bands[11] + bands[12] + bands[13] + bands[14] + bands[15]) / 5;
            if (total > 0) centroid += (Math.Clamp(weighted / total / Math.Log2(12000.0 / 60), 0, 1) - centroid) * Math.Min(1, dt * 6);
            if (isBeat) { beatCount++; bpm = EstimateBpm(); }
            if (isDrop) dropTick = now;
            dropValue = dropTick == 0 ? 0 : Math.Exp(-(now - dropTick) / 600.0);
        }
    }

    /// <summary>Tempo aus dem Median der letzten Beat-Abstände (60–200 BPM, sonst 0).</summary>
    double EstimateBpm()
    {
        long[] ticks;
        lock (beatTicks) ticks = beatTicks.ToArray();
        if (ticks.Length < 4) return 0;
        var gaps = new List<double>();
        for (int i = 1; i < ticks.Length; i++) gaps.Add(ticks[i] - ticks[i - 1]);
        gaps.Sort();
        double median = gaps[gaps.Count / 2];
        return median is >= 300 and <= 1000 ? 60000 / median : 0;
    }

    /// <summary>Aktuelles Ergebnis in <paramref name="into"/> schreiben (vom Effekt-Thread je Bild).</summary>
    public void Read(EffectInputs into)
    {
        long now = Environment.TickCount64;
        lock (resultLock)
        {
            if (into.Bands.Length != BandCount) into.Bands = new double[BandCount];
            if (into.BandPeaks.Length != BandCount) into.BandPeaks = new double[BandCount];
            Array.Copy(bands, into.Bands, BandCount);
            Array.Copy(bandPeaks, into.BandPeaks, BandCount);
            into.AudioLevel = level;
            into.LevelPeak = levelPeak;
            into.Bass = bass;
            into.Mid = mid;
            into.Treble = treble;
            into.Centroid = centroid;
            into.BeatCount = beatCount;
            into.Bpm = bpm;
            into.Drop = dropTick == 0 ? 0 : Math.Exp(-(now - dropTick) / 600.0);
        }
        long[] ticks;
        lock (beatTicks) ticks = beatTicks.ToArray();
        int count = Math.Min(6, ticks.Length);
        var ages = new double[count];
        for (int i = 0; i < count; i++) ages[i] = (now - ticks[ticks.Length - 1 - i]) / 1000.0;
        into.BeatAges = ages;
        double lastAge = count > 0 ? ages[0] : 99;
        into.Beat = Math.Exp(-lastAge * 7);
        // Takt-Position: mit Tempo vorausberechnet, sonst seit dem letzten Beat
        double period = bpm > 0 ? 60 / bpm : 0.5;
        into.BeatPhase = Math.Clamp(lastAge / period, 0, 1.999) % 1;
    }

    static void Fft(Complex[] a)
    {
        int n = a.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (a[i], a[j]) = (a[j], a[i]);
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            var wl = Complex.FromPolarCoordinates(1, -2 * Math.PI / len);
            for (int i = 0; i < n; i += len)
            {
                var w = Complex.One;
                for (int j = 0; j < len / 2; j++)
                {
                    var u = a[i + j];
                    var v = a[i + j + len / 2] * w;
                    a[i + j] = u + v;
                    a[i + j + len / 2] = u - v;
                    w *= wl;
                }
            }
        }
    }

    public void Dispose()
    {
        stop = true;
        thread?.Join(1000);
    }

    static readonly Guid FloatSubtype = new("00000003-0000-0010-8000-00aa00389b71");

    static readonly double[] Hann = Enumerable.Range(0, FftSize).Select(i => 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftSize - 1))).ToArray();

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint padding);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }
}
