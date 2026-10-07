using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;

// Week view: daily average, daily bars, log-in/log-out table, categories, websites & files, apps.
partial class DashView
{
    float PaintWeek(Graphics g, int w, float y)
    {
        float M = 24 * S, cw = w - 2 * M, x = M, gap = 12 * S;
        var ws = WeekStart(Date);
        var days = new DayData[7];
        var agg = new Dictionary<string, AppUsage>(StringComparer.OrdinalIgnoreCase);
        var aggItems = new Dictionary<string, ItemUsage>(StringComparer.Ordinal);
        double total = 0; int withData = 0;
        for (int i = 0; i < 7; i++)
        {
            days[i] = Day(ws.AddDays(i));
            double t = days[i].Total;
            total += t;
            if (t >= 60) withData++;
            foreach (var a in days[i].Apps.Values)
            {
                AppUsage s;
                if (!agg.TryGetValue(a.Key, out s)) { s = new AppUsage(); s.Key = a.Key; agg[a.Key] = s; }
                s.Seconds += a.Seconds; s.Opens += a.Opens;
            }
            foreach (var it in days[i].ItemList)
            {
                string k = DayData.ItemKey(it.AppKey, it.Title);
                ItemUsage s;
                if (!aggItems.TryGetValue(k, out s)) { s = new ItemUsage(); s.AppKey = it.AppKey; s.Title = it.Title; aggItems[k] = s; }
                s.Seconds += it.Seconds; s.Opens += it.Opens;
                if (it.Site != null) s.Site = it.Site;
            }
        }
        var apps = agg.Values.Where(a => a.Seconds >= 1).OrderByDescending(a => a.Seconds).ToList();
        var itemList = aggItems.Values.ToList();
        var colors = ColorsFor(apps.Select(a => a.Key));
        badgeColors = colors;
        double avg = withData > 0 ? total / withData : 0;

        y = PausedBanner(g, x, y, cw);

        double prevTotal = 0; int prevN = 0;
        for (int i = 0; i < 7; i++) { double t = Day(ws.AddDays(i - 7)).Total; if (t >= 60) { prevTotal += t; prevN++; } }
        string sub = "Total " + Util.Fmt(total) + " across " + withData + (withData == 1 ? " day" : " days");
        y = SummaryCard(g, x, y, cw, "DAILY AVERAGE", Util.Fmt(avg), sub, false, avg, prevN > 0 ? prevTotal / prevN : 0, prevN > 0 && withData > 0,
            "last week's average", "(" + Util.Fmt(prevN > 0 ? prevTotal / prevN : 0) + " a day)", 0);
        y += gap;

        int busiest = -1; double bv = 0;
        for (int i = 0; i < 7; i++) if (days[i].Total > bv) { bv = days[i].Total; busiest = i; }
        var top = apps.Count > 0 ? apps[0] : null;
        y = Tiles(g, x, y, cw, top, top == null ? 0 : top.Seconds,
            new[] { "TOTAL", "BUSIEST DAY", "APPS USED" },
            new[] { Util.Fmt(total), busiest < 0 ? "—" : ws.AddDays(busiest).ToString("dddd", Cur), apps.Count.ToString() });
        y += gap;

        // daily bars
        float cardY = y, chartTop = y + 64 * S, chartH = 170 * S, axisW = 40 * S;
        float left = x + 20 * S + axisW, right = x + cw - 20 * S, bottom = chartTop + chartH;
        var legendKeys = apps.Take(5).Select(a => a.Key).ToList();
        bool hasOther = apps.Count > 5;
        float legendH = LegendHeight(g, legendKeys, hasOther, right - (x + 20 * S));
        float cardH = (bottom - y) + 50 * S + (legendKeys.Count > 0 ? legendH + 12 * S : 0) + 8 * S;
        if (InView(y, cardH))
        {
            Card(g, new RectangleF(x, y, cw, cardH));
            Txt(g, "Daily screen time", F(15, true), T.Text, x + 20 * S, y + 18 * S);
            TextRight(g, "Click a day to see its details", F(12.5f), T.Muted, x + cw - 20 * S, y + 21 * S);

            double maxV = Math.Max(bv, avg);
            int stepH = maxV <= 4 * 3600 ? 1 : maxV <= 8 * 3600 ? 2 : maxV <= 12 * 3600 ? 3 : 4;
            int topH = Math.Max(stepH, (int)Math.Ceiling(maxV / 3600 / stepH) * stepH);
            if (topH * 3600 < maxV) topH += stepH;
            float perSec = chartH / (topH * 3600f);
            for (int hv = 0; hv <= topH; hv += stepH)
            {
                float gy = bottom - hv * 3600 * perSec;
                using (var p = new Pen(T.Grid)) g.DrawLine(p, left, gy, right, gy);
                TextRight(g, hv + "h", F(11.5f), T.Muted, left - 8 * S, gy - 8 * S);
            }
            float slot = (right - left) / 7f, bw = Math.Min(slot * 0.5f, 56 * S);
            for (int i = 0; i < 7; i++)
            {
                var date = ws.AddDays(i);
                float sx = left + i * slot;
                bool future = date > Clock.Today;
                string id = "wday:" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var col = new RectangleF(sx, chartTop - 26 * S, slot, chartH + 64 * S);
                if (!future)
                {
                    if (hover == id) FillRound(g, col, 8 * S, T.Hover);
                    AddHit(col, id, true);
                }
                double t = days[i].Total;
                if (t >= 1)
                {
                    var segs = new List<KeyValuePair<Color, double>>();
                    double rest = t;
                    foreach (var k in legendKeys)
                    {
                        AppUsage u;
                        if (days[i].Apps.TryGetValue(k, out u) && u.Seconds > 0) { segs.Add(new KeyValuePair<Color, double>(colors[k], u.Seconds)); rest -= u.Seconds; }
                    }
                    if (rest > 0.5) segs.Add(new KeyValuePair<Color, double>(T.Other, rest));
                    StackedBar(g, sx + (slot - bw) / 2, bottom, bw, perSec, segs);
                    string vt = Util.Fmt(t);
                    var vs = Measure(g, vt, F(11.5f, true));
                    Txt(g, vt, F(11.5f, true), T.Muted, sx + slot / 2 - vs.Width / 2, bottom - (float)t * perSec - 20 * S);
                }
                bool sel = date == Date.Date;
                string dn = date.ToString("ddd", Cur), dd = date.Day.ToString();
                Color dc = future ? T.Border : sel ? T.Accent : T.Muted;
                Txt(g, dn, F(12.5f, sel), dc, sx + slot / 2 - Measure(g, dn, F(12.5f, sel)).Width / 2, bottom + 8 * S);
                Txt(g, dd, F(11.5f), dc, sx + slot / 2 - Measure(g, dd, F(11.5f)).Width / 2, bottom + 26 * S);
                if (hover == id && !future)
                {
                    tip = new List<string>();
                    tip.Add(date.ToString("dddd, d MMM", Cur) + "   " + Util.Fmt(t));
                    foreach (var a in days[i].Sorted().Take(4)) tip.Add(Store.Info(a.Key).Name + "   " + Util.Fmt(a.Seconds));
                }
            }
            if (avg > 0)
            {
                float ay = bottom - (float)avg * perSec;
                using (var p = new Pen(T.Accent, 1.5f * S)) { p.DashPattern = new[] { 4f, 3f }; g.DrawLine(p, left, ay, right, ay); }
                string at = "avg " + Util.Fmt(avg);
                var asz = Measure(g, at, F(11, true));
                var ar = new RectangleF(right - asz.Width - 10 * S, ay - 9 * S, asz.Width + 10 * S, 18 * S);
                FillRound(g, ar, 5 * S, T.Accent);
                Center(g, at, F(11, true), Color.White, ar);
            }
            if (legendKeys.Count > 0) Legend(g, legendKeys, colors, hasOther, x + 20 * S, bottom + 50 * S, right - (x + 20 * S));
        }
        y = cardY + cardH + gap;

        y = LoginTable(g, x, y, cw, ws, days) + gap;

        Func<string, List<ItemUsage>> itemsFor = k => itemList.Where(i => i.Seconds >= 1 && string.Equals(i.AppKey, k, StringComparison.OrdinalIgnoreCase))
                                                             .OrderByDescending(i => i.Seconds).ToList();
        float cy = CategoryCard(g, x, y, cw, apps, itemsFor);
        if (cy > y) y = cy + gap;
        if (itemList.Count > 0) y = SitesCards(g, x, y, cw, itemList) + gap;

        return AppList(g, x, y, cw, apps, colors, total, Math.Max(1, withData), "No screen time recorded this week yet.", itemsFor);
    }

    // One row per day: when the PC session started and ended, how long it was on, and screen time.
    float LoginTable(Graphics g, float x, float y, float cw, DateTime ws, DayData[] days)
    {
        float head = 84 * S, rowH = 36 * S, h = head + 7 * rowH + 10 * S;
        if (!InView(y, h)) return y + h;
        Card(g, new RectangleF(x, y, cw, h));
        Txt(g, "Log in & log out", F(15, true), T.Text, x + 20 * S, y + 18 * S);
        TextRight(g, "Click a day to open its activity log", F(12.5f), T.Muted, x + cw - 20 * S, y + 21 * S);
        float ix = x + 20 * S, iw = cw - 40 * S;
        float[] cx = { 0, 0.20f, 0.38f, 0.56f, 0.72f, 0.86f };
        string[] heads = { "DAY", "LOG IN", "LOG OUT", "PC ON FOR", "SCREEN TIME", "AWAY" };
        for (int c = 0; c < heads.Length; c++) Txt(g, heads[c], F(11, true), T.Muted, ix + cx[c] * iw, y + 58 * S);
        float ry = y + head;
        double nowSec = (Clock.Now - Clock.Today).TotalSeconds;
        for (int i = 0; i < 7; i++)
        {
            var date = ws.AddDays(i);
            bool future = date > Clock.Today, isToday = date == Clock.Today;
            string id = "logday:" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var row = new RectangleF(x + 8 * S, ry, cw - 16 * S, rowH);
            if (!future)
            {
                if (hover == id) FillRound(g, row, 8 * S, T.Hover);
                AddHit(row, id, true);
            }
            if (i > 0) using (var p = new Pen(T.Grid)) g.DrawLine(p, ix, ry, x + cw - 20 * S, ry);
            float ty = ry + 9 * S;
            Color dc = future ? T.Border : date == Date.Date ? T.Accent : T.Text;
            Txt(g, date.ToString("ddd d MMM", Cur), F(13, true), dc, ix, ty);
            if (!future)
            {
                var d = days[i];
                var si = Sessions.Compute(d, Sessions.Merged(d), isToday, Tracker);
                double on = si.OnFor(nowSec);
                if (si.LogIn >= 0 || d.Total >= 1)
                {
                    Txt(g, Util.Time(si.LogIn), F(13), T.Text, ix + cx[1] * iw, ty);
                    if (si.StillOn) Txt(g, "Still on", F(13, true), T.Good, ix + cx[2] * iw, ty);
                    else Txt(g, Util.Time(si.LogOut), F(13), T.Text, ix + cx[2] * iw, ty);
                    Txt(g, on > 0 ? Util.Fmt(on) : "—", F(13), T.Text, ix + cx[3] * iw, ty);
                    Txt(g, Util.Fmt(d.Total), F(13, true), T.Text, ix + cx[4] * iw, ty);
                    Txt(g, on > d.Total ? Util.Fmt(on - d.Total) : "—", F(13), T.Muted, ix + cx[5] * iw, ty);
                    if (hover == id)
                    {
                        tip = new List<string>();
                        tip.Add(date.ToString("dddd, d MMM", Cur));
                        tip.Add("Log in " + Util.Time(si.LogIn) + " – " + si.InVia);
                        tip.Add(si.StillOn ? "Still signed in and active" : "Log out " + Util.Time(si.LogOut) + " – " + si.OutVia);
                        if (si.First >= 0) tip.Add("Activity " + Util.Time(si.First) + " – " + Util.Time(si.Last));
                    }
                }
                else Txt(g, "No activity", F(13), T.Muted, ix + cx[1] * iw, ty);
            }
            ry += rowH;
        }
        return y + h;
    }
}
