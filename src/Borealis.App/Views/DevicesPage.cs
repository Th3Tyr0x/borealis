using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Borealis.App.Controls;
using Borealis.Core.Ipc;
using Borealis.Core.Lighting;
using Borealis.Core.Profiles;

namespace Borealis.App.Views;

/// <summary>
/// "Devices": each device on its own; select and color zones, effect and speed, zone names, effect after shutdown.
/// „Geräte“: jedes Gerät einzeln; Zonen wählen und einfärben, Effekt und Tempo, Zonennamen, Effekt nach dem Herunterfahren.
/// </summary>
sealed class DevicesPage : UserControl, IPage
{
    static AppState S => AppState.Instance;
    public string Title => Loc.T("Geräte", "Devices");

    sealed record Device(string Kind, string Title, string IconKey, GroupKind Group, GroupKind? Standby);

    static Device[] Devices =>
    [
        new("Mainboard", Loc.T("Mainboard", "Motherboard"), "IconMainboard", GroupKind.Mainboard, GroupKind.StandbyMainboard),
        new("Argb", Loc.T("ARGB-Header", "ARGB headers"), "IconStrip", GroupKind.Argb, GroupKind.StandbyArgb),
        new("Ram", Loc.T("Arbeitsspeicher", "Memory"), "IconRam", GroupKind.Ram, null),
    ];

    string currentKind = "Mainboard";
    // Device page tabs: lighting, after shutdown, LED count (ARGB only)
    // Reiter: Beleuchtung, nach dem Herunterfahren, LED-Anzahl (nur ARGB)
    enum Tab { Lighting, Standby, LedCount }
    Tab tab;
    // Selected zones per device and tab / Ausgewählte Zonen je Gerät und Reiter
    readonly Dictionary<string, HashSet<string>> selection = new();

    public DevicesPage() => Rebuild();

    /// <summary>Open a device from outside ("Set device lighting" on the Sync page).
    /// Gerät von außen öffnen („Gerätebeleuchtung festlegen“ auf der Sync-Seite).</summary>
    public void Select(string kind, bool ledCountTab = false)
    {
        currentKind = kind;
        tab = ledCountTab ? Tab.LedCount : Tab.Lighting;
        Rebuild();
    }

    public void Rebuild()
    {
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        root.ColumnDefinitions.Add(new ColumnDefinition());

        var list = new StackPanel();
        foreach (var d in Devices.Where(d => S.ZonesOf(d.Kind).Any()))
        {
            var item = new RadioButton
            {
                Style = Ui.Style("NavItem"), GroupName = "DeviceList", Content = d.Title,
                Tag = Icons.Resource(d.IconKey), IsChecked = d.Kind == currentKind,
            };
            var kind = d.Kind;
            item.Checked += (_, _) => { currentKind = kind; tab = Tab.Lighting; Rebuild(); };
            list.Children.Add(item);
        }
        var listCard = new Border { Style = Ui.Style("Card"), Padding = new Thickness(0, 8, 0, 8), Child = list, VerticalAlignment = VerticalAlignment.Top };
        root.Children.Add(listCard);

        var device = Devices.FirstOrDefault(d => d.Kind == currentKind && S.ZonesOf(d.Kind).Any()) ?? Devices.FirstOrDefault(d => S.ZonesOf(d.Kind).Any());
        if (device != null)
        {
            var body = BuildDevice(device);
            body.Margin = new Thickness(20, 0, 0, 0);
            Grid.SetColumn(body, 1);
            root.Children.Add(body);
        }
        else root.Children.Add(Ui.Hint(Loc.T("Keine Geräte gefunden. Läuft der Borealis-Dienst?", "No devices found. Is the Borealis service running?")));
        Content = root;
    }

    FrameworkElement BuildDevice(Device d)
    {
        var panel = new DockPanel();
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(Ui.Icon(Icons.Resource(d.IconKey), 26, Ui.Brush("Accent")));
        title.Children.Add(new TextBlock { Text = d.Title, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(title);
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);

        var tabItems = new List<(Tab, string)> { (Tab.Lighting, Loc.T("Beleuchtung", "Lighting")) };
        if (d.Standby != null) tabItems.Add((Tab.Standby, Loc.T("Nach dem Herunterfahren", "After shutdown")));
        if (d.Kind == "Argb") tabItems.Add((Tab.LedCount, Loc.T("LED-Anzahl", "LED count")));
        if (!tabItems.Any(t => t.Item1 == tab)) tab = Tab.Lighting;
        if (tabItems.Count > 1)
        {
            var tabs = Ui.Segments(tabItems, tab, v => { tab = v; Rebuild(); }, "DeviceTab");
            tabs.Margin = new Thickness(0, 0, 0, 16);
            DockPanel.SetDock(tabs, Dock.Top);
            panel.Children.Add(tabs);
        }
        if (tab == Tab.LedCount)
        {
            panel.Children.Add(BuildLedCounts());
            return panel;
        }

        bool standby = tab == Tab.Standby;
        // A device in Borealis Sync shows the Sync effect; the first edit here takes it out of Sync.
        // Ein Gerät in Borealis Sync zeigt dessen Effekt; die erste Änderung hier löst es aus Sync.
        bool synced = !standby && S.Profile.IsSynced(d.Group);
        var group = synced ? S.Profile.Resolve().Group(d.Group) : S.Profile.Group(standby ? d.Standby!.Value : d.Group);
        void Detach()
        {
            if (!synced) return;
            SetSynced(d.Group, false);
            synced = false;
            group = group.Clone();
            SetGroup(d.Group, group);
        }
        var zones = S.ZonesOf(d.Kind).ToList();
        string selKey = d.Kind + (standby ? ":standby" : "");
        if (!selection.TryGetValue(selKey, out var selected)) selection[selKey] = selected = zones.Select(z => z.Id).ToHashSet();

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition());
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var swatches = new Dictionary<string, Ellipse>();
        void UpdateSwatches()
        {
            foreach (var (id, sw) in swatches) sw.Fill = Ui.ToBrush(group.ColorOf(id));
        }
        var settingsPanel = new EffectSettingsPanel(
            new GroupTarget(() => group, () => selected, zones.Select(z => z.Id).ToList(), Detach),
            firmwareOnly: standby, () => { UpdateSwatches(); S.Edited(); }, Rebuild);

        var left = new StackPanel();
        left.Children.Add(Ui.Section(Loc.T("Zonen", "Zones")));
        // One row per zone: color dot, name (right-click: rename), LED count; click selects
        // Eine Zeile je Zone: Farbpunkt, Name (Rechtsklick: umbenennen), LED-Anzahl; Klick wählt aus
        var zonePanel = new StackPanel();
        foreach (var z in zones)
        {
            var swatch = new Ellipse { Width = 18, Height = 18, Fill = Ui.ToBrush(group.ColorOf(z.Id)), Stroke = Ui.Brush("Line"), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            swatches[z.Id] = swatch;
            var row = new DockPanel();
            row.Children.Add(swatch);
            var leds = new TextBlock { Text = $"{z.LedCount} LED{(z.LedCount == 1 ? "" : "s")}", Foreground = Ui.Brush("TextMute"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            DockPanel.SetDock(leds, Dock.Right);
            row.Children.Add(leds);
            // Custom name, zone name small below / Eigener Name, darunter klein der Zonenname
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            names.Children.Add(new TextBlock { Text = S.LabelOf(z), TextTrimming = TextTrimming.CharacterEllipsis });
            if (S.Labels.ContainsKey(z.Id))
                names.Children.Add(new TextBlock { Text = Loc.Term(z.Name), Foreground = Ui.Brush("TextMute"), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis });
            row.Children.Add(names);
            var chip = new ToggleButton { Style = Ui.Style("Chip"), Content = row, IsChecked = selected.Contains(z.Id), Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(12, 8, 12, 8) };
            chip.ContextMenu = ZoneMenu(z);
            var id = z.Id;
            chip.Checked += (_, _) => { selected.Add(id); settingsPanel.Refresh(); };
            chip.Unchecked += (_, _) => { selected.Remove(id); settingsPanel.Refresh(); };
            zonePanel.Children.Add(chip);
        }
        left.Children.Add(zonePanel);
        left.Children.Add(Ui.Hint(d.Kind == "Argb" && !standby
            ? Loc.T("Zonen anklicken, um sie auszuwählen; die Farbe rechts gilt für alle ausgewählten Zonen. Rechtsklick benennt eine Zone um. Die LED-Anzahl je Header stellst du im Reiter „LED-Anzahl“ ein.",
                    "Click zones to select them; the color on the right applies to all selected zones. Right-click a zone to rename it. Set the LED count per header in the “LED count” tab.")
            : Loc.T("Zonen anklicken, um sie auszuwählen; die Farbe rechts gilt für alle ausgewählten Zonen. Rechtsklick benennt eine Zone um.",
                    "Click zones to select them; the color on the right applies to all selected zones. Right-click a zone to rename it."), 0));

        // On/off for this device ("Off" is no effect); after shutdown "Off" stays a tile
        // Ein/Aus für dieses Gerät („Aus“ ist kein Effekt); nach dem Herunterfahren bleibt „Aus“ eine Kachel
        bool on = standby || group.Effect != FirmwareEffect.Off;
        if (!standby)
        {
            var power = new CheckBox { Style = Ui.Style("Switch"), Content = Loc.T("Beleuchtung an", "Lighting on"), IsChecked = on, Margin = new Thickness(0, 24, 0, 0) };
            power.Click += (_, _) =>
            {
                Detach();
                if (power.IsChecked == true)
                    group.Effect = group.LastEffect != FirmwareEffect.Off ? group.LastEffect : FirmwareEffect.Static;
                else
                {
                    group.LastEffect = group.Effect;
                    group.Effect = FirmwareEffect.Off;
                }
                S.Edited();
                Rebuild();
            };
            left.Children.Add(power);
        }
        var effectsBox = new StackPanel { Visibility = on ? Visibility.Visible : Visibility.Collapsed };
        effectsBox.Children.Add(Ui.Section(Loc.T("Effekte", "Effects"), 24));
        if (d.Kind == "Argb")
        {
            var shared = Ui.Hint(Loc.T("Die ARGB-Header teilen sich einen Effekt; die Farbe gilt je Header.", "The ARGB headers share one effect; the color applies per header."), 0);
            shared.Margin = new Thickness(0, 0, 0, 10);
            effectsBox.Children.Add(shared);
        }
        effectsBox.Children.Add(Ui.EffectTiles(group.Effect, e => { Detach(); group.Effect = e; if (e != FirmwareEffect.Off) group.LastEffect = e; settingsPanel.Refresh(); S.Edited(); }, "DevEffect" + selKey, compact: true, firmwareOnly: standby));
        left.Children.Add(effectsBox);
        if (standby)
            left.Children.Add(Ui.Hint(Loc.T("Der Effekt nach dem Herunterfahren wird im Controller gespeichert. Sichtbar, wenn im BIOS die Beleuchtung im Ruhezustand/Soft-Off erlaubt ist.",
                "The effect after shutdown is stored in the controller. Visible if the BIOS allows lighting in sleep/soft-off."), 2));
        content.Children.Add(Ui.Scroll(left));

        var rightCard = Ui.Card(settingsPanel, new Thickness(20, 0, 0, 0));
        rightCard.VerticalAlignment = VerticalAlignment.Top;
        rightCard.Width = 290;
        rightCard.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        var rightScroll = Ui.Scroll(rightCard);
        Grid.SetColumn(rightScroll, 1);
        content.Children.Add(rightScroll);
        panel.Children.Add(content);
        return panel;
    }

    // ARGB header currently running the count test / ARGB-Header mit laufendem Zähltest
    string? ledTestZone;

    /// <summary>
    /// "LED count" tab: set the LED count per ARGB header, with a count test to find the real length.
    /// Reiter „LED-Anzahl“: LED-Anzahl je ARGB-Header setzen, mit Zähltest zum Abzählen der echten Länge.
    /// </summary>
    FrameworkElement BuildLedCounts()
    {
        var list = new StackPanel();
        list.Children.Add(new TextBlock
        {
            Text = Loc.T("Trage für jeden ARGB-Header die Anzahl LEDs des angeschlossenen Geräts ein (bei mehreren Lüftern in Reihe die Summe). " +
                         "Die Zahl wird im Controller gespeichert; Hardware- und Software-Effekte laufen dann genau über diese LEDs.",
                         "Enter the number of LEDs of the connected device for each ARGB header (the sum for several fans in a chain). " +
                         "The number is stored in the controller; hardware and software effects then run exactly over these LEDs."),
            Foreground = Ui.Brush("TextDim"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left,
        });

        foreach (var z in S.ZonesOf("Argb"))
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock { Text = S.LabelOf(z), FontSize = 15, FontWeight = FontWeights.SemiBold });
            info.Children.Add(new TextBlock { Text = Loc.T($"{Loc.Term(z.Name)} · eingestellt: {z.LedCount} LEDs", $"{Loc.Term(z.Name)} · set: {z.LedCount} LEDs"), Foreground = Ui.Brush("TextMute"), FontSize = 12, Margin = new Thickness(0, 3, 0, 0) });
            row.Children.Add(info);

            var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var box = new TextBox { Text = z.LedCount.ToString(), Width = 64, TextAlignment = TextAlignment.Center, FontSize = 14 };
            int Value() => int.TryParse(box.Text, out var v) ? Math.Clamp(v, 1, 500) : z.LedCount;
            var minus = new Button { Content = "−", Width = 34, Padding = new Thickness(0, 5, 0, 5), Margin = new Thickness(0, 0, 4, 0) };
            var plus = new Button { Content = "+", Width = 34, Padding = new Thickness(0, 5, 0, 5), Margin = new Thickness(4, 0, 0, 0) };
            minus.Click += (_, _) => box.Text = Math.Max(1, Value() - 1).ToString();
            plus.Click += (_, _) => box.Text = Math.Min(500, Value() + 1).ToString();
            controls.Children.Add(minus);
            controls.Children.Add(box);
            controls.Children.Add(plus);

            var apply = new Button { Content = Loc.T("Übernehmen", "Apply"), Style = Ui.Style("AccentButton"), Margin = new Thickness(14, 0, 0, 0), IsEnabled = false };
            box.TextChanged += (_, _) => apply.IsEnabled = Value() != z.LedCount;
            apply.Click += async (_, _) =>
            {
                apply.IsEnabled = false;
                await S.SetLedCountAsync(z.Id, Value());
                ledTestZone = null;
                await S.RefreshAsync();
                Rebuild();
            };
            controls.Children.Add(apply);

            bool testing = ledTestZone == z.Id;
            var test = new Button { Content = testing ? Loc.T("Zähltest beenden", "Stop count test") : Loc.T("Zähltest", "Count test"), Margin = new Thickness(8, 0, 0, 0) };
            test.Click += async (_, _) =>
            {
                ledTestZone = testing ? null : z.Id;
                await S.LedTestAsync(ledTestZone);
                Rebuild();
            };
            controls.Children.Add(test);
            Grid.SetColumn(controls, 1);
            row.Children.Add(controls);

            var card = Ui.Card(row, new Thickness(0, 0, 0, 10));
            card.Padding = new Thickness(18, 14, 18, 14);
            if (testing) card.BorderBrush = Ui.Brush("Accent");
            list.Children.Add(card);
        }

        list.Children.Add(Ui.Hint(ledTestZone != null
            ? Loc.T("Zähltest läuft: Die erste LED leuchtet grün, jede fünfte blau, jede zehnte weiß, alle anderen schwach rot. " +
                    "Zähle bis zur letzten leuchtenden LED und trage die Zahl ein. „Zähltest beenden“ stellt das Profil wieder her.",
                    "Count test running: the first LED is green, every fifth blue, every tenth white, all others dim red. " +
                    "Count up to the last lit LED and enter the number. “Stop count test” restores the profile.")
            : Loc.T("Zähltest: Leuchtet den Header mit Markierungen über alle eingestellten LEDs, damit du die tatsächliche Länge abzählen kannst. " +
                    "Höchstens 500 LEDs je Header. Der Effekt läuft während des Tests nicht.",
                    "Count test: lights the header with markers across all set LEDs so you can count the actual length. " +
                    "At most 500 LEDs per header. The effect does not run during the test."), 6));
        return Ui.Scroll(list);
    }

    static void SetGroup(GroupKind g, GroupSetting value)
    {
        switch (g)
        {
            case GroupKind.Mainboard: S.Profile.Mainboard = value; break;
            case GroupKind.Argb: S.Profile.Argb = value; break;
            case GroupKind.Ram: S.Profile.Ram = value; break;
        }
    }

    static void SetSynced(GroupKind g, bool on)
    {
        var s = S.Profile.Sync;
        switch (g)
        {
            case GroupKind.Mainboard: s.Mainboard = on; break;
            case GroupKind.Argb: s.Argb = on; break;
            case GroupKind.Ram: s.Ram = on; break;
        }
    }


    /// <summary>Right-click on a zone: rename or restore its name.
    /// Rechtsklick auf eine Zone: umbenennen oder Namen wiederherstellen.</summary>
    ContextMenu ZoneMenu(ZoneInfo z)
    {
        var menu = new ContextMenu();
        var rename = new MenuItem { Header = Loc.T("Umbenennen …", "Rename …") };
        rename.Click += (_, _) =>
        {
            var name = InputDialog.Show(Window.GetWindow(this)!, Loc.T("Zone umbenennen", "Rename zone"),
                Loc.T($"Neuer Name für „{Loc.Term(z.Name)}“ (leer = Standardname):", $"New name for “{Loc.Term(z.Name)}” (empty = default name):"),
                S.LabelOf(z));
            if (name != null) SetLabel(z, name);
        };
        var reset = new MenuItem { Header = Loc.T("Standardname wiederherstellen", "Restore default name"), IsEnabled = S.Labels.ContainsKey(z.Id) };
        reset.Click += (_, _) => SetLabel(z, "");
        menu.Items.Add(rename);
        menu.Items.Add(reset);
        return menu;
    }

    void SetLabel(ZoneInfo z, string name)
    {
        name = name.Trim();
        if (name.Length == 0 || name == Loc.Term(z.Name)) S.Labels.Remove(z.Id);
        else S.Labels[z.Id] = name;
        S.Edited();
        Rebuild();
    }
}
