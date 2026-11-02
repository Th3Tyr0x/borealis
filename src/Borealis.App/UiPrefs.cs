using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using Borealis.Core.Localization;
using Microsoft.Win32;

namespace Borealis.App;

/// <summary>Farbschema der Oberfläche; „System“ folgt der Windows-Einstellung für Apps.</summary>
public enum ThemeChoice { System, Dark, Light }

/// <summary>
/// Einstellungen der Oberfläche je Benutzer (<c>%APPDATA%\Borealis\ui.json</c>): Sprache und Farbschema.
/// Sie gehören nicht in die Dienst-Einstellungen, weil der Dienst für alle Benutzer läuft.
/// </summary>
public sealed record UiPrefs
{
    /// <summary>„de“ oder „en“.</summary>
    public string Language { get; set; } = "de";
    public ThemeChoice Theme { get; set; } = ThemeChoice.Dark;
    /// <summary>Beim ersten Start wurde schon gefragt, ob PawnIO eingerichtet werden soll.</summary>
    public bool PawnIoAsked { get; set; }

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

    /// <summary>Noch nichts gespeichert: Sprache wie Windows (Deutsch, sonst Englisch).</summary>
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

    /// <summary>Wirksames Farbschema: bei „System“ die Windows-Einstellung „App-Modus“.</summary>
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

    /// <summary>Sprache und Farbschema auf die laufende App anwenden (ohne Fenster neu aufzubauen).</summary>
    /// <summary>Kultur von Windows beim Start; bleibt für Zahlen und Datum, solange sie zur gewählten Sprache passt.</summary>
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
