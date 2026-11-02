// Borealis.Service – Hintergrunddienst (LocalSystem): wendet das aktive Beleuchtungsprofil beim Start und nach dem
// Standby an und nimmt Befehle der Tray-App per Named Pipe entgegen.
// Aufrufe: ohne Argumente durch den Dienststeuerungs-Manager; „--console“ im Vordergrund (Strg+C beendet);
// „install“ / „uninstall“ richten den Dienst ein bzw. entfernen ihn (Administrator).
using Borealis.Core.Diagnostics;
using Borealis.Core.Windows;

namespace Borealis.Service;

static class Program
{
    public const string ServiceName = "BorealisService";
    const string DisplayName = "Borealis";
    const string Description = "Beleuchtung (Mainboard, ARGB, RAM) ohne Armoury Crate; wendet das aktive Borealis-Profil an.";

    static int Main(string[] args)
    {
        Log.Init("service");
        string command = args.FirstOrDefault()?.ToLowerInvariant() ?? "";
        switch (command)
        {
            case "install":
                Log.ToConsole = true;
                ServiceControl.Install(ServiceName, DisplayName, Description, Environment.ProcessPath!, ServiceStartType.Automatic);
                ServiceControl.Start(ServiceName);
                Log.Info($"Dienst {ServiceName} installiert und gestartet ({Environment.ProcessPath}).");
                return 0;
            case "uninstall":
                Log.ToConsole = true;
                Log.Info(ServiceControl.Uninstall(ServiceName) ? $"Dienst {ServiceName} entfernt." : $"Dienst {ServiceName} war nicht installiert.");
                return 0;
            case "--sensors":
                // Analyse: Rohwerte des Super-I/O und der CPU einmal ausgeben (nur lesend)
                Console.OutputEncoding = System.Text.Encoding.UTF8;
                try
                {
                    using var nct = Borealis.Core.Hardware.Nct6799.Detect();
                    Console.WriteLine(nct?.Dump() ?? "Kein NCT6799D gefunden.");
                    using var cpu = Borealis.Core.Hardware.AmdCpuTemperature.Create();
                    if (cpu != null)
                    {
                        var (tctl, ccds) = cpu.Read();
                        Console.WriteLine($"CPU (Familie 0x{cpu.Family:X2}, Modell 0x{cpu.Model:X2}): Tctl {tctl:0.0} °C, CCDs {string.Join(" / ", ccds.Select(c => c.ToString("0.0")))} °C");
                    }
                }
                catch (Exception ex) when (ex is IOException or TimeoutException) { Console.WriteLine("Fehler: " + ex.Message); }
                return 0;
            case "--version":
                // Ladetest (z. B. gegen Smart App Control): lädt Borealis.Core und gibt die Version aus
                Console.WriteLine($"Borealis.Service {typeof(Program).Assembly.GetName().Version} mit Borealis.Core {typeof(Log).Assembly.GetName().Version}");
                return 0;
            case "--ramprobe":
                // Analyse: RAM-Beleuchtung über SMBus lesen (nur ENE-Adressen 0x70–0x73, nur lesend)
                Console.OutputEncoding = System.Text.Encoding.UTF8;
                try { return RamProbe.Run(); }
                catch (Exception ex) when (ex is IOException or TimeoutException) { Console.WriteLine("Fehler: " + ex.Message); return 1; }
            case "--fantest":
                // Schreibtest (nur mit Freigabe des Benutzers): Kurve von Gehäuselüfter 1 kurz auf 100 % setzen,
                // Drehzahl beobachten, Originalkurve exakt zurückschreiben und prüfen
                Console.OutputEncoding = System.Text.Encoding.UTF8;
                return FanTest.Run();
            case "--console":
                Log.ToConsole = true;
                Console.OutputEncoding = System.Text.Encoding.UTF8;
                using (var cts = new CancellationTokenSource())
                {
                    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
                    try { Worker.RunAsync(cts.Token).GetAwaiter().GetResult(); }
                    catch (OperationCanceledException) { }
                }
                return 0;
        }

        var host = new ServiceHost(ServiceName, Worker.RunAsync);
        host.PowerEvent += Worker.OnPowerEvent;
        if (!host.RunAsService())
        {
            Console.Error.WriteLine("Borealis.Service ist ein Windows-Dienst. Zum Testen: Borealis.Service --console; einrichten: Borealis.Service install");
            return 1;
        }
        return 0;
    }
}

static class Worker
{
    static BorealisRuntime? runtime;

    public static async Task RunAsync(CancellationToken token)
    {
        Log.Info($"Borealis-Dienst gestartet (Version {typeof(Worker).Assembly.GetName().Version}).");
        using var rt = new BorealisRuntime();
        runtime = rt;
        try
        {
            await rt.StartAsync(token);
            var watch = rt.WatchAsusLightingServiceAsync(token);
            await new PipeServer(rt).RunAsync(token);
            try { await watch; } catch (OperationCanceledException) { }
        }
        finally
        {
            runtime = null;
            Log.Info("Borealis-Dienst beendet.");
        }
    }

    public static void OnPowerEvent(int eventType)
    {
        const int PBT_APMSUSPEND = 0x4, PBT_APMRESUMEAUTOMATIC = 0x12;
        if (eventType == PBT_APMSUSPEND) Log.Info("System geht in den Standby.");
        else if (eventType == PBT_APMRESUMEAUTOMATIC)
        {
            Log.Info("System aus dem Standby zurück.");
            if (runtime is { } rt) _ = Task.Run(rt.OnResumeAsync);
        }
    }
}
