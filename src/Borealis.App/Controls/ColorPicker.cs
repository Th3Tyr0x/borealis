using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Borealis.Core.Lighting;

namespace Borealis.App.Controls;

/// <summary>
/// Color picker: wheel (hue around, saturation outward), brightness slider, swatches, hex input; raises <see cref="ColorChanged"/> while dragging.
/// Farbwahl: Farbrad (Farbton im Kreis, Sättigung nach außen), Helligkeit, Farbfelder, Hex; meldet <see cref="ColorChanged"/> auch beim Ziehen.
/// </summary>
public sealed class ColorPicker : UserControl
{
    const int WheelSize = 210;
    static readonly string[] Presets = ["FF0000", "FF5A00", "FFC800", "7DFF00", "00FF50", "00FFE1", "0096FF", "0010FF", "8C00FF", "FF00C8", "FFFFFF"];

    readonly Image wheel = new() { Width = WheelSize, Height = WheelSize };
    readonly Ellipse wheelThumb = new() { Width = 16, Height = 16, Stroke = Brushes.White, StrokeThickness = 2.5, IsHitTestVisible = false };
    readonly Canvas wheelCanvas = new() { Width = WheelSize, Height = WheelSize, Background = Brushes.Transparent, Cursor = Cursors.Cross };
    readonly Border valueBar = new() { Height = 14, CornerRadius = new CornerRadius(7), Cursor = Cursors.Hand };
    readonly Ellipse valueThumb = new() { Width = 18, Height = 18, Stroke = Brushes.White, StrokeThickness = 2.5, IsHitTestVisible = false };
    readonly Canvas valueCanvas = new() { Height = 18, Background = Brushes.Transparent, Cursor = Cursors.Hand };
    readonly Border preview = new() { Width = 34, Height = 34, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
    readonly TextBox hexBox = new() { Width = 90, Margin = new Thickness(10, 0, 0, 0), VerticalContentAlignment = VerticalAlignment.Center };

    double hue, sat = 1, val = 1;
    bool updating;

    public event Action<Rgb>? ColorChanged;

    public ColorPicker()
    {
        wheel.Source = RenderWheel();
        wheelCanvas.Children.Add(wheel);
        wheelCanvas.Children.Add(wheelThumb);
        wheelCanvas.MouseLeftButtonDown += (_, e) => { wheelCanvas.CaptureMouse(); PickWheel(e.GetPosition(wheelCanvas)); };
        wheelCanvas.MouseMove += (_, e) => { if (wheelCanvas.IsMouseCaptured) PickWheel(e.GetPosition(wheelCanvas)); };
        wheelCanvas.MouseLeftButtonUp += (_, _) => wheelCanvas.ReleaseMouseCapture();

        var valueGrid = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        valueBar.VerticalAlignment = VerticalAlignment.Center;
        valueGrid.Children.Add(valueBar);
        valueCanvas.Children.Add(valueThumb);
        valueGrid.Children.Add(valueCanvas);
        valueCanvas.MouseLeftButtonDown += (_, e) => { valueCanvas.CaptureMouse(); PickValue(e.GetPosition(valueCanvas)); };
        valueCanvas.MouseMove += (_, e) => { if (valueCanvas.IsMouseCaptured) PickValue(e.GetPosition(valueCanvas)); };
        valueCanvas.MouseLeftButtonUp += (_, _) => valueCanvas.ReleaseMouseCapture();
        valueCanvas.SizeChanged += (_, _) => UpdateVisuals();

        var swatches = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        foreach (var hex in Presets)
        {
            var c = Rgb.Parse(hex);
            var b = new Border
            {
                Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 7, 7), Cursor = Cursors.Hand,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(c.R, c.G, c.B)),
                BorderBrush = (Brush)Application.Current.FindResource("Line"), BorderThickness = new Thickness(1),
                ToolTip = "#" + hex,
            };
            b.MouseLeftButtonUp += (_, _) => SetColor(c, notify: true);
            swatches.Children.Add(b);
        }

        preview.BorderBrush = (Brush)Application.Current.FindResource("Line");
        hexBox.LostKeyboardFocus += (_, _) => CommitHex();
        hexBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) CommitHex(); };
        var hexRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        hexRow.Children.Add(preview);
        hexRow.Children.Add(hexBox);

        var root = new StackPanel { Width = WheelSize };
        root.Children.Add(wheelCanvas);
        root.Children.Add(valueGrid);
        root.Children.Add(swatches);
        root.Children.Add(hexRow);
        Content = root;
        UpdateVisuals();
    }

    public Rgb Color => FromHsv(hue, sat, val);

    /// <summary>Set the color (no event unless <paramref name="notify"/>).
    /// Farbe setzen (ohne Ereignis, außer <paramref name="notify"/>).</summary>
    public void SetColor(Rgb c, bool notify = false)
    {
        (hue, sat, val) = ToHsv(c);
        UpdateVisuals();
        if (notify) ColorChanged?.Invoke(Color);
    }

    void PickWheel(Point p)
    {
        double r = WheelSize / 2.0;
        double dx = p.X - r, dy = p.Y - r;
        double dist = Math.Min(Math.Sqrt(dx * dx + dy * dy), r);
        hue = (Math.Atan2(dy, dx) / (2 * Math.PI) + 1) % 1.0;
        sat = dist / r;
        if (val < 0.05) val = 1;
        UpdateVisuals();
        ColorChanged?.Invoke(Color);
    }

    void PickValue(Point p)
    {
        double w = Math.Max(1, valueCanvas.ActualWidth - 18);
        val = Math.Clamp((p.X - 9) / w, 0, 1);
        UpdateVisuals();
        ColorChanged?.Invoke(Color);
    }

    void CommitHex()
    {
        if (updating) return;
        if (Rgb.TryParse(hexBox.Text, out var c)) SetColor(c, notify: true);
        else UpdateVisuals();
    }

    void UpdateVisuals()
    {
        updating = true;
        double r = WheelSize / 2.0;
        double angle = hue * 2 * Math.PI;
        Canvas.SetLeft(wheelThumb, r + Math.Cos(angle) * sat * r - wheelThumb.Width / 2);
        Canvas.SetTop(wheelThumb, r + Math.Sin(angle) * sat * r - wheelThumb.Height / 2);

        var full = FromHsv(hue, sat, 1);
        valueBar.Background = new LinearGradientBrush(Colors.Black, System.Windows.Media.Color.FromRgb(full.R, full.G, full.B), 0);
        double w = Math.Max(1, valueCanvas.ActualWidth - 18);
        Canvas.SetLeft(valueThumb, val * w);

        var c = Color;
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(c.R, c.G, c.B));
        wheelThumb.Fill = brush;
        valueThumb.Fill = brush;
        preview.Background = brush;
        if (!hexBox.IsKeyboardFocused) hexBox.Text = "#" + c;
        updating = false;
    }

    static WriteableBitmap RenderWheel()
    {
        var bmp = new WriteableBitmap(WheelSize, WheelSize, 96, 96, PixelFormats.Bgra32, null);
        var px = new byte[WheelSize * WheelSize * 4];
        double r = WheelSize / 2.0;
        for (int y = 0; y < WheelSize; y++)
        for (int x = 0; x < WheelSize; x++)
        {
            double dx = x + 0.5 - r, dy = y + 0.5 - r;
            double d = Math.Sqrt(dx * dx + dy * dy);
            if (d > r) continue;
            var c = FromHsv((Math.Atan2(dy, dx) / (2 * Math.PI) + 1) % 1.0, d / r, 1);
            // Soft edge / Weicher Rand
            byte a = (byte)(Math.Clamp(r - d, 0, 1) * 255);
            int i = (y * WheelSize + x) * 4;
            px[i] = (byte)(c.B * a / 255); px[i + 1] = (byte)(c.G * a / 255); px[i + 2] = (byte)(c.R * a / 255); px[i + 3] = a;
        }
        bmp.WritePixels(new Int32Rect(0, 0, WheelSize, WheelSize), px, WheelSize * 4, 0);
        return bmp;
    }

    static Rgb FromHsv(double h, double s, double v)
    {
        var full = Rgb.FromHue(h);
        byte Mix(byte channel) => (byte)Math.Round((255 - (255 - channel) * s) * v);
        return new Rgb(Mix(full.R), Mix(full.G), Mix(full.B));
    }

    static (double h, double s, double v) ToHsv(Rgb c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = 0;
        if (d > 0)
        {
            if (max == r) h = ((g - b) / d + 6) % 6;
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h /= 6;
        }
        return (h, max == 0 ? 0 : d / max, max);
    }
}
