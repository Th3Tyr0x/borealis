using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace Borealis.Core.Lighting;

/// <summary>RAM lighting by either path: own SMBus access (<see cref="EneSmbusRam"/>) or the ENE HAL.
/// RAM-Beleuchtung über einen der Wege: eigener SMBus-Zugriff (<see cref="EneSmbusRam"/>) oder ENE-HAL.</summary>
public interface IRamLights : IDisposable
{
    IReadOnlyList<RamModule> Modules { get; }
    /// <summary>Access path, for the log.
    /// Weg der Ansteuerung, fürs Protokoll.</summary>
    string Source { get; }
    /// <summary>Effect with colors per LED; <see cref="RamEffect.Direct"/> = show colors directly.
    /// Effekt mit Farben je LED; <see cref="RamEffect.Direct"/> = Farben direkt anzeigen.</summary>
    void SetEffect(int module, RamEffect effect, IReadOnlyList<Rgb> colors);
    /// <summary>Effect with one color for all LEDs of a module.
    /// Effekt mit einer Farbe für alle LEDs eines Moduls.</summary>
    void SetEffect(int module, RamEffect effect, Rgb color);
}

public sealed record RamModule(int Index, string Name, int LedCount);

/// <summary>
/// RAM lighting via the in-proc ENE HAL <c>ENE_RGB</c> (<c>AacHal_x64.dll</c>), which handles SMBus and locking (docs/hardware.md §3.4). MTA thread only.
/// RAM-Beleuchtung über die ENE-HAL <c>ENE_RGB</c> (<c>AacHal_x64.dll</c>, in-proc); sie übernimmt SMBus und Sperren (docs/hardware.md §3.4). Nur im MTA-Thread.
/// </summary>
public sealed class RamController : IRamLights
{
    public static readonly Guid EneHalClsid = new("05B8E7A3-7D5D-41DE-B6CC-5E6205837FB7");

    readonly IAacLedDeviceHal hal;
    readonly IAacLedDevice?[] devices;
    readonly int[] ledCounts;

    public IReadOnlyList<RamModule> Modules { get; }
    public string Source => "ENE-HAL";

    RamController(IAacLedDeviceHal hal, IAacLedDevice?[] devices)
    {
        this.hal = hal;
        this.devices = devices;
        ledCounts = new int[devices.Length];
        var modules = new List<RamModule>();
        for (int i = 0; i < devices.Length; i++)
        {
            string name = $"RAM {i + 1}";
            int leds = 8;
            if (devices[i] is { } d && d.GetCapability(out var xml) >= 0 && xml != null)
            {
                try
                {
                    var root = XDocument.Parse(xml).Root;
                    var dev = root?.Element("device");
                    name = dev?.Element("name")?.Value ?? name;
                    if (int.TryParse(dev?.Element("layout")?.Element("led_count")?.Value, out var n) && n > 0) leds = n;
                }
                catch (System.Xml.XmlException) { }
            }
            ledCounts[i] = leds;
            modules.Add(new RamModule(i, name, leds));
        }
        Modules = modules;
    }

    /// <summary>Opens the ENE HAL; throws if not installed or the thread isn't MTA.
    /// Öffnet die ENE-HAL; wirft, wenn sie fehlt oder der Thread nicht MTA ist.</summary>
    public static RamController Open()
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            throw new InvalidOperationException("Die ENE-RAM-HAL muss aus einem MTA-Thread benutzt werden.");
        var type = Type.GetTypeFromCLSID(EneHalClsid, throwOnError: true)!;
        var hal = (IAacLedDeviceHal)Activator.CreateInstance(type)!;
        // Get the count with NULL first, then let it fill a pointer array; Enumerate2 returns no valid HRESULT here (§3.4)
        // Erst mit NULL die Anzahl holen, dann ein Zeiger-Array füllen lassen; Enumerate2 liefert hier kein gültiges HRESULT (§3.4)
        hal.Enumerate(IntPtr.Zero, out uint count);
        var devices = new IAacLedDevice?[count];
        if (count > 0)
        {
            IntPtr buf = Marshal.AllocCoTaskMem(IntPtr.Size * (int)count);
            try
            {
                for (int i = 0; i < count; i++) Marshal.WriteIntPtr(buf, i * IntPtr.Size, IntPtr.Zero);
                hal.Enumerate(buf, out count);
                for (int i = 0; i < devices.Length; i++)
                {
                    IntPtr p = Marshal.ReadIntPtr(buf, i * IntPtr.Size);
                    if (p == IntPtr.Zero) continue;
                    devices[i] = (IAacLedDevice)Marshal.GetObjectForIUnknown(p);
                    Marshal.Release(p);
                }
            }
            finally { Marshal.FreeCoTaskMem(buf); }
        }
        return new RamController(hal, devices);
    }

    public string GetCapability(int module)
    {
        var d = devices[module] ?? throw new InvalidOperationException($"RAM-Modul {module} nicht verfügbar.");
        Marshal.ThrowExceptionForHR(d.GetCapability(out var xml));
        return xml ?? "";
    }

    /// <summary>Effect with one color for all LEDs of a module.
    /// Effekt mit einer Farbe für alle LEDs eines Moduls.</summary>
    public void SetEffect(int module, RamEffect effect, Rgb color) =>
        SetEffect(module, effect, Enumerable.Repeat(color, ledCounts[module]).ToArray());

    /// <summary>Effect with colors per LED; <see cref="RamEffect.Direct"/> = show colors directly.
    /// Effekt mit Farben je LED; <see cref="RamEffect.Direct"/> = Farben direkt anzeigen.</summary>
    public void SetEffect(int module, RamEffect effect, IReadOnlyList<Rgb> colors)
    {
        var d = devices[module] ?? throw new InvalidOperationException($"RAM-Modul {module} nicht verfügbar.");
        var raw = colors.Select(c => c.ToBgr()).ToArray();
        int hr = d.SetEffect((uint)effect, raw, (uint)raw.Length);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    public void Dispose()
    {
        foreach (var d in devices) if (d != null) Marshal.ReleaseComObject(d);
        Marshal.ReleaseComObject(hal);
    }

    // Interfaces from Aac3572DramHal.tlb / Aac3572MbHal.tlb (§3.4) / Schnittstellen aus diesen TLBs
    [ComImport, Guid("F2C8D5B4-3854-4325-8A4F-FD7C5072E3B9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAacLedDeviceHal
    {
        [PreserveSig] int Enumerate(IntPtr devices, out uint count);
        [PreserveSig] int Enumerate2(out object devices, out uint count);
    }

    [ComImport, Guid("61711778-AB59-4026-89E8-7A63422C29C2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAacLedDevice
    {
        [PreserveSig] int GetCapability([MarshalAs(UnmanagedType.BStr)] out string? capability);
        [PreserveSig] int SetEffect(uint effectId, [MarshalAs(UnmanagedType.LPArray)] uint[] colors, uint numberOfColors);
        [PreserveSig] int Synchronize(uint effectId, ulong milliseconds);
    }
}
