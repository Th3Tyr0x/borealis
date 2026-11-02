namespace Borealis.Core.Lighting;

/// <summary>Typ einer Unit laut <c>EC 31</c> (§1.3).</summary>
public enum UnitKind { Mainboard, Argb, Unknown }

/// <summary>Eine LED-Unit des Controllers (Antwort auf <c>EC 31 &lt;unit&gt;</c>).</summary>
public sealed record UsbLedUnit(int Index, byte Type, int LedCount, byte Location)
{
    public UnitKind Kind => Type == 0x10 ? UnitKind.Mainboard : (Type & 0xF0) == 0x20 ? UnitKind.Argb : UnitKind.Unknown;

    /// <summary>0-basierter ARGB-Header (nur bei <see cref="UnitKind.Argb"/>).</summary>
    public int ArgbHeader => Type - 0x20;
}

/// <summary>Zustand eines Steuerkanals (Antwort auf <c>EC 32 &lt;kanal&gt;</c>, §1.4).</summary>
public sealed record ChannelState(int Channel, ushort UnitMask, byte Effect, byte Direction, byte Speed,
    ushort StandbyUnitMask, byte StandbyEffect, byte StandbyDirection, byte StandbySpeed)
{
    public bool HasUnit(int unit) => (UnitMask & (1 << unit)) != 0;
}

/// <summary>
/// Befehle des ASUS-RGB-Controllers „6K75“ (Aura50) auf Protokollebene (docs/hardware.md §1).
/// Alle öffentlichen Methoden nehmen die Sperre <c>Global\asusaurausb</c> selbst.
/// </summary>
public sealed class UsbLedController : IDisposable
{
    /// <summary>Direct-Kanal der festen Mainboard-Zone bei Aura50-Controllern (Aura36: 6), §1.4.</summary>
    public const int MainboardDirectChannel = 4;

    readonly UsbLedHid hid;

    public UsbLedController(UsbLedHid hid) { this.hid = hid; }

    public static UsbLedController Open(string pathFilter = UsbLedHid.DefaultPathFilter) => new(UsbLedHid.Open(pathFilter));

    public string DevicePath => hid.DevicePath;

    /// <summary>Konfigurationstabelle als Rohreport (<c>EC B0</c>, §1.3).</summary>
    public byte[]? ReadConfig()
    {
        using var _ = hid.Lock();
        return hid.Query(0x30);
    }

    /// <summary>Units 0..maxUnit abfragen; Units ohne Antwort oder mit Typ 0 fehlen in der Liste.</summary>
    public List<UsbLedUnit> ReadUnits(int maxUnit = 7)
    {
        var units = new List<UsbLedUnit>();
        using var _ = hid.Lock();
        for (int u = 0; u <= maxUnit; u++)
        {
            var r = hid.QueryIndexed(0x31, (byte)u, 400);
            if (r == null || r[3] == 0) continue;
            units.Add(new UsbLedUnit(u, r[3], (r[4] << 8) | r[5], r[6]));
        }
        return units;
    }

    public ChannelState? ReadChannel(int channel)
    {
        using var _ = hid.Lock();
        return ReadChannelLocked(channel);
    }

    ChannelState? ReadChannelLocked(int channel, int timeoutMs = 600)
    {
        var r = hid.QueryIndexed(0x32, (byte)channel, timeoutMs);
        if (r == null) return null;
        return new ChannelState(channel,
            (ushort)((r[4] << 8) | r[5]), r[6], r[7], r[8],
            (ushort)((r[9] << 8) | r[10]), r[11], r[12], r[13]);
    }

    /// <summary>Alle Steuerkanäle 0..maxChannel, die mindestens eine Unit haben.</summary>
    public List<ChannelState> ReadChannels(int maxChannel = 3)
    {
        var list = new List<ChannelState>();
        using var _ = hid.Lock();
        for (int c = 0; c <= maxChannel; c++)
        {
            var s = ReadChannelLocked(c);
            if (s != null && s.UnitMask != 0) list.Add(s);
        }
        return list;
    }

    /// <summary>
    /// Firmware-Effekt auf einem Steuerkanal setzen und im Controller speichern (§1.5).
    /// Der Effekt gilt für alle Units des Kanals; Farben gibt es je Unit (je Strip eine Farbe).
    /// Units, die in <paramref name="colors"/> fehlen, behalten ihre gespeicherte Farbe.
    /// </summary>
    public void SetEffect(int channel, FirmwareEffect effect, byte direction, byte speed,
        IReadOnlyDictionary<int, Rgb>? colors, bool standby = false)
    {
        if (effect == FirmwareEffect.Direct) throw new ArgumentException("Direct über SetDirectMode setzen.", nameof(effect));
        byte flag = standby ? (byte)1 : (byte)0;
        using var _ = hid.Lock();
        if (!standby)
        {
            // Wie der HAL: aus Direct (oder „nichts“) erst auf Effekt 0 zurücksetzen
            var current = ReadChannelLocked(channel);
            if (current == null || current.Effect is 0xFF or 0x00) hid.Write(0x35, (byte)channel, 0x00, 0x00, 0x00);
        }
        hid.Write(0x35, (byte)channel, 0x00, flag, (byte)effect, direction, speed);
        if (colors is { Count: > 0 }) WriteColors(colors, flag);
        hid.Write(0x3F, 0x55);
    }

    // EC 36 <maskH> <maskL> <flag> RGB×16 – Slot n = Unit n (§1.5)
    void WriteColors(IReadOnlyDictionary<int, Rgb> colors, byte flag)
    {
        var p = new byte[4 + 16 * 3];
        p[0] = 0x36;
        p[3] = flag;
        ushort mask = 0;
        foreach (var (unit, c) in colors)
        {
            if (unit is < 0 or > 15) continue;
            mask |= (ushort)(1 << unit);
            p[4 + unit * 3] = c.R;
            p[5 + unit * 3] = c.G;
            p[6 + unit * 3] = c.B;
        }
        p[1] = (byte)(mask >> 8);
        p[2] = (byte)mask;
        hid.Write(p);
    }

    /// <summary>
    /// Wartet, bis <c>EC 32</c> den gesetzten Standby-Effekt meldet (wie der HAL nach <c>Set_Standby_Effect</c>).
    /// </summary>
    public bool VerifyStandby(int channel, FirmwareEffect effect, int timeoutMs = 2000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        using var _ = hid.Lock();
        while (Environment.TickCount64 < deadline)
        {
            var s = ReadChannelLocked(channel, 300);
            if (s != null && s.StandbyEffect == (byte)effect) return true;
            Thread.Sleep(50);
        }
        return false;
    }

    /// <summary>
    /// <c>EC 35 &lt;kanal&gt; 00 00 FF</c>: Die Firmware gibt den Steuerkanal an die Software ab (§1.6).
    /// Achtung: Das löscht die Direct-Puffer aller Units des Kanals; danach alle neu streamen.
    /// </summary>
    public void SetDirectMode(int channel)
    {
        using var _ = hid.Lock();
        hid.Write(0x35, (byte)channel, 0x00, 0x00, 0xFF);
    }

    /// <summary>
    /// Farben eines Direct-Kanals senden (<c>EC 40</c>, 20 LEDs je Report, letzter Report mit Bit 7 = übernehmen, §1.6).
    /// Direct-Kanal: ARGB-Header n → n, Mainboard-Zone → 4 (Aura50).
    /// </summary>
    public void StreamDirect(int directChannel, ReadOnlySpan<Rgb> leds, bool apply = true)
    {
        using var _ = hid.Lock();
        StreamDirectLocked(directChannel, leds, apply);
    }

    void StreamDirectLocked(int directChannel, ReadOnlySpan<Rgb> leds, bool apply)
    {
        var p = new byte[4 + 20 * 3];
        for (int start = 0; start < leds.Length; start += 20)
        {
            int n = Math.Min(20, leds.Length - start);
            bool last = start + n >= leds.Length;
            p[0] = 0x40;
            p[1] = (byte)(directChannel | ((start >> 4) & 0xF0) | (last && apply ? 0x80 : 0));
            p[2] = (byte)(start & 0xFF);
            p[3] = (byte)n;
            for (int i = 0; i < n; i++)
            {
                var c = leds[start + i];
                p[4 + i * 3] = c.R;
                p[5 + i * 3] = c.G;
                p[6 + i * 3] = c.B;
            }
            hid.Write(p.AsSpan(0, 4 + n * 3));
        }
    }

    /// <summary>Mehrere Direct-Kanäle als ein Block unter einer Sperre senden (ruhigster Ablauf, §1.9).</summary>
    public void StreamDirectFrame(IEnumerable<(int DirectChannel, Rgb[] Leds)> frame)
    {
        using var _ = hid.Lock();
        foreach (var (dc, leds) in frame) StreamDirectLocked(dc, leds, true);
    }

    /// <summary>Eingestellte LED-Anzahl einer Unit lesen (<c>EC 31 &lt;unit&gt;</c>, Antwort [4..5], §1.8).</summary>
    public int? ReadLedCount(int unit)
    {
        using var _ = hid.Lock();
        var r = hid.QueryIndexed(0x31, (byte)unit, 1000);
        return r == null ? null : (r[4] << 8) | r[5];
    }

    /// <summary>
    /// LED-Anzahl eines ARGB-Headers im Controller setzen, wie <c>SetManualLedCount</c> der HAL (§1.8):
    /// Kanal auf Direct, alte LEDs schwarz senden, kurz warten, dann <c>EC 3B &lt;unit&gt; &lt;hi&gt; &lt;lo&gt;</c>.
    /// Das Umschalten löscht die Direct-Puffer der anderen Header desselben Steuerkanals; danach neu anwenden.
    /// </summary>
    public void SetLedCount(int controlChannel, int directChannel, int unit, int oldCount, int newCount)
    {
        if (newCount is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(newCount), "LED-Anzahl 1–500.");
        using var _ = hid.Lock();
        hid.Write(0x35, (byte)controlChannel, 0x00, 0x00, 0xFF);
        StreamDirectLocked(directChannel, new Rgb[Math.Max(oldCount, 1)], true);
        Thread.Sleep(50);
        hid.Write(0x3B, (byte)unit, (byte)(newCount >> 8), (byte)newCount);
    }

    /// <summary><c>EC 3F 55</c>: übernehmen/speichern (§1.2). Nicht in Schleifen senden.</summary>
    public void Commit()
    {
        using var _ = hid.Lock();
        hid.Write(0x3F, 0x55);
    }

    public void Dispose() => hid.Dispose();
}
