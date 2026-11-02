using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Borealis.Core.Profiles;

namespace Borealis.Core.Ipc;

/// <summary>
/// Protokoll zwischen Tray-App und Dienst: Named Pipe <c>\\.\pipe\Borealis</c>, eine JSON-Zeile pro Anfrage und Antwort.
/// </summary>
public static class PipeProtocol
{
    public const string PipeName = "Borealis";

    public static async Task WriteAsync<T>(Stream stream, T message, CancellationToken token)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, ProfileStore.Json).Replace("\r", "").Replace("\n", "") + "\n");
        await stream.WriteAsync(bytes, token);
        await stream.FlushAsync(token);
    }

    /// <summary>Liest eine Zeile (bis \n); null bei Verbindungsende.</summary>
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
    /// <summary>Zustand, Zonen, Profile abfragen.</summary>
    public const string Status = "status";
    /// <summary>Profil anwenden ohne zu speichern (Vorschau).</summary>
    public const string Preview = "preview";
    /// <summary>Profil speichern, aktiv setzen und anwenden (mit Standby-Effekten).</summary>
    public const string Save = "save";
    /// <summary>Gespeichertes Profil aktiv setzen und anwenden.</summary>
    public const string Activate = "activate";
    public const string Delete = "delete";
    /// <summary>Gespeichertes aktives Profil erneut anwenden (z. B. nach verworfener Vorschau).</summary>
    public const string Reapply = "reapply";
    public const string SetLabels = "setLabels";
    /// <summary>Profil <c>Name</c> in <c>NewName</c> umbenennen.</summary>
    public const string Rename = "rename";
    /// <summary>Dienst-Einstellungen setzen (<c>Settings</c>).</summary>
    public const string SetSettings = "setSettings";
    /// <summary>ASUS-LightingService starten (<c>Name</c> = "start") oder anhalten ("stop").</summary>
    public const string AsusLightingService = "asusLightingService";
    /// <summary>LED-Anzahl des ARGB-Headers <c>Name</c> (Zonen-ID) im Controller auf <c>Count</c> setzen.</summary>
    public const string SetLedCount = "setLedCount";
    /// <summary>Zähltest auf dem ARGB-Header <c>Name</c> zeigen; ohne Name beenden (Profil wieder anwenden).</summary>
    public const string LedTest = "ledTest";
    /// <summary>
    /// Abgleich-Modus: alle Zonen in der Referenzfarbe <c>Name</c> (RRGGBB, mit Farbabgleich) zeigen;
    /// ohne Name beenden (Profil wieder anwenden).
    /// </summary>
    public const string CalibrationPreview = "calibrationPreview";
    /// <summary>Letzte Messung der Lüfter und Temperaturen (<c>Sensors</c> in der Antwort).</summary>
    public const string Sensors = "sensors";
    /// <summary>Auto-Tuning: <c>Name</c> = "start" (alles), "only:CPUFAN,AUXFAN4" (nur diese), "pump" (nur Pumpe) oder "cancel".</summary>
    public const string FanTune = "fanTune";
    /// <summary>Hardwarezugriff neu versuchen (nach der Einrichtung von PawnIO): Sensoren neu öffnen, Lüfterkurven schreiben.</summary>
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
    /// <summary>Stand des Auto-Tunings (mit jeder Sensorabfrage).</summary>
    public FanTuneStatus? Tune { get; set; }

    public static PipeResponse Fail(string error) => new() { Ok = false, Error = error };
}

public sealed class FanTuneStatus
{
    public bool Running { get; set; }
    /// <summary>Fortschritt 0–100.</summary>
    public int Percent { get; set; }
    public string Message { get; set; } = "";
    /// <summary>Ergebnis des letzten Laufs (Fehler oder Abbruch); null = in Ordnung bzw. noch keiner.</summary>
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
    /// <summary>Gemessene Bildrate der Software-Effekte; 0 = es läuft kein Software-Effekt.</summary>
    public double EffectFps { get; set; }
    public string AsusLightingService { get; set; } = "";
    /// <summary>Referenzfarbe des laufenden Abgleich-Modus (RRGGBB); null = aus.</summary>
    public string? CalibrationColor { get; set; }
    /// <summary>Zustand des ASUS-Lüfterdienstes („läuft“, „angehalten“, …).</summary>
    public string AsusFanService { get; set; } = "";
    /// <summary>Letzter Fehler beim Schreiben der Lüfterkurven; null = in Ordnung.</summary>
    public string? FanError { get; set; }
}

/// <summary>Client für die Tray-App (und Tests). Jede Anfrage öffnet eine eigene Verbindung.</summary>
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
