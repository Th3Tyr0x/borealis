namespace Borealis.Core.Hardware;

/// <summary>
/// Systemweite Sperren, die Hardware-Tools untereinander nutzen (auch der ASUS-Lüfterdienst, §6, und PawnIO-Programme):
/// ISA-Bus für Super-I/O-Zugriffe, PCI für den SMN-Index der AMD-CPU, SMBus für RAM-Beleuchtung und SPD.
/// </summary>
public sealed class BusLock : IDisposable
{
    public static readonly string IsaBus = @"Global\Access_ISABUS.HTP.Method";
    public static readonly string Pci = @"Global\Access_PCI";
    public static readonly string Smbus = @"Global\Access_SMBUS.HTP.Method";

    readonly Mutex mutex;

    public BusLock(string name) { mutex = new Mutex(false, name); }

    /// <summary>Sperre nehmen; false, wenn ein anderes Programm sie zu lange hält.</summary>
    public bool Enter(int timeoutMs = 200)
    {
        try { return mutex.WaitOne(timeoutMs); }
        catch (AbandonedMutexException) { return true; }
    }

    public void Exit() => mutex.ReleaseMutex();

    public void Dispose() => mutex.Dispose();
}
