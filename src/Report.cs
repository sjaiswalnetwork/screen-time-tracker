using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

// PDF reports for one day or a date range (week / month / custom). Pages are laid out as a list of blocks,
// then written through Windows' built-in "Microsoft Print to PDF" (real, selectable text). If that printer is
// missing, the same pages are rendered to images and wrapped in a small hand-written PDF instead.
class PdfReport
{
    const float PW = 595.3f, PH = 841.9f, M = 36f, CW = PW - 2 * M;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture, Cur = CultureInfo.CurrentCulture;

    class Block { public float H; public bool KeepWithNext; public Action<Graphics, float> Draw; }

    readonly List<Block> blocks = new List<Block>();
    List<List<KeyValuePair<Block, float>>> pages;
    readonly Theme T = Theme.Light;
    readonly Func<DateTime, DayData> getDay;
    readonly Tracker tracker;
    readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
    readonly Dictionary<string, Bitmap> icons = new Dictionary<string, Bitmap>();
    readonly StringFormat sf, wrap;
    readonly Graphics mg; // for measuring
    readonly Bitmap mbmp;
    string runTitle = "";
    Dictionary<string, Color> appColors = new Dictionary<string, Color>();

    PdfReport(Tracker tr, Func<DateTime, DayData> getDay)
    {
        tracker = tr;
        var cache = new Dictionary<DateTime, DayData>();
        this.getDay = d => { DayData r; if (!cache.TryGetValue(d.Date, out r)) { r = getDay(d.Date); cache[d.Date] = r; } return r; };
        sf = (StringFormat)StringFormat.GenericTypographic.Clone();
        sf.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;
        sf.Trimming = StringTrimming.EllipsisCharacter;
        wrap = (StringFormat)StringFormat.GenericTypographic.Clone();
        wrap.Trimming = StringTrimming.Word;
        mbmp = new Bitmap(10, 10);
        mg = Graphics.FromImage(mbmp);
        mg.PageUnit = GraphicsUnit.Point;
    }

    // ================= public entry points =================

    public static string DayReport(DateTime date, Tracker tr, Func<DateTime, DayData> getDay, string path)
    {
        var r = new PdfReport(tr, getDay);
        r.BuildDay(date.Date);
        return r.Save(path);
    }

    public static string RangeReport(DateTime from, DateTime to, string kind, Tracker tr, Func<DateTime, DayData> getDay, string path)
    {
        var r = new PdfReport(tr, getDay);
        r.BuildRange(from.Date, to.Date, kind);
        return r.Save(path);
    }

    public static string DefaultName(DateTime from, DateTime to, string kind)
    {
        if (kind == "day") return "Screen Time " + from.ToString("yyyy-MM-dd", Inv) + ".pdf";
        return "Screen Time " + kind + " " + from.ToString("yyyy-MM-dd", Inv) + " to " + to.ToString("yyyy-MM-dd", Inv) + ".pdf";
    }

    // ================= day report =================

    void BuildDay(DateTime date)
    {
        var d = getDay(date);
        bool isToday = date == Clock.Today;
        var evs = Sessions.Merged(d);
        var si = Sessions.Compute(d, evs, isToday, tracker);
        var ins = Insights.ForDay(d, getDay);
        var att = Attendance.For(d, tracker);
        var apps = d.Sorted();
        SetColors(apps.Select(a => a.Key));
        runTitle = "Daily report · " + date.ToString("ddd, d MMM yyyy", Cur);
        double nowSec = (Clock.Now - Clock.Today).TotalSeconds, on = si.OnFor(nowSec);

        TitleBlock("Daily Screen Time Report", date.ToString("dddd, d MMMM yyyy", Cur) + (isToday ? "  ·  report so far (until " + Clock.Now.ToString("h:mm tt", Inv) + ")" : ""));
        Tiles(new[]
        {
            Tile("SCREEN TIME", Util.Fmt(d.Total), d.Merged ? "all PCs" : ""),
            Tile("LOG IN", Util.Time(si.LogIn), si.InVia),
            Tile("LOG OUT", si.StillOn ? "Still on" : Util.Time(si.LogOut), si.StillOn ? "at time of report" : si.OutVia),
            Tile("PC ON FOR", on > 0 ? Util.Fmt(on) : "—", ""),
            Tile("FIRST ACTIVITY", Util.Time(si.First), ""),
            Tile("LAST ACTIVITY", Util.Time(si.Last), ""),
            Tile("AWAY / LOCKED", on > d.Total ? Util.Fmt(on - d.Total) : "—", "while the PC was on"),
            Tile("FOCUS SCORE", d.Total >= 600 ? ins.FocusScore + "/100" : "—", "deep work, switching, productive share"),
        });
        InsightsBlock(d, ins, att);

        if (d.Segments.Count > 0) { Section("Timeline", "coloured by app; gaps are idle, locked or off"); TimelineBlock(d, isToday); }
        if (d.Total >= 60) { Section("Hourly activity", null); HourlyBlock(d); }
        CategoryBlock(d.Sorted(), k => d.ItemsFor(k));
        ProjectsBlock(new[] { d });
        ManualBlock(d);
        AppsTable(apps, d.Total, 0, k => d.ItemsFor(k));
        SitesTable(d.ItemList);
        PagesTable(d.ItemList, 40);
        ActivityLog(d, evs);
    }

    // ================= range report (week / month / custom) =================

    void BuildRange(DateTime from, DateTime to, string kind)
    {
        var days = new List<DayData>();
        for (var x = from; x <= to; x = x.AddDays(1)) days.Add(getDay(x));
        var agg = new DayData();
        agg.Date = from;
        foreach (var d in days) DataTools.MergeInto(agg, d, null);
        var apps = agg.Sorted();
        SetColors(apps.Select(a => a.Key));
        string range = from.ToString("d MMM", Cur) + " – " + to.ToString("d MMM yyyy", Cur);
        string title = kind == "week" ? "Weekly Screen Time Report" : kind == "month" ? "Monthly Screen Time Report" : "Screen Time Report";
        runTitle = (kind == "week" ? "Weekly" : kind == "month" ? "Monthly" : "Range") + " report · " + range;
        TitleBlock(title, range);

        var active = days.Where(d => d.Total >= 60).ToList();
        double total = days.Sum(d => d.Total), avg = active.Count > 0 ? total / active.Count : 0;
        var insights = days.Select(d => Insights.ForDay(d, null)).ToList();
        double deep = insights.Sum(i => i.DeepWork), prod = insights.Sum(i => i.Productive);
        var busiest = days.OrderByDescending(d => d.Total).FirstOrDefault();
        var withScore = insights.Where(i => i.Total >= 600).ToList();
        Tiles(new[]
        {
            Tile("TOTAL SCREEN TIME", Util.Fmt(total), active.Count + " active " + (active.Count == 1 ? "day" : "days")),
            Tile("DAILY AVERAGE", Util.Fmt(avg), "on active days"),
            Tile("BUSIEST DAY", busiest != null && busiest.Total >= 60 ? busiest.Date.ToString("ddd d MMM", Cur) : "—", busiest != null && busiest.Total >= 60 ? Util.Fmt(busiest.Total) : ""),
            Tile("PRODUCTIVE SHARE", total >= 60 ? Math.Round(prod / total * 100) + "%" : "—", Util.Fmt(prod)),
            Tile("DEEP WORK", Util.Fmt(deep), active.Count > 0 ? "avg " + Util.Fmt(deep / active.Count) + " a day" : ""),
            Tile("AVG FOCUS SCORE", withScore.Count > 0 ? Math.Round(withScore.Average(i => i.FocusScore)) + "/100" : "—", ""),
            Tile("CALLS / MEETINGS", Util.Fmt(days.Sum(d => d.MeetingSeconds)), ""),
            Tile("OFFLINE WORK", Util.Fmt(days.Sum(d => d.Manual.Where(m => m.IsWork).Sum(m => m.End - m.Start))), "added by you"),
        });

        if (days.Count <= 31 && total >= 60) { Section("Daily screen time", null); DailyBars(days); }

        Section("Day by day", "log in / log out are the PC session; arrival / departure use real activity");
        string[] heads = { "Day", "Log in", "Log out", "Screen time", "Productive", "Deep work", "Focus", "Attendance" };
        float[] cols = { 0, 78, 132, 186, 248, 306, 362, 404 };
        TableHeader(heads, cols);
        for (int i = 0; i < days.Count; i++)
        {
            var d = days[i]; var ins = insights[i];
            if (d.Date > Clock.Today) continue;
            var si = Sessions.Compute(d, Sessions.Merged(d), d.Date == Clock.Today, tracker);
            var att = Attendance.For(d, tracker);
            bool any = d.Total >= 60 || si.LogIn >= 0;
            string[] cells =
            {
                d.Date.ToString("ddd d MMM", Cur), any ? Util.Time(si.LogIn) : "—", any ? (si.StillOn ? "Still on" : Util.Time(si.LogOut)) : "—",
                Util.Fmt(d.Total), d.Total >= 60 ? Math.Round(ins.Productive / d.Total * 100) + "%" : "—", Util.Fmt(ins.DeepWork),
                d.Total >= 600 ? ins.FocusScore.ToString() : "—", att.Status
            };
            TableRow(cells, cols, i % 2 == 1, att.LateBy > 0 || att.Status == "Absent" ? 7 : -1);
        }

        CategoryBlock(apps, k => agg.ItemsFor(k));
        ProjectsBlock(days);
        AppsTable(apps, total, Math.Max(1, active.Count), k => agg.ItemsFor(k));
        SitesTable(agg.ItemList);
        PagesTable(agg.ItemList, 30);

        var badges = new Dictionary<string, int>();
        foreach (var ins in insights) foreach (var b in ins.Badges) { int n; badges.TryGetValue(b, out n); badges[b] = n + 1; }
        if (badges.Count > 0)
        {
            Section("Badges earned", null);
            foreach (var kv in badges.OrderByDescending(k => k.Value))
            {
                var b = kv;
                Add(15, (g, y) =>
                {
                    Txt(g, "★", 9, true, T.Warn, M, y + 1);
                    Txt(g, b.Key, 9, false, T.Text, M + 14, y + 1);
                    TxtR(g, "× " + b.Value, 9, true, T.Muted, M + CW, y + 1);
                });
            }
        }
    }

    // ================= blocks =================

    void Add(float h, Action<Graphics, float> draw, bool keep = false)
    {
        var b = new Block(); b.H = h; b.Draw = draw; b.KeepWithNext = keep;
        blocks.Add(b);
    }

    void TitleBlock(string title, string sub)
    {
        string who = Program.DemoMode ? "Demo user · DEMO-PC" : Environment.UserName + " · " + Environment.MachineName;
        string gen = "Generated " + Clock.Now.ToString("d MMM yyyy, h:mm tt", Inv) + (Clock.Synced ? " (internet time)" : "");
        var tz = TimeZoneInfo.Local;
        string zone = "Times in " + (tz.IsDaylightSavingTime(Clock.Now) ? tz.DaylightName : tz.StandardName);
        Add(70, (g, y) =>
        {
            Brand.DrawLogo(g, M, y + 2, 34);
            Txt(g, title, 18, true, T.Text, M + 44, y);
            Txt(g, sub, 10.5f, false, T.Muted, M + 44, y + 26);
            TxtR(g, who, 8, false, T.Muted, M + CW, y + 2);
            TxtR(g, gen, 8, false, T.Muted, M + CW, y + 14);
            TxtR(g, zone, 8, false, T.Muted, M + CW, y + 26);
            using (var p = new Pen(T.Border, 0.8f)) g.DrawLine(p, M, y + 56, M + CW, y + 56);
        });
    }

    class TileData { public string Label, Value, Note; }
    static TileData Tile(string l, string v, string n) { var t = new TileData(); t.Label = l; t.Value = v; t.Note = n ?? ""; return t; }

    void Tiles(TileData[] tiles)
    {
        int rows = (tiles.Length + 3) / 4;
        Add(rows * 56 + 6, (g, y) =>
        {
            float gap = 7, tw = (CW - gap * 3) / 4;
            for (int i = 0; i < tiles.Length; i++)
            {
                float x = M + (i % 4) * (tw + gap), ty = y + (i / 4) * 56;
                RoundFill(g, new RectangleF(x, ty, tw, 50), 6, Color.FromArgb(246, 248, 252));
                RoundStroke(g, new RectangleF(x, ty, tw, 50), 6, T.Border);
                Txt(g, tiles[i].Label, 6.8f, true, T.Muted, x + 8, ty + 6);
                Cell(g, tiles[i].Value, 14, true, T.Text, x + 8, ty + 16, tw - 14);
                Cell(g, tiles[i].Note, 6.8f, false, T.Muted, x + 8, ty + 37, tw - 14);
            }
        });
    }

    void InsightsBlock(DayData d, DayInsights ins, AttendanceDay att)
    {
        var lines = new List<string>(ins.Summary);
        float w = CW - 24;
        float h = 30;
        foreach (var l in lines) h += Measure(l, 9, false, w) + 3;
        h += 32; // stats row
        string attLine = "Work hours " + Attendance.Clock12(Settings.WorkStart) + " – " + Attendance.Clock12(Settings.WorkEnd) + ": " +
                         (att.Present ? "arrived " + Util.Time(att.Arrived) + ", " + (att.StillOn ? "still working" : "left " + Util.Time(att.Left)) + " · " : "") + att.Status;
        h += 16;
        string badges = ins.Badges.Count > 0 ? "Badges: " + string.Join("  ·  ", ins.Badges) : null;
        if (badges != null) h += Measure(badges, 8.5f, false, w) + 6;
        foreach (var a in ins.Alerts) h += Measure("⚠ " + a, 8.5f, false, w) + 3;
        h += 10;
        Add(h + 10, (g, y) =>
        {
            RoundFill(g, new RectangleF(M, y, CW, h), 8, Color.FromArgb(243, 246, 255));
            Txt(g, "Summary of the day", 11, true, T.Text, M + 12, y + 10);
            float yy = y + 30;
            foreach (var l in lines) yy += Wrap(g, l, 9, false, T.Text, M + 12, yy, w) + 3;
            yy += 4;
            string[] lab = { "DEEP WORK", "PRODUCTIVE", "CALLS / MEETINGS", "APP SWITCHES / HR", "OFFLINE WORK" };
            string[] val =
            {
                Util.Fmt(ins.DeepWork) + " (" + ins.DeepBlocks.Count + ")", d.Total >= 60 ? Math.Round(ins.Productive / d.Total * 100) + "%" : "—",
                Util.Fmt(d.MeetingSeconds), Math.Round(ins.SwitchesPerHour).ToString(), Util.Fmt(att.Offline)
            };
            float cw5 = w / 5;
            for (int i = 0; i < 5; i++)
            {
                Txt(g, lab[i], 6.8f, true, T.Muted, M + 12 + i * cw5, yy);
                Txt(g, val[i], 11, true, T.Text, M + 12 + i * cw5, yy + 10);
            }
            yy += 32;
            Txt(g, attLine, 8.5f, false, att.LateBy > 0 || att.EarlyBy > 0 ? T.Warn : T.Muted, M + 12, yy);
            yy += 16;
            if (badges != null) yy += Wrap(g, badges, 8.5f, false, T.Good, M + 12, yy, w) + 6;
            foreach (var a in ins.Alerts) yy += Wrap(g, "⚠ " + a, 8.5f, false, T.Danger, M + 12, yy, w) + 3;
        });
    }

    void Section(string title, string note)
    {
        Add(28, (g, y) =>
        {
            Txt(g, title, 12, true, T.Text, M, y + 8);
            if (note != null) TxtR(g, note, 7.5f, false, T.Muted, M + CW, y + 12);
            using (var p = new Pen(T.Border, 0.6f)) g.DrawLine(p, M, y + 25, M + CW, y + 25);
        }, true);
    }

    void TimelineBlock(DayData d, bool isToday)
    {
        Add(70, (g, y) =>
        {
            double first = d.FirstActive, last = d.LastActive;
            if (isToday) last = Math.Max(last, (Clock.Now - Clock.Today).TotalSeconds);
            int sh = (int)Math.Floor(first / 3600), eh = (int)Math.Ceiling(last / 3600);
            while (eh - sh < 4) { if (eh < 24) eh++; else sh--; }
            double t0 = sh * 3600.0, span = (eh - sh) * 3600.0;
            float top = y + 6, bh = 20;
            RoundFill(g, new RectangleF(M, top, CW, bh), 4, T.Track);
            foreach (var s in d.Segments)
            {
                float x0 = M + (float)((s.Start - t0) / span) * CW, x1 = M + (float)((s.End - t0) / span) * CW;
                using (var b = new SolidBrush(ColorOf(s.Key))) g.FillRectangle(b, x0, top, Math.Max(0.4f, x1 - x0), bh);
            }
            foreach (var m in d.Manual)
            {
                float x0 = M + (float)((m.Start - t0) / span) * CW, x1 = M + (float)((m.End - t0) / span) * CW;
                using (var b = new HatchBrush(HatchStyle.BackwardDiagonal, m.IsWork ? T.Accent : T.Muted, Color.White)) g.FillRectangle(b, x0, top, Math.Max(0.4f, x1 - x0), bh);
            }
            int step = eh - sh <= 8 ? 1 : eh - sh <= 14 ? 2 : 3;
            for (int h = sh; h <= eh; h += step)
            {
                float x = M + (float)((h * 3600 - t0) / span) * CW;
                string lab = Insights.HourName(h);
                float lw = Measure1(lab, 7);
                Txt(g, lab, 7, false, T.Muted, Math.Max(M, Math.Min(M + CW - lw, x - lw / 2)), top + bh + 4);
            }
            Legend(g, M, top + bh + 20);
        });
    }

    void HourlyBlock(DayData d)
    {
        Add(132, (g, y) =>
        {
            float left = M + 26, right = M + CW, bottom = y + 104, ch = 92;
            for (int v = 0; v <= 60; v += 15)
            {
                float gy = bottom - v / 60f * ch;
                using (var p = new Pen(T.Grid, 0.5f)) g.DrawLine(p, left, gy, right, gy);
                TxtR(g, v + "m", 6.5f, false, T.Muted, left - 4, gy - 4);
            }
            float slot = (right - left) / 24, bw = slot * 0.62f;
            var top = appColors.Keys.ToList();
            for (int h = 0; h < 24; h++)
            {
                float x = left + h * slot + (slot - bw) / 2, yb = bottom;
                double rest = d.Apps.Values.Sum(a => a.Hours[h]);
                foreach (var k in top)
                {
                    AppUsage u;
                    if (!d.Apps.TryGetValue(k, out u) || u.Hours[h] <= 0) continue;
                    float sh = (float)(u.Hours[h] / 3600) * ch;
                    using (var b = new SolidBrush(appColors[k])) g.FillRectangle(b, x, yb - sh, bw, sh);
                    yb -= sh; rest -= u.Hours[h];
                }
                if (rest > 1) { float sh = (float)(rest / 3600) * ch; using (var b = new SolidBrush(T.Other)) g.FillRectangle(b, x, yb - sh, bw, sh); }
                if (h % 3 == 0) Txt(g, Insights.HourName(h), 6.5f, false, T.Muted, left + h * slot, bottom + 4);
            }
            Legend(g, M, bottom + 16);
        });
    }

    void DailyBars(List<DayData> days)
    {
        Add(150, (g, y) =>
        {
            float left = M + 26, right = M + CW, bottom = y + 112, ch = 98;
            double max = Math.Max(3600, days.Max(d => d.Total));
            int stepH = max <= 4 * 3600 ? 1 : max <= 8 * 3600 ? 2 : 3;
            int topH = (int)Math.Ceiling(max / 3600 / stepH) * stepH;
            for (int hv = 0; hv <= topH; hv += stepH)
            {
                float gy = bottom - (float)(hv / (double)topH) * ch;
                using (var p = new Pen(T.Grid, 0.5f)) g.DrawLine(p, left, gy, right, gy);
                TxtR(g, hv + "h", 6.5f, false, T.Muted, left - 4, gy - 4);
            }
            float slot = (right - left) / days.Count, bw = Math.Min(slot * 0.6f, 34);
            for (int i = 0; i < days.Count; i++)
            {
                var d = days[i];
                float x = left + i * slot + (slot - bw) / 2, yb = bottom;
                double rest = d.Total;
                foreach (var kv in appColors)
                {
                    AppUsage u;
                    if (!d.Apps.TryGetValue(kv.Key, out u) || u.Seconds <= 0) continue;
                    float sh = (float)(u.Seconds / (topH * 3600.0)) * ch;
                    using (var b = new SolidBrush(kv.Value)) g.FillRectangle(b, x, yb - sh, bw, sh);
                    yb -= sh; rest -= u.Seconds;
                }
                if (rest > 1) { float sh = (float)(rest / (topH * 3600.0)) * ch; using (var b = new SolidBrush(T.Other)) g.FillRectangle(b, x, yb - sh, bw, sh); yb -= sh; }
                if (d.Total >= 60 && days.Count <= 14) { string v = Util.Fmt(d.Total); TxtC(g, v, 6.5f, true, T.Muted, left + i * slot + slot / 2, yb - 10); }
                string lab = days.Count <= 7 ? d.Date.ToString("ddd d", Cur) : d.Date.Day.ToString();
                TxtC(g, lab, 6.5f, false, T.Muted, left + i * slot + slot / 2, bottom + 3);
            }
            Legend(g, M, bottom + 16);
        });
    }

    void CategoryBlock(List<AppUsage> apps, Func<string, List<ItemUsage>> itemsFor)
    {
        var totals = Categories.Totals(apps, itemsFor);
        double sum = totals.Values.Sum();
        if (sum < 60) return;
        var cats = Categories.Names.Where(n => totals[n] >= 1).OrderByDescending(n => totals[n]).ToList();
        Section("Categories", "Productive share " + Math.Round(totals["Productive"] / sum * 100) + "%");
        Add(26 + ((cats.Count + 2) / 3) * 15, (g, y) =>
        {
            float x = M, top = y + 4;
            foreach (var c in cats)
            {
                float w = (float)(totals[c] / sum) * CW;
                using (var b = new SolidBrush(Categories.ColorFor(c, T))) g.FillRectangle(b, x, top, w, 10);
                x += w;
            }
            for (int i = 0; i < cats.Count; i++)
            {
                float cx = M + (i % 3) * (CW / 3), cy = top + 18 + (i / 3) * 15;
                using (var b = new SolidBrush(Categories.ColorFor(cats[i], T))) g.FillRectangle(b, cx, cy + 2, 7, 7);
                Txt(g, cats[i], 8.5f, true, T.Text, cx + 11, cy);
                TxtR(g, Util.Fmt(totals[cats[i]]) + "  ·  " + Math.Round(totals[cats[i]] / sum * 100) + "%", 8.5f, false, T.Muted, cx + CW / 3 - 12, cy);
            }
        });
    }

    void ProjectsBlock(IEnumerable<DayData> days)
    {
        var sum = new Dictionary<string, ProjectTotal>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in days)
            foreach (var kv in Projects.Totals(d))
            {
                ProjectTotal t;
                if (!sum.TryGetValue(kv.Key, out t)) { t = new ProjectTotal(); sum[kv.Key] = t; }
                t.Seconds += kv.Value.Seconds; t.Offline += kv.Value.Offline;
                foreach (var th in kv.Value.Things) { double v; t.Things.TryGetValue(th.Key, out v); t.Things[th.Key] = v + th.Value; }
            }
        if (sum.Count == 0) return;
        Section("Projects & clients", "matched by your project rules + offline entries");
        string[] heads = { "Project / client", "Main work", "Hours", "Rate", "Amount" };
        float[] cols = { 0, 130, 370, 420, 470 };
        TableHeader(heads, cols);
        double totalAmt = 0;
        int i = 0;
        foreach (var kv in sum.OrderByDescending(k => k.Value.Seconds))
        {
            var p = Projects.Find(kv.Key);
            double rate = p != null ? p.Rate : 0, amt = kv.Value.Seconds / 3600 * rate;
            totalAmt += amt;
            string main = string.Join(", ", kv.Value.Things.OrderByDescending(k => k.Value).Take(3).Select(k => k.Key));
            TableRow(new[] { kv.Key, main, (kv.Value.Seconds / 3600).ToString("0.00", Inv), rate > 0 ? Settings.Currency + rate.ToString("0.##", Inv) : "—", rate > 0 ? Settings.Currency + amt.ToString("#,0.00", Inv) : "—" }, cols, i++ % 2 == 1, -1);
        }
        if (totalAmt > 0)
        {
            string ta = "Total billable: " + Settings.Currency + totalAmt.ToString("#,0.00", Inv);
            Add(18, (g, y) => TxtR(g, ta, 9.5f, true, T.Text, M + CW, y + 4));
        }
    }

    void ManualBlock(DayData d)
    {
        if (d.Manual.Count == 0) return;
        Section("Offline work & breaks", "added by you");
        string[] heads = { "From", "To", "Duration", "What", "Client", "Type" };
        float[] cols = { 0, 60, 120, 180, 370, 460 };
        TableHeader(heads, cols);
        int i = 0;
        foreach (var m in d.Manual.OrderBy(m => m.Start))
            TableRow(new[] { Util.Time(m.Start), Util.Time(m.End), Util.Fmt(m.End - m.Start), m.Label, m.Client, m.IsWork ? "Work" : "Break" }, cols, i++ % 2 == 1, -1);
    }

    void AppsTable(List<AppUsage> apps, double total, int avgDays, Func<string, List<ItemUsage>> itemsFor)
    {
        if (apps.Count == 0) return;
        Section("Apps", apps.Count + " apps");
        string[] heads = avgDays > 0 ? new[] { "App", "Category", "Opened", "Avg / day", "Time", "" } : new[] { "App", "Category", "Opened", "Share", "Time", "" };
        float[] cols = { 0, 170, 260, 310, 370, 420 };
        TableHeader(heads, cols);
        double max = apps[0].Seconds;
        int i = 0;
        foreach (var a in apps)
        {
            var app = a;
            bool alt = i++ % 2 == 1;
            Add(16, (g, y) =>
            {
                if (alt) g.FillRectangle(Brushes.WhiteSmoke, M, y, CW, 16);
                DrawIcon(g, app.Key, M + 2, y + 3, 10);
                Cell(g, Store.Info(app.Key).Name, 8.5f, true, T.Text, M + 16, y + 3, 150);
                Cell(g, Insights.CategoryOf(app.Key, null), 8, false, T.Muted, M + 170, y + 3, 86);
                Txt(g, app.Opens.ToString(), 8, false, T.Muted, M + 260, y + 3);
                Txt(g, avgDays > 0 ? Util.Fmt(app.Seconds / avgDays) : Math.Round(app.Seconds / Math.Max(1, total) * 100) + "%", 8, false, T.Muted, M + 310, y + 3);
                Txt(g, Util.Fmt(app.Seconds), 8.5f, true, T.Text, M + 370, y + 3);
                float bw = (float)(app.Seconds / max) * (CW - 425);
                RoundFill(g, new RectangleF(M + 422, y + 6, Math.Max(2, bw), 4), 2, ColorOf(app.Key));
            });
        }
    }

    void SitesTable(List<ItemUsage> items)
    {
        var sites = new Dictionary<string, double>();
        foreach (var it in items)
        {
            if (it.Seconds < 1 || !Browser.Is(it.AppKey)) continue;
            string k = it.Title == Browser.PrivateLabel ? "Private windows" : it.Site ?? "Other pages";
            double v; sites.TryGetValue(k, out v); sites[k] = v + it.Seconds;
        }
        if (sites.Count == 0) return;
        Section("Websites", "time in Chrome, Brave, Edge, Firefox… grouped by site");
        string[] heads = { "Website", "Category", "Time", "" };
        float[] cols = { 0, 230, 330, 400 };
        TableHeader(heads, cols);
        double max = sites.Values.Max();
        int i = 0;
        foreach (var kv in sites.OrderByDescending(k => k.Value).Take(25))
        {
            var s = kv; bool alt = i++ % 2 == 1;
            string cat = s.Key == "Other pages" || s.Key == "Private windows" ? "Browsing" : Categories.ForSite(s.Key);
            Add(16, (g, y) =>
            {
                if (alt) g.FillRectangle(Brushes.WhiteSmoke, M, y, CW, 16);
                Cell(g, s.Key, 8.5f, true, T.Text, M + 4, y + 3, 220);
                Txt(g, cat, 8, false, T.Muted, M + 230, y + 3);
                Txt(g, Util.Fmt(s.Value), 8.5f, true, T.Text, M + 330, y + 3);
                RoundFill(g, new RectangleF(M + 400, y + 6, Math.Max(2, (float)(s.Value / max) * (CW - 404)), 4), 2, Categories.ColorFor(cat, T));
            });
        }
    }

    void PagesTable(List<ItemUsage> items, int max)
    {
        var top = items.Where(t => t.Seconds >= 1).OrderByDescending(t => t.Seconds).Take(max).ToList();
        if (top.Count == 0) return;
        Section("Pages, sheets & files", "the window or tab you were looking at");
        string[] heads = { "Page / file / window", "App", "Site", "Time" };
        float[] cols = { 0, 290, 380, 470 };
        TableHeader(heads, cols);
        int i = 0;
        foreach (var it in top)
        {
            var x = it; bool alt = i++ % 2 == 1;
            Add(16, (g, y) =>
            {
                if (alt) g.FillRectangle(Brushes.WhiteSmoke, M, y, CW, 16);
                DrawIcon(g, x.AppKey, M + 2, y + 3, 10);
                Cell(g, x.Title, 8.5f, false, T.Text, M + 16, y + 3, 270);
                Cell(g, Store.Info(x.AppKey).Name, 8, false, T.Muted, M + 290, y + 3, 86);
                Cell(g, x.Site ?? "", 8, false, T.Muted, M + 380, y + 3, 86);
                Txt(g, Util.Fmt(x.Seconds), 8.5f, true, T.Text, M + 470, y + 3);
            });
        }
    }

    void ActivityLog(DayData d, List<LogEvent> evs)
    {
        // app blocks (consecutive time in one app, quick switches under 15 s left out) + events, in time order
        var rows = new List<Tuple<double, double, string, LogEvent, List<KeyValuePair<ItemUsage, double>>>>();
        double bs = -1, be = -1; string bk = null; var items = new Dictionary<ItemUsage, double>();
        Action flush = () =>
        {
            if (bk != null && be - bs >= 15)
                rows.Add(Tuple.Create(bs, be, bk, (LogEvent)null, items.OrderByDescending(k => k.Value).Take(2).ToList()));
        };
        foreach (var s in d.Segments.OrderBy(s => s.Start))
        {
            if (bk != null && s.Key == bk && s.Start - be <= 60) be = Math.Max(be, s.End);
            else { flush(); bs = s.Start; be = s.End; bk = s.Key; items = new Dictionary<ItemUsage, double>(); }
            if (s.Item != null) { double v; items.TryGetValue(s.Item, out v); items[s.Item] = v + s.End - s.Start; }
        }
        flush();
        foreach (var e in evs) rows.Add(Tuple.Create(e.At, e.At, (string)null, e, (List<KeyValuePair<ItemUsage, double>>)null));
        foreach (var m in d.Manual) { var e = new LogEvent(); e.At = m.Start; e.Kind = "manual"; e.Text = (m.IsWork ? "Offline work: " : "Break: ") + m.Label + (m.Client.Length > 0 ? " (" + m.Client + ")" : "") + " until " + Util.Time(m.End); rows.Add(Tuple.Create(m.Start, m.End, (string)null, e, (List<KeyValuePair<ItemUsage, double>>)null)); }
        if (rows.Count == 0) return;
        rows = rows.OrderBy(r => r.Item1).ThenBy(r => r.Item4 == null ? 1 : 0).ToList();
        Section("Activity log", "everything in time order (switches under 15 s left out)");
        foreach (var r in rows)
        {
            var row = r;
            if (row.Item4 != null)
            {
                Add(14, (g, y) =>
                {
                    Txt(g, Util.Time(row.Item1), 7.5f, true, T.Muted, M, y + 2);
                    Txt(g, "•", 9, true, row.Item4.FromWindows ? T.Muted : T.Accent, M + 56, y);
                    Cell(g, row.Item4.Text + (row.Item4.FromWindows ? "  (Windows)" : ""), 8, false, row.Item4.Kind == "manual" ? T.Accent : T.Muted, M + 68, y + 2, CW - 70);
                });
            }
            else
            {
                float h = 15 + row.Item5.Count * 11;
                Add(h, (g, y) =>
                {
                    Txt(g, Util.Time(row.Item1), 7.5f, true, T.Muted, M, y + 2);
                    DrawIcon(g, row.Item3, M + 54, y + 2, 9);
                    Cell(g, Store.Info(row.Item3).Name, 8.5f, true, T.Text, M + 68, y + 1, 220);
                    Txt(g, Util.Time(row.Item1) + " – " + Util.Time(row.Item2), 7.5f, false, T.Muted, M + 300, y + 2);
                    TxtR(g, Util.Fmt(row.Item2 - row.Item1), 8.5f, true, T.Text, M + CW, y + 1);
                    float iy = y + 13;
                    foreach (var kv in row.Item5)
                    {
                        Cell(g, "– " + kv.Key.Title + (kv.Key.Site != null ? "  ·  " + kv.Key.Site : ""), 7.5f, false, T.Muted, M + 76, iy, CW - 140);
                        TxtR(g, Util.Fmt(kv.Value), 7.5f, false, T.Muted, M + CW, iy);
                        iy += 11;
                    }
                });
            }
        }
    }

    void TableHeader(string[] heads, float[] cols)
    {
        Add(16, (g, y) =>
        {
            for (int i = 0; i < heads.Length; i++) Txt(g, heads[i].ToUpperInvariant(), 6.8f, true, T.Muted, M + cols[i] + (i == 0 ? 4 : 0), y + 5);
        }, true);
    }

    void TableRow(string[] cells, float[] cols, bool alt, int highlightCol)
    {
        Add(16, (g, y) =>
        {
            if (alt) g.FillRectangle(Brushes.WhiteSmoke, M, y, CW, 16);
            for (int i = 0; i < cells.Length; i++)
            {
                float w = (i + 1 < cols.Length ? cols[i + 1] : CW) - cols[i] - 6;
                Cell(g, cells[i] ?? "", 8.3f, i == 0, i == highlightCol ? T.Danger : i == 0 ? T.Text : T.Text, M + cols[i] + (i == 0 ? 4 : 0), y + 3, w);
            }
        });
    }

    void Legend(Graphics g, float x, float y)
    {
        float cx = x;
        foreach (var kv in appColors)
        {
            string n = Store.Info(kv.Key).Name;
            if (n.Length > 20) n = n.Substring(0, 19) + "…";
            using (var b = new SolidBrush(kv.Value)) g.FillRectangle(b, cx, y + 2, 7, 7);
            Txt(g, n, 7.5f, false, T.Muted, cx + 10, y);
            cx += 16 + Measure1(n, 7.5f);
            if (cx > M + CW - 60) break;
        }
        using (var b = new SolidBrush(T.Other)) g.FillRectangle(b, cx, y + 2, 7, 7);
        Txt(g, "Other", 7.5f, false, T.Muted, cx + 10, y);
    }

    void SetColors(IEnumerable<string> keys)
    {
        appColors = new Dictionary<string, Color>();
        Color[] pal = { T.Series[0], T.Series[1], T.Series[2], T.Series[3], T.Series[4], Color.FromArgb(234, 88, 12) };
        int i = 0;
        foreach (var k in keys) { if (i >= pal.Length) break; appColors[k] = pal[i++]; }
    }

    Color ColorOf(string key) { Color c; return appColors.TryGetValue(key, out c) ? c : T.Other; }

    void DrawIcon(Graphics g, string key, float x, float y, float s)
    {
        if (key == "screen-time-tracker") { Brand.DrawLogo(g, x, y, s); return; }
        Bitmap b;
        if (!icons.TryGetValue(key, out b))
        {
            b = null;
            string path = key == "desktop" ? Environment.ExpandEnvironmentVariables(@"%WINDIR%\explorer.exe") : Store.Info(key).Path;
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) using (var ic = Icon.ExtractAssociatedIcon(path)) if (ic != null) b = ic.ToBitmap(); } catch { }
            icons[key] = b;
        }
        if (b != null) { g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.DrawImage(b, x, y, s, s); return; }
        string n = Store.Info(key).Name;
        RoundFill(g, new RectangleF(x, y, s, s), s * 0.25f, ColorOf(key) == T.Other ? T.Series[Math.Abs(n.GetHashCode()) % 5] : ColorOf(key));
        TxtC(g, n.Substring(0, 1).ToUpperInvariant(), s * 0.6f, true, Color.White, x + s / 2, y + s * 0.12f);
    }

    // ================= text & shapes (all in points) =================

    Font F(float pt, bool bold)
    {
        string k = pt + (bold ? "b" : "");
        Font f;
        if (!fonts.TryGetValue(k, out f)) { f = new Font(bold ? "Segoe UI Semibold" : "Segoe UI", pt, FontStyle.Regular, GraphicsUnit.Point); fonts[k] = f; }
        return f;
    }

    void Txt(Graphics g, string s, float pt, bool bold, Color c, float x, float y)
    {
        using (var b = new SolidBrush(c)) g.DrawString(s ?? "", F(pt, bold), b, x, y, sf);
    }

    void TxtR(Graphics g, string s, float pt, bool bold, Color c, float xr, float y) { Txt(g, s, pt, bold, c, xr - Measure1(s, pt, bold), y); }
    void TxtC(Graphics g, string s, float pt, bool bold, Color c, float xc, float y) { Txt(g, s, pt, bold, c, xc - Measure1(s, pt, bold) / 2, y); }

    void Cell(Graphics g, string s, float pt, bool bold, Color c, float x, float y, float w)
    {
        using (var b = new SolidBrush(c)) g.DrawString(s ?? "", F(pt, bold), b, new RectangleF(x, y, Math.Max(1, w), pt * 1.6f), sf);
    }

    float Wrap(Graphics g, string s, float pt, bool bold, Color c, float x, float y, float w)
    {
        float h = Measure(s, pt, bold, w);
        using (var b = new SolidBrush(c)) g.DrawString(s, F(pt, bold), b, new RectangleF(x, y, w, h + 2), wrap);
        return h;
    }

    float Measure(string s, float pt, bool bold, float w) { return mg.MeasureString(s, F(pt, bold), new SizeF(w, 10000), wrap).Height; }
    float Measure1(string s, float pt, bool bold = false) { return mg.MeasureString(s ?? "", F(pt, bold), PointF.Empty, sf).Width; }

    static GraphicsPath RoundPath(RectangleF r, float rad)
    {
        rad = Math.Max(0.1f, Math.Min(rad, Math.Min(r.Width, r.Height) / 2));
        var p = new GraphicsPath(); float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }

    static void RoundFill(Graphics g, RectangleF r, float rad, Color c)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        using (var p = RoundPath(r, rad)) using (var b = new SolidBrush(c)) g.FillPath(b, p);
    }

    static void RoundStroke(Graphics g, RectangleF r, float rad, Color c)
    {
        using (var p = RoundPath(r, rad)) using (var pen = new Pen(c, 0.6f)) g.DrawPath(pen, p);
    }

    // ================= pagination & output =================

    void Paginate()
    {
        pages = new List<List<KeyValuePair<Block, float>>>();
        var cur = new List<KeyValuePair<Block, float>>();
        float top = M, contTop = M + 22, bottom = PH - M - 16, y = top;
        for (int i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            float need = b.H;
            for (int j = i; j < blocks.Count - 1 && blocks[j].KeepWithNext; j++) need += blocks[j + 1].H;
            if (y + need > bottom && cur.Count > 0) { pages.Add(cur); cur = new List<KeyValuePair<Block, float>>(); y = contTop; }
            cur.Add(new KeyValuePair<Block, float>(b, y));
            y += b.H;
        }
        pages.Add(cur);
    }

    void DrawPage(Graphics g, int index)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        if (index > 0)
        {
            Txt(g, runTitle, 7.5f, false, T.Muted, M, M - 4);
            using (var p = new Pen(T.Border, 0.5f)) g.DrawLine(p, M, M + 10, M + CW, M + 10);
        }
        foreach (var kv in pages[index]) kv.Key.Draw(g, kv.Value);
        Txt(g, "Screen Time Tracker · private report generated on this PC", 7, false, T.Muted, M, PH - M + 2);
        TxtR(g, "Page " + (index + 1) + " of " + pages.Count, 7, false, T.Muted, M + CW, PH - M + 2);
    }

    string Save(string path)
    {
        Paginate();
        if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            // preview: each page as an image (used for testing)
            for (int i = 0; i < pages.Count; i++)
                using (var bmp = new Bitmap((int)(PW / 72 * 110), (int)(PH / 72 * 110)))
                {
                    bmp.SetResolution(110, 110);
                    using (var g = Graphics.FromImage(bmp)) { g.Clear(Color.White); g.PageUnit = GraphicsUnit.Point; DrawPage(g, i); }
                    bmp.Save(path.Substring(0, path.Length - 4) + "-" + (i + 1) + ".png", ImageFormat.Png);
                }
            return path;
        }
        try { if (File.Exists(path)) File.Delete(path); } catch { }
        bool ok = false;
        try { ok = PrintToPdf(path); } catch { ok = false; }
        if (!ok) ImagePdf(path);
        mg.Dispose(); mbmp.Dispose();
        foreach (var f in fonts.Values) f.Dispose();
        return path;
    }

    bool PrintToPdf(string path)
    {
        const string printer = "Microsoft Print to PDF";
        if (!PrinterSettings.InstalledPrinters.Cast<string>().Contains(printer)) return false;
        using (var doc = new PrintDocument())
        {
            doc.PrinterSettings.PrinterName = printer;
            if (!doc.PrinterSettings.IsValid) return false;
            doc.PrinterSettings.PrintToFile = true;
            doc.PrinterSettings.PrintFileName = path;
            doc.DocumentName = runTitle;
            var a4 = doc.PrinterSettings.PaperSizes.Cast<PaperSize>().FirstOrDefault(p => p.Kind == PaperKind.A4);
            if (a4 != null) doc.DefaultPageSettings.PaperSize = a4;
            doc.DefaultPageSettings.Landscape = false;
            doc.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
            doc.OriginAtMargins = false;
            doc.PrintController = new StandardPrintController();
            int page = 0;
            doc.PrintPage += (s, e) =>
            {
                e.Graphics.PageUnit = GraphicsUnit.Point;
                e.Graphics.TranslateTransform(-e.PageSettings.HardMarginX * 0.72f, -e.PageSettings.HardMarginY * 0.72f);
                DrawPage(e.Graphics, page);
                page++;
                e.HasMorePages = page < pages.Count;
            };
            doc.Print();
        }
        // the spooler finishes writing the file shortly after Print() returns
        long last = -1;
        for (int i = 0; i < 100; i++)
        {
            Thread.Sleep(150);
            if (!File.Exists(path)) continue;
            long len = new FileInfo(path).Length;
            if (len > 0 && len == last) return true;
            last = len;
        }
        return File.Exists(path) && new FileInfo(path).Length > 0;
    }

    void ImagePdf(string path)
    {
        const float dpi = 150;
        int pw = (int)(PW / 72 * dpi), ph = (int)(PH / 72 * dpi);
        var jpegs = new List<byte[]>();
        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.MimeType == "image/jpeg");
        var ep = new EncoderParameters(1);
        ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 88L);
        for (int i = 0; i < pages.Count; i++)
        {
            using (var bmp = new Bitmap(pw, ph))
            {
                bmp.SetResolution(dpi, dpi);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.White);
                    g.PageUnit = GraphicsUnit.Point;
                    DrawPage(g, i);
                }
                using (var ms = new MemoryStream()) { bmp.Save(ms, codec, ep); jpegs.Add(ms.ToArray()); }
            }
        }
        using (var fs = new FileStream(path, FileMode.Create))
        {
            var offsets = new List<long>();
            Action<string> W = s => { var b = Encoding.ASCII.GetBytes(s); fs.Write(b, 0, b.Length); };
            W("%PDF-1.4\n");
            int n = jpegs.Count;
            offsets.Add(fs.Position); W("1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n");
            var kids = string.Join(" ", Enumerable.Range(0, n).Select(i => (3 + i * 3) + " 0 R"));
            offsets.Add(fs.Position); W("2 0 obj << /Type /Pages /Kids [" + kids + "] /Count " + n + " >> endobj\n");
            for (int i = 0; i < n; i++)
            {
                int pg = 3 + i * 3, ct = pg + 1, im = pg + 2;
                string content = "q " + PW.ToString("0.##", Inv) + " 0 0 " + PH.ToString("0.##", Inv) + " 0 0 cm /Im0 Do Q";
                offsets.Add(fs.Position);
                W(pg + " 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 " + PW.ToString("0.##", Inv) + " " + PH.ToString("0.##", Inv) + "] /Resources << /XObject << /Im0 " + im + " 0 R >> >> /Contents " + ct + " 0 R >> endobj\n");
                offsets.Add(fs.Position);
                W(ct + " 0 obj << /Length " + content.Length + " >> stream\n" + content + "\nendstream endobj\n");
                offsets.Add(fs.Position);
                W(im + " 0 obj << /Type /XObject /Subtype /Image /Width " + pw + " /Height " + ph + " /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length " + jpegs[i].Length + " >> stream\n");
                fs.Write(jpegs[i], 0, jpegs[i].Length);
                W("\nendstream endobj\n");
            }
            long xref = fs.Position;
            W("xref\n0 " + (offsets.Count + 1) + "\n0000000000 65535 f \n");
            foreach (var o in offsets) W(o.ToString("D10") + " 00000 n \n");
            W("trailer << /Size " + (offsets.Count + 1) + " /Root 1 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
        }
    }
}

static class Brand
{
    public static void DrawLogo(Graphics g, float x, float y, float s)
    {
        using (var path = new GraphicsPath())
        {
            float r = s * 0.26f, d = r * 2;
            path.AddArc(x, y, d, d, 180, 90); path.AddArc(x + s - d, y, d, d, 270, 90);
            path.AddArc(x + s - d, y + s - d, d, d, 0, 90); path.AddArc(x, y + s - d, d, d, 90, 90);
            path.CloseFigure();
            using (var br = new LinearGradientBrush(new PointF(x, y), new PointF(x + s, y + s), Color.FromArgb(92, 110, 255), Color.FromArgb(20, 184, 166)))
                g.FillPath(br, path);
        }
        float c = x + s / 2, cy = y + s / 2, rr = s * 0.31f;
        g.FillEllipse(Brushes.White, c - rr, cy - rr, rr * 2, rr * 2);
        using (var wb = new SolidBrush(Color.FromArgb(255, 190, 60))) g.FillPie(wb, c - rr * 0.82f, cy - rr * 0.82f, rr * 1.64f, rr * 1.64f, -90, 125);
        using (var pen = new Pen(Color.FromArgb(40, 48, 90), s * 0.07f))
        {
            pen.StartCap = pen.EndCap = LineCap.Round;
            g.DrawLine(pen, c, cy, c, cy - rr * 0.62f);
            g.DrawLine(pen, c, cy, c + rr * 0.48f, cy + rr * 0.18f);
        }
    }
}
