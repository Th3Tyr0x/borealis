using System.Text.Json;
using System.Text.Json.Serialization;

namespace Borealis.Core.Profiles;

/// <summary>Globale Einstellungen (<c>C:\ProgramData\Borealis\settings.json</c>).</summary>
public sealed class BorealisSettings
{
    public string ActiveProfile { get; set; } = "Standard";
    /// <summary>Eigene Namen für Zonen, z. B. argb1 → „AIO“.</summary>
    public Dictionary<string, string> ZoneLabels { get; set; } = new();
    /// <summary>Den ASUS-LightingService beim Dienststart anhalten, damit er nicht dazwischen streamt.</summary>
    public bool StopAsusLightingService { get; set; } = true;
    /// <summary>Profil nach dem Aufwachen aus dem Standby erneut anwenden (der RAM verliert seinen Zustand).</summary>
    public bool ReapplyOnResume { get; set; } = true;
    /// <summary>
    /// Erst wenn der Benutzer einmal ein Profil gespeichert oder aktiviert hat, wendet der Dienst beim Start etwas an.
    /// Bis dahin bleibt die bisherige Beleuchtung (Armoury Crate) unangetastet.
    /// </summary>
    public bool Configured { get; set; }
    /// <summary>
    /// ASUS-LightingService auf „Deaktiviert“ stellen, damit Armoury Crate ihn nicht neu starten kann.
    /// Aus = Starttyp „Automatisch“ wie nach der ASUS-Installation.
    /// </summary>
    public bool DisableAsusLightingService { get; set; }
    /// <summary>
    /// Aura-Leistungsmodus wie in Armoury Crate: an = Software-Effekte mit höherer Bildrate (flüssiger, mehr CPU).
    /// </summary>
    public bool PerformanceMode { get; set; } = true;
    /// <summary>Stufe 1–3 („Flüssigkeit des Lichteffekts“), nur bei eingeschaltetem Leistungsmodus.</summary>
    public int PerformanceLevel { get; set; } = 2;
    /// <summary>
    /// Wo möglich die eingebauten Hardware-Effekte nutzen (laufen ohne Borealis, aber nur Einzelfarbe und drei
    /// Geschwindigkeitsstufen). Aus = Effekte wie in Armoury Crate vom PC berechnen.
    /// </summary>
    public bool PreferHardwareEffects { get; set; }
    /// <summary>Tatsächliche LED-Anzahl je ARGB-Header (Zonen-ID → Anzahl); fehlt = Wert aus dem Controller.</summary>
    public Dictionary<string, int> LedCounts { get; set; } = new();
    /// <summary>Farbabgleich je Zone (Zonen-ID → Kanalverstärkung/Reihenfolge); gilt für alle Profile.</summary>
    public Dictionary<string, ZoneCalibration> Calibration { get; set; } = new();
    /// <summary>Lüftersteuerung (Modus, eigene Kurven, Übernahme vom ASUS-Lüfterdienst).</summary>
    public FanSettings Fans { get; set; } = new();
    /// <summary>Lüfteranschlüsse ohne Lüfter in der App trotzdem anzeigen (sonst ausgeblendet).</summary>
    public bool ShowUnconnectedFans { get; set; }

    /// <summary>Bildrate der Software-Effekte in Bildern je Sekunde.</summary>
    [JsonIgnore]
    public int FramesPerSecond => !PerformanceMode ? 8 : Math.Clamp(PerformanceLevel, 1, 3) switch { 1 => 12, 2 => 20, _ => 30 };
}

/// <summary>Liest und schreibt Einstellungen und Profile als JSON unter <c>C:\ProgramData\Borealis</c>.</summary>
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

    /// <summary>Aktives Profil; legt beim ersten Start „Standard“ an.</summary>
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
