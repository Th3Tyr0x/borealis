using Borealis.Core.Hardware;

namespace Borealis.Core.Profiles;

/// <summary>Fan mode: fixed curves for all fans or custom curves per fan.
/// Lüftermodus: feste Kurven für alle Lüfter oder eigene Kurven je Lüfter.</summary>
public enum FanMode { Silent, Standard, Turbo, FullSpeed, Custom }

/// <summary>Temperature the chip regulates a fan by.
/// Temperatur, nach der der Chip einen Lüfter regelt.</summary>
public enum FanTempSource { Cpu, Motherboard, CpuSocket }

/// <summary>Curve point: from <see cref="Temp"/> °C the fan runs at <see cref="Duty"/> % (linear between).
/// Kurvenpunkt: ab <see cref="Temp"/> °C läuft der Lüfter mit <see cref="Duty"/> % (dazwischen linear).</summary>
public sealed class FanPoint
{
    public int Temp { get; set; }
    public int Duty { get; set; }

    public FanPoint() { }
    public FanPoint(int temp, int duty) { Temp = temp; Duty = duty; }
}

/// <summary>
/// Fan control (for all lighting profiles). Borealis writes the curves to the fan chip, which then regulates on its own;
/// while <see cref="Enabled"/> is off, the ASUS fan service stays in charge.
/// Lüftersteuerung (für alle Beleuchtungsprofile). Borealis schreibt die Kurven in den Lüfter-Chip, der dann selbst regelt;
/// solange <see cref="Enabled"/> aus ist, bleibt alles beim ASUS-Lüfterdienst.
/// </summary>
public sealed class FanSettings
{
    /// <summary>Borealis controls the fans; the ASUS fan service is stopped for it.
    /// Borealis steuert die Lüfter; der ASUS-Lüfterdienst wird dafür angehalten.</summary>
    public bool Enabled { get; set; }
    /// <summary>Also set the ASUS fan service to "Disabled" so it isn't restarted.
    /// ASUS-Lüfterdienst zusätzlich auf „Deaktiviert“ stellen, damit er nicht neu startet.</summary>
    public bool DisableAsusFanService { get; set; }
    public FanMode Mode { get; set; } = FanMode.Standard;
    /// <summary>Custom curves per fan (ID like "CPUFAN"), four points each; used in <see cref="FanMode.Custom"/>.
    /// Eigene Kurven je Lüfter (Kennung wie „CPUFAN“), je vier Punkte; gilt im Modus <see cref="FanMode.Custom"/>.</summary>
    public Dictionary<string, List<FanPoint>> Curves { get; set; } = new();
    /// <summary>Auto-tuning results per fan (ID like "CPUFAN").
    /// Messwerte des Auto-Tunings je Lüfter (Kennung wie „CPUFAN“).</summary>
    public Dictionary<string, FanTuning> Tuning { get; set; } = new();
    /// <summary>
    /// Custom tweaks of the fixed modes per fan: mode ("Silent", "Standard", "Turbo") → fan → four points.
    /// Without an entry the computed curve applies (<see cref="Preset(FanMode, FanHeader, FanTuning?)"/>).
    /// Eigene Anpassungen der festen Modi je Lüfter: Modus („Silent“, „Standard“, „Turbo“) → Lüfter → vier Punkte.
    /// Ohne Eintrag gilt die berechnete Kurve (<see cref="Preset(FanMode, FanHeader, FanTuning?)"/>).
    /// </summary>
    public Dictionary<string, Dictionary<string, List<FanPoint>>> ProfileCurves { get; set; } = new();
    /// <summary>Temperature source per fan (ID like "SYSFAN"); missing = CPU, as with ASUS.
    /// Temperaturquelle je Lüfter (Kennung wie „SYSFAN“); fehlt = CPU wie bei ASUS.</summary>
    public Dictionary<string, FanTempSource> Sources { get; set; } = new();

    public const int MinTemp = 20, MaxTemp = 95;

    /// <summary>The CPU fan always follows the CPU; by motherboard temperature it would run too slow under load.
    /// Der CPU-Lüfter regelt immer nach der CPU; nach der Mainboard-Temperatur liefe er unter Last zu langsam.</summary>
    public static bool CanChooseSource(FanHeader fan) => fan.Id != "CPUFAN";

    public FanTempSource SourceFor(FanHeader fan) =>
        CanChooseSource(fan) ? Sources.GetValueOrDefault(fan.Id) : FanTempSource.Cpu;

    /// <summary>Value for the chip's source register (CPU = 28, as set by the ASUS fan service).
    /// Wert für das Quellregister des Chips (CPU = 28, wie vom ASUS-Lüfterdienst eingestellt).</summary>
    public static TempSource ChipSource(FanTempSource s) => s switch
    {
        FanTempSource.Motherboard => TempSource.Systin,
        FanTempSource.CpuSocket => TempSource.Cputin,
        _ => TempSource.Peci0Cal,
    };

    /// <summary>ID of the matching reading in <see cref="SensorSnapshot.Temperatures"/>.
    /// Kennung des passenden Messwerts in <see cref="SensorSnapshot.Temperatures"/>.</summary>
    public static string SensorId(FanTempSource s) => s switch
    {
        FanTempSource.Motherboard => "systin",
        FanTempSource.CpuSocket => "cputin",
        _ => "tctl",
    };

    public static string DisplayName(FanTempSource s) => s switch
    {
        FanTempSource.Motherboard => Localization.Loc.Term("Mainboard"),
        FanTempSource.CpuSocket => Localization.Loc.Term("CPU-Sockel"),
        _ => "CPU",
    };

    /// <summary>Name in the UI language.
    /// Name in der Sprache der Oberfläche.</summary>
    public static string DisplayName(FanMode m) => Localization.Loc.Term(GermanName(m));

    public static string GermanName(FanMode m) => m switch
    {
        FanMode.Silent => "Leise",
        FanMode.Standard => "Standard",
        FanMode.Turbo => "Turbo",
        FanMode.FullSpeed => "Volle Geschwindigkeit",
        _ => "Benutzerdefiniert",
    };

    /// <summary>Curve currently in effect for a fan.
    /// Kurve, die für einen Lüfter gerade gilt.</summary>
    public List<FanPoint> CurveFor(FanHeader fan)
    {
        if (Mode == FanMode.Custom && Curves.TryGetValue(fan.Id, out var c) && c.Count == 4) return Normalize(c);
        if (ProfileOverride(Mode, fan.Id) is { } own) return Normalize(own);
        return Preset(Mode, fan, Tuning.GetValueOrDefault(fan.Id));
    }

    /// <summary>Can the curve be tweaked per fan in this mode? (Not at "Full speed".)
    /// Lässt sich die Kurve in diesem Modus je Lüfter anpassen? (Nicht bei „Volle Geschwindigkeit“.)</summary>
    public static bool IsEditable(FanMode mode) => mode != FanMode.FullSpeed;

    /// <summary>Custom tweak of mode <paramref name="mode"/> for a fan; null = computed curve.
    /// Eigene Anpassung des Modus <paramref name="mode"/> für einen Lüfter; null = berechnete Kurve.</summary>
    public List<FanPoint>? ProfileOverride(FanMode mode, string fanId) =>
        mode is FanMode.Custom or FanMode.FullSpeed ? null
        : ProfileCurves.TryGetValue(mode.ToString(), out var m) && m.TryGetValue(fanId, out var c) && c.Count == 4 ? c : null;

    /// <summary>Set a fan's curve in the current mode (custom or mode tweak).
    /// Kurve eines Lüfters im aktuellen Modus setzen (Benutzerdefiniert bzw. Anpassung).</summary>
    public void SetCurve(string fanId, List<FanPoint> curve)
    {
        if (Mode == FanMode.Custom) { Curves[fanId] = Normalize(curve); return; }
        if (!IsEditable(Mode)) return;
        if (!ProfileCurves.TryGetValue(Mode.ToString(), out var m)) ProfileCurves[Mode.ToString()] = m = new();
        m[fanId] = Normalize(curve);
    }

    /// <summary>Reset a fan's curve in the current mode (mode: computed curve; custom: default).
    /// Kurve eines Lüfters im aktuellen Modus zurücksetzen (Modus: berechnete Kurve; Benutzerdefiniert: Standard).</summary>
    public void ResetCurve(FanHeader fan)
    {
        if (Mode == FanMode.Custom) Curves[fan.Id] = Preset(FanMode.Standard, fan, Tuning.GetValueOrDefault(fan.Id));
        else if (ProfileCurves.TryGetValue(Mode.ToString(), out var m)) m.Remove(fan.Id);
    }

    /// <summary>Was this fan's fixed curve computed from auto-tuning?
    /// Wurde die feste Kurve dieses Lüfters aus dem Auto-Tuning berechnet?</summary>
    public static bool IsTuned(FanMode mode, FanTuning? tuning) =>
        mode is not (FanMode.FullSpeed or FanMode.Custom) && tuning != null && tuning.Rpm.Length >= 11 && tuning.MaxRpm > 0;

    /// <summary>
    /// Fixed curve after auto-tuning, built like the profiles ASUS creates after its calibration (docs/hardware.md §2.5).
    /// Feste Kurve nach dem Auto-Tuning, aufgebaut wie die Profile, die ASUS nach seiner Kalibrierung erzeugt (docs/hardware.md §2.5).
    /// <list type="bullet">
    /// <item>First point at the lowest useful duty: safe running/start, or for fans that never stop, where they get faster than idle.
    /// Erster Punkt bei der kleinsten sinnvollen Leistung: sicher laufen/anlaufen, bei Lüftern ohne Stillstand ab wann sie schneller werden.</item>
    /// <item>Modes shift the curve in temperature (Silent later, Turbo earlier) and aim mid-curve at a share of the measured max RPM.
    /// Die Modi verschieben die Kurve über die Temperatur (Leise später, Turbo früher) und zielen mittig auf einen Anteil der Höchstdrehzahl.</item>
    /// <item>Pump: own curve per mode, Silent throttled most, Standard slightly, Turbo almost full (never below 50 %).
    /// Pumpe: eigene Kurve je Modus, Leise am stärksten gedrosselt, Standard leicht, Turbo fast voll (nie unter 50 %).</item>
    /// </list>
    /// Without measurement the fixed curves of <see cref="Preset(FanMode, FanHeader)"/> apply.
    /// Ohne Messung gelten die festen Kurven aus <see cref="Preset(FanMode, FanHeader)"/>.
    /// </summary>
    public static List<FanPoint> Preset(FanMode mode, FanHeader fan, FanTuning? tuning)
    {
        if (!IsTuned(mode, tuning)) return Normalize(Preset(mode, fan));
        var t = tuning!;
        // Temperatures and share of max RPM per point; 0 = lowest useful duty
        // Temperaturen und Anteil der Höchstdrehzahl je Punkt; 0 = kleinste sinnvolle Leistung
        (int[] temps, int[] share) = (fan.Pump, mode) switch
        {
            (true, FanMode.Silent) => (new[] { 50, 65, 75, 85 }, new[] { 65, 80, 100, 100 }),
            (true, FanMode.Standard) => (new[] { 40, 60, 70, 80 }, new[] { 80, 90, 100, 100 }),
            (true, _) => (new[] { 35, 50, 65, 75 }, new[] { 90, 100, 100, 100 }),
            (false, FanMode.Silent) => (new[] { 50, 70, 80, 90 }, new[] { 0, 40, 75, 100 }),
            (false, FanMode.Turbo) => (new[] { 30, 50, 70, 80 }, new[] { 0, 65, 100, 100 }),
            _ => (new[] { 40, 60, 75, 85 }, new[] { 0, 50, 85, 100 }),
        };
        int low = Math.Max(t.SafeDuty, t.EffectiveDuty);
        if (fan.Pump) low = Math.Max(low, 50);
        var points = Enumerable.Range(0, 4)
            .Select(i => new FanPoint(temps[i], Math.Max(low, share[i] == 0 ? low : t.DutyForRpm(t.MaxRpm * share[i] / 100))))
            .ToList();
        return Normalize(points);
    }

    /// <summary>
    /// Fixed mode curves without measurement; the pump has its own per mode (Silent throttled, Standard slightly, Turbo almost full).
    /// Feste Kurven der Modi ohne Messung; die Pumpe hat je Modus eine eigene (Leise gedrosselt, Standard leicht, Turbo fast voll).
    /// </summary>
    public static List<FanPoint> Preset(FanMode mode, FanHeader fan)
    {
        if (mode == FanMode.FullSpeed) return [new(40, 100), new(60, 100), new(75, 100), new(85, 100)];
        if (fan.Pump)
            return mode switch
            {
                FanMode.Silent => [new(50, 70), new(65, 85), new(75, 100), new(85, 100)],
                FanMode.Turbo => [new(35, 90), new(50, 100), new(65, 100), new(75, 100)],
                _ => [new(40, 80), new(60, 90), new(70, 100), new(80, 100)],
            };
        return mode switch
        {
            FanMode.Silent => [new(45, 20), new(65, 35), new(78, 65), new(88, 100)],
            FanMode.Turbo => [new(35, 45), new(55, 70), new(70, 100), new(80, 100)],
            _ => [new(40, 30), new(60, 50), new(75, 80), new(85, 100)],
        };
    }

    /// <summary>
    /// Make a curve safe: four points, temperatures 20–95 °C rising (≥ 1 °C apart), duty 0–100 % not falling, last point 100 %.
    /// Kurve sicher machen: vier Punkte, Temperaturen 20–95 °C steigend (≥ 1 °C Abstand), Leistung 0–100 % nicht fallend, letzter Punkt 100 %.
    /// </summary>
    public static List<FanPoint> Normalize(IReadOnlyList<FanPoint> points)
    {
        var p = points.Take(4).Select(x => new FanPoint(x.Temp, x.Duty)).ToList();
        while (p.Count < 4) p.Add(new FanPoint(p.Count == 0 ? 40 : p[^1].Temp + 10, 100));
        for (int i = 0; i < 4; i++)
        {
            int minT = i == 0 ? MinTemp : p[i - 1].Temp + 1;
            int maxT = MaxTemp - (3 - i);
            p[i].Temp = Math.Clamp(p[i].Temp, minT, maxT);
            p[i].Duty = Math.Clamp(p[i].Duty, i == 0 ? 0 : p[i - 1].Duty, 100);
        }
        p[3].Duty = 100;
        return p;
    }

    /// <summary>Duty at a temperature (linear between points, like the chip).
    /// Leistung bei einer Temperatur (linear zwischen den Punkten, wie der Chip).</summary>
    public static int DutyAt(IReadOnlyList<FanPoint> c, double temp)
    {
        if (temp <= c[0].Temp) return c[0].Duty;
        for (int i = 1; i < c.Count; i++)
            if (temp <= c[i].Temp)
                return (int)Math.Round(c[i - 1].Duty + (c[i].Duty - c[i - 1].Duty) * (temp - c[i - 1].Temp) / (c[i].Temp - c[i - 1].Temp));
        return c[^1].Duty;
    }

    public FanSettings Clone() => new()
    {
        Enabled = Enabled, DisableAsusFanService = DisableAsusFanService, Mode = Mode,
        Curves = Curves.ToDictionary(k => k.Key, k => k.Value.Select(p => new FanPoint(p.Temp, p.Duty)).ToList()),
        Tuning = Tuning.ToDictionary(k => k.Key, k => k.Value.Clone()),
        ProfileCurves = ProfileCurves.ToDictionary(m => m.Key,
            m => m.Value.ToDictionary(k => k.Key, k => k.Value.Select(p => new FanPoint(p.Temp, p.Duty)).ToList())),
        Sources = new(Sources),
    };
}

/// <summary>Auto-tuning result for one fan.
/// Ergebnis des Auto-Tunings für einen Lüfter.</summary>
public sealed class FanTuning
{
    public DateTime Time { get; set; }
    /// <summary>RPM at equal duty steps 0–100 % (21 values = 5 % steps; older runs 11).
    /// Drehzahl in gleichen Leistungsstufen 0–100 % (21 Werte = 5-%-Stufen; ältere Läufe 11).</summary>
    public int[] Rpm { get; set; } = [];
    /// <summary>Lowest duty at which the fan keeps running (0 = never stops).
    /// Kleinste Leistung, bei der der Lüfter sicher weiterläuft (0 = bleibt nie stehen).</summary>
    public int MinDuty { get; set; }
    /// <summary>RPM at <see cref="MinDuty"/>.
    /// Drehzahl bei <see cref="MinDuty"/>.</summary>
    public int MinDutyRpm { get; set; }
    /// <summary>Duty at which it starts from standstill (0 = never stops).
    /// Leistung, ab der er aus dem Stand anläuft (0 = bleibt nie stehen).</summary>
    public int StartDuty { get; set; }

    [System.Text.Json.Serialization.JsonIgnore] public int MaxRpm => Rpm.Length > 0 ? Rpm.Max() : 0;
    /// <summary>Lower limit for fixed curves: safe start and run, plus 5 % margin.
    /// Untergrenze für feste Kurven: sicher anlaufen und weiterlaufen, plus 5 % Reserve.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public int SafeDuty => MinDuty == 0 && StartDuty == 0 ? 0 : Math.Min(100, Math.Max(MinDuty, StartDuty) + 5);

    /// <summary>Estimated RPM at a duty (linear between measurements).
    /// Geschätzte Drehzahl bei einer Leistung (linear zwischen den Messpunkten).</summary>
    public int RpmAt(int duty)
    {
        if (Rpm.Length < 2) return 0;
        double x = Math.Clamp(duty, 0, 100) / (100.0 / (Rpm.Length - 1));
        int i = Math.Min((int)x, Rpm.Length - 2);
        return (int)Math.Round(Rpm[i] + (Rpm[i + 1] - Rpm[i]) * (x - i));
    }

    /// <summary>
    /// For fans that never stop: duty above which they clearly exceed their idle RPM (below, less duty does nothing;
    /// ASUS uses 13 % for the CPU fan). 0 for fans that can stop (then <see cref="MinDuty"/>/<see cref="StartDuty"/> apply).
    /// Für Lüfter ohne Stillstand: Leistung, ab der sie deutlich über ihre Grunddrehzahl kommen (darunter bringt weniger nichts;
    /// ASUS nimmt 13 % beim CPU-Lüfter). 0 bei Lüftern, die stehen können (dann gelten <see cref="MinDuty"/>/<see cref="StartDuty"/>).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int EffectiveDuty
    {
        get
        {
            if (Rpm.Length < 2 || Rpm[0] == 0) return 0;
            int threshold = Rpm[0] + Math.Max(50, Rpm[0] * 15 / 100);
            return Rpm.Max() < threshold ? 0 : DutyForRpm(threshold);
        }
    }

    /// <summary>Lowest duty that reaches at least <paramref name="rpm"/> RPM per measurement (linear between steps).
    /// Kleinste Leistung, die laut Messung mindestens <paramref name="rpm"/> U/min erreicht (linear zwischen den Stufen).</summary>
    public int DutyForRpm(int rpm)
    {
        if (Rpm.Length < 2) return 100;
        double step = 100.0 / (Rpm.Length - 1);
        if (rpm <= Rpm[0]) return 0;
        for (int i = 1; i < Rpm.Length; i++)
        {
            if (Rpm[i] < rpm) continue;
            int lo = Rpm[i - 1], hi = Rpm[i];
            double frac = hi > lo ? (rpm - lo) / (double)(hi - lo) : 1;
            return (int)Math.Ceiling((i - 1 + Math.Clamp(frac, 0, 1)) * step);
        }
        return 100;
    }

    public FanTuning Clone() => new() { Time = Time, Rpm = (int[])Rpm.Clone(), MinDuty = MinDuty, MinDutyRpm = MinDutyRpm, StartDuty = StartDuty };
}
