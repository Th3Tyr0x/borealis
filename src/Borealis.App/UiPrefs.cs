using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using Borealis.Core.Localization;
using Microsoft.Win32;

namespace Borealis.App;

/// <summary>UI theme; "System" follows the Windows app setting.
/// Farbschema; „System“ folgt der Windows-Einstellung für Apps.</summary>
public enum ThemeChoice { System, Dark, Light }

/// <summary>
/// Per-user UI settings (<c>%APPDATA%\Borealis\ui.json</c>); not in the service settings, which apply to all users.
/// UI-Einstellungen je Benutzer (<c>%APPDATA%\Borealis\ui.json</c>); nicht in den Dienst-Einstellungen, die für alle gelten.
/// </summary>
public sealed record UiPrefs
{
    /// <summary>"de" or "en".
    /// „de“ oder „en“.</summary>
    public string Language { get; set; } = "de";
    public ThemeChoice Theme { get; set; } = ThemeChoice.Dark;
    /// <summary>PawnIO setup was already offered on first start.
    /// PawnIO-Einrichtung wurde beim ersten Start schon angeboten.</summary>
    public bool PawnIoAsked { get; set; }
    /// <summary>Check GitHub once a day for a newer version (check only, no download).
    /// Einmal am Tag bei GitHub nach einer neueren Version sehen (nur prüfen, kein Download).</summary>
    public bool CheckForUpdates { get; set; } = true;
    /// <summary>Last successful update check and the latest version found (e.g. "0.2.0").
    /// Letzte erfolgreiche Update-Prüfung und die gefundene neueste Version (z. B. „0.2.0“).</summary>
    public DateTime? LastUpdateCheck { get; set; }
    public string? LatestVersion { get; set; }
    /// <summary>A notification was already shown for this version.
    /// Für diese Version kam schon ein Hinweis.</summary>
    public string? NotifiedVersion { get; set; }

    static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Borealis", "ui.json");
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static UiPrefs Current { get; private set; } = Load();

    static UiPrefs Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<UiPrefs>(File.ReadAllText(FilePath), Json) ?? FirstStart() : FirstStart(); }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return FirstStart(); }
    }

    /// <summary>Nothing saved yet: language like Windows (German, else English).
    /// Noch nichts gespeichert: Sprache wie Windows (Deutsch, sonst Englisch).</summary>
    static UiPrefs FirstStart() =>
        new() { Language = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "de" : "en" };

    public static void Save(UiPrefs prefs)
    {
        Current = prefs;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(prefs, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Effective theme: for "System" the Windows app mode.
    /// Wirksames Farbschema: bei „System“ der App-Modus von Windows.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsLight => Theme switch
    {
        ThemeChoice.Light => true,
        ThemeChoice.Dark => false,
        _ => SystemUsesLightTheme(),
    };

    static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v != 0;
        }
        catch (System.Security.SecurityException) { return false; }
    }

    /// <summary>Apply language and theme to the running app (without rebuilding windows).
    /// Sprache und Farbschema auf die laufende App anwenden (ohne Fenster neu aufzubauen).</summary>
    /// <summary>Windows culture at start; kept for numbers and dates while it matches the language.
    /// Windows-Kultur beim Start; bleibt für Zahlen und Datum, solange sie zur Sprache passt.</summary>
    static readonly CultureInfo SystemCulture = CultureInfo.CurrentCulture;

    public static void Apply()
    {
        Loc.English = Current.Language == "en";
        string language = Loc.English ? "en" : "de";
        var culture = SystemCulture.TwoLetterISOLanguageName == language ? SystemCulture : CultureInfo.GetCultureInfo(Loc.English ? "en-US" : "de-DE");
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = culture;
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var theme = new ResourceDictionary { Source = new Uri(Current.IsLight ? "Themes/Light.xaml" : "Themes/Dark.xaml", UriKind.Relative) };
        if (dictionaries.Count > 0) dictionaries[0] = theme;
        else dictionaries.Insert(0, theme);
    }
}
