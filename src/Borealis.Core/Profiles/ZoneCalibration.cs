using Borealis.Core.Lighting;

namespace Borealis.Core.Profiles;

/// <summary>
/// Zone calibration: gain per color channel and channel order, so different devices show the same color alike.
/// Farbabgleich einer Zone: Verstärkung je Farbkanal und Kanalreihenfolge, damit verschiedene Geräte dieselbe Farbe gleich zeigen.
/// </summary>
public sealed class ZoneCalibration
{
    /// <summary>Red, green, blue in percent (0–150, default 100).
    /// Rot, Grün, Blau in Prozent (0–150, Standard 100).</summary>
    public int Red { get; set; } = 100;
    public int Green { get; set; } = 100;
    public int Blue { get; set; } = 100;
    /// <summary>Device channel order, e.g. "GRB": the red output gets the green value etc. For miswired or differently ordered strips.
    /// Kanalreihenfolge des Geräts, z. B. „GRB“: Ausgang Rot bekommt den Grün-Wert usw. Für falsch verdrahtete oder anders sortierte Strips.</summary>
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
