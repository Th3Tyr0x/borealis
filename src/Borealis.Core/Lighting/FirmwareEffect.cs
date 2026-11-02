namespace Borealis.Core.Lighting;

/// <summary>
/// Effekte, die die Firmware des ASUS-RGB-Controllers selbst abspielt (docs/hardware.md §1.5).
/// Sie laufen ohne Software weiter und werden mit <c>EC 3F 55</c> im Controller gespeichert.
/// </summary>
public enum FirmwareEffect : byte
{
    /// <summary>Effekt 0: Ausgabe aus (bzw. Grundzustand).</summary>
    Off = 0x00,
    Static = 0x01,
    Breathing = 0x02,
    Strobing = 0x03,
    ColorCycle = 0x04,
    Rainbow = 0x05,
    Comet = 0x08,
    FlashAndDash = 0x0A,
    Wave = 0x0B,
    GlowingYoyo = 0x0C,
    StarryNight = 0x0D,

    // Ab 0x80: nur in Borealis berechnete Effekte (keine Firmware-IDs; gehen nie an den Controller,
    // siehe SoftwareEffects.FirmwareFallback)
    BreathRainbow = 0x80,
    RainbowComet = 0x81,
    MovingFlash = 0x82,
    Fireworks = 0x83,
    Music = 0x84,
    Smart = 0x85,
    Fire = 0x86,
    Meteor = 0x87,
    Scanner = 0x88,
    Plasma = 0x89,
    Heartbeat = 0x8A,
    Twinkle = 0x8B,

    /// <summary>0xFF: Firmware gibt den Kanal an die Software ab (Direct, §1.6).</summary>
    Direct = 0xFF,
}

public static class FirmwareEffects
{
    /// <summary>
    /// Ob der HAL zu diesem Effekt Farben per <c>EC 36</c> sendet (Spalte Farbe in §1.5).
    /// Bei Effekten ohne Farbe würfelt die Firmware die Farben selbst.
    /// </summary>
    public static bool UsesColor(FirmwareEffect e) => e is FirmwareEffect.Static or FirmwareEffect.Breathing or FirmwareEffect.Strobing;

    /// <summary>Spalte Tempo in §1.5 (Geschwindigkeit wirkt) [abgeleitet].</summary>
    public static bool HasSpeed(FirmwareEffect e) => e is not (FirmwareEffect.Off or FirmwareEffect.Static or FirmwareEffect.Direct);

    /// <summary>Spalte Richtung in §1.5 (Richtung wirkt) [abgeleitet].</summary>
    public static bool HasDirection(FirmwareEffect e) => e is FirmwareEffect.Rainbow or FirmwareEffect.Comet
        or FirmwareEffect.FlashAndDash or FirmwareEffect.Wave or FirmwareEffect.GlowingYoyo;

    static readonly Dictionary<string, FirmwareEffect> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["off"] = FirmwareEffect.Off, ["aus"] = FirmwareEffect.Off,
        ["static"] = FirmwareEffect.Static, ["statisch"] = FirmwareEffect.Static,
        ["breathing"] = FirmwareEffect.Breathing, ["atmen"] = FirmwareEffect.Breathing,
        ["strobing"] = FirmwareEffect.Strobing, ["blinken"] = FirmwareEffect.Strobing,
        ["colorcycle"] = FirmwareEffect.ColorCycle, ["farbzyklus"] = FirmwareEffect.ColorCycle,
        ["rainbow"] = FirmwareEffect.Rainbow, ["regenbogen"] = FirmwareEffect.Rainbow,
        ["comet"] = FirmwareEffect.Comet, ["komet"] = FirmwareEffect.Comet,
        ["flashanddash"] = FirmwareEffect.FlashAndDash,
        ["wave"] = FirmwareEffect.Wave, ["welle"] = FirmwareEffect.Wave,
        ["glowingyoyo"] = FirmwareEffect.GlowingYoyo, ["yoyo"] = FirmwareEffect.GlowingYoyo,
        ["starrynight"] = FirmwareEffect.StarryNight, ["sternennacht"] = FirmwareEffect.StarryNight,
    };

    public static bool TryParse(string text, out FirmwareEffect effect) =>
        Names.TryGetValue(text.Replace("-", "").Replace("_", ""), out effect);

    public static IEnumerable<string> KnownNames => Enum.GetValues<FirmwareEffect>()
        .Where(e => e != FirmwareEffect.Direct).Select(e => e.ToString().ToLowerInvariant());

    public static string Describe(byte id) => Enum.IsDefined(typeof(FirmwareEffect), id)
        ? ((FirmwareEffect)id).ToString()
        : $"0x{id:X2}";

    /// <summary>Effekte, die die Oberfläche anbietet, in Armoury-Crate-Reihenfolge.</summary>
    public static readonly FirmwareEffect[] Selectable =
    [
        FirmwareEffect.Static, FirmwareEffect.Breathing, FirmwareEffect.Strobing, FirmwareEffect.ColorCycle,
        FirmwareEffect.Rainbow, FirmwareEffect.Comet, FirmwareEffect.FlashAndDash, FirmwareEffect.Wave,
        FirmwareEffect.GlowingYoyo, FirmwareEffect.StarryNight, FirmwareEffect.Off,
    ];

    /// <summary>Name in der Sprache der Oberfläche.</summary>
    public static string DisplayName(FirmwareEffect e) => Localization.Loc.Term(GermanName(e));

    public static string GermanName(FirmwareEffect e) => e switch
    {
        FirmwareEffect.Off => "Aus",
        FirmwareEffect.Static => "Statisch",
        FirmwareEffect.Breathing => "Atmen",
        FirmwareEffect.Strobing => "Blinken",
        FirmwareEffect.ColorCycle => "Farbzyklus",
        FirmwareEffect.Rainbow => "Regenbogen",
        FirmwareEffect.Comet => "Komet",
        FirmwareEffect.FlashAndDash => "Flash and Dash",
        FirmwareEffect.Wave => "Welle",
        FirmwareEffect.GlowingYoyo => "Glowing Yoyo",
        FirmwareEffect.StarryNight => "Sternennacht",
        FirmwareEffect.BreathRainbow => "Atmender Regenbogen",
        FirmwareEffect.RainbowComet => "Regenbogen-Komet",
        FirmwareEffect.MovingFlash => "Moving Flash",
        FirmwareEffect.Fireworks => "Feuerwerk",
        FirmwareEffect.Music => "Musik",
        FirmwareEffect.Smart => "Smart",
        FirmwareEffect.Fire => "Feuer",
        FirmwareEffect.Meteor => "Meteor",
        FirmwareEffect.Scanner => "Scanner",
        FirmwareEffect.Plasma => "Farbfluss",
        FirmwareEffect.Heartbeat => "Herzschlag",
        FirmwareEffect.Twinkle => "Funkeln",
        _ => e.ToString(),
    };

    /// <summary>
    /// Speed-Byte zu einer Stufe. Armoury Crate (Mainboard-Ansicht, Abschalt-Effekt) sendet „Langsam/Normal/Schnell“
    /// als 2/1/0 (Atmen, Blinken, Farbzyklus) bzw. 1/0/−1 (Regenbogen, Sternennacht); größer = langsamer.
    /// Für die Bewegungseffekte ohne eigene Liste gilt hier dieselbe Tabelle wie für Regenbogen [abgeleitet].
    /// </summary>
    public static byte SpeedValue(FirmwareEffect e, SpeedLevel level)
    {
        bool motion = e is FirmwareEffect.Rainbow or FirmwareEffect.StarryNight or FirmwareEffect.Comet
            or FirmwareEffect.FlashAndDash or FirmwareEffect.Wave or FirmwareEffect.GlowingYoyo;
        int v = level switch { SpeedLevel.Slow => 2, SpeedLevel.Fast => 0, _ => 1 } - (motion ? 1 : 0);
        return unchecked((byte)(sbyte)v);
    }
}

public enum SpeedLevel { Slow, Normal, Fast }

/// <summary>Effekt-IDs der ENE-RAM-HAL (§3.4). Andere Nummern als beim USB-Controller.</summary>
public enum RamEffect : uint
{
    Direct = 0,
    Static = 1,
    Breathing = 2,
    Flash = 3,
    ColorCycle = 4,
    Rainbow = 5,
    BreathRainbow = 6,
    Comet = 7,
    RainbowComet = 8,
    MovingFlash = 9,
    MovingFlashRainbow = 10,
    Wave = 11,
    GlowingYoyo = 12,
    StarryNight = 13,
    Fireworks = 22,
    FireworksRainbow = 23,
    Default = 255,
}

public static class RamEffects
{
    /// <summary>Gleichnamiger RAM-Effekt zu einem Firmware-Effekt des USB-Controllers.</summary>
    public static RamEffect FromFirmware(FirmwareEffect e) => e switch
    {
        FirmwareEffect.Static => RamEffect.Static,
        FirmwareEffect.Breathing => RamEffect.Breathing,
        FirmwareEffect.Strobing => RamEffect.Flash,
        FirmwareEffect.ColorCycle => RamEffect.ColorCycle,
        FirmwareEffect.Rainbow => RamEffect.Rainbow,
        FirmwareEffect.Comet => RamEffect.Comet,
        FirmwareEffect.FlashAndDash => RamEffect.MovingFlash,
        FirmwareEffect.Wave => RamEffect.Wave,
        FirmwareEffect.GlowingYoyo => RamEffect.GlowingYoyo,
        FirmwareEffect.StarryNight => RamEffect.StarryNight,
        _ => RamEffect.Direct,
    };
}
