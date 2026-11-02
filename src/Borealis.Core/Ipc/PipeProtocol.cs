using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Borealis.Core.Profiles;

namespace Borealis.Core.Ipc;

/// <summary>Tray app ↔ service protocol: named pipe <c>\\.\pipe\Borealis</c>, one JSON line per request and response.
/// Protokoll Tray-App ↔ Dienst: Named Pipe <c>\\.\pipe\Borealis</c>, eine JSON-Zeile pro Anfrage und Antwort.</summary>
public static class PipeProtocol
{
    public const string PipeName = "Borealis";

    public static async Task WriteAsync<T>(Stream stream, T message, CancellationToken token)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, ProfileStore.Json).Replace("\r", "").Replace("\n", "") + "\n");
        await stream.WriteAsync(bytes, token);
        await stream.FlushAsync(token);
    }

    /// <summary>Read one line (up to \n); null when the connection ends.
    /// Liest eine Zeile (bis \n); null bei Verbindungsende.</summary>
    public static async Task<T?> ReadAsync<T>(Stream stream, CancellationToken token) where T : class
    {
        var buf = new MemoryStream();
        var one = new byte[1];
        while (true)
        {
            int n = await stream.ReadAsync(one, token);
            if (n == 0) return null;
            if (one[0] == (byte)'\n') break;
            buf.WriteByte(one[0]);
            if (buf.Length > 4 * 1024 * 1024) throw new InvalidDataException("Nachricht zu groß.");
        }
        return JsonSerializer.Deserialize<T>(buf.ToArray(), ProfileStore.Json);
    }
}

public static class Commands
{
    /// <summary>Query state, zones, profiles.
    /// Zustand, Zonen, Profile abfragen.</summary>
    public const string Status = "status";
    /// <summary>Apply a profile without saving (preview).
    /// Profil anwenden ohne zu speichern (Vorschau).</summary>
    public const string Preview = "preview";
    /// <summary>Save, activate and apply a profile (with standby effects).
    /// Profil speichern, aktivieren und anwenden (mit Standby-Effekten).</summary>
    public const string Save = "save";
    /// <summary>Activate and apply a saved profile.
    /// Gespeichertes Profil aktivieren und anwenden.</summary>
    public const string Activate = "activate";
    public const string Delete = "delete";
    /// <summary>Re-apply the saved active profile (e.g. after a discarded preview).
    /// Gespeichertes aktives Profil erneut anwenden (z. B. nach verworfener Vorschau).</summary>
    public const string Reapply = "reapply";
    public const string SetLabels = "setLabels";
    /// <summary>Rename profile <c>Name</c> to <c>NewName</c>.
    /// Profil <c>Name</c> in <c>NewName</c> umbenennen.</summary>
    public const string Rename = "rename";
    /// <summary>Set service settings (<c>Settings</c>).
    /// Dienst-Einstellungen setzen (<c>Settings</c>).</summary>
    public const string SetSettings = "setSettings";
    /// <summary>Start (<c>Name</c> = "start") or stop ("stop") the ASUS LightingService.
    /// ASUS-LightingService starten (<c>Name</c> = "start") oder anhalten ("stop").</summary>
    public const string AsusLightingService = "asusLightingService";
    /// <summary>Set the LED count of ARGB header <c>Name</c> (zone ID) to <c>Count</c>.
    /// LED-Anzahl des ARGB-Headers <c>Name</c> (Zonen-ID) auf <c>Count</c> setzen.</summary>
    public const string SetLedCount = "setLedCount";
    /// <summary>Show the count test on ARGB header <c>Name</c>; no name ends it.
    /// Zähltest auf ARGB-Header <c>Name</c> zeigen; ohne Name beenden.</summary>
    public const string LedTest = "ledTest";
    /// <summary>Calibration mode: all zones in reference color <c>Name</c> (RRGGBB, calibrated); no name ends it.
    /// Abgleich-Modus: alle Zonen in Referenzfarbe <c>Name</c> (RRGGBB, mit Abgleich); ohne Name beenden.</summary>
    public const string CalibrationPreview = "calibrationPreview";
    /// <summary>Latest fan and temperature reading (<c>Sensors</c> in the response).
    /// Letzte Messung der Lüfter und Temperaturen (<c>Sensors</c> in der Antwort).</summary>
    public const string Sensors = "sensors";
    /// <summary>Auto-tuning: <c>Name</c> = "start" (all), "only:CPUFAN,AUXFAN4", "pump" or "cancel".
    /// Auto-Tuning: <c>Name</c> = "start" (alles), "only:CPUFAN,AUXFAN4", "pump" oder "cancel".</summary>
    public const string FanTune = "fanTune";
    /// <summary>Retry hardware access (after PawnIO setup): reopen sensors, write fan curves.
    /// Hardwarezugriff neu versuchen (nach PawnIO-Einrichtung): Sensoren öffnen, Kurven schreiben.</summary>
    public const string HardwareRetry = "hardwareRetry";
}

public sealed class PipeRequest
{
    public string Command { get; set; } = Commands.Status;
    public LightingProfile? Profile { get; set; }
    public string? Name { get; set; }
    public string? NewName { get; set; }
    public int Count { get; set; }
    public Dictionary<string, string>? Labels { get; set; }
    public BorealisSettings? Settings { get; set; }
}

public sealed class PipeResponse
{
    public bool Ok { get; set; } = true;
    public string? Error { get; set; }
    public List<string> Notes { get; set; } = [];
    public StatusInfo? Status { get; set; }
    public Hardware.SensorSnapshot? Sensors { get; set; }
    /// <summary>Auto-tuning state (with every sensor poll).
    /// Stand des Auto-Tunings (mit jeder Sensorabfrage).</summary>
    public FanTuneStatus? Tune { get; set; }

    public static PipeResponse Fail(string error) => new() { Ok = false, Error = error };
}

public sealed class FanTuneStatus
{
    public bool Running { get; set; }
    /// <summary>Progress 0–100.
    /// Fortschritt 0–100.</summary>
    public int Percent { get; set; }
    public string Message { get; set; } = "";
    /// <summary>Result of the last run (error or cancel); null = OK or none yet.
    /// Ergebnis des letzten Laufs (Fehler oder Abbruch); null = OK bzw. noch keiner.</summary>
    public string? Error { get; set; }
}

public sealed class ZoneInfo
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public int LedCount { get; set; }
}

public sealed class StatusInfo
{
    public string ServiceVersion { get; set; } = "";
    public bool HardwareReady { get; set; }
    public string? HardwareError { get; set; }
    public string? RamError { get; set; }
    public List<ZoneInfo> Zones { get; set; } = [];
    public List<string> Profiles { get; set; } = [];
    public LightingProfile? ActiveProfile { get; set; }
    public Dictionary<string, string> ZoneLabels { get; set; } = new();
    public BorealisSettings Settings { get; set; } = new();
    /// <summary>Measured software effect frame rate; 0 = none running.
    /// Gemessene Bildrate der Software-Effekte; 0 = keiner läuft.</summary>
    public double EffectFps { get; set; }
    public string AsusLightingService { get; set; } = "";
    /// <summary>Reference color of the running calibration mode (RRGGBB); null = off.
    /// Referenzfarbe des laufenden Abgleich-Modus (RRGGBB); null = aus.</summary>
    public string? CalibrationColor { get; set; }
    /// <summary>ASUS fan service state ("läuft", "angehalten", …).
    /// Zustand des ASUS-Lüfterdienstes („läuft“, „angehalten“, …).</summary>
    public string AsusFanService { get; set; } = "";
    /// <summary>Last error writing fan curves; null = OK.
    /// Letzter Fehler beim Schreiben der Lüfterkurven; null = OK.</summary>
    public string? FanError { get; set; }
}

/// <summary>Client for the tray app (and tests); each request opens its own connection.
/// Client für die Tray-App (und Tests); jede Anfrage öffnet eine eigene Verbindung.</summary>
public sealed class PipeClient
{
    readonly int connectTimeoutMs;

    public PipeClient(int connectTimeoutMs = 1500) { this.connectTimeoutMs = connectTimeoutMs; }

    public async Task<PipeResponse> SendAsync(PipeRequest request, CancellationToken token = default)
    {
        using var pipe = new NamedPipeClientStream(".", PipeProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try { await pipe.ConnectAsync(connectTimeoutMs, token); }
        catch (TimeoutException) { return PipeResponse.Fail("Der Borealis-Dienst läuft nicht."); }
        catch (UnauthorizedAccessException) { return PipeResponse.Fail("Keine Berechtigung für die Verbindung zum Borealis-Dienst."); }
        await PipeProtocol.WriteAsync(pipe, request, token);
        return await PipeProtocol.ReadAsync<PipeResponse>(pipe, token) ?? PipeResponse.Fail("Der Dienst hat die Verbindung beendet.");
    }
}
