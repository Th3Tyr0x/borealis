using System.Windows.Threading;
using Borealis.Core.Ipc;
using Borealis.Core.Profiles;

namespace Borealis.App;

/// <summary>
/// Shared UI state and service connection. Every change is saved and applied after a short delay (also while dragging).
/// Gemeinsamer UI-Zustand und Dienstverbindung. Jede Änderung wird kurz verzögert gespeichert und angewendet (auch beim Ziehen).
/// </summary>
sealed class AppState
{
    public static AppState Instance { get; } = new();

    readonly PipeClient client = new();
    readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    bool saving, saveAgain;

    public StatusInfo? Status { get; private set; }
    public LightingProfile Profile { get; private set; } = LightingProfile.CreateDefault();
    public Dictionary<string, string> Labels { get; private set; } = new();
    public string? LastError { get; private set; }
    public IReadOnlyList<string> LastNotes { get; private set; } = [];
    /// <summary>An unsaved change waits for the timer or is being sent.
    /// Ungespeicherte Änderung wartet auf den Timer oder wird gesendet.</summary>
    public bool Pending => saveTimer.IsEnabled || saving;

    /// <summary>Profile or status reloaded from the service (pages rebuild).
    /// Profil oder Status neu vom Dienst geladen (Seiten bauen sich neu auf).</summary>
    public event Action? Reloaded;
    /// <summary>Connection or save state changed (status line).
    /// Verbindungs- oder Speicherzustand geändert (Statuszeile).</summary>
    public event Action? StateChanged;

    AppState()
    {
        saveTimer.Tick += async (_, _) => { saveTimer.Stop(); await SaveNowAsync(); };
        calibrationTimer.Tick += async (_, _) => { calibrationTimer.Stop(); await SendCalibrationAsync(); };
    }

    /// <summary>Custom zone name or the translated service name.
    /// Eigener Zonenname oder der übersetzte Name vom Dienst.</summary>
    public string LabelOf(ZoneInfo z) => Labels.TryGetValue(z.Id, out var l) && !string.IsNullOrWhiteSpace(l) ? l : Loc.Term(z.Name);

    public IEnumerable<ZoneInfo> ZonesOf(string kind) => Status?.Zones.Where(z => z.Kind == kind) ?? [];

    /// <summary>Call after changing <see cref="Profile"/> or <see cref="Labels"/>.
    /// Nach Änderungen an <see cref="Profile"/> oder <see cref="Labels"/> aufrufen.</summary>
    public void Edited()
    {
        saveTimer.Stop();
        saveTimer.Start();
        StateChanged?.Invoke();
    }

    async Task SaveNowAsync()
    {
        if (saving) { saveAgain = true; return; }
        saving = true;
        StateChanged?.Invoke();
        try
        {
            do
            {
                saveAgain = false;
                await SendAsync(new PipeRequest { Command = Commands.Save, Profile = Profile.Clone(), Labels = new(Labels) }, reload: false);
            } while (saveAgain);
        }
        finally
        {
            saving = false;
            StateChanged?.Invoke();
        }
    }

    public async Task RefreshAsync(bool reloadProfile = true)
    {
        if (Pending) reloadProfile = false;
        await SendAsync(new PipeRequest { Command = Commands.Status }, reloadProfile);
    }

    public Task ActivateAsync(string name) => SendAsync(new PipeRequest { Command = Commands.Activate, Name = name }, reload: true);

    public async Task CreateAsync(string name)
    {
        var p = Profile.Clone();
        p.Name = name.Trim();
        await SendAsync(new PipeRequest { Command = Commands.Save, Profile = p, Labels = new(Labels) }, reload: true);
    }

    /// <summary>Take over an imported profile: save, activate, apply.
    /// Importiertes Profil übernehmen: speichern, aktivieren, anwenden.</summary>
    public Task ImportAsync(LightingProfile profile) =>
        SendAsync(new PipeRequest { Command = Commands.Save, Profile = profile.Clone(), Labels = new(Labels) }, reload: true);

    /// <summary>Reset the active profile to <see cref="LightingProfile.CreateDefault"/> (name stays).
    /// Aktives Profil auf <see cref="LightingProfile.CreateDefault"/> zurücksetzen (Name bleibt).</summary>
    public Task ResetActiveProfileAsync() =>
        SendAsync(new PipeRequest { Command = Commands.Save, Profile = LightingProfile.CreateDefault(Profile.Name), Labels = new(Labels) }, reload: true);

    public Task RenameAsync(string name, string newName) =>
        SendAsync(new PipeRequest { Command = Commands.Rename, Name = name, NewName = newName }, reload: true);

    public Task DeleteAsync(string name) => SendAsync(new PipeRequest { Command = Commands.Delete, Name = name }, reload: true);

    public Task SetSettingsAsync(BorealisSettings s)
    {
        // Don't overwrite a running calibration with the older status
        // Laufenden Farbabgleich nicht mit dem älteren Status überschreiben
        if (lastCalibration != null) s.Calibration = Clone(lastCalibration);
        return SendAsync(new PipeRequest { Command = Commands.SetSettings, Settings = s }, reload: false);
    }

    // Calibration: send only the latest state once the previous request is done (slider drag)
    // Farbabgleich: nur den letzten Stand senden, sobald die vorige Anfrage fertig ist (Regler ziehen)
    readonly DispatcherTimer calibrationTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    Dictionary<string, ZoneCalibration>? lastCalibration, pendingCalibration;
    (bool Set, Core.Lighting.Rgb? Color) pendingPreview;
    bool calibrationBusy;

    /// <summary>Current calibration (even if not yet at the service).
    /// Aktueller Farbabgleich (auch wenn noch nicht beim Dienst).</summary>
    public IReadOnlyDictionary<string, ZoneCalibration> Calibration =>
        lastCalibration ?? Status?.Settings.Calibration ?? new Dictionary<string, ZoneCalibration>();

    /// <summary>Change calibration; sent after a short delay, applied at once.
    /// Farbabgleich ändern; kurz verzögert gesendet, sofort angewendet.</summary>
    public void SetCalibration(IReadOnlyDictionary<string, ZoneCalibration> calibration)
    {
        lastCalibration = Clone(calibration);
        pendingCalibration = Clone(calibration);
        calibrationTimer.Stop();
        calibrationTimer.Start();
    }

    /// <summary>Calibration mode: reference color on all devices; <c>null</c> ends it.
    /// Abgleich-Modus: Referenzfarbe auf allen Geräten; <c>null</c> beendet ihn.</summary>
    public void PreviewCalibration(Core.Lighting.Rgb? color)
    {
        pendingPreview = (true, color);
        calibrationTimer.Stop();
        calibrationTimer.Start();
    }

    async Task SendCalibrationAsync()
    {
        if (calibrationBusy) return;
        calibrationBusy = true;
        try
        {
            // Calibration before preview, so ending calibration mode applies the latest state
            // Erst Abgleich, dann Vorschau, damit das Beenden des Abgleich-Modus den letzten Stand anwendet
            while (pendingCalibration != null || pendingPreview.Set)
            {
                if (pendingCalibration is { } c)
                {
                    pendingCalibration = null;
                    await UpdateSettingsAsync(s => s.Calibration = c);
                }
                if (pendingPreview.Set)
                {
                    var color = pendingPreview.Color;
                    pendingPreview = default;
                    await SendAsync(new PipeRequest { Command = Commands.CalibrationPreview, Name = color?.ToString() }, reload: false);
                }
            }
        }
        finally { calibrationBusy = false; }
    }

    static Dictionary<string, ZoneCalibration> Clone(IReadOnlyDictionary<string, ZoneCalibration> c) =>
        c.ToDictionary(k => k.Key, k => k.Value.Clone());

    /// <summary>Change a copy of the service settings and send it.
    /// Kopie der Dienst-Einstellungen ändern und senden.</summary>
    public Task UpdateSettingsAsync(Action<BorealisSettings> change)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(Status?.Settings ?? new BorealisSettings(), ProfileStore.Json);
        var copy = System.Text.Json.JsonSerializer.Deserialize<BorealisSettings>(json, ProfileStore.Json)!;
        change(copy);
        return SetSettingsAsync(copy);
    }

    /// <summary>Set the LED count of an ARGB header in the controller.
    /// LED-Anzahl eines ARGB-Headers im Controller setzen.</summary>
    public Task SetLedCountAsync(string zoneId, int count) =>
        SendAsync(new PipeRequest { Command = Commands.SetLedCount, Name = zoneId, Count = count }, reload: false);

    /// <summary>Show the count test on an ARGB header; <c>null</c> ends it.
    /// Zähltest auf einem ARGB-Header zeigen; <c>null</c> beendet ihn.</summary>
    public Task LedTestAsync(string? zoneId) =>
        SendAsync(new PipeRequest { Command = Commands.LedTest, Name = zoneId }, reload: false);

    /// <summary>Latest fan/temperature reading and auto-tuning state; null if the service is unreachable.
    /// Letzte Lüfter-/Temperaturmessung und Auto-Tuning-Stand; null, wenn der Dienst nicht erreichbar ist.</summary>
    public async Task<(Core.Hardware.SensorSnapshot? Sensors, FanTuneStatus? Tune)> SensorsAsync()
    {
        try
        {
            var r = await Task.Run(() => client.SendAsync(new PipeRequest { Command = Commands.Sensors }));
            return r.Ok ? (r.Sensors, r.Tune) : (null, null);
        }
        catch (Exception ex) when (ex is System.IO.IOException or System.Text.Json.JsonException) { return (null, null); }
    }

    /// <summary>Start or cancel fan auto-tuning.
    /// Auto-Tuning der Lüfter starten oder abbrechen.</summary>
    /// <param name="action">"start" (fans and pump), "pump" or "cancel".
    /// "start" (Lüfter und Pumpe), "pump" oder "cancel".</param>
    public Task FanTuneAsync(string action) =>
        SendAsync(new PipeRequest { Command = Commands.FanTune, Name = action }, reload: false);

    /// <summary>
    /// The ASUS lighting/fan service is installed (per service); otherwise the app hides the related switches.
    /// ASUS-Licht- bzw. Lüfterdienst ist installiert (laut Dienst); sonst blendet die App die Schalter aus.
    /// </summary>
    public bool AsusLightingInstalled => IsInstalled(Status?.AsusLightingService);
    public bool AsusFanServiceInstalled => IsInstalled(Status?.AsusFanService);

    static bool IsInstalled(string? state) => !string.IsNullOrEmpty(state) && state != "nicht installiert";

    public Task SetAsusLightingAsync(bool start) =>
        SendAsync(new PipeRequest { Command = Commands.AsusLightingService, Name = start ? "start" : "stop" }, reload: false);

    /// <summary>Reload the profile from the service.
    /// Profil neu vom Dienst übernehmen.</summary>
    public Task ReapplyAsync() => SendAsync(new PipeRequest { Command = Commands.Reapply }, reload: true);

    /// <summary>Ask the service to retry hardware access (after PawnIO setup).
    /// Dienst soll den Hardwarezugriff neu versuchen (nach PawnIO-Einrichtung).</summary>
    public Task HardwareRetryAsync() => SendAsync(new PipeRequest { Command = Commands.HardwareRetry }, reload: false);

    async Task SendAsync(PipeRequest request, bool reload)
    {
        PipeResponse r;
        try { r = await Task.Run(() => client.SendAsync(request)); }
        catch (Exception ex) when (ex is System.IO.IOException or System.Text.Json.JsonException)
        {
            r = PipeResponse.Fail(Loc.T("Verbindung zum Dienst gestört: ", "Connection to the service failed: ") + ex.Message);
        }
        LastError = r.Ok ? null : r.Error;
        LastNotes = r.Notes;
        if (r.Status != null)
        {
            Status = r.Status;
            if (reload && r.Status.ActiveProfile is { } p && !Pending)
            {
                Profile = p.Clone();
                Labels = new Dictionary<string, string>(r.Status.ZoneLabels);
                Reloaded?.Invoke();
            }
        }
        StateChanged?.Invoke();
    }
}
