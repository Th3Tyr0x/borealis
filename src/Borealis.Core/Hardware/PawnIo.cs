using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace Borealis.Core.Hardware;

/// <summary>
/// Hardwarezugriff über den offenen, von Microsoft signierten Treiber PawnIO (https://pawnio.eu), den auch OpenRGB,
/// LibreHardwareMonitor und FanControl nutzen. PawnIO führt kleine, signierte Module im Kernel aus; jedes Modul erlaubt
/// nur bestimmte Zugriffe (LpcIO: Super-I/O-Ports, AMDFamily17: SMN lesen, SmbusPIIX4: SMBus des AMD-FCH).
/// Die Module liegen unverändert als Ressource in Borealis.Core (PawnIO.Modules 0.2.11, LGPL-2.1, siehe
/// <c>Hardware/PawnIO/COPYING</c>). Schnittstelle wie PawnIOLib: Gerät <c>\Device\PawnIO</c>, IOCTL „Modul laden“
/// und „Funktion ausführen“ (32 Byte Funktionsname, dann 64-Bit-Zellen). Braucht Administratorrechte.
/// </summary>
public static class PawnIo
{
    /// <summary>Installer, den Borealis bei der Einrichtung lädt (feste Version, Prüfsumme von GitHub).</summary>
    public const string SetupVersion = "2.2.0";
    public const string SetupUrl = "https://github.com/namazso/PawnIO.Setup/releases/download/2.2.0/PawnIO_setup.exe";
    public const string SetupSha256 = "1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032";
    public const string SetupArguments = "-install -silent";

    /// <summary>Installierte Version laut Windows (Programme und Features), null wenn PawnIO fehlt; ohne Adminrechte lesbar.</summary>
    public static string? InstalledVersion()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
        return key == null ? null : key.GetValue("DisplayVersion") as string ?? "?";
    }
}

/// <summary>PawnIO ist nicht installiert (das Gerät <c>\Device\PawnIO</c> fehlt).</summary>
public sealed class PawnIoMissingException()
    : IOException("PawnIO ist nicht installiert. Borealis braucht den Treiber für Lüfter, Temperaturen und RAM-Beleuchtung; einrichten unter Einstellungen.");

/// <summary>Ein geladenes PawnIO-Modul (eigenes Handle je Modul, wie PawnIOLib).</summary>
public sealed class PawnIoModule : IDisposable
{
    const string DevicePath = @"\\.\GLOBALROOT\Device\PawnIO";
    const uint DeviceType = 41394;
    // CTL_CODE(DeviceType, Funktion, METHOD_BUFFERED, FILE_ANY_ACCESS)
    static uint Ctl(uint function) => (DeviceType << 16) | (function << 2);
    static readonly uint IoctlLoad = Ctl(0x821), IoctlExecute = Ctl(0x841);
    const int NameLength = 32;

    readonly SafeFileHandle handle;
    readonly object gate = new();

    public string Name { get; }

    PawnIoModule(SafeFileHandle handle, string name)
    {
        this.handle = handle;
        Name = name;
    }

    /// <summary>Modul aus den Ressourcen laden, z. B. „LpcIO“; wirft <see cref="PawnIoMissingException"/>, wenn PawnIO fehlt.</summary>
    public static PawnIoModule Load(string name)
    {
        var handle = Native.CreateFile(DevicePath, Native.GenericRead | Native.GenericWrite, Native.FileShareAll, IntPtr.Zero, Native.OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            throw error switch
            {
                2 or 3 => new PawnIoMissingException(),
                5 => new IOException("PawnIO: Zugriff verweigert (braucht Administratorrechte)."),
                _ => new IOException($"PawnIO lässt sich nicht öffnen: {new Win32Exception(error).Message}"),
            };
        }
        try
        {
            byte[] blob = ReadModule(name);
            if (!Native.DeviceIoControl(handle, IoctlLoad, blob, blob.Length, null, 0, out _, IntPtr.Zero))
                throw new IOException($"PawnIO-Modul {name} lässt sich nicht laden: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
            return new PawnIoModule(handle, name);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    static byte[] ReadModule(string name)
    {
        using var stream = typeof(PawnIoModule).Assembly.GetManifestResourceStream($"PawnIO.{name}.bin")
                           ?? throw new IOException($"PawnIO-Modul {name} fehlt in Borealis.Core.");
        var blob = new byte[stream.Length];
        stream.ReadExactly(blob);
        return blob;
    }

    /// <summary>Funktion des Moduls ausführen; gibt die geschriebenen Ausgabezellen zurück (höchstens <paramref name="outCount"/>).</summary>
    public ulong[] Execute(string function, int outCount, params ulong[] input)
    {
        if (Encoding.ASCII.GetByteCount(function) >= NameLength) throw new ArgumentException("Funktionsname zu lang.", nameof(function));
        var inBuffer = new byte[NameLength + input.Length * sizeof(ulong)];
        Encoding.ASCII.GetBytes(function, inBuffer);
        Buffer.BlockCopy(input, 0, inBuffer, NameLength, input.Length * sizeof(ulong));
        var outBuffer = outCount > 0 ? new byte[outCount * sizeof(ulong)] : null;
        int written;
        lock (gate)
        {
            if (!Native.DeviceIoControl(handle, IoctlExecute, inBuffer, inBuffer.Length, outBuffer, outBuffer?.Length ?? 0, out written, IntPtr.Zero))
                throw new IOException($"PawnIO {Name}.{function}: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
        }
        var result = new ulong[written / sizeof(ulong)];
        if (result.Length > 0) Buffer.BlockCopy(outBuffer!, 0, result, 0, result.Length * sizeof(ulong));
        return result;
    }

    /// <summary>Funktion mit genau einem Rückgabewert.</summary>
    public ulong Execute1(string function, params ulong[] input)
    {
        var r = Execute(function, 1, input);
        return r.Length == 1 ? r[0] : throw new IOException($"PawnIO {Name}.{function}: keine Antwort.");
    }

    public void Dispose() => handle.Dispose();

    static class Native
    {
        public const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, FileShareAll = 7, OpenExisting = 3;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[]? inBuffer, int inSize, byte[]? outBuffer, int outSize,
                                                  out int returned, IntPtr overlapped);
    }
}

/// <summary>
/// Port-I/O am Super-I/O über das PawnIO-Modul LpcIO. Erlaubt sind nur das gewählte Konfigurationsport-Paar
/// (0x2E/0x2F oder 0x4E/0x4F), die Basisadressen, die <see cref="FindBars"/> im Chip gefunden hat (z. B. der
/// Hardware-Monitor bei 0x290), und die ASUS-EC-Ports 0x25C/0x25D.
/// </summary>
public sealed class LpcIo : IDisposable
{
    readonly PawnIoModule module;

    LpcIo(PawnIoModule module) { this.module = module; }

    public static LpcIo Open() => new(PawnIoModule.Load("LpcIO"));

    /// <summary>Konfigurationsport wählen (0x2E oder 0x4E); vergisst die gefundenen Basisadressen.</summary>
    public void SelectSlot(ushort configPort) => module.Execute("ioctl_select_slot", 0, configPort switch
    {
        0x2E => 0UL,
        0x4E => 1UL,
        _ => throw new ArgumentOutOfRangeException(nameof(configPort)),
    });

    /// <summary>Basisadressen aller logischen Geräte suchen und freigeben; der Chip muss im Konfigurationsmodus sein.</summary>
    public void FindBars() => module.Execute("ioctl_find_bars", 0);

    public byte InB(ushort port) => (byte)module.Execute1("ioctl_pio_inb", port);

    public void OutB(ushort port, byte value) => module.Execute("ioctl_pio_outb", 0, port, value);

    public void Dispose() => module.Dispose();
}
