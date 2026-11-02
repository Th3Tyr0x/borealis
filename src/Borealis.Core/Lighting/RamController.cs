using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace Borealis.Core.Lighting;

/// <summary>RAM-Beleuchtung, gleich über welchen Weg: eigener SMBus-Zugriff (<see cref="EneSmbusRam"/>) oder ENE-HAL.</summary>
public interface IRamLights : IDisposable
{
    IReadOnlyList<RamModule> Modules { get; }
    /// <summary>Weg der Ansteuerung, fürs Protokoll.</summary>
    string Source { get; }
    /// <summary>Effekt mit Farben je LED. <see cref="RamEffect.Direct"/> = Farben direkt anzeigen.</summary>
    void SetEffect(int module, RamEffect effect, IReadOnlyList<Rgb> colors);
    /// <summary>Effekt mit einer Farbe für alle LEDs eines Moduls.</summary>
    void SetEffect(int module, RamEffect effect, Rgb color);
}

public sealed record RamModule(int Index, string Name, int LedCount);

/// <summary>
/// RAM-Beleuchtung (ENE-Controller auf den DIMMs) über die ENE-HAL <c>ENE_RGB</c>
/// (<c>C:\Program Files\ENE\Aac_ENE RGB HAL\x64\AacHal_x64.dll</c>, in-proc). Die HAL übernimmt SMBus-Zugriff
/// und Sperren, ein eigener SMBus-Treiber ist nicht nötig (docs/hardware.md §3.4).
/// Muss in einem MTA-Thread erzeugt und benutzt werden.
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

    /// <summary>Öffnet die ENE-HAL. Wirft, wenn sie nicht installiert ist oder der Thread nicht MTA ist.</summary>
    public static RamController Open()
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            throw new InvalidOperationException("Die ENE-RAM-HAL muss aus einem MTA-Thread benutzt werden.");
        var type = Type.GetTypeFromCLSID(EneHalClsid, throwOnError: true)!;
        var hal = (IAacLedDeviceHal)Activator.CreateInstance(type)!;
        // Wie das Aura SDK: erst mit NULL die Anzahl holen, dann ein Zeiger-Array füllen lassen.
        // Enumerate2 liefert bei dieser HAL kein gültiges HRESULT (§3.4).
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

    /// <summary>Effekt mit einer Farbe für alle LEDs eines Moduls.</summary>
    public void SetEffect(int module, RamEffect effect, Rgb color) =>
        SetEffect(module, effect, Enumerable.Repeat(color, ledCounts[module]).ToArray());

    /// <summary>Effekt mit Farben je LED. <see cref="RamEffect.Direct"/> = Farben direkt anzeigen.</summary>
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

    // Schnittstellen aus Aac3572DramHal.tlb / Aac3572MbHal.tlb (§3.4)
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
