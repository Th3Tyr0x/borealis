
namespace Borealis.Core.Hardware;

/// <summary>Snapshot of all fans and temperatures (sent to the app over the pipe).
/// Momentaufnahme aller Lüfter und Temperaturen (geht per Pipe an die App).</summary>
public sealed class SensorSnapshot
{
    public DateTime Time { get; set; }
    /// <summary>Where the values come from, e.g. "NCT6799D (…) via PawnIO".
    /// Woher die Werte kommen, z. B. „NCT6799D (…) über PawnIO“.</summary>
    public string? Source { get; set; }
    public string? Error { get; set; }
    public List<FanSensor> Fans { get; set; } = [];
    public List<TemperatureSensor> Temperatures { get; set; } = [];
}

public sealed class FanSensor
{
    /// <summary>Chip input, e.g. "CPUFAN".
    /// Eingang am Chip, z. B. „CPUFAN“.</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>RPM; 0 = stopped or not connected, null = no valid reading.
    /// Drehzahl; 0 = steht oder nicht angeschlossen, null = kein gültiger Wert.</summary>
    public int? Rpm { get; set; }
    /// <summary>Current duty in percent (chip PWM output).
    /// Aktuelle Leistung in Prozent (PWM-Ausgang des Chips).</summary>
    public int Duty { get; set; }
    /// <summary>A fan is connected: it spun since service start or was found by auto-tuning.
    /// Ein Lüfter hängt dran: hat seit dem Dienststart gedreht oder wurde beim Auto-Tuning gefunden.</summary>
    public bool Connected { get; set; }
}

public sealed class TemperatureSensor
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public double Celsius { get; set; }
}

/// <summary>
/// Reads fans and temperatures (NCT6799D Super I/O and AMD CPU via SMN). Read-only; curves are written by <see cref="Nct6799.WriteCurve"/>.
/// Liest Lüfter und Temperaturen (Super-I/O NCT6799D und AMD-CPU über SMN). Nur lesend; Kurven schreibt <see cref="Nct6799.WriteCurve"/>.
/// </summary>
public sealed class SensorReader : IDisposable
{

    /// <summary>Chip temperature sources wired on the board.
    /// Temperaturquellen des Chips, die auf dem Board beschaltet sind.</summary>
    static readonly Dictionary<TempSource, string> SioTemperatures = new()
    {
        [TempSource.Cputin] = "CPU-Sockel",
        [TempSource.Systin] = "Mainboard",
    };

    readonly Nct6799 sio;
    readonly AmdCpuTemperature? cpu;

    SensorReader(Nct6799 sio, AmdCpuTemperature? cpu)
    {
        this.sio = sio;
        this.cpu = cpu;
    }

    public string Source => sio.Description + " über PawnIO";

    /// <summary>Find and open the chip; throws without PawnIO or a supported chip.
    /// Chip suchen und öffnen; wirft ohne PawnIO oder unterstützten Chip.</summary>
    public static SensorReader Open()
    {
        var sio = Nct6799.Detect() ?? throw new IOException("Kein unterstützter Lüfter-Chip gefunden (erwartet Nuvoton NCT6799D).");
        AmdCpuTemperature? cpu = null;
        try { cpu = AmdCpuTemperature.Create(); }
        catch (IOException) { }
        return new SensorReader(sio, cpu);
    }

    public SensorSnapshot Read()
    {
        var snapshot = new SensorSnapshot { Time = DateTime.Now, Source = Source };
        var r = sio.Read();
        foreach (var h in BoardFans.Headers)
        {
            snapshot.Fans.Add(new FanSensor
            {
                Id = h.Id, Name = h.Name, Rpm = r.FanRpm[h.Input],
                Duty = (int)Math.Round(r.Pwm[h.Input] / 2.55),
            });
        }
        if (cpu != null)
        {
            try
            {
                var (tctl, ccds) = cpu.Read();
                snapshot.Temperatures.Add(new TemperatureSensor { Id = "tctl", Name = "CPU (Tctl)", Celsius = Math.Round(tctl, 1) });
                for (int i = 0; i < ccds.Count; i++)
                    snapshot.Temperatures.Add(new TemperatureSensor { Id = $"ccd{i + 1}", Name = $"CPU-CCD {i + 1}", Celsius = Math.Round(ccds[i], 1) });
            }
            catch (Exception ex) when (ex is IOException or TimeoutException) { snapshot.Error = "CPU-Temperatur: " + ex.Message; }
        }
        // In table order; unwired sensors read near 0 or garbage / In Tabellenreihenfolge; unbeschaltete Fühler liefern ~0 oder Unsinn
        foreach (var (source, name) in SioTemperatures)
        {
            var hit = r.Temperatures.FirstOrDefault(t => t.Source == source);
            if (hit.Source != source || hit.Celsius is < 5 or > 115) continue;
            snapshot.Temperatures.Add(new TemperatureSensor { Id = source.ToString().ToLowerInvariant(), Name = name, Celsius = hit.Celsius });
        }
        return snapshot;
    }

    public void Dispose()
    {
        sio.Dispose();
        cpu?.Dispose();
    }
}

/// <summary>A board fan header: chip input/channel, ID, name, pump yes/no.
/// Ein Lüfteranschluss des Boards: Eingang/Kanal am Chip, Kennung, Name, Pumpe ja/nein.</summary>
public sealed record FanHeader(int Input, string Id, string Name, bool Pump);

/// <summary>
/// Headers on ASUS AM5 boards such as the B650E-E (from <c>FanInfo.data</c>, §2.4); input = chip index (SYSFAN, CPUFAN, AUXFAN0–4).
/// Anschlüsse auf ASUS-AM5-Boards wie dem B650E-E (aus <c>FanInfo.data</c>, §2.4); Eingang = Index am Chip (SYSFAN, CPUFAN, AUXFAN0–4).
/// </summary>
public static class BoardFans
{
    public static readonly IReadOnlyList<FanHeader> Headers =
    [
        new(1, "CPUFAN", "CPU-Lüfter", false),
        new(0, "SYSFAN", "Gehäuselüfter 1", false),
        new(2, "AUXFAN0", "Gehäuselüfter 2", false),
        new(3, "AUXFAN1", "Gehäuselüfter 3", false),
        new(4, "AUXFAN2", "Gehäuselüfter 4", false),
        new(5, "AUXFAN3", "Gehäuselüfter 5", false),
        new(6, "AUXFAN4", "AIO-Pumpe", true),
    ];

    public static FanHeader? Find(string id) => Headers.FirstOrDefault(h => h.Id == id);
}
