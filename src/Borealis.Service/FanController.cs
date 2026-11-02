using Borealis.Core.Diagnostics;
using Borealis.Core.Hardware;
using Borealis.Core.Profiles;

namespace Borealis.Service;

/// <summary>
/// Schreibt die Lüfterkurven in den Chip (SmartFan IV, wie der ASUS-Lüfterdienst). Der Chip regelt danach selbst;
/// Borealis muss dafür nicht weiterlaufen. Aufrufe kommen nacheinander aus der Laufzeit (unter deren Sperre).
/// </summary>
sealed class FanController : IDisposable
{
    Nct6799? chip;

    Nct6799 Chip => chip ??= Nct6799.Detect() ?? throw new IOException("Kein unterstützter Lüfter-Chip gefunden (erwartet Nuvoton NCT6799D).");

    /// <summary>Aktuelle Kurven aus dem Chip lesen (Ausgangspunkt für „Benutzerdefiniert“, so wie ASUS sie gesetzt hat).</summary>
    public Dictionary<string, List<FanPoint>> ReadCurves()
    {
        var result = new Dictionary<string, List<FanPoint>>();
        foreach (var h in BoardFans.Headers)
        {
            var c = Chip.ReadCurve(h.Input);
            result[h.Id] = FanSettings.Normalize(Enumerable.Range(0, 4)
                .Select(i => new FanPoint(c.Temps[i], (int)Math.Round(c.Duties[i] / 2.55))).ToList());
        }
        return result;
    }

    /// <summary>Kurven aller Lüfter schreiben und zurücklesen; Abweichungen landen in den Hinweisen.</summary>
    public List<string> Apply(FanSettings settings)
    {
        var notes = new List<string>();
        foreach (var h in BoardFans.Headers)
        {
            var curve = settings.CurveFor(h);
            var temps = curve.Select(p => (byte)p.Temp).ToArray();
            var duties = curve.Select(p => (byte)Math.Round(p.Duty * 2.55)).ToArray();
            var back = Chip.WriteCurve(h.Input, temps, duties);
            if (!back.IsSmartFan4 || !back.Temps.SequenceEqual(temps) || !back.Duties.SequenceEqual(duties))
                notes.Add($"{h.Name}: Chip meldet abweichende Kurve.");
        }
        Log.Info($"Lüfterkurven geschrieben ({FanSettings.GermanName(settings.Mode)}): " +
                 string.Join("; ", BoardFans.Headers.Select(h => h.Name + " " + string.Join(" ", settings.CurveFor(h).Select(p => $"{p.Temp}°/{p.Duty}%")))) + ".");
        return notes;
    }

    public void Reset()
    {
        chip?.Dispose();
        chip = null;
    }

    public void Dispose() => Reset();
}
