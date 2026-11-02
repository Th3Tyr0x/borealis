using System.Text.Json;
using System.Text.Json.Serialization;

namespace Borealis.Core.Profiles;

/// <summary>Global settings (<c>C:\ProgramData\Borealis\settings.json</c>).
/// Globale Einstellungen (<c>C:\ProgramData\Borealis\settings.json</c>).</summary>
public sealed class BorealisSettings
{
    public string ActiveProfile { get; set; } = "Standard";
    /// <summary>Custom zone names, e.g. argb1 → "AIO".
    /// Eigene Zonennamen, z. B. argb1 → „AIO“.</summary>
    public Dictionary<string, string> ZoneLabels { get; set; } = new();
    /// <summary>Stop the ASUS LightingService at service start so it doesn't stream in between.
    /// ASUS-LightingService beim Dienststart anhalten, damit er nicht dazwischen streamt.</summary>
    public bool StopAsusLightingService { get; set; } = true;
    /// <summary>Reapply the profile after waking from standby (RAM loses its state).
    /// Profil nach dem Aufwachen erneut anwenden (der RAM verliert seinen Zustand).</summary>
    public bool ReapplyOnResume { get; set; } = true;
    /// <summary>The service applies nothing at start until the user has saved or activated a profile once; existing lighting stays untouched.
    /// Erst wenn der Benutzer einmal ein Profil gespeichert oder aktiviert hat, wendet der Dienst beim Start etwas an; bis dahin bleibt die Beleuchtung unberührt.</summary>
    public bool Configured { get; set; }
    /// <summary>Set the ASUS LightingService to "Disabled" so it can't be restarted; off = start type "Automatic".
    /// ASUS-LightingService auf „Deaktiviert“ stellen, damit er nicht neu startet; aus = Starttyp „Automatisch“.</summary>
    public bool DisableAsusLightingService { get; set; }
    /// <summary>Performance mode: on = software effects at a higher frame rate (smoother, more CPU).
    /// Leistungsmodus: an = Software-Effekte mit höherer Bildrate (flüssiger, mehr CPU).</summary>
    public bool PerformanceMode { get; set; } = true;
    /// <summary>Level 1–3 (smoothness), only with performance mode on.
    /// Stufe 1–3 (Flüssigkeit), nur bei eingeschaltetem Leistungsmodus.</summary>
    public int PerformanceLevel { get; set; } = 2;
    /// <summary>Use built-in hardware effects where possible (run without Borealis, but single color and three speeds); off = computed by the PC.
    /// Wo möglich eingebaute Hardware-Effekte nutzen (laufen ohne Borealis, aber nur Einzelfarbe und drei Tempi); aus = vom PC berechnet.</summary>
    public bool PreferHardwareEffects { get; set; }
    /// <summary>Real LED count per ARGB header (zone ID → count); missing = controller value.
    /// Tatsächliche LED-Anzahl je ARGB-Header (Zonen-ID → Anzahl); fehlt = Wert aus dem Controller.</summary>
    public Dictionary<string, int> LedCounts { get; set; } = new();
    /// <summary>Calibration per zone (zone ID → channel gain/order); applies to all profiles.
    /// Farbabgleich je Zone (Zonen-ID → Verstärkung/Reihenfolge); gilt für alle Profile.</summary>
    public Dictionary<string, ZoneCalibration> Calibration { get; set; } = new();
    /// <summary>Fan control (mode, custom curves, takeover from the ASUS fan service).
    /// Lüftersteuerung (Modus, eigene Kurven, Übernahme vom ASUS-Lüfterdienst).</summary>
    public FanSettings Fans { get; set; } = new();
    /// <summary>Show fan headers without a fan in the app anyway (hidden otherwise).
    /// Lüfteranschlüsse ohne Lüfter trotzdem anzeigen (sonst ausgeblendet).</summary>
    public bool ShowUnconnectedFans { get; set; }

    /// <summary>Software effect frame rate in frames per second.
    /// Bildrate der Software-Effekte in Bildern je Sekunde.</summary>
    [JsonIgnore]
    public int FramesPerSecond => !PerformanceMode ? 8 : Math.Clamp(PerformanceLevel, 1, 3) switch { 1 => 12, 2 => 20, _ => 30 };
}

/// <summary>Reads and writes settings and profiles as JSON in <c>C:\ProgramData\Borealis</c>.
/// Liest und schreibt Einstellungen und Profile als JSON in <c>C:\ProgramData\Borealis</c>.</summary>
public sealed class ProfileStore
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public string Root { get; }
    string ProfileDir => Path.Combine(Root, "profiles");
    string SettingsPath => Path.Combine(Root, "settings.json");

    public ProfileStore(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Borealis");
    }

    public BorealisSettings LoadSettings() => Read<BorealisSettings>(SettingsPath) ?? new BorealisSettings();

    public void SaveSettings(BorealisSettings settings) => WriteAtomic(SettingsPath, settings);

    public IReadOnlyList<string> ListProfiles()
    {
        if (!Directory.Exists(ProfileDir)) return [];
        return Directory.EnumerateFiles(ProfileDir, "*.json")
            .Select(f => Read<LightingProfile>(f)?.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public LightingProfile? LoadProfile(string name) => Read<LightingProfile>(PathFor(name));

    public void SaveProfile(LightingProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name)) throw new ArgumentException("Profilname fehlt.");
        WriteAtomic(PathFor(profile.Name), profile);
    }

    public bool DeleteProfile(string name)
    {
        var path = PathFor(name);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    /// <summary>Active profile; creates "Standard" on first start.
    /// Aktives Profil; legt beim ersten Start „Standard“ an.</summary>
    public LightingProfile LoadActiveOrDefault(BorealisSettings settings)
    {
        var p = LoadProfile(settings.ActiveProfile);
        if (p != null) return p;
        p = LightingProfile.CreateDefault(settings.ActiveProfile);
        SaveProfile(p);
        return p;
    }

    string PathFor(string name)
    {
        var safe = string.Concat(name.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(ProfileDir, safe + ".json");
    }

    static T? Read<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json); }
        catch (JsonException) { return null; }
    }

    static void WriteAtomic<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json));
        File.Move(tmp, path, overwrite: true);
    }
}
