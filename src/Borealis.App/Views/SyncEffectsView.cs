using System.Windows;
using System.Windows.Controls;
using Borealis.Core.Lighting;

namespace Borealis.App.Views;

/// <summary>
/// "Borealis effects" tab: one effect for all devices; any choice here puts all devices back into Sync.
/// Reiter „Borealis-Effekte“: ein Effekt für alle Geräte; jede Auswahl nimmt alle Geräte wieder in Sync auf.
/// </summary>
sealed class SyncEffectsView : UserControl
{
    static AppState S => AppState.Instance;

    public SyncEffectsView() => Rebuild();

    public void Rebuild()
    {
        var sync = S.Profile.Sync;
        bool hasMb = S.ZonesOf("Mainboard").Any(), hasArgb = S.ZonesOf("Argb").Any(), hasRam = S.ZonesOf("Ram").Any();

        var info = new TextBlock { FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18) };
        void UpdateInfo()
        {
            var single = new List<string>();
            if (hasMb && !sync.Mainboard) single.Add(Loc.T("Mainboard", "Motherboard"));
            if (hasRam && !sync.Ram) single.Add(Loc.T("Arbeitsspeicher", "Memory"));
            if (hasArgb && !sync.Argb) single.Add(Loc.T("ARGB-Header", "ARGB headers"));
            info.Text = single.Count == 0
                ? Loc.T("Gilt für alle Geräte und läuft auf allen synchron.", "Applies to all devices and runs in sync on all of them.")
                : Loc.T($"Gilt für alle Geräte und läuft auf allen synchron. {string.Join(", ", single)} " +
                        $"{(single.Count == 1 ? "ist" : "sind")} gerade einzeln eingestellt und {(single.Count == 1 ? "wird" : "werden")} mit der nächsten Auswahl hier wieder synchronisiert.",
                        $"Applies to all devices and runs in sync on all of them. {string.Join(", ", single)} " +
                        $"{(single.Count == 1 ? "is" : "are")} currently set individually and will be synced again with the next selection here.");
            info.Foreground = single.Count == 0 ? Ui.Brush("TextDim") : Ui.Brush("Warn");
        }
        // Every change here applies to all devices; per device under "Devices"
        // Jede Änderung hier gilt für alle Geräte; einzeln unter „Geräte“
        void Changed()
        {
            if (hasMb) sync.Mainboard = true;
            if (hasArgb) sync.Argb = true;
            if (hasRam) sync.Ram = true;
            UpdateInfo();
            S.Edited();
        }

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition());
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var panel = new EffectSettingsPanel(new SyncTarget(sync), firmwareOnly: false, Changed, Rebuild);
        var card = Ui.Card(panel, new Thickness(20, 0, 0, 0));
        card.VerticalAlignment = VerticalAlignment.Top;
        card.Width = 290;
        bool on = sync.Effect != FirmwareEffect.Off;
        card.Visibility = on ? Visibility.Visible : Visibility.Collapsed;

        var left = new StackPanel();
        UpdateInfo();
        left.Children.Add(info);
        // Lighting on/off; "Off" is no longer an effect / Beleuchtung ein/aus; „Aus“ ist kein Effekt mehr
        var power = new CheckBox { Style = Ui.Style("Switch"), Content = Loc.T("Beleuchtung an", "Lighting on"), IsChecked = on, Margin = new Thickness(0, 0, 0, 20) };
        power.Click += (_, _) =>
        {
            if (power.IsChecked == true)
                sync.Effect = sync.LastEffect != FirmwareEffect.Off ? sync.LastEffect : FirmwareEffect.Static;
            else
            {
                sync.LastEffect = sync.Effect;
                sync.Effect = FirmwareEffect.Off;
            }
            Changed();
            Rebuild();
        };
        left.Children.Add(power);
        var effects = new StackPanel { Visibility = on ? Visibility.Visible : Visibility.Collapsed };
        effects.Children.Add(Ui.Section(Loc.T("Effekte", "Effects")));
        effects.Children.Add(Ui.EffectTiles(sync.Effect, e => { sync.Effect = e; sync.LastEffect = e; panel.Refresh(); Changed(); }, "SyncEffect"));
        left.Children.Add(effects);

        root.Children.Add(Ui.Scroll(left));
        var right = Ui.Scroll(card);
        Grid.SetColumn(right, 1);
        root.Children.Add(right);
        Content = root;
    }
}
