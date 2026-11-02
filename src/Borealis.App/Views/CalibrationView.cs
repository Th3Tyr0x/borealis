using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Borealis.App.Controls;
using Borealis.Core.Lighting;
using Borealis.Core.Profiles;

namespace Borealis.App.Views;

/// <summary>
/// "Color calibration" tab: red/green/blue gain and channel order per zone so all devices show the same color.
/// Reiter „Farbabgleich“: Verstärkung von Rot/Grün/Blau und Kanalreihenfolge je Zone, damit alle Geräte gleich aussehen.
/// </summary>
sealed class CalibrationView : UserControl
{
    static AppState S => AppState.Instance;

    static (string Name, Rgb Color)[] References =>
    [
        (Loc.T("Weiß", "White"), Rgb.White), (Loc.T("Rot", "Red"), new Rgb(255, 0, 0)), (Loc.T("Grün", "Green"), new Rgb(0, 255, 0)),
        (Loc.T("Blau", "Blue"), new Rgb(0, 0, 255)), (Loc.T("Gelb", "Yellow"), new Rgb(255, 255, 0)), ("Cyan", new Rgb(0, 255, 255)),
        ("Magenta", new Rgb(255, 0, 255)), ("Orange", new Rgb(255, 90, 0)),
    ];

    static (string Kind, string Name)[] Devices => [("Mainboard", Loc.T("Mainboard", "Motherboard")), ("Argb", "Addressable LED Strip"), ("Ram", Loc.T("Arbeitsspeicher", "Memory"))];

    // Kept across rebuilds (page changes) / Bleibt über Neuaufbauten erhalten
    static Rgb reference = Rgb.White;
    static readonly Dictionary<string, bool> linked = new();

    Dictionary<string, ZoneCalibration> cal = new();
    bool previewOn;
    /// <summary>Something changed or calibration mode was used since the tab opened.
    /// Seit dem Öffnen des Reiters geändert oder Abgleich-Modus benutzt.</summary>
    bool touched;
    readonly List<Action> refreshSent = [];

    public CalibrationView() => Rebuild();

    public void Rebuild()
    {
        cal = S.Calibration.ToDictionary(k => k.Key, k => k.Value.Clone());
        previewOn = S.Status?.CalibrationColor != null;
        if (previewOn && Rgb.TryParse(S.Status!.CalibrationColor, out var c)) reference = c;
        refreshSent.Clear();

        var root = new StackPanel { MaxWidth = 1120, HorizontalAlignment = HorizontalAlignment.Left };
        root.Children.Add(new TextBlock
        {
            Text = Loc.T("Gleichen Sie die Farben der Geräte aneinander an, damit dieselbe Farbe überall gleich aussieht. " +
                         "Der Abgleich gilt für alle Profile und Effekte.",
                         "Match the colors of your devices so the same color looks the same everywhere. The matching applies to all profiles and effects."),
            Foreground = Ui.Brush("TextDim"), FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 22),
        });
        root.Children.Add(ModeCard());

        foreach (var (kind, name) in Devices)
        {
            var zones = S.ZonesOf(kind).ToList();
            if (zones.Count > 0) root.Children.Add(DeviceSection(kind, name, zones));
        }
        if (S.Status?.Zones.Count is null or 0) root.Children.Add(Ui.Hint(Loc.T("Keine Geräte gefunden. Läuft der Borealis-Dienst?", "No devices found. Is the Borealis service running?")));

        var reset = new Button { Content = Loc.T("Gesamten Farbabgleich zurücksetzen", "Reset all color matching"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        reset.Click += (_, _) =>
        {
            cal.Clear();
            Send();
            Rebuild();
        };
        root.Children.Add(reset);
        root.Children.Add(Ui.Hint(
            Loc.T("Tipp: Mit Weiß beginnen und beim Gerät mit Farbstich den überwiegenden Kanal senken (bläuliches Weiß → Blau senken). " +
                  "Werte über 100 % wirken nur, wenn der Kanal nicht schon voll ist; für Weiß deshalb die anderen Kanäle senken. " +
                  "Danach mit Rot, Grün, Blau und Mischfarben prüfen.",
                  "Tip: Start with white and lower the dominant channel on the device with a color cast (bluish white → lower blue). " +
                  "Values above 100 % only help if the channel is not already at full; for white, lower the other channels instead. " +
                  "Then check with red, green, blue and mixed colors."), 14));
        root.Children.Add(Ui.Hint(
            Loc.T("Effekte, deren Farben der Controller selbst erzeugt (Farbzyklus oder Regenbogen mit „Hardware-Effekte bevorzugen“, " +
                  "Regenbogen nach dem Herunterfahren), lassen sich nicht abgleichen.",
                  "Effects whose colors the controller generates itself (color cycle or rainbow with “Prefer hardware effects”, " +
                  "rainbow after shutdown) cannot be matched."), 6));
        Content = Ui.Scroll(root);
    }

    /// <summary>On leaving the tab: end calibration mode and write the standby effect with the new calibration.
    /// Beim Verlassen: Abgleich-Modus beenden und den Standby-Effekt mit dem neuen Abgleich schreiben.</summary>
    public void Leave()
    {
        if (!previewOn && !touched) return;
        previewOn = false;
        touched = false;
        S.PreviewCalibration(null);
    }

    FrameworkElement ModeCard()
    {
        var panel = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { Margin = new Thickness(0, 0, 24, 0) };
        text.Children.Add(new TextBlock { Text = Loc.T("Abgleich-Modus", "Matching mode"), FontSize = 16, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock
        {
            Text = Loc.T("Alle Geräte leuchten in derselben Referenzfarbe, die Effekte pausieren. So lassen sich Farbstiche direkt vergleichen.",
                         "All devices light up in the same reference color and effects pause, so color casts can be compared directly."),
            Foreground = Ui.Brush("TextDim"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
        });
        head.Children.Add(text);
        var toggle = new CheckBox { Style = Ui.Style("Switch"), IsChecked = previewOn, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(toggle, 1);
        head.Children.Add(toggle);
        panel.Children.Add(head);

        panel.Children.Add(Ui.Section(Loc.T("Referenzfarbe", "Reference color"), 18));
        var swatches = new WrapPanel();
        var picker = new ColorPicker { Margin = new Thickness(0, 6, 0, 0) };
        var custom = new StackPanel { Visibility = Visibility.Collapsed };
        custom.Children.Add(picker);
        var buttons = new List<(ToggleButton Button, Rgb? Color)>();
        void Mark()
        {
            bool known = References.Any(r => r.Color == reference);
            foreach (var (b, c) in buttons) b.IsChecked = c == null ? !known : c == reference;
        }
        foreach (var (name, color) in References.Select(r => (r.Name, (Rgb?)r.Color)).Append((Loc.T("Eigene", "Custom"), null)))
        {
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            var dot = new Ellipse { Width = 14, Height = 14, Margin = new Thickness(0, 0, 8, 0), Stroke = Ui.Brush("Line"), VerticalAlignment = VerticalAlignment.Center };
            dot.Fill = color is { } c ? Ui.ToBrush(c) : RainbowBrush();
            content.Children.Add(dot);
            content.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            var b = new ToggleButton { Style = Ui.Style("Chip"), Content = content, Padding = new Thickness(10, 6, 12, 6), Margin = new Thickness(0, 0, 8, 8) };
            b.Click += (_, _) =>
            {
                if (color is { } c) { reference = c; custom.Visibility = Visibility.Collapsed; }
                else { custom.Visibility = custom.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; picker.SetColor(reference); }
                Mark();
                ReferenceChanged();
            };
            buttons.Add((b, color));
            swatches.Children.Add(b);
        }
        Mark();
        panel.Children.Add(swatches);
        panel.Children.Add(custom);
        picker.ColorChanged += c =>
        {
            reference = c;
            Mark();
            ReferenceChanged();
        };

        toggle.Click += (_, _) =>
        {
            previewOn = toggle.IsChecked == true;
            touched = true;
            S.PreviewCalibration(previewOn ? reference : null);
        };

        void ReferenceChanged()
        {
            foreach (var r in refreshSent) r();
            if (previewOn) S.PreviewCalibration(reference);
        }
        return Ui.Card(panel, new Thickness(0, 0, 0, 22));
    }

    FrameworkElement DeviceSection(string kind, string name, List<Core.Ipc.ZoneInfo> zones)
    {
        var section = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        bool isLinked = zones.Count > 1 && (linked.TryGetValue(kind, out var l) ? l : kind == "Ram" && AllEqual(zones));
        if (zones.Count > 1)
        {
            var linkSwitch = new CheckBox { Style = Ui.Style("Switch"), Content = Loc.T("Alle Zonen gleich", "All zones the same"), IsChecked = isLinked, VerticalAlignment = VerticalAlignment.Center };
            linkSwitch.Click += (_, _) =>
            {
                linked[kind] = linkSwitch.IsChecked == true;
                if (linked[kind])
                {
                    // Copy the first zone's values to all / Werte der ersten Zone für alle übernehmen
                    var first = Get(zones[0].Id);
                    foreach (var z in zones.Skip(1)) Set(z.Id, first.Clone());
                    Send();
                }
                Rebuild();
            };
            DockPanel.SetDock(linkSwitch, Dock.Right);
            head.Children.Add(linkSwitch);
        }
        head.Children.Add(new TextBlock { Text = name, FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        section.Children.Add(head);

        var cards = new WrapPanel();
        if (isLinked)
            cards.Children.Add(ZoneCard(zones.Count == 2 ? string.Join(" + ", zones.Select(S.LabelOf)) : Loc.T($"Alle {zones.Count} Zonen", $"All {zones.Count} zones"), zones));
        else
            foreach (var z in zones) cards.Children.Add(ZoneCard(S.LabelOf(z), [z]));
        section.Children.Add(cards);
        return section;
    }

    /// <summary>Sliders for one zone or several linked zones with equal values.
    /// Regler für eine Zone oder mehrere verknüpfte Zonen mit gleichen Werten.</summary>
    FrameworkElement ZoneCard(string title, List<Core.Ipc.ZoneInfo> zones)
    {
        var value = Get(zones[0].Id).Clone();
        var panel = new StackPanel { Width = 262 };

        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var sent = new Ellipse { Width = 18, Height = 18, Stroke = Ui.Brush("Line"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        DockPanel.SetDock(sent, Dock.Right);
        head.Children.Add(sent);
        head.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(head);

        void RefreshSent()
        {
            var c = value.Apply(reference);
            sent.Fill = Ui.ToBrush(c);
            sent.ToolTip = Loc.T($"Gesendet für die Referenzfarbe: #{c}", $"Sent for the reference color: #{c}");
        }
        void Changed()
        {
            foreach (var z in zones) Set(z.Id, value.Clone());
            RefreshSent();
            Send();
        }

        panel.Children.Add(ChannelRow("R", Color.FromRgb(0xFF, 0x4A, 0x4A), value.Red, v => { value.Red = v; Changed(); }));
        panel.Children.Add(ChannelRow("G", Color.FromRgb(0x4A, 0xE0, 0x6A), value.Green, v => { value.Green = v; Changed(); }));
        panel.Children.Add(ChannelRow("B", Color.FromRgb(0x4A, 0x8A, 0xFF), value.Blue, v => { value.Blue = v; Changed(); }));

        // Channel order only for USB zones; the RAM controller sorts itself
        // Kanalreihenfolge nur für USB-Zonen; der RAM-Controller sortiert selbst
        if (zones.All(z => z.Kind != "Ram"))
        {
            var orderRow = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var label = new TextBlock { Text = Loc.T("Reihenfolge", "Order"), Foreground = Ui.Brush("TextDim"), VerticalAlignment = VerticalAlignment.Center, Width = 96 };
            orderRow.Children.Add(label);
            var order = new ComboBox { Width = 150, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = Loc.T("Zeigt das Gerät bei Rot eine andere Farbe, die Reihenfolge wechseln, bis Rot, Grün und Blau stimmen.", "If the device shows a different color for red, change the order until red, green and blue are right.") };
            foreach (var o in ZoneCalibration.Orders) order.Items.Add(o == "RGB" ? Loc.T("RGB (Standard)", "RGB (default)") : o);
            order.SelectedIndex = Math.Max(0, Array.IndexOf(ZoneCalibration.Orders, value.Order));
            order.SelectionChanged += (_, _) =>
            {
                value.Order = ZoneCalibration.Orders[Math.Max(0, order.SelectedIndex)];
                Changed();
            };
            orderRow.Children.Add(order);
            panel.Children.Add(orderRow);
        }

        var reset = new Button { Style = Ui.Style("LinkButton"), Content = Loc.T("Zurücksetzen", "Reset"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
        reset.Click += (_, _) =>
        {
            foreach (var z in zones) cal.Remove(z.Id);
            Send();
            Rebuild();
        };
        panel.Children.Add(reset);

        RefreshSent();
        refreshSent.Add(RefreshSent);
        return Ui.Card(panel, new Thickness(0, 0, 14, 14));
    }

    static FrameworkElement ChannelRow(string name, Color color, int value, Action<int> set)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
        row.Children.Add(new TextBlock { Text = name, Foreground = new SolidColorBrush(color), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var slider = new Slider { Minimum = 0, Maximum = 150, TickFrequency = 1, IsSnapToTickEnabled = true, Value = Math.Clamp(value, 0, 150), VerticalAlignment = VerticalAlignment.Center,
                                  Foreground = new SolidColorBrush(color), ToolTip = Loc.T("Doppelklick: 100 %", "Double-click: 100 %") };
        Grid.SetColumn(slider, 1);
        row.Children.Add(slider);
        var text = new TextBlock { Text = $"{value} %", Foreground = Ui.Brush("TextDim"), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(text, 2);
        row.Children.Add(text);
        slider.ValueChanged += (_, e) =>
        {
            text.Text = $"{(int)e.NewValue} %";
            set((int)e.NewValue);
        };
        // Double-click resets the channel to 100 % / Doppelklick setzt den Kanal auf 100 % zurück
        slider.MouseDoubleClick += (_, _) => slider.Value = 100;
        return row;
    }

    ZoneCalibration Get(string zoneId) => cal.TryGetValue(zoneId, out var c) ? c : new ZoneCalibration();

    void Set(string zoneId, ZoneCalibration c)
    {
        if (c.IsNeutral) cal.Remove(zoneId);
        else cal[zoneId] = c;
    }

    bool AllEqual(List<Core.Ipc.ZoneInfo> zones)
    {
        var first = Get(zones[0].Id);
        return zones.All(z => Get(z.Id) is var c && c.Red == first.Red && c.Green == first.Green && c.Blue == first.Blue && c.Order == first.Order);
    }

    void Send()
    {
        touched = true;
        S.SetCalibration(cal);
    }

    static Brush RainbowBrush() => new LinearGradientBrush
    {
        StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
        GradientStops =
        {
            new GradientStop(Color.FromRgb(0xFF, 0x30, 0x30), 0), new GradientStop(Color.FromRgb(0xFF, 0xE0, 0x30), 0.33),
            new GradientStop(Color.FromRgb(0x30, 0xD0, 0xFF), 0.66), new GradientStop(Color.FromRgb(0xC0, 0x40, 0xFF), 1),
        },
    };
}
