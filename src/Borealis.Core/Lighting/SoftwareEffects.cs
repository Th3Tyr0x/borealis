using Borealis.Core.Profiles;

namespace Borealis.Core.Lighting;

/// <summary>What an effect lets you set (controls which sliders the UI shows).
/// Was ein Effekt einstellen lässt (steuert, welche Regler die Oberfläche zeigt).</summary>
public sealed record EffectCaps(bool Color, bool Speed, bool Direction, bool Size, bool Density, bool Background,
    string SizeLabel = "Größe", bool TwoColors = false, string Color1Label = "Farbe 1", string Color2Label = "Farbe 2",
    string[]? Modes = null);

/// <summary>Live signals for reactive effects, set by the service per frame.
/// Live-Signale für reaktive Effekte, vom Dienst je Bild gesetzt.</summary>
public sealed class EffectInputs
{
    /// <summary>Overall playback level 0–1 (auto gain, smoothed).
    /// Gesamtpegel der Wiedergabe 0–1 (automatisch verstärkt, geglättet).</summary>
    public double AudioLevel { get; set; }
    /// <summary>Bass, mids, highs 0–1.
    /// Bass, Mitten, Höhen 0–1.</summary>
    public double Bass { get; set; }
    public double Mid { get; set; }
    public double Treble { get; set; }
    /// <summary>16 bands from 40 Hz to 16 kHz, each 0–1.
    /// 16 Frequenzbänder von 40 Hz bis 16 kHz, je 0–1.</summary>
    public double[] Bands { get; set; } = new double[16];
    /// <summary>Beat pulse: 1 at the beat, then decays.
    /// Beat-Impuls: 1 im Moment des Beats, klingt danach ab.</summary>
    public double Beat { get; set; }
    /// <summary>Number of detected beats (for color change per beat).
    /// Anzahl erkannter Beats (für Farbwechsel je Beat).</summary>
    public long BeatCount { get; set; }
    /// <summary>Seconds since the last beats, newest first (at most 6).
    /// Sekunden seit den letzten Beats, neuester zuerst (höchstens 6).</summary>
    public double[] BeatAges { get; set; } = [];
    /// <summary>Estimated tempo in BPM (0 = unknown).
    /// Geschätztes Tempo in Schlägen je Minute (0 = unbekannt).</summary>
    public double Bpm { get; set; }
    /// <summary>Position in the current bar 0–1 (from tempo and last beat).
    /// Position im aktuellen Takt 0–1 (aus Tempo und letztem Beat).</summary>
    public double BeatPhase { get; set; }
    /// <summary>"Drop": sudden strong energy rise, 1 at the drop, then decays.
    /// „Drop“: plötzlicher starker Energieanstieg, 1 im Moment des Drops, klingt ab.</summary>
    public double Drop { get; set; }
    /// <summary>Slowly falling peaks (level and per band) for peak dots.
    /// Langsam fallende Spitzenwerte (Pegel und je Band) für Spitzenpunkte.</summary>
    public double LevelPeak { get; set; }
    public double[] BandPeaks { get; set; } = new double[16];
    /// <summary>Timbre/pitch: spectral centroid 0 (low) – 1 (high).
    /// Klangfarbe/Tonhöhe: Schwerpunkt des Spektrums 0 (tief) – 1 (hoch).</summary>
    public double Centroid { get; set; }
    /// <summary>CPU load 0–1 (smoothed).
    /// CPU-Auslastung 0–1 (geglättet).</summary>
    public double CpuLoad { get; set; }
    /// <summary>RAM usage 0–1 (smoothed).
    /// RAM-Belegung 0–1 (geglättet).</summary>
    public double RamLoad { get; set; }

    public static readonly EffectInputs None = new();
}

/// <summary>
/// Effects computed by Borealis and streamed as direct colors (§5). Stateless: each frame depends only on time (and live
/// signals), so all devices stay in sync. Randomness comes from a hash over zone, LED and time slot.
/// Effekte, die Borealis berechnet und als Direct-Farben streamt (§5). Zustandslos: Jedes Bild hängt nur von der Zeit (und
/// den Live-Signalen) ab, daher laufen alle Geräte synchron. Zufall kommt aus einem Hash über Zone, LED und Zeitabschnitt.
/// </summary>
public static class SoftwareEffects
{
    /// <summary>"Music" variants (value of <c>Mode</c>).
    /// Unterarten von „Musik“ (Wert von <c>Mode</c>).</summary>
    public static readonly string[] MusicModes =
    [
        "Pegel", "Spektrum", "Spektrum gespiegelt", "Puls", "Bass",
        "Welle im Takt", "Lauflicht im Takt", "Funken im Takt", "Tonhöhe", "Drop-Strobo",
    ];

    static readonly Dictionary<FirmwareEffect, EffectCaps> Caps = new()
    {
        [FirmwareEffect.Static] = new(true, false, false, false, false, false),
        [FirmwareEffect.Breathing] = new(true, true, false, false, false, true),
        [FirmwareEffect.Strobing] = new(true, true, false, false, false, true),
        [FirmwareEffect.ColorCycle] = new(false, true, false, false, false, false),
        [FirmwareEffect.Rainbow] = new(false, true, true, true, false, false, "Breite"),
        [FirmwareEffect.Comet] = new(true, true, true, true, false, true, "Schweiflänge"),
        [FirmwareEffect.FlashAndDash] = new(true, true, true, true, false, true, "Länge"),
        [FirmwareEffect.Wave] = new(true, true, true, true, false, true, "Wellenlänge"),
        [FirmwareEffect.GlowingYoyo] = new(true, true, false, true, false, true),
        [FirmwareEffect.StarryNight] = new(true, true, false, false, true, true),
        [FirmwareEffect.BreathRainbow] = new(false, true, true, true, false, false, "Breite"),
        [FirmwareEffect.RainbowComet] = new(false, true, true, true, false, true, "Schweiflänge"),
        [FirmwareEffect.MovingFlash] = new(true, true, true, true, true, true, "Länge"),
        [FirmwareEffect.Fireworks] = new(true, true, false, true, true, true),
        [FirmwareEffect.Music] = new(true, true, true, true, true, true, "Empfindlichkeit", Modes: MusicModes),
        [FirmwareEffect.Smart] = new(true, false, false, false, false, true, TwoColors: true, Color1Label: "Niedrig", Color2Label: "Hoch",
            Modes: ["CPU · Farbe", "CPU · Balken", "RAM · Farbe", "RAM · Balken"]),
        [FirmwareEffect.Fire] = new(false, true, false, true, false, false, "Flammenhöhe"),
        [FirmwareEffect.Meteor] = new(true, true, true, true, true, true, "Schweiflänge"),
        [FirmwareEffect.Scanner] = new(true, true, false, true, false, true),
        [FirmwareEffect.Plasma] = new(false, true, false, true, false, false, "Muster"),
        [FirmwareEffect.Heartbeat] = new(true, true, false, false, false, true),
        [FirmwareEffect.Twinkle] = new(true, true, false, false, true, true),
        [FirmwareEffect.Off] = new(false, false, false, false, false, false),
    };

    public static EffectCaps CapsOf(FirmwareEffect e) => Caps.TryGetValue(e, out var c) ? c : Caps[FirmwareEffect.Static];

    /// <summary>Effect groups for the UI (tile order).
    /// Effektgruppen für die Oberfläche (Reihenfolge der Kacheln).</summary>
    public static readonly (string Title, FirmwareEffect[] Effects)[] Sections =
    [
        ("Grundeffekte", [FirmwareEffect.Static, FirmwareEffect.Breathing, FirmwareEffect.Strobing, FirmwareEffect.ColorCycle,
                          FirmwareEffect.Rainbow, FirmwareEffect.BreathRainbow, FirmwareEffect.StarryNight]),
        ("Bewegung", [FirmwareEffect.Comet, FirmwareEffect.RainbowComet, FirmwareEffect.FlashAndDash, FirmwareEffect.MovingFlash,
                      FirmwareEffect.Wave, FirmwareEffect.GlowingYoyo, FirmwareEffect.Meteor, FirmwareEffect.Scanner]),
        ("Lichtspiele", [FirmwareEffect.Fire, FirmwareEffect.Fireworks, FirmwareEffect.Plasma, FirmwareEffect.Heartbeat, FirmwareEffect.Twinkle]),
        ("Reaktiv", [FirmwareEffect.Music, FirmwareEffect.Smart]),
    ];

    public static bool HasColor(FirmwareEffect e) => CapsOf(e).Color;
    public static bool HasSpeed(FirmwareEffect e) => CapsOf(e).Speed;
    public static bool HasDirection(FirmwareEffect e) => CapsOf(e).Direction;

    /// <summary>Effects without a firmware counterpart (always software).
    /// Effekte ohne Firmware-Gegenstück (immer in Software).</summary>
    public static bool IsSoftwareOnly(FirmwareEffect e) => (byte)e is >= 0x80 and < 0xFF;

    /// <summary>Hardware effect shown at boot until Borealis takes over.
    /// Hardware-Effekt beim Hochfahren, bis Borealis übernimmt.</summary>
    public static FirmwareEffect FirmwareFallback(FirmwareEffect e) => e switch
    {
        FirmwareEffect.BreathRainbow or FirmwareEffect.Plasma => FirmwareEffect.Rainbow,
        FirmwareEffect.RainbowComet or FirmwareEffect.Meteor => FirmwareEffect.Comet,
        FirmwareEffect.MovingFlash => FirmwareEffect.FlashAndDash,
        FirmwareEffect.Fireworks or FirmwareEffect.Twinkle => FirmwareEffect.StarryNight,
        FirmwareEffect.Scanner => FirmwareEffect.GlowingYoyo,
        FirmwareEffect.Heartbeat => FirmwareEffect.Breathing,
        FirmwareEffect.Music or FirmwareEffect.Smart or FirmwareEffect.Fire or FirmwareEffect.Direct => FirmwareEffect.Static,
        _ => e,
    };

    /// <summary>Cycles per second for level 1–10 (1 ≈ every 12 s, 5 ≈ every 3 s, 10 ≈ 2 per second).
    /// Durchläufe je Sekunde für Stufe 1–10 (1 ≈ alle 12 s, 5 ≈ alle 3 s, 10 ≈ 2 je Sekunde).</summary>
    public static double Rate(int speed) => 0.08 * Math.Pow(1.42, Math.Clamp(speed, 1, 10) - 1);

    /// <summary>Compute one frame for a zone.
    /// Ein Bild für eine Zone berechnen.</summary>
    /// <param name="zoneId">Zone ID (color per zone, random seed). / Zonen-ID (Farbe je Zone, Startwert für Zufall).</param>
    /// <param name="t">Seconds since service start. / Sekunden seit Dienststart.</param>
    /// <param name="brightness">Brightness 0–1. / Helligkeit 0–1.</param>
    public static void Render(GroupSetting s, string zoneId, double t, double brightness, Span<Rgb> leds, EffectInputs? inputs = null)
    {
        int n = leds.Length;
        if (n == 0) return;
        inputs ??= EffectInputs.None;
        int seed = StableHash(zoneId);
        // In Sync, random colors change on all devices at once; stars etc. stay random per LED
        // In Sync wechseln Zufallsfarben auf allen Geräten gleichzeitig; Sterne usw. bleiben je LED zufällig
        int zoneSeed = s.Synced ? 0x5A17C0DE : seed;
        double f = Rate(s.EffectiveSpeed);
        int dir = s.Direction == EffectDirection.Left ? -1 : 1;
        double size = Math.Clamp(s.Size, 1, 10);
        double density = Math.Clamp(s.Density, 1, 10);
        double sizeFrac = 0.04 + 0.06 * size; // Share of zone length: 0.1 … 0.64 / Anteil der Zonenlänge
        var c1 = s.ColorOf(zoneId);
        var c2 = s.SecondColor;
        double interval = Math.Clamp(s.ChangeInterval, 1, 60);
        double fade = Math.Clamp(s.Transition, 0, 10) / 10.0;

        // Random color per lane: holds, then glides around the hue circle to the next one at the end of the interval
        // Zufallsfarbe je Spur: hält und gleitet am Ende des Intervalls über den Farbkreis zur nächsten
        Rgb RandomColor(int sd, int lane)
        {
            double offset = lane == 0 ? 0 : Hash01(sd, lane, -11) * interval;
            double ph = (t + offset) / interval;
            long k = (long)Math.Floor(ph);
            double local = Frac(ph);
            double h0 = Hash01(sd, lane, k), h1 = Hash01(sd, lane, k + 1);
            double x = fade <= 0 || local < 1 - fade ? 0 : SmoothStep((local - (1 - fade)) / fade);
            double dh = h1 - h0;
            if (dh > 0.5) dh -= 1; else if (dh < -0.5) dh += 1;
            return Rgb.FromHue(h0 + dh * x);
        }
        // Color by color mode; perLed = own random lane per LED (stars, sparkle …), else one per zone
        // Farbe nach Farbmodus; perLed = eigene Zufallsspur je LED (Sterne, Funkeln …), sonst eine je Zone
        Rgb ColorFor(int lane, double gradientPos, bool perLed = false) => s.ColorMode switch
        {
            ColorMode.Random => perLed ? RandomColor(seed, lane + 1) : RandomColor(zoneSeed, 0),
            ColorMode.Gradient => Lerp(c1, c2, gradientPos),
            _ => c1,
        };
        // Intensity v between background and color, then brightness; background: off = black, color, or slow dimmed rainbow
        // Intensität v zwischen Hintergrund und Farbe, dann Helligkeit; Hintergrund: aus = schwarz, Farbe oder gedimmter Regenbogen
        var bgRow = BackgroundRow(s, n, t);
        Rgb ShadeAt(int i, Rgb c, double v) => Scale(Lerp(bgRow[i], c, v), brightness);
        double Pos(int i) => n > 1 ? (double)i / (n - 1) : 0;
        int Index(int i) => dir > 0 ? i : n - 1 - i;

        switch (s.Effect)
        {
            case FirmwareEffect.Off:
                leds.Fill(Rgb.Black);
                return;

            case FirmwareEffect.Static:
                for (int i = 0; i < n; i++) leds[i] = Scale(ColorFor(i, Pos(i), perLed: true), brightness);
                return;

            case FirmwareEffect.Breathing:
            {
                double v = (1 - Math.Cos(2 * Math.PI * Frac(t * f))) / 2;
                v *= v;
                for (int i = 0; i < n; i++) leds[i] = ShadeAt(i, ColorFor(0, Pos(i)), v);
                return;
            }

            case FirmwareEffect.Strobing:
            {
                double v = Frac(t * f * 2) < 0.18 ? 1 : 0;
                for (int i = 0; i < n; i++) leds[i] = ShadeAt(i, ColorFor(0, Pos(i)), v);
                return;
            }

            case FirmwareEffect.ColorCycle:
            {
                var c = Rgb.FromHue(t * f * 0.5);
                for (int i = 0; i < n; i++) leds[i] = Scale(c, brightness);
                return;
            }

            case FirmwareEffect.Rainbow:
            {
                double spread = 0.2 + size * 0.16; // Hue circles per zone / Farbkreise je Zone
                for (int i = 0; i < n; i++)
                    leds[i] = Scale(Rgb.FromHue(dir * t * f * 0.5 - (n > 1 ? (double)i / n * spread : 0)), brightness);
                return;
            }

            case FirmwareEffect.BreathRainbow:
            {
                double spread = 0.2 + size * 0.16;
                double v = (1 - Math.Cos(2 * Math.PI * Frac(t * f))) / 2;
                for (int i = 0; i < n; i++)
                    leds[i] = Scale(Rgb.FromHue(dir * t * f * 0.15 - (n > 1 ? (double)i / n * spread : 0)), v * v * brightness);
                return;
            }

            case FirmwareEffect.Comet:
            case FirmwareEffect.RainbowComet:
            {
                double tail = Math.Max(1.5, n * sizeFrac);
                double head = Frac(t * f) * (n + tail);
                bool rainbow = s.Effect == FirmwareEffect.RainbowComet;
                for (int i = 0; i < n; i++)
                {
                    double d = head - Index(i);
                    double v = d >= 0 && d <= tail ? Math.Pow(1 - d / tail, 2) : 0;
                    var c = rainbow ? Rgb.FromHue(t * f * 0.3 + d / tail * 0.8) : ColorFor(0, d / tail);
                    leds[i] = ShadeAt(i, c, v);
                }
                return;
            }

            case FirmwareEffect.FlashAndDash:
            {
                double p = Frac(t * f);
                double w = Math.Max(1, n * sizeFrac * 0.5);
                bool flash = p < 0.05 || (p >= 0.1 && p < 0.15);
                double pos = p < 0.2 ? -1 : (p - 0.2) / 0.8 * (n + w);
                for (int i = 0; i < n; i++)
                {
                    double d = pos - Index(i);
                    leds[i] = ShadeAt(i, ColorFor(0, Pos(i)), flash ? 1 : d >= 0 && d < w ? 1 : 0);
                }
                return;
            }

            case FirmwareEffect.MovingFlash:
            {
                int count = 1 + (int)(density / 3); // 1–4 flashes at once / Blitze gleichzeitig
                double w = Math.Max(1, n * sizeFrac * 0.4);
                for (int i = 0; i < n; i++)
                {
                    double v = 0; int hit = 0;
                    for (int j = 0; j < count; j++)
                    {
                        double pos = Frac(t * f * 1.5 + (double)j / count) * (n + w);
                        double d = pos - Index(i);
                        if (d >= 0 && d < w) { v = 1; hit = j; }
                    }
                    leds[i] = ShadeAt(i, ColorFor(hit, Pos(i), perLed: count > 1), v);
                }
                return;
            }

            case FirmwareEffect.Wave:
            {
                double lambda = Math.Max(3, n * sizeFrac * 1.5);
                for (int i = 0; i < n; i++)
                {
                    double v = (1 + Math.Sin(2 * Math.PI * (Index(i) / lambda - t * f))) / 2;
                    leds[i] = ShadeAt(i, ColorFor(0, s.ColorMode == ColorMode.Gradient ? v : Pos(i)), v);
                }
                return;
            }

            case FirmwareEffect.GlowingYoyo:
            {
                double ph = Frac(t * f);
                double x = ph < 0.5 ? ph * 2 : 2 - ph * 2;
                double center = x * (n - 1);
                double w = Math.Max(1.2, n * sizeFrac * 0.5);
                for (int i = 0; i < n; i++)
                {
                    double v = n == 1 ? x : Math.Pow(Math.Max(0, 1 - Math.Abs(i - center) / w), 1.5);
                    leds[i] = ShadeAt(i, ColorFor(0, x), v);
                }
                return;
            }

            case FirmwareEffect.Scanner:
            {
                double ph = Frac(t * f);
                bool forward = ph < 0.5;
                double x = forward ? ph * 2 : 2 - ph * 2;
                double pos = x * (n - 1);
                double w = Math.Max(1, n * sizeFrac * 0.3);
                for (int i = 0; i < n; i++)
                {
                    double v = Math.Max(0, 1 - Math.Abs(i - pos) / w);
                    double behind = forward ? pos - i : i - pos;
                    if (behind > 0) v = Math.Max(v, Math.Exp(-behind / (w * 3)) * 0.45);
                    leds[i] = ShadeAt(i, ColorFor(0, Pos(i)), n == 1 ? 0.5 + 0.5 * Math.Sin(2 * Math.PI * ph) : v);
                }
                return;
            }

            case FirmwareEffect.Meteor:
            {
                int count = 1 + (int)(density / 3);
                double tail = Math.Max(1.5, n * sizeFrac);
                for (int i = 0; i < n; i++)
                {
                    double best = 0; int hit = 0;
                    for (int j = 0; j < count; j++)
                    {
                        double rate = f * (0.8 + 0.5 * Hash01(seed, j, -21));
                        double head = Frac(t * rate + Hash01(seed, j, -22)) * (n + tail);
                        double d = head - Index(i);
                        if (d < 0 || d > tail) continue;
                        // Fading tail with sparkle / Zerfallender Schweif mit Funkeln
                        double v = Math.Pow(1 - d / tail, 1.5) * (d < 1 ? 1 : 0.55 + 0.45 * Hash01(seed, i, (long)(t * 12)));
                        if (v > best) { best = v; hit = j; }
                    }
                    leds[i] = ShadeAt(i, ColorFor(hit, Pos(i), perLed: count > 1), best);
                }
                return;
            }

            case FirmwareEffect.StarryNight:
            case FirmwareEffect.Twinkle:
            {
                bool twinkle = s.Effect == FirmwareEffect.Twinkle;
                double prob = (twinkle ? 0.15 : 0.08) + density * (twinkle ? 0.075 : 0.065);
                for (int i = 0; i < n; i++)
                {
                    double rate = f * (twinkle ? 2.2 : 1) * (0.6 + 0.8 * Hash01(seed, i, -7));
                    double ph = t * rate + Hash01(seed, i, -3);
                    long cycle = (long)Math.Floor(ph);
                    bool lit = Hash01(seed, i, cycle) < prob;
                    double v = lit ? Math.Pow(Math.Sin(Math.PI * Frac(ph)), twinkle ? 2 : 3) : 0;
                    leds[i] = ShadeAt(i, ColorFor(i, Hash01(seed, i, cycle + 1), perLed: true), v);
                }
                return;
            }

            case FirmwareEffect.Fireworks:
            {
                int lanes = 1 + (int)(density / 2); // 1–6 rockets at once / Raketen gleichzeitig
                for (int i = 0; i < n; i++)
                {
                    double best = 0; Rgb color = c1;
                    for (int j = 0; j < lanes; j++)
                    {
                        double ph = t * f * 0.8 + Hash01(seed, j, -31);
                        long k = (long)Math.Floor(ph);
                        double age = Frac(ph);
                        double center = Hash01(seed, j, k) * (n - 1);
                        double radius = age * Math.Max(1, n * sizeFrac) + 0.5;
                        double dist = Math.Abs(i - center);
                        if (dist > radius) continue;
                        double v = Math.Pow(1 - age, 2) * Math.Sqrt(1 - dist / (radius + 1)) * (0.6 + 0.4 * Hash01(seed, i, (long)(t * 25)));
                        if (v <= best) continue;
                        best = v;
                        color = s.ColorMode switch
                        {
                            ColorMode.Single => c1,
                            ColorMode.Gradient => Lerp(c1, c2, Hash01(seed, j, k + 7)),
                            _ => Rgb.FromHue(Hash01(seed, j, k + 3)),
                        };
                    }
                    leds[i] = ShadeAt(i, color, best);
                }
                return;
            }

            case FirmwareEffect.Music:
                RenderMusic(s, leds, inputs, t, brightness, zoneSeed, seed, c1, c2, bgRow, dir, size, density, f);
                return;

            case FirmwareEffect.Smart:
            {
                // Mode: 0 CPU color, 1 CPU bar, 2 RAM color, 3 RAM bar / Modus: 0 CPU-Farbe, 1 CPU-Balken, 2 RAM-Farbe, 3 RAM-Balken
                int mode = Math.Clamp(s.Mode, 0, 3);
                double value = mode < 2 ? inputs.CpuLoad : inputs.RamLoad;
                if (mode % 2 == 0 || n == 1)
                {
                    var c = Lerp(c1, c2, value);
                    for (int i = 0; i < n; i++) leds[i] = Scale(c, brightness);
                    return;
                }
                double lit = value * n;
                for (int i = 0; i < n; i++)
                {
                    double v = i < Math.Floor(lit) ? 1 : i < lit ? Frac(lit) : 0;
                    leds[i] = ShadeAt(i, Lerp(c1, c2, Pos(i)), v);
                }
                return;
            }

            case FirmwareEffect.Fire:
            {
                double height = 0.3 + size * 0.07; // Burning share of the zone / Anteil der Zone, der brennt
                for (int i = 0; i < n; i++)
                {
                    double p = Pos(i);
                    double noise = ValueNoise(seed, i * 0.45, t * f * 6);
                    double heat = Math.Clamp((1 - p / height) * 0.85 + (noise - 0.45) * 0.9, 0, 1);
                    if (n == 1) heat = 0.45 + 0.55 * ValueNoise(seed, 0, t * f * 8);
                    leds[i] = Scale(FirePalette(heat), brightness);
                }
                return;
            }

            case FirmwareEffect.Plasma:
            {
                double k = 0.05 + (11 - size) * 0.04;
                for (int i = 0; i < n; i++)
                {
                    double v = Math.Sin(i * k + t * f * 2) + Math.Sin(i * k * 1.7 - t * f * 1.3 + 1.7) + Math.Sin((i * k * 0.6 + t * f) * 0.9);
                    leds[i] = Scale(Rgb.FromHue(v / 6 + t * f * 0.05), brightness);
                }
                return;
            }

            case FirmwareEffect.Heartbeat:
            {
                double ph = Frac(t * f);
                double v = Math.Max(Gauss(ph, 0.06, 0.03), 0.7 * Gauss(ph, 0.22, 0.035));
                for (int i = 0; i < n; i++) leds[i] = ShadeAt(i, ColorFor(0, Pos(i)), v);
                return;
            }

            default:
                leds.Fill(Scale(c1, brightness));
                return;
        }
    }

    /// <summary>Runs all effects with all color modes and zone lengths, no hardware (dev test).
    /// Rechnet alle Effekte mit allen Farbmodi und Zonenlängen durch, ohne Hardware (Entwicklertest).</summary>
    public static string SelfTest()
    {
        int frames = 0, lit = 0;
        var inputs = new EffectInputs { AudioLevel = 0.6, Bass = 0.7, Mid = 0.4, Treble = 0.3, Beat = 0.8, BeatCount = 5, CpuLoad = 0.4, RamLoad = 0.55,
            BeatAges = [0.05, 0.4, 0.8, 1.3], Bpm = 128, BeatPhase = 0.3, Drop = 0.5, LevelPeak = 0.8, Centroid = 0.4 };
        for (int b = 0; b < inputs.Bands.Length; b++) inputs.Bands[b] = (b % 5) / 4.0;
        foreach (var (_, effects) in Sections)
        foreach (var effect in effects)
        foreach (int subMode in Enumerable.Range(0, CapsOf(effect).Modes?.Length ?? 1))
        foreach (var mode in Enum.GetValues<Profiles.ColorMode>())
        foreach (bool synced in new[] { false, true })
        foreach (int n in new[] { 1, 8, 34, 120 })
        {
            var g = new Profiles.GroupSetting
            {
                Effect = effect, ColorMode = mode, Synced = synced, DefaultColor = "FF2000", Color2 = "0040FF",
                Background = "000010", BackgroundEnabled = n % 3 != 0, BackgroundRainbow = n % 2 == 0, Size = 1 + n % 10, Density = 10 - n % 10, SpeedStep = 7, Mode = subMode,
                Direction = n % 2 == 0 ? Profiles.EffectDirection.Left : Profiles.EffectDirection.Right,
            };
            var leds = new Rgb[n];
            for (double t = 0; t < 20; t += 0.37)
            {
                Render(g, "argb1", t, 1.0, leds, inputs);
                frames++;
                lit += leds.Count(c => c.R + c.G + c.B > 0);
            }
        }
        // Calibration: gain per channel, clamp to 255, channel order / Farbabgleich: Verstärkung je Kanal, Begrenzung, Reihenfolge
        void Expect(Profiles.ZoneCalibration c, Rgb input, Rgb want)
        {
            var got = c.Apply(input);
            if (got != want) throw new InvalidOperationException($"Farbabgleich R{c.Red} G{c.Green} B{c.Blue} {c.Order}: {input} → {got}, erwartet {want}.");
        }
        Expect(new() { Red = 50 }, Rgb.White, new Rgb(128, 255, 255));
        Expect(new() { Green = 150 }, new Rgb(100, 100, 200), new Rgb(100, 150, 200));
        Expect(new() { Blue = 150 }, new Rgb(0, 0, 200), new Rgb(0, 0, 255));
        Expect(new() { Order = "GRB" }, new Rgb(10, 20, 30), new Rgb(20, 10, 30));
        Expect(new() { Order = "BRG" }, new Rgb(10, 20, 30), new Rgb(30, 10, 20));
        Expect(new() { Red = 0, Order = "GBR" }, new Rgb(10, 20, 30), new Rgb(20, 30, 0));
        return $"{frames} Bilder ohne Fehler berechnet, {lit} leuchtende LEDs insgesamt; Farbabgleich geprüft.";
    }


    /// <summary>
    /// "Music": ten views of the audio analysis. Random = new color per beat (glides in 0.3 s), gradient by position, else color 1.
    /// Sensitivity = size; speed/direction for wave and chase.
    /// „Musik“: zehn Darstellungen der Audio-Analyse. Zufall = neue Farbe je Beat (gleitet in 0,3 s), Verlauf über die Position,
    /// sonst Farbe 1. Empfindlichkeit = Größe; Tempo/Richtung für Welle und Lauflicht.
    /// </summary>
    static void RenderMusic(GroupSetting s, Span<Rgb> leds, EffectInputs a, double t, double brightness,
        int zoneSeed, int seed, Rgb c1, Rgb c2, Rgb[] bgRow, int dir, double size, double density, double f)
    {
        int n = leds.Length;
        double gain = 0.5 + size * 0.1;
        double Amp(double x) => Math.Clamp(x * gain, 0, 1);
        double lastAge = a.BeatAges.Length > 0 ? a.BeatAges[0] : 99;
        Rgb BeatHue(long k) => Rgb.FromHue(Hash01(zoneSeed, 0, k));
        Rgb ColorAt(double pos, long beat) => s.ColorMode switch
        {
            ColorMode.Gradient => Lerp(c1, c2, pos),
            ColorMode.Random => beat == a.BeatCount ? Lerp(BeatHue(beat - 1), BeatHue(beat), Math.Clamp(lastAge / 0.3, 0, 1)) : BeatHue(beat),
            _ => c1,
        };
        Rgb ShadeAt(int i, Rgb c, double v) => Scale(Lerp(bgRow[i], c, Math.Clamp(v, 0, 1)), brightness);
        double Pos(int i) => n > 1 ? (double)i / (n - 1) : 0;
        int Index(int i) => dir > 0 ? i : n - 1 - i;
        double Band(double x)
        {
            var b = a.Bands;
            if (b.Length == 0) return 0;
            x = Math.Clamp(x, 0, 1) * (b.Length - 1);
            int b0 = (int)x, b1 = Math.Min(b.Length - 1, b0 + 1);
            return b[b0] + (b[b1] - b[b0]) * (x - b0);
        }
        double Peak(double x)
        {
            var b = a.BandPeaks;
            if (b.Length == 0) return 0;
            return b[(int)Math.Round(Math.Clamp(x, 0, 1) * (b.Length - 1))];
        }
        // White flash over everything on a drop / Weißer Blitz über allem bei einem Drop
        Rgb WithDrop(Rgb c) => a.Drop > 0.05 ? Lerp(c, Rgb.White, a.Drop * 0.8) : c;

        switch (Math.Clamp(s.Mode, 0, MusicModes.Length - 1))
        {
            case 1: // Spectrum: bass first, highs last, peak afterglow / Spektrum: Bass vorn, Höhen hinten, Nachleuchten
            case 2: // Mirrored spectrum: bass in the middle / Spektrum gespiegelt: Bass in der Mitte
            {
                bool mirrored = s.Mode == 2;
                double half = (n - 1) / 2.0;
                for (int i = 0; i < n; i++)
                {
                    double x = n == 1 ? 0 : mirrored ? Math.Abs(i - half) / Math.Max(half, 0.5) : Pos(Index(i));
                    double v = n == 1 ? Amp(a.AudioLevel) : Math.Max(Amp(Band(x)), Amp(Peak(x)) * 0.3);
                    var c = s.ColorMode switch
                    {
                        ColorMode.Single => c1,
                        ColorMode.Gradient => Lerp(c1, c2, x),
                        _ => Rgb.FromHue(x * 0.8),
                    };
                    leds[i] = ShadeAt(i, c, v * v);
                }
                return;
            }
            case 3: // Pulse: flash on every beat, faint glow between / Puls: Blitz auf jeden Beat, dazwischen Glimmen
            {
                double v = Math.Max(Math.Pow(a.Beat, 1.2), Amp(a.AudioLevel) * 0.25);
                var c = WithDrop(s.ColorMode == ColorMode.Gradient ? Lerp(c2, c1, a.Beat) : ColorAt(0, a.BeatCount));
                for (int i = 0; i < n; i++) leds[i] = ShadeAt(i, c, v);
                return;
            }
            case 4: // Bass: brightness follows the bass, beats add a kick / Bass: Helligkeit folgt dem Bass, Beats geben Schub
            {
                double v = Math.Max(Amp(a.Bass) * Amp(a.Bass), a.Beat * 0.9);
                var c = s.ColorMode == ColorMode.Gradient ? Lerp(c1, c2, v) : ColorAt(0, a.BeatCount);
                for (int i = 0; i < n; i++) leds[i] = ShadeAt(i, c, v);
                return;
            }
            case 5: // Beat wave: each beat sends a wave across the device / Welle im Takt: jeder Beat schickt eine Welle
            {
                double travel = 1.7 - s.EffectiveSpeed * 0.13; // Seconds for the full length / Sekunden für die ganze Länge
                double w = Math.Max(1.5, n * 0.12);
                for (int i = 0; i < n; i++)
                {
                    double best = Amp(a.AudioLevel) * 0.08; long beat = a.BeatCount;
                    for (int j = 0; j < a.BeatAges.Length; j++)
                    {
                        double age = a.BeatAges[j];
                        if (age > travel * 1.2) break;
                        double front = age / travel * (n + w);
                        double v = (1 - age / (travel * 1.2)) * Math.Max(0, 1 - Math.Abs(Index(i) - front) / w);
                        if (n == 1) v = Math.Pow(1 - Math.Min(1, age / 0.4), 2);
                        if (v > best) { best = v; beat = a.BeatCount - j; }
                    }
                    leds[i] = ShadeAt(i, ColorAt(Pos(i), beat), best);
                }
                return;
            }
            case 6: // Beat chase: segments step with the tempo / Lauflicht im Takt: Segmente springen mit dem Tempo
            {
                double spacing = Math.Max(3, n / (2 + density / 2));
                double w = Math.Max(1, spacing * 0.4);
                double steps = a.Bpm > 0 ? a.BeatCount + a.BeatPhase : t * f * 4;
                double offset = steps * spacing / 2;
                double glow = 0.35 + 0.65 * Math.Max(Amp(a.AudioLevel), a.Beat);
                for (int i = 0; i < n; i++)
                {
                    double x = ((Index(i) + offset) % spacing + spacing) % spacing;
                    double v = x < w ? 1 : x < w + 1 ? w + 1 - x : 0;
                    long segment = (long)Math.Floor((Index(i) + offset) / spacing);
                    var c = s.ColorMode switch
                    {
                        ColorMode.Gradient => segment % 2 == 0 ? c1 : c2,
                        ColorMode.Random => Rgb.FromHue(Hash01(zoneSeed, (int)(segment & 0xFFFF), a.BeatCount)),
                        _ => c1,
                    };
                    leds[i] = ShadeAt(i, c, n == 1 ? glow : v * glow);
                }
                return;
            }
            case 7: // Beat sparks: each beat lights sparks at random spots / Funken im Takt an zufälligen Stellen
            {
                int bursts = 1 + (int)(density / 4);
                for (int i = 0; i < n; i++)
                {
                    double best = 0; long beat = a.BeatCount;
                    for (int j = 0; j < a.BeatAges.Length; j++)
                    {
                        double age = a.BeatAges[j];
                        if (age > 0.9) break;
                        long k = a.BeatCount - j;
                        for (int q = 0; q < bursts; q++)
                        {
                            double center = Hash01(seed, q, k) * (n - 1);
                            double radius = age * Math.Max(1, n * (0.05 + size * 0.02)) + 0.5;
                            double d = Math.Abs(i - center);
                            if (d > radius) continue;
                            double v = Math.Pow(1 - age / 0.9, 2) * (0.6 + 0.4 * Hash01(seed, i, (long)(t * 30)));
                            if (v > best) { best = v; beat = k; }
                        }
                    }
                    leds[i] = ShadeAt(i, ColorAt(Hash01(seed, 7, beat), beat), best);
                }
                return;
            }
            case 8: // Pitch: color follows the spectral centroid (low = red, high = violet) / Tonhöhe: tief = rot, hoch = violett
            {
                var baseColor = s.ColorMode switch
                {
                    ColorMode.Single => c1,
                    ColorMode.Gradient => Lerp(c1, c2, a.Centroid),
                    _ => Rgb.FromHue(a.Centroid * 0.75),
                };
                double level = Amp(a.AudioLevel);
                for (int i = 0; i < n; i++)
                {
                    double v = level * (0.55 + 0.45 * Amp(Band(Pos(i))));
                    leds[i] = ShadeAt(i, WithDrop(baseColor), v);
                }
                return;
            }
            case 9: // Drop strobe: bass glow, strobe on a drop / Drop-Strobo: Bass-Glühen, Stroboskop beim Drop
            {
                bool strobe = a.Drop > 0.15 && Frac(t * 14) < 0.5;
                double v = strobe ? 1 : Math.Max(Amp(a.Bass) * Amp(a.Bass) * 0.7, a.Beat * 0.5);
                var c = strobe ? Rgb.White : ColorAt(0, a.BeatCount);
                for (int i = 0; i < n; i++) leds[i] = ShadeAt(i, c, v);
                return;
            }
            default: // Level: grows from the center out, with a falling peak / Pegel: wächst von der Mitte nach außen, mit fallender Spitze
            {
                double level = Amp(a.AudioLevel), peak = Amp(a.LevelPeak);
                if (n == 1) { leds[0] = ShadeAt(0, ColorAt(level, a.BeatCount), level); return; }
                double half = (n - 1) / 2.0;
                double step = 1 / Math.Max(half, 0.5);
                for (int i = 0; i < n; i++)
                {
                    double dist = Math.Abs(i - half) / Math.Max(half, 0.5);
                    double v = dist <= level - step ? 1 : dist <= level ? (level - dist) / step : 0;
                    var c = ColorAt(dist, a.BeatCount);
                    if (Math.Abs(dist - peak) < step * 0.75 && peak > 0.05) { c = Lerp(c, Rgb.White, 0.6); v = 1; }
                    leds[i] = ShadeAt(i, c, v);
                }
                return;
            }
        }
    }

    /// <summary>Background per LED (off, color or rainbow).
    /// Hintergrund je LED (aus, Farbe oder Regenbogen).</summary>
    static Rgb[] BackgroundRow(GroupSetting s, int n, double t)
    {
        var row = new Rgb[n];
        if (!s.BackgroundEnabled) return row;
        if (!s.BackgroundRainbow)
        {
            Array.Fill(row, s.BackgroundColor);
            return row;
        }
        double level = Math.Clamp(s.BackgroundLevel, 1, 10) / 10.0;
        for (int i = 0; i < n; i++) row[i] = Scale(Rgb.FromHue(t * 0.04 + (n > 1 ? (double)i / n : 0)), level);
        return row;
    }

    public static Rgb Scale(Rgb c, double v)
    {
        v = Math.Clamp(v, 0, 1);
        return new Rgb((byte)Math.Round(c.R * v), (byte)Math.Round(c.G * v), (byte)Math.Round(c.B * v));
    }

    public static Rgb Lerp(Rgb a, Rgb b, double x)
    {
        x = Math.Clamp(x, 0, 1);
        return new Rgb((byte)Math.Round(a.R + (b.R - a.R) * x), (byte)Math.Round(a.G + (b.G - a.G) * x), (byte)Math.Round(a.B + (b.B - a.B) * x));
    }

    // Ember: black → red → orange → yellow → whitish / Glut: schwarz → rot → orange → gelb → weißlich
    static Rgb FirePalette(double h)
    {
        h = Math.Clamp(h, 0, 1);
        if (h < 0.35) return new Rgb((byte)(h / 0.35 * 200), 0, 0);
        if (h < 0.7) return new Rgb((byte)(200 + (h - 0.35) / 0.35 * 55), (byte)((h - 0.35) / 0.35 * 120), 0);
        return new Rgb(255, (byte)(120 + (h - 0.7) / 0.3 * 120), (byte)((h - 0.7) / 0.3 * 60));
    }

    static double Gauss(double x, double mu, double sigma) => Math.Exp(-(x - mu) * (x - mu) / (2 * sigma * sigma));

    static double SmoothStep(double x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }

    static double Frac(double x) => x - Math.Floor(x);

    /// <summary>Smooth 2D noise 0–1 (grid values, smoothed between).
    /// Weiches 2D-Rauschen 0–1 (Werte an Gitterpunkten, dazwischen geglättet).</summary>
    static double ValueNoise(int seed, double x, double y)
    {
        long x0 = (long)Math.Floor(x), y0 = (long)Math.Floor(y);
        double fx = SmoothStep(x - x0), fy = SmoothStep(y - y0);
        double Corner(long cx, long cy) => Hash01(seed, (int)cx, cy * 7919);
        double a = Corner(x0, y0) + (Corner(x0 + 1, y0) - Corner(x0, y0)) * fx;
        double b = Corner(x0, y0 + 1) + (Corner(x0 + 1, y0 + 1) - Corner(x0, y0 + 1)) * fx;
        return a + (b - a) * fy;
    }

    static int StableHash(string s)
    {
        unchecked
        {
            int h = 17;
            foreach (char ch in s) h = h * 31 + ch;
            return h;
        }
    }

    /// <summary>Uniform pseudo-random number 0–1 from three values (SplitMix64).
    /// Gleichverteilte Pseudozufallszahl 0–1 aus drei Werten (SplitMix64).</summary>
    static double Hash01(int seed, int i, long cycle)
    {
        unchecked
        {
            ulong z = (ulong)seed * 0x9E3779B97F4A7C15UL ^ (ulong)i * 0xBF58476D1CE4E5B9UL ^ (ulong)cycle * 0x94D049BB133111EBUL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (z >> 11) * (1.0 / (1UL << 53));
        }
    }
}
