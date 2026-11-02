using System.Text;

namespace Borealis.Core.Hardware;

/// <summary>
/// Nuvoton NCT6799D Super I/O (SIO ID 0xD8xx) via PawnIO (LpcIO): RPM, PWM duty, temperatures and SmartFan IV curves.
/// Registers as in the NCT6796D datasheet / LibreHardwareMonitor; curves written like the ASUS fan service (docs/hardware.md §2.3).
/// Every access runs under the ISA bus lock; bank and index registers are restored afterwards.
/// Super-I/O NCT6799D (SIO-ID 0xD8xx) über PawnIO (LpcIO): Drehzahl, PWM-Leistung, Temperaturen und SmartFan-IV-Kurven.
/// Register wie im NCT6796D-Datenblatt / LibreHardwareMonitor; Kurven wie der ASUS-Lüfterdienst (docs/hardware.md §2.3).
/// Jeder Zugriff läuft unter der ISA-Bus-Sperre; Bank- und Indexregister werden danach wiederhergestellt.
/// </summary>
public sealed class Nct6799 : IDisposable
{
    // Configuration space / Konfigurationsraum
    const byte LdnSelect = 0x07, ChipIdHigh = 0x20, ChipIdLow = 0x21, Activate = 0x30, BaseHigh = 0x60, BaseLow = 0x61;
    const byte HardwareMonitorLdn = 0x0B;
    // Hardware monitor: address/data port from base, bank select / Adress-/Datenport ab Basis, Bankauswahl
    const int AddressOffset = 5, DataOffset = 6;
    const byte BankSelect = 0x4E;
    const ushort VendorHigh = 0x804F, VendorLow = 0x004F, NuvotonVendor = 0x5CA3;

    /// <summary>Count register per fan input (SYSFAN, CPUFAN, AUXFAN0–4); 13 bit, RPM = 1.35·10⁶ / count.
    /// Zählerregister je Lüftereingang (SYSFAN, CPUFAN, AUXFAN0–4); 13 Bit, Drehzahl = 1,35·10⁶ / Zähler.</summary>
    static readonly ushort[] FanCountRegisters = [0x4B0, 0x4B2, 0x4B4, 0x4B6, 0x4B8, 0x4BA, 0x4CC];
    /// <summary>Current PWM output per fan channel (0–255).
    /// Aktuelle PWM-Ausgabe je Lüfterkanal (0–255).</summary>
    static readonly ushort[] PwmOutRegisters = [0x001, 0x003, 0x011, 0x013, 0x015, 0xA09, 0xB09];
    public static readonly string[] FanInputs = ["SYSFAN", "CPUFAN", "AUXFAN0", "AUXFAN1", "AUXFAN2", "AUXFAN3", "AUXFAN4"];
    /// <summary>Control register bank per fan channel (matches the SIO banks in FanInfo.data, §2.4).
    /// Bank der Regelregister je Lüfterkanal (passt zu den SIO-Bänken in FanInfo.data, §2.4).</summary>
    static readonly int[] ControlBanks = [1, 2, 3, 8, 9, 10, 11];
    // Per bank: temp source, mode (0x40 = SmartFan IV), curve T1–T4 and duty 1–4 (0–255)
    // Je Bank: Temperaturquelle, Modus (0x40 = SmartFan IV), Kurve T1–T4 und Leistung 1–4 (0–255)
    const byte SourceRegister = 0x00, ModeRegister = 0x02, SmartFan4 = 0x40;
    static readonly byte[] CurveTempRegisters = [0x21, 0x22, 0x23, 0x24];
    static readonly byte[] CurveDutyRegisters = [0x27, 0x28, 0x29, 0x2A];

    /// <summary>
    /// Temperature slots: value (whole degrees), half-degree register and bit, source register, fixed source (if none) and fallback register.
    /// Temperatur-Messplätze: Wert (ganze Grad), Register und Bit für halbe Grad, Quellregister, feste Quelle (falls keins) und Ersatzregister.
    /// </summary>
    readonly record struct TempSlot(ushort Register, ushort HalfRegister, int HalfBit, ushort SourceRegister, TempSource Source, ushort Alternate = 0);

    static readonly TempSlot[] TempSlots =
    [
        new(0x073, 0x074, 7, 0x100, TempSource.Peci0),
        new(0x075, 0x076, 7, 0x200, TempSource.Cputin, 0x491),
        new(0x077, 0x078, 7, 0x300, TempSource.Systin, 0x490),
        new(0x079, 0x07A, 7, 0x800, TempSource.Auxtin0, 0x492),
        new(0x07B, 0x07C, 7, 0x900, TempSource.Auxtin1, 0x493),
        new(0x07D, 0x07E, 7, 0xA00, TempSource.Auxtin2, 0x494),
        new(0x4A0, 0x49E, 6, 0xB00, TempSource.Auxtin3, 0x495),
        new(0x027, 0, -1, 0x621, TempSource.Auxtin4),
        new(0x4A2, 0x4A1, 7, 0xC00, TempSource.TSensor, 0x496),
        new(0x150, 0x151, 7, 0x622, TempSource.SmbusMaster0),
        new(0x670, 0, -1, 0xC26, TempSource.SmbusMaster1),
        new(0x672, 0, -1, 0xC27, TempSource.Peci1),
        new(0x674, 0, -1, 0xC28, TempSource.PchChipCpuMax, 0x400),
        new(0x676, 0, -1, 0xC29, TempSource.PchChip, 0x401),
        new(0x678, 0, -1, 0xC2A, TempSource.PchCpu, 0x402),
        new(0x67A, 0, -1, 0xC2B, TempSource.PchMch, 0x404),
        new(0, 0, -1, 0, TempSource.Agent0Dimm0, 0x405),
        new(0, 0, -1, 0, TempSource.Agent0Dimm1, 0x406),
        new(0, 0, -1, 0, TempSource.Agent1Dimm0, 0x407),
        new(0, 0, -1, 0, TempSource.Agent1Dimm1, 0x408),
        new(0, 0, -1, 0, TempSource.ByteTemp0, 0x419),
        new(0, 0, -1, 0, TempSource.ByteTemp1, 0x41A),
        new(0, 0, -1, 0, TempSource.Peci0Cal, 0x4F4),
        new(0, 0, -1, 0, TempSource.Peci1Cal, 0x4F5),
    ];

    // Global config register with the hardware monitor I/O lock / Globales Konfigurationsregister mit der I/O-Sperre
    const byte IoSpaceLockRegister = 0x28, IoSpaceLock = 0x10;

    readonly BusLock isa = new(BusLock.IsaBus);
    // ASUS fan service event (auto-reset, free = signaled); created by Borealis if the service is missing
    // Ereignis des ASUS-Lüfterdienstes (Auto-Reset, frei = signalisiert); fehlt der Dienst, legt Borealis es an
    readonly EventWaitHandle asusSio = new(true, EventResetMode.AutoReset, @"Global\ASUS_SIO_MUTEX");
    readonly ushort address, data;
    readonly LpcIo lpc;

    public ushort ConfigPort { get; }
    public ushort ChipId { get; }
    public ushort Base { get; }

    Nct6799(LpcIo lpc, ushort configPort, ushort chipId, ushort baseAddress)
    {
        this.lpc = lpc;
        ConfigPort = configPort;
        ChipId = chipId;
        Base = baseAddress;
        address = (ushort)(baseAddress + AddressOffset);
        data = (ushort)(baseAddress + DataOffset);
    }

    public string Description => $"NCT6799D (ID 0x{ChipId:X4}, Port 0x{ConfigPort:X2}, Basis 0x{Base:X3})";

    // Analysis: global config 0x22–0x2F and LDN 0x0B registers 0x60–0x63, 0xE0–0xEF
    // Analyse: globale Konfiguration 0x22–0x2F und LDN-0x0B-Register 0x60–0x63, 0xE0–0xEF
    byte[] GlobalConfig = [], LdnConfig = [];

    /// <summary>Find the chip at 0x2E or 0x4E; null if no NCT6799D. Throws <see cref="PawnIoMissingException"/> without PawnIO.
    /// Chip an 0x2E bzw. 0x4E suchen; null ohne NCT6799D. Wirft <see cref="PawnIoMissingException"/> ohne PawnIO.</summary>
    public static Nct6799? Detect()
    {
        var lpc = LpcIo.Open();
        try
        {
            using var isa = new BusLock(BusLock.IsaBus);
            using var asusSio = new EventWaitHandle(true, EventResetMode.AutoReset, @"Global\ASUS_SIO_MUTEX");
            foreach (ushort port in new ushort[] { 0x2E, 0x4E })
            {
                if (!isa.Enter(1000)) throw new TimeoutException("ISA-Bus-Sperre nicht frei (ein anderes Programm greift auf den Chip zu).");
                if (!asusSio.WaitOne(2000)) { isa.Exit(); throw new TimeoutException("ASUS-SIO-Sperre nicht frei."); }
                try
                {
                    lpc.SelectSlot(port);
                    // Nuvoton config mode: 0x87 twice, leave with 0xAA / Konfigurationsmodus: zweimal 0x87, verlassen mit 0xAA
                    lpc.OutB(port, 0x87);
                    lpc.OutB(port, 0x87);
                    try
                    {
                        ushort id = (ushort)((ReadConfig(lpc, port, ChipIdHigh) << 8) | ReadConfig(lpc, port, ChipIdLow));
                        if ((id & 0xFF00) != 0xD800) continue;
                        // Unlock base addresses for LpcIO (switches the logical device)
                        // Basisadressen für LpcIO freigeben (wechselt das logische Gerät)
                        lpc.FindBars();
                        WriteConfig(lpc, port, LdnSelect, HardwareMonitorLdn);
                        bool active = (ReadConfig(lpc, port, Activate) & 1) != 0;
                        ushort baseAddress = (ushort)(((ReadConfig(lpc, port, BaseHigh) << 8) | ReadConfig(lpc, port, BaseLow)) & 0xFFF8);
                        if (!active || baseAddress < 0x100) throw new IOException($"Hardware-Monitor des NCT6799D ist nicht aktiv (Basis 0x{baseAddress:X}).");
                        var chip = new Nct6799(lpc, port, id, baseAddress);
                        chip.GlobalConfig = Enumerable.Range(0x22, 14).Select(r => ReadConfig(lpc, port, (byte)r)).ToArray();
                        chip.LdnConfig = Enumerable.Range(0x60, 4).Concat(Enumerable.Range(0xE0, 16)).Select(r => ReadConfig(lpc, port, (byte)r)).ToArray();
                        return chip;
                    }
                    finally { lpc.OutB(port, 0xAA); }
                }
                finally
                {
                    asusSio.Set();
                    isa.Exit();
                }
            }
        }
        catch
        {
            lpc.Dispose();
            throw;
        }
        lpc.Dispose();
        return null;
    }

    static byte ReadConfig(LpcIo lpc, ushort port, byte register)
    {
        lpc.OutB(port, register);
        return lpc.InB((ushort)(port + 1));
    }

    static void WriteConfig(LpcIo lpc, ushort port, byte register, byte value)
    {
        lpc.OutB(port, register);
        lpc.OutB((ushort)(port + 1), value);
    }

    /// <summary>Chip readings.
    /// Messwerte des Chips.</summary>
    public sealed record Reading(int?[] FanRpm, byte[] Pwm, IReadOnlyList<(TempSource Source, double Celsius)> Temperatures);

    public Reading Read() => Locked(io =>
    {
        if (io.Vendor != NuvotonVendor) throw new IOException($"Hardware-Monitor meldet Hersteller 0x{io.Vendor:X4} statt Nuvoton.");
        var rpm = new int?[FanCountRegisters.Length];
        var pwm = new byte[PwmOutRegisters.Length];
        for (int i = 0; i < FanCountRegisters.Length; i++)
        {
            int count = (io.Read(FanCountRegisters[i]) << 5) | (io.Read((ushort)(FanCountRegisters[i] + 1)) & 0x1F);
            // 0x1FFF = stopped or missing; very small counts are glitches / steht oder fehlt; sehr kleine Zähler sind Messfehler
            rpm[i] = count >= 0x1FFF ? 0 : count < 0x15 ? null : (int)Math.Round(1.35e6 / count);
            pwm[i] = io.Read(PwmOutRegisters[i]);
        }
        return new Reading(rpm, pwm, ReadTemperatures(io));
    });

    static List<(TempSource, double)> ReadTemperatures(Session io)
    {
        var result = new List<(TempSource, double)>();
        var seen = new HashSet<TempSource>();
        foreach (var slot in TempSlots)
        {
            if (slot.Register == 0) continue;
            var source = slot.SourceRegister > 0 ? (TempSource)(io.Read(slot.SourceRegister) & 0x1F) : slot.Source;
            if (source == TempSource.None || !seen.Add(source)) continue;
            int value = (sbyte)io.Read(slot.Register) << 1;
            if (slot.HalfBit > 0) value |= (io.Read(slot.HalfRegister) >> slot.HalfBit) & 1;
            double t = value * 0.5;
            if (t is > 125 or < -55) { seen.Remove(source); continue; }
            result.Add((source, t));
        }
        // Fallback registers for sources without a slot / Ersatzregister für Quellen ohne Messplatz
        foreach (var slot in TempSlots)
        {
            if (slot.Alternate == 0 || seen.Contains(slot.Source)) continue;
            int t = (sbyte)io.Read(slot.Alternate);
            if (t is > 125 or <= 0) continue;
            seen.Add(slot.Source);
            result.Add((slot.Source, t));
        }
        return result;
    }

    /// <summary>Raw data for analysis (<c>Borealis.Service --sensors</c>).
    /// Rohdaten für die Analyse (<c>Borealis.Service --sensors</c>).</summary>
    public string Dump() => Locked(io =>
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{Description}, Hersteller 0x{io.Vendor:X4}");
        sb.AppendLine("  CR22–2F: " + string.Join(" ", GlobalConfig.Select((v, i) => $"{0x22 + i:X2}={v:X2}")));
        sb.AppendLine("  LDN0B:   " + string.Join(" ", LdnConfig.Select((v, i) => $"{(i < 4 ? 0x60 + i : 0xE0 + i - 4):X2}={v:X2}")));
        sb.AppendLine($"  Port 0x{address:X}/0x{data:X} roh: {lpc.InB(address):X2}/{lpc.InB(data):X2}");
        for (int i = 0; i < FanCountRegisters.Length; i++)
        {
            int hi = io.Read(FanCountRegisters[i]), lo = io.Read((ushort)(FanCountRegisters[i] + 1));
            int count = (hi << 5) | (lo & 0x1F);
            sb.AppendLine($"  Lüfter {i} {FanInputs[i],-8} Zähler 0x{count:X4} → {(count >= 0x1FFF ? "steht" : count < 0x15 ? "?" : $"{1.35e6 / count:0} U/min")}, " +
                          $"PWM {io.Read(PwmOutRegisters[i])}, Modus 0x{io.Read(ControlModeRegister(i)):X2}");
        }
        foreach (var slot in TempSlots.Where(s => s.Register != 0))
        {
            int src = slot.SourceRegister > 0 ? io.Read(slot.SourceRegister) & 0x1F : (int)slot.Source;
            int value = (sbyte)io.Read(slot.Register) << 1;
            if (slot.HalfBit > 0) value |= (io.Read(slot.HalfRegister) >> slot.HalfBit) & 1;
            sb.AppendLine($"  Temp 0x{slot.Register:X3}: Quelle {src,2} ({(TempSource)src}) = {value * 0.5:0.0} °C");
        }
        foreach (var slot in TempSlots.Where(s => s.Alternate != 0))
            sb.AppendLine($"  Ersatz 0x{slot.Alternate:X3} ({slot.Source}) = {(sbyte)io.Read(slot.Alternate)} °C");
        // Control channels (SmartFan IV): registers 0x00–0x3F per bank / Regelkanäle: Register 0x00–0x3F je Bank
        for (int i = 0; i < FanInputs.Length; i++)
        {
            int bank = ControlModeRegister(i) >> 8;
            sb.Append($"  Bank {bank,2} ({FanInputs[i],-7}):");
            for (int r = 0; r < 0x40; r++) sb.Append((r % 16 == 0 ? "  " : " ") + io.Read((ushort)((bank << 8) | r)).ToString("X2"));
            sb.AppendLine();
        }
        return sb.ToString();
    });

    static ushort ControlModeRegister(int fan) => ControlRegister(fan, ModeRegister);
    static ushort ControlRegister(int fan, byte register) => (ushort)((ControlBanks[fan] << 8) | register);

    /// <summary>Curve currently in the chip for a fan channel.
    /// Im Chip stehende Kurve eines Lüfterkanals.</summary>
    public sealed record ChipCurve(byte Mode, byte Source, byte[] Temps, byte[] Duties)
    {
        public bool IsSmartFan4 => (Mode & 0xF0) == SmartFan4;
    }

    public ChipCurve ReadCurve(int fan) => Locked(io => ReadCurve(io, fan));

    static ChipCurve ReadCurve(Session io, int fan) => new(
        io.Read(ControlRegister(fan, ModeRegister)), io.Read(ControlRegister(fan, SourceRegister)),
        CurveTempRegisters.Select(r => io.Read(ControlRegister(fan, r))).ToArray(),
        CurveDutyRegisters.Select(r => io.Read(ControlRegister(fan, r))).ToArray());

    /// <summary>Registers 0x00–0x0F of a channel's control bank (read-only, for the log).
    /// Register 0x00–0x0F der Regelbank eines Kanals (nur lesend, fürs Protokoll).</summary>
    public byte[] ReadControlBlock(int fan) =>
        Locked(io => Enumerable.Range(0, 16).Select(r => io.Read(ControlRegister(fan, (byte)r))).ToArray());

    /// <summary>Write back a previously read curve unchanged (mode, points); returns the read-back curve.
    /// Zuvor gelesene Kurve unverändert zurückschreiben (Modus, Punkte); liefert die zurückgelesene Kurve.</summary>
    public ChipCurve RestoreCurve(int fan, ChipCurve original) => Locked(io =>
    {
        for (int i = 0; i < 4; i++) io.Write(ControlRegister(fan, CurveTempRegisters[i]), original.Temps[i]);
        for (int i = 0; i < 4; i++) io.Write(ControlRegister(fan, CurveDutyRegisters[i]), original.Duties[i]);
        io.Write(ControlRegister(fan, ModeRegister), original.Mode);
        return ReadCurve(io, fan);
    });

    /// <summary>
    /// Write the curve and enable SmartFan IV; the chip then regulates on its own, even without Borealis.
    /// <paramref name="source"/> also sets the temperature source (bits 0–4 of bank register 0x00). Returns the read-back curve.
    /// Kurve schreiben und SmartFan IV einschalten; danach regelt der Chip selbst, auch ohne Borealis.
    /// <paramref name="source"/> setzt auch die Temperaturquelle (Bits 0–4 im Bankregister 0x00). Liefert die zurückgelesene Kurve.
    /// </summary>
    public ChipCurve WriteCurve(int fan, IReadOnlyList<byte> temps, IReadOnlyList<byte> duties, TempSource? source = null)
    {
        if (source is { } s && (s == TempSource.None || (int)s > 0x1F)) throw new ArgumentException($"Ungültige Temperaturquelle {s}.");
        if (temps.Count != 4 || duties.Count != 4) throw new ArgumentException("Eine Kurve hat genau vier Punkte.");
        for (int i = 0; i < 4; i++)
        {
            if (temps[i] is < 1 or > 100) throw new ArgumentException($"Temperatur {temps[i]} °C außerhalb 1–100 °C.");
            if (i > 0 && temps[i] < temps[i - 1]) throw new ArgumentException("Die Temperaturen der Kurve müssen aufsteigen.");
        }
        return Locked(io =>
        {
            if (source is { } src)
            {
                byte old = io.Read(ControlRegister(fan, SourceRegister));
                if ((old & 0x1F) != (byte)src) io.Write(ControlRegister(fan, SourceRegister), (byte)((old & 0xE0) | (byte)src));
            }
            // Same order as the ASUS service: mode, temperatures, duty / Reihenfolge wie im ASUS-Dienst: Modus, Temperaturen, Leistung
            io.Write(ControlRegister(fan, ModeRegister), SmartFan4);
            for (int i = 0; i < 4; i++) io.Write(ControlRegister(fan, CurveTempRegisters[i]), temps[i]);
            for (int i = 0; i < 4; i++) io.Write(ControlRegister(fan, CurveDutyRegisters[i]), duties[i]);
            return ReadCurve(io, fan);
        });
    }

    /// <summary>
    /// Access like the ASUS fan service (§2.1): ISA bus lock, then the event <c>Global\ASUS_SIO_MUTEX</c>. The BIOS locks the
    /// hardware monitor I/O range (CR28 bit 4), so the lock is lifted during access and set again afterwards.
    /// Zugriff wie der ASUS-Lüfterdienst (§2.1): ISA-Bus-Sperre, dann das Ereignis <c>Global\ASUS_SIO_MUTEX</c>. Das BIOS sperrt
    /// den I/O-Bereich des Hardware-Monitors (CR28 Bit 4); die Sperre wird für den Zugriff aufgehoben und danach wieder gesetzt.
    /// </summary>
    T Locked<T>(Func<Session, T> body)
    {
        if (!isa.Enter()) throw new TimeoutException("ISA-Bus-Sperre nicht frei.");
        try
        {
            if (!asusSio.WaitOne(2000)) throw new TimeoutException("ASUS-SIO-Sperre nicht frei.");
            try
            {
                byte cr28 = ConfigSession(() => ReadConfig(lpc, ConfigPort, IoSpaceLockRegister));
                if ((cr28 & IoSpaceLock) != 0) ConfigSession(() => WriteConfig(lpc, ConfigPort, IoSpaceLockRegister, (byte)(cr28 & ~IoSpaceLock)));
                try
                {
                    // Save index and bank, restore afterwards / Index und Bank merken und danach wiederherstellen
                    byte oldIndex = lpc.InB(address);
                    lpc.OutB(address, BankSelect);
                    byte oldBank = lpc.InB(data);
                    var io = new Session(lpc, address, data, oldBank);
                    try { return body(io); }
                    finally
                    {
                        lpc.OutB(address, BankSelect);
                        lpc.OutB(data, oldBank);
                        lpc.OutB(address, oldIndex);
                    }
                }
                finally
                {
                    if ((cr28 & IoSpaceLock) != 0) ConfigSession(() => WriteConfig(lpc, ConfigPort, IoSpaceLockRegister, cr28));
                }
            }
            finally { asusSio.Set(); }
        }
        finally { isa.Exit(); }
    }

    /// <summary>Enter Nuvoton config mode (2× 0x87), run the action, leave (0xAA).
    /// Konfigurationsmodus betreten (2× 0x87), Aktion ausführen, verlassen (0xAA).</summary>
    T ConfigSession<T>(Func<T> action)
    {
        lpc.OutB(ConfigPort, 0x87);
        lpc.OutB(ConfigPort, 0x87);
        try { return action(); }
        finally { lpc.OutB(ConfigPort, 0xAA); }
    }

    void ConfigSession(Action action) => ConfigSession(() => { action(); return 0; });

    /// <summary>Hardware monitor register access (bank in the high byte) inside a locked session.
    /// Zugriff auf Hardware-Monitor-Register (Bank im oberen Byte) in einer gesperrten Sitzung.</summary>
    sealed class Session(LpcIo lpc, ushort address, ushort data, byte bank)
    {
        int currentBank = bank;

        public byte Read(ushort register)
        {
            int b = register >> 8;
            if (b != currentBank)
            {
                lpc.OutB(address, BankSelect);
                lpc.OutB(data, (byte)b);
                currentBank = b;
            }
            lpc.OutB(address, (byte)register);
            return lpc.InB(data);
        }

        public void Write(ushort register, byte value)
        {
            Read(register);
            lpc.OutB(data, value);
        }

        public ushort Vendor => (ushort)((Read(VendorHigh) << 8) | Read(VendorLow));
    }

    public void Dispose()
    {
        isa.Dispose();
        asusSio.Dispose();
        lpc.Dispose();
    }
}

/// <summary>NCT679x temperature sources (source register value, bits 0–4).
/// Temperaturquellen des NCT679x (Wert im Quellregister, Bits 0–4).</summary>
public enum TempSource
{
    None = 0, Systin = 1, Cputin = 2, Auxtin0 = 3, Auxtin1 = 4, Auxtin2 = 5, Auxtin3 = 6, Auxtin4 = 7,
    SmbusMaster0 = 8, SmbusMaster1 = 9, TSensor = 10, Peci0 = 16, Peci1 = 17, PchChipCpuMax = 18, PchChip = 19,
    PchCpu = 20, PchMch = 21, Agent0Dimm0 = 22, Agent0Dimm1 = 23, Agent1Dimm0 = 24, Agent1Dimm1 = 25,
    ByteTemp0 = 26, ByteTemp1 = 27, Peci0Cal = 28, Peci1Cal = 29, Virtual = 31,
}
