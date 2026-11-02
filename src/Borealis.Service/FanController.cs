using Borealis.Core.Diagnostics;
using Borealis.Core.Hardware;
using Borealis.Core.Profiles;

namespace Borealis.Service;

/// <summary>
/// Writes the fan curves to the chip (SmartFan IV); the chip then regulates without Borealis. Calls come serially from the runtime (under its lock).
/// Schreibt die Lüfterkurven in den Chip (SmartFan IV); der regelt danach ohne Borealis. Aufrufe kommen nacheinander aus der Laufzeit (unter deren Sperre).
/// </summary>
sealed class FanController : IDisposable
{
    Nct6799? chip;

    Nct6799 Chip
    {
        get
        {
            if (chip != null) return chip;
            chip = Nct6799.Detect() ?? throw new IOException("Kein unterstützter Lüfter-Chip gefunden (erwartet Nuvoton NCT6799D).");
            LogControlBlocks(chip);
            return chip;
        }
    }

    /// <summary>
    /// Logs control registers 0x00–0x0F per channel once (source, mode, step times, start/stop values) as a basis for settings like response time. Read-only.
    /// Schreibt die Regelregister 0x00–0x0F je Kanal einmal ins Protokoll (Quelle, Modus, Schrittzeiten, Start-/Stoppwerte), als Grundlage z. B. für die Reaktionszeit. Nur lesend.
    /// </summary>
    static void LogControlBlocks(Nct6799 c)
    {
        try
        {
            foreach (var h in BoardFans.Headers)
                Log.Info($"Regelregister {h.Id}: " + string.Join(" ", c.ReadControlBlock(h.Input).Select((v, r) => $"{r:X2}={v:X2}")));
        }
        catch (Exception ex) when (ex is IOException or TimeoutException) { Log.Warn("Regelregister nicht lesbar: " + ex.Message); }
    }

    /// <summary>Read the current curves from the chip (starting point for "Custom", as ASUS set them).
    /// Aktuelle Kurven aus dem Chip lesen (Ausgangspunkt für „Benutzerdefiniert“, wie ASUS sie gesetzt hat).</summary>
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

    /// <summary>Write and read back all fan curves; differences go into the notes.
    /// Kurven aller Lüfter schreiben und zurücklesen; Abweichungen landen in den Hinweisen.</summary>
    public List<string> Apply(FanSettings settings)
    {
        var notes = new List<string>();
        foreach (var h in BoardFans.Headers)
        {
            var curve = settings.CurveFor(h);
            var temps = curve.Select(p => (byte)p.Temp).ToArray();
            var duties = curve.Select(p => (byte)Math.Round(p.Duty * 2.55)).ToArray();
            var source = FanSettings.ChipSource(settings.SourceFor(h));
            var back = Chip.WriteCurve(h.Input, temps, duties, source);
            if (!back.IsSmartFan4 || !back.Temps.SequenceEqual(temps) || !back.Duties.SequenceEqual(duties))
                notes.Add($"{h.Name}: Chip meldet abweichende Kurve.");
            if ((back.Source & 0x1F) != (byte)source)
                notes.Add($"{h.Name}: Chip meldet abweichende Temperaturquelle.");
        }
        Log.Info($"Lüfterkurven geschrieben ({FanSettings.GermanName(settings.Mode)}): " +
                 string.Join("; ", BoardFans.Headers.Select(h => h.Name + " " + string.Join(" ", settings.CurveFor(h).Select(p => $"{p.Temp}°/{p.Duty}%"))
                     + (settings.SourceFor(h) != FanTempSource.Cpu ? $" nach {settings.SourceFor(h)}" : ""))) + ".");
        return notes;
    }

    public void Reset()
    {
        chip?.Dispose();
        chip = null;
    }

    public void Dispose() => Reset();
}
