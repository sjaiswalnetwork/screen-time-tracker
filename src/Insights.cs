using System;
using System.Collections.Generic;
using System.Linq;

// What a day "means": deep-work blocks, app switching, a focus score, a plain-words summary, badges and unusual-use alerts.
class DayInsights
{
    public double Total, Productive, DeepWork, Meetings;
    public List<KeyValuePair<double, double>> DeepBlocks = new List<KeyValuePair<double, double>>();
    public int Switches;
    public double SwitchesPerHour;
    public int FocusScore;
    public List<string> Summary = new List<string>();
    public List<string> Badges = new List<string>();
    public List<string> Alerts = new List<string>();
}

static class Insights
{
    public const double DeepMin = 25 * 60;

    public static string CategoryOf(string appKey, ItemUsage item)
    {
        if (Browser.Is(appKey) && !Settings.CategoryOverrides.ContainsKey("app:" + appKey))
            return item != null && item.Site != null ? Categories.ForSite(item.Site) : "Browsing";
        return Categories.ForApp(appKey);
    }

    public static bool IsProductive(Segment s) { return CategoryOf(s.Key, s.Item) == "Productive"; }

    public static DayInsights ForDay(DayData d, Func<DateTime, DayData> getDay)
    {
        var r = new DayInsights();
        r.Total = d.Total;
        r.Meetings = d.MeetingSeconds;
        var segs = d.Segments.OrderBy(s => s.Start).ToList();
        foreach (var s in segs) if (IsProductive(s)) r.Productive += s.End - s.Start;

        // deep work: productive stretches of 25+ minutes; short detours (under 2 minutes in total) don't break them
        double runStart = -1, runEnd = -1, runProd = 0, detour = 0;
        foreach (var s in segs)
        {
            double len = s.End - s.Start;
            if (IsProductive(s))
            {
                if (runStart >= 0 && s.Start - runEnd <= 120) { runEnd = s.End; runProd += len; }
                else { CloseRun(r, runStart, runEnd, runProd); runStart = s.Start; runEnd = s.End; runProd = len; detour = 0; }
            }
            else if (runStart >= 0)
            {
                detour += len;
                if (detour > 120) { CloseRun(r, runStart, runEnd, runProd); runStart = -1; detour = 0; }
            }
        }
        CloseRun(r, runStart, runEnd, runProd);

        for (int i = 1; i < segs.Count; i++) if (segs[i].Key != segs[i - 1].Key) r.Switches++;
        double hours = r.Total / 3600;
        r.SwitchesPerHour = hours > 0.1 ? r.Switches / hours : 0;
        if (r.Total >= 600)
        {
            double deepShare = Math.Min(1, r.DeepWork / r.Total * 1.3), prodShare = r.Productive / r.Total;
            double calm = 1 - Math.Min(1, r.SwitchesPerHour / 45);
            r.FocusScore = (int)Math.Round(100 * (0.5 * deepShare + 0.25 * calm + 0.25 * prodShare));
            r.FocusScore = Math.Max(0, Math.Min(100, r.FocusScore));
        }

        Summarise(d, segs, r);
        Badge(d, r);
        if (getDay != null) Unusual(d, getDay, r);
        return r;
    }

    static void CloseRun(DayInsights r, double start, double end, double prod)
    {
        if (start < 0 || prod < DeepMin) return;
        r.DeepBlocks.Add(new KeyValuePair<double, double>(start, end));
        r.DeepWork += prod;
    }

    static void Summarise(DayData d, List<Segment> segs, DayInsights r)
    {
        if (r.Total < 60) { r.Summary.Add("No screen time recorded."); return; }
        string[] names = { "Morning", "Afternoon", "Evening", "Night" };
        double[] from = { 0, 12 * 3600, 17 * 3600, 21 * 3600 }, to = { 12 * 3600, 17 * 3600, 21 * 3600, 86400 };
        for (int p = 0; p < 4; p++)
        {
            var part = segs.Where(s => s.End > from[p] && s.Start < to[p]).ToList();
            double tot = part.Sum(s => Math.Min(s.End, to[p]) - Math.Max(s.Start, from[p]));
            if (tot < 600) continue;
            var byThing = new Dictionary<string, double>();
            var byApp = new Dictionary<string, double>();
            foreach (var s in part)
            {
                double len = Math.Min(s.End, to[p]) - Math.Max(s.Start, from[p]);
                string app = Store.Info(s.Key).Name;
                string thing = s.Item != null && s.Item.Title != Browser.PrivateLabel ? s.Item.Title + (s.Item.Site != null ? " (" + s.Item.Site + ")" : " in " + app) : app;
                double v;
                byThing.TryGetValue(thing, out v); byThing[thing] = v + len;
                byApp.TryGetValue(app, out v); byApp[app] = v + len;
            }
            var topApp = byApp.OrderByDescending(k => k.Value).First();
            double first = part.Min(s => Math.Max(s.Start, from[p])), last = part.Max(s => Math.Min(s.End, to[p]));
            string line = names[p] + " (" + Util.Time(first) + " – " + Util.Time(last) + ", " + Util.Fmt(tot) + "): mostly " + topApp.Key + " (" + Util.Fmt(topApp.Value) + ")";
            // the main page / file inside that app
            var inApp = new Dictionary<string, double>();
            foreach (var s in part)
            {
                if (s.Item == null || s.Item.Title == Browser.PrivateLabel || Store.Info(s.Key).Name != topApp.Key) continue;
                string thing = s.Item.Title + (s.Item.Site != null ? " (" + s.Item.Site + ")" : "");
                double v; inApp.TryGetValue(thing, out v); inApp[thing] = v + Math.Min(s.End, to[p]) - Math.Max(s.Start, from[p]);
            }
            if (inApp.Count > 0)
            {
                var t = inApp.OrderByDescending(k => k.Value).First();
                if (t.Value >= 120) line += ", especially " + t.Key + " (" + Util.Fmt(t.Value) + ")";
            }
            var second = byApp.OrderByDescending(k => k.Value).Skip(1).FirstOrDefault();
            if (second.Key != null && second.Value >= 600) line += "; then " + second.Key + " (" + Util.Fmt(second.Value) + ")";
            r.Summary.Add(line + ".");
        }
        string tail = "Deep work " + Util.Fmt(r.DeepWork) + " in " + r.DeepBlocks.Count + (r.DeepBlocks.Count == 1 ? " block" : " blocks") +
                      ", " + Math.Round(r.SwitchesPerHour) + " app switches per hour";
        if (r.Meetings >= 60) tail += ", " + Util.Fmt(r.Meetings) + " in calls/meetings";
        r.Summary.Add(tail + ".");
    }

    static void Badge(DayData d, DayInsights r)
    {
        if (r.Total < 60) return;
        bool weekend = d.Date.DayOfWeek == DayOfWeek.Saturday || d.Date.DayOfWeek == DayOfWeek.Sunday;
        if (d.FirstActive >= 0 && d.FirstActive < 8 * 3600) r.Badges.Add("Early Bird – started before 8 AM");
        if (r.DeepWork >= 4 * 3600) r.Badges.Add("Deep Diver – 4h+ of deep work");
        else if (r.DeepWork >= 2 * 3600) r.Badges.Add("Focus Master – 2h+ of deep work");
        if (r.Total >= 2 * 3600 && r.Productive / r.Total >= 0.6) r.Badges.Add("Balanced – 60%+ productive");
        if (r.Total >= 2 * 3600 && r.SwitchesPerHour < 12) r.Badges.Add("Steady – few app switches");
        if (weekend && r.Total < 2 * 3600) r.Badges.Add("Digital Detox – light weekend day");
        if (Settings.DailyGoalMinutes > 0 && r.Total <= Settings.DailyGoalMinutes * 60) r.Badges.Add("On Target – within your daily goal");
        if (r.Total >= 10 * 3600) r.Badges.Add("Marathon – 10h+ on screen (take care!)");
        if (d.LastActive >= 23 * 3600) r.Badges.Add("Night Owl – active after 11 PM");
    }

    // Apps and sites used far more than usual (3x the average of the previous 14 days with data).
    static void Unusual(DayData d, Func<DateTime, DayData> getDay, DayInsights r)
    {
        var appAvg = new Dictionary<string, double>();
        var siteAvg = new Dictionary<string, double>();
        int n = 0;
        for (int i = 1; i <= 14; i++)
        {
            var p = getDay(d.Date.AddDays(-i));
            if (p.Total < 600) continue;
            n++;
            foreach (var a in p.Apps.Values) { double v; appAvg.TryGetValue(a.Key, out v); appAvg[a.Key] = v + a.Seconds; }
            foreach (var kv in SiteTotals(p)) { double v; siteAvg.TryGetValue(kv.Key, out v); siteAvg[kv.Key] = v + kv.Value; }
        }
        if (n < 3) return;
        foreach (var a in d.Apps.Values)
        {
            double avg; appAvg.TryGetValue(a.Key, out avg); avg /= n;
            if (a.Seconds >= 1800 && a.Seconds >= 3 * Math.Max(avg, 300))
                r.Alerts.Add(Store.Info(a.Key).Name + ": " + Util.Fmt(a.Seconds) + " – " + (avg < 60 ? "much more than usual" : Math.Round(a.Seconds / avg, 1) + "x your usual " + Util.Fmt(avg)));
        }
        foreach (var kv in SiteTotals(d))
        {
            double avg; siteAvg.TryGetValue(kv.Key, out avg); avg /= n;
            if (kv.Value >= 1800 && kv.Value >= 3 * Math.Max(avg, 300))
                r.Alerts.Add(kv.Key + ": " + Util.Fmt(kv.Value) + " – " + (avg < 60 ? "much more than usual" : Math.Round(kv.Value / avg, 1) + "x your usual " + Util.Fmt(avg)));
        }
    }

    public static Dictionary<string, double> SiteTotals(DayData d)
    {
        var r = new Dictionary<string, double>();
        foreach (var it in d.ItemList)
        {
            if (it.Site == null || it.Seconds < 1 || !Browser.Is(it.AppKey)) continue;
            double v; r.TryGetValue(it.Site, out v); r[it.Site] = v + it.Seconds;
        }
        return r;
    }

    /// The 3-hour window with the most productive time over the last `days` days, e.g. "10 AM – 1 PM".
    public static string BestHours(DateTime end, int days, Func<DateTime, DayData> getDay, out double share)
    {
        var prod = new double[24];
        double all = 0;
        for (int i = 0; i < days; i++)
        {
            var d = getDay(end.AddDays(-i));
            foreach (var s in d.Segments)
            {
                if (!IsProductive(s)) continue;
                for (double t = s.Start; t < s.End; )
                {
                    int h = Math.Min(23, (int)(t / 3600));
                    double e = Math.Min(s.End, (h + 1) * 3600.0);
                    prod[h] += e - t; all += e - t;
                    t = e;
                }
            }
        }
        share = 0;
        if (all < 3600) return null;
        int best = 0; double bv = -1;
        for (int h = 0; h <= 21; h++) { double v = prod[h] + prod[h + 1] + prod[h + 2]; if (v > bv) { bv = v; best = h; } }
        share = bv / all;
        return HourName(best) + " – " + HourName(best + 3);
    }

    public static string HourName(int h)
    {
        h = ((h % 24) + 24) % 24;
        if (h == 0) return "12 AM";
        if (h == 12) return "12 PM";
        return h < 12 ? h + " AM" : (h - 12) + " PM";
    }

    /// A ready-to-paste "what I did" note for a day (for a standup, WhatsApp or a timesheet).
    public static string StandupNote(DayData d)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(d.Date.ToString("dddd, d MMM yyyy") + " – " + Util.Fmt(d.Total) + " on screen");
        var projects = Projects.Totals(d);
        if (projects.Count > 0)
        {
            foreach (var p in projects.OrderByDescending(k => k.Value.Seconds).Take(6))
            {
                sb.Append("• " + p.Key + " – " + Util.Fmt(p.Value.Seconds));
                var tops = p.Value.Things.OrderByDescending(k => k.Value).Take(3).Select(k => k.Key).ToList();
                if (tops.Count > 0) sb.Append(" (" + string.Join(", ", tops) + ")");
                sb.AppendLine();
            }
        }
        else
        {
            foreach (var a in d.Sorted().Take(5))
            {
                var items = d.ItemsFor(a.Key).Where(i => i.Title != Browser.PrivateLabel).Take(3).Select(i => i.Title).ToList();
                sb.AppendLine("• " + Store.Info(a.Key).Name + " – " + Util.Fmt(a.Seconds) + (items.Count > 0 ? " (" + string.Join(", ", items) + ")" : ""));
            }
        }
        foreach (var m in d.Manual.Where(m => m.IsWork))
            sb.AppendLine("• " + m.Label + (m.Client.Length > 0 ? " – " + m.Client : "") + " – " + Util.Fmt(m.End - m.Start) + " (" + Util.Time(m.Start) + " – " + Util.Time(m.End) + ")");
        var ins = ForDay(d, null);
        sb.AppendLine("Deep work " + Util.Fmt(ins.DeepWork) + (d.MeetingSeconds >= 60 ? " · Calls/meetings " + Util.Fmt(d.MeetingSeconds) : "") + " · Focus score " + ins.FocusScore + "/100");
        return sb.ToString();
    }
}
