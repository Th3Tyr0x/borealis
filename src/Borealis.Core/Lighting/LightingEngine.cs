namespace Borealis.Core.Lighting;

public enum ZoneKind { Mainboard, Argb, Ram }

/// <summary>
/// Eine einzeln einfärbbare Beleuchtungszone. USB-Zonen sind Units des Controllers, RAM-Zonen ENE-Module.
/// </summary>
public sealed record Zone(string Id, string Name, ZoneKind Kind, int Unit, int ControlChannel, int DirectChannel, int DirectOffset, int LedCount)
{
    public bool IsUsb => Kind != ZoneKind.Ram;
}

/// <summary>Ergebnis eines Schreibvorgangs mit Hinweisen für den Benutzer.</summary>
public sealed class ApplyResult
{
    public List<string> Notes { get; } = [];
    public void Note(string text) { if (!Notes.Contains(text)) Notes.Add(text); }
}

/// <summary>
/// Beleuchtung des ganzen Systems auf Zonenebene: Mainboard, ARGB-Header (USB-Controller) und RAM (SMBus über
/// PawnIO, sonst ENE-HAL).
/// Zonen-IDs: <c>mb1..mbN</c>, <c>argb1..argbN</c>, <c>ram1..ramN</c>; Gruppen <c>mb</c>, <c>argb</c>, <c>ram</c>, <c>usb</c>, <c>all</c>.
/// </summary>
public sealed class LightingEngine : IDisposable
{
    public UsbLedController Usb { get; }
    public IRamLights? Ram { get; }
    /// <summary>Warum der RAM nicht verfügbar ist (null, wenn verfügbar oder nicht angefordert).</summary>
    public string? RamError { get; }
    public IReadOnlyList<UsbLedUnit> Units { get; }
    public IReadOnlyList<Zone> Zones { get; }

    LightingEngine(UsbLedController usb, IRamLights? ram, string? ramError, List<UsbLedUnit> units)
    {
        Usb = usb;
        Ram = ram;
        RamError = ramError;
        Units = units;
        Zones = BuildZones(units, usb.ReadChannels(), ram);
    }

    /// <summary>Öffnet Controller und (optional) RAM und liest die Topologie. RAM nur aus einem MTA-Thread.</summary>
    public static LightingEngine Open(bool withRam = true)
    {
        var usb = UsbLedController.Open();
        try
        {
            var units = usb.ReadUnits();
            IRamLights? ram = null;
            string? ramError = null;
            if (withRam)
            {
                // Erst selbst über SMBus (PawnIO); findet das keine DIMMs, die ENE-HAL, falls installiert
                try { ram = EneSmbusRam.Open(); }
                catch (Exception ex) when (ex is IOException or TimeoutException) { ramError = ex.Message; }
                if (ram == null)
                {
                    try { ram = RamController.Open(); ramError = null; }
                    catch (Exception ex) { ramError ??= ex.Message; }
                }
            }
            return new LightingEngine(usb, ram, ramError, units);
        }
        catch
        {
            usb.Dispose();
            throw;
        }
    }

    static List<Zone> BuildZones(List<UsbLedUnit> units, List<ChannelState> channels, IRamLights? ram)
    {
        var zones = new List<Zone>();
        int ControlChannelOf(int unit) => channels.FirstOrDefault(c => c.HasUnit(unit))?.Channel ?? -1;

        // Mainboard-Units teilen sich Direct-Kanal 4; jede Unit ist eine LED in Unit-Reihenfolge
        int mbOffset = 0, mbNo = 0;
        foreach (var u in units.Where(u => u.Kind == UnitKind.Mainboard))
        {
            mbNo++;
            string name = u.Location switch
            {
                // Ort 5 heißt bei ASUS „Back IO“: die I/O-Abdeckung, auf diesem Board das ROG-Logo
                0x05 => $"ROG-Logo (Back-IO {mbNo})",
                0x11 => "Mainboard 12-V-RGB-Header",
                _ => $"Mainboard-Zone {mbNo}",
            };
            zones.Add(new Zone($"mb{mbNo}", name, ZoneKind.Mainboard, u.Index, ControlChannelOf(u.Index),
                UsbLedController.MainboardDirectChannel, mbOffset, u.LedCount));
            mbOffset += u.LedCount;
        }
        foreach (var u in units.Where(u => u.Kind == UnitKind.Argb))
        {
            int h = u.ArgbHeader + 1;
            zones.Add(new Zone($"argb{h}", $"ARGB-Header {h}", ZoneKind.Argb, u.Index, ControlChannelOf(u.Index),
                u.ArgbHeader, 0, u.LedCount));
        }
        if (ram != null)
            foreach (var m in ram.Modules)
                zones.Add(new Zone($"ram{m.Index + 1}", $"RAM {m.Index + 1}", ZoneKind.Ram, m.Index, -1, -1, 0, m.LedCount));
        return zones;
    }

    /// <summary>Zone oder Gruppe auflösen; unbekannte Namen werfen.</summary>
    public IReadOnlyList<Zone> Resolve(string token)
    {
        var t = token.Trim().ToLowerInvariant();
        List<Zone> result = t switch
        {
            "all" or "alle" => Zones.ToList(),
            "usb" => Zones.Where(z => z.IsUsb).ToList(),
            "mb" or "mainboard" => Zones.Where(z => z.Kind == ZoneKind.Mainboard).ToList(),
            "argb" => Zones.Where(z => z.Kind == ZoneKind.Argb).ToList(),
            "ram" => Zones.Where(z => z.Kind == ZoneKind.Ram).ToList(),
            _ => Zones.Where(z => z.Id == t).ToList(),
        };
        if (result.Count == 0)
        {
            if (t.StartsWith("ram") && Ram == null) throw new ArgumentException($"RAM nicht verfügbar: {RamError}");
            throw new ArgumentException($"Unbekannte Zone „{token}“. Bekannt: {string.Join(", ", Zones.Select(z => z.Id))}, mb, argb, ram, usb, all.");
        }
        return result;
    }

    /// <summary>
    /// Firmware-Effekt (bzw. gleichnamiger RAM-Effekt) mit einer Farbe je Zone setzen. Bei USB-Zonen gilt der Effekt
    /// für den ganzen Steuerkanal und wird im Controller gespeichert; die Farbe je Zone bleibt erhalten.
    /// </summary>
    public ApplyResult ApplyEffect(IReadOnlyDictionary<Zone, Rgb> zoneColors, FirmwareEffect effect, byte speed = 0, byte direction = 0)
    {
        var result = new ApplyResult();
        foreach (var group in zoneColors.Where(z => z.Key.IsUsb).GroupBy(z => z.Key.ControlChannel))
        {
            if (group.Key < 0) { result.Note("Zone ohne Steuerkanal übersprungen."); continue; }
            var colors = group.ToDictionary(z => z.Key.Unit, z => Calibrate(z.Key, z.Value));
            // Wie der HAL: EC 36 nur bei Effekten mit Farbe (§1.5)
            Usb.SetEffect(group.Key, effect, direction, speed, FirmwareEffects.UsesColor(effect) ? colors : null);
            var others = Zones.Where(z => z.IsUsb && z.ControlChannel == group.Key && !colors.ContainsKey(z.Unit)).ToList();
            if (others.Count > 0)
                result.Note($"Der Effekt gilt für den ganzen Steuerkanal {group.Key}; {string.Join(", ", others.Select(o => o.Id))} behalten ihre gespeicherte Farbe.");
        }
        foreach (var (zone, color) in zoneColors.Where(z => z.Key.Kind == ZoneKind.Ram))
        {
            if (Ram == null) { result.Note($"RAM nicht verfügbar: {RamError}"); continue; }
            lock (ramGate)
            {
                if (effect == FirmwareEffect.Off) Ram.SetEffect(zone.Unit, RamEffect.Direct, Rgb.Black);
                else Ram.SetEffect(zone.Unit, RamEffects.FromFirmware(effect), Calibrate(zone, color));
            }
        }
        return result;
    }

    /// <summary>
    /// Feste Farben im Direct-Modus (nicht gespeichert; nach Neustart gilt wieder der gespeicherte Firmware-Effekt).
    /// Ein Steuerkanal wird komplett umgeschaltet; nicht angegebene Zonen dieses Kanals werden schwarz.
    /// </summary>
    public ApplyResult ApplyDirect(IReadOnlyDictionary<Zone, Rgb> zoneColors)
    {
        var result = new ApplyResult();
        var channels = zoneColors.Keys.Where(z => z.IsUsb && z.ControlChannel >= 0).Select(z => z.ControlChannel).Distinct().ToList();
        foreach (var ch in channels) Usb.SetDirectMode(ch);

        // Alle Zonen der umgeschalteten Kanäle neu senden (das Umschalten löscht ihre Puffer, §1.4)
        var affected = Zones.Where(z => z.IsUsb && channels.Contains(z.ControlChannel)).ToList();
        foreach (var z in affected.Where(z => !zoneColors.ContainsKey(z)))
            result.Note($"{z.Id} liegt auf demselben Steuerkanal und wird schwarz.");
        var frame = new List<(int, Rgb[])>();
        foreach (var dc in affected.GroupBy(z => z.DirectChannel))
        {
            int len = dc.Max(z => z.DirectOffset + z.LedCount);
            var leds = new Rgb[len];
            foreach (var z in dc)
            {
                var c = zoneColors.TryGetValue(z, out var col) ? Calibrate(z, col) : Rgb.Black;
                for (int i = 0; i < z.LedCount; i++) leds[z.DirectOffset + i] = c;
            }
            frame.Add((dc.Key, leds));
        }
        Usb.StreamDirectFrame(frame);

        foreach (var (zone, color) in zoneColors.Where(z => z.Key.Kind == ZoneKind.Ram))
        {
            if (Ram == null) { result.Note($"RAM nicht verfügbar: {RamError}"); continue; }
            lock (ramGate) Ram.SetEffect(zone.Unit, RamEffect.Direct, Calibrate(zone, color));
        }
        return result;
    }

    /// <summary>
    /// Effekt nach dem Herunterfahren bzw. im Standby (S3/S5) setzen (§1.7). Nur USB-Zonen; der RAM ist dann aus.
    /// Wirkt nur, wenn im BIOS die Beleuchtung im Standby erlaubt ist.
    /// </summary>
    public ApplyResult ApplyStandby(IReadOnlyDictionary<Zone, Rgb> zoneColors, FirmwareEffect effect, byte speed = 0, byte direction = 0)
    {
        var result = new ApplyResult();
        if (zoneColors.Keys.Any(z => z.Kind == ZoneKind.Ram)) result.Note("Der RAM hat keinen Standby-Effekt und wird übersprungen.");
        foreach (var group in zoneColors.Where(z => z.Key.IsUsb && z.Key.ControlChannel >= 0).GroupBy(z => z.Key.ControlChannel))
        {
            var colors = group.ToDictionary(z => z.Key.Unit, z => Calibrate(z.Key, z.Value));
            Usb.SetEffect(group.Key, effect, direction, speed, FirmwareEffects.UsesColor(effect) ? colors : null, standby: true);
            if (!Usb.VerifyStandby(group.Key, effect))
                result.Note($"Steuerkanal {group.Key}: Controller bestätigt den Standby-Effekt nicht.");
        }
        return result;
    }

    /// <summary>
    /// Farbabgleich je Zone (Zonen-ID); wird in jede Farbe eingerechnet, die an die Hardware geht.
    /// Firmware-Effekte mit eigenen Farben (z. B. Regenbogen als Hardware-Effekt) lassen sich nicht abgleichen.
    /// </summary>
    public IReadOnlyDictionary<string, Profiles.ZoneCalibration> Calibration { get; set; } = new Dictionary<string, Profiles.ZoneCalibration>();

    Rgb Calibrate(Zone z, Rgb c) => Calibration.TryGetValue(z.Id, out var cal) && !cal.IsNeutral ? cal.Apply(c) : c;

    // Zuletzt geschriebener Zustand je Gruppe, damit Live-Änderungen (Farbrad ziehen) nur Neues schreiben
    readonly Dictionary<ZoneKind, string> appliedSignature = new();
    readonly HashSet<int> directChannels = new();

    /// <summary>
    /// Ein Profil anwenden. Gruppen mit Hardware-Effekt werden in den Controller geschrieben; für Gruppen mit
    /// Software-Effekt wird ein gleichartiger Hardware-Effekt als Startbild gespeichert, der Kanal auf Direct
    /// geschaltet und die Zone in <paramref name="software"/> eingetragen (die Bilder streamt der Aufrufer).
    /// </summary>
    /// <param name="force">Alles neu schreiben (nach Start, Standby, fremdem Zugriff).</param>
    public ApplyResult ApplyProfile(Profiles.LightingProfile profile, bool includeStandby, LightingOptions options,
        List<SoftwareZone> software, bool force = false)
    {
        if (force) { appliedSignature.Clear(); directChannels.Clear(); }
        double brightness = Math.Clamp(profile.Brightness, 1, 100) / 100.0;
        var resolved = profile.Resolve();
        var result = new ApplyResult();
        void Merge(ApplyResult r) { foreach (var n in r.Notes) result.Note(n); }

        foreach (var (kind, g) in new[] { (ZoneKind.Mainboard, resolved.Mainboard), (ZoneKind.Argb, resolved.Argb), (ZoneKind.Ram, resolved.Ram) })
        {
            var zones = Zones.Where(z => z.Kind == kind).ToList();
            if (zones.Count == 0) continue;
            bool sw = NeedsSoftware(g, options.PreferHardwareEffects);
            var fwEffect = sw ? FallbackEffect(g.Effect) : g.Effect;
            // Bei Software-Effekten nur bei Effektwechsel neu schreiben: Farbe/Tempo ändert der Stream sofort,
            // ein erneutes Umschalten des Kanals würde kurz blinken
            var signature = sw
                ? System.Text.Json.JsonSerializer.Serialize(new { sw, g.Effect })
                : System.Text.Json.JsonSerializer.Serialize(new { sw, g.Effect, g.SpeedStep, g.Speed, g.Direction, g.ColorMode, g.Colors, g.DefaultColor, g.Color2, brightness,
                    cal = zones.Select(z => Calibration.GetValueOrDefault(z.Id)) });
            bool changed = !appliedSignature.TryGetValue(kind, out var old) || old != signature;
            if (changed)
            {
                // Firmware-Farben mit Helligkeit; bei Software-Effekt ist das nur das Bild bis zum ersten Frame/Neustart
                var colors = zones.ToDictionary(z => z, z => SoftwareEffects.Scale(g.ColorOf(z.Id), sw ? 1 : brightness));
                byte speed = FirmwareEffects.SpeedValue(fwEffect, g.FirmwareSpeed);
                byte dir = (byte)(g.Direction == Profiles.EffectDirection.Left ? 1 : 0);
                if (kind == ZoneKind.Ram)
                {
                    if (!sw) Merge(ApplyEffect(colors, fwEffect, speed, dir));
                }
                else
                {
                    foreach (var ch in zones.Select(z => z.ControlChannel).Distinct()) directChannels.Remove(ch);
                    Merge(ApplyEffect(colors, fwEffect, speed, dir));
                }
                appliedSignature[kind] = signature;
            }
            if (sw)
            {
                foreach (var ch in zones.Where(z => z.IsUsb && z.ControlChannel >= 0).Select(z => z.ControlChannel).Distinct())
                    if (directChannels.Add(ch)) Usb.SetDirectMode(ch);
                foreach (var z in zones)
                {
                    int leds = z.LedCount;
                    if (z.Kind == ZoneKind.Argb && options.LedCounts.TryGetValue(z.Id, out var n) && n > 0) leds = Math.Min(n, z.LedCount);
                    software.Add(new SoftwareZone(z, leds, g));
                }
            }
        }
        if (includeStandby)
        {
            foreach (var (kind, g) in new[] { (ZoneKind.Mainboard, resolved.StandbyMainboard), (ZoneKind.Argb, resolved.StandbyArgb) })
            {
                var colors = Zones.Where(z => z.Kind == kind).ToDictionary(z => z, z => g.ColorOf(z.Id));
                if (colors.Count == 0) continue;
                Merge(ApplyStandby(colors, g.Effect, FirmwareEffects.SpeedValue(g.Effect, g.FirmwareSpeed)));
            }
        }
        return result;
    }

    /// <summary>Kompatibilität (CLI): nur Hardware-Effekte, Software-Zonen werden nicht gestreamt.</summary>
    public ApplyResult ApplyProfile(Profiles.LightingProfile profile, bool includeStandby) =>
        ApplyProfile(profile, includeStandby, new LightingOptions { PreferHardwareEffects = true }, [], force: true);

    /// <summary>
    /// Wie in Armoury Crate (§5): Statisch mit einer Farbe läuft in der Firmware, alles andere rechnet Borealis.
    /// Mit „Hardware-Effekte bevorzugen“ nur noch Zufall/Verlauf in Software.
    /// </summary>
    public static bool NeedsSoftware(Profiles.GroupSetting g, bool preferHardware)
    {
        if (g.Effect == FirmwareEffect.Off) return false;
        if (SoftwareEffects.IsSoftwareOnly(g.Effect)) return true;
        bool multiColor = SoftwareEffects.HasColor(g.Effect) && g.ColorMode != Profiles.ColorMode.Single;
        bool background = SoftwareEffects.CapsOf(g.Effect).Background && g.BackgroundEnabled && (g.BackgroundRainbow || g.BackgroundColor != Rgb.Black);
        if (g.Effect == FirmwareEffect.Static) return multiColor;
        return !preferHardware || multiColor || background;
    }

    /// <summary>Hardware-Effekt, der beim Hochfahren gezeigt wird, bis Borealis mit dem Software-Effekt übernimmt.</summary>
    static FirmwareEffect FallbackEffect(FirmwareEffect e) => SoftwareEffects.FirmwareFallback(e);

    /// <summary>Ein berechnetes Bild senden: USB-Zonen als ein Block (§1.9), RAM-Module über die ENE-HAL.</summary>
    public void StreamSoftwareFrame(IReadOnlyList<(SoftwareZone Zone, Rgb[] Leds)> frame)
    {
        var usbFrame = new List<(int, Rgb[])>();
        foreach (var dc in frame.Where(f => f.Zone.Zone.IsUsb).GroupBy(f => f.Zone.Zone.DirectChannel))
        {
            int len = dc.Max(f => f.Zone.Zone.DirectOffset + f.Leds.Length);
            var buf = new Rgb[len];
            foreach (var (z, leds) in dc)
                for (int i = 0; i < leds.Length; i++) buf[z.Zone.DirectOffset + i] = Calibrate(z.Zone, leds[i]);
            usbFrame.Add((dc.Key, buf));
        }
        if (usbFrame.Count > 0) Usb.StreamDirectFrame(usbFrame);
    }

    /// <summary>
    /// RAM-Teil eines berechneten Bildes über die ENE-HAL senden. Eigene Sperre statt der USB-Sperre, damit der
    /// langsamere SMBus-Weg in einem eigenen Thread laufen kann und die ARGB-Bildrate nicht bremst.
    /// </summary>
    public void StreamSoftwareRam(IReadOnlyList<(SoftwareZone Zone, Rgb[] Leds)> frame)
    {
        lock (ramGate)
        {
            if (disposed || Ram == null) return;
            foreach (var (z, leds) in frame.Where(f => f.Zone.Zone.Kind == ZoneKind.Ram))
                Ram.SetEffect(z.Zone.Unit, RamEffect.Direct, leds.Select(c => Calibrate(z.Zone, c)).ToArray());
        }
    }

    /// <summary>
    /// LED-Anzahl eines ARGB-Headers im Controller setzen und zurücklesen. Danach die Beleuchtung neu öffnen,
    /// damit die Zonen die neue Anzahl kennen.
    /// </summary>
    public int SetLedCount(Zone zone, int count)
    {
        if (zone.Kind != ZoneKind.Argb) throw new ArgumentException("Die LED-Anzahl lässt sich nur bei ARGB-Headern einstellen.");
        Usb.SetLedCount(zone.ControlChannel, zone.DirectChannel, zone.Unit, zone.LedCount, count);
        directChannels.Add(zone.ControlChannel);
        appliedSignature.Remove(ZoneKind.Argb);
        Thread.Sleep(100);
        return Usb.ReadLedCount(zone.Unit) ?? throw new IOException("Controller antwortet nicht auf die LED-Abfrage.");
    }

    /// <summary>
    /// Zähltest: Header im Direct-Modus mit Markierungen über alle LEDs, die der Controller kennt –
    /// erste LED grün, jede zehnte weiß, jede fünfte blau, sonst schwach rot. So lässt sich die echte Länge abzählen.
    /// </summary>
    public void ShowLedTest(Zone zone)
    {
        if (directChannels.Add(zone.ControlChannel)) Usb.SetDirectMode(zone.ControlChannel);
        appliedSignature.Remove(zone.Kind);
        var leds = new Rgb[zone.LedCount];
        for (int i = 0; i < leds.Length; i++)
            leds[i] = i == 0 ? new Rgb(0, 255, 0) : (i + 1) % 10 == 0 ? Rgb.White : (i + 1) % 5 == 0 ? new Rgb(0, 60, 255) : new Rgb(60, 0, 0);
        Usb.StreamDirectFrame([(zone.DirectChannel, leds)]);
    }

    /// <summary>ARGB-Zonen einmal über die volle LED-Anzahl schwarz senden (nach Verkleinern der LED-Anzahl).</summary>
    public void ClearArgb(IEnumerable<SoftwareZone> zones)
    {
        var frame = zones.Where(z => z.Zone.Kind == ZoneKind.Argb && z.LedCount < z.Zone.LedCount)
            .Select(z => (z.Zone.DirectChannel, new Rgb[z.Zone.LedCount])).ToList();
        if (frame.Count > 0) Usb.StreamDirectFrame(frame);
    }

    // Schützt RAM-Zugriffe (ENE-HAL) unabhängig von der USB-Sperre des Aufrufers
    readonly object ramGate = new();
    bool disposed;

    public void Dispose()
    {
        lock (ramGate)
        {
            disposed = true;
            Ram?.Dispose();
        }
        Usb.Dispose();
    }
}

/// <summary>Zone, die Borealis selbst animiert, mit wirksamer LED-Anzahl und Effekt-Einstellung.</summary>
public sealed record SoftwareZone(Zone Zone, int LedCount, Profiles.GroupSetting Setting);

/// <summary>Einstellungen aus <see cref="Profiles.BorealisSettings"/>, die das Anwenden beeinflussen.</summary>
public sealed class LightingOptions
{
    public bool PreferHardwareEffects { get; init; }
    public IReadOnlyDictionary<string, int> LedCounts { get; init; } = new Dictionary<string, int>();
}
