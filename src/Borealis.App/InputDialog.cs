using System.Windows;
using System.Windows.Controls;
using Borealis.App.Views;

namespace Borealis.App;

/// <summary>Kleiner Eingabedialog für einen Text (WPF hat keinen eingebauten).</summary>
static class InputDialog
{
    public static string? Show(Window owner, string title, string prompt, string initial)
    {
        var box = new TextBox { Text = initial, Margin = new Thickness(0, 6, 0, 10), MinWidth = 300 };
        var ok = new Button { Content = "OK", IsDefault = true, Style = Ui.Style("AccentButton"), Margin = new Thickness(0, 0, 8, 0), MinWidth = 80 };
        var cancel = new Button { Content = Loc.T("Abbrechen", "Cancel"), IsCancel = true, MinWidth = 80 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap, MaxWidth = 360, Foreground = Ui.Brush("Text") });
        panel.Children.Add(box);
        panel.Children.Add(buttons);
        var win = new Window
        {
            Title = title,
            Content = panel,
            Owner = owner,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = Ui.Brush("Bg1"),
        };
        win.SourceInitialized += (_, _) => MainWindow.UseThemeTitleBar(win);
        ok.Click += (_, _) => win.DialogResult = true;
        win.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return win.ShowDialog() == true ? box.Text : null;
    }
}
