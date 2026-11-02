using System.Diagnostics;
using Borealis.Core.Diagnostics;
using Borealis.Core.Lighting;

namespace Borealis.Service;

/// <summary>
/// Streamt Software-Effekte (eigener Thread). Die Bildrate kommt aus dem Leistungsmodus; jedes Bild geht als
/// ein Block an den Controller (§1.9: alle Kanäle zusammen, danach Pause), der RAM über die ENE-HAL.
/// Hardwarezugriffe teilen sich die Sperre <see cref="hw"/> mit dem übrigen Dienst.
/// </summary>
sealed class EffectRunner : IDisposable
{
    readonly object hw;
    readonly object state = new();
    readonly AutoResetEvent wake = new(false);
    readonly Thread thread;
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly SignalSources signals = new();
    volatile bool stop;
    bool renderErrorLogged;

    LightingEngine? engine;
    IReadOnlyList<SoftwareZone> zones = [];
    double brightness = 1;
    int fps = 20;
    bool clearPending;

    // RAM läuft in eigenem Thread (SMBus ist langsamer als USB): er bekommt immer das neueste Bild, ältere verfallen
    readonly Thread ramThread;
    readonly AutoResetEvent ramWake = new(false);
    sealed record RamJob(LightingEngine Engine, List<(SoftwareZone, Rgb[])> Frame);
    RamJob? ramPending;

    public EffectRunner(object hwLock)
    {
        hw = hwLock;
        thread = new Thread(Run) { IsBackground = true, Name = "Borealis-Effekte" };
        thread.Start();
        ramThread = new Thread(RunRam) { IsBackground = true, Name = "Borealis-Effekte-RAM" };
        ramThread.Start();
    }

    void RunRam()
    {
        while (!stop)
        {
            ramWake.WaitOne(500);
            var job = Interlocked.Exchange(ref ramPending, null);
            if (job == null) continue;
            try { job.Engine.StreamSoftwareRam(job.Frame); }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or System.Runtime.InteropServices.ExternalException or InvalidOperationException)
            {
                Log.Warn("RAM-Bild nicht gesendet: " + ex.Message);
                Thread.Sleep(1000);
            }
        }
    }

    /// <summary>Gemessene Bildrate (Bilder/s) der letzten Sekunde, 0 = kein Software-Effekt aktiv.</summary>
    public double MeasuredFps { get; private set; }

    /// <summary>Neue Zonen/Einstellungen übernehmen; leere Liste hält an. Die Animation läuft ohne Sprung weiter.</summary>
    public void Update(LightingEngine? target, IReadOnlyList<SoftwareZone> softwareZones, double brightness01, int framesPerSecond)
    {
        lock (state)
        {
            bool countsChanged = !zones.Select(z => (z.Zone.Id, z.LedCount)).SequenceEqual(softwareZones.Select(z => (z.Zone.Id, z.LedCount)));
            engine = target;
            zones = softwareZones;
            brightness = brightness01;
            fps = Math.Clamp(framesPerSecond, 1, 60);
            clearPending |= countsChanged;
        }
        wake.Set();
    }

    /// <summary>Anhalten, bevor das Gerät geschlossen wird.</summary>
    public void Pause() => Update(null, [], brightness, fps);

    void Run()
    {
        int frames = 0;
        long secondStart = clock.ElapsedMilliseconds;
        while (!stop)
        {
            LightingEngine? e;
            IReadOnlyList<SoftwareZone> zs;
            double b;
            int rate;
            bool clear;
            lock (state)
            {
                e = engine; zs = zones; b = brightness; rate = fps; clear = clearPending;
                clearPending = false;
            }
            if (e == null || zs.Count == 0)
            {
                MeasuredFps = 0;
                wake.WaitOne(1000);
                continue;
            }

            long start = clock.ElapsedMilliseconds;
            double t = start / 1000.0;
            // Reaktive Effekte: Pegel/CPU nur abfragen, wenn sie gebraucht werden
            var inputs = signals.Update(zs.Any(z => z.Setting.Effect == FirmwareEffect.Music), zs.Any(z => z.Setting.Effect == FirmwareEffect.Smart));
            var frame = new List<(SoftwareZone, Rgb[])>(zs.Count);
            foreach (var z in zs)
            {
                var leds = new Rgb[z.LedCount];
                try { SoftwareEffects.Render(z.Setting, z.Zone.Id, t, b, leds, inputs); }
                catch (Exception ex)
                {
                    // Ein Rechenfehler darf den Dienst nicht beenden; Zone bleibt für dieses Bild dunkel
                    if (!renderErrorLogged) Log.Error($"Effekt {z.Setting.Effect} auf {z.Zone.Id} fehlgeschlagen", ex);
                    renderErrorLogged = true;
                }
                frame.Add((z, leds));
            }
            var ramPart = frame.Where(f => f.Item1.Zone.Kind == ZoneKind.Ram).ToList();
            if (ramPart.Count > 0)
            {
                Interlocked.Exchange(ref ramPending, new RamJob(e, ramPart));
                ramWake.Set();
            }
            try
            {
                lock (hw)
                {
                    if (clear) e.ClearArgb(zs);
                    e.StreamSoftwareFrame(frame);
                }
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or ObjectDisposedException or System.Runtime.InteropServices.ExternalException)
            {
                Log.Warn("Effekt-Bild nicht gesendet: " + ex.Message);
                wake.WaitOne(1000);
                continue;
            }

            frames++;
            long now = clock.ElapsedMilliseconds;
            if (now - secondStart >= 1000)
            {
                MeasuredFps = frames * 1000.0 / (now - secondStart);
                frames = 0;
                secondStart = now;
            }
            // Mindestens 5 ms Pause, damit der Controller zwischen zwei Blöcken Ruhe hat (§1.9)
            int wait = Math.Max(5, (int)(1000 / rate - (now - start)));
            wake.WaitOne(wait);
        }
    }

    public void Dispose()
    {
        stop = true;
        wake.Set();
        ramWake.Set();
        thread.Join(2000);
        ramThread.Join(2000);
        wake.Dispose();
        ramWake.Dispose();
        signals.Dispose();
    }
}
