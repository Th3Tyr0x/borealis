using Microsoft.Win32;

namespace Borealis.App;

/// <summary>
/// Starts the tray app at sign-in (per user, <c>HKCU\…\Run</c>, no admin). The state is mirrored under
/// <c>Explorer\StartupApproved\Run</c> so Task Manager can show and disable it (even first byte = on).
/// Startet die Tray-App bei der Anmeldung (je Benutzer, <c>HKCU\…\Run</c>, ohne Admin). Der Zustand steht auch unter
/// <c>Explorer\StartupApproved\Run</c>, damit der Task-Manager ihn zeigt und abschaltet (erstes Byte gerade = an).
/// </summary>
static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    const string ValueName = "Borealis";
    static readonly byte[] Enabled = [0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    static string Command => $"\"{Environment.ProcessPath}\" --minimized";

    /// <summary>Registered, pointing to this Borealis.exe and not disabled in Task Manager.
    /// Eingetragen, zeigt auf diese Borealis.exe und nicht im Task-Manager deaktiviert.</summary>
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
