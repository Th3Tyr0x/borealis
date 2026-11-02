using System.Text.Json.Serialization;
using Borealis.Core.Lighting;

namespace Borealis.Core.Profiles;

/// <summary>
/// A lighting profile, split like the hardware: motherboard and ARGB headers have one effect per channel (color per zone),
/// RAM one effect for all modules (color per module).
/// Ein Beleuchtungsprofil, eingeteilt wie die Hardware: Mainboard und ARGB-Header je ein Effekt pro Kanal (Farbe je Zone),
/// der RAM ein Effekt für alle Module (Farbe je Modul).
/// </summary>
public sealed class LightingProfile
{
    public string Name { get; set; } = "Standard";
    /// <summary>Borealis Sync: one effect and color for all linked devices.
    /// Borealis Sync: ein Effekt und eine Farbe für alle verbundenen Geräte.</summary>
    public SyncSetting Sync { get; set; } = new();
    public GroupSetting Mainboard { get; set; } = new();
    public GroupSetting Argb { get; set; } = new();
    public GroupSetting Ram { get; set; } = new();
    /// <summary>Effect after shutdown / in standby (stored in the controller, §1.7).
    /// Effekt nach dem Herunterfahren / im Standby (im Controller gespeichert, §1.7).</summary>
    public GroupSetting StandbyMainboard { get; set; } = new() { Effect = FirmwareEffect.Rainbow };
    public GroupSetting StandbyArgb { get; set; } = new() { Effect = FirmwareEffect.Rainbow };
    /// <summary>Brightness of all effects in percent (1–100).
    /// Helligkeit aller Effekte in Prozent (1–100).</summary>
    public int Brightness { get; set; } = 100;

    public LightingProfile Clone() => new()
    {
        Name = Name,
        Sync = Sync.Clone(),
        Mainboard = Mainboard.Clone(),
        Argb = Argb.Clone(),
        Ram = Ram.Clone(),
        StandbyMainboard = StandbyMainboard.Clone(),
        StandbyArgb = StandbyArgb.Clone(),
        Brightness = Brightness,
    };

    /// <summary>New profile: all devices in Sync, static red; standby rainbow.
    /// Neues Profil: alle Geräte in Sync, rot statisch; Standby Regenbogen.</summary>
    public static LightingProfile CreateDefault(string name = "Standard") => new()
    {
        Name = name,
        Sync = new SyncSetting { Mainboard = true, Argb = true, Ram = true },
    };

    /// <summary>Profile as sent to the hardware: groups in Sync get the Sync page's effect settings for all their zones.
    /// Profil, wie es an die Hardware geht: Gruppen in Sync bekommen die Effekt-Einstellungen der Sync-Seite für alle Zonen.</summary>
    public LightingProfile Resolve()
    {
        var p = Clone();
        if (Sync.Mainboard) p.Mainboard = Sync.ToGroup();
        if (Sync.Argb) p.Argb = Sync.ToGroup();
        if (Sync.Ram) p.Ram = Sync.ToGroup();
        return p;
    }

    public bool IsSynced(GroupKind kind) => kind switch
    {
        GroupKind.Mainboard => Sync.Mainboard,
        GroupKind.Argb => Sync.Argb,
        GroupKind.Ram => Sync.Ram,
        _ => false,
    };

    public GroupSetting Group(GroupKind kind) => kind switch
    {
        GroupKind.Mainboard => Mainboard,
        GroupKind.Argb => Argb,
        GroupKind.Ram => Ram,
        GroupKind.StandbyMainboard => StandbyMainboard,
        _ => StandbyArgb,
    };
}

public enum GroupKind { Mainboard, Argb, Ram, StandbyMainboard, StandbyArgb }

/// <summary>Color mode of an effect with color.
/// Farbmodus eines Effekts mit Farbe.</summary>
public enum ColorMode { Single, Random, Gradient }

/// <summary>Direction of moving effects.
/// Laufrichtung bewegter Effekte.</summary>
public enum EffectDirection { Right, Left }

/// <summary>Borealis Sync setting; missing in older profile files = no device linked.
/// Borealis-Sync-Einstellung; fehlt sie in älteren Profilen, ist kein Gerät verbunden.</summary>
public sealed class SyncSetting
{
    public bool Mainboard { get; set; }
    public bool Argb { get; set; }
    public bool Ram { get; set; }
    public FirmwareEffect Effect { get; set; } = FirmwareEffect.Static;
    /// <summary>Effect before switching off, so "Lighting on" restores it.
    /// Effekt vor dem Ausschalten, damit „Beleuchtung an“ ihn wiederherstellt.</summary>
    public FirmwareEffect LastEffect { get; set; } = FirmwareEffect.Static;
    /// <summary>Old level (Slow/Normal/Fast); older profiles only, see <see cref="SpeedStep"/>.
    /// Alte Stufe (Langsam/Normal/Schnell); nur für ältere Profile, siehe <see cref="SpeedStep"/>.</summary>
    public SpeedLevel Speed { get; set; } = SpeedLevel.Normal;
    /// <summary>Speed 1 (slow) to 10 (fast); 0 = derive from <see cref="Speed"/>.
    /// Geschwindigkeit 1 (langsam) bis 10 (schnell); 0 = aus <see cref="Speed"/> ableiten.</summary>
    public int SpeedStep { get; set; }
    public EffectDirection Direction { get; set; }
    public ColorMode ColorMode { get; set; }
    public string Color { get; set; } = "FF0000";
    /// <summary>Second color for the gradient.
    /// Zweite Farbe für den Farbverlauf.</summary>
    public string Color2 { get; set; } = "0050FF";
    /// <summary>Effect variant (e.g. music: level/spectrum/pulse/bass), see EffectCaps.Modes.
    /// Unterart des Effekts (z. B. Musik: Pegel/Spektrum/Puls/Bass), siehe EffectCaps.Modes.</summary>
    public int Mode { get; set; }
    /// <summary>Size/length (tail, wave, dot …) 1–10.
    /// Größe/Länge (Schweif, Welle, Leuchtpunkt …) 1–10.</summary>
    public int Size { get; set; } = 5;
    /// <summary>Density (stars, sparks, meteors …) 1–10.
    /// Dichte (Sterne, Funken, Meteore …) 1–10.</summary>
    public int Density { get; set; } = 5;
    /// <summary>Background color behind comet, stars etc. (RRGGBB, default black).
    /// Hintergrundfarbe hinter Komet, Sternen usw. (RRGGBB, Standard Schwarz).</summary>
    public string Background { get; set; } = "000000";
    /// <summary>Use the background color; off = black (the chosen color stays saved).
    /// Hintergrundfarbe verwenden; aus = schwarz (die gewählte Farbe bleibt gespeichert).</summary>
    public bool BackgroundEnabled { get; set; }
    /// <summary>Background as a slowly moving rainbow instead of a color.
    /// Hintergrund als langsam wandernder Regenbogen statt einer Farbe.</summary>
    public bool BackgroundRainbow { get; set; }
    /// <summary>Rainbow background strength 1–10 (×10 %).
    /// Stärke des Regenbogen-Hintergrunds 1–10 (×10 %).</summary>
    public int BackgroundLevel { get; set; } = 3;
    /// <summary>Random color mode: seconds between new colors (1–60).
    /// Farbmodus Zufall: Sekunden bis zur nächsten Farbe (1–60).</summary>
    public int ChangeInterval { get; set; } = 4;
    /// <summary>Random color mode: share of the interval for the soft fade, 0 = hard, 10 = always gliding.
    /// Farbmodus Zufall: Anteil des Intervalls für den weichen Übergang, 0 = hart, 10 = ständig gleitend.</summary>
    public int Transition { get; set; } = 7;

    public SyncSetting Clone() => (SyncSetting)MemberwiseClone();

    /// <summary>Reset all effect settings; effect and linked devices stay.
    /// Alle Effekt-Einstellungen auf Standard; Effekt und verbundene Geräte bleiben.</summary>
    public void ResetOptions()
    {
        var d = new SyncSetting();
        (Speed, SpeedStep, Direction, ColorMode, Color, Color2) = (d.Speed, d.SpeedStep, d.Direction, d.ColorMode, d.Color, d.Color2);
        (Size, Density, Background, BackgroundEnabled, BackgroundRainbow, BackgroundLevel) = (d.Size, d.Density, d.Background, d.BackgroundEnabled, d.BackgroundRainbow, d.BackgroundLevel);
        (ChangeInterval, Transition, Mode) = (d.ChangeInterval, d.Transition, d.Mode);
    }

    public GroupSetting ToGroup() => new()
    {
        Effect = Effect, LastEffect = LastEffect, Speed = Speed, SpeedStep = SpeedStep, Direction = Direction,
        ColorMode = ColorMode, DefaultColor = Color, Color2 = Color2, Synced = true,
        Size = Size, Density = Density, Background = Background, BackgroundEnabled = BackgroundEnabled, BackgroundRainbow = BackgroundRainbow, BackgroundLevel = BackgroundLevel, ChangeInterval = ChangeInterval, Transition = Transition, Mode = Mode,
    };

    [JsonIgnore] public int EffectiveSpeed => GroupSetting.SpeedOf(SpeedStep, Speed);
}

public sealed class GroupSetting
{
    public FirmwareEffect Effect { get; set; } = FirmwareEffect.Static;
    /// <summary>Effect before switching off, so "Lighting on" restores it.
    /// Effekt vor dem Ausschalten, damit „Beleuchtung an“ ihn wiederherstellt.</summary>
    public FirmwareEffect LastEffect { get; set; } = FirmwareEffect.Static;
    /// <summary>Old level (Slow/Normal/Fast); older profiles only, see <see cref="SpeedStep"/>.
    /// Alte Stufe (Langsam/Normal/Schnell); nur für ältere Profile, siehe <see cref="SpeedStep"/>.</summary>
    public SpeedLevel Speed { get; set; } = SpeedLevel.Normal;
    /// <summary>Speed 1 (slow) to 10 (fast); 0 = derive from <see cref="Speed"/>.
    /// Geschwindigkeit 1 (langsam) bis 10 (schnell); 0 = aus <see cref="Speed"/> ableiten.</summary>
    public int SpeedStep { get; set; }
    public EffectDirection Direction { get; set; }
    public ColorMode ColorMode { get; set; }
    /// <summary>Color per zone ID (e.g. "argb1") as RRGGBB; missing zones get <see cref="DefaultColor"/>.
    /// Farbe je Zonen-ID (z. B. „argb1“) als RRGGBB; fehlende Zonen bekommen <see cref="DefaultColor"/>.</summary>
    public Dictionary<string, string> Colors { get; set; } = new();
    public string DefaultColor { get; set; } = "FFFFFF";
    /// <summary>Second gradient color (per zone from zone color to Color2).
    /// Zweite Verlaufsfarbe (je Zone von der Zonenfarbe nach Color2).</summary>
    public string Color2 { get; set; } = "0050FF";
    /// <summary>Effect variant (e.g. music: level/spectrum/pulse/bass), see EffectCaps.Modes.
    /// Unterart des Effekts (z. B. Musik: Pegel/Spektrum/Puls/Bass), siehe EffectCaps.Modes.</summary>
    public int Mode { get; set; }
    /// <summary>Size/length (tail, wave, dot …) 1–10.
    /// Größe/Länge (Schweif, Welle, Leuchtpunkt …) 1–10.</summary>
    public int Size { get; set; } = 5;
    /// <summary>Density (stars, sparks, meteors …) 1–10.
    /// Dichte (Sterne, Funken, Meteore …) 1–10.</summary>
    public int Density { get; set; } = 5;
    /// <summary>Background color behind comet, stars etc. (RRGGBB, default black).
    /// Hintergrundfarbe hinter Komet, Sternen usw. (RRGGBB, Standard Schwarz).</summary>
    public string Background { get; set; } = "000000";
    /// <summary>Use the background color; off = black (the chosen color stays saved).
    /// Hintergrundfarbe verwenden; aus = schwarz (die gewählte Farbe bleibt gespeichert).</summary>
    public bool BackgroundEnabled { get; set; }
    /// <summary>Background as a slowly moving rainbow instead of a color.
    /// Hintergrund als langsam wandernder Regenbogen statt einer Farbe.</summary>
    public bool BackgroundRainbow { get; set; }
    /// <summary>Rainbow background strength 1–10 (×10 %).
    /// Stärke des Regenbogen-Hintergrunds 1–10 (×10 %).</summary>
    public int BackgroundLevel { get; set; } = 3;
    /// <summary>Random color mode: seconds between new colors (1–60).
    /// Farbmodus Zufall: Sekunden bis zur nächsten Farbe (1–60).</summary>
    public int ChangeInterval { get; set; } = 4;
    /// <summary>Random color mode: share of the interval for the soft fade, 0 = hard, 10 = always gliding.
    /// Farbmodus Zufall: Anteil des Intervalls für den weichen Übergang, 0 = hart, 10 = ständig gleitend.</summary>
    public int Transition { get; set; } = 7;

    public Rgb ColorOf(string zoneId) =>
        Colors.TryGetValue(zoneId, out var s) && Rgb.TryParse(s, out var c) ? c : Rgb.TryParse(DefaultColor, out var d) ? d : Rgb.White;

    public Rgb SecondColor => Rgb.TryParse(Color2, out var c) ? c : Rgb.White;

    public Rgb BackgroundColor => BackgroundEnabled && Rgb.TryParse(Background, out var c) ? c : Rgb.Black;

    /// <summary>From Borealis Sync: random colors change the same on all devices (not saved).
    /// Kommt aus Borealis Sync: Zufallsfarben wechseln auf allen Geräten gleich (nicht gespeichert).</summary>
    [JsonIgnore] public bool Synced { get; set; }

    [JsonIgnore] public int EffectiveSpeed => SpeedOf(SpeedStep, Speed);

    /// <summary>Map level 1–10 to the three firmware levels.
    /// Stufe 1–10 in die drei Firmware-Stufen übersetzen.</summary>
    [JsonIgnore] public SpeedLevel FirmwareSpeed => EffectiveSpeed switch { <= 3 => SpeedLevel.Slow, >= 8 => SpeedLevel.Fast, _ => SpeedLevel.Normal };

    public static int SpeedOf(int step, SpeedLevel level) => step is >= 1 and <= 10 ? step
        : level switch { SpeedLevel.Slow => 3, SpeedLevel.Fast => 8, _ => 5 };

    /// <summary>Reset all effect settings (incl. zone colors); the effect stays.
    /// Alle Effekt-Einstellungen auf Standard (auch Zonenfarben); der Effekt bleibt.</summary>
    public void ResetOptions()
    {
        var d = new GroupSetting();
        (Speed, SpeedStep, Direction, ColorMode, DefaultColor, Color2) = (d.Speed, d.SpeedStep, d.Direction, d.ColorMode, d.DefaultColor, d.Color2);
        (Size, Density, Background, BackgroundEnabled, BackgroundRainbow, BackgroundLevel) = (d.Size, d.Density, d.Background, d.BackgroundEnabled, d.BackgroundRainbow, d.BackgroundLevel);
        (ChangeInterval, Transition, Mode) = (d.ChangeInterval, d.Transition, d.Mode);
        Colors.Clear();
    }

    public GroupSetting Clone() => new()
    {
        Effect = Effect,
        LastEffect = LastEffect,
        Speed = Speed,
        SpeedStep = SpeedStep,
        Direction = Direction,
        ColorMode = ColorMode,
        Colors = new Dictionary<string, string>(Colors),
        DefaultColor = DefaultColor,
        Color2 = Color2,
        Synced = Synced,
        Size = Size,
        Density = Density,
        Background = Background,
        BackgroundEnabled = BackgroundEnabled,
        BackgroundRainbow = BackgroundRainbow,
        BackgroundLevel = BackgroundLevel,
        ChangeInterval = ChangeInterval,
        Transition = Transition,
        Mode = Mode,
    };
}
