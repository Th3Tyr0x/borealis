using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows;
using Borealis.Core.Hardware;

namespace Borealis.App;

/// <summary>
/// Richtet den Treiber PawnIO ein, den Borealis für Lüfter, Temperaturen und RAM-Beleuchtung braucht.
/// Der Installer kommt aus dem Borealis-Ordner (falls mitgeliefert) oder von GitHub; ausgeführt wird er nur, wenn
/// seine SHA-256 mit der festen Prüfsumme übereinstimmt. Er läuft still mit Adminrechten (UAC-Abfrage).
/// </summary>
static class PawnIoSetup
{
    public static bool IsInstalled => PawnIo.InstalledVersion() != null;

    /// <summary>Beim ersten Start einmal fragen, wenn PawnIO fehlt; später geht es über die Einstellungen.</summary>
    public static async Task AskOnFirstStartAsync(Window owner)
    {
        if (IsInstalled || UiPrefs.Current.PawnIoAsked) return;
        UiPrefs.Save(UiPrefs.Current with { PawnIoAsked = true });
        var answer = MessageBox.Show(owner,
            Loc.T("Für Lüfter, Temperaturen und RAM-Beleuchtung braucht Borealis den Treiber PawnIO (pawnio.eu). " +
                  "Mainboard- und ARGB-Beleuchtung gehen auch ohne.\n\nPawnIO jetzt einrichten? Windows fragt dafür nach Administratorrechten.",
                  "Borealis needs the PawnIO driver (pawnio.eu) for fans, temperatures and memory lighting. " +
                  "Motherboard and ARGB lighting also work without it.\n\nSet up PawnIO now? Windows will ask for administrator rights."),
            Loc.T("Hardwarezugriff einrichten", "Set up hardware access"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        var (_, message) = await InstallAsync();
        MessageBox.Show(owner, message, "Borealis", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>Installer holen, prüfen und still installieren; danach den Dienst neu verbinden lassen.</summary>
    public static async Task<(bool Ok, string Message)> InstallAsync()
    {
        string setup;
        FileStream locked;
        try { (setup, locked) = await GetInstallerAsync(); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or UnauthorizedAccessException)
        {
            return (false, Loc.T($"PawnIO-Installer nicht geladen: {ex.Message}", $"PawnIO installer not downloaded: {ex.Message}"));
        }

        int exitCode;
        try
        {
            // Die geprüfte Datei bleibt bis zum Ende gegen Schreiben gesperrt
            using var process = Process.Start(new ProcessStartInfo(setup, PawnIo.SetupArguments) { UseShellExecute = true, Verb = "runas" })
                                ?? throw new IOException("Installer nicht gestartet.");
            await process.WaitForExitAsync();
            exitCode = process.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return (false, Loc.T("Abgebrochen: Ohne Administratorrechte lässt sich PawnIO nicht einrichten.",
                                 "Cancelled: PawnIO cannot be set up without administrator rights."));
        }
        catch (Exception ex) when (ex is Win32Exception or IOException)
        {
            return (false, Loc.T($"PawnIO-Installer nicht ausgeführt: {ex.Message}", $"PawnIO installer not run: {ex.Message}"));
        }
        finally { await locked.DisposeAsync(); }

        // 3010 = ERROR_SUCCESS_REBOOT_REQUIRED
        if (exitCode is not (0 or 3010) || !IsInstalled)
            return (false, Loc.T($"PawnIO wurde nicht eingerichtet (Code {exitCode}).", $"PawnIO was not set up (code {exitCode})."));
        await AppState.Instance.HardwareRetryAsync();
        return (true, exitCode == 3010
            ? Loc.T("PawnIO ist eingerichtet. Windows braucht einen Neustart, bevor Borealis darauf zugreifen kann.",
                    "PawnIO is set up. Windows needs a restart before Borealis can use it.")
            : Loc.T("PawnIO ist eingerichtet. Lüfter und Temperaturen erscheinen in wenigen Sekunden.",
                    "PawnIO is set up. Fans and temperatures appear in a few seconds."));
    }

    /// <summary>
    /// Mitgelieferten Installer nehmen oder von GitHub laden. Gibt ihn geprüft und zum Schreiben gesperrt zurück;
    /// wirft, wenn die Prüfsumme nicht stimmt.
    /// </summary>
    static async Task<(string Path, FileStream Locked)> GetInstallerAsync()
    {
        string bundled = Path.Combine(AppContext.BaseDirectory, "PawnIO_setup.exe");
        string folder = Path.Combine(Path.GetTempPath(), "Borealis", "PawnIO-" + PawnIo.SetupVersion);
        string target = Path.Combine(folder, "PawnIO_setup.exe");
        foreach (var candidate in new[] { bundled, target })
        {
            if (!File.Exists(candidate)) continue;
            var stream = OpenLocked(candidate);
            if (await Sha256Async(stream) == PawnIo.SetupSha256) return (candidate, stream);
            await stream.DisposeAsync();
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Borealis");
        byte[] data = await http.GetByteArrayAsync(PawnIo.SetupUrl);
        if (Convert.ToHexStringLower(SHA256.HashData(data)) != PawnIo.SetupSha256)
            throw new IOException(Loc.T("Prüfsumme des Installers stimmt nicht.", "The installer checksum does not match."));
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(target, data);
        var written = OpenLocked(target);
        if (await Sha256Async(written) == PawnIo.SetupSha256) return (target, written);
        await written.DisposeAsync();
        throw new IOException(Loc.T("Prüfsumme des Installers stimmt nicht.", "The installer checksum does not match."));
    }

    // Lesen erlaubt (zum Starten), Schreiben und Löschen nicht
    static FileStream OpenLocked(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    static async Task<string> Sha256Async(FileStream stream)
    {
        stream.Position = 0;
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
    }
}
