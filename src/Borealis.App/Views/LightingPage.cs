using System.Windows;
using System.Windows.Controls;
using Borealis.App.Controls;
using Borealis.Core.Lighting;
using Borealis.Core.Profiles;
using Microsoft.Win32;

namespace Borealis.App.Views;

/// <summary>
/// „Beleuchtung &amp; Borealis Sync“ : Reiter „Sync Geräte“ (welche Geräte den gemeinsamen Effekt zeigen),
/// „Borealis-Effekte“ (gemeinsamer Effekt der angehakten Geräte) und „Farbabgleich“ (gleiche Farben auf allen Geräten).
/// </summary>
sealed class LightingPage : UserControl, IPage
{
    static AppState S => AppState.Instance;
    public string Title => Loc.T("Beleuchtung & Borealis Sync", "Lighting & Borealis Sync");

    /// <summary>„Gerätebeleuchtung festlegen“ wurde für ein Gerät (Zonenart) angeklickt.</summary>
    public event Action<string>? OpenDevice;

    readonly ContentControl body = new() { Focusable = false };
    readonly SyncEffectsView effects = new();
    readonly CalibrationView calibration = new();
    enum Tab { Sync, Effects, Calibration }
    Tab tab;
    readonly RadioButton effTab, calTab;

    static readonly string BoardName = ReadBoardName();

    public LightingPage()
    {
        var tabs = new StackPanel { Orientation = Orientation.Horizontal };
        var syncTab = new RadioButton { Style = Ui.Style("PageTab"), Content = Loc.T("Sync Geräte", "Sync devices"), GroupName = "LightingTabs", IsChecked = true };
        effTab = new RadioButton { Style = Ui.Style("PageTab"), Content = Loc.T("Borealis-Effekte", "Borealis effects"), GroupName = "LightingTabs" };
        calTab = new RadioButton { Style = Ui.Style("PageTab"), Content = Loc.T("Farbabgleich", "Color matching"), GroupName = "LightingTabs" };
        syncTab.Checked += (_, _) => SelectTab(Tab.Sync);
        effTab.Checked += (_, _) => SelectTab(Tab.Effects);
        calTab.Checked += (_, _) => SelectTab(Tab.Calibration);
        tabs.Children.Add(syncTab);
        tabs.Children.Add(effTab);
        tabs.Children.Add(calTab);
        var tabBar = new Border
        {
            Child = tabs, BorderBrush = Ui.Brush("Line"), BorderThickness = new Thickness(0, 0, 0, 1),
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 26),
        };
        var root = new DockPanel();
        DockPanel.SetDock(tabBar, Dock.Top);
        root.Children.Add(tabBar);
        root.Children.Add(body);
        Content = root;
        ShowTab();
        // Änderungen auf der Geräteseite (z. B. Gerät aus Sync gelöst) beim Zurückkehren anzeigen
        // Abgleich-Modus beim Verlassen der Seite (auch Fenster ins Tray) beenden
        IsVisibleChanged += (_, e) =>
        {
            if ((bool)e.NewValue) Rebuild();
            else calibration.Leave();
        };
    }

    /// <summary>Reiter „Farbabgleich“ bzw. „Borealis-Effekte“ öffnen (Bildschirmfotos).</summary>
    internal void ShowCalibration() => calTab.IsChecked = true;
    internal void ShowEffects() => effTab.IsChecked = true;

    public void Rebuild()
    {
        effects.Rebuild();
        if (tab == Tab.Calibration) calibration.Rebuild();
        ShowTab();
    }

    void SelectTab(Tab t)
    {
        if (tab == Tab.Calibration && t != Tab.Calibration) calibration.Leave();
        if (t == Tab.Calibration && tab != Tab.Calibration) calibration.Rebuild();
        tab = t;
        ShowTab();
    }

    void ShowTab() => body.Content = tab switch
    {
        Tab.Effects => effects,
        Tab.Calibration => calibration,
        _ => BuildSyncDevices(),
    };

    FrameworkElement BuildSyncDevices()
    {
        var sync = S.Profile.Sync;
        var resolved = S.Profile.Resolve();
        var root = new StackPanel { MaxWidth = 972, HorizontalAlignment = HorizontalAlignment.Left };
        root.Children.Add(new TextBlock
        {
            Text = Loc.T("Geräte in Borealis Sync zeigen den gemeinsamen Effekt aus „Borealis-Effekte“. Für einen eigenen Effekt nimmst du ein Gerät " +
                         "aus Sync und stellst es unter „Geräte“ ein.",
                         "Devices in Borealis Sync show the shared effect from “Borealis effects”. For an effect of its own, take a device out of " +
                         "sync and set it up under “Devices”."),
            Foreground = Ui.Brush("TextDim"), FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 22),
        });

        string Count(string kind, string one, string many)
        {
            int n = S.ZonesOf(kind).Count();
            return n == 1 ? $"1 {one}" : $"{n} {many}";
        }
        var devices = new List<(string Kind, string Name, string Subtitle, string Icon, GroupSetting Group, Func<bool> Get, Action<bool> Set)>();
        if (S.ZonesOf("Mainboard").Any())
            devices.Add(("Mainboard", Loc.T("Mainboard", "Motherboard"), $"{BoardName} · {Count("Mainboard", Loc.T("Zone", "zone"), Loc.T("Zonen", "zones"))}", "IconMainboard", resolved.Mainboard, () => sync.Mainboard, v => sync.Mainboard = v));
        if (S.ZonesOf("Argb").Any())
            devices.Add(("Argb", Loc.T("ARGB-Header", "ARGB headers"), $"{Count("Argb", Loc.T("Header", "header"), Loc.T("Header", "headers"))} · {S.ZonesOf("Argb").Sum(z => z.LedCount)} LEDs", "IconStrip", resolved.Argb, () => sync.Argb, v => sync.Argb = v));
        if (S.ZonesOf("Ram").Any())
            devices.Add(("Ram", Loc.T("Arbeitsspeicher", "Memory"), Count("Ram", Loc.T("Modul", "module"), Loc.T("Module", "modules")), "IconRam", resolved.Ram, () => sync.Ram, v => sync.Ram = v));

        var head = new DockPanel { Margin = new Thickness(0, 0, 14, 10) };
        if (devices.Count > 1)
        {
            var all = new CheckBox { Style = Ui.Style("Switch"), Content = Loc.T("Alle in Borealis Sync", "All in Borealis Sync"), IsChecked = devices.All(d => d.Get()), VerticalAlignment = VerticalAlignment.Center };
            all.Click += (_, _) =>
            {
                foreach (var d in devices) d.Set(all.IsChecked == true);
                Changed();
            };
            DockPanel.SetDock(all, Dock.Right);
            head.Children.Add(all);
        }
        head.Children.Add(new TextBlock { Text = Loc.T("Geräte", "Devices"), FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(head);

        var cards = new WrapPanel();
        foreach (var d in devices)
            cards.Children.Add(DeviceCard(d.Name, d.Subtitle, d.Icon, d.Group, d.Get(), on => { d.Set(on); Changed(); }, () => OpenDevice?.Invoke(d.Kind)));
        root.Children.Add(cards);
        if (devices.Count == 0) root.Children.Add(Ui.Hint(Loc.T("Keine Geräte gefunden. Läuft der Borealis-Dienst?", "No devices found. Is the Borealis service running?")));
        root.Children.Add(PerformanceCard());

        void Changed()
        {
            S.Edited();
            effects.Rebuild();
            ShowTab();
        }
        return Ui.Scroll(root);
    }

    /// <summary>Gerätekarte: Symbol, Name, Schalter für Borealis Sync, aktueller Effekt und Verknüpfung zur Geräteseite.</summary>
    static FrameworkElement DeviceCard(string name, string subtitle, string iconKey, GroupSetting group, bool synced, Action<bool> onToggle, Action onOpenDevice)
    {
        var panel = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var icon = Ui.Icon(Icons.Resource(iconKey), 28, synced ? Ui.Brush("Accent") : Ui.Brush("TextDim"));
        icon.Margin = new Thickness(0, 0, 14, 0);
        head.Children.Add(icon);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = name, FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = subtitle, Foreground = Ui.Brush("TextMute"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = subtitle });
        Grid.SetColumn(text, 1);
        head.Children.Add(text);
        var toggle = new CheckBox
        {
            Style = Ui.Style("Switch"), IsChecked = synced, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0),
            ToolTip = synced ? Loc.T("Aus Borealis Sync nehmen", "Remove from Borealis Sync") : Loc.T("In Borealis Sync übernehmen", "Add to Borealis Sync"),
        };
        toggle.Click += (_, _) => onToggle(toggle.IsChecked == true);
        Grid.SetColumn(toggle, 2);
        head.Children.Add(toggle);
        panel.Children.Add(head);

        // Aktueller Effekt (gemeinsam oder eigener)
        bool off = group.Effect == FirmwareEffect.Off;
        var effectRow = new Grid();
        effectRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        effectRow.ColumnDefinitions.Add(new ColumnDefinition());
        var effectIcon = Ui.Icon(Icons.Effect(off ? group.LastEffect : group.Effect), 20, Ui.Brush(off ? "TextMute" : "TextDim"));
        effectIcon.Margin = new Thickness(0, 0, 12, 0);
        effectRow.Children.Add(effectIcon);
        var effectText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        effectText.Children.Add(new TextBlock { Text = synced ? "BOREALIS SYNC" : Loc.T("EIGENER EFFEKT", "OWN EFFECT"), Foreground = Ui.Brush("TextMute"), FontSize = 11, FontWeight = FontWeights.SemiBold });
        effectText.Children.Add(new TextBlock { Text = off ? Loc.T("Beleuchtung aus", "Lighting off") : FirmwareEffects.DisplayName(group.Effect), Foreground = Ui.Brush(off ? "TextMute" : "Text") });
        Grid.SetColumn(effectText, 1);
        effectRow.Children.Add(effectText);
        panel.Children.Add(new Border { Child = effectRow, Background = Ui.Brush("Bg1"), CornerRadius = new CornerRadius(4), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 16, 0, 0) });

        var link = new Button
        {
            Style = Ui.Style("LinkButton"), Content = synced ? Loc.T("Gerät einzeln einstellen", "Set device individually") : Loc.T("Gerätebeleuchtung festlegen", "Set device lighting"),
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 14, 0, 0),
        };
        link.Click += (_, _) => onOpenDevice();
        panel.Children.Add(link);

        var card = Ui.Card(panel, new Thickness(0, 0, 14, 14));
        card.Width = 310;
        if (synced) card.BorderBrush = Ui.Brush("Accent");
        return card;
    }

    /// <summary>
    /// „Borealis Leistungsmodus“ (wie der Aura-Leistungsmodus in Armoury Crate): Ein/Aus und die Stufe „Flüssigkeit des Lichteffekts“.
    /// In Borealis bestimmt er die Bildrate der Software-Effekte (8 Bilder/s aus, 12/20/30 je Stufe).
    /// </summary>
    static FrameworkElement PerformanceCard()
    {
        var settings = S.Status?.Settings ?? new BorealisSettings();
        var panel = new StackPanel();
        panel.Children.Add(Ui.Section(Loc.T("Borealis Leistungsmodus", "Borealis performance mode")));
        var toggle = new CheckBox { Style = Ui.Style("Switch"), IsChecked = settings.PerformanceMode, Content = Loc.T("Leistungsmodus: flüssigere Effekte, etwas mehr CPU-Last", "Performance mode: smoother effects, slightly more CPU load") };
        panel.Children.Add(toggle);

        var levelPanel = new StackPanel { Margin = new Thickness(0, 16, 0, 0), IsEnabled = settings.PerformanceMode };
        levelPanel.Children.Add(new TextBlock { Text = Loc.T("Flüssigkeit des Lichteffekts", "Smoothness of the lighting effect"), Foreground = Ui.Brush("TextDim"), Margin = new Thickness(0, 0, 0, 8) });
        levelPanel.Children.Add(Ui.Segments([(1, Loc.T("Stufe 1", "Level 1")), (2, Loc.T("Stufe 2", "Level 2")), (3, Loc.T("Höchste Stufe", "Highest level"))], Math.Clamp(settings.PerformanceLevel, 1, 3),
            async v => await S.UpdateSettingsAsync(s => s.PerformanceLevel = v), "PerformanceLevel"));
        panel.Children.Add(levelPanel);

        var fps = S.Status?.EffectFps ?? 0;
        panel.Children.Add(Ui.Hint(
            Loc.T("Ziel: 12, 20 oder 30 Bilder/s je Stufe, ohne Leistungsmodus 8. ", "Target: 12, 20 or 30 frames/s per level, 8 without performance mode. ") +
            (fps > 0 ? Loc.T($"Software-Effekte laufen gerade mit {fps:0.#} Bildern/s (Ziel {settings.FramesPerSecond}).",
                             $"Software effects are running at {fps:0.#} frames/s (target {settings.FramesPerSecond}).")
                     : Loc.T("Gilt für Effekte, die Borealis berechnet (alles außer „Statisch“ mit einer Farbe).",
                             "Applies to effects calculated by Borealis (everything except “Static” with one color).")), 12));

        toggle.Click += async (_, _) =>
        {
            levelPanel.IsEnabled = toggle.IsChecked == true;
            await S.UpdateSettingsAsync(s => s.PerformanceMode = toggle.IsChecked == true);
        };
        return Ui.Card(panel, new Thickness(0, 12, 14, 14));
    }

    static string ReadBoardName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            var product = key?.GetValue("BaseBoardProduct") as string;
            if (!string.IsNullOrWhiteSpace(product)) return product.Trim();
        }
        catch (System.Security.SecurityException) { }
        return Loc.T("Mainboard", "Motherboard");
    }
}
