using System.Globalization;

namespace Borealis.Core.Lighting;

/// <summary>A color with 8 bits per channel.
/// Eine Farbe mit 8 Bit je Kanal.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static readonly Rgb Black = new(0, 0, 0);
    public static readonly Rgb White = new(255, 255, 255);

    /// <summary>Parses "RRGGBB" or "#RRGGBB".
    /// Liest "RRGGBB" oder "#RRGGBB".</summary>
    public static Rgb Parse(string text)
    {
        if (!TryParse(text, out var rgb)) throw new FormatException($"Ungültige Farbe „{text}“ (erwartet RRGGBB).");
        return rgb;
    }

    public static bool TryParse(string? text, out Rgb rgb)
    {
        rgb = default;
        if (text is null) return false;
        var s = text.Trim().TrimStart('#');
        if (s.Length != 6 || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
        rgb = new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    /// <summary>Color as 0x00BBGGRR, as the ENE RAM HAL expects.
    /// Farbe als 0x00BBGGRR, wie die ENE-RAM-HAL sie erwartet.</summary>
    public uint ToBgr() => (uint)(R | (G << 8) | (B << 16));

    public override string ToString() => $"{R:X2}{G:X2}{B:X2}";

    /// <summary>Color from hue (0–1, wraps), full saturation and brightness 0–1.
    /// Farbe aus Farbton (0–1, umgebrochen), voller Sättigung und Helligkeit 0–1.</summary>
    public static Rgb FromHue(double hue, double brightness = 1.0)
    {
        double h = hue % 1.0;
        if (h < 0) h += 1.0;
        double x = h * 6.0;
        int sector = (int)x;
        double f = x - sector;
        double r = 0, g = 0, b = 0;
        switch (sector)
        {
            case 0: r = 1; g = f; break;
            case 1: r = 1 - f; g = 1; break;
            case 2: g = 1; b = f; break;
            case 3: g = 1 - f; b = 1; break;
            case 4: r = f; b = 1; break;
            default: r = 1; b = 1 - f; break;
        }
        return new Rgb((byte)(r * 255 * brightness), (byte)(g * 255 * brightness), (byte)(b * 255 * brightness));
    }
}
