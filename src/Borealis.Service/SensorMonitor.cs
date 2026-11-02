using Borealis.Core.Diagnostics;
using Borealis.Core.Hardware;

namespace Borealis.Service;

/// <summary>
/// Liest Lüfter und Temperaturen einmal pro Sekunde (eigener Thread) und hält die letzte Messung bereit.
/// Fehlt PawnIO oder der Chip, wird es alle 30 s erneut versucht (sofort nach <see cref="Retry"/>).
/// </summary>
sealed class SensorMonitor : IDisposable
{
    readonly Thread thread;
    readonly ManualResetEventSlim stop = new(false);
    readonly AutoResetEvent retry = new(false);
    volatile SensorSnapshot latest = new() { Error = "Noch keine Messung." };
    // Anschlüsse, an denen seit dem Start ein Lüfter gedreht hat (ein Lüfter mit Stopp-Funktion steht auch mal)
    readonly HashSet<string> seenRunning = new();
    readonly Func<string, bool> knownConnected;

    /// <param name="knownConnected">Anschluss ist sicher belegt (z. B. vom Auto-Tuning gefunden).</param>
    public SensorMonitor(Func<string, bool> knownConnected)
    {
        this.knownConnected = knownConnected;
        thread = new Thread(Run) { IsBackground = true, Name = "Borealis-Sensoren" };
        thread.Start();
    }

    public SensorSnapshot Latest => latest;

    void Run()
    {
        SensorReader? reader = null;
        string? lastError = null;
        while (!stop.IsSet)
        {
            try
            {
                if (reader == null)
                {
                    reader = SensorReader.Open();
                    Log.Info($"Sensoren: {reader.Source}.");
                }
                var snapshot = reader.Read();
                foreach (var f in snapshot.Fans)
                {
                    if (f.Rpm > 0) seenRunning.Add(f.Id);
                    f.Connected = seenRunning.Contains(f.Id) || knownConnected(f.Id);
                }
                latest = snapshot;
                lastError = null;
                stop.Wait(1000);
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or DllNotFoundException or EntryPointNotFoundException)
            {
                // Bei Zeitüberschreitung (Chip gerade belegt) beim nächsten Mal wieder versuchen, sonst neu öffnen
                if (ex is not TimeoutException) { reader?.Dispose(); reader = null; }
                if (ex.Message != lastError) Log.Warn("Sensoren nicht lesbar: " + ex.Message);
                lastError = ex.Message;
                latest = new SensorSnapshot { Time = DateTime.Now, Error = ex.Message, Fans = latest.Fans, Temperatures = latest.Temperatures };
                WaitHandle.WaitAny([stop.WaitHandle, retry], ex is TimeoutException ? 1000 : 30000);
            }
        }
        reader?.Dispose();
    }

    /// <summary>Nach einem Fehler sofort neu verbinden (z. B. gleich nach der Einrichtung von PawnIO).</summary>
    public void Retry() => retry.Set();

    public void Dispose()
    {
        stop.Set();
        thread.Join(2000);
        stop.Dispose();
        retry.Dispose();
    }
}
