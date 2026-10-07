using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

// Groups apps and websites into Productive / Communication / Entertainment / Social.
// Defaults cover common apps and sites; the user can change any app or site from the dashboard.
static class Categories
{
    public static readonly string[] Names = { "Productive", "Communication", "Entertainment", "Social", "Browsing", "Other" };

    static readonly Dictionary<string, string> apps = Map(
        "Productive", "excel.exe winword.exe powerpnt.exe onenote.exe msaccess.exe mspub.exe code.exe devenv.exe notepad.exe notepad++.exe " +
                      "acrobat.exe acrord32.exe sumatrapdf.exe calculatorapp.exe calc.exe tally.exe tallyprime.exe busy.exe marg.exe " +
                      "idea64.exe pycharm64.exe rider64.exe webstorm64.exe android studio.exe studio64.exe figma.exe photoshop.exe illustrator.exe " +
                      "afterfx.exe premiere pro.exe coreldrw.exe windowsterminal.exe cmd.exe powershell.exe pwsh.exe wt.exe explorer.exe " +
                      "notion.exe obsidian.exe claude.exe chatgpt.exe cursor.exe sublime_text.exe soffice.bin scalc.exe swriter.exe wps.exe et.exe",
        "Communication", "whatsapp.exe whatsapp.root.exe teams.exe ms-teams.exe slack.exe zoom.exe outlook.exe olk.exe thunderbird.exe " +
                         "telegram.exe skype.exe signal.exe webex.exe anydesk.exe teamviewer.exe",
        "Entertainment", "spotify.exe vlc.exe wmplayer.exe microsoft.media.player.exe netflix.exe steam.exe epicgameslauncher.exe " +
                         "potplayermini64.exe mpc-hc64.exe music.ui.exe video.ui.exe",
        "Social", "discord.exe instagram.exe facebook.exe");

    static readonly Dictionary<string, string> sites = Map(
        "Productive", "google-sheets google-docs google-slides google-drive google-forms google-calendar google-search github.com " +
                      "stackoverflow.com notion.so chatgpt.com claude.ai gemini.google.com office.com sharepoint.com canva.com figma.com " +
                      "trello.com asana.com airtable.com zoho.com tallysolutions.com localhost local-file",
        "Communication", "gmail outlook google-meet whatsapp-web microsoft-teams slack.com teams.microsoft.com outlook.live.com outlook.office.com zoom.us",
        "Entertainment", "youtube netflix.com primevideo.com hotstar.com jiocinema.com spotify.com twitch.tv sonyliv.com zee5.com music.apple.com",
        "Social", "facebook.com instagram.com x-(twitter) x.com twitter.com linkedin linkedin.com reddit.com pinterest.com threads.net snapchat.com quora.com");

    static Dictionary<string, string> Map(params string[] pairs)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < pairs.Length; i += 2)
            foreach (var k in pairs[i + 1].Split(' ')) if (k.Length > 0) d[k] = pairs[i];
        return d;
    }

    static string Norm(string site) { return site.ToLowerInvariant().Replace(' ', '-'); }

    public static string ForApp(string key)
    {
        string c;
        if (Settings.CategoryOverrides.TryGetValue("app:" + key, out c)) return c;
        if (key == "desktop" || key == "windows-shell" || key == "screen-time-tracker") return "Other";
        if (Browser.Is(key)) return "Browsing";
        return apps.TryGetValue(key, out c) ? c : "Other";
    }

    public static string ForSite(string site)
    {
        if (string.IsNullOrEmpty(site)) return "Browsing";
        string c;
        if (Settings.CategoryOverrides.TryGetValue("site:" + site, out c)) return c;
        string n = Norm(site);
        if (sites.TryGetValue(n, out c)) return c;
        foreach (var kv in sites)
            if (kv.Key.Contains(".") && n.EndsWith("." + kv.Key)) return kv.Value;
        return "Browsing";
    }

    /// Moves an app ("app:chrome.exe") or site ("site:youtube.com") to the next category.
    public static void Cycle(string overrideKey, string current)
    {
        int i = Array.IndexOf(Names, current);
        Settings.CategoryOverrides[overrideKey] = Names[(i + 1) % Names.Length];
        Settings.Save();
    }

    public static Color ColorFor(string cat, Theme t)
    {
        switch (cat)
        {
            case "Productive": return t.Good;
            case "Communication": return t.Series[0];
            case "Entertainment": return t.Series[2];
            case "Social": return t.Series[3];
            case "Browsing": return t.Series[1];
            default: return t.Other;
        }
    }

    /// Seconds per category for a set of apps and their windows. Browser time is split by website.
    public static Dictionary<string, double> Totals(IEnumerable<AppUsage> appList, Func<string, List<ItemUsage>> itemsFor)
    {
        var r = new Dictionary<string, double>();
        foreach (var n in Names) r[n] = 0;
        foreach (var a in appList)
        {
            if (Browser.Is(a.Key) && !Settings.CategoryOverrides.ContainsKey("app:" + a.Key))
            {
                double rest = a.Seconds;
                foreach (var it in itemsFor(a.Key))
                {
                    if (it.Site == null) continue;
                    r[ForSite(it.Site)] += it.Seconds;
                    rest -= it.Seconds;
                }
                if (rest > 0) r["Browsing"] += rest;
            }
            else r[ForApp(a.Key)] += a.Seconds;
        }
        return r;
    }
}
