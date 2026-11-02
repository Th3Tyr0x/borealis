namespace Borealis.Core.Lighting;

/// <summary>Unit type per <c>EC 31</c> (§1.3).
/// Typ einer Unit laut <c>EC 31</c> (§1.3).</summary>
public enum UnitKind { Mainboard, Argb, Unknown }

/// <summary>An LED unit of the controller (reply to <c>EC 31 &lt;unit&gt;</c>).
/// Eine LED-Unit des Controllers (Antwort auf <c>EC 31 &lt;unit&gt;</c>).</summary>
public sealed record UsbLedUnit(int Index, byte Type, int LedCount, byte Location)
{
    public UnitKind Kind => Type == 0x10 ? UnitKind.Mainboard : (Type & 0xF0) == 0x20 ? UnitKind.Argb : UnitKind.Unknown;

    /// <summary>0-based ARGB header (only for <see cref="UnitKind.Argb"/>).
    /// 0-basierter ARGB-Header (nur bei <see cref="UnitKind.Argb"/>).</summary>
    public int ArgbHeader => Type - 0x20;
}

/// <summary>Control channel state (reply to <c>EC 32 &lt;channel&gt;</c>, §1.4).
/// Zustand eines Steuerkanals (Antwort auf <c>EC 32 &lt;kanal&gt;</c>, §1.4).</summary>
public sealed record ChannelState(int Channel, ushort UnitMask, byte Effect, byte Direction, byte Speed,
    ushort StandbyUnitMask, byte StandbyEffect, byte StandbyDirection, byte StandbySpeed)
{
    public bool HasUnit(int unit) => (UnitMask & (1 << unit)) != 0;
}

/// <summary>
/// Protocol-level commands of the "6K75" RGB controller (docs/hardware.md §1); all public methods take <c>Global\asusaurausb</c> themselves.
/// Befehle des RGB-Controllers „6K75“ auf Protokollebene (docs/hardware.md §1); alle öffentlichen Methoden nehmen <c>Global\asusaurausb</c> selbst.
/// </summary>
public sealed class UsbLedController : IDisposable
{
    /// <summary>Direct channel of the fixed motherboard zone on this controller generation (older: 6), §1.4.
    /// Direct-Kanal der festen Mainboard-Zone bei dieser Controller-Generation (ältere: 6), §1.4.</summary>
    public const int MainboardDirectChannel = 4;

    readonly UsbLedHid hid;

    public UsbLedController(UsbLedHid hid) { this.hid = hid; }

    public static UsbLedController Open(string pathFilter = UsbLedHid.DefaultPathFilter) => new(UsbLedHid.Open(pathFilter));

    public string DevicePath => hid.DevicePath;

    /// <summary>Config table as a raw report (<c>EC B0</c>, §1.3).
    /// Konfigurationstabelle als Rohreport (<c>EC B0</c>, §1.3).</summary>
    public byte[]? ReadConfig()
    {
        using var _ = hid.Lock();
        return hid.Query(0x30);
    }

    /// <summary>Query units 0..maxUnit; units without reply or with type 0 are left out.
    /// Units 0..maxUnit abfragen; ohne Antwort oder mit Typ 0 fehlen sie.</summary>
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

    /// <summary>All control channels 0..maxChannel with at least one unit.
    /// Alle Steuerkanäle 0..maxChannel mit mindestens einer Unit.</summary>
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
    /// Set a firmware effect on a channel and store it (§1.5). The effect covers all units; colors are per unit.
    /// Units missing in <paramref name="colors"/> keep their stored color.
    /// Firmware-Effekt auf einem Kanal setzen und speichern (§1.5). Er gilt für alle Units; Farben gibt es je Unit.
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
            // Like the HAL: from direct (or "nothing") reset to effect 0 first / Wie der HAL: aus Direct erst auf Effekt 0 zurücksetzen
            var current = ReadChannelLocked(channel);
            if (current == null || current.Effect is 0xFF or 0x00) hid.Write(0x35, (byte)channel, 0x00, 0x00, 0x00);
        }
        hid.Write(0x35, (byte)channel, 0x00, flag, (byte)effect, direction, speed);
        if (colors is { Count: > 0 }) WriteColors(colors, flag);
        hid.Write(0x3F, 0x55);
    }

    // EC 36 <maskH> <maskL> <flag> RGB×16 – slot n = unit n (§1.5)
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

    /// <summary>Waits until <c>EC 32</c> reports the set standby effect (like the HAL after <c>Set_Standby_Effect</c>).
    /// Wartet, bis <c>EC 32</c> den gesetzten Standby-Effekt meldet (wie der HAL nach <c>Set_Standby_Effect</c>).</summary>
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
    /// <c>EC 35 &lt;channel&gt; 00 00 FF</c>: firmware hands the channel to software (§1.6). Clears all direct buffers of the channel; restream afterwards.
    /// <c>EC 35 &lt;kanal&gt; 00 00 FF</c>: Firmware gibt den Kanal an die Software ab (§1.6). Löscht alle Direct-Puffer des Kanals; danach neu streamen.
    /// </summary>
    public void SetDirectMode(int channel)
    {
        using var _ = hid.Lock();
        hid.Write(0x35, (byte)channel, 0x00, 0x00, 0xFF);
    }

    /// <summary>
    /// Send colors of a direct channel (<c>EC 40</c>, 20 LEDs per report, last one with bit 7 = apply, §1.6). ARGB header n → n, motherboard → 4.
    /// Farben eines Direct-Kanals senden (<c>EC 40</c>, 20 LEDs je Report, letzter mit Bit 7 = übernehmen, §1.6). ARGB-Header n → n, Mainboard → 4.
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

    /// <summary>Send several direct channels as one block under one lock (smoothest, §1.9).
    /// Mehrere Direct-Kanäle als ein Block unter einer Sperre senden (am ruhigsten, §1.9).</summary>
    public void StreamDirectFrame(IEnumerable<(int DirectChannel, Rgb[] Leds)> frame)
    {
        using var _ = hid.Lock();
        foreach (var (dc, leds) in frame) StreamDirectLocked(dc, leds, true);
    }

    /// <summary>Read a unit's LED count (<c>EC 31 &lt;unit&gt;</c>, reply [4..5], §1.8).
    /// LED-Anzahl einer Unit lesen (<c>EC 31 &lt;unit&gt;</c>, Antwort [4..5], §1.8).</summary>
    public int? ReadLedCount(int unit)
    {
        using var _ = hid.Lock();
        var r = hid.QueryIndexed(0x31, (byte)unit, 1000);
        return r == null ? null : (r[4] << 8) | r[5];
    }

    /// <summary>
    /// Set an ARGB header's LED count like the HAL (§1.8): channel to direct, old LEDs black, short wait, then
    /// <c>EC 3B &lt;unit&gt; &lt;hi&gt; &lt;lo&gt;</c>. Clears the other headers' direct buffers on that channel; reapply afterwards.
    /// LED-Anzahl eines ARGB-Headers setzen wie die HAL (§1.8): Kanal auf Direct, alte LEDs schwarz, kurz warten, dann
    /// <c>EC 3B &lt;unit&gt; &lt;hi&gt; &lt;lo&gt;</c>. Löscht die Direct-Puffer der anderen Header des Kanals; danach neu anwenden.
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

    /// <summary><c>EC 3F 55</c>: apply/store (§1.2). Don't send in loops.
    /// <c>EC 3F 55</c>: übernehmen/speichern (§1.2). Nicht in Schleifen senden.</summary>
    public void Commit()
    {
        using var _ = hid.Lock();
        hid.Write(0x3F, 0x55);
    }

    public void Dispose() => hid.Dispose();
}
