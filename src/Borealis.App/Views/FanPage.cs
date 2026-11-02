using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Borealis.App.Controls;
using Borealis.Core.Hardware;
using Borealis.Core.Ipc;
using Borealis.Core.Profiles;

namespace Borealis.App.Views;

/// <summary>
/// „Lüftersteuerung“: Drehzahl und Leistung je Lüfteranschluss sowie die Temperaturen, jede Sekunde aktualisiert.
/// Die Werte liest der Dienst über den ASUS-IO-Treiber wie Armoury Crate; die Kurven schreibt er in den Lüfter-Chip.
/// </summary>
sealed class FanPage : UserControl, IPage
{
    static AppState S => AppState.Instance;
    public string Title => Loc.T("Lüftersteuerung", "Fan control");

    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly Dictionary<string, FanCard> fanCards = new();
    readonly Dictionary<string, TempCard> tempCards = new();
    readonly ContentControl controls = new() { Focusable = false };
    readonly FanCurveEditor editor = new() { Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
    string layoutKey = "";
    bool polling, sending;
    SensorSnapshot? last;
    // Arbeitskopie der Lüftereinstellungen; wird nach jeder Antwort des Dienstes neu aus dem Status geholt
    FanSettings working = new();
    static string selectedFan = BoardFans.Headers[0].Id;

    public FanPage()
    {
        timer.Tick += async (_, _) => await PollAsync();
        IsVisibleChanged += async (_, e) =>
        {
            if ((bool)e.NewValue) { BuildControls(); timer.Start(); await PollAsync(); }
            else timer.Stop();
        };
        editor.CurveCommitted += async curve =>
        {
            working.SetCurve(selectedFan, curve);
            await SendAsync();
        };
        Rebuild();
    }

    public void Rebuild()
    {
        layoutKey = "";
        BuildControls();
        Show(last);
    }

    async Task PollAsync()
    {
        if (polling) return;
        polling = true;
        try
        {
            var (sensors, tune) = await S.SensorsAsync();
            Show(last = sensors);
            UpdateTune(tune);
        }
        finally { polling = false; }
    }

    async Task SendAsync()
    {
        if (sending) return;
        sending = true;
        try
        {
            var f = working.Clone();
            await S.UpdateSettingsAsync(s => s.Fans = f);
        }
        finally { sending = false; }
        BuildControls();
    }

    static bool ShowAll => S.Status?.Settings.ShowUnconnectedFans == true;

    /// <summary>Lüfter anzeigen? Nicht angeschlossene nur mit der Einstellung „Nicht angeschlossene Lüfter anzeigen“.</summary>
    static bool Visible(FanSensor f) => f.Connected || ShowAll;

    /// <summary>Anschluss in der Kurvenauswahl zeigen (ohne Messung alle).</summary>
    bool HeaderVisible(FanHeader h) => last?.Fans.FirstOrDefault(f => f.Id == h.Id) is not { } f || Visible(f);

    void Show(SensorSnapshot? s)
    {
        string key = s == null ? "-" : string.Join(",", s.Fans.Where(Visible).Select(f => f.Id)) + "|" + string.Join(",", s.Temperatures.Select(t => t.Id));
        if (key != layoutKey)
        {
            // Auch die Kurvenauswahl hängt davon ab, welche Lüfter angeschlossen sind
            if (layoutKey != "") BuildControls();
            Build(s, key);
        }
        if (s == null) return;
        foreach (var f in s.Fans) if (fanCards.TryGetValue(f.Id, out var c)) c.Update(f);
        foreach (var t in s.Temperatures) if (tempCards.TryGetValue(t.Id, out var c)) c.Update(t);
        editor.CurrentTemp = s.Temperatures.FirstOrDefault(t => t.Id == "tctl")?.Celsius;
        footer.Text = (s.Source != null ? Loc.T($"Quelle: {s.Source}", $"Source: {s.Source.Replace(" über ", " via ")}") : "") + Loc.T($" · aktualisiert {s.Time:HH:mm:ss}", $" · updated {s.Time:HH:mm:ss}");
        error.Text = s.Error ?? "";
        error.Visibility = s.Error == null ? Visibility.Collapsed : Visibility.Visible;
    }

    readonly TextBlock footer = new() { Style = (Style)Application.Current.FindResource("Hint"), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
    readonly TextBlock error = new() { Foreground = (Brush)Application.Current.FindResource("Warn"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14), Visibility = Visibility.Collapsed };

    /// <summary>
    /// Regelung: Übernahme vom ASUS-Lüfterdienst, Modus (Leise … Benutzerdefiniert) und Kurve je Lüfter.
    /// </summary>
    void BuildControls()
    {
        var status = S.Status;
        working = (status?.Settings.Fans ?? new FanSettings()).Clone();
        bool on = working.Enabled;
        var panel = new StackPanel();

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { Margin = new Thickness(0, 0, 24, 0) };
        text.Children.Add(new TextBlock { Text = Loc.T("Lüfter mit Borealis steuern", "Control fans with Borealis"), FontSize = 16, FontWeight = FontWeights.SemiBold });
        bool asusFan = S.AsusFanServiceInstalled;
        text.Children.Add(new TextBlock
        {
            Text = Loc.T("Borealis schreibt die Kurven in den Lüfter-Chip; der regelt danach selbst nach der CPU-Temperatur, auch wenn " +
                         "Borealis nicht läuft." + (asusFan ? " Der ASUS-Lüfterdienst wird dafür angehalten." : ""),
                         "Borealis writes the curves into the fan chip, which then regulates on its own by CPU temperature, even when " +
                         "Borealis is not running." + (asusFan ? " The ASUS fan service is stopped for this." : "")),
            Foreground = Ui.Brush("TextDim"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
        });
        head.Children.Add(text);
        var toggle = new CheckBox { Style = Ui.Style("Switch"), IsChecked = on, VerticalAlignment = VerticalAlignment.Center };
        toggle.Click += async (_, _) =>
        {
            working.Enabled = toggle.IsChecked == true;
            // Beim ersten Einschalten die Kurven übernehmen, die ASUS gerade im Chip hat (der Dienst liest sie aus)
            if (working.Enabled && working.Curves.Count == 0) working.Mode = FanMode.Custom;
            await SendAsync();
        };
        Grid.SetColumn(toggle, 1);
        head.Children.Add(toggle);
        panel.Children.Add(head);

        var state = new TextBlock { Style = Ui.Style("Hint"), Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap };
        state.Text = asusFan
            ? Loc.T($"ASUS-Lüfterdienst: {ServiceState(status?.AsusFanService)}", $"ASUS fan service: {ServiceState(status?.AsusFanService)}") +
              (on ? Loc.T(" · Borealis steuert die Lüfter.", " · Borealis controls the fans.") : Loc.T(" · Die Lüfter regelt noch ASUS.", " · ASUS still controls the fans."))
            : on ? Loc.T("Borealis steuert die Lüfter.", "Borealis controls the fans.")
            : Loc.T("Aus: Es gelten die zuletzt geschriebenen Kurven, nach einem Neustart regelt das BIOS.",
                    "Off: the last written curves stay active; after a restart the BIOS controls the fans.");
        panel.Children.Add(state);
        var problem = status?.FanError ?? (on ? S.LastError : null);
        if (problem != null) panel.Children.Add(new TextBlock { Text = problem, Foreground = Ui.Brush("Warn"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });


        panel.Children.Add(Ui.Section(Loc.T("Modus", "Mode"), 20));
        var modes = Ui.Segments(Enum.GetValues<FanMode>().Select(m => (m, FanSettings.DisplayName(m))), working.Mode, async m =>
        {
            working.Mode = m;
            await SendAsync();
        }, "FanMode");
        modes.IsEnabled = on;
        panel.Children.Add(modes);

        panel.Children.Add(Ui.Section(Loc.T("Kurve", "Curve"), 20));
        var hint = Ui.Hint("", 8);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var copy = new Button { Content = Loc.T("Auf alle Lüfter übertragen", "Copy to all fans"), Margin = new Thickness(0, 0, 10, 0) };
        var reset = new Button();
        var fanRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var headers = BoardFans.Headers.Where(HeaderVisible).ToList();
        if (headers.Count == 0) headers = BoardFans.Headers.ToList();
        if (headers.All(h => h.Id != selectedFan)) selectedFan = headers[0].Id;
        foreach (var h in headers)
        {
            var b = new RadioButton { Style = Ui.Style("Segment"), GroupName = "FanSelect", Content = Loc.Term(h.Name), IsChecked = h.Id == selectedFan, Margin = new Thickness(0, 0, 0, 6) };
            var id = h.Id;
            b.Checked += (_, _) => { selectedFan = id; ShowCurve(); };
            fanRow.Children.Add(b);
        }
        panel.Children.Add(fanRow);
        (editor.Parent as Panel)?.Children.Remove(editor);
        panel.Children.Add(editor);
        panel.Children.Add(hint);

        copy.Click += async (_, _) =>
        {
            var curve = working.CurveFor(Header());
            foreach (var h in BoardFans.Headers.Where(h => !h.Pump && h.Id != selectedFan))
                working.SetCurve(h.Id, curve.Select(p => new FanPoint(p.Temp, p.Duty)).ToList());
            await SendAsync();
        };
        reset.Click += async (_, _) =>
        {
            working.ResetCurve(Header());
            await SendAsync();
        };
        buttons.Children.Add(copy);
        buttons.Children.Add(reset);
        panel.Children.Add(buttons);

        void ShowCurve()
        {
            var h = Header();
            bool custom = working.Mode == FanMode.Custom;
            var tuning = working.Tuning.GetValueOrDefault(h.Id);
            bool tuned = FanSettings.IsTuned(working.Mode, tuning);
            bool editable = FanSettings.IsEditable(working.Mode);
            var own = working.ProfileOverride(working.Mode, h.Id);
            editor.SetCurve(working.CurveFor(h), editable: on && editable);
            editor.Tuning = tuning;
            // Gestrichelt zum Vergleich: bei eigener Anpassung die berechnete Kurve, sonst dieselbe ohne Messung
            editor.SetReference(own != null ? FanSettings.Preset(working.Mode, h, tuning)
                : tuned ? FanSettings.Normalize(FanSettings.Preset(working.Mode, h)) : null);
            buttons.Visibility = on && editable ? Visibility.Visible : Visibility.Collapsed;
            reset.Content = custom ? Loc.T("Auf Standard zurücksetzen", "Reset to default") : Loc.T("Profil zurücksetzen", "Reset profile");
            reset.IsEnabled = custom || own != null;
            string mode = FanSettings.DisplayName(working.Mode);
            string who = h.Pump ? Loc.T("diese Pumpe", "this pump") : Loc.T("diesen Lüfter", "this fan");
            string drag = Loc.T("Punkte ziehen: Temperatur nach links/rechts, Leistung nach oben/unten; der letzte Punkt steht bei 100 %.",
                                "Drag points: temperature left/right, duty up/down; the last point stays at 100 %.");
            hint.Text = custom ? drag
                : working.Mode == FanMode.FullSpeed ? Loc.T("Volle Leistung bei jeder Temperatur.", "Full duty at any temperature.")
                : own != null ? Loc.T($"„{mode}“ für {who} von dir angepasst; gestrichelt: die berechnete Kurve. „Profil zurücksetzen“ holt sie zurück.",
                                      $"“{mode}” adjusted by you for {who}; dashed: the calculated curve. “Reset profile” brings it back.")
                : tuned ? Loc.T($"„{mode}“ aus dem Auto-Tuning ({tuning!.Time:dd.MM. HH:mm}) für {who} angepasst; gestrichelt: ohne Messung. {drag} Die Änderung gilt nur für {who} in „{mode}“.",
                                $"“{mode}” adapted to {who} by auto tuning ({tuning!.Time:MM/dd HH:mm}); dashed: without measurement. {drag} The change only applies to {who} in “{mode}”.")
                : Loc.T($"Feste Kurve des Modus „{mode}“ (ohne Messung). {drag} Die Änderung gilt nur für {who} in „{mode}“.",
                        $"Fixed curve of the “{mode}” mode (without measurement). {drag} The change only applies to {who} in “{mode}”.");
            if (!on) hint.Text = Loc.T("Vorschau. Die Kurven gelten erst, wenn Borealis die Lüfter steuert. ", "Preview. The curves only apply once Borealis controls the fans. ") + hint.Text;
        }
        ShowCurve();

        BuildTuning(panel, on);
        controls.Content = Ui.Card(panel, new Thickness(0, 0, 14, 22));
    }

    readonly Grid tuneTrack = new() { Width = 260, Margin = new Thickness(16, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly Border tuneBar = new() { Height = 4, CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left };
    readonly TextBlock tuneText = new() { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    // Nicht fokussierbar: sonst löst nach dem Start ein Tastendruck (Leertaste/Enter) versehentlich „Abbrechen“ aus
    readonly Button tuneButton = new() { MinWidth = 170, Focusable = false };
    // Knöpfe „Messen“ je Lüfter (während eines Laufs gesperrt)
    readonly List<Button> measureButtons = [];
    bool tuneRunning, tuneHandlerSet;

    /// <summary>Auto-Tuning: Start/Abbruch, Fortschritt (live über <see cref="UpdateTune"/>) und Ergebnisse je Lüfter.</summary>
    void BuildTuning(StackPanel panel, bool on)
    {
        panel.Children.Add(Ui.Section("Auto-Tuning", 24));
        panel.Children.Add(new TextBlock
        {
            Text = Loc.T("Misst Drehzahl und Mindestleistung jedes Lüfters und passt Leise, Standard und Turbo daran an.",
                         "Measures the speed and minimum duty of each fan and adapts Silent, Standard and Turbo to it."),
            Foreground = Ui.Brush("TextDim"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
        });
        if (tuneTrack.Children.Count == 0)
        {
            tuneTrack.Children.Add(new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Ui.Brush("Bg3") });
            tuneBar.Background = Ui.Brush("Accent");
            tuneTrack.Children.Add(tuneBar);
            tuneText.Foreground = Ui.Brush("TextDim");
            tuneButton.Style = Ui.Style("AccentButton");
        }
        if (!tuneHandlerSet)
        {
            tuneHandlerSet = true;
            tuneButton.Click += async (_, _) =>
            {
                if (tuneRunning)
                {
                    if (MessageBox.Show(Window.GetWindow(this)!, Loc.T("Auto-Tuning abbrechen?\n\nBereits fertig gemessene Lüfter bleiben gespeichert.", "Cancel auto tuning?\n\nFans that are already measured stay saved."), "Borealis",
                            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                        await S.FanTuneAsync("cancel");
                    return;
                }
                if (MessageBox.Show(Window.GetWindow(this)!, Loc.T("Auto-Tuning für alle Lüfter starten?\n\nDauert etwa 6–8 Minuten; die Lüfter drehen dabei hörbar hoch und runter.", "Start auto tuning for all fans?\n\nTakes about 6–8 minutes; the fans audibly spin up and down."), "Borealis",
                        MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
                await S.FanTuneAsync("start");
                if (S.LastError != null) tuneText.Text = S.LastError;
            };

        }
        tuneButton.IsEnabled = on || tuneRunning;
        tuneButton.ToolTip = on ? null : Loc.T("Erst „Lüfter mit Borealis steuern“ einschalten.", "Turn on “Control fans with Borealis” first.");
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var e in new FrameworkElement[] { tuneButton, tuneTrack, tuneText }) (e.Parent as Panel)?.Children.Remove(e);
        row.Children.Add(tuneButton);
        row.Children.Add(tuneTrack);
        row.Children.Add(tuneText);
        panel.Children.Add(row);
        UpdateTuneVisuals(null);

        // Je Lüfter: Ergebnis und eigener Knopf „Messen“
        measureButtons.Clear();
        var list = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        foreach (var h in BoardFans.Headers.Where(HeaderVisible))
        {
            var line = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            line.ColumnDefinitions.Add(new ColumnDefinition());
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.Children.Add(new TextBlock { Text = Loc.Term(h.Name), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            var result = new TextBlock { Text = TuningText(h), Foreground = Ui.Brush("TextDim"), FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            Grid.SetColumn(result, 1);
            line.Children.Add(result);
            var measure = new Button { Content = Loc.T("Tuning starten", "Start tuning"), Focusable = false, MinWidth = 120 };
            var header = h;
            measure.Click += async (_, _) =>
            {
                if (MessageBox.Show(Window.GetWindow(this)!, Loc.T($"Tuning für „{Loc.Term(header.Name)}“ starten?\n\nDauert etwa 2–3 Minuten.", $"Start tuning for “{Loc.Term(header.Name)}”?\n\nTakes about 2–3 minutes."),
                        "Borealis", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
                await S.FanTuneAsync("only:" + header.Id);
                if (S.LastError != null) tuneText.Text = S.LastError;
            };
            Grid.SetColumn(measure, 2);
            line.Children.Add(measure);
            measureButtons.Add(measure);
            list.Children.Add(line);
        }
        panel.Children.Add(list);
    }

    /// <summary>Messergebnis eines Lüfters als eine Zeile.</summary>
    string TuningText(FanHeader h)
    {
        if (!working.Tuning.TryGetValue(h.Id, out var t)) return Loc.T("noch kein Tuning", "not tuned yet");
        string rpmUnit = Loc.T("U/min", "RPM");
        string run = t.MinDuty > 0 ? Loc.T($"läuft sicher ab {t.MinDuty} %", $"runs safely from {t.MinDuty} %") + (t.MinDutyRpm > 0 ? $" ({t.MinDutyRpm:N0} {rpmUnit})" : "")
            : t.EffectiveDuty > 0 ? Loc.T($"bleibt nie stehen (Grunddrehzahl {t.Rpm[0]:N0} U/min), wird ab {t.EffectiveDuty} % schneller",
                                          $"never stops (base speed {t.Rpm[0]:N0} RPM), gets faster from {t.EffectiveDuty} %")
            : Loc.T("bleibt nie stehen", "never stops");
        string start = t.StartDuty > 0 ? Loc.T($", läuft aus dem Stand an ab {t.StartDuty} %", $", starts from standstill at {t.StartDuty} %") : "";
        string rough = t.Rpm.Length < 21 ? Loc.T(", alte Messung", ", old measurement") : "";
        return Loc.T($"{run}{start}, höchstens {t.MaxRpm:N0} U/min (Tuning {t.Time:dd.MM. HH:mm}{rough})",
                     $"{run}{start}, at most {t.MaxRpm:N0} RPM (tuning {t.Time:MM/dd HH:mm}{rough})");
    }

    /// <summary>Zustand eines Dienstes vom Borealis-Dienst (deutsch) für die Anzeige.</summary>
    static string ServiceState(string? state) => state switch
    {
        null => "?",
        "läuft" => Loc.T("läuft", "running"),
        "angehalten" => Loc.T("angehalten", "stopped"),
        "nicht installiert" => Loc.T("nicht installiert", "not installed"),
        _ => state,
    };

    /// <summary>Fortschrittstexte des Auto-Tunings kommen deutsch vom Dienst; für Englisch übersetzen.</summary>
    static string TuneText(string message)
    {
        if (!Loc.English) return message;
        (string Pattern, Func<System.Text.RegularExpressions.Match, string> Make)[] rules =
        [
            (@"^(.+): 100 %$", m => $"{Loc.Term(m.Groups[1].Value)}: 100 %"),
            (@"^(.+): Drehzahl bei (\d+) % messen$", m => $"{Loc.Term(m.Groups[1].Value)}: measuring speed at {m.Groups[2].Value} %"),
            (@"^(.+): fertig$", m => $"{Loc.Term(m.Groups[1].Value)}: done"),
            (@"^Mindestleistung suchen \(−(\d+) %\)$", m => $"Searching minimum duty (−{m.Groups[1].Value} %)"),
            (@"^Mindestleistung suchen$", _ => "Searching minimum duty"),
            (@"^Mindestleistung prüfen$", _ => "Checking minimum duty"),
            (@"^Lüfter anhalten$", _ => "Stopping fans"),
            (@"^Anlaufen prüfen \((\d+) %\)$", m => $"Checking start-up ({m.Groups[1].Value} %)"),
            (@"^Anlaufen genau prüfen$", _ => "Checking start-up precisely"),
            (@"^Startet …$", _ => "Starting …"),
            (@"^Fertig\.$", _ => "Done."),
            (@"^Abgebrochen\.$", _ => "Cancelled."),
            (@"^Pumpe nicht abgestimmt: abgebrochen\.$", _ => "Pump not tuned: cancelled."),
            (@"^(?:Pumpe nicht abgestimmt: )?CPU-Temperatur (\d+) °C über (\d+) °C – abgebrochen\.$", m => $"CPU temperature {m.Groups[1].Value} °C above {m.Groups[2].Value} °C – cancelled."),
        ];
        foreach (var (pattern, make) in rules)
        {
            var m = System.Text.RegularExpressions.Regex.Match(message, pattern);
            if (m.Success) return make(m);
        }
        return message;
    }

    FanTuneStatus? lastTune;

    /// <summary>Fortschritt aus jeder Sensorabfrage; nach dem Ende Einstellungen neu laden (Ergebnisse, Kurven).</summary>
    async void UpdateTune(FanTuneStatus? t)
    {
        bool running = t?.Running == true;
        bool finished = tuneRunning && !running;
        tuneRunning = running;
        lastTune = t;
        UpdateTuneVisuals(t);
        if (finished)
        {
            await S.RefreshAsync(false);
            BuildControls();
            UpdateTuneVisuals(t);
        }
    }

    void UpdateTuneVisuals(FanTuneStatus? t)
    {
        t ??= lastTune;
        bool running = t?.Running == true;
        tuneButton.Content = running ? Loc.T("Abbrechen", "Cancel") : Loc.T("Auto-Tuning starten", "Start auto tuning");
        tuneButton.IsEnabled = running || working.Enabled;
        foreach (var b in measureButtons) b.IsEnabled = !running && working.Enabled;
        tuneTrack.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        tuneBar.Width = tuneTrack.Width * Math.Clamp(t?.Percent ?? 0, 0, 100) / 100.0;
        tuneText.Text = running ? $"{t!.Percent} % · {TuneText(t.Message)}" : t?.Error is { } err ? TuneText(err) : (t?.Message == "Fertig." ? TuneText("Fertig.") : "");
        tuneText.Margin = new Thickness(running ? 0 : 16, 0, 0, 0);
    }

    static FanHeader Header() => BoardFans.Find(selectedFan) ?? BoardFans.Headers[0];

    void Build(SensorSnapshot? s, string key)
    {
        layoutKey = key;
        fanCards.Clear();
        tempCards.Clear();
        // Wiederverwendete Teile erst aus dem alten Aufbau lösen
        foreach (var t in new FrameworkElement[] { error, footer, controls }) (t.Parent as Panel)?.Children.Remove(t);
        var root = new StackPanel { MaxWidth = 972, HorizontalAlignment = HorizontalAlignment.Left };
        root.Children.Add(new TextBlock
        {
            Text = Loc.T("Drehzahlen und Temperaturen live vom Lüfter-Chip, dazu Modus und Kurven der Regelung.", "Live fan speeds and temperatures from the fan chip, plus mode and curves for regulation."),
            Foreground = Ui.Brush("TextDim"), FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 22),
        });
        root.Children.Add(error);
        root.Children.Add(controls);

        if (s == null || (s.Fans.Count == 0 && s.Temperatures.Count == 0))
        {
            if (!PawnIoSetup.IsInstalled)
            {
                root.Children.Add(Ui.Hint(Loc.T("Für Lüfter und Temperaturen braucht Borealis den Treiber PawnIO.",
                                                "Borealis needs the PawnIO driver for fans and temperatures.")));
                var setup = new Button { Content = Loc.T("PawnIO einrichten", "Set up PawnIO"), Style = Ui.Style("AccentButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
                var result = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Foreground = Ui.Brush("TextDim") };
                setup.Click += async (_, _) =>
                {
                    setup.IsEnabled = false;
                    setup.Content = Loc.T("Wird eingerichtet …", "Setting up …");
                    var (ok, message) = await PawnIoSetup.InstallAsync();
                    result.Text = message;
                    setup.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
                    setup.IsEnabled = true;
                    setup.Content = Loc.T("PawnIO einrichten", "Set up PawnIO");
                };
                root.Children.Add(setup);
                root.Children.Add(result);
            }
            else root.Children.Add(Ui.Hint(s?.Error ?? Loc.T("Keine Verbindung zum Borealis-Dienst.", "No connection to the Borealis service.")));
            Content = Ui.Scroll(root);
            return;
        }

        root.Children.Add(Heading(Loc.T("Lüfter", "Fans")));
        var fans = new WrapPanel();
        foreach (var f in s.Fans.Where(Visible))
        {
            var card = new FanCard(Loc.Term(f.Name));
            fanCards[f.Id] = card;
            fans.Children.Add(card.Root);
        }
        root.Children.Add(fans);

        root.Children.Add(Heading(Loc.T("Temperaturen", "Temperatures"), 12));
        var temps = new WrapPanel();
        foreach (var t in s.Temperatures)
        {
            var card = new TempCard(Loc.Term(t.Name));
            tempCards[t.Id] = card;
            temps.Children.Add(card.Root);
        }
        root.Children.Add(temps);
        root.Children.Add(footer);
        Content = Ui.Scroll(root);
    }

    static TextBlock Heading(string text, double top = 0) =>
        new() { Text = text, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, top, 0, 10) };

    /// <summary>Karte eines Lüfteranschlusses: Symbol (dreht sich, solange der Lüfter läuft), Drehzahl, Leistung.</summary>
    sealed class FanCard
    {
        public Border Root { get; }
        readonly System.Windows.Shapes.Path icon;
        readonly RotateTransform spin = new();
        readonly TextBlock rpm = new() { FontSize = 26, FontWeight = FontWeights.SemiBold };
        readonly TextBlock unit = new() { Text = Loc.T(" U/min", " RPM"), Foreground = Ui.Brush("TextMute"), FontSize = 13, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 5) };
        readonly TextBlock duty = new() { Foreground = Ui.Brush("TextDim"), FontSize = 12 };
        readonly Border bar = new() { Height = 4, CornerRadius = new CornerRadius(2), Background = Ui.Brush("Accent"), HorizontalAlignment = HorizontalAlignment.Left };
        bool? spinning;

        public FanCard(string name)
        {
            var panel = new StackPanel();
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
            icon = Ui.Icon(Icons.Resource("IconFan"), 22, Ui.Brush("TextDim"));
            icon.RenderTransformOrigin = new Point(0.5, 0.5);
            icon.RenderTransform = spin;
            icon.Margin = new Thickness(0, 0, 10, 0);
            head.Children.Add(icon);
            head.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            panel.Children.Add(head);
            var value = new StackPanel { Orientation = Orientation.Horizontal };
            value.Children.Add(rpm);
            value.Children.Add(unit);
            panel.Children.Add(value);
            var track = new Grid { Margin = new Thickness(0, 10, 0, 6) };
            track.Children.Add(new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Ui.Brush("Bg3") });
            track.Children.Add(bar);
            track.SizeChanged += (_, _) => bar.Width = track.ActualWidth * barFraction;
            panel.Children.Add(track);
            panel.Children.Add(duty);
            Root = Ui.Card(panel, new Thickness(0, 0, 14, 14));
            Root.Width = 226;
            trackGrid = track;
        }

        readonly Grid trackGrid;
        double barFraction;

        public void Update(FanSensor f)
        {
            bool running = f.Rpm is > 0;
            rpm.Text = f.Rpm is { } r ? (r > 0 ? r.ToString("N0") : "–") : "?";
            unit.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
            duty.Text = running ? Loc.T($"Leistung {f.Duty} %", $"Duty {f.Duty} %") : Loc.T("steht oder nicht angeschlossen", "stopped or not connected");
            barFraction = running ? f.Duty / 100.0 : 0;
            bar.Width = trackGrid.ActualWidth * barFraction;
            Root.Opacity = running ? 1 : 0.55;
            if (spinning != running)
            {
                spinning = running;
                icon.Stroke = running ? Ui.Brush("Accent") : Ui.Brush("TextDim");
                if (running)
                    spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.6)) { RepeatBehavior = RepeatBehavior.Forever });
                else spin.BeginAnimation(RotateTransform.AngleProperty, null);
            }
        }
    }

    /// <summary>Karte eines Temperaturfühlers; die Farbe zeigt grob, wie warm es ist.</summary>
    sealed class TempCard
    {
        public Border Root { get; }
        readonly TextBlock value = new() { FontSize = 26, FontWeight = FontWeights.SemiBold };
        readonly Border dot = new() { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };

        public TempCard(string name)
        {
            var panel = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            head.Children.Add(dot);
            head.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            panel.Children.Add(head);
            panel.Children.Add(value);
            Root = Ui.Card(panel, new Thickness(0, 0, 14, 14));
            Root.Width = 226;
        }

        public void Update(TemperatureSensor t)
        {
            value.Text = $"{t.Celsius:0.0} °C";
            dot.Background = t.Celsius >= 85 ? Ui.Brush("Accent") : t.Celsius >= 65 ? Ui.Brush("Warn") : Ui.Brush("Good");
        }
    }
}
