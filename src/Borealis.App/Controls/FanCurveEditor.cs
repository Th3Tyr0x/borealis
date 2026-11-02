using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Borealis.Core.Profiles;

namespace Borealis.App.Controls;

/// <summary>
/// Lüfterkurve mit vier verschiebbaren Punkten (Temperatur → Leistung). Der letzte Punkt liegt fest bei 100 %;
/// Temperaturen steigen, die Leistung fällt nicht (<see cref="FanSettings.Normalize"/>). Meldet die Kurve erst beim
/// Loslassen (<see cref="CurveCommitted"/>), damit der Chip nicht bei jeder Mausbewegung beschrieben wird.
/// </summary>
public sealed class FanCurveEditor : UserControl
{
    const double W = 660, H = 270, Left = 44, Bottom = 30, Top = 12, Right = 76;
    const int TMin = 20, TMax = 100;

    readonly Canvas canvas = new() { Width = W, Height = H, Background = Brushes.Transparent, ClipToBounds = false };
    readonly Polyline line = new() { StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round };
    readonly Polygon area = new();
    // Vergleichskurve (gestrichelt), z. B. derselbe Modus ohne Auto-Tuning
    readonly Polyline reference = new() { StrokeThickness = 1.5, StrokeDashArray = [5, 4], StrokeLineJoin = PenLineJoin.Round };
    readonly Line marker = new() { StrokeThickness = 1.5, StrokeDashArray = [4, 3] };
    readonly TextBlock markerText = new() { FontSize = 11 };
    readonly Ellipse markerDot = new() { Width = 10, Height = 10 };
    readonly Line floor = new() { StrokeThickness = 1.2, StrokeDashArray = [2, 3] };
    readonly TextBlock floorText = new() { FontSize = 11 };
    // Rechte Achse: gemessene Drehzahl je Leistung (nach dem Auto-Tuning)
    readonly TextBlock[] rpmLabels = new TextBlock[6];
    readonly TextBlock dragText = new() { FontSize = 12, FontWeight = FontWeights.SemiBold, Visibility = Visibility.Collapsed };
    readonly Ellipse[] handles = new Ellipse[4];
    List<FanPoint> points = FanSettings.Normalize([]);
    int dragging = -1;
    bool readOnly;
    double? currentTemp;
    FanTuning? tuning;

    /// <summary>Kurve wurde verändert (nach dem Loslassen eines Punktes).</summary>
    public event Action<List<FanPoint>>? CurveCommitted;

    public FanCurveEditor()
    {
        var accent = (Brush)Application.Current.FindResource("Accent");
        var soft = (Brush)Application.Current.FindResource("AccentSoft");
        var lineBrush = (Brush)Application.Current.FindResource("Line");
        var mute = (Brush)Application.Current.FindResource("TextMute");
        line.Stroke = accent;
        area.Fill = soft;
        marker.Stroke = (Brush)Application.Current.FindResource("TextDim");
        markerText.Foreground = (Brush)Application.Current.FindResource("TextDim");
        markerDot.Fill = (Brush)Application.Current.FindResource("Text");
        dragText.Foreground = (Brush)Application.Current.FindResource("Text");

        // Raster: alle 10 °C und 20 %
        for (int t = TMin; t <= TMax; t += 10)
        {
            canvas.Children.Add(new Line { X1 = X(t), X2 = X(t), Y1 = Y(0), Y2 = Y(100), Stroke = lineBrush, StrokeThickness = 1 });
            var label = new TextBlock { Text = $"{t}°", FontSize = 11, Foreground = mute };
            Canvas.SetLeft(label, X(t) - 9);
            Canvas.SetTop(label, Y(0) + 6);
            canvas.Children.Add(label);
        }
        for (int d = 0; d <= 100; d += 20)
        {
            canvas.Children.Add(new Line { X1 = X(TMin), X2 = X(TMax), Y1 = Y(d), Y2 = Y(d), Stroke = lineBrush, StrokeThickness = 1 });
            var label = new TextBlock { Text = $"{d} %", FontSize = 11, Foreground = mute, Width = Left - 8, TextAlignment = TextAlignment.Right };
            Canvas.SetLeft(label, 0);
            Canvas.SetTop(label, Y(d) - 8);
            canvas.Children.Add(label);
            var rpmLabel = new TextBlock { FontSize = 11, Foreground = mute };
            Canvas.SetLeft(rpmLabel, X(TMax) + 8);
            Canvas.SetTop(rpmLabel, Y(d) - 8);
            canvas.Children.Add(rpmLabel);
            rpmLabels[d / 20] = rpmLabel;
        }
        var warn = (Brush)Application.Current.FindResource("Warn");
        floor.Stroke = warn;
        floorText.Foreground = warn;
        canvas.Children.Add(area);
        reference.Stroke = (Brush)Application.Current.FindResource("TextMute");
        canvas.Children.Add(reference);
        canvas.Children.Add(floor);
        canvas.Children.Add(floorText);
        canvas.Children.Add(line);
        canvas.Children.Add(marker);
        canvas.Children.Add(markerText);
        canvas.Children.Add(markerDot);
        for (int i = 0; i < 4; i++)
        {
            var h = new Ellipse { Width = 16, Height = 16, Fill = accent, Stroke = Brushes.White, StrokeThickness = 2.5, Cursor = Cursors.SizeAll };
            int index = i;
            h.MouseLeftButtonDown += (_, e) =>
            {
                if (readOnly) return;
                dragging = index;
                h.CaptureMouse();
                e.Handled = true;
            };
            h.MouseMove += (_, e) => { if (dragging == index) Drag(e.GetPosition(canvas)); };
            h.MouseLeftButtonUp += (_, _) =>
            {
                if (dragging != index) return;
                dragging = -1;
                h.ReleaseMouseCapture();
                dragText.Visibility = Visibility.Collapsed;
                CurveCommitted?.Invoke(points.Select(p => new FanPoint(p.Temp, p.Duty)).ToList());
            };
            handles[i] = h;
            canvas.Children.Add(h);
        }
        canvas.Children.Add(dragText);
        Content = canvas;
        Redraw();
    }

    /// <summary>Kurve anzeigen; <paramref name="editable"/> = Punkte verschiebbar.</summary>
    /// <summary>Vergleichskurve gestrichelt einblenden; null blendet sie aus.</summary>
    public void SetReference(IReadOnlyList<FanPoint>? curve)
    {
        if (curve == null) { reference.Visibility = Visibility.Collapsed; return; }
        var pts = new PointCollection { new(X(TMin), Y(curve[0].Duty)) };
        foreach (var p in curve) pts.Add(new Point(X(p.Temp), Y(p.Duty)));
        pts.Add(new Point(X(TMax), Y(100)));
        reference.Points = pts;
        reference.Visibility = Visibility.Visible;
    }

    public void SetCurve(IReadOnlyList<FanPoint> curve, bool editable)
    {
        if (dragging >= 0) return;
        points = FanSettings.Normalize(curve);
        readOnly = !editable;
        foreach (var h in handles) h.Cursor = editable ? Cursors.SizeAll : Cursors.Arrow;
        Redraw();
    }

    /// <summary>Aktuelle Temperatur der Quelle als Markierung (null = ausblenden).</summary>
    public double? CurrentTemp
    {
        get => currentTemp;
        set { currentTemp = value; Redraw(); }
    }

    /// <summary>Name der Temperaturquelle in der Markierung („CPU“, „Mainboard“ …).</summary>
    public string SourceName
    {
        get => sourceName;
        set { sourceName = value; Redraw(); }
    }
    string sourceName = "CPU";

    /// <summary>Ergebnis des Auto-Tunings: zeigt die Mindestleistung und die geschätzte Drehzahl.</summary>
    public FanTuning? Tuning
    {
        get => tuning;
        set { tuning = value; Redraw(); }
    }

    static double X(double temp) => Left + (Math.Clamp(temp, TMin, TMax) - TMin) / (TMax - TMin) * (W - Left - Right);
    static double Y(double duty) => Top + (1 - Math.Clamp(duty, 0, 100) / 100.0) * (H - Top - Bottom);
    static double TempAt(double x) => TMin + (x - Left) / (W - Left - Right) * (TMax - TMin);
    static double DutyAt(double y) => (1 - (y - Top) / (H - Top - Bottom)) * 100;

    void Drag(Point p)
    {
        var next = points.Select(x => new FanPoint(x.Temp, x.Duty)).ToList();
        next[dragging].Temp = (int)Math.Round(TempAt(p.X));
        // Der letzte Punkt bleibt bei 100 %, nur die Temperatur ist verschiebbar
        if (dragging < 3) next[dragging].Duty = (int)Math.Round(DutyAt(p.Y));
        // Nachbarn nicht überholen: Temperatur zwischen den Nachbarn, Leistung zwischen deren Leistung
        int lowT = dragging == 0 ? FanSettings.MinTemp : next[dragging - 1].Temp + 1;
        int highT = dragging == 3 ? FanSettings.MaxTemp : next[dragging + 1].Temp - 1;
        next[dragging].Temp = Math.Clamp(next[dragging].Temp, lowT, Math.Max(lowT, highT));
        int lowD = dragging == 0 ? 0 : next[dragging - 1].Duty;
        int highD = dragging == 3 ? 100 : next[dragging + 1].Duty;
        next[dragging].Duty = Math.Clamp(next[dragging].Duty, lowD, Math.Max(lowD, highD));
        points = FanSettings.Normalize(next);
        var q = points[dragging];
        dragText.Text = $"{q.Temp} °C · {q.Duty} %" + (tuning != null ? $" ≈ {tuning.RpmAt(q.Duty):N0} {Loc.T("U/min", "RPM")}" : "");
        dragText.Visibility = Visibility.Visible;
        Canvas.SetLeft(dragText, Math.Min(X(q.Temp) + 12, W - 90));
        Canvas.SetTop(dragText, Math.Max(Y(q.Duty) - 26, 0));
        Redraw();
    }

    void Redraw()
    {
        var pts = new PointCollection { new(X(TMin), Y(points[0].Duty)) };
        foreach (var p in points) pts.Add(new Point(X(p.Temp), Y(p.Duty)));
        pts.Add(new Point(X(TMax), Y(100)));
        line.Points = pts;
        var fill = new PointCollection(pts) { new Point(X(TMax), Y(0)), new Point(X(TMin), Y(0)) };
        area.Points = fill;
        for (int i = 0; i < 4; i++)
        {
            Canvas.SetLeft(handles[i], X(points[i].Temp) - 8);
            Canvas.SetTop(handles[i], Y(points[i].Duty) - 8);
            handles[i].Opacity = readOnly ? 0.6 : 1;
        }
        line.Opacity = readOnly ? 0.6 : 1;

        for (int i = 0; i < rpmLabels.Length; i++)
            rpmLabels[i].Text = tuning != null && tuning.Rpm.Length > 1 ? $"{tuning.RpmAt(i * 20):N0} {Loc.T("U/min", "RPM")}" : "";

        // Untergrenze: sicher laufen/anlaufen, bei Lüftern ohne Stillstand ab wann sie schneller werden
        int safe = tuning?.SafeDuty ?? 0, effective = tuning?.EffectiveDuty ?? 0;
        int lowest = Math.Max(safe, effective);
        floor.Visibility = floorText.Visibility = lowest > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (lowest > 0)
        {
            floor.X1 = X(TMin);
            floor.X2 = X(TMax);
            floor.Y1 = floor.Y2 = Y(lowest);
            floorText.Text = safe >= effective ? Loc.T($"läuft sicher ab {safe} %", $"runs safely from {safe} %") : Loc.T($"ab {effective} % schneller", $"faster from {effective} %");
            Canvas.SetLeft(floorText, X(TMax) - 112);
            Canvas.SetTop(floorText, Y(lowest) - 17);
        }

        bool show = currentTemp is { } now && now >= TMin;
        marker.Visibility = markerText.Visibility = markerDot.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
        {
            double t = currentTemp!.Value, x = X(t);
            int duty = FanSettings.DutyAt(points, t);
            marker.X1 = marker.X2 = x;
            marker.Y1 = Y(0);
            marker.Y2 = Y(100);
            markerText.Text = $"{sourceName} {t:0} °C → {duty} %" + (tuning != null ? $" ≈ {tuning.RpmAt(duty):N0} {Loc.T("U/min", "RPM")}" : "");
            Canvas.SetLeft(markerText, x + 6 > W - 210 ? x - 216 : x + 6);
            Canvas.SetTop(markerText, Top);
            Canvas.SetLeft(markerDot, x - 5);
            Canvas.SetTop(markerDot, Y(duty) - 5);
        }
    }
}
