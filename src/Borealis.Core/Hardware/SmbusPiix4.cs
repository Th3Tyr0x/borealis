namespace Borealis.Core.Hardware;

/// <summary>
/// AMD FCH SMBus (PIIX4-compatible, port 0 at 0x0B00, port 1 at 0x0B20) via the PawnIO module SmbusPIIX4 (<c>ioctl_smbus_xfer</c>).
/// Words go low byte first. Callers take <see cref="BusLock.Smbus"/> first, like every program on this bus.
/// SMBus des AMD-FCH (PIIX4-kompatibel, Port 0 an 0x0B00, Port 1 an 0x0B20) über das PawnIO-Modul SmbusPIIX4 (<c>ioctl_smbus_xfer</c>).
/// Wörter gehen mit dem niedrigen Byte zuerst. Aufrufer nehmen vorher <see cref="BusLock.Smbus"/>, wie alle Programme auf diesem Bus.
/// </summary>
public sealed class SmbusPiix4 : IDisposable
{
    const ulong Read = 1, Write = 0;
    const ulong Byte = 1, ByteData = 2, WordData = 3, BlockData = 5;
    public const int BlockMax = 32;

    readonly PawnIoModule module;

    SmbusPiix4(PawnIoModule module) { this.module = module; }

    /// <summary>Load the module; throws without PawnIO or a matching AMD SMBus controller.
    /// Modul laden; wirft ohne PawnIO oder passenden AMD-SMBus-Controller.</summary>
    public static SmbusPiix4 Open() => new(PawnIoModule.Load("SmbusPIIX4"));

    /// <summary>Controller base address and PCI IDs (vendor/device, subsystem).
    /// Basisadresse des Controllers und PCI-IDs (Hersteller/Gerät, Subsystem).</summary>
    public (ushort Base, uint PciIds, uint SubsystemIds) Identity()
    {
        var r = module.Execute("ioctl_identity", 3);
        if (r.Length < 3) throw new IOException("PawnIO SmbusPIIX4: unvollständige Kennung.");
        return ((ushort)r[1], (uint)r[2], (uint)(r[2] >> 32));
    }

    /// <summary>Select a port (0–4); returns the previous one. -1 changes nothing.
    /// Port wählen (0–4); liefert den bisherigen. -1 ändert nichts.</summary>
    public int SelectPort(int port) => (int)module.Execute1("ioctl_piix4_port_sel", unchecked((ulong)port));

    /// <summary>Receive one byte without a command ("receive byte"); throws if nobody answers.
    /// Ein Byte ohne Befehl empfangen („receive byte“); wirft, wenn niemand antwortet.</summary>
    public byte ReceiveByte(byte address) => (byte)Transfer(address, Read, 0, Byte, 0);

    public byte ReadByteData(byte address, byte command) => (byte)Transfer(address, Read, command, ByteData, 0);

    public void WriteByteData(byte address, byte command, byte value) => Transfer(address, Write, command, ByteData, value);

    /// <summary>Write a word: <paramref name="value"/> & 0xFF goes on the bus first.
    /// Wort schreiben: <paramref name="value"/> & 0xFF geht zuerst auf den Bus.</summary>
    public void WriteWordData(byte address, byte command, ushort value) => Transfer(address, Write, command, WordData, value);

    /// <summary>Write an SMBus block (length, then up to 32 bytes).
    /// SMBus-Block schreiben (Länge, dann bis zu 32 Byte).</summary>
    public void WriteBlockData(byte address, byte command, ReadOnlySpan<byte> data)
    {
        if (data.Length is < 1 or > BlockMax) throw new ArgumentOutOfRangeException(nameof(data));
        // Cells 4–8: bytes little-endian from cell 4, first byte = length / Zellen 4–8: Bytes little-endian, erstes Byte = Länge
        var bytes = new byte[5 * sizeof(ulong)];
        bytes[0] = (byte)data.Length;
        data.CopyTo(bytes.AsSpan(1));
        var input = new ulong[9];
        input[0] = address;
        input[1] = Write;
        input[2] = command;
        input[3] = BlockData;
        Buffer.BlockCopy(bytes, 0, input, 4 * sizeof(ulong), bytes.Length);
        module.Execute("ioctl_smbus_xfer", 6, input);
    }

    ulong Transfer(byte address, ulong direction, byte command, ulong protocol, ulong data)
    {
        if (address > 0x7F) throw new ArgumentOutOfRangeException(nameof(address));
        var r = module.Execute("ioctl_smbus_xfer", 1, address, direction, command, protocol, data);
        return r.Length > 0 ? r[0] : 0;
    }

    public void Dispose() => module.Dispose();
}
