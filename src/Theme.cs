using System;
using System.Drawing;
using Microsoft.Win32;

class Theme
{
    public bool Dark;
    public Color Bg, Surface, Border, Text, Muted, Accent, AccentSoft, Grid, Track, Hover, Danger, Good, Warn, Other;
    public Color[] Series;

    public static Theme Current = Make(false);
    public static Theme Light { get { return Make(false); } }

    public static void Apply(string pref)
    {
        bool dark = pref == "dark" || (pref != "light" && SystemDark());
        Current = Make(dark);
    }

    static bool SystemDark()
    {
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            {
                object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                return v is int && (int)v == 0;
            }
        }
        catch { return false; }
    }

    static Color H(int rgb) { return Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }

    static Theme Make(bool dark)
    {
        var t = new Theme();
        t.Dark = dark;
        if (!dark)
        {
            t.Bg = H(0xF3F5FA); t.Surface = H(0xFFFFFF); t.Border = H(0xE3E7EF); t.Text = H(0x141824); t.Muted = H(0x687080);
            t.Accent = H(0x4C6FFF); t.AccentSoft = H(0xE8EDFF); t.Grid = H(0xEDF0F5); t.Track = H(0xEEF1F6); t.Hover = H(0xF4F6FB);
            t.Danger = H(0xE5484D); t.Good = H(0x16A34A); t.Warn = H(0xD97706); t.Other = H(0xB4BAC6);
            t.Series = new[] { H(0x4C6FFF), H(0x14B8A6), H(0xF59E0B), H(0xEC4899), H(0x8B5CF6) };
        }
        else
        {
            t.Bg = H(0x0F1115); t.Surface = H(0x181B22); t.Border = H(0x272B35); t.Text = H(0xE9EBF1); t.Muted = H(0x9AA1AE);
            t.Accent = H(0x7C9CFF); t.AccentSoft = H(0x222B4A); t.Grid = H(0x232731); t.Track = H(0x252A34); t.Hover = H(0x1E222B);
            t.Danger = H(0xFF6369); t.Good = H(0x3DD68C); t.Warn = H(0xFBBF24); t.Other = H(0x5B6270);
            t.Series = new[] { H(0x7C9CFF), H(0x2DD4BF), H(0xFBBF24), H(0xF472B6), H(0xA78BFA) };
        }
        return t;
    }
}
