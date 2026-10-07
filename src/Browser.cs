using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;

// Turns window titles into readable "what was open" names, and reads the website from a browser's address bar.
static class Browser
{
    public const string PrivateLabel = "Private / incognito window";

    static readonly HashSet<string> exes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { "chrome.exe", "brave.exe", "msedge.exe", "firefox.exe", "opera.exe", "vivaldi.exe", "arc.exe", "iexplore.exe", "browser.exe", "chromium.exe", "waterfox.exe", "librewolf.exe" };

    public static bool Is(string exe) { return exe != null && exes.Contains(exe); }

    static readonly Regex BrowserSuffix = new Regex(@"\s+[-–—]\s+(Google Chrome|Brave|Mozilla Firefox|Firefox|Opera|Vivaldi|Chromium|Arc|Microsoft\W*Edge)(\s*\(.*\))?\s*$", RegexOptions.IgnoreCase);
    static readonly Regex EdgeProfile = new Regex(@"\s+[-–—]\s+[^-–—]{1,40}$");
    static readonly Regex MorePages = new Regex(@"\s+and \d+ more pages?$", RegexOptions.IgnoreCase);
    static readonly Regex Counts = new Regex(@"^\(\d[\d,]*\+?\)\s*|\s*\(\d[\d,]*\+?\)(?=\s|$)");

    /// A short, stable name for what the window shows ("" when the title adds nothing beyond the app name).
    public static string Clean(string title, string exe, string appName)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";
        string t = title.Replace('\t', ' ').Trim();
        if (Is(exe))
        {
            if (Regex.IsMatch(t, @"incognito|inprivate|private browsing|\(private\)", RegexOptions.IgnoreCase)) return PrivateLabel;
            bool edge = Regex.IsMatch(t, @"Microsoft\W*Edge\s*$", RegexOptions.IgnoreCase);
            t = BrowserSuffix.Replace(t, "");
            if (edge) { t = EdgeProfile.Replace(t, ""); t = MorePages.Replace(t, ""); }
        }
        else
        {
            // "Book1 - Excel", "notes.txt - Notepad", "file.cs - project - Visual Studio Code" -> drop the trailing app name
            var parts = Regex.Split(t, @"\s+[-–—|]\s+");
            if (parts.Length > 1 && LooksLikeApp(parts[parts.Length - 1], appName))
                t = t.Substring(0, t.Length - parts[parts.Length - 1].Length).TrimEnd(' ', '-', '–', '—', '|');
            t = t.TrimStart('*', '●', ' ');
        }
        t = Regex.Replace(Counts.Replace(t, " "), @"\s{2,}", " ").Trim();
        if (t.Length > 150) t = t.Substring(0, 149) + "…";
        if (string.Equals(t, appName, StringComparison.OrdinalIgnoreCase)) return "";
        return t;
    }

    static bool LooksLikeApp(string last, string appName)
    {
        last = last.Trim();
        if (last.Length == 0 || last.Length > 40 || appName == null) return false;
        if (appName.IndexOf(last, StringComparison.OrdinalIgnoreCase) >= 0 || last.IndexOf(appName, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        foreach (var w in appName.Split(' '))
            if (w.Length >= 4 && last.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    /// Best guess of the site from the page title alone (used until the address bar has been read).
    public static string SiteFromTitle(string t)
    {
        string[,] map =
        {
            { " - Google Sheets", "Google Sheets" }, { " - Google Docs", "Google Docs" }, { " - Google Slides", "Google Slides" },
            { " - Google Drive", "Google Drive" }, { " - Google Forms", "Google Forms" }, { " - YouTube", "YouTube" },
            { " - Gmail", "Gmail" }, { " | LinkedIn", "LinkedIn" }, { " / X", "X (Twitter)" }, { " - Google Search", "Google Search" },
            { " - Outlook", "Outlook" }, { " | Microsoft Teams", "Microsoft Teams" }, { " - Google Meet", "Google Meet" },
        };
        for (int i = 0; i < map.GetLength(0); i++)
            if (t.IndexOf(map[i, 0], StringComparison.OrdinalIgnoreCase) >= 0) return map[i, 1];
        if (t.StartsWith("WhatsApp", StringComparison.OrdinalIgnoreCase)) return "WhatsApp Web";
        if (t.StartsWith("ChatGPT", StringComparison.OrdinalIgnoreCase)) return "chatgpt.com";
        if (t.EndsWith("Instagram", StringComparison.OrdinalIgnoreCase)) return "instagram.com";
        if (t.EndsWith("Facebook", StringComparison.OrdinalIgnoreCase)) return "facebook.com";
        return null;
    }

    /// "https://docs.google.com/spreadsheets/d/..." -> "Google Sheets"; "https://www.amazon.in/..." -> "amazon.in".
    public static string SiteFromUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        url = url.Trim();
        if (url.IndexOf(' ') >= 0) return null; // the user is typing a search
        string lower = url.ToLowerInvariant();
        if (lower.StartsWith("chrome://") || lower.StartsWith("brave://") || lower.StartsWith("edge://") || lower.StartsWith("about:"))
            return lower.Contains("newtab") || lower.Contains("new-tab") ? "New tab" : "Browser settings";
        if (lower.StartsWith("file:")) return "Local file";
        int scheme = lower.IndexOf("://");
        if (scheme >= 0) lower = lower.Substring(scheme + 3);
        int slash = lower.IndexOfAny(new[] { '/', '?', '#' });
        string host = slash >= 0 ? lower.Substring(0, slash) : lower, path = slash >= 0 ? lower.Substring(slash) : "";
        int colon = host.IndexOf(':'); if (colon >= 0) host = host.Substring(0, colon);
        if (host.IndexOf('.') < 0 && host != "localhost") return null;
        if (host.StartsWith("www.")) host = host.Substring(4);
        if (host == "docs.google.com")
        {
            if (path.StartsWith("/spreadsheets")) return "Google Sheets";
            if (path.StartsWith("/presentation")) return "Google Slides";
            if (path.StartsWith("/forms")) return "Google Forms";
            return "Google Docs";
        }
        switch (host)
        {
            case "mail.google.com": return "Gmail";
            case "drive.google.com": return "Google Drive";
            case "meet.google.com": return "Google Meet";
            case "calendar.google.com": return "Google Calendar";
            case "youtube.com": case "m.youtube.com": case "music.youtube.com": return "YouTube";
            case "web.whatsapp.com": return "WhatsApp Web";
            case "google.com": case "google.co.in": return path.StartsWith("/search") ? "Google Search" : "google.com";
        }
        return host;
    }

    // ---- reading the address bar with UI Automation (on a background thread; can take a moment the first time) ----

    public static readonly ConcurrentDictionary<string, string> Resolved = new ConcurrentDictionary<string, string>();
    static readonly object gate = new object();
    static readonly AutoResetEvent signal = new AutoResetEvent(false);
    static IntPtr reqHwnd;
    static string reqKey, reqTitle;
    static Thread worker;

    public static void Request(IntPtr hwnd, string itemKey, string rawTitle)
    {
        lock (gate) { reqHwnd = hwnd; reqKey = itemKey; reqTitle = rawTitle; }
        if (worker == null)
        {
            worker = new Thread(Loop);
            worker.IsBackground = true;
            worker.Name = "address-bar";
            worker.SetApartmentState(ApartmentState.MTA);
            worker.Start();
        }
        signal.Set();
    }

    static void Loop()
    {
        var edits = new Dictionary<IntPtr, AutomationElement>();
        while (true)
        {
            signal.WaitOne();
            IntPtr h; string key, title;
            lock (gate) { h = reqHwnd; key = reqKey; title = reqTitle; }
            if (key == null || Resolved.ContainsKey(key)) continue;
            try
            {
                string url = ReadUrl(h, edits);
                // make sure the tab did not change while we were reading
                if (Native.WindowText(h) != title) continue;
                string site = SiteFromUrl(url);
                if (site != null)
                {
                    if (Resolved.Count > 5000) Resolved.Clear();
                    Resolved[key] = site;
                }
            }
            catch { }
        }
    }

    /// Test hook: reads the address bar of one window synchronously.
    public static string ReadUrlNow(IntPtr h) { return ReadUrl(h, new Dictionary<IntPtr, AutomationElement>()); }

    static string ReadUrl(IntPtr h, Dictionary<IntPtr, AutomationElement> edits)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            AutomationElement edit;
            if (!edits.TryGetValue(h, out edit))
            {
                if (edits.Count > 50) edits.Clear();
                var root = AutomationElement.FromHandle(h);
                edit = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
                if (edit == null) return null;
                edits[h] = edit;
            }
            try
            {
                object p;
                if (edit.TryGetCurrentPattern(ValuePattern.Pattern, out p)) return ((ValuePattern)p).Current.Value;
                return null;
            }
            catch (ElementNotAvailableException) { edits.Remove(h); }
        }
        return null;
    }
}
