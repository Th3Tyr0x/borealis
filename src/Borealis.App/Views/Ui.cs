using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Borealis.App.Controls;
using Borealis.Core.Lighting;

namespace Borealis.App.Views;

/// <summary>Page building blocks (tiles, segments, cards) using the styles in Themes/Borealis.xaml.
/// Bausteine für die Seiten (Kacheln, Segmente, Karten) mit den Stilen aus Themes/Borealis.xaml.</summary>
static class Ui
{
    public static T Res<T>(string key) => (T)Application.Current.FindResource(key);
    public static Style Style(string key) => Res<Style>(key);
    public static Brush Brush(string key) => Res<Brush>(key);

    public static TextBlock Section(string text, double top = 0) =>
        new() { Text = text, Style = Style("SectionTitle"), Margin = new Thickness(0, top, 0, 10) };

    public static TextBlock Hint(string text, double top = 6) =>
        new() { Text = text, Style = Style("Hint"), Margin = new Thickness(0, top, 0, 0) };

    public static Border Card(UIElement child, Thickness? margin = null) =>
        new() { Style = Style("Card"), Child = child, Margin = margin ?? new Thickness(0, 0, 0, 14) };

    public static Path Icon(Geometry data, double size = 20, Brush? stroke = null) => new()
    {
        Data = data, Width = size, Height = size, Stretch = Stretch.Uniform,
        Stroke = stroke ?? Brush("Text"), StrokeThickness = 1.7,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
    };

    /// <summary>
    /// Effect tiles (single choice), grouped like <see cref="SoftwareEffects.Sections"/>; <paramref name="firmwareOnly"/>: controller effects only.
    /// Effekt-Kacheln (eine Auswahl), gruppiert wie <see cref="SoftwareEffects.Sections"/>; <paramref name="firmwareOnly"/>: nur Controller-Effekte.
    /// </summary>
    public static StackPanel EffectTiles(FirmwareEffect selected, Action<FirmwareEffect> onSelect, string group, bool compact = false, bool firmwareOnly = false)
    {
        var root = new StackPanel();
        var sections = firmwareOnly ? [("", FirmwareEffects.Selectable)] : SoftwareEffects.Sections;
        foreach (var (title, effects) in sections)
        {
            if (title.Length > 0)
            {
                var head = new TextBlock { Text = Loc.Term(title), Foreground = Brush("TextMute"), FontSize = 12, Margin = new Thickness(0, root.Children.Count == 0 ? 0 : 8, 0, 8) };
                root.Children.Add(head);
            }
            var panel = new WrapPanel();
            foreach (var e in effects)
            {
                var tile = new RadioButton
                {
                    Style = Style("EffectTile"), GroupName = group, Tag = Icons.Effect(e), IsChecked = e == selected,
                    Content = new TextBlock { Text = FirmwareEffects.DisplayName(e), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
                };
                if (compact) { tile.Width = 98; tile.Height = 84; tile.Margin = new Thickness(0, 0, 8, 8); }
                var effect = e;
                tile.Checked += (_, _) => onSelect(effect);
                panel.Children.Add(tile);
            }
            root.Children.Add(panel);
        }
        return root;
    }

    /// <summary>Segmented switch; returns the chosen value.
    /// Segmentschalter; liefert den gewählten Wert.</summary>
    public static StackPanel Segments<T>(IEnumerable<(T Value, string Text)> items, T selected, Action<T> onSelect, string group)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (value, text) in items)
        {
            var b = new RadioButton { Style = Style("Segment"), GroupName = group, Content = text, IsChecked = Equals(value, selected) };
            var v = value;
            b.Checked += (_, _) => onSelect(v);
            panel.Children.Add(b);
        }
        return panel;
    }

    public static StackPanel SpeedSegments(SpeedLevel selected, Action<SpeedLevel> onSelect, string group) =>
        Segments([(SpeedLevel.Slow, Loc.T("Langsam", "Slow")), (SpeedLevel.Normal, "Normal"), (SpeedLevel.Fast, Loc.T("Schnell", "Fast"))], selected, onSelect, group);

    /// <summary>Toggle card with icon, title and subtitle.
    /// Karte mit Symbol, Titel und Untertitel zum Ein-/Ausschalten.</summary>
    public static ToggleButton Chip(Geometry icon, string title, string subtitle, bool isChecked, Action<bool> onToggle, double width = 250)
    {
        var grid = new Grid { Width = width };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var ic = Icon(icon, 26, Brush("TextDim"));
        ic.Margin = new Thickness(0, 0, 12, 0);
        grid.Children.Add(ic);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = subtitle, Foreground = Brush("TextMute"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var chip = new ToggleButton { Style = Style("Chip"), Content = grid, IsChecked = isChecked };
        void Paint() => ic.Stroke = chip.IsChecked == true ? Brush("Accent") : Brush("TextDim");
        Paint();
        chip.Checked += (_, _) => { Paint(); onToggle(true); };
        chip.Unchecked += (_, _) => { Paint(); onToggle(false); };
        return chip;
    }

    public static SolidColorBrush ToBrush(Rgb c) => new(Color.FromRgb(c.R, c.G, c.B));

    public static ScrollViewer Scroll(UIElement content) => new()
    {
        Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 10, 0),
    };
}

interface IPage
{
    string Title { get; }
    void Rebuild();
}
