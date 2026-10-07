using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

// Log view: internet-synced world clock, log-in/log-out summary, and a chronological activity log of the day.
partial class DashView
{
    class LogRow
    {
        public double At, End;
        public LogEvent Ev;
        public string Key;
        public Dictionary<ItemUsage, double> Items;
        public float H;
    }

    float PaintLog(Graphics g, int w, float y)
    {
        float M = 24 * S, cw = w - 2 * M, x = M, gap = 12 * S;
        y = PausedBanner(g, x, y, cw);
        y = WorldClockCard(g, x, y, cw) + gap;

        var d = Day(Date);
        bool isToday = Date == Clock.Today;
        var evs = Sessions.Merged(d);
        var si = Sessions.Compute(d, evs, isToday, Tracker);
        badgeColors = ColorsFor(d.Sorted().Select(a => a.Key));
        y = SessionCard(g, x, y, cw, d, si, isToday) + gap;
        return ActivityCard(g, x, y, cw, d, evs);
    }

    float WorldClockCard(Graphics g, float x, float y, float cw)
    {
        float h = 136 * S;
        if (!InView(y, h)) return y + h;
        Card(g, new RectangleF(x, y, cw, h));
        var now = Clock.Now;
        Txt(g, "INTERNET TIME", F(11.5f, true), T.Muted, x + 22 * S, y + 18 * S);
        Txt(g, now.ToString("h:mm:ss tt", System.Globalization.CultureInfo.InvariantCulture), F(34, true), T.Text, x + 20 * S, y + 34 * S);
        var tz = TimeZoneInfo.Local;
        string zone = tz.IsDaylightSavingTime(now) ? tz.DaylightName : tz.StandardName;
        Txt(g, now.ToString("dddd, d MMMM yyyy", Cur) + "  ·  " + zone, F(13), T.Text, x + 22 * S, y + 80 * S);
        Color dot = Clock.Synced ? T.Good : T.Warn;
        using (var b = new SolidBrush(dot)) g.FillEllipse(b, x + 22 * S, y + 108 * S, 8 * S, 8 * S);
        string status = Clock.Status();
        var ssz = Measure(g, status, F(12));
        Txt(g, status, F(12), T.Muted, x + 36 * S, y + 103 * S);
        var sync = new RectangleF(x + 44 * S + ssz.Width, y + 100 * S, Measure(g, "Sync now", F(12, true)).Width + 8 * S, 22 * S);
        if (sync.Right < x + cw * 0.58f)
        {
            Txt(g, "Sync now", F(12, true), hover == "sync" ? T.Text : T.Accent, sync.X + 4 * S, y + 103 * S);
            AddHit(sync, "sync", true);
        }

        // world clocks: two columns on the right
        var zones = Settings.WorldClocks.Take(6).ToList();
        float colW = 170 * S, gx = x + cw - 20 * S - colW * 2, gy = y + 20 * S;
        for (int i = 0; i < zones.Count; i++)
        {
            var t = Clock.In(zones[i]);
            if (t == null) continue;
            float cx = gx + (i % 2) * colW, cy = gy + (i / 2) * 36 * S;
            Txt(g, Clock.CityName(zones[i]), F(11.5f), T.Muted, cx, cy);
            string ts = t.Value.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture);
            Txt(g, ts, F(15, true), T.Text, cx, cy + 14 * S);
            int dayDiff = (t.Value.Date - now.Date).Days;
            if (dayDiff != 0)
                Txt(g, dayDiff > 0 ? "tomorrow" : "yesterday", F(11), T.Muted, cx + Measure(g, ts, F(15, true)).Width + 6 * S, cy + 18 * S);
        }
        return y + h;
    }

    float SessionCard(Graphics g, float x, float y, float cw, DayData d, SessionInfo si, bool isToday)
    {
        float h = 214 * S;
        if (!InView(y, h)) return y + h;
        Card(g, new RectangleF(x, y, cw, h));
        Txt(g, "Log in & log out", F(15, true), T.Text, x + 20 * S, y + 18 * S);
        TextRight(g, Date.ToString("dddd, d MMMM yyyy", Cur), F(12.5f), T.Muted, x + cw - 20 * S, y + 21 * S);
        double nowSec = (Clock.Now - Clock.Today).TotalSeconds, on = si.OnFor(nowSec);
        string[] labels = { "LOG IN", "FIRST ACTIVITY", "LAST ACTIVITY", "LOG OUT", "PC ON FOR", "SCREEN TIME" };
        string[] values =
        {
            Util.Time(si.LogIn), Util.Time(si.First), si.StillOn ? "Now" : Util.Time(si.Last),
            si.StillOn ? "Still on" : Util.Time(si.LogOut), on > 0 ? Util.Fmt(on) : "—", Util.Fmt(d.Total)
        };
        string[] notes =
        {
            si.InVia, "", "", si.StillOn ? "Signed in and active" : si.OutVia,
            on > 0 && isToday && si.StillOn ? "so far" : "", on > d.Total ? "Away / locked " + Util.Fmt(on - d.Total) : ""
        };
        float cellW = (cw - 40 * S) / 3, cellH = 76 * S;
        for (int i = 0; i < 6; i++)
        {
            float cx = x + 20 * S + (i % 3) * cellW, cy = y + 56 * S + (i / 3) * cellH;
            if (i % 3 > 0) using (var p = new Pen(T.Grid)) g.DrawLine(p, cx - 10 * S, cy + 4 * S, cx - 10 * S, cy + cellH - 10 * S);
            Txt(g, labels[i], F(11, true), T.Muted, cx, cy);
            Color vc = i == 3 && si.StillOn ? T.Good : T.Text;
            Txt(g, values[i], F(22, true), vc, cx, cy + 16 * S);
            if (notes[i].Length > 0) Txt(g, notes[i], F(12), T.Muted, cx, cy + 46 * S);
        }
        return y + h;
    }

    float ActivityCard(Graphics g, float x, float y, float cw, DayData d, List<LogEvent> evs)
    {
        // build rows: app blocks (consecutive time in one app) and events
        var rows = new List<LogRow>();
        LogRow cur = null;
        int hidden = 0;
        foreach (var s in d.Segments)
        {
            if (cur != null && cur.Key == s.Key && s.Start - cur.End <= 60) cur.End = Math.Max(cur.End, s.End);
            else
            {
                if (cur != null) rows.Add(cur);
                cur = new LogRow(); cur.At = s.Start; cur.End = s.End; cur.Key = s.Key; cur.Items = new Dictionary<ItemUsage, double>();
            }
            if (s.Item != null)
            {
                double v; cur.Items.TryGetValue(s.Item, out v);
                cur.Items[s.Item] = v + (s.End - s.Start);
            }
        }
        if (cur != null) rows.Add(cur);
        if (hideShort)
        {
            int before = rows.Count;
            rows = rows.Where(r => r.End - r.At >= 15).ToList();
            hidden = before - rows.Count;
        }
        foreach (var e in evs) { var r = new LogRow(); r.At = e.At; r.Ev = e; rows.Add(r); }
        foreach (var m in d.Manual)
        {
            var r = new LogRow(); r.At = m.Start; r.End = m.End;
            r.Ev = new LogEvent { At = m.Start, Kind = m.IsWork ? "manual" : "break", Text = (m.IsWork ? "Offline work: " : "Break: ") + m.Label + (m.Client.Length > 0 ? " · " + m.Client : "") + "  (" + Util.Time(m.Start) + " – " + Util.Time(m.End) + ", " + Util.Fmt(m.End - m.Start) + ")" };
            rows.Add(r);
        }
        rows = rows.OrderBy(r => r.At).ThenBy(r => r.Ev == null ? 1 : 0).ToList();

        float head = 60 * S, total = 0;
        foreach (var r in rows)
        {
            r.H = r.Ev != null ? 36 * S : 48 * S + Math.Min(3, r.Items.Count) * 20 * S;
            total += r.H;
        }
        float h = head + (rows.Count == 0 ? 60 * S : total) + 14 * S;
        Card(g, new RectangleF(x, y, cw, h));
        if (InView(y, head))
        {
            Txt(g, "Activity log", F(15, true), T.Text, x + 20 * S, y + 18 * S);
            string chip = hideShort ? (hidden > 0 ? "Hiding " + hidden + " quick switches (under 15 s) – show" : "Hide quick switches") : "Showing everything – hide quick switches";
            var csz = Measure(g, chip, F(12, true));
            var cr = new RectangleF(x + cw - 20 * S - csz.Width - 20 * S, y + 14 * S, csz.Width + 20 * S, 28 * S);
            FillRound(g, cr, 14 * S, hover == "short" ? T.AccentSoft : T.Track);
            Center(g, chip, F(12, true), T.Text, cr);
            AddHit(cr, "short", true);
        }
        float ry = y + head;
        if (rows.Count == 0)
        {
            Txt(g, "No activity recorded for this day.", F(13.5f), T.Muted, x + 20 * S, ry + 10 * S);
            return y + h;
        }
        float timeX = x + 20 * S, lineX = x + 112 * S, bodyX = x + 136 * S, right = x + cw - 20 * S;
        for (int i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (!InView(ry, r.H)) { ry += r.H; continue; }
            // the vertical rail
            using (var p = new Pen(T.Grid, 2 * S))
                g.DrawLine(p, lineX, i == 0 ? ry + 14 * S : ry, lineX, i == rows.Count - 1 ? ry + 14 * S : ry + r.H);
            Txt(g, Util.Time(r.At), F(12.5f, true), T.Muted, timeX, ry + 6 * S);
            if (r.Ev != null)
            {
                var c = new RectangleF(lineX - 12 * S, ry + 2 * S, 24 * S, 24 * S);
                g.FillEllipse(Brush(r.Ev.FromWindows ? T.Track : T.AccentSoft), c);
                Center(g, GlyphFor(r.Ev.Kind), Glyph(11), r.Ev.FromWindows ? T.Muted : T.Accent, c);
                Txt(g, r.Ev.Text, F(13, true), T.Text, bodyX, ry + 6 * S);
                if (r.Ev.FromWindows) Txt(g, "from Windows", F(11.5f), T.Muted, bodyX + Measure(g, r.Ev.Text, F(13, true)).Width + 8 * S, ry + 8 * S);
            }
            else
            {
                g.FillEllipse(Brush(T.Surface), lineX - 5 * S, ry + 9 * S, 10 * S, 10 * S);
                using (var p = new Pen(T.Accent, 2 * S)) g.DrawEllipse(p, lineX - 5 * S, ry + 9 * S, 10 * S, 10 * S);
                DrawAppIcon(g, r.Key, bodyX, ry + 3 * S, 22 * S);
                string name = Store.Info(r.Key).Name, dur = Util.Fmt(r.End - r.At);
                Txt(g, name, F(13.5f, true), T.Text, bodyX + 30 * S, ry + 4 * S);
                TextRight(g, dur, F(13, true), T.Text, right, ry + 4 * S);
                Txt(g, Util.Time(r.At) + " – " + Util.Time(r.End), F(12), T.Muted, bodyX + 30 * S, ry + 24 * S);
                float iy = ry + 46 * S;
                foreach (var kv in r.Items.OrderByDescending(k => k.Value).Take(3))
                {
                    string t = Util.Fmt(kv.Value);
                    var tsz = Measure(g, t, F(12));
                    Txt(g, t, F(12), T.Muted, right - tsz.Width, iy);
                    float siteW = 0;
                    if (kv.Key.Site != null)
                    {
                        siteW = Math.Min(170 * S, Measure(g, kv.Key.Site, F(12)).Width);
                        g.DrawString(kv.Key.Site, F(12), Brush(T.Muted), new RectangleF(right - tsz.Width - 14 * S - siteW, iy, siteW + 2, 18 * S), sf);
                        siteW += 14 * S;
                    }
                    g.DrawString("• " + kv.Key.Title, F(12.5f), Brush(T.Text), new RectangleF(bodyX + 30 * S, iy, right - tsz.Width - siteW - bodyX - 46 * S, 18 * S), sf);
                    string id = ItemHit(kv.Key);
                    AddHit(new RectangleF(bodyX + 26 * S, iy - 1 * S, right - bodyX - 26 * S, 20 * S), id, true);
                    iy += 20 * S;
                }
            }
            ry += r.H;
        }
        return y + h;
    }

    static string GlyphFor(string kind)
    {
        switch (kind)
        {
            case "lock": return "";
            case "unlock": return "";
            case "sleep": case "away": return "";
            case "wake": return "";
            case "back": case "logon": return "";
            case "boot": case "shutdown": case "logoff": return "";
            case "start": case "resume": return "";
            case "stop": return "";
            case "pause": return "";
            case "crash": return "";
            case "call": case "callend": return "";
            case "focus": case "focusend": case "blocked": return "";
            case "manual": return "";
            case "break": return "";
            default: return "";
        }
    }
}
