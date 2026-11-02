using System.Text.Json;
using Borealis.Core.Lighting;
using Borealis.Core.Localization;

namespace Borealis.Core.Profiles;

/// <summary>
/// Lighting profile as a file (<c>*.borealis.json</c>): a small envelope with format and version, so foreign JSON and later
/// formats are recognized. Fans, calibration and zone names are left out, they depend on the own hardware.
/// Beleuchtungsprofil als Datei (<c>*.borealis.json</c>): ein kleiner Umschlag mit Format und Version, damit fremde JSON-Dateien
/// und spätere Formate erkannt werden. Lüfter, Farbabgleich und Zonennamen fehlen, sie hängen an der eigenen Hardware.
/// </summary>
public static class ProfileTransfer
{
    public const string FileExtension = ".borealis.json";
    const string FormatName = "borealis-profile";
    const int FormatVersion = 1;
    const int MaxFileSize = 256 * 1024;

    sealed class Envelope
    {
        public string Format { get; set; } = "";
        public int Version { get; set; }
        public LightingProfile? Profile { get; set; }
    }

    public static string Export(LightingProfile profile) =>
        JsonSerializer.Serialize(new Envelope { Format = FormatName, Version = FormatVersion, Profile = profile.Clone() }, ProfileStore.Json);

    /// <summary>Read and validate an exported profile; <see cref="InvalidDataException"/> with a reason if it doesn't fit.
    /// Exportiertes Profil lesen und prüfen; <see cref="InvalidDataException"/> mit Begründung, wenn es nicht passt.</summary>
    public static LightingProfile Import(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > MaxFileSize) throw new InvalidDataException(Loc.T("Die Datei ist zu groß für ein Borealis-Profil.", "The file is too large for a Borealis profile."));
        Envelope? e;
        try { e = JsonSerializer.Deserialize<Envelope>(File.ReadAllText(path), ProfileStore.Json); }
        catch (JsonException ex) { throw new InvalidDataException(Loc.T("Die Datei ist kein gültiges Borealis-Profil: ", "The file is not a valid Borealis profile: ") + ex.Message); }
        if (e?.Format != FormatName || e.Profile == null) throw new InvalidDataException(Loc.T("Die Datei ist kein Borealis-Profil.", "The file is not a Borealis profile."));
        if (e.Version > FormatVersion) throw new InvalidDataException(Loc.T("Das Profil stammt aus einer neueren Borealis-Version.", "The profile comes from a newer Borealis version."));

        var p = e.Profile;
        p.Name = (p.Name ?? "").Trim();
        if (p.Name.Length == 0) p.Name = Path.GetFileName(path).Replace(FileExtension, "", StringComparison.OrdinalIgnoreCase);
        p.Sync ??= new SyncSetting();
        p.Mainboard ??= new GroupSetting();
        p.Argb ??= new GroupSetting();
        p.Ram ??= new GroupSetting();
        p.StandbyMainboard ??= new GroupSetting();
        p.StandbyArgb ??= new GroupSetting();
        p.Brightness = Math.Clamp(p.Brightness, 1, 100);

        CheckColor(p.Sync.Color);
        CheckColor(p.Sync.Color2);
        CheckColor(p.Sync.Background);
        foreach (var g in new[] { p.Mainboard, p.Argb, p.Ram, p.StandbyMainboard, p.StandbyArgb })
        {
            g.Colors ??= new();
            foreach (var c in g.Colors.Values) CheckColor(c);
            CheckColor(g.DefaultColor);
            CheckColor(g.Color2);
            CheckColor(g.Background);
        }
        return p;
    }

    static void CheckColor(string? color)
    {
        if (!Rgb.TryParse(color, out _)) throw new InvalidDataException(Loc.T($"Ungültige Farbe „{color}“ im Profil.", $"Invalid color “{color}” in the profile."));
    }
}
