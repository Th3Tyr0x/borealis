using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Borealis.Core.Lighting;

/// <summary>
/// HID-Transport zum ASUS-RGB-Controller über USB (Report-ID 0xEC, immer 65 Byte, docs/hardware.md §1.1).
/// Kennt nur Senden, Lesen und Abfragen; die Befehle selbst stehen in <see cref="UsbLedController"/>.
/// </summary>
public sealed class UsbLedHid : IDisposable
{
    public const int ReportSize = 65;
    public const byte ReportId = 0xEC;

    /// <summary>Pfadfilter für den Controller auf diesem Board (ROG STRIX B650E-E, „6K75“).</summary>
    public const string DefaultPathFilter = "vid_0b05&pid_19af&mi_02";

    readonly SafeFileHandle handle;
    readonly FileStream stream;
    // Systemweite Sperre, die auch die ASUS-HAL nimmt (§1.1). Solange ASUS-Software installiert ist, verhindert
    // sie, dass Reports zweier Prozesse ineinander laufen.
    readonly Mutex usbMutex;

    Task<int>? pendingRead;
    byte[] pendingBuf = new byte[ReportSize];

    public string DevicePath { get; }

    UsbLedHid(string path, SafeFileHandle handle)
    {
        DevicePath = path;
        this.handle = handle;
        stream = new FileStream(handle, FileAccess.ReadWrite, ReportSize, isAsync: true);
        usbMutex = new Mutex(false, @"Global\asusaurausb");
    }

    public static UsbLedHid Open(string pathFilter = DefaultPathFilter)
    {
        var path = FindDevice(pathFilter) ?? throw new IOException($"ASUS-RGB-Controller (USB) nicht gefunden ({pathFilter}).");
        var h = Native.CreateFile(path, Native.GENERIC_READ | Native.GENERIC_WRITE,
            Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING,
            Native.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (h.IsInvalid) throw new IOException($"ASUS-RGB-Controller (USB) lässt sich nicht öffnen (Fehler {Marshal.GetLastWin32Error()}).");
        return new UsbLedHid(path, h);
    }

    public static string? FindDevice(string pathFilter)
    {
        Native.HidD_GetHidGuid(out var hidGuid);
        IntPtr set = Native.SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, Native.DIGCF_PRESENT | Native.DIGCF_DEVICEINTERFACE);
        if (set == new IntPtr(-1)) return null;
        try
        {
            var data = new Native.SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<Native.SP_DEVICE_INTERFACE_DATA>() };
            for (uint i = 0; Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, i, ref data); i++)
            {
                Native.SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out uint required, IntPtr.Zero);
                IntPtr detail = Marshal.AllocHGlobal((int)required);
                try
                {
                    // cbSize von SP_DEVICE_INTERFACE_DETAIL_DATA_W unter x64
                    Marshal.WriteInt32(detail, 8);
                    if (!Native.SetupDiGetDeviceInterfaceDetail(set, ref data, detail, required, out _, IntPtr.Zero)) continue;
                    string? path = Marshal.PtrToStringUni(detail + 4);
                    if (path != null && path.Contains(pathFilter, StringComparison.OrdinalIgnoreCase)) return path;
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { Native.SetupDiDestroyDeviceInfoList(set); }
        return null;
    }

    /// <summary>Nimmt die Sperre <c>Global\asusaurausb</c>. Mit <c>using</c> verwenden.</summary>
    public IDisposable Lock(int timeoutMs = 5000)
    {
        bool taken;
        try { taken = usbMutex.WaitOne(timeoutMs); }
        catch (AbandonedMutexException) { taken = true; }
        if (!taken) throw new TimeoutException(@"Sperre Global\asusaurausb nicht erhalten (anderer Prozess sendet gerade).");
        return new Releaser(usbMutex);
    }

    sealed class Releaser(Mutex m) : IDisposable
    {
        int done;
        public void Dispose() { if (Interlocked.Exchange(ref done, 1) == 0) m.ReleaseMutex(); }
    }

    /// <summary>Sendet <c>EC</c> + payload, aufgefüllt auf 65 Byte. Bei Fehler wie der HAL einmal wiederholen (§1.1).</summary>
    public void Write(ReadOnlySpan<byte> payload)
    {
        var buf = new byte[ReportSize];
        buf[0] = ReportId;
        payload[..Math.Min(payload.Length, ReportSize - 1)].CopyTo(buf.AsSpan(1));
        try { stream.Write(buf, 0, ReportSize); }
        catch (IOException)
        {
            Thread.Sleep(1);
            Native.HidD_FlushQueue(handle);
            stream.Write(buf, 0, ReportSize);
        }
    }

    public void Write(params byte[] payload) => Write(payload.AsSpan());

    /// <summary>
    /// Liest, bis eine Antwort kommt, deren Bytes [1..] mit <paramref name="echo"/> beginnen.
    /// Ein offener Lesevorgang bleibt über Timeouts hinweg bestehen, damit kein Report verloren geht.
    /// </summary>
    public byte[]? ReadMatching(ReadOnlySpan<byte> echo, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (true)
        {
            long left = deadline - Environment.TickCount64;
            if (left <= 0) return null;
            if (pendingRead == null)
            {
                pendingBuf = new byte[ReportSize];
                pendingRead = stream.ReadAsync(pendingBuf, 0, ReportSize);
            }
            if (!pendingRead.Wait((int)left)) return null;
            var buf = pendingBuf;
            int n = pendingRead.Result;
            pendingRead = null;
            if (n < 2 || buf[0] != ReportId) continue;
            if (buf.AsSpan(1, echo.Length).SequenceEqual(echo)) return buf;
        }
    }

    /// <summary>Abfrage ohne Index: <c>EC &lt;cmd|0x80&gt;</c>, Antwort mit <c>[1] = cmd</c> (§1.1).</summary>
    public byte[]? Query(byte cmd, int timeoutMs = 2000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            Write((byte)(cmd | 0x80));
            var r = ReadMatching([cmd], 30);
            if (r != null) return r;
        }
        return null;
    }

    /// <summary>
    /// Indizierte Abfrage wie die HAL 1.6.1.9 (§1.1): <c>EC cmd idx</c> setzt den Index,
    /// <c>EC cmd|0x80</c> fordert an, die Antwort wiederholt <c>[1]=cmd, [2]=idx</c>. Alle 30 ms neu.
    /// </summary>
    public byte[]? QueryIndexed(byte cmd, byte idx, int timeoutMs = 2000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            Write(cmd, idx);
            Write((byte)(cmd | 0x80));
            var r = ReadMatching([cmd, idx], 30);
            if (r != null) return r;
        }
        return null;
    }

    public void Dispose()
    {
        stream.Dispose();
        usbMutex.Dispose();
    }

    static class Native
    {
        public const uint DIGCF_PRESENT = 0x2, DIGCF_DEVICEINTERFACE = 0x10;
        public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3, FILE_FLAG_OVERLAPPED = 0x40000000;

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid guid; public int flags; public IntPtr reserved; }

        [DllImport("hid.dll")] public static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_FlushQueue(SafeFileHandle h);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwnd, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid guid, uint index, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, uint size, out uint required, IntPtr devInfo);
        [DllImport("setupapi.dll")] public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disposition, uint flags, IntPtr template);
    }
}
