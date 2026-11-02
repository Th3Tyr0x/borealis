// borealis – command-line tool for testing the lighting core. Runs as admin (manifest) and MTA (needed by the ENE RAM HAL).
// borealis – Kommandozeilenwerkzeug zum Testen des Licht-Kerns. Läuft als Admin (Manifest) und im MTA (für die ENE-RAM-HAL).
using System.Globalization;
using Borealis.Core.Lighting;
using Borealis.Core.Windows;

namespace Borealis.Cli;

static class Program
{
    const string Usage = """
        borealis – Beleuchtung ohne Armoury Crate

          borealis info
              Controller, Zonen, gespeicherte Effekte, RAM und LightingService anzeigen (nur lesend).
          borealis color <zone>=<RRGGBB> …
              Feste Farben als Firmware-Effekt „Statisch“ (im Controller gespeichert, läuft ohne Software).
          borealis effect <effekt> <zone>[=<RRGGBB>] … [--speed N] [--dir N]
              Firmware-Effekt setzen und speichern. Effekte: off, static, breathing, strobing, colorcycle,
              rainbow, comet, flashanddash, wave, glowingyoyo, starrynight.
          borealis direct <zone>=<RRGGBB> …
              Farben im Direct-Modus (nicht gespeichert, nach Neustart gilt wieder der Firmware-Effekt).
          borealis standby
          borealis standby <effekt> <zone>[=<RRGGBB>] … [--speed N] [--dir N]
              Effekt nach dem Herunterfahren (S3/S5) anzeigen bzw. setzen. Nur Mainboard und ARGB.
          borealis ls status|stop|start|disable|enable
              ASUS-LightingService steuern (er überschreibt sonst die Beleuchtung).
          borealis service
              Laufenden Borealis-Dienst abfragen (Named Pipe, nur lesend).

        Zonen: mb1 mb2 mb3 argb1 argb2 argb3 ram1 … ram4, Gruppen: mb argb ram usb all.
        Spätere Angaben überschreiben frühere, z. B. „all=FFFFFF argb1=FF0000“.
        Optionen für Schreibbefehle: --ls-stop (LightingService vorher anhalten), --no-ram.
        """;

    static int Main(string[] args)
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help" or "hilfe")
        {
            Console.WriteLine(Usage);
            return 0;
        }
        try
        {
            var opts = Options.Parse(args.Skip(1));
            return args[0].ToLowerInvariant() switch
            {
                "info" => Info(),
                "color" or "farbe" => Write(opts, (e, z) => e.ApplyEffect(z, FirmwareEffect.Static), needsEffect: false),
                "effect" or "effekt" => Write(opts, (e, z) => e.ApplyEffect(z, opts.Effect!.Value, opts.Speed, opts.Direction), needsEffect: true),
                "direct" => Write(opts, (e, z) => e.ApplyDirect(z), needsEffect: false),
                "standby" => opts.Positional.Count == 0 ? StandbyInfo()
                    : Write(opts, (e, z) => e.ApplyStandby(z, opts.Effect!.Value, opts.Speed, opts.Direction), needsEffect: true, withRam: false),
                "ls" => LightingServiceCommand(opts.Positional.FirstOrDefault() ?? "status"),
                "service" or "dienst" => BorealisServiceStatus(),
                _ => Fail($"Unbekannter Befehl „{args[0]}“. borealis --help zeigt die Befehle."),
            };
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException or IOException
                                       or TimeoutException or System.Runtime.InteropServices.ExternalException)
        {
            return Fail(ex.Message);
        }
    }

    static int Fail(string message)
    {
        Console.Error.WriteLine("Fehler: " + message);
        return 1;
    }

    static int Info()
    {
        Console.WriteLine($"LightingService: {DescribeLs()}");
        using var engine = LightingEngine.Open();
        Console.WriteLine($"Controller: {engine.Usb.DevicePath}");
        var cfg = engine.Usb.ReadConfig();
        if (cfg != null) Console.WriteLine($"Konfiguration (EC B0): {Hex(cfg, 36)}");

        Console.WriteLine();
        Console.WriteLine("Zonen:");
        foreach (var z in engine.Zones)
        {
            string where = z.IsUsb ? $"Unit {z.Unit}, Steuerkanal {z.ControlChannel}, Direct-Kanal {z.DirectChannel}" : $"ENE-Modul {z.Unit}";
            Console.WriteLine($"  {z.Id,-6} {z.Name,-28} {z.LedCount,4} LEDs  ({where})");
        }
        if (engine.Ram == null) Console.WriteLine($"  RAM nicht verfügbar: {engine.RamError}");

        Console.WriteLine();
        Console.WriteLine("Steuerkanäle (gespeicherter Zustand im Controller):");
        foreach (var c in engine.Usb.ReadChannels())
        {
            var zones = engine.Zones.Where(z => z.IsUsb && c.HasUnit(z.Unit)).Select(z => z.Id);
            Console.WriteLine($"  Kanal {c.Channel} [{string.Join(", ", zones)}]: Betrieb {FirmwareEffects.Describe(c.Effect)} " +
                              $"(Richtung {c.Direction}, Speed {c.Speed}), Standby {FirmwareEffects.Describe(c.StandbyEffect)} " +
                              $"(Richtung {c.StandbyDirection}, Speed {c.StandbySpeed})");
        }
        return 0;
    }

    static int StandbyInfo()
    {
        using var engine = LightingEngine.Open(withRam: false);
        foreach (var c in engine.Usb.ReadChannels())
        {
            var zones = engine.Zones.Where(z => c.HasUnit(z.Unit)).Select(z => z.Id);
            Console.WriteLine($"Kanal {c.Channel} [{string.Join(", ", zones)}]: Standby-Effekt {FirmwareEffects.Describe(c.StandbyEffect)} " +
                              $"(Richtung {c.StandbyDirection}, Speed {c.StandbySpeed})");
        }
        Console.WriteLine("Hinweis: Sichtbar nur, wenn im BIOS die Beleuchtung im Ruhezustand/Soft-Off erlaubt ist.");
        return 0;
    }

    static int Write(Options opts, Func<LightingEngine, IReadOnlyDictionary<Zone, Rgb>, ApplyResult> apply,
        bool needsEffect, bool withRam = true)
    {
        var positional = new List<string>(opts.Positional);
        if (needsEffect)
        {
            if (positional.Count == 0 || !FirmwareEffects.TryParse(positional[0], out var eff))
                return Fail($"Effekt fehlt oder unbekannt. Bekannt: {string.Join(", ", FirmwareEffects.KnownNames)}.");
            opts.Effect = eff;
            positional.RemoveAt(0);
        }
        if (positional.Count == 0) return Fail("Keine Zone angegeben (z. B. argb1=FF0000).");

        var ls = ServiceControl.GetState(ServiceControl.AsusLightingService);
        if (ls is ServiceState.Running or ServiceState.StartPending)
        {
            if (!opts.StopLightingService)
                return Fail("Der ASUS-LightingService läuft und würde die Einstellung gleich überschreiben. " +
                            "Mit --ls-stop anhalten oder vorher „borealis ls stop“.");
            Console.WriteLine("LightingService wird angehalten …");
            ServiceControl.Stop(ServiceControl.AsusLightingService);
        }

        using var engine = LightingEngine.Open(withRam && !opts.NoRam);
        var colors = new Dictionary<Zone, Rgb>();
        foreach (var arg in positional)
        {
            var parts = arg.Split('=', 2);
            var color = parts.Length == 2 ? Rgb.Parse(parts[1]) : Rgb.Black;
            foreach (var z in engine.Resolve(parts[0]))
            {
                if (opts.NoRam && z.Kind == ZoneKind.Ram) continue;
                colors[z] = color;
            }
        }
        var result = apply(engine, colors);
        bool showColor = opts.Effect is not { } e || FirmwareEffects.UsesColor(e);
        foreach (var z in colors.Keys.OrderBy(z => z.Kind).ThenBy(z => z.Id))
            Console.WriteLine($"  {z.Id,-6} {z.Name,-28} {(showColor ? "#" + colors[z] : opts.Effect.ToString())}");
        foreach (var n in result.Notes) Console.WriteLine("Hinweis: " + n);
        Console.WriteLine("Fertig.");
        return 0;
    }

    static int LightingServiceCommand(string action)
    {
        const string name = ServiceControl.AsusLightingService;
        switch (action.ToLowerInvariant())
        {
            case "status": break;
            case "stop": ServiceControl.Stop(name); break;
            case "start": ServiceControl.Start(name); break;
            case "disable": ServiceControl.Stop(name); ServiceControl.SetStartType(name, ServiceStartType.Disabled); break;
            case "enable": ServiceControl.SetStartType(name, ServiceStartType.Automatic); ServiceControl.Start(name); break;
            default: return Fail($"Unbekannte Aktion „{action}“ (status, stop, start, disable, enable).");
        }
        Console.WriteLine($"LightingService: {DescribeLs()}");
        return 0;
    }

    // Queries the running service over the named pipe (read-only)
    // Fragt den laufenden Dienst über die Named Pipe ab (nur lesend)
    static int BorealisServiceStatus()
    {
        var r = new Borealis.Core.Ipc.PipeClient().SendAsync(new Borealis.Core.Ipc.PipeRequest()).GetAwaiter().GetResult();
        if (!r.Ok || r.Status is not { } s) return Fail(r.Error ?? "Keine Antwort.");
        Console.WriteLine($"Borealis-Dienst v{s.ServiceVersion}, Hardware {(s.HardwareReady ? "bereit" : "nicht bereit: " + s.HardwareError)}");
        Console.WriteLine($"Zonen: {string.Join(", ", s.Zones.Select(z => z.Id))}");
        Console.WriteLine($"Profile: {string.Join(", ", s.Profiles)}; aktiv: {s.ActiveProfile?.Name}");
        Console.WriteLine($"ASUS-LightingService: {s.AsusLightingService}");
        return 0;
    }

    static string DescribeLs()
    {
        var state = ServiceControl.GetState(ServiceControl.AsusLightingService);
        if (state == ServiceState.NotInstalled) return "nicht installiert";
        return $"{state}, Starttyp {ServiceControl.GetStartType(ServiceControl.AsusLightingService)}";
    }

    static string Hex(byte[] data, int count) => string.Join(" ", data.Take(count).Select(b => b.ToString("X2")));

    sealed class Options
    {
        public List<string> Positional { get; } = [];
        public FirmwareEffect? Effect { get; set; }
        public byte Speed { get; private set; }
        public byte Direction { get; private set; }
        public bool StopLightingService { get; private set; }
        public bool NoRam { get; private set; }

        public static Options Parse(IEnumerable<string> args)
        {
            var o = new Options();
            using var it = args.GetEnumerator();
            while (it.MoveNext())
            {
                string a = it.Current;
                switch (a.ToLowerInvariant())
                {
                    case "--speed": o.Speed = NextByte(it, a); break;
                    case "--dir": o.Direction = NextByte(it, a); break;
                    case "--ls-stop": o.StopLightingService = true; break;
                    case "--no-ram": o.NoRam = true; break;
                    default:
                        if (a.StartsWith("--")) throw new ArgumentException($"Unbekannte Option „{a}“.");
                        o.Positional.Add(a);
                        break;
                }
            }
            return o;
        }

        static byte NextByte(IEnumerator<string> it, string name)
        {
            if (!it.MoveNext() || !byte.TryParse(it.Current, out var v)) throw new ArgumentException($"{name} braucht eine Zahl 0–255.");
            return v;
        }
    }
}
