using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

// A client or project. Time is assigned to it when a window title / website contains one of its keywords,
// or when one of its apps is in front. Offline entries (meetings, visits) are assigned by the client you pick.
class Project
{
    public string Name = "";
    public double Rate; // per hour, for the invoice helper
    public List<string> Keywords = new List<string>();
    public List<string> Apps = new List<string>();
}

class ProjectTotal
{
    public double Seconds, Offline;
    public Dictionary<string, double> Things = new Dictionary<string, double>();
}

static class Projects
{
    public static List<Project> All = new List<Project>();
    static string FilePath { get { return Path.Combine(Store.Root, "projects.txt"); } }

    public static void Load()
    {
        All.Clear();
        if (!File.Exists(FilePath)) return;
        foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
        {
            var f = line.Split('\t');
            if (f.Length < 4 || f[0].Trim().Length == 0) continue;
            var p = new Project();
            p.Name = f[0].Trim();
            double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out p.Rate);
            p.Keywords = f[2].Split(';').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            p.Apps = f[3].Split(';').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            All.Add(p);
        }
    }

    public static void Save()
    {
        if (Store.Root == null) return;
        Store.AtomicWrite(FilePath, string.Join("", All.Select(p => p.Name.Replace('\t', ' ') + "\t" + p.Rate.ToString(CultureInfo.InvariantCulture) + "\t" +
            string.Join(";", p.Keywords) + "\t" + string.Join(";", p.Apps) + "\n")));
    }

    public static string Match(string appKey, ItemUsage item)
    {
        if (All.Count == 0) return null;
        string title = item != null ? item.Title : null, site = item != null ? item.Site : null;
        foreach (var p in All)
            foreach (var k in p.Keywords)
                if ((title != null && title.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (site != null && site.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)) return p.Name;
        string appName = Store.Info(appKey).Name;
        foreach (var p in All)
            foreach (var a in p.Apps)
                if (string.Equals(a, appKey, StringComparison.OrdinalIgnoreCase) || string.Equals(a, appName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a + ".exe", appKey, StringComparison.OrdinalIgnoreCase)) return p.Name;
        return null;
    }

    public static Project Find(string name) { return All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)); }

    /// Seconds per project for a day: matched screen time plus offline work entries tagged with that client.
    public static Dictionary<string, ProjectTotal> Totals(DayData d)
    {
        var r = new Dictionary<string, ProjectTotal>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in d.Segments)
        {
            string name = Match(s.Key, s.Item);
            if (name == null) continue;
            ProjectTotal t;
            if (!r.TryGetValue(name, out t)) { t = new ProjectTotal(); r[name] = t; }
            double len = s.End - s.Start;
            t.Seconds += len;
            string thing = s.Item != null && s.Item.Title != Browser.PrivateLabel ? s.Item.Title : Store.Info(s.Key).Name;
            double v; t.Things.TryGetValue(thing, out v); t.Things[thing] = v + len;
        }
        foreach (var m in d.Manual)
        {
            if (!m.IsWork || m.Client.Length == 0) continue;
            ProjectTotal t;
            if (!r.TryGetValue(m.Client, out t)) { t = new ProjectTotal(); r[m.Client] = t; }
            double len = m.End - m.Start;
            t.Seconds += len; t.Offline += len;
            string thing = m.Label + " (offline)";
            double v; t.Things.TryGetValue(thing, out v); t.Things[thing] = v + len;
        }
        return r;
    }

    /// Timesheet CSV for a date range: one row per day per project, plus amounts.
    public static void ExportTimesheet(string path, DateTime from, DateTime to, Func<DateTime, DayData> getDay)
    {
        var sb = new StringBuilder("date,project_or_client,hours,offline_hours,rate,amount,main_work\r\n");
        var sum = new Dictionary<string, double>();
        for (var day = from.Date; day <= to.Date; day = day.AddDays(1))
        {
            var d = getDay(day);
            foreach (var kv in Totals(d).OrderByDescending(k => k.Value.Seconds))
            {
                var p = Find(kv.Key);
                double h = kv.Value.Seconds / 3600, rate = p != null ? p.Rate : 0;
                string main = string.Join(" | ", kv.Value.Things.OrderByDescending(k => k.Value).Take(3).Select(k => k.Key));
                sb.Append(day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(',').Append(Csv(kv.Key)).Append(',')
                  .Append(h.ToString("0.00", CultureInfo.InvariantCulture)).Append(',').Append((kv.Value.Offline / 3600).ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                  .Append(rate.ToString("0.##", CultureInfo.InvariantCulture)).Append(',').Append((h * rate).ToString("0.00", CultureInfo.InvariantCulture)).Append(',')
                  .Append(Csv(main)).Append("\r\n");
                double v; sum.TryGetValue(kv.Key, out v); sum[kv.Key] = v + h;
            }
        }
        sb.Append("\r\nTOTAL,,,,,,\r\n");
        foreach (var kv in sum.OrderByDescending(k => k.Value))
        {
            var p = Find(kv.Key);
            double rate = p != null ? p.Rate : 0;
            sb.Append("total,").Append(Csv(kv.Key)).Append(',').Append(kv.Value.ToString("0.00", CultureInfo.InvariantCulture)).Append(",,")
              .Append(rate.ToString("0.##", CultureInfo.InvariantCulture)).Append(',').Append((kv.Value * rate).ToString("0.00", CultureInfo.InvariantCulture)).Append(",\r\n");
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }

    static string Csv(string s) { return s.IndexOfAny(new[] { ',', '"' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s; }
}

// Arrival / departure against your work hours. Uses first and last ACTIVITY (not just when the PC was switched on),
// so turning the PC on at 7 and starting work at 9 counts as arriving at 9.
class AttendanceDay
{
    public DateTime Date;
    public bool WorkDay, Present, StillOn;
    public double Arrived = -1, Left = -1, Worked, Offline, LateBy, EarlyBy, Overtime;
    public string Status = "";
}

static class Attendance
{
    public static AttendanceDay For(DayData d, Tracker tr)
    {
        var a = new AttendanceDay();
        a.Date = d.Date;
        a.WorkDay = Settings.IsWorkDay(d.Date);
        bool isToday = d.Date == Clock.Today;
        a.Offline = d.Manual.Where(m => m.IsWork).Sum(m => m.End - m.Start);
        a.Worked = d.Total + a.Offline;
        double first = d.FirstActive, last = d.LastActive;
        foreach (var m in d.Manual.Where(m => m.IsWork))
        {
            if (first < 0 || m.Start < first) first = m.Start;
            if (m.End > last) last = m.End;
        }
        a.Present = first >= 0 && a.Worked >= 15 * 60;
        if (!a.Present)
        {
            a.Status = !a.WorkDay ? "Day off" : isToday ? "Not started" : d.Date > Clock.Today ? "" : "Absent";
            return a;
        }
        a.Arrived = first;
        a.StillOn = isToday && tr != null && !tr.Locked && !tr.Idle && (Clock.Now - tr.LastActiveAt).TotalMinutes < 10;
        a.Left = last;
        double start = Settings.WorkStart * 60, end = Settings.WorkEnd * 60;
        if (a.WorkDay && first > start + Settings.GraceMinutes * 60) a.LateBy = first - start;
        if (!a.StillOn && a.WorkDay && last < end - Settings.GraceMinutes * 60 && !(isToday && Clock.Now.TimeOfDay.TotalSeconds < end)) a.EarlyBy = end - last;
        if (last > end) a.Overtime = last - Math.Max(end, first);
        if (!a.WorkDay) a.Overtime = a.Worked;
        var parts = new List<string>();
        if (!a.WorkDay) parts.Add("Worked on day off");
        if (a.LateBy > 0) parts.Add("Late " + Util.Fmt(a.LateBy));
        if (a.EarlyBy > 0) parts.Add("Left early " + Util.Fmt(a.EarlyBy));
        if (a.Overtime >= 15 * 60 && a.WorkDay) parts.Add("Overtime " + Util.Fmt(a.Overtime));
        a.Status = parts.Count == 0 ? (a.StillOn ? "Working" : "On time") : string.Join(" · ", parts);
        return a;
    }

    public static string Clock12(int minutes) { return Util.Time(minutes * 60); }
}

// Detects an ongoing call/meeting: Windows records which app is currently using the microphone.
static class Mic
{
    const string Root = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";

    /// Name of the app using the microphone right now, or null.
    public static string InUseBy()
    {
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(Root))
            {
                if (k == null) return null;
                string r = Scan(k);
                if (r != null) return r;
                using (var np = k.OpenSubKey("NonPackaged")) if (np != null) return Scan(np);
            }
        }
        catch { }
        return null;
    }

    static string Scan(RegistryKey k)
    {
        foreach (var name in k.GetSubKeyNames())
        {
            if (name == "NonPackaged") continue;
            using (var s = k.OpenSubKey(name))
            {
                if (s == null) continue;
                object start = s.GetValue("LastUsedTimeStart"), stop = s.GetValue("LastUsedTimeStop");
                if (start is long && stop is long && (long)start > 0 && (long)stop == 0) return Friendly(name);
            }
        }
        return null;
    }

    static string Friendly(string regName)
    {
        string n = regName;
        int hash = n.LastIndexOf('#');
        if (hash >= 0) n = n.Substring(hash + 1);               // C:#Program Files#Zoom#bin#Zoom.exe -> Zoom.exe
        int us = n.IndexOf('_');
        if (us > 0 && n.Contains(".")) n = n.Substring(0, us);  // MSTeams_8wekyb3d8bbwe -> MSTeams
        if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = Store.Info(n.ToLowerInvariant()).Name;
        n = n.Replace("Microsoft.", "").Replace("MSTeams", "Microsoft Teams");
        return n;
    }
}

// Merging data from other PCs (e.g. a home and an office laptop sharing a OneDrive folder) and deleting old history.
static class DataTools
{
    public static void MergeInto(DayData t, DayData s, string tag)
    {
        t.Unlocks += s.Unlocks;
        t.MeetingSeconds += s.MeetingSeconds;
        foreach (var a in s.Apps.Values)
        {
            var u = t.Get(a.Key);
            u.Seconds += a.Seconds; u.Opens += a.Opens;
            for (int i = 0; i < 24; i++) u.Hours[i] += a.Hours[i];
        }
        var map = new Dictionary<ItemUsage, ItemUsage>();
        foreach (var it in s.ItemList)
        {
            var n = t.GetItem(it.AppKey, it.Title);
            n.Seconds += it.Seconds; n.Opens += it.Opens;
            if (n.Site == null) n.Site = it.Site;
            map[it] = n;
        }
        foreach (var g in s.Segments)
        {
            var c = new Segment(); c.Start = g.Start; c.End = g.End; c.Key = g.Key;
            if (g.Item != null) map.TryGetValue(g.Item, out c.Item);
            t.Segments.Add(c);
        }
        t.Segments.Sort((a, b) => a.Start.CompareTo(b.Start));
        foreach (var e in s.Events) t.AddEvent(e.At, e.Kind, e.Text + (tag != null ? " (" + tag + ")" : ""));
        foreach (var m in s.Manual) t.Manual.Add(m);
    }

    /// Our day merged with the same day from every extra folder (other PCs).
    public static DayData WithOtherPcs(DayData own)
    {
        if (Settings.ExtraFolders.Count == 0) return own;
        var r = new DayData();
        r.Date = own.Date;
        MergeInto(r, own, null);
        bool any = false;
        foreach (var folder in Settings.ExtraFolders)
        {
            string f = Path.Combine(folder, "days", own.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".tsv");
            if (!File.Exists(f)) f = Path.Combine(folder, own.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".tsv");
            if (!File.Exists(f)) continue;
            var other = Store.LoadFile(f, own.Date);
            MergeInto(r, other, Path.GetFileName(folder.TrimEnd('\\', '/')));
            any = true;
        }
        if (!any) return own;
        r.Merged = true;
        return r;
    }

    /// Deletes day files older than Settings.KeepDays (0 = keep forever).
    public static int Prune()
    {
        if (Settings.KeepDays <= 0) return 0;
        int n = 0;
        var cutoff = Clock.Today.AddDays(-Settings.KeepDays);
        foreach (var d in Store.AllDays())
            if (d < cutoff)
            {
                try { File.Delete(Path.Combine(Store.DaysDir, d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".tsv")); n++; } catch { }
            }
        return n;
    }

    /// Copies all data into a zip-free backup folder (plain files) under `dest`.
    public static string Backup(string dest)
    {
        string target = Path.Combine(dest, "Screen Time backup " + Clock.Now.ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(Path.Combine(target, "days"));
        foreach (var f in Directory.GetFiles(Store.Root)) File.Copy(f, Path.Combine(target, Path.GetFileName(f)), true);
        foreach (var f in Directory.GetFiles(Store.DaysDir)) File.Copy(f, Path.Combine(target, "days", Path.GetFileName(f)), true);
        return target;
    }

    public static string HashPin(string pin)
    {
        using (var sha = System.Security.Cryptography.SHA256.Create())
            return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes("screen-time:" + pin)));
    }
}
