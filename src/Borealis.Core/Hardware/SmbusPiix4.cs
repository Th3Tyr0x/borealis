namespace Borealis.Core.Hardware;

/// <summary>
/// SMBus des AMD-FCH (PIIX4-kompatibel, Port 0 an 0x0B00, Port 1 an 0x0B20) über das PawnIO-Modul SmbusPIIX4.
/// Alle Übertragungen laufen über <c>ioctl_smbus_xfer</c> (Adresse, Lesen/Schreiben, Befehl, Protokoll, Daten);
/// ein Wort geht mit dem niedrigen Byte zuerst auf den Bus. Aufrufer nehmen vorher die Sperre
/// <see cref="BusLock.Smbus"/>, wie alle Programme, die auf diesen Bus zugreifen.
/// </summary>
public sealed class SmbusPiix4 : IDisposable
{
    const ulong Read = 1, Write = 0;
    const ulong Byte = 1, ByteData = 2, WordData = 3, BlockData = 5;
    public const int BlockMax = 32;

    readonly PawnIoModule module;

    SmbusPiix4(PawnIoModule module) { this.module = module; }

    /// <summary>Modul laden; wirft, wenn PawnIO fehlt oder kein passender AMD-SMBus-Controller da ist.</summary>
    public static SmbusPiix4 Open() => new(PawnIoModule.Load("SmbusPIIX4"));

    /// <summary>Basisadresse des Controllers und PCI-IDs (Hersteller/Gerät, Subsystem).</summary>
    public (ushort Base, uint PciIds, uint SubsystemIds) Identity()
    {
        var r = module.Execute("ioctl_identity", 3);
        if (r.Length < 3) throw new IOException("PawnIO SmbusPIIX4: unvollständige Kennung.");
        return ((ushort)r[1], (uint)r[2], (uint)(r[2] >> 32));
    }

    /// <summary>Port wählen (0–4); gibt den bisherigen zurück. -1 ändert nichts.</summary>
    public int SelectPort(int port) => (int)module.Execute1("ioctl_piix4_port_sel", unchecked((ulong)port));

    /// <summary>Ein Byte ohne Befehl empfangen („receive byte“); wirft, wenn niemand antwortet.</summary>
    public byte ReceiveByte(byte address) => (byte)Transfer(address, Read, 0, Byte, 0);

    public byte ReadByteData(byte address, byte command) => (byte)Transfer(address, Read, command, ByteData, 0);

    public void WriteByteData(byte address, byte command, byte value) => Transfer(address, Write, command, ByteData, value);

    /// <summary>Wort schreiben: <paramref name="value"/> & 0xFF geht zuerst auf den Bus.</summary>
    public void WriteWordData(byte address, byte command, ushort value) => Transfer(address, Write, command, WordData, value);

    /// <summary>SMBus-Block schreiben (Länge, dann bis zu 32 Byte).</summary>
    public void WriteBlockData(byte address, byte command, ReadOnlySpan<byte> data)
    {
        if (data.Length is < 1 or > BlockMax) throw new ArgumentOutOfRangeException(nameof(data));
        // Zellen 4–8: Bytes little-endian ab Zelle 4, erstes Byte = Länge
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
