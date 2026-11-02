using Borealis.Core.Hardware;

namespace Borealis.Core.Profiles;

/// <summary>Lüftermodus: feste Kurven für alle Lüfter oder eigene Kurven je Lüfter.</summary>
public enum FanMode { Silent, Standard, Turbo, FullSpeed, Custom }

/// <summary>Kurvenpunkt: ab <see cref="Temp"/> °C läuft der Lüfter mit <see cref="Duty"/> % (dazwischen linear).</summary>
public sealed class FanPoint
{
    public int Temp { get; set; }
    public int Duty { get; set; }

    public FanPoint() { }
    public FanPoint(int temp, int duty) { Temp = temp; Duty = duty; }
}

/// <summary>
/// Lüftersteuerung (gilt für alle Beleuchtungsprofile). Borealis schreibt die Kurven in den Lüfter-Chip; der regelt
/// danach selbst nach der CPU-Temperatur. Solange <see cref="Enabled"/> aus ist, bleibt alles beim ASUS-Lüfterdienst.
/// </summary>
public sealed class FanSettings
{
    /// <summary>Borealis steuert die Lüfter; der ASUS-Lüfterdienst wird dafür angehalten.</summary>
    public bool Enabled { get; set; }
    /// <summary>ASUS-Lüfterdienst zusätzlich auf „Deaktiviert“ stellen, damit Armoury Crate ihn nicht neu startet.</summary>
    public bool DisableAsusFanService { get; set; }
    public FanMode Mode { get; set; } = FanMode.Standard;
    /// <summary>Eigene Kurven je Lüfter (Kennung wie „CPUFAN“), je vier Punkte; gilt im Modus <see cref="FanMode.Custom"/>.</summary>
    public Dictionary<string, List<FanPoint>> Curves { get; set; } = new();
    /// <summary>Messwerte des Auto-Tunings je Lüfter (Kennung wie „CPUFAN“).</summary>
    public Dictionary<string, FanTuning> Tuning { get; set; } = new();
    /// <summary>
    /// Eigene Anpassungen der festen Profile je Lüfter: Modus („Silent“, „Standard“, „Turbo“) → Lüfter → vier Punkte.
    /// Fehlt ein Eintrag, gilt die berechnete Kurve (<see cref="Preset(FanMode, FanHeader, FanTuning?)"/>).
    /// </summary>
    public Dictionary<string, Dictionary<string, List<FanPoint>>> ProfileCurves { get; set; } = new();

    public const int MinTemp = 20, MaxTemp = 95;

    /// <summary>Name in der Sprache der Oberfläche.</summary>
    public static string DisplayName(FanMode m) => Localization.Loc.Term(GermanName(m));

    public static string GermanName(FanMode m) => m switch
    {
        FanMode.Silent => "Leise",
        FanMode.Standard => "Standard",
        FanMode.Turbo => "Turbo",
        FanMode.FullSpeed => "Volle Geschwindigkeit",
        _ => "Benutzerdefiniert",
    };

    /// <summary>Kurve, die für einen Lüfter gerade gilt.</summary>
    public List<FanPoint> CurveFor(FanHeader fan)
    {
        if (Mode == FanMode.Custom && Curves.TryGetValue(fan.Id, out var c) && c.Count == 4) return Normalize(c);
        if (ProfileOverride(Mode, fan.Id) is { } own) return Normalize(own);
        return Preset(Mode, fan, Tuning.GetValueOrDefault(fan.Id));
    }

    /// <summary>Lässt sich die Kurve in diesem Modus je Lüfter anpassen? (Nicht bei „Volle Geschwindigkeit“.)</summary>
    public static bool IsEditable(FanMode mode) => mode != FanMode.FullSpeed;

    /// <summary>Eigene Anpassung des Profils <paramref name="mode"/> für einen Lüfter; null = berechnete Kurve.</summary>
    public List<FanPoint>? ProfileOverride(FanMode mode, string fanId) =>
        mode is FanMode.Custom or FanMode.FullSpeed ? null
        : ProfileCurves.TryGetValue(mode.ToString(), out var m) && m.TryGetValue(fanId, out var c) && c.Count == 4 ? c : null;

    /// <summary>Kurve eines Lüfters im aktuellen Modus setzen (Benutzerdefiniert bzw. Anpassung des Profils).</summary>
    public void SetCurve(string fanId, List<FanPoint> curve)
    {
        if (Mode == FanMode.Custom) { Curves[fanId] = Normalize(curve); return; }
        if (!IsEditable(Mode)) return;
        if (!ProfileCurves.TryGetValue(Mode.ToString(), out var m)) ProfileCurves[Mode.ToString()] = m = new();
        m[fanId] = Normalize(curve);
    }

    /// <summary>Kurve eines Lüfters im aktuellen Modus zurücksetzen (Profil: berechnete Kurve; Benutzerdefiniert: Standard).</summary>
    public void ResetCurve(FanHeader fan)
    {
        if (Mode == FanMode.Custom) Curves[fan.Id] = Preset(FanMode.Standard, fan, Tuning.GetValueOrDefault(fan.Id));
        else if (ProfileCurves.TryGetValue(Mode.ToString(), out var m)) m.Remove(fan.Id);
    }

    /// <summary>Wurde die feste Kurve dieses Lüfters aus dem Auto-Tuning berechnet?</summary>
    public static bool IsTuned(FanMode mode, FanTuning? tuning) =>
        mode is not (FanMode.FullSpeed or FanMode.Custom) && tuning != null && tuning.Rpm.Length >= 11 && tuning.MaxRpm > 0;

    /// <summary>
    /// Feste Kurve nach dem Auto-Tuning, aufgebaut wie die Profile, die Armoury Crate nach seiner Kalibrierung erzeugt
    /// (docs/hardware.md §2.5):
    /// <list type="bullet">
    /// <item>Der erste Punkt liegt auf der kleinsten sinnvollen Leistung des Lüfters: sicher laufen bzw. anlaufen, und bei
    /// Lüftern, die nie stehen bleiben, die Leistung, ab der sie über ihre Grunddrehzahl hinaus schneller werden.</item>
    /// <item>Die Modi verschieben die Kurve über die Temperatur (Leise später, Turbo früher) und zielen in der Mitte auf
    /// einen Anteil der gemessenen Höchstdrehzahl; die Leistung dafür kommt aus der Messtabelle.</item>
    /// <item>Pumpe: jeder Modus mit eigener Kurve – „Leise“ am stärksten gedrosselt, „Standard“ leicht, „Turbo“ fast voll
    /// (nie unter 50 %).</item>
    /// </list>
    /// Ohne Messung gelten die festen Kurven aus <see cref="Preset(FanMode, FanHeader)"/>.
    /// </summary>
    public static List<FanPoint> Preset(FanMode mode, FanHeader fan, FanTuning? tuning)
    {
        if (!IsTuned(mode, tuning)) return Normalize(Preset(mode, fan));
        var t = tuning!;
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
    /// Feste Kurven der Modi ohne Messung. Die Pumpe hat je Modus eine eigene Kurve (Leise gedrosselt, Standard leicht,
    /// Turbo fast voll); erst „Volle Geschwindigkeit“ läuft immer voll.
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
    /// Kurve in sichere Form bringen: vier Punkte, Temperaturen 20–95 °C aufsteigend (mindestens 1 °C Abstand),
    /// Leistung 0–100 % und nicht fallend, letzter Punkt immer 100 % (wie im ASUS-Dienst). Über dem letzten Punkt läuft
    /// der Lüfter damit sicher voll.
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

    /// <summary>Leistung bei einer Temperatur (wie der Chip zwischen den Punkten rechnet, linear).</summary>
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
    };
}

/// <summary>Ergebnis des Auto-Tunings für einen Lüfter.</summary>
public sealed class FanTuning
{
    public DateTime Time { get; set; }
    /// <summary>Drehzahl in gleichen Leistungsstufen von 0 bis 100 % (21 Werte = 5-%-Stufen; ältere Läufe 11 Werte).</summary>
    public int[] Rpm { get; set; } = [];
    /// <summary>Kleinste Leistung, bei der der Lüfter noch sicher weiterläuft (0 = bleibt nie stehen).</summary>
    public int MinDuty { get; set; }
    /// <summary>Drehzahl bei <see cref="MinDuty"/>.</summary>
    public int MinDutyRpm { get; set; }
    /// <summary>Leistung, ab der er aus dem Stand anläuft (0 = bleibt nie stehen).</summary>
    public int StartDuty { get; set; }

    [System.Text.Json.Serialization.JsonIgnore] public int MaxRpm => Rpm.Length > 0 ? Rpm.Max() : 0;
    /// <summary>Untergrenze für feste Kurven: anlaufen und weiterlaufen sicher, plus 5 % Reserve.</summary>
    [System.Text.Json.Serialization.JsonIgnore] public int SafeDuty => MinDuty == 0 && StartDuty == 0 ? 0 : Math.Min(100, Math.Max(MinDuty, StartDuty) + 5);

    /// <summary>Geschätzte Drehzahl bei einer Leistung (zwischen den Messpunkten linear).</summary>
    public int RpmAt(int duty)
    {
        if (Rpm.Length < 2) return 0;
        double x = Math.Clamp(duty, 0, 100) / (100.0 / (Rpm.Length - 1));
        int i = Math.Min((int)x, Rpm.Length - 2);
        return (int)Math.Round(Rpm[i] + (Rpm[i + 1] - Rpm[i]) * (x - i));
    }

    /// <summary>
    /// Für Lüfter, die nie stehen bleiben: Leistung, ab der sie deutlich über ihre Grunddrehzahl (bei 0 %) hinaus schneller
    /// werden – darunter bringt weniger Leistung nichts (ASUS: „mindutytokeepruning“ 13 % beim CPU-Lüfter). 0 bei Lüftern,
    /// die stehen bleiben können (dort gilt <see cref="MinDuty"/>/<see cref="StartDuty"/>).
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

    /// <summary>Kleinste Leistung, bei der laut Messung mindestens <paramref name="rpm"/> U/min erreicht werden (linear zwischen den Stufen).</summary>
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
