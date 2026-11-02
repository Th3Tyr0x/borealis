using System.Text.Json.Serialization;
using Borealis.Core.Lighting;

namespace Borealis.Core.Profiles;

/// <summary>
/// Ein Beleuchtungsprofil. Die Einteilung folgt der Hardware: Mainboard und ARGB-Header haben je einen gemeinsamen
/// Effekt pro Steuerkanal (Farbe je Zone), der RAM einen Effekt für alle Module (Farbe je Modul).
/// </summary>
public sealed class LightingProfile
{
    public string Name { get; set; } = "Standard";
    /// <summary>Borealis Sync wie in Armoury Crate: ein Effekt und eine Farbe für alle verbundenen Geräte.</summary>
    public SyncSetting Sync { get; set; } = new();
    public GroupSetting Mainboard { get; set; } = new();
    public GroupSetting Argb { get; set; } = new();
    public GroupSetting Ram { get; set; } = new();
    /// <summary>Effekt nach dem Herunterfahren / im Standby (im Controller gespeichert, §1.7).</summary>
    public GroupSetting StandbyMainboard { get; set; } = new() { Effect = FirmwareEffect.Rainbow };
    public GroupSetting StandbyArgb { get; set; } = new() { Effect = FirmwareEffect.Rainbow };
    /// <summary>Helligkeit aller Effekte in Prozent (1–100).</summary>
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

    /// <summary>Neues Profil: alle Geräte in Borealis Sync, rot statisch; Standby Regenbogen (wie bisher im Controller).</summary>
    public static LightingProfile CreateDefault(string name = "Standard") => new()
    {
        Name = name,
        Sync = new SyncSetting { Mainboard = true, Argb = true, Ram = true },
    };

    /// <summary>
    /// Profil, wie es an die Hardware geht: Gruppen, die in Borealis Sync sind, bekommen die Effekt-Einstellungen
    /// der Sync-Seite für alle ihre Zonen.
    /// </summary>
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

/// <summary>Farbmodus eines Effekts mit Farbe.</summary>
public enum ColorMode { Single, Random, Gradient }

/// <summary>Laufrichtung bewegter Effekte.</summary>
public enum EffectDirection { Right, Left }

/// <summary>Borealis-Sync-Einstellung. Fehlt sie in einer älteren Profildatei, ist kein Gerät verbunden.</summary>
public sealed class SyncSetting
{
    public bool Mainboard { get; set; }
    public bool Argb { get; set; }
    public bool Ram { get; set; }
    public FirmwareEffect Effect { get; set; } = FirmwareEffect.Static;
    /// <summary>Effekt vor dem Ausschalten, damit „Beleuchtung an“ ihn wiederherstellt.</summary>
    public FirmwareEffect LastEffect { get; set; } = FirmwareEffect.Static;
    /// <summary>Alte Stufe (Langsam/Normal/Schnell); nur für ältere Profile, siehe <see cref="SpeedStep"/>.</summary>
    public SpeedLevel Speed { get; set; } = SpeedLevel.Normal;
    /// <summary>Geschwindigkeit 1 (langsam) bis 10 (schnell); 0 = aus <see cref="Speed"/> ableiten.</summary>
    public int SpeedStep { get; set; }
    public EffectDirection Direction { get; set; }
    public ColorMode ColorMode { get; set; }
    public string Color { get; set; } = "FF0000";
    /// <summary>Zweite Farbe für den Farbverlauf.</summary>
    public string Color2 { get; set; } = "0050FF";
    /// <summary>Unterart des Effekts (z. B. Musik: Pegel/Spektrum/Puls/Bass), siehe EffectCaps.Modes.</summary>
    public int Mode { get; set; }
    /// <summary>Größe/Länge (Schweif, Welle, Leuchtpunkt …) 1–10.</summary>
    public int Size { get; set; } = 5;
    /// <summary>Dichte (Sterne, Funken, Meteore …) 1–10.</summary>
    public int Density { get; set; } = 5;
    /// <summary>Hintergrundfarbe hinter Komet, Sternen usw. (RRGGBB, Standard Schwarz).</summary>
    public string Background { get; set; } = "000000";
    /// <summary>Hintergrundfarbe verwenden; aus = schwarz (die gewählte Farbe bleibt gespeichert).</summary>
    public bool BackgroundEnabled { get; set; }
    /// <summary>Hintergrund als langsam wandernder Regenbogen statt einer Farbe.</summary>
    public bool BackgroundRainbow { get; set; }
    /// <summary>Stärke des Regenbogen-Hintergrunds 1–10 (×10 %).</summary>
    public int BackgroundLevel { get; set; } = 3;
    /// <summary>Farbmodus Zufall: alle wie viele Sekunden eine neue Farbe kommt (1–60).</summary>
    public int ChangeInterval { get; set; } = 4;
    /// <summary>Farbmodus Zufall: Anteil des Intervalls für den weichen Übergang, 0 = hart, 10 = ständig gleitend.</summary>
    public int Transition { get; set; } = 7;

    public SyncSetting Clone() => (SyncSetting)MemberwiseClone();

    /// <summary>Alle Effekt-Einstellungen auf Standard; Effekt und verbundene Geräte bleiben.</summary>
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
    /// <summary>Effekt vor dem Ausschalten, damit „Beleuchtung an“ ihn wiederherstellt.</summary>
    public FirmwareEffect LastEffect { get; set; } = FirmwareEffect.Static;
    /// <summary>Alte Stufe (Langsam/Normal/Schnell); nur für ältere Profile, siehe <see cref="SpeedStep"/>.</summary>
    public SpeedLevel Speed { get; set; } = SpeedLevel.Normal;
    /// <summary>Geschwindigkeit 1 (langsam) bis 10 (schnell); 0 = aus <see cref="Speed"/> ableiten.</summary>
    public int SpeedStep { get; set; }
    public EffectDirection Direction { get; set; }
    public ColorMode ColorMode { get; set; }
    /// <summary>Farbe je Zonen-ID (z. B. „argb1“) als RRGGBB. Fehlende Zonen bekommen <see cref="DefaultColor"/>.</summary>
    public Dictionary<string, string> Colors { get; set; } = new();
    public string DefaultColor { get; set; } = "FFFFFF";
    /// <summary>Zweite Farbe für den Farbverlauf (Verlauf je Zone von Zonenfarbe nach Color2).</summary>
    public string Color2 { get; set; } = "0050FF";
    /// <summary>Unterart des Effekts (z. B. Musik: Pegel/Spektrum/Puls/Bass), siehe EffectCaps.Modes.</summary>
    public int Mode { get; set; }
    /// <summary>Größe/Länge (Schweif, Welle, Leuchtpunkt …) 1–10.</summary>
    public int Size { get; set; } = 5;
    /// <summary>Dichte (Sterne, Funken, Meteore …) 1–10.</summary>
    public int Density { get; set; } = 5;
    /// <summary>Hintergrundfarbe hinter Komet, Sternen usw. (RRGGBB, Standard Schwarz).</summary>
    public string Background { get; set; } = "000000";
    /// <summary>Hintergrundfarbe verwenden; aus = schwarz (die gewählte Farbe bleibt gespeichert).</summary>
    public bool BackgroundEnabled { get; set; }
    /// <summary>Hintergrund als langsam wandernder Regenbogen statt einer Farbe.</summary>
    public bool BackgroundRainbow { get; set; }
    /// <summary>Stärke des Regenbogen-Hintergrunds 1–10 (×10 %).</summary>
    public int BackgroundLevel { get; set; } = 3;
    /// <summary>Farbmodus Zufall: alle wie viele Sekunden eine neue Farbe kommt (1–60).</summary>
    public int ChangeInterval { get; set; } = 4;
    /// <summary>Farbmodus Zufall: Anteil des Intervalls für den weichen Übergang, 0 = hart, 10 = ständig gleitend.</summary>
    public int Transition { get; set; } = 7;

    public Rgb ColorOf(string zoneId) =>
        Colors.TryGetValue(zoneId, out var s) && Rgb.TryParse(s, out var c) ? c : Rgb.TryParse(DefaultColor, out var d) ? d : Rgb.White;

    public Rgb SecondColor => Rgb.TryParse(Color2, out var c) ? c : Rgb.White;

    public Rgb BackgroundColor => BackgroundEnabled && Rgb.TryParse(Background, out var c) ? c : Rgb.Black;

    /// <summary>Kommt aus Borealis Sync: Zufallsfarben wechseln auf allen Geräten gleich (nicht gespeichert).</summary>
    [JsonIgnore] public bool Synced { get; set; }

    [JsonIgnore] public int EffectiveSpeed => SpeedOf(SpeedStep, Speed);

    /// <summary>Stufe 1–10 in die drei Firmware-Stufen übersetzen.</summary>
    [JsonIgnore] public SpeedLevel FirmwareSpeed => EffectiveSpeed switch { <= 3 => SpeedLevel.Slow, >= 8 => SpeedLevel.Fast, _ => SpeedLevel.Normal };

    public static int SpeedOf(int step, SpeedLevel level) => step is >= 1 and <= 10 ? step
        : level switch { SpeedLevel.Slow => 3, SpeedLevel.Fast => 8, _ => 5 };

    /// <summary>Alle Effekt-Einstellungen auf Standard (auch Zonenfarben); der Effekt bleibt.</summary>
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
