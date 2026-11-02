using System.Text;
using Borealis.Core.Hardware;

namespace Borealis.Core.Lighting;

/// <summary>
/// RAM lighting directly over SMBus (PawnIO, SmbusPIIX4) instead of the ENE HAL, same sequence as the HAL
/// (docs/hardware.md §3) for DIMMs with an ASUS-family ENE controller (chip ID 0x68).
/// RAM-Beleuchtung direkt über SMBus (PawnIO, SmbusPIIX4) statt über die ENE-HAL, Ablauf wie die HAL
/// (docs/hardware.md §3) für DIMMs mit ENE-Controller der ASUS-Familie (Chip-ID 0x68).
/// <list type="bullet">
/// <item>Select register: word on command 0x00, address high byte first. / Register wählen: Wort auf Befehl 0x00, hohes Byte zuerst.</item>
/// <item>Write: blocks up to 24 bytes on 0x03, else byte by byte on 0x01; read: 0x81. <see cref="Open"/> tests blocks once.
/// Schreiben: Blöcke bis 24 Byte auf 0x03, sonst Byte für Byte auf 0x01; Lesen: 0x81. <see cref="Open"/> prüft Blöcke einmal.</item>
/// <item>Colors per LED as R, B, G: direct from 0x8100, firmware effects from 0x8160. / Farben je LED als R, B, G: direkt ab 0x8100, Firmware ab 0x8160.</item>
/// <item>Control block at 0x8020 (16 bytes): direct mode, effect, speed, direction; 0x802F = apply. / Steuerblock ab 0x8020; 0x802F = übernehmen.</item>
/// </list>
/// Only 0x70–0x77 are touched, and only addresses identified as ENE RAM are written; never SPD hub (0x50–0x57) or PMIC (0x48–0x4F).
/// Nur 0x70–0x77 werden angesprochen und nur als ENE-RAM erkannte Adressen beschrieben; nie SPD-Hub (0x50–0x57) oder PMIC (0x48–0x4F).
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
        // Last written control block; bytes 4–14 stay as the controller had them
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

    /// <summary>Find ENE DIMMs on port 0; null if none. Throws without PawnIO.
    /// ENE-DIMMs auf Port 0 suchen; null, wenn keine. Wirft ohne PawnIO.</summary>
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
                    // Only addresses that answer get a register select / Nur Adressen, die antworten, bekommen eine Registerwahl
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
    /// Effect with colors per LED. <see cref="RamEffect.Direct"/> shows colors directly (fast, no control block while the mode stays); otherwise the DIMM firmware runs it.
    /// Effekt mit Farben je LED. <see cref="RamEffect.Direct"/> zeigt die Farben direkt (schnell, ohne Steuerblock, solange der Modus bleibt); sonst spielt ihn die DIMM-Firmware.
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
            dimm.Control[2] = 0; // Speed: normal / Geschwindigkeit: normal
            dimm.Control[3] = 0; // Direction / Richtung
            dimm.Control[15] = 1; // 0x802F: apply / übernehmen
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

    /// <summary>Write the shown direct colors back as a block and compare (nothing visible changes).
    /// Angezeigte Direktfarben als Block zurückschreiben und vergleichen (sichtbar ändert sich nichts).</summary>
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
