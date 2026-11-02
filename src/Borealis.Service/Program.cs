// Borealis.Service – background service (LocalSystem): applies the active lighting profile at start and after standby,
// takes tray app commands over a named pipe. No args: run by the SCM; "--console" in the foreground; "install"/"uninstall" (admin).
// Borealis.Service – Hintergrunddienst (LocalSystem): wendet das aktive Profil beim Start und nach dem Standby an und nimmt
// Befehle der Tray-App per Named Pipe an. Ohne Argumente: vom SCM; „--console“ im Vordergrund; „install“/„uninstall“ (Admin).
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
                // Analysis: print raw Super I/O and CPU values once (read-only) / Analyse: Rohwerte von Super-I/O und CPU einmal ausgeben
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
                // Load test (e.g. Smart App Control): loads Borealis.Core and prints the version
                // Ladetest (z. B. Smart App Control): lädt Borealis.Core und gibt die Version aus
                Console.WriteLine($"Borealis.Service {typeof(Program).Assembly.GetName().Version} mit Borealis.Core {typeof(Log).Assembly.GetName().Version}");
                return 0;
            case "--ramprobe":
                // Analysis: read RAM lighting over SMBus (ENE addresses 0x70–0x73 only, read-only)
                // Analyse: RAM-Beleuchtung über SMBus lesen (nur ENE-Adressen 0x70–0x73, nur lesend)
                Console.OutputEncoding = System.Text.Encoding.UTF8;
                try { return RamProbe.Run(); }
                catch (Exception ex) when (ex is IOException or TimeoutException) { Console.WriteLine("Fehler: " + ex.Message); return 1; }
            case "--fantest":
                // Write test (only with the user's consent): chassis fan 1 briefly to 100 %, watch RPM, restore and verify the original curve
                // Schreibtest (nur mit Freigabe): Gehäuselüfter 1 kurz auf 100 %, Drehzahl beobachten, Originalkurve exakt zurückschreiben und prüfen
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
