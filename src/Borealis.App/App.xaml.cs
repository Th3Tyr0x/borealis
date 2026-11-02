using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Borealis.Core.Localization;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace Borealis.App;

/// <summary>
/// Tray app: notification area icon, window on double-click; closing only hides it. The service drives the hardware.
/// Tray-App: Symbol im Infobereich, Fenster per Doppelklick; Schließen versteckt es nur. Die Hardware steuert der Dienst.
/// </summary>
public partial class App : Application
{
    Forms.NotifyIcon? tray;
    MainWindow? window;
    System.Drawing.Icon? icon;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // User language and theme before the first window
        // Sprache und Farbschema vor dem ersten Fenster
        UiPrefs.Apply();
        // Dev aid: "--effecttest <file>" renders all effects without hardware
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
        // Dev aid: "--screenshot <folder>" renders all pages as PNG and exits
        // Entwicklerhilfe: „--screenshot <ordner>“ rendert alle Seiten als PNG und beendet sich
        int shot = Array.IndexOf(e.Args, "--screenshot");
        if (shot >= 0 && shot + 1 < e.Args.Length)
        {
            _ = Screenshots.RenderAsync(e.Args[shot + 1]).ContinueWith(_ => Dispatcher.Invoke(Shutdown));
            return;
        }
        // Single instance: a second one brings the first window to the front
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
        // Theme "System": follow Windows light/dark switches
        // Farbschema „System“: Hell/Dunkel-Wechsel von Windows übernehmen
        SystemEvents.UserPreferenceChanged += (_, args) =>
        {
            if (args.Category == UserPreferenceCategory.General && UiPrefs.Current.Theme == ThemeChoice.System)
                Dispatcher.BeginInvoke(RebuildWindow);
        };
        tray.MouseClick += (_, args) => { if (args.Button == Forms.MouseButtons.Left) ShowWindow(); };
        tray.BalloonTipClicked += (_, _) => UpdateCheck.OpenReleasePage();
        _ = WatchForUpdatesAsync();

        if (!e.Args.Contains("--minimized"))
        {
            ShowWindow();
            // First start without PawnIO: offer to set it up once
            // Erster Start ohne PawnIO: einmal die Einrichtung anbieten
            Dispatcher.BeginInvoke(async () => await PawnIoSetup.AskOnFirstStartAsync(window!), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        else _ = AppState.Instance.RefreshAsync();
    }

    Forms.ToolStripMenuItem? openItem, exitItem;
    bool? builtLight;

    /// <summary>
    /// Checks shortly after start, then every 6 h (network at most once a day); one notification per new version.
    /// Prüft kurz nach dem Start, dann alle 6 h (Netz höchstens einmal am Tag); je neuer Version ein Hinweis.
    /// </summary>
    async Task WatchForUpdatesAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(30));
        while (true)
        {
            if (await UpdateCheck.CheckAsync(force: false) is { } v && UiPrefs.Current.NotifiedVersion != v.ToString() && tray != null)
            {
                UiPrefs.Save(UiPrefs.Current with { NotifiedVersion = v.ToString() });
                tray.ShowBalloonTip(10000, Loc.T("Neue Borealis-Version", "New Borealis version"),
                    Loc.T($"Borealis {v} ist verfügbar. Klicken, um die Release-Seite zu öffnen.", $"Borealis {v} is available. Click to open the release page."),
                    Forms.ToolTipIcon.Info);
                window?.RefreshSettings();
            }
            await Task.Delay(TimeSpan.FromHours(6));
        }
    }

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
    /// After a language or theme change: apply and rebuild the window, keeping size, position and page.
    /// Nach Wechsel von Sprache oder Farbschema: anwenden und Fenster neu aufbauen; Größe, Lage und Seite bleiben.
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

    /// <summary>Tray icon (Assets\Borealis.ico).
    /// Tray-Symbol (Assets\Borealis.ico).</summary>
    static System.Drawing.Icon CreateIcon()
    {
        // Same icon as the .exe, in the size Windows uses for the tray
        // Gleiches Symbol wie die .exe, in der Größe für den Infobereich
        using var stream = GetResourceStream(new Uri("pack://application:,,,/Assets/Borealis.ico"))!.Stream;
        return new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
    }
}
