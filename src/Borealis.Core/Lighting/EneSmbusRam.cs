using System.Text;
using Borealis.Core.Hardware;

namespace Borealis.Core.Lighting;

/// <summary>
/// RAM-Beleuchtung direkt über SMBus (PawnIO, Modul SmbusPIIX4) statt über die ENE-HAL; Ablauf wie die HAL
/// (<c>AacHal_x64.dll</c>, docs/hardware.md §3) für DIMMs mit ENE-Controller der ASUS-Familie
/// (Chip-ID 0x68, Firmware z. B. „AUDA0-E6K5-0101“).
/// <list type="bullet">
/// <item>Register wählen: Wort auf Befehl 0x00, Registeradresse mit dem hohen Byte zuerst auf dem Bus.</item>
/// <item>Schreiben: SMBus-Blöcke bis 24 Byte auf Befehl 0x03, sonst Byte für Byte auf Befehl 0x01 (die Adresse zählt
/// selbst hoch); Lesen: Befehl 0x81. Ob Blöcke gehen, prüft <see cref="Open"/> einmal mit unveränderten Farben.</item>
/// <item>Farben je LED als R, B, G: direkt ab 0x8100, für Firmware-Effekte ab 0x8160.</item>
/// <item>Steuerblock ab 0x8020 (16 Byte): Direktmodus, Effekt, Geschwindigkeit, Richtung; 0x802F = übernehmen.</item>
/// </list>
/// Angesprochen werden nur 0x70–0x77, und geschrieben wird nur auf Adressen, die sich mit Chip-ID und Namen als
/// ENE-RAM ausweisen; SPD-Hub (0x50–0x57) und PMIC (0x48–0x4F) der DDR5-Module nie.
/// </summary>
public sealed class EneSmbusRam : IRamLights
{
    const ushort RegName = 0x1000, RegConfig = 0x1C00, RegChipId = 0xF01C;
    const ushort RegControl = 0x8020, RegDirectColors = 0x8100, RegEffectColors = 0x8160;
    const byte ConfigLedCount = 0x02;
    const byte CmdSelect = 0x00, CmdWrite = 0x01, CmdBlock = 0x03, CmdRead = 0x81;
    const int BlockLength = 24;
    const byte ChipAsus = 0x68;
    const int MaxLeds = 32;

    sealed class Dimm(byte address, byte[] control)
    {
        public byte Address { get; } = address;
        // Zuletzt geschriebener Steuerblock; Bytes 4–14 bleiben, wie der Controller sie hatte
        public byte[] Control { get; } = control;
        public RamEffect? Effect { get; set; }
    }

    readonly SmbusPiix4 bus;
    readonly BusLock smbusLock = new(BusLock.Smbus);
    readonly Dimm[] dimms;
    readonly bool blocks;

    public IReadOnlyList<RamModule> Modules { get; }
    public string Source => "SMBus über PawnIO" + (blocks ? ", Blockschreiben" : "");

    EneSmbusRam(SmbusPiix4 bus, List<(Dimm Dimm, RamModule Module)> found, bool blocks)
    {
        this.bus = bus;
        this.blocks = blocks;
        dimms = found.Select(f => f.Dimm).ToArray();
        Modules = found.Select(f => f.Module).ToArray();
    }

    /// <summary>ENE-DIMMs auf Port 0 suchen; null, wenn keine gefunden wurden. Wirft, wenn PawnIO fehlt.</summary>
    public static EneSmbusRam? Open()
    {
        var bus = SmbusPiix4.Open();
        try
        {
            using var probeLock = new BusLock(BusLock.Smbus);
            var found = new List<(Dimm, RamModule)>();
            for (byte address = 0x70; address <= 0x77; address++)
            {
                if (!probeLock.Enter(2000)) throw new TimeoutException("SMBus-Sperre nicht frei.");
                try
                {
                    // Nur Adressen, an denen jemand antwortet, bekommen eine Registerwahl
                    try { bus.ReceiveByte(address); }
                    catch (IOException) { continue; }
                    if (Read(bus, address, RegChipId, 1)[0] != ChipAsus) continue;
                    string name = Encoding.ASCII.GetString(Read(bus, address, RegName, 16)).Split('\0')[0];
                    if (name.Length < 4 || name.Any(c => c is < ' ' or > '~')) continue;
                    int leds = Read(bus, address, (ushort)(RegConfig + ConfigLedCount), 1)[0];
                    if (leds is < 1 or > MaxLeds) leds = 8;
                    var control = Read(bus, address, RegControl, 16);
                    int index = found.Count;
                    found.Add((new Dimm(address, control), new RamModule(index, name, leds)));
                }
                finally { probeLock.Exit(); }
            }
            if (found.Count == 0)
            {
                bus.Dispose();
                return null;
            }
            bool blocks;
            if (!probeLock.Enter(2000)) throw new TimeoutException("SMBus-Sperre nicht frei.");
            try { blocks = BlocksWork(bus, found[0].Item1.Address, found[0].Item2.LedCount * 3); }
            finally { probeLock.Exit(); }
            return new EneSmbusRam(bus, found, blocks);
        }
        catch
        {
            bus.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Effekt mit Farben je LED. <see cref="RamEffect.Direct"/> = Farben direkt anzeigen (schnell, ohne Steuerblock,
    /// solange der Modus gleich bleibt); sonst übernimmt die Firmware des DIMMs den Effekt.
    /// </summary>
    public void SetEffect(int module, RamEffect effect, IReadOnlyList<Rgb> colors)
    {
        var dimm = dimms[module];
        int leds = Math.Min(colors.Count, Modules[module].LedCount);
        var rbg = new byte[leds * 3];
        for (int i = 0; i < leds; i++)
        {
            rbg[i * 3] = colors[i].R;
            rbg[i * 3 + 1] = colors[i].B;
            rbg[i * 3 + 2] = colors[i].G;
        }
        bool direct = effect == RamEffect.Direct;
        if (!smbusLock.Enter(1000)) throw new TimeoutException("SMBus-Sperre nicht frei.");
        try
        {
            Write(bus, dimm.Address, direct ? RegDirectColors : RegEffectColors, rbg, blocks);
            if (dimm.Effect == effect && direct) return;
            dimm.Control[0] = (byte)(direct ? 1 : 0);
            dimm.Control[1] = (byte)(direct ? 0 : effect == RamEffect.Default ? 5 : (int)effect);
            dimm.Control[2] = 0;   // Geschwindigkeit: normal
            dimm.Control[3] = 0;   // Richtung
            dimm.Control[15] = 1;  // 0x802F: übernehmen
            Write(bus, dimm.Address, RegControl, dimm.Control, blocks);
            dimm.Effect = effect;
        }
        finally { smbusLock.Exit(); }
    }

    public void SetEffect(int module, RamEffect effect, Rgb color) =>
        SetEffect(module, effect, Enumerable.Repeat(color, Modules[module].LedCount).ToArray());

    static void Select(SmbusPiix4 bus, byte address, ushort register) =>
        bus.WriteWordData(address, CmdSelect, (ushort)((register << 8) | (register >> 8)));

    static byte[] Read(SmbusPiix4 bus, byte address, ushort register, int count)
    {
        Select(bus, address, register);
        var data = new byte[count];
        for (int i = 0; i < count; i++) data[i] = bus.ReadByteData(address, CmdRead);
        return data;
    }

    static void Write(SmbusPiix4 bus, byte address, ushort register, byte[] data, bool blocks)
    {
        if (address is < 0x70 or > 0x77) throw new ArgumentOutOfRangeException(nameof(address), "Nur ENE-Adressen 0x70–0x77.");
        Select(bus, address, register);
        if (blocks)
            for (int i = 0; i < data.Length; i += BlockLength)
                bus.WriteBlockData(address, CmdBlock, data.AsSpan(i, Math.Min(BlockLength, data.Length - i)));
        else
            foreach (var b in data) bus.WriteByteData(address, CmdWrite, b);
    }

    /// <summary>Die gerade angezeigten Direktfarben als Block zurückschreiben und vergleichen (sichtbar ändert sich nichts).</summary>
    static bool BlocksWork(SmbusPiix4 bus, byte address, int length)
    {
        try
        {
            var current = Read(bus, address, RegDirectColors, length);
            Write(bus, address, RegDirectColors, current, blocks: true);
            return Read(bus, address, RegDirectColors, length).AsSpan().SequenceEqual(current);
        }
        catch (IOException) { return false; }
    }

    public void Dispose()
    {
        bus.Dispose();
        smbusLock.Dispose();
    }
}
