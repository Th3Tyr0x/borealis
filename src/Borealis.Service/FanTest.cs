using Borealis.Core.Hardware;

namespace Borealis.Service;

/// <summary>
/// Fan curve hardware test (<c>Borealis.Service --fantest</c>, admin): reads all curves, sets chassis fan 1 briefly to a flat 100 % curve, then restores the original exactly.
/// Hardwaretest der Lüfterkurven (<c>Borealis.Service --fantest</c>, Admin): liest alle Kurven, setzt Gehäuselüfter 1 kurz auf flache 100 % und schreibt danach die Originalkurve exakt zurück.
/// </summary>
static class FanTest
{
    public static int Run()
    {
        using var reader = SensorReader.Open();
        using var chip = Nct6799.Detect() ?? throw new IOException("Kein NCT6799D gefunden.");
        Console.WriteLine(chip.Description);
        foreach (var h in BoardFans.Headers)
        {
            var c = chip.ReadCurve(h.Input);
            Console.WriteLine($"  {h.Name,-16} Modus 0x{c.Mode:X2} Quelle {c.Source,2}  Kurve " +
                              string.Join("  ", Enumerable.Range(0, 4).Select(i => $"{c.Temps[i]}°/{c.Duties[i] * 100 / 255}%")));
        }

        var fan = BoardFans.Find("SYSFAN")!;
        var original = chip.ReadCurve(fan.Input);
        Console.WriteLine($"Test {fan.Name}: vorher {Rpm(reader, fan)} U/min");
        try
        {
            var written = chip.WriteCurve(fan.Input, original.Temps, [255, 255, 255, 255]);
            Console.WriteLine($"  geschrieben: Modus 0x{written.Mode:X2}, Leistung {string.Join("/", written.Duties)}");
            for (int s = 0; s < 4; s++)
            {
                Thread.Sleep(5000);
                Console.WriteLine($"  nach {(s + 1) * 5,2} s: {Rpm(reader, fan)} U/min");
            }
        }
        finally
        {
            var back = chip.RestoreCurve(fan.Input, original);
            bool same = back.Mode == original.Mode && back.Temps.SequenceEqual(original.Temps) && back.Duties.SequenceEqual(original.Duties);
            Console.WriteLine($"  zurückgeschrieben: {(same ? "identisch mit dem Original" : "ABWEICHUNG!")}");
        }
        Thread.Sleep(6000);
        Console.WriteLine($"  6 s danach: {Rpm(reader, fan)} U/min");
        return 0;
    }

    static string Rpm(SensorReader reader, FanHeader fan)
    {
        var f = reader.Read().Fans.FirstOrDefault(x => x.Id == fan.Id);
        return f == null ? "?" : $"{f.Rpm} (Leistung {f.Duty} %)";
    }
}
