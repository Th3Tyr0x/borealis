using Borealis.Core.Diagnostics;
using Borealis.Core.Hardware;
using Borealis.Core.Ipc;
using Borealis.Core.Lighting;
using Borealis.Core.Profiles;
using Borealis.Core.Windows;

namespace Borealis.Service;

/// <summary>
/// Zustand des Dienstes: Einstellungen, Profile und die geöffnete Hardware. Alle Hardwarezugriffe laufen
/// nacheinander über <see cref="gate"/>; der Dienst läuft in MTA-Threads, wie es die ENE-RAM-HAL verlangt.
/// </summary>
sealed class BorealisRuntime : IDisposable
{
    readonly ProfileStore store = new();
    readonly SemaphoreSlim gate = new(1, 1);
    BorealisSettings settings = new();
    LightingEngine? engine;
    string? hardwareError;
    // Gemeinsame Sperre für Hardwarezugriffe von Befehlen und Effekt-Schleife
    readonly object hw = new();
    readonly EffectRunner runner;
    readonly SensorMonitor sensors;
    readonly FanController fans = new();
    readonly FanTuner tuner;
    string? fanError;
    // Abgleich-Modus: Referenzfarbe auf allen Zonen statt des Profils
    Rgb? calibrationColor;
    // Farbabgleich geändert, Standby-Effekt im Controller noch mit alten Farben (wird gebündelt nachgeschrieben)
    bool standbyStale;

    public BorealisRuntime()
    {
        runner = new EffectRunner(hw);
        sensors = new SensorMonitor(id => settings.Fans.Tuning.ContainsKey(id));
        tuner = new FanTuner(() => sensors.Latest.Temperatures.FirstOrDefault(t => t.Id == "tctl")?.Celsius);
    }

    public async Task StartAsync(CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            settings = store.LoadSettings();
            var profile = store.LoadActiveOrDefault(settings);
            store.SaveSettings(settings);
            if (settings.Fans.Enabled) ApplyFansLocked("beim Start");
            if (!settings.Configured)
            {
                // Noch nie gespeichert: nichts ändern, bis der Benutzer in der App ein Profil speichert
                Log.Info("Noch kein Profil gespeichert: Beleuchtung bleibt unverändert.");
                EnsureEngine();
                return;
            }
            var result = ApplyLocked(profile, includeStandby: false, force: true);
            Log.Info($"Profil „{profile.Name}“ beim Start angewendet{FormatNotes(result)}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Error("Profil beim Start nicht angewendet", ex);
        }
        finally { gate.Release(); }
    }

    /// <summary>Nach dem Aufwachen: Geräte neu öffnen (Handles können ungültig sein) und Profil erneut anwenden.</summary>
    public async Task OnResumeAsync()
    {
        bool lighting = settings.ReapplyOnResume && settings.Configured;
        if (!lighting && !settings.Fans.Enabled) return;
        await Task.Delay(3000);
        await gate.WaitAsync();
        try
        {
            // Das BIOS setzt nach dem Aufwachen seine eigenen Lüfterkurven
            if (settings.Fans.Enabled)
            {
                fans.Reset();
                ApplyFansLocked("nach dem Standby");
            }
            if (!lighting) return;
            CloseEngine();
            var profile = store.LoadActiveOrDefault(settings);
            ApplyLocked(profile, includeStandby: false, force: true);
            Log.Info($"Profil „{profile.Name}“ nach dem Standby erneut angewendet.");
        }
        catch (Exception ex) { Log.Error("Profil nach dem Standby nicht angewendet", ex); }
        finally { gate.Release(); }
    }

    public async Task<PipeResponse> HandleAsync(PipeRequest request, CancellationToken token)
    {
        // Sensoren laufen in eigenem Thread und brauchen die Beleuchtungs-Sperre nicht
        if (request.Command == Commands.Sensors) return new PipeResponse { Sensors = sensors.Latest, Tune = tuner.Status };
        await gate.WaitAsync(token);
        try
        {
            switch (request.Command)
            {
                case Commands.Status:
                    EnsureEngine();
                    return new PipeResponse { Status = BuildStatus() };

                case Commands.Preview:
                    return Respond(ApplyLocked(Require(request.Profile), includeStandby: false));

                case Commands.Save:
                {
                    var p = Require(request.Profile);
                    if (string.IsNullOrWhiteSpace(p.Name)) return PipeResponse.Fail("Profilname fehlt.");
                    p.Name = p.Name.Trim();
                    // Standby-Effekte nur schreiben, wenn sie sich geändert haben (die App speichert automatisch)
                    var old = store.LoadProfile(p.Name);
                    bool standbyChanged = old == null || !settings.Configured || !string.Equals(old.Name, settings.ActiveProfile, StringComparison.CurrentCultureIgnoreCase)
                        || StandbyJson(old) != StandbyJson(p);
                    store.SaveProfile(p);
                    settings.ActiveProfile = p.Name;
                    settings.Configured = true;
                    if (request.Labels != null) settings.ZoneLabels = request.Labels;
                    store.SaveSettings(settings);
                    Log.Info($"Profil „{p.Name}“ gespeichert und aktiviert{(standbyChanged ? " (mit Standby-Effekt)" : "")}.");
                    return Respond(ApplyLocked(p, includeStandby: standbyChanged || standbyStale));
                }

                case Commands.Rename:
                {
                    var p = store.LoadProfile(request.Name ?? "");
                    var newName = request.NewName?.Trim();
                    if (p == null) return PipeResponse.Fail($"Profil „{request.Name}“ nicht gefunden.");
                    if (string.IsNullOrWhiteSpace(newName)) return PipeResponse.Fail("Neuer Name fehlt.");
                    if (store.LoadProfile(newName) != null) return PipeResponse.Fail($"Ein Profil „{newName}“ gibt es schon.");
                    p.Name = newName;
                    store.SaveProfile(p);
                    store.DeleteProfile(request.Name!);
                    if (string.Equals(request.Name, settings.ActiveProfile, StringComparison.CurrentCultureIgnoreCase))
                    {
                        settings.ActiveProfile = newName;
                        store.SaveSettings(settings);
                    }
                    Log.Info($"Profil „{request.Name}“ in „{newName}“ umbenannt.");
                    return new PipeResponse { Status = BuildStatus() };
                }

                case Commands.SetSettings:
                {
                    var s = request.Settings ?? throw new ArgumentException("Einstellungen fehlen.");
                    settings.StopAsusLightingService = s.StopAsusLightingService;
                    settings.ReapplyOnResume = s.ReapplyOnResume;
                    settings.ShowUnconnectedFans = s.ShowUnconnectedFans;
                    bool effectsChanged = settings.PerformanceMode != s.PerformanceMode || settings.PerformanceLevel != s.PerformanceLevel
                        || settings.PreferHardwareEffects != s.PreferHardwareEffects
                        || !settings.LedCounts.OrderBy(k => k.Key).SequenceEqual((s.LedCounts ?? new()).OrderBy(k => k.Key));
                    settings.PerformanceMode = s.PerformanceMode;
                    settings.PerformanceLevel = Math.Clamp(s.PerformanceLevel, 1, 3);
                    settings.PreferHardwareEffects = s.PreferHardwareEffects;
                    settings.LedCounts = s.LedCounts ?? new();
                    var calibration = (s.Calibration ?? new()).Where(k => !k.Value.IsNeutral).ToDictionary(k => k.Key, k => k.Value);
                    bool calibrationChanged = CalibrationJson(settings.Calibration) != CalibrationJson(calibration);
                    settings.Calibration = calibration;
                    if (engine != null) engine.Calibration = calibration;
                    var fanNotes = SetFansLocked(s.Fans ?? new FanSettings());
                    if (settings.DisableAsusLightingService != s.DisableAsusLightingService)
                    {
                        settings.DisableAsusLightingService = s.DisableAsusLightingService;
                        ApplyLightingServiceStartType();
                    }
                    store.SaveSettings(settings);
                    Log.Info($"Einstellungen: LightingService anhalten={s.StopAsusLightingService}, deaktivieren={s.DisableAsusLightingService}, nach Standby anwenden={s.ReapplyOnResume}, Leistungsmodus={s.PerformanceMode}/{s.PerformanceLevel}, Hardware-Effekte={s.PreferHardwareEffects}.");
                    if (calibrationChanged)
                    {
                        Log.Info($"Farbabgleich: {(calibration.Count == 0 ? "neutral" : string.Join(", ", calibration.Select(k => $"{k.Key} R{k.Value.Red} G{k.Value.Green} B{k.Value.Blue} {k.Value.Order}")))}.");
                        // Standby-Effekt nicht bei jedem Schieberegler-Schritt schreiben, sondern beim Verlassen des Abgleichs
                        standbyStale = true;
                        if (calibrationColor is { } c) { ShowCalibration(c); return new PipeResponse { Status = BuildStatus() }; }
                    }
                    if ((effectsChanged || calibrationChanged) && settings.Configured)
                    {
                        var applied = ApplyLocked(store.LoadActiveOrDefault(settings), includeStandby: false);
                        foreach (var n in fanNotes) applied.Note(n);
                        return Respond(applied);
                    }
                    return new PipeResponse { Status = BuildStatus(), Notes = fanNotes };
                }

                case Commands.AsusLightingService:
                {
                    // Armoury Crate die Beleuchtung zurückgeben (start) oder wegnehmen (stop).
                    // Die Einstellung merkt sich das, sonst hält die Überwachung den Dienst sofort wieder an.
                    if (request.Name == "start")
                    {
                        settings.StopAsusLightingService = false;
                        settings.DisableAsusLightingService = false;
                        ApplyLightingServiceStartType();
                        store.SaveSettings(settings);
                        ServiceControl.Start(ServiceControl.AsusLightingService);
                        Log.Info("Beleuchtung an Armoury Crate zurückgegeben.");
                    }
                    else
                    {
                        settings.StopAsusLightingService = true;
                        store.SaveSettings(settings);
                        ServiceControl.Stop(ServiceControl.AsusLightingService);
                        Log.Info("Beleuchtung von Armoury Crate übernommen.");
                    }
                    return new PipeResponse { Status = BuildStatus() };
                }

                case Commands.Activate:
                {
                    var p = store.LoadProfile(request.Name ?? "");
                    if (p == null) return PipeResponse.Fail($"Profil „{request.Name}“ nicht gefunden.");
                    settings.ActiveProfile = p.Name;
                    settings.Configured = true;
                    store.SaveSettings(settings);
                    Log.Info($"Profil „{p.Name}“ aktiviert.");
                    return Respond(ApplyLocked(p, includeStandby: true, force: true));
                }

                case Commands.Delete:
                {
                    var name = request.Name ?? "";
                    if (string.Equals(name, settings.ActiveProfile, StringComparison.CurrentCultureIgnoreCase))
                        return PipeResponse.Fail("Das aktive Profil kann nicht gelöscht werden.");
                    if (!store.DeleteProfile(name)) return PipeResponse.Fail($"Profil „{name}“ nicht gefunden.");
                    Log.Info($"Profil „{name}“ gelöscht.");
                    return new PipeResponse { Status = BuildStatus() };
                }

                case Commands.Reapply:
                    if (!settings.Configured)
                    {
                        // Noch nie gespeichert: Vorschau zurücknehmen heißt, Armoury Crate wieder übernehmen lassen
                        ServiceControl.Start(ServiceControl.AsusLightingService);
                        return new PipeResponse { Status = BuildStatus(), Notes = ["Armoury-Crate-Beleuchtung wiederhergestellt."] };
                    }
                    return Respond(ApplyLocked(store.LoadActiveOrDefault(settings), includeStandby: false, force: true));

                case Commands.SetLedCount:
                {
                    var e = EnsureEngine() ?? throw new IOException(hardwareError ?? "Beleuchtung nicht verfügbar.");
                    var zone = e.Zones.FirstOrDefault(z => z.Id == request.Name) ?? throw new ArgumentException($"Zone „{request.Name}“ nicht gefunden.");
                    int before = zone.LedCount;
                    runner.Pause();
                    int after;
                    lock (hw) after = e.SetLedCount(zone, request.Count);
                    // Der Controller kennt die Anzahl jetzt selbst; eine Streaming-Begrenzung ist nicht mehr nötig
                    settings.LedCounts.Remove(zone.Id);
                    store.SaveSettings(settings);
                    Log.Info($"LED-Anzahl {zone.Id}: {before} → {request.Count} (Controller meldet {after}).");
                    CloseEngine();
                    var result = settings.Configured ? ApplyLocked(store.LoadActiveOrDefault(settings), includeStandby: false, force: true) : new ApplyResult();
                    if (after != request.Count) result.Note($"Der Controller meldet {after} statt {request.Count} LEDs.");
                    return Respond(result);
                }

                case Commands.LedTest:
                {
                    if (string.IsNullOrEmpty(request.Name))
                        return Respond(settings.Configured ? ApplyLocked(store.LoadActiveOrDefault(settings), includeStandby: false, force: true) : new ApplyResult());
                    StopAsusLightingServiceIfWanted();
                    var e = EnsureEngine() ?? throw new IOException(hardwareError ?? "Beleuchtung nicht verfügbar.");
                    var zone = e.Zones.FirstOrDefault(z => z.Id == request.Name) ?? throw new ArgumentException($"Zone „{request.Name}“ nicht gefunden.");
                    runner.Pause();
                    lock (hw) e.ShowLedTest(zone);
                    Log.Info($"Zähltest auf {zone.Id} ({zone.LedCount} LEDs).");
                    return new PipeResponse { Status = BuildStatus() };
                }

                case Commands.CalibrationPreview:
                {
                    if (string.IsNullOrEmpty(request.Name))
                    {
                        bool wasActive = calibrationColor != null;
                        calibrationColor = null;
                        if (!settings.Configured || (!wasActive && !standbyStale)) return new PipeResponse { Status = BuildStatus() };
                        if (wasActive) Log.Info("Abgleich-Modus beendet.");
                        return Respond(ApplyLocked(store.LoadActiveOrDefault(settings), includeStandby: standbyStale, force: wasActive));
                    }
                    if (!Rgb.TryParse(request.Name, out var color)) return PipeResponse.Fail($"Ungültige Farbe „{request.Name}“.");
                    if (calibrationColor == null) Log.Info($"Abgleich-Modus mit Referenzfarbe {color}.");
                    ShowCalibration(color);
                    return new PipeResponse { Status = BuildStatus() };
                }

                case Commands.FanTune:
                    if (request.Name == "cancel")
                    {
                        Log.Info("Auto-Tuning: Abbruch über die App angefordert.");
                        tuner.Cancel();
                        return new PipeResponse { Status = BuildStatus(), Tune = tuner.Status };
                    }
                    if (!settings.Fans.Enabled) return PipeResponse.Fail("Das Auto-Tuning geht nur, wenn Borealis die Lüfter steuert.");
                    // "start" = alle; "only:CPUFAN,AUXFAN4" = nur diese; "pump" = nur Pumpen (ältere App)
                    IReadOnlyCollection<string>? only = request.Name switch
                    {
                        "pump" => BoardFans.Headers.Where(h => h.Pump).Select(h => h.Id).ToList(),
                        { } n when n.StartsWith("only:") => n[5..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                        _ => null,
                    };
                    tuner.Start(OnTuned, only);
                    return new PipeResponse { Status = BuildStatus(), Tune = tuner.Status };

                case Commands.HardwareRetry:
                {
                    Log.Info("Hardwarezugriff wird neu versucht (PawnIO eingerichtet).");
                    sensors.Retry();
                    var notes = settings.Fans.Enabled ? ApplyFansLocked("nach Einrichtung von PawnIO") : [];
                    return new PipeResponse { Status = BuildStatus(), Notes = notes };
                }

                case Commands.SetLabels:
                    settings.ZoneLabels = request.Labels ?? new();
                    store.SaveSettings(settings);
                    return new PipeResponse { Status = BuildStatus() };

                default:
                    return PipeResponse.Fail($"Unbekannter Befehl „{request.Command}“.");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException
                                       or TimeoutException or System.Runtime.InteropServices.ExternalException)
        {
            Log.Error($"Befehl {request.Command} fehlgeschlagen", ex);
            if (ex is IOException or TimeoutException) CloseEngine();
            return PipeResponse.Fail(ex.Message);
        }
        finally { gate.Release(); }
    }

    PipeResponse Respond(ApplyResult result) => new() { Notes = result.Notes, Status = BuildStatus() };

    static LightingProfile Require(LightingProfile? p) => p ?? throw new ArgumentException("Profil fehlt in der Anfrage.");

    ApplyResult ApplyLocked(LightingProfile profile, bool includeStandby, bool force = false)
    {
        StopAsusLightingServiceIfWanted();
        calibrationColor = null;
        if (includeStandby) standbyStale = false;
        var options = new LightingOptions { PreferHardwareEffects = settings.PreferHardwareEffects, LedCounts = settings.LedCounts };
        ApplyResult Apply(LightingEngine e)
        {
            var software = new List<SoftwareZone>();
            ApplyResult r;
            lock (hw) r = e.ApplyProfile(profile, includeStandby, options, software, force);
            runner.Update(e, software, Math.Clamp(profile.Brightness, 1, 100) / 100.0, settings.FramesPerSecond);
            return r;
        }
        var engine = EnsureEngine() ?? throw new IOException(hardwareError ?? "Beleuchtung nicht verfügbar.");
        try { return Apply(engine); }
        catch (IOException)
        {
            // USB-Handle nach Standby oder Neuanmeldung ungültig: einmal neu öffnen und wiederholen
            CloseEngine();
            engine = EnsureEngine() ?? throw new IOException(hardwareError ?? "Beleuchtung nicht verfügbar.");
            force = true;
            return Apply(engine);
        }
    }

    /// <summary>Abgleich-Modus: Effekte anhalten und alle Zonen in der Referenzfarbe (mit Farbabgleich) zeigen.</summary>
    void ShowCalibration(Rgb color)
    {
        StopAsusLightingServiceIfWanted();
        var e = EnsureEngine() ?? throw new IOException(hardwareError ?? "Beleuchtung nicht verfügbar.");
        runner.Pause();
        lock (hw) e.ApplyDirect(e.Zones.ToDictionary(z => z, _ => color));
        calibrationColor = color;
    }

    void ApplyLightingServiceStartType()
    {
        if (ServiceControl.GetState(ServiceControl.AsusLightingService) == ServiceState.NotInstalled) return;
        var type = settings.DisableAsusLightingService ? ServiceStartType.Disabled : ServiceStartType.Automatic;
        ServiceControl.SetStartType(ServiceControl.AsusLightingService, type);
        if (settings.DisableAsusLightingService) ServiceControl.Stop(ServiceControl.AsusLightingService);
        Log.Info($"ASUS-LightingService: Starttyp {type}.");
    }

    /// <summary>
    /// Armoury Crate startet den LightingService neu, sobald er angehalten wird. Solange Borealis die Beleuchtung
    /// steuern soll, wird er hier wieder angehalten und das Profil neu angewendet (er streamt beim Start sein Profil).
    /// </summary>
    public async Task WatchAsusLightingServiceAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(3), token);
            bool lighting = settings.Configured && settings.StopAsusLightingService && IsRunning(ServiceControl.AsusLightingService);
            bool fan = settings.Fans.Enabled && IsRunning(ServiceControl.AsusFanService);
            if (!lighting && !fan) continue;
            await gate.WaitAsync(token);
            try
            {
                // Kurz warten, bis der Dienst seinen Start abgeschlossen hat, sonst überschreibt er danach
                await Task.Delay(TimeSpan.FromSeconds(2), token);
                if (lighting)
                {
                    Log.Info("ASUS-LightingService wurde neu gestartet; Borealis übernimmt wieder.");
                    if (calibrationColor is { } c) ShowCalibration(c);
                    else ApplyLocked(store.LoadActiveOrDefault(settings), includeStandby: false, force: true);
                }
                if (fan)
                {
                    Log.Info("ASUS-Lüfterdienst wurde gestartet; Borealis übernimmt die Lüfter wieder.");
                    ApplyFansLocked("nach Neustart des ASUS-Lüfterdienstes");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Error("Übernahme nach Neustart des LightingService fehlgeschlagen", ex);
            }
            finally { gate.Release(); }
        }
    }

    static bool IsRunning(string service) =>
        ServiceControl.GetState(service) is ServiceState.Running or ServiceState.StartPending;

    /// <summary>
    /// Neue Lüftereinstellungen übernehmen. Beim ersten Einschalten werden die Kurven, die gerade im Chip stehen (vom
    /// ASUS-Dienst), als eigene Kurven übernommen, damit sich nichts sprunghaft ändert. Beim Ausschalten bekommt der
    /// ASUS-Lüfterdienst die Lüfter zurück.
    /// </summary>
    List<string> SetFansLocked(FanSettings next)
    {
        var old = settings.Fans;
        bool changed = System.Text.Json.JsonSerializer.Serialize(old, ProfileStore.Json) != System.Text.Json.JsonSerializer.Serialize(next, ProfileStore.Json);
        if (!changed) return [];
        next = next.Clone();
        foreach (var (id, curve) in next.Curves.ToList()) next.Curves[id] = FanSettings.Normalize(curve);
        foreach (var mode in next.ProfileCurves.Values)
            foreach (var (id, curve) in mode.ToList()) mode[id] = FanSettings.Normalize(curve);
        if (next.Enabled && next.Curves.Count == 0)
        {
            try { next.Curves = fans.ReadCurves(); }
            catch (Exception ex) when (ex is IOException or TimeoutException) { Log.Warn("Kurven aus dem Chip nicht lesbar: " + ex.Message); }
        }
        settings.Fans = next;
        if (old.DisableAsusFanService != next.DisableAsusFanService)
        {
            try
            {
                SetAsusFanStartType(next.DisableAsusFanService ? ServiceStartType.Disabled : ServiceStartType.Automatic);
                // Wieder erlaubt und Borealis steuert nicht: ASUS übernimmt wie vorher
                if (!next.DisableAsusFanService && !next.Enabled) ServiceControl.Start(ServiceControl.AsusFanService);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Log.Error("Starttyp des ASUS-Lüfterdienstes nicht geändert", ex); }
        }
        if (!next.Enabled)
        {
            tuner.Cancel();
            fanError = null;
            if (old.Enabled && !next.DisableAsusFanService) GiveFansBackToAsus();
            return [];
        }
        return ApplyFansLocked("nach Änderung");
    }

    /// <summary>ASUS-Lüfterdienst anhalten (er schreibt sonst beim Start seine Kurven) und die eigenen Kurven schreiben.</summary>
    List<string> ApplyFansLocked(string when, bool fromTuner = false)
    {
        // Während des Auto-Tunings stellt der Tuner die Leistung; die Kurven folgen, wenn er fertig ist
        if (tuner.Running && !fromTuner) return ["Das Auto-Tuning läuft; die Kurven werden danach geschrieben."];
        try
        {
            if (settings.Fans.DisableAsusFanService) SetAsusFanStartType(ServiceStartType.Disabled);
            if (IsRunning(ServiceControl.AsusFanService))
            {
                Log.Info("ASUS-Lüfterdienst wird angehalten.");
                ServiceControl.Stop(ServiceControl.AsusFanService);
            }
            var notes = fans.Apply(settings.Fans);
            fanError = null;
            Log.Info($"Lüfter {when} übernommen.");
            return notes;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or ArgumentException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception)
        {
            fans.Reset();
            fanError = ex.Message;
            if (ex is PawnIoMissingException) Log.Warn($"Lüfter {when} nicht übernommen: {ex.Message}");
            else Log.Error($"Lüfter {when} nicht übernommen", ex);
            return ["Lüfter: " + ex.Message];
        }
    }

    /// <summary>Ende des Auto-Tunings (Tuner-Thread): Ergebnisse speichern und die normalen Kurven zurückschreiben.</summary>
    void OnTuned(Dictionary<string, FanTuning> results)
    {
        gate.Wait();
        try
        {
            if (results.Count > 0)
            {
                // Neues Wörterbuch statt Ändern: der Sensor-Thread liest es gleichzeitig
                var tuning = new Dictionary<string, FanTuning>(settings.Fans.Tuning);
                foreach (var (id, t) in results) tuning[id] = t;
                settings.Fans.Tuning = tuning;
                store.SaveSettings(settings);
            }
            if (settings.Fans.Enabled) ApplyFansLocked("nach dem Auto-Tuning", fromTuner: true);
            else if (IsRunning(ServiceControl.AsusFanService))
            {
                // Während des Abbruchs zurückgegeben: ASUS neu starten, damit er seine Kurven wieder schreibt
                ServiceControl.Stop(ServiceControl.AsusFanService);
                ServiceControl.Start(ServiceControl.AsusFanService);
            }
        }
        finally { gate.Release(); }
    }

    void GiveFansBackToAsus()
    {
        try
        {
            if (ServiceControl.GetState(ServiceControl.AsusFanService) == ServiceState.NotInstalled)
            {
                Log.Info("Lüftersteuerung aus; die Kurven im Chip bleiben, nach einem Neustart regelt das BIOS.");
                return;
            }
            SetAsusFanStartType(ServiceStartType.Automatic);
            ServiceControl.Start(ServiceControl.AsusFanService);
            Log.Info("Lüfter an den ASUS-Lüfterdienst zurückgegeben.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Log.Error("ASUS-Lüfterdienst nicht gestartet", ex); }
    }

    static void SetAsusFanStartType(ServiceStartType type)
    {
        if (ServiceControl.GetState(ServiceControl.AsusFanService) == ServiceState.NotInstalled) return;
        if (ServiceControl.GetStartType(ServiceControl.AsusFanService) == type) return;
        ServiceControl.SetStartType(ServiceControl.AsusFanService, type);
        if (type == ServiceStartType.Disabled) ServiceControl.Stop(ServiceControl.AsusFanService);
        Log.Info($"ASUS-Lüfterdienst: Starttyp {type}.");
    }

    void StopAsusLightingServiceIfWanted()
    {
        if (!settings.StopAsusLightingService) return;
        var state = ServiceControl.GetState(ServiceControl.AsusLightingService);
        if (state is ServiceState.Running or ServiceState.StartPending)
        {
            Log.Info("ASUS-LightingService wird angehalten.");
            ServiceControl.Stop(ServiceControl.AsusLightingService);
        }
    }

    LightingEngine? EnsureEngine()
    {
        if (engine != null) return engine;
        try
        {
            engine = LightingEngine.Open();
            engine.Calibration = settings.Calibration;
            hardwareError = null;
            if (engine.RamError != null) Log.Warn("RAM nicht verfügbar: " + engine.RamError);
            Log.Info($"Beleuchtung geöffnet: {string.Join(", ", engine.Zones.Select(z => z.Id))}" +
                     (engine.Ram != null ? $"; RAM über {engine.Ram.Source} ({string.Join(", ", engine.Ram.Modules.Select(m => m.Name).Distinct())})." : "."));
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or System.Runtime.InteropServices.ExternalException)
        {
            hardwareError = ex.Message;
            Log.Error("Beleuchtung nicht verfügbar", ex);
        }
        return engine;
    }

    void CloseEngine()
    {
        runner.Pause();
        lock (hw) engine?.Dispose();
        engine = null;
    }

    StatusInfo BuildStatus()
    {
        var ls = ServiceControl.GetState(ServiceControl.AsusLightingService);
        return new StatusInfo
        {
            ServiceVersion = typeof(BorealisRuntime).Assembly.GetName().Version?.ToString() ?? "",
            HardwareReady = engine != null,
            HardwareError = hardwareError,
            RamError = engine?.RamError,
            Zones = engine?.Zones.Select(z => new ZoneInfo { Id = z.Id, Name = z.Name, Kind = z.Kind.ToString(), LedCount = z.LedCount }).ToList() ?? [],
            Profiles = store.ListProfiles().ToList(),
            ActiveProfile = store.LoadActiveOrDefault(settings),
            ZoneLabels = settings.ZoneLabels,
            Settings = settings,
            EffectFps = Math.Round(runner.MeasuredFps, 1),
            CalibrationColor = calibrationColor?.ToString(),
            AsusFanService = FormatState(ServiceControl.GetState(ServiceControl.AsusFanService)),
            FanError = fanError,
            AsusLightingService = FormatState(ls),
        };
    }

    static string FormatState(ServiceState state) => state switch
    {
        ServiceState.NotInstalled => "nicht installiert",
        ServiceState.Running => "läuft",
        ServiceState.Stopped => "angehalten",
        _ => state.ToString(),
    };

    static string StandbyJson(LightingProfile p) =>
        System.Text.Json.JsonSerializer.Serialize(new { p.StandbyMainboard, p.StandbyArgb }, ProfileStore.Json);

    static string CalibrationJson(Dictionary<string, ZoneCalibration> c) =>
        System.Text.Json.JsonSerializer.Serialize(c.OrderBy(k => k.Key, StringComparer.Ordinal), ProfileStore.Json);

    static string FormatNotes(ApplyResult r) => r.Notes.Count == 0 ? "" : " (" + string.Join("; ", r.Notes) + ")";

    public void Dispose()
    {
        tuner.Cancel();
        CloseEngine();
        runner.Dispose();
        sensors.Dispose();
        fans.Dispose();
        gate.Dispose();
    }
}
