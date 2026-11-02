using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Borealis.App;

/// <summary>Renders the main window with every page as PNG (design check without screen capture).
/// Rendert das Hauptfenster mit jeder Seite als PNG (Designprüfung ohne Bildschirmfoto).</summary>
static class Screenshots
{
    public static async Task RenderAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        await AppState.Instance.RefreshAsync();
        var w = new MainWindow { Left = -20000, Top = -20000, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
        w.Show();
        await Task.Delay(1500);
        var navs = new[] { "NavSync", "NavDevices", "NavFan", "NavSettings" };
        foreach (var nav in navs)
        {
            ((RadioButton)w.FindName(nav)).IsChecked = true;
            await Task.Delay(400);
            Save(w, Path.Combine(folder, nav + ".png"));
        }
        // Context menu of the first zone (popups aren't in the window image)
        // Kontextmenü der ersten Zone (Popups fehlen im Fensterbild)
        ((RadioButton)w.FindName("NavDevices")).IsChecked = true;
        await Task.Delay(400);
        if (FindAll<System.Windows.Controls.Primitives.ToggleButton>(w).FirstOrDefault(t => t.ContextMenu != null)?.ContextMenu is { } menu)
        {
            menu.ApplyTemplate();
            menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            menu.Arrange(new Rect(menu.DesiredSize));
            menu.UpdateLayout();
            var bmp = new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth), (int)Math.Ceiling(menu.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bmp.Render(menu);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var fs = File.Create(Path.Combine(folder, "NavDevicesMenu.png"));
            enc.Save(fs);
        }
        w.Devices.Select("Argb", ledCountTab: true);
        await Task.Delay(400);
        Save(w, Path.Combine(folder, "NavDevicesLeds.png"));
        ((RadioButton)w.FindName("NavSync")).IsChecked = true;
        w.Lighting.ShowCalibration();
        await Task.Delay(400);
        Save(w, Path.Combine(folder, "NavSyncCalibration.png"));
        w.Lighting.ShowEffects();
        await Task.Delay(400);
        Save(w, Path.Combine(folder, "NavSyncEffects.png"));
        w.AllowClose = true;
        w.Close();
    }

    static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var deeper in FindAll<T>(child)) yield return deeper;
        }
    }

    static void Save(Window w, string path)
    {
        var root = (FrameworkElement)w.Content;
        root.UpdateLayout();
        var bmp = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var bg = new DrawingVisual();
        using (var dc = bg.RenderOpen())
            dc.DrawRectangle(w.Background, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        bmp.Render(bg);
        bmp.Render(root);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}
