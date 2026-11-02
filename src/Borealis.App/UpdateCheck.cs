using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace Borealis.App;

/// <summary>
/// Checks the GitHub API for a newer release. Read-only: nothing is downloaded; at most once a day, can be turned off.
/// Prüft über die GitHub-API auf ein neueres Release. Nur lesen, kein Download; höchstens einmal am Tag, abschaltbar.
/// </summary>
static class UpdateCheck
{
    const string LatestApi = "https://api.github.com/repos/Th3Tyr0x/borealis/releases/latest";
    public const string ReleasesPage = "https://github.com/Th3Tyr0x/borealis/releases/latest";
    static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    /// <summary>Version of this app (Major.Minor.Build).
    /// Version dieser App (Major.Minor.Build).</summary>
    public static Version Current
    {
        get
        {
            var v = typeof(UpdateCheck).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
        }
    }

    /// <summary>Newer version from the last check; null = up to date or not checked.
    /// Neuere Version laut letzter Prüfung; null = aktuell oder ungeprüft.</summary>
    public static Version? Available =>
        TryParse(UiPrefs.Current.LatestVersion) is { } latest && latest > Current ? latest : null;

    /// <summary>
    /// Ask GitHub (without <paramref name="force"/> only if the last check is over a day old) and store the result.
    /// Returns the newer version or null; network errors count as "nothing new".
    /// Bei GitHub nachsehen (ohne <paramref name="force"/> nur, wenn die letzte Prüfung über einen Tag her ist) und merken.
    /// Liefert die neuere Version oder null; Netzfehler zählen als „nichts Neues“.
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

    /// <summary>Open the release page (fixed URL, not from the API response).
    /// Release-Seite öffnen (feste Adresse, nicht aus der API-Antwort).</summary>
    public static void OpenReleasePage() => Process.Start(new ProcessStartInfo(ReleasesPage) { UseShellExecute = true });

    /// <summary>"v0.2.0" or "0.2.0" → version (Major.Minor.Build).
    /// „v0.2.0“ oder „0.2.0“ → Version (Major.Minor.Build).</summary>
    static Version? TryParse(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || !Version.TryParse(tag.Trim().TrimStart('v', 'V'), out var v)) return null;
        return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
    }
}
