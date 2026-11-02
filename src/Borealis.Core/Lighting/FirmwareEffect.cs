namespace Borealis.Core.Lighting;

/// <summary>
/// Effects played by the RGB controller firmware itself (docs/hardware.md §1.5); they run without software and are stored with <c>EC 3F 55</c>.
/// Effekte, die die Firmware des RGB-Controllers selbst abspielt (docs/hardware.md §1.5); laufen ohne Software, gespeichert mit <c>EC 3F 55</c>.
/// </summary>
public enum FirmwareEffect : byte
{
    /// <summary>Effect 0: output off (or idle state).
    /// Effekt 0: Ausgabe aus (bzw. Grundzustand).</summary>
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

    // From 0x80: effects computed in Borealis only (never sent to the controller, see SoftwareEffects.FirmwareFallback)
    // Ab 0x80: nur in Borealis berechnete Effekte (gehen nie an den Controller, siehe SoftwareEffects.FirmwareFallback)
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

    /// <summary>0xFF: firmware hands the channel to software (Direct, §1.6).
    /// 0xFF: Firmware gibt den Kanal an die Software ab (Direct, §1.6).</summary>
    Direct = 0xFF,
}

public static class FirmwareEffects
{
    /// <summary>Whether the HAL sends colors via <c>EC 36</c> for this effect (§1.5); without, the firmware picks random colors.
    /// Ob der HAL für diesen Effekt Farben per <c>EC 36</c> sendet (§1.5); sonst würfelt die Firmware die Farben.</summary>
    public static bool UsesColor(FirmwareEffect e) => e is FirmwareEffect.Static or FirmwareEffect.Breathing or FirmwareEffect.Strobing;

    /// <summary>Speed has an effect (§1.5) [derived].
    /// Geschwindigkeit wirkt (§1.5) [abgeleitet].</summary>
    public static bool HasSpeed(FirmwareEffect e) => e is not (FirmwareEffect.Off or FirmwareEffect.Static or FirmwareEffect.Direct);

    /// <summary>Direction has an effect (§1.5) [derived].
    /// Richtung wirkt (§1.5) [abgeleitet].</summary>
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

    /// <summary>Effects offered in the UI, in their usual order.
    /// Effekte, die die Oberfläche anbietet, in der üblichen Reihenfolge.</summary>
    public static readonly FirmwareEffect[] Selectable =
    [
        FirmwareEffect.Static, FirmwareEffect.Breathing, FirmwareEffect.Strobing, FirmwareEffect.ColorCycle,
        FirmwareEffect.Rainbow, FirmwareEffect.Comet, FirmwareEffect.FlashAndDash, FirmwareEffect.Wave,
        FirmwareEffect.GlowingYoyo, FirmwareEffect.StarryNight, FirmwareEffect.Off,
    ];

    /// <summary>Name in the UI language.
    /// Name in der Sprache der Oberfläche.</summary>
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
    /// Speed byte for a level: Slow/Normal/Fast = 2/1/0 (breathing, flash, color cycle) or 1/0/−1 (rainbow, starry night);
    /// higher = slower. Other motion effects use the rainbow table [derived].
    /// Speed-Byte zu einer Stufe: Langsam/Normal/Schnell = 2/1/0 (Atmen, Blinken, Farbzyklus) bzw. 1/0/−1 (Regenbogen,
    /// Sternennacht); größer = langsamer. Andere Bewegungseffekte nutzen die Regenbogen-Tabelle [abgeleitet].
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

/// <summary>ENE RAM HAL effect IDs (§3.4); different numbers than the USB controller.
/// Effekt-IDs der ENE-RAM-HAL (§3.4); andere Nummern als beim USB-Controller.</summary>
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
    /// <summary>RAM effect with the same name as a USB controller firmware effect.
    /// Gleichnamiger RAM-Effekt zu einem Firmware-Effekt des USB-Controllers.</summary>
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
