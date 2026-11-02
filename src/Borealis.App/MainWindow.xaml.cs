using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Borealis.App.Views;
using Borealis.Core.Localization;

namespace Borealis.App;

/// <summary>
/// Main window: sidebar (Borealis Sync, devices, fan control, settings), profile picker top right; changes go straight to the service.
/// Hauptfenster: Seitenleiste (Borealis Sync, Geräte, Lüfter, Einstellungen), Profilauswahl oben rechts; Änderungen gehen sofort an den Dienst.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Really close only when the app exits, otherwise hide to the tray.
    /// Nur beim Beenden wirklich schließen, sonst ins Tray verstecken.</summary>
    public bool AllowClose { get; set; }

    static AppState S => AppState.Instance;
    /// <summary>Screenshot mode: open a device page directly.
    /// Screenshot-Modus: Geräteseite direkt öffnen.</summary>
    internal Views.DevicesPage Devices { get; private set; } = null!;
    internal Views.LightingPage Lighting { get; private set; } = null!;
    readonly Dictionary<RadioButton, IPage> pages;
    readonly DispatcherTimer heartbeat = new() { Interval = TimeSpan.FromSeconds(5) };
    bool suppressProfileBox;

    public MainWindow()
    {
        InitializeComponent();
        var lighting = new LightingPage();
        Lighting = lighting;
        var devices = new DevicesPage();
        Devices = devices;
        lighting.OpenDevice += kind => { devices.Select(kind); NavDevices.IsChecked = true; };
        pages = new()
        {
            [NavSync] = lighting,
            [NavDevices] = devices,
            [NavFan] = new FanPage(),
            [NavSettings] = new SettingsPage(),
        };
        NavSync.Content = "Borealis Sync";
        NavDevices.Content = Loc.T("Geräte", "Devices");
        NavFan.Content = Loc.T("Lüftersteuerung", "Fan control");
        NavSettings.Content = Loc.T("Einstellungen", "Settings");
        NewProfileButton.ToolTip = Loc.T("Neues Profil", "New profile");
        ServiceText.Text = Loc.T("Verbinde …", "Connecting …");
        ShowPage(NavSync);
        S.Reloaded += OnReloaded;
        S.StateChanged += UpdateState;
        heartbeat.Tick += async (_, _) => { if (IsVisible && !S.Pending) await S.RefreshAsync(reloadProfile: false); };
        IsVisibleChanged += async (_, e) =>
        {
            if ((bool)e.NewValue) { heartbeat.Start(); await S.RefreshAsync(); }
            else heartbeat.Stop();
        };
        SourceInitialized += (_, _) => UseThemeTitleBar(this);
        // A replaced window (language/theme change) must stop reacting to changes
        // Ein ersetztes Fenster (Sprache/Farbschema) darf nicht mehr auf Änderungen reagieren
        Closed += (_, _) =>
        {
            S.Reloaded -= OnReloaded;
            S.StateChanged -= UpdateState;
            heartbeat.Stop();
        };
        if (S.Status != null) OnReloaded();
    }

    /// <summary>Key of the open page (nav item name), e.g. "NavFan".
    /// Kennung der offenen Seite (Name des Navigationspunkts), z. B. „NavFan“.</summary>
    public string CurrentPage => pages.FirstOrDefault(p => p.Value == PageHost.Content).Key?.Name ?? NavSync.Name;

    /// <summary>Rebuild the settings page (e.g. after the update check found a new version).
    /// Einstellungsseite neu aufbauen (z. B. wenn die Update-Prüfung eine neue Version fand).</summary>
    public void RefreshSettings() => pages[NavSettings].Rebuild();

    /// <summary>Open a page by its key (after rebuilding the window).
    /// Seite über ihre Kennung öffnen (nach dem Neuaufbau des Fensters).</summary>
    public void SelectPage(string name)
    {
        if (FindName(name) is RadioButton rb) rb.IsChecked = true;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (pages != null && sender is RadioButton rb) ShowPage(rb);
    }

    void ShowPage(RadioButton nav)
    {
        var page = pages[nav];
        PageTitle.Text = page.Title;
        PageHost.Content = page;
    }

    void OnReloaded()
    {
        foreach (var p in pages.Values) p.Rebuild();
        suppressProfileBox = true;
        ProfileBox.Items.Clear();
        foreach (var name in S.Status?.Profiles ?? []) ProfileBox.Items.Add(name);
        if (!ProfileBox.Items.Contains(S.Profile.Name)) ProfileBox.Items.Add(S.Profile.Name);
        ProfileBox.SelectedItem = S.Profile.Name;
        suppressProfileBox = false;
        UpdateState();
    }

    void UpdateState()
    {
        var st = S.Status;
        bool connected = st != null && S.LastError == null;
        ServiceDot.Fill = (Brush)FindResource(connected ? (st!.HardwareReady ? "Good" : "Warn") : "Accent");
        ServiceText.Text = !connected ? Loc.T("Dienst nicht verbunden", "Service not connected")
            : !st!.HardwareReady ? Loc.T("Beleuchtung nicht verfügbar", "Lighting not available")
            : !st.Settings.StopAsusLightingService && S.AsusLightingInstalled ? Loc.T("Armoury Crate steuert", "Armoury Crate in control")
            : Loc.T("Borealis aktiv", "Borealis active");
        SaveState.Text = S.Pending ? Loc.T("Wird angewendet …", "Applying …") : connected ? Loc.T("Gespeichert", "Saved") : "";
        var message = S.LastError ?? (S.LastNotes.Count > 0 ? string.Join(" ", S.LastNotes) : null);
        MessageBar.Visibility = message == null ? Visibility.Collapsed : Visibility.Visible;
        MessageText.Text = message ?? "";
        NewProfileButton.IsEnabled = ProfileBox.IsEnabled = connected;
    }

    async void ProfileBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressProfileBox || ProfileBox.SelectedItem is not string name || name == S.Profile.Name) return;
        await S.ActivateAsync(name);
    }

    async void NewProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var name = InputDialog.Show(this, Loc.T("Neues Profil", "New profile"),
            Loc.T("Name des neuen Profils (übernimmt die aktuellen Einstellungen):", "Name of the new profile (takes over the current settings):"), "");
        if (!string.IsNullOrWhiteSpace(name)) await S.CreateAsync(name);
    }

    // Title bar matching the theme (Windows 11: DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_CAPTION_COLOR = 35)
    // Titelleiste passend zum Farbschema (Windows 11: DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_CAPTION_COLOR = 35)
    /// <summary>Title bar matching the theme (also for dialogs).
    /// Titelleiste passend zum Farbschema (auch für Dialoge).</summary>
    internal static void UseThemeTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        bool light = UiPrefs.Current.IsLight;
        int dark = light ? 0 : 1;
        DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
        int caption = light ? 0x00F9F7F7 : 0x00161212; // 0x00BBGGRR = #F7F7F9 or #121216 (sidebar) / bzw. (Seitenleiste)
        DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
