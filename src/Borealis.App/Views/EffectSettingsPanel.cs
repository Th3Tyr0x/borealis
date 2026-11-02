using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;
using Borealis.App.Controls;
using Borealis.Core.Lighting;
using Borealis.Core.Profiles;

namespace Borealis.App.Views;

/// <summary>What the panel edits: the Borealis Sync setting or a device group.
/// Was das Feld bearbeitet: die Borealis-Sync-Einstellung oder eine Gerätegruppe.</summary>
interface IEffectTarget
{
    FirmwareEffect Effect { get; }
    int Speed { get; set; }
    EffectDirection Direction { get; set; }
    ColorMode ColorMode { get; set; }
    Rgb Color1 { get; set; }
    Rgb Color2 { get; set; }
    Rgb Background { get; set; }
    bool BackgroundEnabled { get; set; }
    bool BackgroundRainbow { get; set; }
    int BackgroundLevel { get; set; }
    int Size { get; set; }
    int Density { get; set; }
    int ChangeInterval { get; set; }
    int Transition { get; set; }
    int Mode { get; set; }
    /// <summary>Reset all effect settings (the effect stays).
    /// Alle Effekt-Einstellungen zurücksetzen (der Effekt bleibt).</summary>
    void ResetOptions();
    /// <summary>False if no zone is selected (color 1 can't be set then).
    /// False, wenn keine Zone gewählt ist (Farbe 1 geht dann nicht).</summary>
    bool CanEditColor1 { get; }
}

sealed class SyncTarget(SyncSetting s) : IEffectTarget
{
    public FirmwareEffect Effect => s.Effect;
    public int Speed { get => s.EffectiveSpeed; set => s.SpeedStep = value; }
    public EffectDirection Direction { get => s.Direction; set => s.Direction = value; }
    public ColorMode ColorMode { get => s.ColorMode; set => s.ColorMode = value; }
    public Rgb Color1 { get => Rgb.TryParse(s.Color, out var c) ? c : Rgb.White; set => s.Color = value.ToString(); }
    public Rgb Color2 { get => Rgb.TryParse(s.Color2, out var c) ? c : Rgb.White; set => s.Color2 = value.ToString(); }
    public Rgb Background { get => Rgb.TryParse(s.Background, out var c) ? c : Rgb.Black; set => s.Background = value.ToString(); }
    public bool BackgroundEnabled { get => s.BackgroundEnabled; set => s.BackgroundEnabled = value; }
    public bool BackgroundRainbow { get => s.BackgroundRainbow; set => s.BackgroundRainbow = value; }
    public int BackgroundLevel { get => s.BackgroundLevel; set => s.BackgroundLevel = value; }
    public int Size { get => s.Size; set => s.Size = value; }
    public int Density { get => s.Density; set => s.Density = value; }
    public int ChangeInterval { get => s.ChangeInterval; set => s.ChangeInterval = value; }
    public int Transition { get => s.Transition; set => s.Transition = value; }
    public int Mode { get => s.Mode; set => s.Mode = value; }
    public void ResetOptions() => s.ResetOptions();
    public bool CanEditColor1 => true;
}

/// <summary>Device group: color 1 applies to the selected zones; <paramref name="beforeEdit"/> may leave Borealis Sync.
/// Gerätegruppe: Farbe 1 gilt für die gewählten Zonen; <paramref name="beforeEdit"/> löst ggf. aus Borealis Sync.</summary>
sealed class GroupTarget(Func<GroupSetting> group, Func<IReadOnlyCollection<string>> selectedZones, IReadOnlyList<string> zoneOrder, Action beforeEdit) : IEffectTarget
{
    public FirmwareEffect Effect => group().Effect;
    public int Speed { get => group().EffectiveSpeed; set { beforeEdit(); group().SpeedStep = value; } }
    public EffectDirection Direction { get => group().Direction; set { beforeEdit(); group().Direction = value; } }
    public ColorMode ColorMode { get => group().ColorMode; set { beforeEdit(); group().ColorMode = value; } }
    public Rgb Color1
    {
        get
        {
            var sel = selectedZones();
            var first = zoneOrder.FirstOrDefault(sel.Contains);
            return first != null ? group().ColorOf(first) : Rgb.White;
        }
        set
        {
            beforeEdit();
            foreach (var id in selectedZones()) group().Colors[id] = value.ToString();
        }
    }
    public Rgb Color2 { get => group().SecondColor; set { beforeEdit(); group().Color2 = value.ToString(); } }
    public Rgb Background { get => Rgb.TryParse(group().Background, out var c) ? c : Rgb.Black; set { beforeEdit(); group().Background = value.ToString(); } }
    public bool BackgroundEnabled { get => group().BackgroundEnabled; set { beforeEdit(); group().BackgroundEnabled = value; } }
    public bool BackgroundRainbow { get => group().BackgroundRainbow; set { beforeEdit(); group().BackgroundRainbow = value; } }
    public int BackgroundLevel { get => group().BackgroundLevel; set { beforeEdit(); group().BackgroundLevel = value; } }
    public int Size { get => group().Size; set { beforeEdit(); group().Size = value; } }
    public int Density { get => group().Density; set { beforeEdit(); group().Density = value; } }
    public int ChangeInterval { get => group().ChangeInterval; set { beforeEdit(); group().ChangeInterval = value; } }
    public int Transition { get => group().Transition; set { beforeEdit(); group().Transition = value; } }
    public int Mode { get => group().Mode; set { beforeEdit(); group().Mode = value; } }
    public void ResetOptions() { beforeEdit(); group().ResetOptions(); }
    public bool CanEditColor1 => selectedZones().Count > 0;
}

/// <summary>
/// Right column of the effect pages: color mode, colors, random change, speed, direction, size, density, brightness;
/// shows only what the effect supports.
/// Rechte Spalte der Effektseiten: Farbmodus, Farben, Zufallswechsel, Tempo, Richtung, Größe, Dichte, Helligkeit;
/// zeigt nur, was der Effekt kann.
/// </summary>
sealed class EffectSettingsPanel : StackPanel
{
    static AppState S => AppState.Instance;

    enum Slot { Color1, Color2, Background }

    readonly IEffectTarget target;
    readonly bool firmwareOnly;
    readonly Action changed;
    readonly ColorPicker picker = new();
    readonly StackPanel colorSection = new();
    readonly StackPanel modeSection = new() { Margin = new Thickness(0, 0, 0, 20) };
    FirmwareEffect modeEffect = (FirmwareEffect)0xFE;
    readonly FrameworkElement modeRow;
    readonly WrapPanel slots = new() { Margin = new Thickness(0, 0, 0, 12) };
    readonly CheckBox backgroundSwitch = new() { Content = Loc.T("Hintergrund", "Background"), Margin = new Thickness(0, 0, 0, 12) };
    readonly StackPanel backgroundOptions = new() { Margin = new Thickness(0, 0, 0, 12) };
    readonly StackPanel backgroundLevelRow = new() { Margin = new Thickness(0, 10, 0, 0) };
    readonly ToggleButton slot1, slot2, slotBg;
    readonly TextBlock slot1Text = new(), slot2Text = new();
    readonly Ellipse slot1Dot = Dot(), slot2Dot = Dot(), slotBgDot = Dot();
    readonly StackPanel randomSection = new() { Margin = new Thickness(0, 14, 0, 0) };
    readonly StackPanel speedSection = Section();
    readonly StackPanel directionSection = Section();
    readonly StackPanel sizeSection = Section();
    readonly TextBlock sizeTitle;
    readonly StackPanel densitySection = Section();
    readonly StackPanel brightnessSection = Section();
    readonly TextBlock noOptions;
    readonly TextBlock hardwareHint;
    Slot editing = Slot.Color1;
    bool loading;

    /// <param name="rebuild">Rebuild the page (after a reset, so all sliders show defaults).
    /// Seite neu aufbauen (nach dem Zurücksetzen, damit alle Regler die Standardwerte zeigen).</param>
    public EffectSettingsPanel(IEffectTarget target, bool firmwareOnly, Action changed, Action? rebuild = null)
    {
        this.target = target;
        this.firmwareOnly = firmwareOnly;
        this.changed = changed;
        string id = GetHashCode().ToString();

        // Effect variant (music: level/spectrum/pulse/bass, smart: CPU/RAM …), built in Refresh()
        // Unterart des Effekts (Musik: Pegel/Spektrum/Puls/Bass, Smart: CPU/RAM …), aufgebaut in Refresh()
        Children.Add(modeSection);

        // Color: mode, slots (color 1/2, background), wheel, random change
        // Farbe: Modus, Farbplätze (Farbe 1/2, Hintergrund), Farbrad, Zufallswechsel
        colorSection.Children.Add(Ui.Section(Loc.T("Farbe", "Color")));
        modeRow = Ui.Segments([(ColorMode.Single, Loc.T("Einzelfarbe", "Single color")), (ColorMode.Random, Loc.T("Zufall", "Random")), (ColorMode.Gradient, Loc.T("Verlauf", "Gradient"))],
            target.ColorMode, m => { if (loading) return; target.ColorMode = m; editing = Slot.Color1; Refresh(); changed(); }, "ColorMode" + id);
        modeRow.Margin = new Thickness(0, 0, 0, 12);
        colorSection.Children.Add(modeRow);
        slot1 = SlotButton(slot1Text, slot1Dot, Slot.Color1);
        slot2 = SlotButton(slot2Text, slot2Dot, Slot.Color2);
        slotBg = SlotButton(new TextBlock { Text = Loc.T("Hintergrund", "Background") }, slotBgDot, Slot.Background);
        slots.Children.Add(slot1);
        slots.Children.Add(slot2);
        slots.Children.Add(slotBg);
        // Background on/off: off = black; the chosen color stays saved
        // Hintergrund an/aus: aus = schwarz; die gewählte Farbe bleibt gespeichert
        backgroundSwitch.Style = Ui.Style("Switch");
        backgroundSwitch.IsChecked = target.BackgroundEnabled;
        backgroundSwitch.Click += (_, _) =>
        {
            target.BackgroundEnabled = backgroundSwitch.IsChecked == true;
            editing = target.BackgroundEnabled && !target.BackgroundRainbow ? Slot.Background : Slot.Color1;
            Refresh();
            changed();
        };
        colorSection.Children.Add(backgroundSwitch);
        // Background as a color or a dimmed moving rainbow
        // Hintergrund als Farbe oder gedimmter, wandernder Regenbogen
        var bgMode = Ui.Segments([(false, Loc.T("Farbe", "Color")), (true, Loc.T("Regenbogen", "Rainbow"))], target.BackgroundRainbow, r =>
        {
            if (loading) return;
            target.BackgroundRainbow = r;
            editing = r ? Slot.Color1 : Slot.Background;
            Refresh();
            changed();
        }, "BgMode" + id);
        backgroundOptions.Children.Add(bgMode);
        backgroundLevelRow.Children.Add(SliderRow(Loc.T("Stärke", "Strength"), 1, 10, target.BackgroundLevel, v => $"{v * 10} %", v => target.BackgroundLevel = v));
        backgroundOptions.Children.Add(backgroundLevelRow);
        colorSection.Children.Add(backgroundOptions);
        colorSection.Children.Add(slots);
        colorSection.Children.Add(picker);
        picker.ColorChanged += c =>
        {
            if (loading) return;
            switch (editing)
            {
                case Slot.Color2: target.Color2 = c; break;
                case Slot.Background: target.Background = c; break;
                default:
                    if (!target.CanEditColor1) return;
                    target.Color1 = c;
                    break;
            }
            UpdateSlots();
            changed();
        };
        randomSection.Children.Add(SliderRow(Loc.T("Farbwechsel alle", "Color change every"), 1, 30, target.ChangeInterval, v => $"{v} s", v => target.ChangeInterval = v));
        randomSection.Children.Add(SliderRow(Loc.T("Übergang", "Transition"), 0, 10, target.Transition, v => v == 0 ? Loc.T("hart", "hard") : v >= 10 ? Loc.T("fließend", "smooth") : $"{v * 10} %", v => target.Transition = v));
        colorSection.Children.Add(randomSection);
        Children.Add(colorSection);

        speedSection.Children.Add(SliderRow(Loc.T("Geschwindigkeit", "Speed"), 1, 10, target.Speed, v => v <= 3 ? Loc.T("langsam", "slow") : v >= 8 ? Loc.T("schnell", "fast") : Loc.T("mittel", "medium"), v => target.Speed = v));
        Children.Add(speedSection);

        directionSection.Children.Add(Ui.Section(Loc.T("Richtung", "Direction")));
        directionSection.Children.Add(Ui.Segments([(EffectDirection.Left, Loc.T("◀  Links", "◀  Left")), (EffectDirection.Right, Loc.T("Rechts  ▶", "Right  ▶"))],
            target.Direction, d => { if (loading) return; target.Direction = d; changed(); }, "Direction" + id));
        Children.Add(directionSection);

        var sizeRow = SliderRow(Loc.T("Größe", "Size"), 1, 10, target.Size, v => v.ToString(), v => target.Size = v);
        sizeTitle = (TextBlock)((DockPanel)sizeRow.Children[0]).Children[1];
        sizeSection.Children.Add(sizeRow);
        Children.Add(sizeSection);

        densitySection.Children.Add(SliderRow(Loc.T("Dichte", "Density"), 1, 10, target.Density, v => v.ToString(), v => target.Density = v));
        Children.Add(densitySection);

        // Brightness applies to the whole profile / Helligkeit gilt für das ganze Profil
        brightnessSection.Children.Add(SliderRow(Loc.T("Helligkeit", "Brightness"), 5, 100, S.Profile.Brightness, v => $"{v} %", v => S.Profile.Brightness = v, step: 5));
        Children.Add(brightnessSection);

        noOptions = Ui.Hint(Loc.T("Dieser Effekt hat keine weiteren Einstellungen.", "This effect has no further settings."), 0);
        Children.Add(noOptions);
        hardwareHint = Ui.Hint("", 16);
        Children.Add(hardwareHint);

        var reset = new Button { Content = Loc.T("Auf Standard zurücksetzen", "Reset to default"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 22, 0, 0) };
        reset.Click += (_, _) =>
        {
            target.ResetOptions();
            if (!firmwareOnly) S.Profile.Brightness = 100;
            editing = Slot.Color1;
            changed();
            rebuild?.Invoke();
        };
        Children.Add(reset);

        Refresh();
    }

    static StackPanel Section() => new() { Margin = new Thickness(0, 20, 0, 0) };

    static Ellipse Dot() => new() { Width = 14, Height = 14, Margin = new Thickness(0, 0, 8, 0) };

    ToggleButton SlotButton(TextBlock text, Ellipse dot, Slot slot)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        dot.Stroke = Ui.Brush("Line");
        text.VerticalAlignment = VerticalAlignment.Center;
        content.Children.Add(dot);
        content.Children.Add(text);
        var b = new ToggleButton { Style = Ui.Style("Chip"), Content = content, Padding = new Thickness(10, 6, 12, 6), Margin = new Thickness(0, 0, 8, 8) };
        b.Click += (_, _) => { editing = slot; Refresh(); };
        return b;
    }

    /// <summary>Heading with value display and slider.
    /// Überschrift mit Wertanzeige und Schieberegler.</summary>
    StackPanel SliderRow(string title, int min, int max, int value, Func<int, string> format, Action<int> set, int step = 1)
    {
        var row = new StackPanel();
        var head = new DockPanel();
        var valueText = new TextBlock { Text = format(Math.Clamp(value, min, max)), Foreground = Ui.Brush("TextDim"), FontSize = 12 };
        DockPanel.SetDock(valueText, Dock.Right);
        head.Children.Add(valueText);
        head.Children.Add(Ui.Section(title));
        row.Children.Add(head);
        var slider = new Slider { Minimum = min, Maximum = max, TickFrequency = step, IsSnapToTickEnabled = true, Value = Math.Clamp(value, min, max) };
        slider.ValueChanged += (_, e) =>
        {
            valueText.Text = format((int)e.NewValue);
            if (loading) return;
            set((int)e.NewValue);
            changed();
        };
        row.Children.Add(slider);
        return row;
    }

    void UpdateSlots()
    {
        slot1Dot.Fill = Ui.ToBrush(target.Color1);
        slot2Dot.Fill = Ui.ToBrush(target.Color2);
        slotBgDot.Fill = Ui.ToBrush(target.Background);
    }

    /// <summary>Call after an effect change or a new zone selection.
    /// Nach Effektwechsel oder neuer Zonenauswahl aufrufen.</summary>
    public void Refresh()
    {
        loading = true;
        var e = target.Effect;
        var caps = firmwareOnly
            ? new EffectCaps(FirmwareEffects.UsesColor(e), FirmwareEffects.HasSpeed(e), false, false, false, false)
            : SoftwareEffects.CapsOf(e);
        var mode = firmwareOnly || caps.TwoColors ? ColorMode.Single : target.ColorMode;
        bool showColor2 = caps.Color && (caps.TwoColors || mode == ColorMode.Gradient);
        bool bgAvailable = caps.Background && !firmwareOnly;
        bool bgOn = bgAvailable && target.BackgroundEnabled;
        bool showBg = bgOn && !target.BackgroundRainbow;
        if (editing == Slot.Color2 && !showColor2) editing = Slot.Color1;
        if (editing == Slot.Background && !showBg) editing = Slot.Color1;
        // With random there's nothing to pick for color 1, so edit the background
        // Bei Zufall gibt es für Farbe 1 nichts zu wählen, also gleich den Hintergrund
        if (editing == Slot.Color1 && (!caps.Color || mode == ColorMode.Random) && showBg) editing = Slot.Background;

        if (modeEffect != e)
        {
            modeEffect = e;
            modeSection.Children.Clear();
            if (caps.Modes is { Length: > 0 } modes && !firmwareOnly)
            {
                modeSection.Children.Add(Ui.Section(Loc.T("Modus", "Mode")));
                var row = Ui.Segments(modes.Select((label, i) => (i, Loc.Term(label))), Math.Clamp(target.Mode, 0, modes.Length - 1),
                    m => { if (loading) return; target.Mode = m; changed(); }, "Mode" + GetHashCode() + e);
                // Four modes don't fit in one row: two rows / Vier Modi passen nicht in eine Zeile: zwei Zeilen
                var wrap = new WrapPanel();
                foreach (var child in row.Children.Cast<UIElement>().ToList()) { row.Children.Remove(child); wrap.Children.Add(child); }
                modeSection.Children.Add(wrap);
            }
        }
        modeSection.Visibility = modeSection.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        colorSection.Visibility = caps.Color || bgAvailable ? Visibility.Visible : Visibility.Collapsed;
        backgroundSwitch.Visibility = bgAvailable ? Visibility.Visible : Visibility.Collapsed;
        backgroundSwitch.IsChecked = target.BackgroundEnabled;
        backgroundOptions.Visibility = bgOn ? Visibility.Visible : Visibility.Collapsed;
        backgroundLevelRow.Visibility = bgOn && target.BackgroundRainbow ? Visibility.Visible : Visibility.Collapsed;
        modeRow.Visibility = caps.Color && !firmwareOnly && !caps.TwoColors ? Visibility.Visible : Visibility.Collapsed;
        slot1Text.Text = caps.TwoColors ? Loc.Term(caps.Color1Label) : Loc.T("Farbe 1", "Color 1");
        slot2Text.Text = caps.TwoColors ? Loc.Term(caps.Color2Label) : Loc.T("Farbe 2", "Color 2");
        slot1.Visibility = caps.Color && mode != ColorMode.Random && (showColor2 || showBg) ? Visibility.Visible : Visibility.Collapsed;
        slot2.Visibility = showColor2 ? Visibility.Visible : Visibility.Collapsed;
        slotBg.Visibility = showBg ? Visibility.Visible : Visibility.Collapsed;
        slots.Visibility = slot1.Visibility == Visibility.Visible || showBg ? Visibility.Visible : Visibility.Collapsed;
        slot1.IsChecked = editing == Slot.Color1;
        slot2.IsChecked = editing == Slot.Color2;
        slotBg.IsChecked = editing == Slot.Background;
        UpdateSlots();
        // With random there's nothing to pick for color 1 (background still is)
        // Bei Zufall gibt es für Farbe 1 nichts zu wählen (Hintergrund schon)
        picker.Visibility = editing == Slot.Color1 && (mode == ColorMode.Random || !caps.Color) ? Visibility.Collapsed : Visibility.Visible;
        picker.SetColor(editing switch { Slot.Color2 => target.Color2, Slot.Background => target.Background, _ => target.Color1 });
        picker.IsEnabled = editing != Slot.Color1 || target.CanEditColor1;
        randomSection.Visibility = caps.Color && mode == ColorMode.Random ? Visibility.Visible : Visibility.Collapsed;

        speedSection.Visibility = caps.Speed ? Visibility.Visible : Visibility.Collapsed;
        directionSection.Visibility = caps.Direction ? Visibility.Visible : Visibility.Collapsed;
        sizeSection.Visibility = caps.Size ? Visibility.Visible : Visibility.Collapsed;
        sizeTitle.Text = Loc.Term(caps.SizeLabel);
        densitySection.Visibility = caps.Density ? Visibility.Visible : Visibility.Collapsed;
        brightnessSection.Visibility = firmwareOnly || e == FirmwareEffect.Off ? Visibility.Collapsed : Visibility.Visible;
        noOptions.Visibility = colorSection.Visibility == Visibility.Collapsed && speedSection.Visibility == Visibility.Collapsed
            && directionSection.Visibility == Visibility.Collapsed && brightnessSection.Visibility == Visibility.Collapsed
            && sizeSection.Visibility == Visibility.Collapsed && densitySection.Visibility == Visibility.Collapsed
            ? Visibility.Visible : Visibility.Collapsed;

        bool preferHw = S.Status?.Settings.PreferHardwareEffects == true;
        hardwareHint.Text = firmwareOnly
            ? Loc.T("Wird als Hardware-Effekt im Controller gespeichert: eine Farbe je Zone, drei Geschwindigkeitsstufen.",
                    "Stored as a hardware effect in the controller: one color per zone, three speed levels.")
            : e == FirmwareEffect.Music
                ? Loc.T("Reagiert auf den Ton des Standard-Wiedergabegeräts.", "Reacts to the sound of the default playback device.")
                : e == FirmwareEffect.Smart
                    ? Loc.T("Die Farbe folgt der CPU-Auslastung: von „Niedrige Last“ nach „Hohe Last“.", "The color follows the CPU load: from “Low” to “High”.")
                    : preferHw && !SoftwareEffects.IsSoftwareOnly(e) && e is not (FirmwareEffect.Static or FirmwareEffect.Off) && mode == ColorMode.Single
                        ? Loc.T("„Hardware-Effekte bevorzugen“ ist an: Richtung, Größe und feine Geschwindigkeit wirken nur eingeschränkt.",
                                "“Prefer hardware effects” is on: direction, size and fine speed have limited effect.")
                        : "";
        hardwareHint.Visibility = hardwareHint.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        loading = false;
    }
}
