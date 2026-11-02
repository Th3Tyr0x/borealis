using Borealis.Core.Lighting;

namespace Borealis.Core.Profiles;

/// <summary>
/// Farbabgleich einer Zone: Verstärkung je Farbkanal und Kanalreihenfolge, damit verschiedene Geräte dieselbe
/// Farbe gleich zeigen (wie die RGB-Header-Kalibrierung in Armoury Crate, aber für alle Zonen).
/// </summary>
public sealed class ZoneCalibration
{
    /// <summary>Rot, Grün, Blau in Prozent (0–150, Standard 100).</summary>
    public int Red { get; set; } = 100;
    public int Green { get; set; } = 100;
    public int Blue { get; set; } = 100;
    /// <summary>
    /// Kanalreihenfolge des Geräts, z. B. „GRB“: Ausgang Rot bekommt den Grün-Wert usw. Für falsch verdrahtete
    /// oder anders sortierte Strips.
    /// </summary>
    public string Order { get; set; } = "RGB";

    public static readonly string[] Orders = ["RGB", "RBG", "GRB", "GBR", "BRG", "BGR"];

    public bool IsNeutral => Red == 100 && Green == 100 && Blue == 100 && Order == "RGB";

    public Rgb Apply(Rgb c)
    {
        double r = c.R * Red / 100.0, g = c.G * Green / 100.0, b = c.B * Blue / 100.0;
        byte Clamp(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);
        double Channel(char ch) => ch switch { 'G' => g, 'B' => b, _ => r };
        var o = Orders.Contains(Order) ? Order : "RGB";
        return new Rgb(Clamp(Channel(o[0])), Clamp(Channel(o[1])), Clamp(Channel(o[2])));
    }

    public ZoneCalibration Clone() => (ZoneCalibration)MemberwiseClone();
}
