using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace Borealis.App;

/// <summary>
/// Prüft über die GitHub-API, ob es ein neueres Release gibt. Nur lesen: Es wird nichts heruntergeladen oder installiert,
/// der Hinweis führt auf die Release-Seite. Höchstens einmal am Tag automatisch, abschaltbar unter Einstellungen → Info.
/// </summary>
static class UpdateCheck
{
    const string LatestApi = "https://api.github.com/repos/Th3Tyr0x/borealis/releases/latest";
    public const string ReleasesPage = "https://github.com/Th3Tyr0x/borealis/releases/latest";
    static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    /// <summary>Version dieser App (Major.Minor.Build).</summary>
    public static Version Current
    {
        get
        {
            var v = typeof(UpdateCheck).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }
    }

    /// <summary>Neuere Version laut letzter Prüfung; null = aktuell oder noch nicht geprüft.</summary>
    public static Version? Available =>
        TryParse(UiPrefs.Current.LatestVersion) is { } latest && latest > Current ? latest : null;

    /// <summary>
    /// Bei GitHub nachsehen (ohne <paramref name="force"/> nur, wenn die letzte Prüfung über einen Tag her ist) und das
    /// Ergebnis in den UI-Einstellungen merken. Liefert die neuere Version oder null; Netzfehler zählen als „nichts Neues“.
    /// </summary>
    public static async Task<Version?> CheckAsync(bool force)
    {
        var prefs = UiPrefs.Current;
        if (!force && (!prefs.CheckForUpdates || prefs.LastUpdateCheck is { } last && DateTime.UtcNow - last < Interval))
            return Available;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"Borealis/{Current}");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using var doc = JsonDocument.Parse(await http.GetStringAsync(LatestApi));
            string? tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            if (TryParse(tag) is not { } latest) return Available;
            UiPrefs.Save(UiPrefs.Current with { LastUpdateCheck = DateTime.UtcNow, LatestVersion = latest.ToString() });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException) { }
        return Available;
    }

    /// <summary>Release-Seite im Browser öffnen (feste Adresse, nicht aus der API-Antwort).</summary>
    public static void OpenReleasePage() => Process.Start(new ProcessStartInfo(ReleasesPage) { UseShellExecute = true });

    /// <summary>„v0.2.0“ oder „0.2.0“ → Version (Major.Minor.Build).</summary>
    static Version? TryParse(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || !Version.TryParse(tag.Trim().TrimStart('v', 'V'), out var v)) return null;
        return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
    }
}
