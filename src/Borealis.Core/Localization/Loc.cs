using System.Text.RegularExpressions;

namespace Borealis.Core.Localization;

/// <summary>
/// UI language: German (default) or English. App texts are pairs <c>Loc.T("Deutsch", "English")</c>; German terms from Core
/// and the service (effect, mode, fan and zone names) are translated with <see cref="Term"/>. The service log stays German.
/// Sprache der Oberfläche: Deutsch (Standard) oder Englisch. App-Texte sind Paare <c>Loc.T("Deutsch", "English")</c>; deutsche
/// Begriffe aus Core und Dienst (Effekt-, Modus-, Lüfter-, Zonennamen) übersetzt <see cref="Term"/>. Das Dienst-Log bleibt deutsch.
/// </summary>
public static class Loc
{
    public static bool English { get; set; }

    public static string T(string de, string en) => English ? en : de;

    /// <summary>Translate a German term for display (unknown ones stay as they are).
    /// Deutschen Begriff für die Anzeige übersetzen (unbekannte bleiben, wie sie sind).</summary>
    public static string Term(string de)
    {
        if (!English || string.IsNullOrEmpty(de)) return de;
        if (Terms.TryGetValue(de, out var en)) return en;
        foreach (var (pattern, replacement) in Patterns)
            if (pattern.IsMatch(de)) return pattern.Replace(de, replacement);
        return de;
    }

    static readonly Dictionary<string, string> Terms = new()
    {
        // Effects / Effekte
        ["Aus"] = "Off", ["Statisch"] = "Static", ["Atmen"] = "Breathing", ["Blinken"] = "Strobing",
        ["Farbzyklus"] = "Color cycle", ["Regenbogen"] = "Rainbow", ["Komet"] = "Comet", ["Welle"] = "Wave",
        ["Sternennacht"] = "Starry night", ["Atmender Regenbogen"] = "Breathing rainbow", ["Regenbogen-Komet"] = "Rainbow comet",
        ["Feuerwerk"] = "Fireworks", ["Musik"] = "Music", ["Feuer"] = "Fire", ["Farbfluss"] = "Color flow",
        ["Herzschlag"] = "Heartbeat", ["Funkeln"] = "Twinkle",
        // Effect groups / Effektgruppen
        ["Grundeffekte"] = "Basic", ["Bewegung"] = "Motion", ["Lichtspiele"] = "Light shows", ["Reaktiv"] = "Reactive",
        // Effect settings / Effekt-Einstellungen
        ["Größe"] = "Size", ["Farbe 1"] = "Color 1", ["Farbe 2"] = "Color 2", ["Breite"] = "Width", ["Schweiflänge"] = "Tail length",
        ["Länge"] = "Length", ["Wellenlänge"] = "Wavelength", ["Empfindlichkeit"] = "Sensitivity", ["Niedrig"] = "Low", ["Hoch"] = "High",
        ["Flammenhöhe"] = "Flame height", ["Muster"] = "Pattern",
        // Music and smart modes / Musik- und Smart-Modi
        ["Pegel"] = "Level", ["Spektrum"] = "Spectrum", ["Spektrum gespiegelt"] = "Mirrored spectrum", ["Puls"] = "Pulse",
        ["Welle im Takt"] = "Beat wave", ["Lauflicht im Takt"] = "Beat chaser", ["Funken im Takt"] = "Beat sparks",
        ["Tonhöhe"] = "Pitch", ["Drop-Strobo"] = "Drop strobe",
        ["CPU · Farbe"] = "CPU · Color", ["CPU · Balken"] = "CPU · Bar", ["RAM · Farbe"] = "RAM · Color", ["RAM · Balken"] = "RAM · Bar",
        // Fan modes, fans and temperatures / Lüftermodi, Lüfter und Temperaturen
        ["Leise"] = "Silent", ["Volle Geschwindigkeit"] = "Full speed", ["Benutzerdefiniert"] = "Custom",
        ["Lüfter"] = "Fans", ["Pumpe"] = "Pump", ["CPU-Lüfter"] = "CPU fan", ["AIO-Pumpe"] = "AIO pump", ["CPU-Sockel"] = "CPU socket", ["Mainboard"] = "Motherboard",
        // Zones / Zonen
        ["Mainboard 12-V-RGB-Header"] = "Motherboard 12 V RGB header",
    };

    static readonly (Regex Pattern, string Replacement)[] Patterns =
    [
        (new Regex(@"^Gehäuselüfter (\d+)$"), "Chassis fan $1"),
        (new Regex(@"^CPU-CCD (\d+)$"), "CPU CCD $1"),
        (new Regex(@"^ROG-Logo \(Back-IO (\d+)\)$"), "ROG logo (back I/O $1)"),
        (new Regex(@"^ARGB-Header (\d+)$"), "ARGB header $1"),
    ];
}
