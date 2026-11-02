using System.Windows;
using System.Windows.Media;
using Borealis.Core.Lighting;

namespace Borealis.App.Controls;

/// <summary>Strich-Symbole (24×24) für Effekte, im Stil der Effekt-Kacheln von Armoury Crate.</summary>
static class Icons
{
    static readonly Dictionary<FirmwareEffect, Geometry> Effects = new()
    {
        [FirmwareEffect.Static] = Parse("M12,3 A9,9 0 1 1 11.99,3 Z M12,8.5 A3.5,3.5 0 1 1 11.99,8.5 Z"),
        [FirmwareEffect.Breathing] = Parse("M12,10 A2,2 0 1 1 11.99,10 Z M12,6 A6,6 0 1 1 11.99,6 Z M12,2 A10,10 0 1 1 11.99,2 Z"),
        [FirmwareEffect.Strobing] = Parse("M13,2 L5,13 L11,13 L10,22 L19,10 L13,10 Z"),
        [FirmwareEffect.ColorCycle] = Parse("M20,12 A8,8 0 1 1 17.66,6.34 M20,3 L20,8 L15,8"),
        [FirmwareEffect.Rainbow] = Parse("M2.5,18 A9.5,9.5 0 0 1 21.5,18 M6,18 A6,6 0 0 1 18,18 M9.5,18 A2.5,2.5 0 0 1 14.5,18"),
        // Komet: Kopf mit Kern, gewölbter Schweif nach links unten
        [FirmwareEffect.Comet] = Parse("M18.3,11.87 A4.5,4.5 0 1 0 12.13,5.7 Q6.72,12.5 3,21 Q11.5,17.29 18.3,11.87 Z M18.2,8 A2.2,2.2 0 1 0 13.8,8 A2.2,2.2 0 1 0 18.2,8 Z"),
        [FirmwareEffect.FlashAndDash] = Parse("M2,12 L6,12 M9,12 L13,12 M16,12 L22,12 M4,7 L10,7 M14,17 L20,17"),
        [FirmwareEffect.Wave] = Parse("M2,12 C5,4 8,4 10,12 C12,20 15,20 17,12 C18.5,6 20.5,6 22,9"),
        [FirmwareEffect.GlowingYoyo] = Parse("M6,9 A3,3 0 1 1 5.99,9 Z M18,9 A3,3 0 1 1 17.99,9 Z M9,12 L15,12 M13,10 L15,12 L13,14"),
        [FirmwareEffect.StarryNight] = Parse("M7,3 L8,7 L12,8 L8,9 L7,13 L6,9 L2,8 L6,7 Z M17,11 L17.8,14.2 L21,15 L17.8,15.8 L17,19 L16.2,15.8 L13,15 L16.2,14.2 Z M17,3 L17,5 M16,4 L18,4"),
        [FirmwareEffect.Off] = Parse("M12,3 L12,11 M7,6 A8,8 0 1 0 17,6"),
        [FirmwareEffect.BreathRainbow] = Parse("M12,9 A2,2 0 1 1 11.99,9 Z M2.5,19 A9.5,9.5 0 0 1 21.5,19 M6,19 A6,6 0 0 1 18,19"),
        // Regenbogen-Komet: derselbe Umriss, innen eine zweite Schicht als Farbband
        [FirmwareEffect.RainbowComet] = Parse("M18.3,11.87 A4.5,4.5 0 1 0 12.13,5.7 Q6.72,12.5 3,21 Q11.5,17.29 18.3,11.87 Z M17.28,9.79 A2.2,2.2 0 1 0 14.21,6.72 Q10.1,11.36 7,17 Q12.63,13.9 17.28,9.79 Z"),
        [FirmwareEffect.MovingFlash] = Parse("M2,8 L8,8 M10,12 L16,12 M17,16 L22,16 M4,16 L9,16 M14,8 L18,8"),
        [FirmwareEffect.Fireworks] = Parse("M12,12 L12,3 M12,12 L18.5,6 M12,12 L21,13 M12,12 L16.5,20 M12,12 L7.5,20 M12,12 L3,13 M12,12 L5.5,6"),
        [FirmwareEffect.Music] = Parse("M9,18 A3,3 0 1 1 8.99,18 Z M12,18 L12,4 L19,6 L19,9 L12,7"),
        [FirmwareEffect.Smart] = Parse("M10,14 L10,4 A2,2 0 0 1 14,4 L14,14 A4,4 0 1 1 10,14 Z M12,9 L12,17"),
        [FirmwareEffect.Fire] = Parse("M12,22 C7,22 5,18 6,14 C7,11 9,10 9,7 C11,9 12,10 12,12 C13,10 14,8 13,3 C17,6 19,10 18,15 C17.5,19 15,22 12,22 Z"),
        [FirmwareEffect.Meteor] = Parse("M21,3 L11,13 M16,3 L9,10 M21,8 L14,15 M7,14 A3,3 0 1 1 6.99,14 Z"),
        [FirmwareEffect.Scanner] = Parse("M2,12 L22,12 M9,8.5 L15,8.5 L15,15.5 L9,15.5 Z M4.5,10 L4.5,14 M19.5,10 L19.5,14"),
        [FirmwareEffect.Plasma] = Parse("M2,14 C5,8 8,20 12,14 C16,8 19,20 22,14 M2,8 C5,2 8,14 12,8 C16,2 19,14 22,8"),
        [FirmwareEffect.Heartbeat] = Parse("M2,12 L7,12 L9,6.5 L12,18 L14.5,9.5 L16.5,12 L22,12"),
        [FirmwareEffect.Twinkle] = Parse("M10,3 L11,9 L17,10 L11,11 L10,17 L9,11 L3,10 L9,9 Z M18,14 L18.6,16.4 L21,17 L18.6,17.6 L18,20 L17.4,17.6 L15,17 L17.4,16.4 Z"),
    };

    public static Geometry Effect(FirmwareEffect e) => Effects.TryGetValue(e, out var g) ? g : Effects[FirmwareEffect.Static];

    public static Geometry Resource(string key) => (Geometry)Application.Current.FindResource(key);

    static Geometry Parse(string data)
    {
        var g = Geometry.Parse(data);
        g.Freeze();
        return g;
    }
}
