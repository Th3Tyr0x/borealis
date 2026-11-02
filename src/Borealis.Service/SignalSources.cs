using System.Runtime.InteropServices;
using Borealis.Core.Lighting;

namespace Borealis.Service;

/// <summary>
/// Live signals for reactive effects (§5): audio analysis (<see cref="AudioAnalyzer"/>), CPU load (<c>GetSystemTimes</c>), RAM usage (<c>GlobalMemoryStatusEx</c>); only polled while a reactive effect runs.
/// Live-Signale für reaktive Effekte (§5): Audio-Analyse (<see cref="AudioAnalyzer"/>), CPU-Last (<c>GetSystemTimes</c>), RAM-Belegung (<c>GlobalMemoryStatusEx</c>); nur abgefragt, solange ein reaktiver Effekt läuft.
/// </summary>
sealed class SignalSources : IDisposable
{
    readonly EffectInputs inputs = new();
    readonly AudioAnalyzer audio = new();
    long lastIdle, lastKernel, lastUser, nextSystemSample;

    /// <summary>Update the signals (from the effect thread per frame) and return them.
    /// Signale aktualisieren (vom Effekt-Thread je Bild) und zurückgeben.</summary>
    public EffectInputs Update(bool needAudio, bool needSystem)
    {
        if (needAudio)
        {
            audio.EnsureRunning();
            audio.Read(inputs);
        }
        long now = Environment.TickCount64;
        if (needSystem && now >= nextSystemSample)
        {
            nextSystemSample = now + 500;
            if (GetSystemTimes(out long idle, out long kernel, out long user))
            {
                long dIdle = idle - lastIdle, total = kernel - lastKernel + user - lastUser;
                if (lastKernel != 0 && total > 0)
                    inputs.CpuLoad += (Math.Clamp(1 - (double)dIdle / total, 0, 1) - inputs.CpuLoad) * 0.4;
                lastIdle = idle; lastKernel = kernel; lastUser = user;
            }
            var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref mem)) inputs.RamLoad += (mem.dwMemoryLoad / 100.0 - inputs.RamLoad) * 0.4;
        }
        return inputs;
    }

    public void Dispose() => audio.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);
}
