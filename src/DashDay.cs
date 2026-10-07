using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;

// Day view: summary, tiles, timeline, hourly chart, categories, websites & files, apps.
partial class DashView
{
    float PaintDay(Graphics g, int w, float y)
    {
        float M = 24 * S, cw = w - 2 * M, x = M, gap = 12 * S;
        var d = Day(Date);
        var apps = d.Sorted();
        double total = d.Total;
        var colors = ColorsFor(apps.Select(a => a.Key));
        badgeColors = colors;
        bool isToday = Date == Clock.Today;

        y = PausedBanner(g, x, y, cw);

        double avg; int n;
        AverageBefore(Date, 7, isToday ? Clock.Now.TimeOfDay.TotalHours : 24, out avg, out n);
        string sub = Date.ToString("dddd, d MMMM", Cur);
        if (d.Segments.Count > 0)
            sub += "  ·  " + Util.Time(d.FirstActive) + " – " + (isToday && ActiveNow ? "now" : Util.Time(d.LastActive));
        y = SummaryCard(g, x, y, cw, "SCREEN TIME", Util.Fmt(total), sub, isToday, total, avg, n > 0,
            isToday ? "your usual by this time" : "your 7-day average",
            isToday ? "(usually " + Util.Fmt(avg) + " by now)" : "(" + Util.Fmt(avg) + " a day)",
            Settings.DailyGoalMinutes * 60);
        y += gap;

        var top = apps.Count > 0 ? apps[0] : null;
        y = Tiles(g, x, y, cw, top, top == null ? 0 : top.Seconds,
            new[] { "APPS USED", "APP SWITCHES", "UNLOCKS" },
            new[] { apps.Count.ToString(), d.Opens.ToString(), d.Unlocks.ToString() });
        y += gap;

        y = InsightsCard(g, x, y, cw, d) + gap;
        if (d.Segments.Count > 0 || d.Manual.Count > 0) y = TimelineCard(g, x, y, cw, d, colors, isToday) + gap;

        // hourly chart
        float cardY = y;
        float chartTop = y + 58 * S, chartH = 150 * S, axisW = 40 * S;
        float left = x + 20 * S + axisW, right = x + cw - 20 * S, bottom = chartTop + chartH;
        var legendKeys = apps.Take(5).Select(a => a.Key).ToList();
        bool hasOther = apps.Count > 5;
        float legendH = LegendHeight(g, legendKeys, hasOther, right - (x + 20 * S));
        float cardH = (bottom - y) + 34 * S + (legendKeys.Count > 0 ? legendH + 12 * S : 0) + 8 * S;
        if (InView(y, cardH))
        {
            Card(g, new RectangleF(x, y, cw, cardH));
            Txt(g, "Hourly activity", F(15, true), T.Text, x + 20 * S, y + 18 * S);
            var hourTotals = new double[24];
            foreach (var a in d.Apps.Values) for (int i = 0; i < 24; i++) hourTotals[i] += a.Hours[i];
            int peakH = -1; double peakV = 0;
            for (int i = 0; i < 24; i++) if (hourTotals[i] > peakV) { peakV = hourTotals[i]; peakH = i; }
            if (peakH >= 0 && peakV >= 60) TextRight(g, "Busiest hour: " + HourName(peakH) + " – " + HourName(peakH + 1), F(12.5f), T.Muted, x + cw - 20 * S, y + 21 * S);

            for (int v = 0; v <= 60; v += 15)
            {
                float gy = bottom - v / 60f * chartH;
                using (var p = new Pen(T.Grid)) g.DrawLine(p, left, gy, right, gy);
                TextRight(g, v + "m", F(11.5f), T.Muted, left - 8 * S, gy - 8 * S);
            }
            float slot = (right - left) / 24f, bw = slot * 0.64f;
            for (int hr = 0; hr < 24; hr++)
            {
                float sx = left + hr * slot;
                var col = new RectangleF(sx, chartTop - 6 * S, slot, chartH + 6 * S);
                string id = "hour:" + hr;
                if (hover == id) FillRound(g, col, 6 * S, T.Hover);
                AddHit(col, id, true);
                double ht = hourTotals[hr];
                if (ht >= 1)
                {
                    var segs = new List<KeyValuePair<Color, double>>();
                    double rest = ht;
                    foreach (var k in legendKeys)
                    {
                        double v = d.Apps[k].Hours[hr];
                        if (v > 0) { segs.Add(new KeyValuePair<Color, double>(colors[k], v)); rest -= v; }
                    }
                    if (rest > 0.5) segs.Add(new KeyValuePair<Color, double>(T.Other, rest));
                    StackedBar(g, sx + (slot - bw) / 2, bottom, bw, chartH / 3600f, segs);
                }
                if (hover == id) tip = HourTip(d, hr, ht);
                if (hr % 6 == 0) Txt(g, HourName(hr), F(11.5f), T.Muted, sx + slot / 2 - Measure(g, HourName(hr), F(11.5f)).Width / 2, bottom + 8 * S);
            }
            if (isToday)
            {
                float nx = left + Clock.Now.Hour * slot + slot / 2;
                using (var b = new SolidBrush(T.Accent)) g.FillEllipse(b, nx - 2.5f * S, bottom + 28 * S, 5 * S, 5 * S);
            }
            if (legendKeys.Count > 0) Legend(g, legendKeys, colors, hasOther, x + 20 * S, bottom + 34 * S, right - (x + 20 * S));
        }
        y = cardY + cardH + gap;

        Func<string, List<ItemUsage>> itemsFor = k => d.ItemsFor(k);
        float cy = CategoryCard(g, x, y, cw, apps, itemsFor);
        if (cy > y) y = cy + gap;
        if (d.ItemList.Count > 0 || apps.Any(a => Browser.Is(a.Key))) y = SitesCards(g, x, y, cw, d.ItemList) + gap;

        return AppList(g, x, y, cw, apps, colors, total, 0, "No screen time recorded for this day yet.", itemsFor);
    }

    // A horizontal strip of the day, zoomed to the hours that had activity, coloured by app.
    float TimelineCard(Graphics g, float x, float y, float cw, DayData d, Dictionary<string, Color> colors, bool isToday)
    {
        float h = 132 * S;
        if (!InView(y, h)) return y + h;
        Card(g, new RectangleF(x, y, cw, h));
        Txt(g, "Timeline", F(15, true), T.Text, x + 20 * S, y + 18 * S);
        TextRight(g, "Hover to see what you were doing", F(12.5f), T.Muted, x + cw - 20 * S, y + 21 * S);

        double first = d.FirstActive, last = d.LastActive, nowSec = (Clock.Now - Clock.Today).TotalSeconds;
        foreach (var m in d.Manual) { first = first < 0 ? m.Start : Math.Min(first, m.Start); last = Math.Max(last, m.End); }
        if (isToday) last = Math.Max(last, nowSec);
        int startH = (int)Math.Floor(first / 3600), endH = (int)Math.Ceiling(last / 3600);
        while (endH - startH < 4) { if (endH < 24) endH++; else startH--; }
        double span = (endH - startH) * 3600.0, t0 = startH * 3600.0;
        float left = x + 20 * S, right = x + cw - 20 * S, W = right - left, top = y + 58 * S, barH = 30 * S;
        FillRound(g, new RectangleF(left, top, W, barH), 7 * S, T.Track);

        var st = g.Save();
        using (var clip = Round(new RectangleF(left, top, W, barH), 7 * S))
        {
            g.SetClip(clip, CombineMode.Intersect);
            foreach (var s in d.Segments)
            {
                float sx = left + (float)((s.Start - t0) / span) * W, ex = left + (float)((s.End - t0) / span) * W;
                Color c;
                if (!colors.TryGetValue(s.Key, out c)) c = T.Other;
                using (var b = new SolidBrush(c)) g.FillRectangle(b, sx, top, Math.Max(1f, ex - sx), barH);
            }
        }
        g.Restore(st);

        // offline work / breaks you added (hatched)
        foreach (var m in d.Manual)
        {
            float mx0 = left + (float)((m.Start - t0) / span) * W, mx1 = left + (float)((m.End - t0) / span) * W;
            if (mx1 < left || mx0 > right) continue;
            using (var hb = new HatchBrush(HatchStyle.WideUpwardDiagonal, m.IsWork ? T.Accent : T.Muted, T.Track))
                g.FillRectangle(hb, Math.Max(left, mx0), top, Math.Min(right, mx1) - Math.Max(left, mx0), barH);
        }
        // lock / sleep / idle markers
        foreach (var e in d.Events)
        {
            if (e.Kind != "lock" && e.Kind != "sleep" && e.Kind != "away" && e.Kind != "logoff") continue;
            float ex = left + (float)((e.At - t0) / span) * W;
            if (ex < left || ex > right) continue;
            using (var p = new Pen(T.Muted, 1.5f * S)) g.DrawLine(p, ex, top - 5 * S, ex, top);
        }

        if (isToday)
        {
            float nx = left + (float)((nowSec - t0) / span) * W;
            using (var p = new Pen(T.Accent, 2 * S)) g.DrawLine(p, nx, top - 4 * S, nx, top + barH + 4 * S);
        }

        int hours = endH - startH, step = hours <= 8 ? 1 : hours <= 14 ? 2 : 3;
        for (int hh = startH; hh <= endH; hh += step)
        {
            float lx = left + (float)((hh * 3600 - t0) / span) * W;
            string lab = HourName(hh);
            var sz = Measure(g, lab, F(11.5f));
            Txt(g, lab, F(11.5f), T.Muted, Math.Max(left, Math.Min(right - sz.Width, lx - sz.Width / 2)), top + barH + 8 * S);
        }

        var hitR = new RectangleF(left, top - 6 * S, W, barH + 12 * S);
        AddHit(hitR, "timeline", true);
        if (hover == "timeline")
        {
            float mx = mouse.X;
            double t = t0 + (mx - left) / W * span, tol = span / W * 3;
            Segment best = null; double bestD = double.MaxValue;
            foreach (var s in d.Segments)
            {
                double dd = t < s.Start ? s.Start - t : t > s.End ? t - s.End : 0;
                if (dd <= tol && dd < bestD) { bestD = dd; best = s; }
            }
            using (var p = new Pen(T.Text, 1f * S)) g.DrawLine(p, mx, top - 2 * S, mx, top + barH + 2 * S);
            tip = new List<string>();
            var man = d.Manual.FirstOrDefault(m => t >= m.Start && t <= m.End);
            if (man != null && (best == null || bestD > 0)) { tip.Add(Util.Time(man.Start) + " – " + Util.Time(man.End) + "   " + Util.Fmt(man.End - man.Start)); tip.Add((man.IsWork ? "Offline work: " : "Break: ") + man.Label + (man.Client.Length > 0 ? " · " + man.Client : "")); }
            else if (best == null) tip.Add(Util.Time(t) + "  ·  no activity");
            else
            {
                tip.Add(Util.Time(best.Start) + " – " + Util.Time(best.End) + "   " + Util.Fmt(best.End - best.Start));
                tip.Add(Store.Info(best.Key).Name);
                if (best.Item != null)
                {
                    tip.Add(best.Item.Title);
                    if (best.Item.Site != null) tip.Add(best.Item.Site);
                }
            }
        }
        return y + h;
    }

    List<string> HourTip(DayData d, int hr, double ht)
    {
        var l = new List<string>();
        l.Add(HourName(hr) + " – " + HourName(hr + 1) + "   " + Util.Fmt(ht));
        foreach (var a in d.Apps.Values.Where(a => a.Hours[hr] >= 30).OrderByDescending(a => a.Hours[hr]).Take(4))
            l.Add(Store.Info(a.Key).Name + "   " + Util.Fmt(a.Hours[hr]));
        if (ht < 1) l.Add("No activity");
        return l;
    }

    // Average of the previous days that have data, counting only their first `upToHour` hours (24 = whole day).
    void AverageBefore(DateTime d, int days, double upToHour, out double avg, out int n)
    {
        double sum = 0; n = 0;
        for (int i = 1; i <= days; i++)
        {
            var day = Day(d.AddDays(-i));
            if (day.Total < 60) continue;
            double t = 0;
            foreach (var a in day.Apps.Values)
                for (int h = 0; h < 24 && h < upToHour; h++) t += a.Hours[h] * Math.Min(1, upToHour - h);
            sum += t; n++;
        }
        avg = n > 0 ? sum / n : 0;
    }
}
