using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;

// Month view: totals, a calendar heatmap, Wrapped/PDF actions, categories, websites, apps.
partial class DashView
{
    float PaintMonth(Graphics g, int w, float y)
    {
        float M = 24 * S, cw = w - 2 * M, x = M, gap = 12 * S;
        var first = new DateTime(Date.Year, Date.Month, 1);
        int n = DateTime.DaysInMonth(Date.Year, Date.Month);
        var days = new List<DayData>();
        var agg = new DayData(); agg.Date = first;
        for (int i = 0; i < n; i++)
        {
            var d = Day(first.AddDays(i));
            days.Add(d);
            if (d.Date <= Clock.Today) DataTools.MergeInto(agg, d, null);
        }
        var apps = agg.Sorted();
        var colors = ColorsFor(apps.Select(a => a.Key));
        badgeColors = colors;
        var active = days.Where(d => d.Total >= 60).ToList();
        double total = active.Sum(d => d.Total), avg = active.Count > 0 ? total / active.Count : 0;
        var ins = active.Where(d => d.Total >= 600).Select(d => Insights.ForDay(d, null)).ToList();

        y = PausedBanner(g, x, y, cw);

        // previous month comparison
        var pFirst = first.AddMonths(-1);
        double pTotal = 0; int pN = 0;
        for (var d = pFirst; d < first; d = d.AddDays(1)) { double t = Day(d).Total; if (t >= 60) { pTotal += t; pN++; } }
        y = SummaryCard(g, x, y, cw, Lang.T("DAILY AVERAGE"), Util.Fmt(avg), "Total " + Util.Fmt(total) + " across " + active.Count + (active.Count == 1 ? " day" : " days") + " in " + first.ToString("MMMM", Cur),
            false, avg, pN > 0 ? pTotal / pN : 0, pN > 0 && active.Count > 0, "last month's average", "(" + Util.Fmt(pN > 0 ? pTotal / pN : 0) + " a day)", 0);
        y += gap;
        var top = apps.Count > 0 ? apps[0] : null;
        y = Tiles(g, x, y, cw, top, top == null ? 0 : top.Seconds,
            new[] { "DEEP WORK", "AVG FOCUS SCORE", "CALLS / MEETINGS" },
            new[] { Util.Fmt(ins.Sum(i => i.DeepWork)), ins.Count > 0 ? Math.Round(ins.Average(i => i.FocusScore)) + "/100" : "—", Util.Fmt(days.Sum(d => d.MeetingSeconds)) });
        y += gap;

        y = CalendarCard(g, x, y, cw, first, days) + gap;

        // actions
        float ah = 64 * S;
        if (InView(y, ah))
        {
            Card(g, new RectangleF(x, y, cw, ah));
            float cx = x + 20 * S, cy = y + 17 * S;
            cx = Chip(g, cx, cy, "  Monthly PDF report", "pdf") + 8 * S;
            cx = Chip(g, cx, cy, "★  " + Lang.T("Make my Screen Time Wrapped card"), "wrapped:month") + 8 * S;
            Chip(g, cx, cy, "★  Wrapped – " + Date.Year + " so far", "wrapped:year");
        }
        y += ah + gap;

        Func<string, List<ItemUsage>> itemsFor = k => agg.ItemsFor(k);
        float c2 = CategoryCard(g, x, y, cw, apps, itemsFor);
        if (c2 > y) y = c2 + gap;
        if (agg.ItemList.Count > 0) y = SitesCards(g, x, y, cw, agg.ItemList) + gap;
        return AppList(g, x, y, cw, apps, colors, total, Math.Max(1, active.Count), "No screen time recorded this month yet.", itemsFor);
    }

    float CalendarCard(Graphics g, float x, float y, float cw, DateTime first, List<DayData> days)
    {
        int lead = ((int)first.DayOfWeek + 6) % 7;
        int weeks = (lead + days.Count + 6) / 7;
        float head = 84 * S, cellH = 66 * S, h = head + weeks * cellH + 16 * S;
        if (!InView(y, h)) return y + h;
        Card(g, new RectangleF(x, y, cw, h));
        Txt(g, Lang.T("Calendar"), F(15, true), T.Text, x + 20 * S, y + 18 * S);
        TextRight(g, "Darker = more screen time · click a day to open it", F(12.5f), T.Muted, x + cw - 20 * S, y + 21 * S);
        float gx = x + 20 * S, gw = cw - 40 * S, cwid = gw / 7;
        string[] dn = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
        for (int i = 0; i < 7; i++) Txt(g, dn[i], F(11.5f, true), T.Muted, gx + i * cwid + 6 * S, y + 56 * S);
        double max = Math.Max(3600, days.Max(d => d.Total));
        for (int i = 0; i < days.Count; i++)
        {
            var d = days[i];
            int pos = lead + i;
            var r = new RectangleF(gx + (pos % 7) * cwid + 3 * S, y + head + (pos / 7) * cellH, cwid - 6 * S, cellH - 6 * S);
            bool future = d.Date > Clock.Today;
            float k = (float)Math.Min(1, d.Total / max);
            Color c = future ? T.Surface : d.Total < 60 ? T.Hover : Blend(T.Surface, T.Accent, 0.12f + 0.78f * k);
            FillRound(g, r, 8 * S, c);
            if (d.Date == Clock.Today) using (var p = new Pen(T.Accent, 2 * S)) using (var path = Round(r, 8 * S)) g.DrawPath(p, path);
            string id = "mday:" + d.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            bool dark = k > 0.55f && !future;
            Txt(g, d.Date.Day.ToString(), F(12.5f, true), future ? T.Border : dark ? Color.White : T.Text, r.X + 8 * S, r.Y + 6 * S);
            if (!future && d.Total >= 60)
            {
                Txt(g, Util.Fmt(d.Total), F(12), dark ? Color.White : T.Muted, r.X + 8 * S, r.Bottom - 22 * S);
                var att = Attendance.For(d, Tracker);
                if (att.LateBy > 0) { var dot = new RectangleF(r.Right - 14 * S, r.Y + 9 * S, 7 * S, 7 * S); g.FillEllipse(Brush(T.Warn), dot); }
            }
            if (!future)
            {
                if (hover == id) using (var p = new Pen(T.Text, 1.5f * S)) using (var path = Round(r, 8 * S)) g.DrawPath(p, path);
                AddHit(r, id, true);
                if (hover == id)
                {
                    var si = Sessions.Compute(d, Sessions.Merged(d), d.Date == Clock.Today, Tracker);
                    tip = new List<string> { d.Date.ToString("dddd, d MMM", Cur) + "   " + Util.Fmt(d.Total) };
                    if (si.LogIn >= 0) tip.Add("Log in " + Util.Time(si.LogIn) + " · log out " + (si.StillOn ? "still on" : Util.Time(si.LogOut)));
                    foreach (var a in d.Sorted().Take(3)) tip.Add(Store.Info(a.Key).Name + "   " + Util.Fmt(a.Seconds));
                    var att = Attendance.For(d, Tracker);
                    if (att.Status.Length > 0) tip.Add("Attendance: " + att.Status);
                }
            }
        }
        return y + h;
    }
}
