using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

// Usage of one app on one day.
class AppUsage
{
    public string Key;
    public double Seconds;
    public int Opens;
    public double[] Hours = new double[24];
}

// One window inside an app: a web page, a Google Sheet, an Excel workbook, a chat...
class ItemUsage
{
    public string AppKey, Title, Site;
    public double Seconds;
    public int Opens;
    public int Id;
}

// One continuous stretch in one app window. Times are seconds since midnight (internet-synced clock).
class Segment
{
    public double Start, End;
    public string Key;
    public ItemUsage Item;
}

// Sign-in, lock, sleep, idle... entries for the activity log.
class LogEvent
{
    public double At;
    public string Kind, Text;
    public bool FromWindows;
}

// Time you add yourself (meeting, client visit, lunch...) - offline work counts towards projects, breaks do not.
class ManualEntry
{
    public double Start, End;
    public string Kind = "work", Label = "", Client = "";
    public bool IsWork { get { return Kind == "work"; } }
}

// Everything recorded for one calendar day.
class DayData
{
    public DateTime Date;
    public int Unlocks;
    public double MeetingSeconds;
    public List<ManualEntry> Manual = new List<ManualEntry>();
    public bool Merged; // includes data from another PC
    public Dictionary<string, AppUsage> Apps = new Dictionary<string, AppUsage>(StringComparer.OrdinalIgnoreCase);
    public List<ItemUsage> ItemList = new List<ItemUsage>();
    public Dictionary<string, ItemUsage> Items = new Dictionary<string, ItemUsage>(StringComparer.Ordinal);
    public List<Segment> Segments = new List<Segment>();
    public List<LogEvent> Events = new List<LogEvent>();

    public AppUsage Get(string key)
    {
        AppUsage u;
        if (!Apps.TryGetValue(key, out u)) { u = new AppUsage(); u.Key = key; Apps[key] = u; }
        return u;
    }

    public static string ItemKey(string app, string title) { return app.ToLowerInvariant() + "\u0001" + title; }

    public ItemUsage GetItem(string app, string title)
    {
        string k = ItemKey(app, title);
        ItemUsage i;
        if (!Items.TryGetValue(k, out i))
        {
            i = new ItemUsage(); i.AppKey = app; i.Title = title;
            Items[k] = i;
            ItemList.Add(i);
        }
        return i;
    }

    public void AddEvent(double at, string kind, string text)
    {
        var e = new LogEvent(); e.At = at; e.Kind = kind; e.Text = text;
        Events.Add(e);
    }

    public double Total
    {
        get { double t = 0; foreach (var a in Apps.Values) t += a.Seconds; return t; }
    }

    public int Opens
    {
        get { int t = 0; foreach (var a in Apps.Values) t += a.Opens; return t; }
    }

    public List<AppUsage> Sorted()
    {
        return Apps.Values.Where(a => a.Seconds >= 1).OrderByDescending(a => a.Seconds).ToList();
    }

    public List<ItemUsage> ItemsFor(string app)
    {
        return ItemList.Where(i => i.Seconds >= 1 && string.Equals(i.AppKey, app, StringComparison.OrdinalIgnoreCase))
                       .OrderByDescending(i => i.Seconds).ToList();
    }

    public double FirstActive { get { return Segments.Count == 0 ? -1 : Segments.Min(s => s.Start); } }
    public double LastActive { get { return Segments.Count == 0 ? -1 : Segments.Max(s => s.End); } }
}

class AppInfo
{
    public string Key, Name, Path;
}

// Local storage: %APPDATA%\Screen Time Tracker\days\yyyy-MM-dd.tsv plus apps.txt / settings.txt / limits.txt / categories.txt.
// Nothing ever leaves the PC.
static class Store
{
    public static string Root, DaysDir;
    static Dictionary<string, AppInfo> infos = new Dictionary<string, AppInfo>(StringComparer.OrdinalIgnoreCase);
    static bool infosDirty;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static void Init(string root)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Screen Time Tracker");
        DaysDir = Path.Combine(Root, "days");
        Directory.CreateDirectory(DaysDir);
        LoadInfos();
        Settings.Load();
    }

    static string DayPath(DateTime d) { return Path.Combine(DaysDir, d.ToString("yyyy-MM-dd", Inv) + ".tsv"); }

    static double D(string s) { double v; double.TryParse(s, NumberStyles.Float, Inv, out v); return v; }
    static string N(double v) { return v.ToString("0.#", Inv); }
    static string Clean(string s) { return s == null ? "" : s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }

    /// This PC's own data for a day (what the tracker writes to).
    public static DayData LoadDay(DateTime date) { return LoadFile(DayPath(date), date); }

    /// A day for viewing and reports: this PC's data plus other PCs' (Settings.ExtraFolders), if any.
    public static DayData ViewDay(DateTime date) { return DataTools.WithOtherPcs(LoadDay(date)); }

    public static DayData LoadFile(string p, DateTime date)
    {
        var d = new DayData();
        d.Date = date.Date;
        if (!File.Exists(p)) return d;
        var byId = new Dictionary<int, ItemUsage>();
        try
        {
            foreach (var line in File.ReadAllLines(p, Encoding.UTF8))
            {
                var f = line.Split('\t');
                if (f.Length < 2) continue;
                switch (f[0])
                {
                    case "unlocks": int.TryParse(f[1], out d.Unlocks); break;
                    case "app":
                        if (f.Length < 5) break;
                        var u = d.Get(f[1]);
                        u.Seconds = D(f[2]);
                        int.TryParse(f[3], out u.Opens);
                        var hs = f[4].Split(',');
                        for (int i = 0; i < 24 && i < hs.Length; i++) u.Hours[i] = D(hs[i]);
                        break;
                    case "item":
                        if (f.Length < 7) break;
                        var it = d.GetItem(f[2], f[4]);
                        it.Site = f[3].Length == 0 ? null : f[3];
                        it.Seconds = D(f[5]);
                        int.TryParse(f[6], out it.Opens);
                        int id;
                        if (int.TryParse(f[1], out id)) byId[id] = it;
                        break;
                    case "seg":
                        if (f.Length < 5) break;
                        var s = new Segment();
                        s.Start = D(f[1]); s.End = D(f[2]); s.Key = f[3];
                        int iid; ItemUsage ref1;
                        if (int.TryParse(f[4], out iid) && byId.TryGetValue(iid, out ref1)) s.Item = ref1;
                        d.Segments.Add(s);
                        break;
                    case "ev":
                        if (f.Length < 4) break;
                        d.AddEvent(D(f[1]), f[2], f[3]);
                        break;
                    case "meet": d.MeetingSeconds = D(f[1]); break;
                    case "man":
                        if (f.Length < 6) break;
                        var m = new ManualEntry();
                        m.Start = D(f[1]); m.End = D(f[2]); m.Kind = f[3]; m.Label = f[4]; m.Client = f[5];
                        d.Manual.Add(m);
                        break;
                }
            }
        }
        catch { }
        d.Segments.Sort((a, b) => a.Start.CompareTo(b.Start));
        return d;
    }

    public static void SaveDay(DayData d)
    {
        if (d.Merged) return; // never write a merged (multi-PC) view back
        if (d.Apps.Count == 0 && d.Unlocks == 0 && d.Events.Count == 0 && d.Manual.Count == 0) return;
        var sb = new StringBuilder();
        sb.Append("unlocks\t").Append(d.Unlocks).Append('\n');
        sb.Append("meet\t").Append(N(d.MeetingSeconds)).Append('\n');
        foreach (var m in d.Manual)
            sb.Append("man\t").Append(N(m.Start)).Append('\t').Append(N(m.End)).Append('\t').Append(m.Kind).Append('\t')
              .Append(Clean(m.Label)).Append('\t').Append(Clean(m.Client)).Append('\n');
        foreach (var u in d.Apps.Values.OrderByDescending(a => a.Seconds))
        {
            if (u.Seconds < 0.5) continue;
            sb.Append("app\t").Append(u.Key).Append('\t').Append(N(u.Seconds)).Append('\t').Append(u.Opens).Append('\t');
            sb.Append(string.Join(",", u.Hours.Select(h => N(h)))).Append('\n');
        }
        int next = 0;
        foreach (var it in d.ItemList)
        {
            it.Id = -1;
            if (it.Seconds < 0.5) continue;
            it.Id = next++;
            sb.Append("item\t").Append(it.Id).Append('\t').Append(it.AppKey).Append('\t').Append(Clean(it.Site)).Append('\t')
              .Append(Clean(it.Title)).Append('\t').Append(N(it.Seconds)).Append('\t').Append(it.Opens).Append('\n');
        }
        foreach (var s in d.Segments)
        {
            if (s.End - s.Start < 0.5) continue;
            sb.Append("seg\t").Append(N(s.Start)).Append('\t').Append(N(s.End)).Append('\t').Append(s.Key).Append('\t')
              .Append(s.Item == null ? -1 : s.Item.Id).Append('\n');
        }
        foreach (var e in d.Events)
            sb.Append("ev\t").Append(N(e.At)).Append('\t').Append(e.Kind).Append('\t').Append(Clean(e.Text)).Append('\n');
        AtomicWrite(DayPath(d.Date), sb.ToString());
        SaveInfos();
        if (Settings.MirrorFolder.Length > 0)
        {
            try
            {
                string dir = Path.Combine(Settings.MirrorFolder, Environment.MachineName, "days");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, d.Date.ToString("yyyy-MM-dd", Inv) + ".tsv"), sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }
    }

    public static List<DateTime> AllDays()
    {
        var list = new List<DateTime>();
        foreach (var f in Directory.GetFiles(DaysDir, "*.tsv"))
        {
            DateTime d;
            if (DateTime.TryParseExact(Path.GetFileNameWithoutExtension(f), "yyyy-MM-dd", Inv, DateTimeStyles.None, out d)) list.Add(d);
        }
        list.Sort();
        return list;
    }

    public static void DeleteAll()
    {
        foreach (var f in Directory.GetFiles(DaysDir, "*.tsv")) File.Delete(f);
    }

    /// Writes three CSV files next to each other: apps per day, the full activity log, and daily log-in/log-out times.
    public static List<string> ExportCsv(string path, DayData today)
    {
        string dir = Path.GetDirectoryName(path), stem = Path.GetFileNameWithoutExtension(path);
        string pApps = path, pLog = Path.Combine(dir, stem + "-activity.csv"), pSess = Path.Combine(dir, stem + "-login-logout.csv");
        var apps = new StringBuilder("date,app,exe,category,minutes,times_opened\r\n");
        var log = new StringBuilder("date,start,end,minutes,app,window_or_page,site\r\n");
        var sess = new StringBuilder("date,log_in,log_in_via,first_activity,last_activity,log_out,log_out_via,pc_on_minutes,screen_time_minutes\r\n");
        var days = AllDays();
        if (!days.Contains(today.Date)) days.Add(today.Date);
        foreach (var date in days)
        {
            var d = date == today.Date ? today : LoadDay(date);
            string ds = date.ToString("yyyy-MM-dd", Inv);
            foreach (var u in d.Sorted())
                apps.Append(ds).Append(',').Append(Csv(Info(u.Key).Name)).Append(',').Append(Csv(u.Key)).Append(',')
                    .Append(Categories.ForApp(u.Key)).Append(',').Append((u.Seconds / 60).ToString("0.0", Inv)).Append(',').Append(u.Opens).Append("\r\n");
            foreach (var s in d.Segments)
            {
                if (s.End - s.Start < 1) continue;
                log.Append(ds).Append(',').Append(Util.Time(s.Start, true)).Append(',').Append(Util.Time(s.End, true)).Append(',')
                   .Append(((s.End - s.Start) / 60).ToString("0.0", Inv)).Append(',').Append(Csv(Info(s.Key).Name)).Append(',')
                   .Append(Csv(s.Item == null ? "" : s.Item.Title)).Append(',').Append(Csv(s.Item == null ? "" : s.Item.Site ?? "")).Append("\r\n");
            }
            var si = Sessions.Compute(d, Sessions.Merged(d), false, null);
            sess.Append(ds).Append(',').Append(Util.Time(si.LogIn)).Append(',').Append(si.InVia).Append(',').Append(Util.Time(si.First)).Append(',')
                .Append(Util.Time(si.Last)).Append(',').Append(Util.Time(si.LogOut)).Append(',').Append(si.OutVia).Append(',')
                .Append((si.OnFor(0) / 60).ToString("0", Inv)).Append(',').Append((d.Total / 60).ToString("0", Inv)).Append("\r\n");
        }
        var enc = new UTF8Encoding(true);
        File.WriteAllText(pApps, apps.ToString(), enc);
        File.WriteAllText(pLog, log.ToString(), enc);
        File.WriteAllText(pSess, sess.ToString(), enc);
        return new List<string> { pApps, pLog, pSess };
    }

    static string Csv(string s)
    {
        if (s == null) return "";
        return s.IndexOfAny(new[] { ',', '"' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    // ---- app names / exe paths ----

    public static AppInfo Info(string key)
    {
        AppInfo i;
        if (infos.TryGetValue(key, out i)) return i;
        i = new AppInfo();
        i.Key = key;
        i.Name = key == "screen-time-tracker" ? "Screen Time Tracker" : Util.TitleFromExe(key);
        return i;
    }

    public static void Remember(string key, string name, string path)
    {
        AppInfo i;
        if (infos.TryGetValue(key, out i) && i.Name == name && (i.Path == path || string.IsNullOrEmpty(path))) return;
        i = new AppInfo(); i.Key = key; i.Name = name; i.Path = path;
        infos[key] = i;
        infosDirty = true;
    }

    static void LoadInfos()
    {
        string p = Path.Combine(Root, "apps.txt");
        if (!File.Exists(p)) return;
        foreach (var line in File.ReadAllLines(p, Encoding.UTF8))
        {
            var f = line.Split('\t');
            if (f.Length < 3) continue;
            var i = new AppInfo(); i.Key = f[0]; i.Name = f[1]; i.Path = f[2];
            infos[i.Key] = i;
        }
    }

    public static void SaveInfos()
    {
        if (!infosDirty) return;
        var sb = new StringBuilder();
        foreach (var i in infos.Values) sb.Append(i.Key).Append('\t').Append(i.Name).Append('\t').Append(i.Path ?? "").Append('\n');
        AtomicWrite(Path.Combine(Root, "apps.txt"), sb.ToString());
        infosDirty = false;
    }

    public static void AtomicWrite(string path, string content)
    {
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, content, Encoding.UTF8);
        if (File.Exists(path)) File.Delete(path);
        File.Move(tmp, path);
    }
}

static class Settings
{
    // tracking
    public static int IdleMinutes = 5;
    public static bool AudioKeepsActive = true;
    public static bool RecordTitles = true;
    public static List<string> IgnoreApps = new List<string>();
    // goals & reminders
    public static int BreakMinutes = 60;
    public static int DailyGoalMinutes = 0;
    public static int GoalProductiveMin = 0, GoalEntertainmentMax = 0, GoalSocialMax = 0;
    public static bool EyeCare = false;
    public static int WaterMinutes = 0;
    public static int LateNightMin = 23 * 60; // -1 = off
    // work
    public static int WorkStart = 9 * 60, WorkEnd = 18 * 60, GraceMinutes = 10, AskReasonMinutes = 15;
    public static string WorkDays = "1111110"; // Monday..Sunday
    public static string Currency = "₹";
    // focus mode
    public static List<string> FocusBlockCategories = new List<string> { "Entertainment", "Social" };
    public static List<string> FocusBlockExtra = new List<string>();
    // reports, look & feel
    public static bool WeeklyPdf = true;
    public static string ReportFolder = "";
    public static string LastWeeklyReport = "";
    public static string Theme = "system";
    public static string Language = "en";
    public static List<string> WorldClocks = new List<string> { "UTC", "GMT Standard Time", "Eastern Standard Time", "Arabian Standard Time", "Singapore Standard Time", "AUS Eastern Standard Time" };
    public static bool Hotkey = true, TrayHours = true, WidgetOn = false, CheckUpdates = true;
    public static int WidgetX = -1, WidgetY = -1;
    // privacy & data
    public static int KeepDays = 0; // 0 = forever
    public static string PinHash = "";
    public static List<string> ExtraFolders = new List<string>(); // other PCs' data folders to merge in
    public static string MirrorFolder = ""; // copy this PC's data here (e.g. OneDrive) so another PC can include it
    public static bool FirstRunDone;

    public static Dictionary<string, int> Limits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public static Dictionary<string, string> CategoryOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    static string FilePath { get { return Path.Combine(Store.Root, "settings.txt"); } }
    static string LimitsPath { get { return Path.Combine(Store.Root, "limits.txt"); } }
    static string CatPath { get { return Path.Combine(Store.Root, "categories.txt"); } }
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "Screen Time Tracker";

    static Dictionary<string, string> raw = new Dictionary<string, string>();
    static int I(string k, int def) { string v; int n; return raw.TryGetValue(k, out v) && int.TryParse(v, out n) ? n : def; }
    static bool B(string k, bool def) { string v; return raw.TryGetValue(k, out v) ? v == "1" : def; }
    static string Str(string k, string def) { string v; return raw.TryGetValue(k, out v) ? v : def; }
    static List<string> L(string k, List<string> def) { string v; return raw.TryGetValue(k, out v) ? v.Split(';').Where(x => x.Trim().Length > 0).Select(x => x.Trim()).ToList() : def; }

    public static void Load()
    {
        raw.Clear();
        if (File.Exists(FilePath))
            foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
            {
                int eq = line.IndexOf('=');
                if (eq > 0) raw[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
        IdleMinutes = Math.Max(1, I("idleMinutes", 5));
        AudioKeepsActive = B("audioKeepsActive", true);
        RecordTitles = B("recordTitles", true);
        IgnoreApps = L("ignoreApps", new List<string>());
        BreakMinutes = Math.Max(0, I("breakMinutes", 60));
        DailyGoalMinutes = Math.Max(0, I("dailyGoalMinutes", 0));
        GoalProductiveMin = I("goalProductiveMin", 0); GoalEntertainmentMax = I("goalEntertainmentMax", 0); GoalSocialMax = I("goalSocialMax", 0);
        EyeCare = B("eyeCare", false);
        WaterMinutes = I("waterMinutes", 0);
        LateNightMin = I("lateNightMin", 23 * 60);
        WorkStart = I("workStart", 9 * 60); WorkEnd = I("workEnd", 18 * 60); GraceMinutes = I("graceMinutes", 10);
        AskReasonMinutes = I("askReasonMinutes", 15);
        WorkDays = Str("workDays", "1111110"); if (WorkDays.Length != 7) WorkDays = "1111110";
        Currency = Str("currency", "₹");
        FocusBlockCategories = L("focusBlockCategories", new List<string> { "Entertainment", "Social" });
        FocusBlockExtra = L("focusBlockExtra", new List<string>());
        WeeklyPdf = B("weeklyPdf", true);
        ReportFolder = Str("reportFolder", "");
        LastWeeklyReport = Str("lastWeeklyReport", "");
        Theme = Str("theme", "system");
        Language = Str("language", "en");
        WorldClocks = L("worldClocks", WorldClocks);
        Hotkey = B("hotkey", true); TrayHours = B("trayHours", true); WidgetOn = B("widgetOn", false); CheckUpdates = B("checkUpdates", true);
        WidgetX = I("widgetX", -1); WidgetY = I("widgetY", -1);
        KeepDays = Math.Max(0, I("keepDays", 0));
        PinHash = Str("pinHash", "");
        ExtraFolders = L("extraFolders", new List<string>());
        MirrorFolder = Str("mirrorFolder", "");
        FirstRunDone = B("firstRunDone", false);

        Limits.Clear();
        if (File.Exists(LimitsPath))
            foreach (var line in File.ReadAllLines(LimitsPath))
            {
                var f = line.Split('\t');
                int n;
                if (f.Length == 2 && int.TryParse(f[1], out n) && n > 0) Limits[f[0]] = n;
            }
        CategoryOverrides.Clear();
        if (File.Exists(CatPath))
            foreach (var line in File.ReadAllLines(CatPath, Encoding.UTF8))
            {
                var f = line.Split('\t');
                if (f.Length == 2) CategoryOverrides[f[0]] = f[1];
            }
        Projects.Load();
    }

    public static void Save()
    {
        var kv = new List<string>
        {
            "idleMinutes=" + IdleMinutes, "audioKeepsActive=" + (AudioKeepsActive ? 1 : 0), "recordTitles=" + (RecordTitles ? 1 : 0),
            "ignoreApps=" + string.Join(";", IgnoreApps),
            "breakMinutes=" + BreakMinutes, "dailyGoalMinutes=" + DailyGoalMinutes,
            "goalProductiveMin=" + GoalProductiveMin, "goalEntertainmentMax=" + GoalEntertainmentMax, "goalSocialMax=" + GoalSocialMax,
            "eyeCare=" + (EyeCare ? 1 : 0), "waterMinutes=" + WaterMinutes, "lateNightMin=" + LateNightMin,
            "workStart=" + WorkStart, "workEnd=" + WorkEnd, "graceMinutes=" + GraceMinutes, "askReasonMinutes=" + AskReasonMinutes,
            "workDays=" + WorkDays, "currency=" + Currency,
            "focusBlockCategories=" + string.Join(";", FocusBlockCategories), "focusBlockExtra=" + string.Join(";", FocusBlockExtra),
            "weeklyPdf=" + (WeeklyPdf ? 1 : 0), "reportFolder=" + ReportFolder, "lastWeeklyReport=" + LastWeeklyReport,
            "theme=" + Theme, "language=" + Language, "worldClocks=" + string.Join(";", WorldClocks),
            "hotkey=" + (Hotkey ? 1 : 0), "trayHours=" + (TrayHours ? 1 : 0), "widgetOn=" + (WidgetOn ? 1 : 0), "checkUpdates=" + (CheckUpdates ? 1 : 0),
            "widgetX=" + WidgetX, "widgetY=" + WidgetY,
            "keepDays=" + KeepDays, "pinHash=" + PinHash, "extraFolders=" + string.Join(";", ExtraFolders), "mirrorFolder=" + MirrorFolder,
            "firstRunDone=" + (FirstRunDone ? 1 : 0),
        };
        Store.AtomicWrite(FilePath, string.Join("\n", kv) + "\n");
        Store.AtomicWrite(LimitsPath, string.Join("", Limits.Select(p => p.Key + "\t" + p.Value + "\n")));
        Store.AtomicWrite(CatPath, string.Join("", CategoryOverrides.Select(p => p.Key + "\t" + p.Value + "\n")));
        Projects.Save();
    }

    public static string ReportDir
    {
        get
        {
            string d = ReportFolder.Length > 0 ? ReportFolder : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Screen Time Reports");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    public static bool IsWorkDay(DateTime d) { return WorkDays[((int)d.DayOfWeek + 6) % 7] == '1'; }

    /// install.ps1 records the user's start-up choice here, so the first run doesn't override it.
    public static bool InstallerChoseAutostart
    {
        get
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(@"Software\ScreenTimeTracker")) return k != null && k.GetValue("AutostartChosen") != null; }
            catch { return false; }
        }
    }

    public static bool StartWithWindows
    {
        get
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue(RunName) != null; }
            catch { return false; }
        }
        set
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) k.SetValue(RunName, Program.RunCommand ?? "\"" + System.Windows.Forms.Application.ExecutablePath + "\" --minimized");
                    else k.DeleteValue(RunName, false);
                }
            }
            catch { }
        }
    }

    /// Keeps the autostart entry pointing at this copy if the user moved it.
    public static void RefreshStartupPath()
    {
        if (StartWithWindows) StartWithWindows = true;
    }
}

static class Util
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Fmt(double seconds)
    {
        if (seconds < 60) return seconds < 1 ? "0m" : "<1m";
        int m = (int)(seconds / 60);
        if (m < 60) return m + "m";
        int h = m / 60; m %= 60;
        return m == 0 ? h + "h" : h + "h " + m + "m";
    }

    /// Seconds since midnight to "9:03 AM" (or 24-hour "09:03:12" for CSV).
    public static string Time(double secOfDay, bool csv = false)
    {
        if (secOfDay < 0) return csv ? "" : "—";
        var t = DateTime.MinValue.AddSeconds(Math.Min(secOfDay, 86399.9));
        return t.ToString(csv ? "HH:mm:ss" : "h:mm tt", Inv);
    }

    public static string TitleFromExe(string exe)
    {
        string s = exe;
        if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
        if (s.Length == 0) return exe;
        return char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
