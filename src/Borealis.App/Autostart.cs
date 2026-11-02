using Microsoft.Win32;

namespace Borealis.App;

/// <summary>
/// Startet die Tray-App bei der Anmeldung (je Benutzer, <c>HKCU\…\Run</c>, keine Adminrechte nötig). Der Dienst
/// läuft davon unabhängig; die App ist nur die Oberfläche. Wie Windows-Programme üblich wird der Zustand auch unter
/// <c>Explorer\StartupApproved\Run</c> geführt: Darüber zeigen Task-Manager und Einstellungen den Eintrag an und
/// schalten ihn ab (erstes Byte gerade = an, ungerade = vom Benutzer deaktiviert).
/// </summary>
static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    const string ValueName = "Borealis";
    static readonly byte[] Enabled = [0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    static string Command => $"\"{Environment.ProcessPath}\" --minimized";

    /// <summary>Eingetragen, auf diese Borealis.exe zeigend und nicht im Task-Manager deaktiviert.</summary>
    public static bool IsEnabled
    {
        get
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKey);
            if (run?.GetValue(ValueName) is not string value || !string.Equals(value, Command, StringComparison.OrdinalIgnoreCase)) return false;
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
            return approved?.GetValue(ValueName) is not byte[] { Length: > 0 } state || (state[0] & 1) == 0;
        }
    }

    public static void Set(bool enabled)
    {
        using var run = Registry.CurrentUser.CreateSubKey(RunKey);
        using var approved = Registry.CurrentUser.CreateSubKey(ApprovedKey);
        if (enabled)
        {
            run.SetValue(ValueName, Command);
            approved.SetValue(ValueName, Enabled, RegistryValueKind.Binary);
        }
        else
        {
            run.DeleteValue(ValueName, throwOnMissingValue: false);
            approved.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
