using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;

// Work view: projects & clients (hours and amounts), attendance for the month, offline work & breaks,
// calls & meetings, and focus mode.
partial class DashView
{
    float PaintWork(Graphics g, int w, float y)
    {
        float M = 24 * S, cw = w - 2 * M, x = M, gap = 12 * S;
        y = PausedBanner(g, x, y, cw);
        y = ProjectsCard(g, x, y, cw) + gap;
        y = ManualCard(g, x, y, cw) + gap;
        y = FocusCallsCard(g, x, y, cw) + gap;
        return AttendanceCard(g, x, y, cw);
    }

    float ProjectsCard(Graphics g, float x, float y, float cw)
    {
        var ws = WeekStart(Date);
        var ms = new DateTime(Date.Year, Date.Month, 1);
        var today = Projects.Totals(Day(Date));
        var week = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var month = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        for (var d = ms; d <= ms.AddMonths(1).AddDays(-1) && d <= Clock.Today; d = d.AddDays(1))
            foreach (var kv in Projects.Totals(Day(d))) { double v; month.TryGetValue(kv.Key, out v); month[kv.Key] = v + kv.Value.Seconds; }
        for (var d = ws; d <= ws.AddDays(6) && d <= Clock.Today; d = d.AddDays(1))
            foreach (var kv in Projects.Totals(Day(d))) { double v; week.TryGetValue(kv.Key, out v); week[kv.Key] = v + kv.Value.Seconds; }
        var names = Projects.All.Select(p => p.Name).Union(month.Keys, StringComparer.OrdinalIgnoreCase).ToList();
        names = names.OrderByDescending(nm => { double v; month.TryGetValue(nm, out v); return v; }).ToList();

        float head = 100 * S, rowH = 34 * S;
        float h = head + (names.Count == 0 ? 80 * S : names.Count * rowH + 40 * S) + 10 * S;
        if (!InView(y, h)) return y + h;
        Card(g, new RectangleF(x, y, cw, h));
        Txt(g, Lang.T("Projects & clients"), F(15, true), T.Text, x + 20 * S, y + 18 * S);
        float cx = x + 20 * S, cy = y + 50 * S;
        cx = Chip(g, cx, cy, "⚙  Manage projects & clients", "projects") + 8 * S;
        cx = Chip(g, cx, cy, "  Copy work note for " + (Date == Clock.Today ? "today" : Date.ToString("d MMM", Cur)), "note") + 8 * S;
        Chip(g, cx, cy, "  Timesheet CSV – " + Date.ToString("MMM yyyy", Cur), "timesheet");
        float ty = y + head;
        if (names.Count == 0)
        {
            g.DrawString("Add your clients or projects with a few keywords (e.g. \"Sharma\" or a sheet name). Screen time on matching windows, sites and apps – plus offline work you tag – becomes hours per client, ready for timesheets and invoices.",
                F(13), Brush(T.Muted), new RectangleF(x + 20 * S, ty, cw - 40 * S, 70 * S));
            return y + h;
        }
        float ix = x + 20 * S, iw = cw - 40 * S;
        float[] cols = { 0, 0.34f, 0.47f, 0.60f, 0.73f, 0.85f };
        string[] heads = { "PROJECT / CLIENT", Date == Clock.Today ? "TODAY" : Date.ToString("d MMM", Cur).ToUpperInvariant(), "THIS WEEK", "THIS MONTH", "RATE / HR", "AMOUNT (MONTH)" };
        for (int i = 0; i < heads.Length; i++) Txt(g, heads[i], F(11, true), T.Muted, ix + cols[i] * iw, ty);
        ty += 22 * S;
        double totalAmt = 0;
        foreach (var nm in names)
        {
            var p = Projects.Find(nm);
            ProjectTotal pt; double tv = today.TryGetValue(nm, out pt) ? pt.Seconds : 0;
            double wv; week.TryGetValue(nm, out wv);
            double mv; month.TryGetValue(nm, out mv);
            double rate = p != null ? p.Rate : 0, amt = mv / 3600 * rate;
            totalAmt += amt;
            using (var pen = new Pen(T.Grid)) g.DrawLine(pen, ix, ty, ix + iw, ty);
            g.DrawString(nm, F(13.5f, true), Brush(T.Text), new RectangleF(ix, ty + 8 * S, iw * 0.33f, 20 * S), sf);
            Txt(g, Util.Fmt(tv), F(13), T.Text, ix + cols[1] * iw, ty + 8 * S);
            Txt(g, Util.Fmt(wv), F(13), T.Text, ix + cols[2] * iw, ty + 8 * S);
            Txt(g, Util.Fmt(mv), F(13, true), T.Text, ix + cols[3] * iw, ty + 8 * S);
            Txt(g, rate > 0 ? Settings.Currency + rate.ToString("0.##", CultureInfo.InvariantCulture) : "—", F(13), T.Muted, ix + cols[4] * iw, ty + 8 * S);
            Txt(g, rate > 0 ? Settings.Currency + amt.ToString("#,0", CultureInfo.InvariantCulture) : "—", F(13, true), T.Text, ix + cols[5] * iw, ty + 8 * S);
            if (pt != null && new RectangleF(ix, ty, iw, rowH).Contains(mouse.X, mouse.Y + scrollY))
            {
                tip = new List<string> { nm + " – " + Util.Fmt(pt.Seconds) + (Date == Clock.Today ? " today" : "") };
                foreach (var th in pt.Things.OrderByDescending(k => k.Value).Take(5)) tip.Add(th.Key + "   " + Util.Fmt(th.Value));
            }
            ty += rowH;
        }
        if (totalAmt > 0) TextRight(g, "Total for " + Date.ToString("MMMM", Cur) + ": " + Settings.Currency + totalAmt.ToString("#,0.00", CultureInfo.InvariantCulture), F(13.5f, true), T.Text, x + cw - 20 * S, ty + 10 * S);
        return y + h;
    }

    float ManualCard(Graphics g, float x, float y, float cw)
    {
        var d = Day(Date);
        var list = d.Manual.OrderBy(m => m.Start).ToList();
        float head = 92 * S, rowH = 40 * S;
        float h = head + (list.Count == 0 ? 40 * S : list.Count * rowH) + 10 * S;
        if (!InView(y, h)) return y + h;
        Card(g, new RectangleF(x, y, cw, h));
        Txt(g, Lang.T("Offline work & breaks") + " – " + (Date == Clock.Today ? Lang.T("Today") : Date.ToString("ddd d MMM", Cur)), F(15, true), T.Text, x + 20 * S, y + 18 * S);
        Chip(g, x + 20 * S, y + 48 * S, "  Add offline work or a break", "addman");
        TextRight(g, "Meetings, calls, visits, lunch… – time away from the screen", F(12.5f), T.Muted, x + cw - 20 * S, y + 21 * S);
        float ry = y + head;
        if (list.Count == 0)
        {
            Txt(g, "Nothing added. After a long break you'll be asked what you were doing.", F(13), T.Muted, x + 20 * S, ry + 6 * S);
            return y + h;
        }
        for (int i = 0; i < list.Count; i++)
        {
            var m = list[i];
            var row = new RectangleF(x + 8 * S, ry, cw - 16 * S, rowH);
            string id = "man:" + i;
            if (hover == id) FillRound(g, row, 8 * S, T.Hover);
            AddHit(row, id, true);
            FillRound(g, new RectangleF(x + 20 * S, ry + 12 * S, 14 * S, 14 * S), 4 * S, m.IsWork ? T.Accent : T.Muted);
            Txt(g, Util.Time(m.Start) + " – " + Util.Time(m.End), F(13, true), T.Text, x + 44 * S, ry + 10 * S);
            Txt(g, m.Label + (m.Client.Length > 0 ? "  ·  " + m.Client : "") + (m.IsWork ? "" : "  (break)"), F(13), T.Text, x + 210 * S, ry + 10 * S);
            TextRight(g, Util.Fmt(m.End - m.Start) + "   ✎", F(13, true), T.Muted, x + cw - 20 * S, ry + 10 * S);
            ry += rowH;
        }
        return y + h;
    }

    float FocusCallsCard(Graphics g, float x, float y, float cw)
    {
        var d = Day(Date);
        float h = 150 * S;
        if (!InView(y, h)) return y + h;
        float colW = (cw - 12 * S) / 2;
        // focus
        Card(g, new RectangleF(x, y, colW, h));
        Txt(g, Lang.T("Focus mode"), F(15, true), T.Text, x + 20 * S, y + 18 * S);
        int sessions = d.Events.Count(e => e.Kind == "focusend"), blocked = d.Events.Count(e => e.Kind == "blocked");
        Txt(g, sessions + (sessions == 1 ? " session" : " sessions") + " · " + blocked + " distractions blocked", F(12.5f), T.Muted, x + 20 * S, y + 44 * S);
        if (Tracker != null && Tracker.Focusing)
            Chip(g, x + 20 * S, y + 74 * S, "  " + Lang.T("Stop focus") + " (" + Math.Ceiling((Tracker.FocusUntil - Clock.Now).TotalMinutes) + " min left)", "stopfocus");
        else
        {
            float cx = x + 20 * S;
            foreach (var mins in new[] { 25, 50, 90 }) cx = Chip(g, cx, y + 74 * S, "  " + mins + " min", "focus:" + mins) + 8 * S;
        }
        g.DrawString("Blocks: " + string.Join(", ", Settings.FocusBlockCategories.Concat(Settings.FocusBlockExtra)), F(11.5f), Brush(T.Muted), new RectangleF(x + 20 * S, y + 116 * S, colW - 40 * S, 18 * S), sf);

        // calls & meetings this week
        float x2 = x + colW + 12 * S;
        Card(g, new RectangleF(x2, y, colW, h));
        Txt(g, Lang.T("Calls & meetings"), F(15, true), T.Text, x2 + 20 * S, y + 18 * S);
        var ws = WeekStart(Date);
        double[] vals = new double[7];
        for (int i = 0; i < 7; i++) vals[i] = Day(ws.AddDays(i)).MeetingSeconds;
        TextRight(g, "This week " + Util.Fmt(vals.Sum()), F(12.5f), T.Muted, x2 + colW - 20 * S, y + 21 * S);
        double max = Math.Max(1800, vals.Max());
        float bx = x2 + 20 * S, bw = (colW - 40 * S) / 7, bottom = y + h - 26 * S, ch = 64 * S;
        for (int i = 0; i < 7; i++)
        {
            float bh = (float)(vals[i] / max) * ch;
            FillRound(g, new RectangleF(bx + i * bw + bw * 0.25f, bottom - Math.Max(2 * S, bh), bw * 0.5f, Math.Max(2 * S, bh)), 3 * S, ws.AddDays(i) == Date ? T.Accent : Blend(T.Accent, T.Surface, 0.45f));
            string lab = ws.AddDays(i).ToString("ddd", Cur);
            Txt(g, lab, F(11), T.Muted, bx + i * bw + bw / 2 - Measure(g, lab, F(11)).Width / 2, bottom + 4 * S);
            if (vals[i] >= 60) { string v = Util.Fmt(vals[i]); Txt(g, v, F(10.5f), T.Muted, bx + i * bw + bw / 2 - Measure(g, v, F(10.5f)).Width / 2, bottom - Math.Max(2 * S, bh) - 16 * S); }
        }
        return y + h;
    }

    float AttendanceCard(Graphics g, float x, float y, float cw)
    {
        var ms = new DateTime(Date.Year, Date.Month, 1);
        var rows = new List<AttendanceDay>();
        for (var d = ms; d <= ms.AddMonths(1).AddDays(-1) && d <= Clock.Today; d = d.AddDays(1)) rows.Add(Attendance.For(Day(d), Tracker));
        rows.Reverse();
        float head = 108 * S, rowH = 32 * S, h = head + rows.Count * rowH + 10 * S;
        if (!InView(y, h)) return y + h;
        Card(g, new RectangleF(x, y, cw, h));
        Txt(g, Lang.T("Attendance") + " – " + Date.ToString("MMMM yyyy", Cur), F(15, true), T.Text, x + 20 * S, y + 18 * S);
        TextRight(g, "Work hours " + Attendance.Clock12(Settings.WorkStart) + " – " + Attendance.Clock12(Settings.WorkEnd) + " (change in Settings → Work)", F(12.5f), T.Muted, x + cw - 20 * S, y + 21 * S);
        int present = rows.Count(r => r.Present), late = rows.Count(r => r.LateBy > 0), early = rows.Count(r => r.EarlyBy > 0),
            absent = rows.Count(r => r.Status == "Absent");
        double ot = rows.Sum(r => r.WorkDay ? r.Overtime : 0), worked = rows.Sum(r => r.Worked);
        string sum = "Present " + present + " · Late " + late + " · Left early " + early + " · Absent " + absent + " · Overtime " + Util.Fmt(ot) + " · Worked " + Util.Fmt(worked);
        Txt(g, sum, F(13, true), T.Text, x + 20 * S, y + 48 * S);
        float ix = x + 20 * S, iw = cw - 40 * S;
        float[] cols = { 0, 0.18f, 0.32f, 0.46f, 0.60f };
        string[] heads = { "DAY", "ARRIVED", "LEFT", "WORKED", "STATUS" };
        for (int i = 0; i < heads.Length; i++) Txt(g, heads[i], F(11, true), T.Muted, ix + cols[i] * iw, y + 80 * S);
        float ry = y + head;
        foreach (var r in rows)
        {
            if (InView(ry, rowH))
            {
                string id = "logday:" + r.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var row = new RectangleF(x + 8 * S, ry, cw - 16 * S, rowH);
                if (hover == id) FillRound(g, row, 8 * S, T.Hover);
                AddHit(row, id, true);
                Txt(g, r.Date.ToString("ddd d MMM", Cur), F(13, true), r.WorkDay ? T.Text : T.Muted, ix, ry + 7 * S);
                Txt(g, Util.Time(r.Arrived), F(13), T.Text, ix + cols[1] * iw, ry + 7 * S);
                Txt(g, r.StillOn ? "Still working" : Util.Time(r.Left), F(13), r.StillOn ? T.Good : T.Text, ix + cols[2] * iw, ry + 7 * S);
                Txt(g, r.Worked >= 60 ? Util.Fmt(r.Worked) : "—", F(13), T.Text, ix + cols[3] * iw, ry + 7 * S);
                Color sc = r.Status == "Absent" ? T.Danger : r.LateBy > 0 || r.EarlyBy > 0 ? T.Warn : r.Status.StartsWith("Overtime") || r.Status.Contains("Overtime") ? T.Accent : r.Present ? T.Good : T.Muted;
                Txt(g, r.Status, F(13, true), sc, ix + cols[4] * iw, ry + 7 * S);
            }
            ry += rowH;
        }
        return y + h;
    }
}
