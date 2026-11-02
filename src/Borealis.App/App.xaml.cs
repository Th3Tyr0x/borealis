using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Borealis.Core.Localization;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Borealis.App;

/// <summary>
/// Tray-Anwendung: Symbol im Infobereich, Fenster bei Doppelklick. Schließen versteckt das Fenster nur;
/// „Beenden“ im Kontextmenü beendet die App. Die Hardware steuert der Borealis-Dienst.
/// </summary>
public partial class App : Application
{
    Forms.NotifyIcon? tray;
    MainWindow? window;
    System.Drawing.Icon? icon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Sprache und Farbschema des Benutzers vor dem ersten Fenster
        UiPrefs.Apply();
        // Entwicklerhilfe: „--effecttest <datei>“ rechnet alle Effekte ohne Hardware durch
        int et = Array.IndexOf(e.Args, "--effecttest");
        if (et >= 0 && et + 1 < e.Args.Length)
        {
            string result;
            try { result = Borealis.Core.Lighting.SoftwareEffects.SelfTest(); } catch (Exception ex) { result = "FEHLER: " + ex; }
            System.IO.File.WriteAllText(e.Args[et + 1], result);
            Shutdown();
            return;
        }
        // Entwicklerhilfe: „--screenshot <ordner>“ rendert alle Seiten als PNG und beendet sich
        int shot = Array.IndexOf(e.Args, "--screenshot");
        if (shot >= 0 && shot + 1 < e.Args.Length)
        {
            _ = Screenshots.RenderAsync(e.Args[shot + 1]).ContinueWith(_ => Dispatcher.Invoke(Shutdown));
            return;
        }
        // Nur eine Instanz: eine zweite holt das Fenster der ersten nach vorn
        var single = new Mutex(true, @"Local\BorealisTrayApp", out bool first);
        if (!first)
        {
            EventWaitHandle.OpenExisting(@"Local\BorealisTrayAppShow").Set();
            Shutdown();
            return;
        }
        GC.KeepAlive(single);
        var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\BorealisTrayAppShow");
        new Thread(() => { while (showSignal.WaitOne()) Dispatcher.Invoke(ShowWindow); }) { IsBackground = true }.Start();

        icon = CreateIcon();
        var menu = new Forms.ContextMenuStrip();
        openItem = (Forms.ToolStripMenuItem)menu.Items.Add("", null, (_, _) => ShowWindow());
        menu.Items.Add(new Forms.ToolStripSeparator());
        exitItem = (Forms.ToolStripMenuItem)menu.Items.Add("", null, (_, _) => ExitApp());
        UpdateTrayTexts();
        tray = new Forms.NotifyIcon { Icon = icon, Text = "Borealis", ContextMenuStrip = menu, Visible = true };
        // Farbschema „System“: Windows-Umschaltung zwischen hell und dunkel übernehmen
        SystemEvents.UserPreferenceChanged += (_, args) =>
        {
            if (args.Category == UserPreferenceCategory.General && UiPrefs.Current.Theme == ThemeChoice.System)
                Dispatcher.BeginInvoke(RebuildWindow);
        };
        tray.MouseClick += (_, args) => { if (args.Button == Forms.MouseButtons.Left) ShowWindow(); };

        if (!e.Args.Contains("--minimized"))
        {
            ShowWindow();
            // Erster Start ohne PawnIO: einmal anbieten, es einzurichten
            Dispatcher.BeginInvoke(async () => await PawnIoSetup.AskOnFirstStartAsync(window!), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        else _ = AppState.Instance.RefreshAsync();
    }

    Forms.ToolStripMenuItem? openItem, exitItem;
    bool? builtLight;

    void UpdateTrayTexts()
    {
        if (openItem != null) openItem.Text = Loc.T("Borealis öffnen", "Open Borealis");
        if (exitItem != null) exitItem.Text = Loc.T("Beenden", "Exit");
    }

    MainWindow CreateWindow()
    {
        builtLight = UiPrefs.Current.IsLight;
        var w = new MainWindow();
        if (icon != null) w.Icon = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        return w;
    }

    /// <summary>
    /// Nach einem Wechsel von Sprache oder Farbschema: anwenden und das Fenster neu aufbauen (die Seiten setzen Texte und
    /// Farben beim Aufbau). Größe, Lage und die offene Seite bleiben.
    /// </summary>
    public void RebuildWindow()
    {
        bool lightBefore = builtLight ?? false;
        bool englishBefore = Loc.English;
        UiPrefs.Apply();
        UpdateTrayTexts();
        if (window == null || (lightBefore == UiPrefs.Current.IsLight && englishBefore == Loc.English)) return;
        var old = window;
        bool visible = old.IsVisible;
        var next = CreateWindow();
        next.WindowStartupLocation = WindowStartupLocation.Manual;
        var r = old.WindowState == WindowState.Normal ? new Rect(old.Left, old.Top, old.Width, old.Height) : old.RestoreBounds;
        (next.Left, next.Top, next.Width, next.Height) = (r.Left, r.Top, r.Width, r.Height);
        next.SelectPage(old.CurrentPage);
        window = next;
        if (visible)
        {
            next.Show();
            if (old.WindowState == WindowState.Maximized) next.WindowState = WindowState.Maximized;
            next.Activate();
        }
        old.AllowClose = true;
        old.Close();
    }

    void ShowWindow()
    {
        window ??= CreateWindow();
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    void ExitApp()
    {
        if (window != null) window.AllowClose = true;
        window?.Close();
        if (tray != null) { tray.Visible = false; tray.Dispose(); }
        Shutdown();
    }

    /// <summary>Tray-Symbol: Polarstern über roten Lichtbögen (Assets\Borealis.ico, wie die Seitenleiste).</summary>
    static System.Drawing.Icon CreateIcon()
    {
        // Gleiches Symbol wie die .exe, in der Größe, die Windows für den Infobereich nimmt
        using var stream = GetResourceStream(new Uri("pack://application:,,,/Assets/Borealis.ico"))!.Stream;
        return new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }
}
