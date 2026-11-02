using Borealis.Core.Hardware;

namespace Borealis.Service;

/// <summary>
/// Analyse der RAM-Beleuchtung über SMBus (<c>--ramprobe</c>), nur lesend. Spricht ausschließlich die ENE-Adressen
/// 0x70–0x73 an, die die ENE-HAL auf diesem PC gefunden hat; SPD-Hub (0x50–0x57) und PMIC (0x48–0x4F) nie.
/// Klärt die Byte-Reihenfolge der Registeradresse (Chip-ID-Register 0xF01C in beiden Reihenfolgen) und liest
/// Name, Konfiguration und Steuerblöcke.
/// </summary>
static class RamProbe
{
    static readonly byte[] Addresses = [0x70, 0x71, 0x72, 0x73];

    public static int Run()
    {
        using var bus = SmbusPiix4.Open();
        var (baseAddress, ids, subsystem) = bus.Identity();
        Console.WriteLine($"SMBus PIIX4: Basis 0x{baseAddress:X4}, PCI {ids & 0xFFFF:X4}:{ids >> 16:X4}, Subsystem {subsystem:X8}, Port {bus.SelectPort(-1)}");
        using var smbusLock = new BusLock(BusLock.Smbus);
        foreach (var a in Addresses)
        {
            if (!smbusLock.Enter(2000)) { Console.WriteLine("SMBus-Sperre nicht frei."); return 1; }
            try
            {
                try { bus.ReceiveByte(a); }
                catch (IOException ex) { Console.WriteLine($"0x{a:X2}: keine Antwort ({ex.Message})"); continue; }
                byte asIs = ReadRegister(bus, a, 0xF01C, swap: false, 1)[0];
                byte swapped = ReadRegister(bus, a, 0xF01C, swap: true, 1)[0];
                Console.WriteLine($"0x{a:X2}: Chip-ID 0xF01C unverändert=0x{asIs:X2}, vertauscht=0x{swapped:X2}");
                foreach (bool swap in new[] { false, true })
                {
                    string order = swap ? "vertauscht" : "unverändert";
                    Console.WriteLine($"  [{order}] 0x1000 Name : {Ascii(ReadRegister(bus, a, 0x1000, swap, 16))}");
                    Console.WriteLine($"  [{order}] 0x1C00 Konf.: {Hex(ReadRegister(bus, a, 0x1C00, swap, 32))}");
                    Console.WriteLine($"  [{order}] 0x8020 Steuer: {Hex(ReadRegister(bus, a, 0x8020, swap, 16))}");
                    Console.WriteLine($"  [{order}] 0xE020 Steuer: {Hex(ReadRegister(bus, a, 0xE020, swap, 16))}");
                    Console.WriteLine($"  [{order}] 0x8100 Farben: {Hex(ReadRegister(bus, a, 0x8100, swap, 24))}");
                    Console.WriteLine($"  [{order}] 0xE100 Farben: {Hex(ReadRegister(bus, a, 0xE100, swap, 24))}");
                }
            }
            catch (IOException ex) { Console.WriteLine($"0x{a:X2}: Fehler {ex.Message}"); }
            finally { smbusLock.Exit(); }
        }
        return 0;
    }

    /// <summary>Register wählen (Wort auf Befehl 0x00) und Bytes über Befehl 0x81 lesen (Adresse zählt selbst hoch).</summary>
    static byte[] ReadRegister(SmbusPiix4 bus, byte address, ushort register, bool swap, int count)
    {
        ushort word = swap ? (ushort)((register << 8) | (register >> 8)) : register;
        bus.WriteWordData(address, 0x00, word);
        var data = new byte[count];
        for (int i = 0; i < count; i++) data[i] = bus.ReadByteData(address, 0x81);
        return data;
    }

    static string Hex(byte[] data) => string.Join(" ", data.Select(b => b.ToString("X2")));

    static string Ascii(byte[] data) => new string(data.Select(b => b is >= 0x20 and < 0x7F ? (char)b : '.').ToArray()) + "  (" + Hex(data) + ")";
}
