using Borealis.Core.Diagnostics;
using Borealis.Core.Hardware;
using Borealis.Core.Ipc;
using Borealis.Core.Profiles;

namespace Borealis.Service;

/// <summary>
/// Auto-Tuning der Lüfter (wie die Lüfterkalibrierung in Armoury Crate, docs/hardware.md §2.5):
/// alle Lüfter gleichzeitig in 5-%-Stufen von 100 auf 0 % stellen und je Stufe die eingeschwungene, gemittelte Drehzahl
/// messen; dann auf 1 % genau die kleinste Leistung, bei der ein Lüfter sicher weiterläuft (mit Gegenprobe), und die,
/// ab der er aus dem Stand anläuft. Danach die Pumpe genauso, während alle Lüfter zur Kühlung voll laufen und mit
/// strengerer Temperaturgrenze (70 °C). Dauer etwa 5–7 Minuten.
/// Die Leistung wird über flache Kurven im Chip gesetzt (derselbe Weg wie die Regelung). Steigt die CPU-Temperatur
/// über 85 °C oder bricht der Benutzer ab, endet der Lauf sofort; danach schreibt die Laufzeit die normalen Kurven zurück.
/// </summary>
sealed class FanTuner
{
    const double MaxCpuTemp = 85, PumpCpuTemp = 70;
    // Flache Kurve: ab 20 °C überall dieselbe Leistung
    static readonly byte[] FlatTemps = [20, 30, 40, 50];

    readonly Func<double?> cpuTemp;
    readonly object state = new();
    CancellationTokenSource? cts;
    FanTuneStatus status = new();
    // Grenze und Fortschrittsbereich der laufenden Phase (Lüfter bzw. Pumpe)
    double cpuLimit = MaxCpuTemp;
    int scopeFrom, scopeTo = 100;
    string? finishNote;

    public FanTuner(Func<double?> cpuTemp) { this.cpuTemp = cpuTemp; }

    public bool Running { get { lock (state) return status.Running; } }

    public FanTuneStatus Status
    {
        get { lock (state) return new FanTuneStatus { Running = status.Running, Percent = status.Percent, Message = status.Message, Error = status.Error }; }
    }

    /// <summary>
    /// Lauf starten. <paramref name="done"/> bekommt die Ergebnisse (leer bei Abbruch/Fehler) und muss die normalen
    /// Kurven wieder schreiben.
    /// </summary>
    /// <param name="only">Nur diese Lüfter/Pumpen messen (Kennungen wie „CPUFAN“); null = alle.</param>
    public void Start(Action<Dictionary<string, FanTuning>> done, IReadOnlyCollection<string>? only = null)
    {
        lock (state)
        {
            if (status.Running) throw new InvalidOperationException("Das Auto-Tuning läuft bereits.");
            cts = new CancellationTokenSource();
            status = new FanTuneStatus { Running = true, Message = "Startet …" };
        }
        var token = cts.Token;
        new Thread(() =>
        {
            var results = new Dictionary<string, FanTuning>();
            string? error = null;
            try { results = Run(token, only); }
            catch (OperationCanceledException) { error = "Abgebrochen."; }
            catch (Exception ex) when (ex is IOException or TimeoutException or InvalidOperationException) { error = ex.Message; }
            Log.Info(error == null
                ? "Auto-Tuning fertig: " + string.Join("; ", results.Select(r => $"{r.Key} läuft ab {r.Value.MinDuty} %, startet ab {r.Value.StartDuty} %, max {r.Value.MaxRpm} U/min"))
                : "Auto-Tuning beendet: " + error);
            try { done(error == null ? results : []); }
            catch (Exception ex) { Log.Error("Kurven nach dem Auto-Tuning nicht geschrieben", ex); }
            lock (state) status = new FanTuneStatus { Running = false, Percent = error == null ? 100 : status.Percent, Message = error == null ? "Fertig." : "", Error = error ?? finishNote };
        }) { IsBackground = true, Name = "Borealis-Auto-Tuning" }.Start();
    }

    public void Cancel()
    {
        lock (state) cts?.Cancel();
    }

    void Report(int percent, string message)
    {
        lock (state) { status.Percent = scopeFrom + percent * (scopeTo - scopeFrom) / 100; status.Message = message; }
    }

    Dictionary<string, FanTuning> Run(CancellationToken token, IReadOnlyCollection<string>? only)
    {
        using var chip = Nct6799.Detect() ?? throw new IOException("Kein unterstützter Lüfter-Chip gefunden.");
        bool Selected(FanHeader h) => only == null || only.Contains(h.Id);
        var allFans = BoardFans.Headers.Where(h => !h.Pump).ToList();
        var fans = allFans.Where(Selected).ToList();
        var pumps = BoardFans.Headers.Where(h => h.Pump && Selected(h)).ToList();
        finishNote = null;
        if (fans.Count == 0 && pumps.Count == 0) throw new IOException("Nichts zum Messen ausgewählt.");
        Log.Info("Auto-Tuning gestartet für " + string.Join(", ", fans.Concat(pumps).Select(f => f.Name)) + ".");

        // Fortschritt: Lüfter und Pumpe teilen sich die Anzeige, wenn beide gemessen werden
        int split = fans.Count == 0 ? 0 : pumps.Count == 0 ? 100 : 75;
        (scopeFrom, scopeTo, cpuLimit) = (0, split, MaxCpuTemp);
        var results = fans.Count > 0 ? Tune(chip, fans, token, fans.Count == 1 ? fans[0].Name : "Lüfter") : new Dictionary<string, FanTuning>();
        if (pumps.Count > 0)
        {
            // Pumpe: alle Lüfter voll zur Kühlung, strengere Temperaturgrenze; ein Abbruch hier behält die Lüfterwerte
            (scopeFrom, scopeTo, cpuLimit) = (split, 100, PumpCpuTemp);
            SetAll(chip, allFans, 100);
            try
            {
                foreach (var (id, t) in Tune(chip, pumps, token, "Pumpe")) results[id] = t;
            }
            catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException)
            {
                // Zu warm oder abgebrochen: die Lüfterwerte sind fertig und bleiben erhalten
                finishNote = "Pumpe nicht abgestimmt: " + (ex is OperationCanceledException ? "abgebrochen." : ex.Message);
                Log.Warn(finishNote);
            }
            finally
            {
                SetAll(chip, pumps, 100);
                cpuLimit = MaxCpuTemp;
            }
        }
        if (results.Count == 0) throw new IOException(finishNote ?? "Kein Lüfter dreht; nichts abzustimmen.");
        return results;
    }

    /// <summary>Messablauf für eine Gruppe (Lüfter oder Pumpe); liefert nur angeschlossene Geräte.</summary>
    Dictionary<string, FanTuning> Tune(Nct6799 chip, List<FanHeader> fans, CancellationToken token, string what)
    {

        // 1. Volle Leistung: lange genug hochlaufen lassen, angeschlossene Lüfter erkennen
        Report(1, $"{what}: 100 %");
        SetAll(chip, fans, 100);
        var full = Measure(chip, fans, token, minMs: 8000, maxMs: 25000);
        fans = fans.Where(f => full.Mean[f.Input] > 200).ToList();
        if (fans.Count == 0) return [];
        var rpm = fans.ToDictionary(f => f.Id, _ => new int[Stages]);
        foreach (var f in fans) rpm[f.Id][Stages - 1] = full.Mean[f.Input];

        // 2. Drehzahl in 5-%-Stufen von oben nach unten, jeweils eingeschwungen und gemittelt
        for (int stage = Stages - 2; stage >= 0; stage--)
        {
            int duty = stage * StageStep;
            Report(5 + (Stages - 2 - stage) * 50 / (Stages - 1), $"{what}: Drehzahl bei {duty} % messen");
            SetAll(chip, fans, duty);
            var m = Measure(chip, fans, token, minMs: 4000, maxMs: 15000);
            foreach (var f in fans) rpm[f.Id][stage] = m.Mean[f.Input];
        }

        // 3. Kleinste Leistung, bei der der Lüfter sicher weiterläuft: unter der kleinsten laufenden Stufe in 1-%-Schritten,
        //    jede Stufe mindestens 5 s und nur „läuft“, wenn in allen Messungen eine Drehzahl da ist. Lüfter, die bei 0 %
        //    weiterdrehen, bleiben nie stehen (Mindestleistung 0).
        var lowest = fans.ToDictionary(f => f.Id, f => Enumerable.Range(0, Stages).First(i => rpm[f.Id][i] > 0) * StageStep);
        var minDuty = new Dictionary<string, int>(lowest);
        var minRpm = fans.ToDictionary(f => f.Id, f => rpm[f.Id][lowest[f.Id] / StageStep]);
        var active = fans.Where(f => lowest[f.Id] > 0).ToList();
        if (active.Count > 0)
        {
            Report(56, "Mindestleistung suchen");
            SpinUp(chip, active, token);
            foreach (var f in active) SetAll(chip, [f], lowest[f.Id]);
            Measure(chip, active, token, minMs: 5000, maxMs: 12000);
            for (int k = 1; k < StageStep && active.Count > 0; k++)
            {
                Report(56 + k * 3, $"Mindestleistung suchen (−{k} %)");
                foreach (var f in active) SetAll(chip, [f], lowest[f.Id] - k);
                var m = Measure(chip, active, token, minMs: 5000, maxMs: 12000);
                foreach (var f in active.ToList())
                {
                    if (m.AlwaysRunning(f.Input)) { minDuty[f.Id] = lowest[f.Id] - k; minRpm[f.Id] = m.Mean[f.Input]; }
                    else active.Remove(f);
                }
            }
            // Gegenprobe: 15 s auf der gefundenen Leistung halten; bleibt er stehen, 1 % mehr
            var confirm = fans.Where(f => minDuty[f.Id] > 0).ToList();
            if (confirm.Count > 0)
            {
                Report(72, "Mindestleistung prüfen");
                SpinUp(chip, confirm, token);
                foreach (var f in confirm) SetAll(chip, [f], minDuty[f.Id]);
                var m = Measure(chip, confirm, token, minMs: 15000, maxMs: 20000);
                foreach (var f in confirm.Where(f => !m.AlwaysRunning(f.Input))) minDuty[f.Id] = Math.Min(100, minDuty[f.Id] + 1);
            }
        }

        // 4. Anlaufen aus dem Stand (nur Lüfter, die bei 0 % stehen): grob in 3-%-Schritten, dann nach erneutem Stillstand
        //    in 1-%-Schritten. Unter ~165 U/min meldet der Chip schon 0, deshalb nach „0“ noch auslaufen lassen.
        var startDuty = fans.ToDictionary(f => f.Id, _ => 0);
        var stoppers = fans.Where(f => rpm[f.Id][0] == 0).ToList();
        if (stoppers.Count > 0)
        {
            Report(76, "Lüfter anhalten");
            StopAndWait(chip, stoppers, token);
            var bracket = new Dictionary<string, int>();
            var open = stoppers.ToList();
            for (int d = Math.Max(1, stoppers.Min(f => minDuty[f.Id])); d <= 60 && open.Count > 0; d += 3)
            {
                Report(80 + Math.Min(8, d / 4), $"Anlaufen prüfen ({d} %)");
                SetAll(chip, open, d);
                var m = Measure(chip, open, token, minMs: 6000, maxMs: 8000);
                foreach (var f in open.Where(f => m.Started(f.Input)).ToList())
                {
                    bracket[f.Id] = d;
                    open.Remove(f);
                }
            }
            foreach (var f in open) startDuty[f.Id] = 60;

            var fine = stoppers.Where(f => bracket.ContainsKey(f.Id)).ToList();
            foreach (var f in fine) startDuty[f.Id] = bracket[f.Id];
            if (fine.Count > 0)
            {
                Report(90, "Anlaufen genau prüfen");
                StopAndWait(chip, fine, token);
                var pending = fine.ToList();
                for (int k = 2; k >= 0 && pending.Count > 0; k--)
                {
                    Report(96 - k * 2, "Anlaufen genau prüfen");
                    foreach (var f in pending) SetAll(chip, [f], bracket[f.Id] - k);
                    var m = Measure(chip, pending, token, minMs: 6000, maxMs: 8000);
                    foreach (var f in pending.Where(f => m.Started(f.Input)).ToList())
                    {
                        startDuty[f.Id] = bracket[f.Id] - k;
                        pending.Remove(f);
                    }
                }
            }
        }

        Report(99, $"{what}: fertig");
        var now = DateTime.Now;
        return fans.ToDictionary(f => f.Id, f => new FanTuning
        {
            Time = now, Rpm = rpm[f.Id], MinDuty = minDuty[f.Id], MinDutyRpm = minDuty[f.Id] > 0 ? minRpm[f.Id] : 0, StartDuty = startDuty[f.Id],
        });
    }

    const int StageStep = 5, Stages = 100 / StageStep + 1;

    static void SetAll(Nct6799 chip, IEnumerable<FanHeader> fans, int duty)
    {
        byte d = (byte)Math.Round(Math.Clamp(duty, 0, 100) * 2.55);
        foreach (var f in fans) chip.WriteCurve(f.Input, FlatTemps, [d, d, d, d]);
    }

    /// <summary>Kurz voll laufen lassen, damit auch ein stehender Lüfter sicher dreht.</summary>
    void SpinUp(Nct6799 chip, IReadOnlyList<FanHeader> fans, CancellationToken token)
    {
        SetAll(chip, fans, 100);
        Wait(token, 4000);
    }

    /// <summary>Auf 0 % stellen und warten, bis der Chip 5 s lang 0 meldet; danach noch 15 s auslaufen lassen (unter ~165 U/min meldet er schon 0).</summary>
    void StopAndWait(Nct6799 chip, IReadOnlyList<FanHeader> fans, CancellationToken token)
    {
        SetAll(chip, fans, 0);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int quiet = 0;
        while (quiet < 10 && sw.ElapsedMilliseconds < 40000)
        {
            Wait(token, 500);
            var r = chip.Read().FanRpm;
            quiet = fans.All(f => (r[f.Input] ?? 0) == 0) ? quiet + 1 : 0;
        }
        Wait(token, 15000);
    }

    /// <summary>Messfenster der letzten 3 s (6 Werte im Abstand von 0,5 s).</summary>
    sealed record Window(int[] Mean, int[] Min, int[] Max, int[] RecentMin)
    {
        public bool AlwaysRunning(int input) => Min[input] > 0;
        /// <summary>Angelaufen: die letzten 2 s durchgehend eine Drehzahl (ein einzelner Ausreißer zählt nicht).</summary>
        public bool Started(int input) => RecentMin[input] > 0;
    }

    /// <summary>
    /// Warten, bis die Drehzahl eingeschwungen ist (3 s lang Schwankung höchstens 1,5 % bzw. 15 U/min), dann den
    /// Mittelwert dieser 3 s nehmen. Spätestens nach <paramref name="maxMs"/> wird das letzte Fenster genommen.
    /// </summary>
    Window Measure(Nct6799 chip, IReadOnlyList<FanHeader> fans, CancellationToken token, int minMs, int maxMs)
    {
        const int Size = 6;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var history = new List<int[]>();
        while (true)
        {
            Wait(token, 500);
            history.Add(chip.Read().FanRpm.Select(v => v ?? 0).ToArray());
            if (history.Count < Size) continue;
            var window = history.Skip(history.Count - Size).ToList();
            bool stable = fans.All(f =>
            {
                var v = window.Select(w => w[f.Input]).ToList();
                return v.Max() - v.Min() <= Math.Max(15, v.Average() * 0.015);
            });
            if ((stable && sw.ElapsedMilliseconds >= minMs) || sw.ElapsedMilliseconds >= maxMs)
            {
                int n = window[0].Length;
                return new Window(
                    Enumerable.Range(0, n).Select(i => (int)Math.Round(window.Average(w => w[i]))).ToArray(),
                    Enumerable.Range(0, n).Select(i => window.Min(w => w[i])).ToArray(),
                    Enumerable.Range(0, n).Select(i => window.Max(w => w[i])).ToArray(),
                    Enumerable.Range(0, n).Select(i => window.Skip(Size - 4).Min(w => w[i])).ToArray());
            }
        }
    }

    /// <summary>Warten mit Abbruch; bricht auch ab, wenn die CPU zu heiß wird.</summary>
    void Wait(CancellationToken token, int ms)
    {
        if (token.WaitHandle.WaitOne(ms)) throw new OperationCanceledException();
        if (cpuTemp() is { } t && t > cpuLimit)
            throw new InvalidOperationException($"CPU-Temperatur {t:0} °C über {cpuLimit:0} °C – abgebrochen.");
    }
}
