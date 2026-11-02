namespace Borealis.Core.Hardware;

/// <summary>
/// System-wide locks shared by hardware tools (incl. the ASUS fan service, §6): ISA bus, PCI (AMD SMN index), SMBus.
/// Systemweite Sperren, die Hardware-Tools teilen (auch der ASUS-Lüfterdienst, §6): ISA-Bus, PCI (SMN-Index), SMBus.
/// </summary>
public sealed class BusLock : IDisposable
{
    public static readonly string IsaBus = @"Global\Access_ISABUS.HTP.Method";
    public static readonly string Pci = @"Global\Access_PCI";
    public static readonly string Smbus = @"Global\Access_SMBUS.HTP.Method";

    readonly Mutex mutex;

    public BusLock(string name) { mutex = new Mutex(false, name); }

    /// <summary>Take the lock; false if another program holds it too long.
    /// Sperre nehmen; false, wenn ein anderes Programm sie zu lange hält.</summary>
    public bool Enter(int timeoutMs = 200)
    {
        try { return mutex.WaitOne(timeoutMs); }
        catch (AbandonedMutexException) { return true; }
    }

    public void Exit() => mutex.ReleaseMutex();

    public void Dispose() => mutex.Dispose();
}
