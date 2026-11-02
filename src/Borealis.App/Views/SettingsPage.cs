using System.Windows;
using System.Windows.Controls;
using Borealis.App.Controls;
using Borealis.Core.Profiles;

namespace Borealis.App.Views;

/// <summary>Profile verwalten, Verhalten des Dienstes, Armoury Crate, Info.</summary>
sealed class SettingsPage : UserControl, IPage
{
    static AppState S => AppState.Instance;
    public string Title => Loc.T("Einstellungen", "Settings");

    public SettingsPage() => Rebuild();

    public void Rebuild()
    {
        var status = S.Status;
        var root = new StackPanel { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };

        root.Children.Add(Ui.Card(AppearanceCard()));
        root.Children.Add(Ui.Card(HardwareCard()));

        // Profile
        var profiles = new StackPanel();
        profiles.Children.Add(Ui.Section(Loc.T("Profile", "Profiles")));
        foreach (var name in status?.Profiles ?? [])
        {
            bool active = name == S.Profile.Name;
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(buttons, Dock.Right);
            if (!active)
            {
                var activate = new Button { Content = Loc.T("Aktivieren", "Activate"), Margin = new Thickness(0, 0, 6, 0) };
                activate.Click += async (_, _) => await S.ActivateAsync(name);
                buttons.Children.Add(activate);
            }
            var rename = new Button { Content = Loc.T("Umbenennen", "Rename"), Margin = new Thickness(0, 0, 6, 0) };
            rename.Click += async (_, _) =>
            {
                var newName = InputDialog.Show(Window.GetWindow(this)!, Loc.T("Profil umbenennen", "Rename profile"), Loc.T($"Neuer Name für „{name}“:", $"New name for “{name}”:"), name);
                if (!string.IsNullOrWhiteSpace(newName) && newName.Trim() != name) await S.RenameAsync(name, newName);
            };
            buttons.Children.Add(rename);
            var delete = new Button { Content = Loc.T("Löschen", "Delete"), IsEnabled = !active, ToolTip = active ? Loc.T("Das aktive Profil kann nicht gelöscht werden.", "The active profile cannot be deleted.") : null };
            delete.Click += async (_, _) =>
            {
                if (MessageBox.Show(Window.GetWindow(this)!, Loc.T($"Profil „{name}“ löschen?", $"Delete profile “{name}”?"), "Borealis", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    await S.DeleteAsync(name);
            };
            buttons.Children.Add(delete);
            row.Children.Add(buttons);
            var label = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(Ui.Icon(Icons.Resource("IconProfile"), 18, active ? Ui.Brush("Accent") : Ui.Brush("TextDim")));
            label.Children.Add(new TextBlock { Text = name, Margin = new Thickness(10, 0, 0, 0), FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal });
            if (active) label.Children.Add(new TextBlock { Text = Loc.T("aktiv", "active"), Foreground = Ui.Brush("Accent"), Margin = new Thickness(10, 0, 0, 0), FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(label);
            profiles.Children.Add(row);
        }
        var add = new Button { Content = Loc.T("Neues Profil …", "New profile …"), Style = Ui.Style("AccentButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
        add.Click += async (_, _) =>
        {
            var name = InputDialog.Show(Window.GetWindow(this)!, Loc.T("Neues Profil", "New profile"),
                Loc.T("Name des neuen Profils (übernimmt die aktuellen Einstellungen):", "Name of the new profile (takes over the current settings):"), "");
            if (!string.IsNullOrWhiteSpace(name)) await S.CreateAsync(name);
        };
        var resetProfile = new Button { Content = Loc.T("Aktives Profil auf Standard zurücksetzen", "Reset active profile to default"), Margin = new Thickness(10, 8, 0, 0) };
        resetProfile.Click += async (_, _) =>
        {
            if (MessageBox.Show(Window.GetWindow(this)!,
                    Loc.T($"Profil „{S.Profile.Name}“ auf Standard zurücksetzen?\n\nAlle Geräte kommen in Borealis Sync (Statisch, Rot), alle Effekt-Einstellungen, " +
                          "Zonenfarben und der Effekt nach dem Herunterfahren (Regenbogen) werden zurückgesetzt. Zonennamen und LED-Anzahlen bleiben.",
                          $"Reset profile “{S.Profile.Name}” to default?\n\nAll devices go back to Borealis Sync (static, red); all effect settings, " +
                          "zone colors and the effect after shutdown (rainbow) are reset. Zone names and LED counts stay."),
                    "Borealis", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            await S.ResetActiveProfileAsync();
        };
        var profileButtons = new StackPanel { Orientation = Orientation.Horizontal };
        profiles.Children.Remove(add);
        profileButtons.Children.Add(add);
        profileButtons.Children.Add(resetProfile);
        profiles.Children.Add(profileButtons);
        var exportButton = new Button { Content = Loc.T("Aktives Profil exportieren …", "Export active profile …"), Margin = new Thickness(0, 8, 0, 0) };
        exportButton.Click += (_, _) => ExportProfile();
        var importButton = new Button { Content = Loc.T("Profil importieren …", "Import profile …"), Margin = new Thickness(10, 8, 0, 0) };
        importButton.Click += async (_, _) => await ImportProfileAsync();
        var transferButtons = new StackPanel { Orientation = Orientation.Horizontal };
        transferButtons.Children.Add(exportButton);
        transferButtons.Children.Add(importButton);
        profiles.Children.Add(transferButtons);
        profiles.Children.Add(Ui.Hint(Loc.T("Eine Profildatei enthält die Beleuchtung (Effekte, Farben, Effekt nach dem Herunterfahren). Lüfter, Farbabgleich und Zonennamen bleiben auf diesem PC.",
                                            "A profile file contains the lighting (effects, colors, effect after shutdown). Fans, color matching and zone names stay on this PC."), 8));
        root.Children.Add(Ui.Card(profiles));

        // Verhalten
        var behavior = new StackPanel();
        behavior.Children.Add(Ui.Section(Loc.T("Verhalten", "Behavior")));
        var settings = status?.Settings ?? new BorealisSettings();
        // Gilt nur für diesen Benutzer und braucht den Dienst nicht
        var autostart = new CheckBox { Style = Ui.Style("Switch"), IsChecked = Autostart.IsEnabled, Content = Loc.T("Borealis bei der Anmeldung im Infobereich starten", "Start Borealis in the notification area at sign-in") };
        autostart.Click += (_, _) => Autostart.Set(autostart.IsChecked == true);
        behavior.Children.Add(autostart);
        var stopLs = new CheckBox { Style = Ui.Style("Switch"), IsChecked = settings.StopAsusLightingService, Content = Loc.T("ASUS-LightingService anhalten, damit Borealis die Beleuchtung steuert", "Stop the ASUS LightingService so Borealis controls the lighting"), Margin = new Thickness(0, 12, 0, 0) };
        var disableLs = new CheckBox { Style = Ui.Style("Switch"), IsChecked = settings.DisableAsusLightingService, Content = Loc.T("ASUS-LightingService dauerhaft deaktivieren (Armoury Crate kann ihn nicht mehr starten)", "Disable the ASUS LightingService permanently (Armoury Crate can no longer start it)"), Margin = new Thickness(0, 12, 0, 0) };
        var disableFan = new CheckBox { Style = Ui.Style("Switch"), IsChecked = settings.Fans.DisableAsusFanService, Content = Loc.T("ASUS-Lüfterdienst dauerhaft deaktivieren (Armoury Crate kann ihn nicht mehr starten)", "Disable the ASUS fan service permanently (Armoury Crate can no longer start it)"), Margin = new Thickness(0, 12, 0, 0) };
        var showFans = new CheckBox { Style = Ui.Style("Switch"), IsChecked = settings.ShowUnconnectedFans, Content = Loc.T("Nicht angeschlossene Lüfter anzeigen (Lüftersteuerung)", "Show unconnected fans (fan control)"), Margin = new Thickness(0, 12, 0, 0) };
        var resume = new CheckBox { Style = Ui.Style("Switch"), IsChecked = settings.ReapplyOnResume, Content = Loc.T("Profil nach dem Standby erneut anwenden", "Reapply profile after standby"), Margin = new Thickness(0, 12, 0, 0) };
        var hardware = new CheckBox { Style = Ui.Style("Switch"), IsChecked = settings.PreferHardwareEffects, Content = Loc.T("Hardware-Effekte bevorzugen (laufen ohne Borealis, aber nur Einzelfarbe und drei Geschwindigkeitsstufen)", "Prefer hardware effects (run without Borealis, but single color and three speed levels only)"), Margin = new Thickness(0, 12, 0, 0) };
        async void Save(object s, RoutedEventArgs e)
        {
            settings.StopAsusLightingService = stopLs.IsChecked == true;
            settings.DisableAsusLightingService = disableLs.IsChecked == true;
            settings.Fans.DisableAsusFanService = disableFan.IsChecked == true;
            settings.ShowUnconnectedFans = showFans.IsChecked == true;
            settings.ReapplyOnResume = resume.IsChecked == true;
            settings.PreferHardwareEffects = hardware.IsChecked == true;
            await S.SetSettingsAsync(settings);
            Rebuild();
        }
        stopLs.Click += Save;
        disableLs.Click += Save;
        disableFan.Click += Save;
        showFans.Click += Save;
        resume.Click += Save;
        hardware.Click += Save;
        // Schalter für ASUS-Dienste nur, solange sie installiert sind
        if (S.AsusLightingInstalled)
        {
            behavior.Children.Add(stopLs);
            behavior.Children.Add(disableLs);
        }
        if (S.AsusFanServiceInstalled) behavior.Children.Add(disableFan);
        behavior.Children.Add(showFans);
        behavior.Children.Add(resume);
        behavior.Children.Add(hardware);
        root.Children.Add(Ui.Card(behavior));

        // Armoury Crate (nur, solange der LightingService installiert ist)
        var ac = new StackPanel();
        ac.Children.Add(Ui.Section("Armoury Crate"));
        bool borealisControls = settings.StopAsusLightingService;
        ac.Children.Add(new TextBlock
        {
            Text = borealisControls ? Loc.T($"Borealis steuert die Beleuchtung (ASUS-LightingService: {ServiceState(status?.AsusLightingService)}).",
                                            $"Borealis controls the lighting (ASUS LightingService: {ServiceState(status?.AsusLightingService)}).")
                                  : Loc.T($"Armoury Crate steuert die Beleuchtung (ASUS-LightingService: {ServiceState(status?.AsusLightingService)}).",
                                          $"Armoury Crate controls the lighting (ASUS LightingService: {ServiceState(status?.AsusLightingService)})."),
            TextWrapping = TextWrapping.Wrap,
        });
        var acButton = new Button { Content = borealisControls ? Loc.T("Beleuchtung an Armoury Crate zurückgeben", "Hand lighting back to Armoury Crate") : Loc.T("Beleuchtung wieder mit Borealis steuern", "Control lighting with Borealis again"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
        acButton.Click += async (_, _) =>
        {
            if (borealisControls) await S.SetAsusLightingAsync(start: true);
            else { await S.SetAsusLightingAsync(start: false); await S.ReapplyAsync(); }
            Rebuild();
        };
        ac.Children.Add(acButton);
        ac.Children.Add(Ui.Hint(Loc.T("Armoury Crate startet den LightingService neu, sobald er angehalten wird. Der Borealis-Dienst bemerkt das innerhalb weniger Sekunden und übernimmt wieder – oder du deaktivierst ihn oben dauerhaft.",
                 "Armoury Crate restarts the LightingService as soon as it is stopped. The Borealis service notices this within a few seconds and takes over again – or you disable it permanently above."), 10));
        if (S.AsusLightingInstalled) root.Children.Add(Ui.Card(ac));

        // Info
        var info = new StackPanel();
        info.Children.Add(Ui.Section("Info"));
        info.Children.Add(new TextBlock { Text = $"{Loc.T("Borealis-Dienst", "Borealis service")}: {(status == null ? Loc.T("nicht verbunden", "not connected") : "v" + status.ServiceVersion)}" });
        info.Children.Add(new TextBlock { Text = $"App: v{typeof(SettingsPage).Assembly.GetName().Version} · .NET {Environment.Version}", Margin = new Thickness(0, 4, 0, 0) });
        info.Children.Add(UpdatePanel());
        // Hinweis, den die GPL für Programme mit Oberfläche vorsieht
        info.Children.Add(Ui.Hint(Loc.T(
            "Borealis ist freie Software unter der GNU General Public License v3 (oder später) und kommt ohne jede Gewährleistung. " +
            "Unabhängiges Projekt, nicht verbunden mit ASUSTeK Computer Inc.",
            "Borealis is free software under the GNU General Public License v3 (or later) and comes with absolutely no warranty. " +
            "Independent project, not affiliated with ASUSTeK Computer Inc."), 8));
        if (status != null)
        {
            info.Children.Add(new TextBlock { Text = $"{Loc.T("Zonen", "Zones")}: {string.Join(", ", status.Zones.Select(S.LabelOf))}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            if (status.RamError != null) info.Children.Add(new TextBlock { Text = "RAM: " + status.RamError, Foreground = Ui.Brush("Warn"), Margin = new Thickness(0, 4, 0, 0) });
            if (status.HardwareError != null) info.Children.Add(new TextBlock { Text = "Beleuchtung: " + status.HardwareError, Foreground = Ui.Brush("Warn"), Margin = new Thickness(0, 4, 0, 0) });
        }
        root.Children.Add(Ui.Card(info));

        Content = Ui.Scroll(root);
    }

    /// <summary>Neue Version, „Jetzt suchen“ und der Schalter für die tägliche Prüfung (je Benutzer).</summary>
    StackPanel UpdatePanel()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 12, 0, 4) };
        var prefs = UiPrefs.Current;
        var result = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        if (UpdateCheck.Available is { } v)
        {
            result.Text = Loc.T($"Borealis {v} ist verfügbar.", $"Borealis {v} is available.");
            result.Foreground = Ui.Brush("Accent");
            result.FontWeight = FontWeights.SemiBold;
        }
        else if (prefs.LastUpdateCheck is { } last)
        {
            result.Text = Loc.T($"Borealis ist aktuell (geprüft {last.ToLocalTime():g}).", $"Borealis is up to date (checked {last.ToLocalTime():g}).");
            result.Foreground = Ui.Brush("TextDim");
        }
        else result.Visibility = Visibility.Collapsed;
        panel.Children.Add(result);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        if (UpdateCheck.Available != null)
        {
            var open = new Button { Content = Loc.T("Zur Release-Seite", "Open release page"), Style = Ui.Style("AccentButton"), Margin = new Thickness(0, 0, 10, 0) };
            open.Click += (_, _) => UpdateCheck.OpenReleasePage();
            buttons.Children.Add(open);
        }
        var check = new Button { Content = Loc.T("Jetzt nach Updates suchen", "Check for updates now") };
        check.Click += async (_, _) =>
        {
            check.IsEnabled = false;
            var before = UiPrefs.Current.LastUpdateCheck;
            await UpdateCheck.CheckAsync(force: true);
            if (UiPrefs.Current.LastUpdateCheck == before)
            {
                check.IsEnabled = true;
                result.Visibility = Visibility.Visible;
                result.Foreground = Ui.Brush("Warn");
                result.Text = Loc.T("GitHub war nicht erreichbar.", "GitHub could not be reached.");
                return;
            }
            Rebuild();
        };
        buttons.Children.Add(check);
        panel.Children.Add(buttons);

        var auto = new CheckBox { Style = Ui.Style("Switch"), IsChecked = prefs.CheckForUpdates, Margin = new Thickness(0, 12, 0, 0),
            Content = Loc.T("Einmal am Tag nach Updates suchen", "Check for updates once a day") };
        auto.Click += (_, _) => UiPrefs.Save(UiPrefs.Current with { CheckForUpdates = auto.IsChecked == true });
        panel.Children.Add(auto);
        panel.Children.Add(Ui.Hint(Loc.T("Fragt nur bei GitHub nach der neuesten Version; heruntergeladen oder installiert wird nichts.",
                                         "Only asks GitHub for the latest version; nothing is downloaded or installed."), 6));
        return panel;
    }

    void ExportProfile()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Loc.T("Profil exportieren", "Export profile"),
            FileName = SafeFileName(S.Profile.Name) + ProfileTransfer.FileExtension,
            Filter = Loc.T("Borealis-Profil", "Borealis profile") + $" (*{ProfileTransfer.FileExtension})|*{ProfileTransfer.FileExtension}",
            AddExtension = false,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try { System.IO.File.WriteAllText(dialog.FileName, ProfileTransfer.Export(S.Profile)); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(Window.GetWindow(this)!, Loc.T("Export fehlgeschlagen: ", "Export failed: ") + ex.Message, "Borealis", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    async Task ImportProfileAsync()
    {
        var owner = Window.GetWindow(this)!;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.T("Profil importieren", "Import profile"),
            Filter = Loc.T("Borealis-Profil", "Borealis profile") + $" (*{ProfileTransfer.FileExtension})|*{ProfileTransfer.FileExtension}|JSON (*.json)|*.json",
        };
        if (dialog.ShowDialog(owner) != true) return;
        LightingProfile profile;
        try { profile = ProfileTransfer.Import(dialog.FileName); }
        catch (Exception ex) when (ex is System.IO.InvalidDataException or System.IO.IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(owner, ex.Message, "Borealis", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        // Gleichnamiges Profil nicht stillschweigend überschreiben
        var existing = S.Status?.Profiles ?? [];
        while (existing.Any(n => string.Equals(n, profile.Name, StringComparison.CurrentCultureIgnoreCase)))
        {
            var name = InputDialog.Show(owner, Loc.T("Profil importieren", "Import profile"),
                Loc.T($"Ein Profil „{profile.Name}“ gibt es schon. Name für das importierte Profil:", $"A profile “{profile.Name}” already exists. Name for the imported profile:"),
                profile.Name + " (2)");
            if (string.IsNullOrWhiteSpace(name)) return;
            profile.Name = name.Trim();
        }
        await S.ImportAsync(profile);
    }

    static string SafeFileName(string name) =>
        string.Concat(name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    /// <summary>Zustand eines Dienstes vom Borealis-Dienst (deutsch) für die Anzeige.</summary>
    static string ServiceState(string? state) => state switch
    {
        null => "?",
        "läuft" => Loc.T("läuft", "running"),
        "angehalten" => Loc.T("angehalten", "stopped"),
        "nicht installiert" => Loc.T("nicht installiert", "not installed"),
        _ => state,
    };

    /// <summary>Sprache und Farbschema der Oberfläche (je Benutzer, wirkt sofort).</summary>
    StackPanel AppearanceCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(Ui.Section(Loc.T("Darstellung", "Appearance")));
        var prefs = UiPrefs.Current;
        void Change(UiPrefs next)
        {
            UiPrefs.Save(next);
            ((App)Application.Current).RebuildWindow();
        }
        panel.Children.Add(new TextBlock { Text = Loc.T("Sprache", "Language"), Foreground = Ui.Brush("TextDim"), Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(Ui.Segments([("de", "Deutsch"), ("en", "English")], prefs.Language,
            lang => Change(prefs with { Language = lang }), "UiLanguage"));
        panel.Children.Add(new TextBlock { Text = Loc.T("Farbschema", "Theme"), Foreground = Ui.Brush("TextDim"), Margin = new Thickness(0, 16, 0, 8) });
        panel.Children.Add(Ui.Segments([(ThemeChoice.System, "System"), (ThemeChoice.Dark, Loc.T("Dunkel", "Dark")), (ThemeChoice.Light, Loc.T("Hell", "Light"))],
            prefs.Theme, theme => Change(prefs with { Theme = theme }), "UiTheme"));
        panel.Children.Add(Ui.Hint(Loc.T("„System“ folgt der Windows-Einstellung für den App-Modus.", "“System” follows the Windows app mode setting."), 10));
        return panel;
    }

    /// <summary>Hardwarezugriff: Zustand von PawnIO, Einrichtung.</summary>
    StackPanel HardwareCard()
    {
        var panel = new StackPanel();
        panel.Children.Add(Ui.Section(Loc.T("Hardwarezugriff", "Hardware access")));
        string? version = Borealis.Core.Hardware.PawnIo.InstalledVersion();
        panel.Children.Add(new TextBlock
        {
            Text = version != null
                ? Loc.T($"PawnIO {version} ist eingerichtet.", $"PawnIO {version} is set up.")
                : Loc.T("PawnIO ist nicht eingerichtet.", "PawnIO is not set up."),
            Foreground = version != null ? Ui.Brush("Good") : Ui.Brush("Warn"), FontWeight = FontWeights.SemiBold,
        });
        panel.Children.Add(Ui.Hint(Loc.T(
            "Borealis liest und steuert Lüfter, Temperaturen und RAM-Beleuchtung über den offenen, signierten Treiber PawnIO (pawnio.eu). " +
            "Mainboard- und ARGB-Beleuchtung laufen direkt über USB und brauchen ihn nicht.",
            "Borealis reads and controls fans, temperatures and memory lighting through the open, signed PawnIO driver (pawnio.eu). " +
            "Motherboard and ARGB lighting run directly over USB and do not need it."), 6));
        if (version == null)
        {
            var setup = new Button { Content = Loc.T("PawnIO einrichten", "Set up PawnIO"), Style = Ui.Style("AccentButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
            var result = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Foreground = Ui.Brush("TextDim") };
            setup.Click += async (_, _) =>
            {
                setup.IsEnabled = false;
                setup.Content = Loc.T("Wird eingerichtet …", "Setting up …");
                var (ok, message) = await PawnIoSetup.InstallAsync();
                if (ok) { Rebuild(); return; }
                setup.IsEnabled = true;
                setup.Content = Loc.T("PawnIO einrichten", "Set up PawnIO");
                result.Text = message;
            };
            panel.Children.Add(setup);
            panel.Children.Add(result);
            panel.Children.Add(Ui.Hint(Loc.T("Lädt den offiziellen Installer von GitHub, prüft ihn und installiert still; Windows fragt nach Administratorrechten.",
                                             "Downloads the official installer from GitHub, verifies it and installs silently; Windows asks for administrator rights."), 6));
        }
        return panel;
    }
}
