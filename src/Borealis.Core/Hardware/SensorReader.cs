
namespace Borealis.Core.Hardware;

/// <summary>Momentaufnahme aller Lüfter und Temperaturen (geht per Pipe an die App).</summary>
public sealed class SensorSnapshot
{
    public DateTime Time { get; set; }
    /// <summary>Woher die Werte kommen, z. B. „NCT6799D (…) über PawnIO“.</summary>
    public string? Source { get; set; }
    public string? Error { get; set; }
    public List<FanSensor> Fans { get; set; } = [];
    public List<TemperatureSensor> Temperatures { get; set; } = [];
}

public sealed class FanSensor
{
    /// <summary>Eingang am Chip, z. B. „CPUFAN“.</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Drehzahl; 0 = steht oder nicht angeschlossen, null = kein gültiger Messwert.</summary>
    public int? Rpm { get; set; }
    /// <summary>Aktuelle Leistung in Prozent (PWM-Ausgang des Chips).</summary>
    public int Duty { get; set; }
    /// <summary>Ein Lüfter hängt dran: hat seit dem Dienststart gedreht oder wurde beim Auto-Tuning gefunden.</summary>
    public bool Connected { get; set; }
}

public sealed class TemperatureSensor
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public double Celsius { get; set; }
}

/// <summary>
/// Liest Lüfter und Temperaturen über den ASUS-IO-Treiber: Super-I/O NCT6799D und die AMD-CPU (SMN).
/// Nur lesend; die Kurven schreibt <see cref="Nct6799.WriteCurve"/>.
/// </summary>
public sealed class SensorReader : IDisposable
{

    /// <summary>Temperaturquellen des Chips, die auf dem Board beschaltet sind.</summary>
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

    /// <summary>Chip suchen und öffnen; wirft, wenn PawnIO fehlt oder kein unterstützter Chip gefunden wird.</summary>
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
        // In der Reihenfolge der Tabelle; unbeschaltete Fühler liefern Werte nahe 0 oder Unsinn
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

/// <summary>Ein Lüfteranschluss des Boards: Eingang/Kanal am Chip, Kennung, Name, Pumpe ja/nein.</summary>
public sealed record FanHeader(int Input, string Id, string Name, bool Pump);

/// <summary>
/// Anschlüsse auf ASUS-AM5-Boards wie dem ROG STRIX B650E-E (aus <c>FanInfo.data</c>, §2.4: SIO-Bank je Lüfter),
/// in der Reihenfolge von Armoury Crate. Eingang = Index am Chip (SYSFAN, CPUFAN, AUXFAN0–4).
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
