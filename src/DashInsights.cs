using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;

// Day view "Insights" card: focus score, deep work, calls, switching, comparison, summary, goals, streaks, badges, alerts.
partial class DashView
{
    float InsightsCard(Graphics g, float x, float y, float cw, DayData d)
    {
        if (d.Total < 60 && d.Manual.Count == 0) return y - 12 * S;
        var ins = Insights.ForDay(d, Day);
        var cmpDate = compareMode == "week" ? Date.AddDays(-7) : Date.AddDays(-1);
        var cd = Day(cmpDate);
        var cins = Insights.ForDay(cd, null);
        string cmpName = compareMode == "week" ? "last " + cmpDate.ToString("dddd", Cur) : "yesterday";
        bool isToday = Date == Clock.Today;

        // goals
        var goals = new List<Tuple<string, double, double, bool>>(); // label, value, target, isMax
        var cats = Categories.Totals(d.Sorted(), k => d.ItemsFor(k));
        if (Settings.DailyGoalMinutes > 0) goals.Add(Tuple.Create("Screen time", d.Total, Settings.DailyGoalMinutes * 60.0, true));
        if (Settings.GoalProductiveMin > 0) goals.Add(Tuple.Create("Productive", cats["Productive"], Settings.GoalProductiveMin * 60.0, false));
        if (Settings.GoalEntertainmentMax > 0) goals.Add(Tuple.Create("Entertainment", cats["Entertainment"], Settings.GoalEntertainmentMax * 60.0, true));
        if (Settings.GoalSocialMax > 0) goals.Add(Tuple.Create("Social media", cats["Social"], Settings.GoalSocialMax * 60.0, true));

        double share;
        string best = Insights.BestHours(Date, 28, Day, out share);
        var streaks = Streaks(Date);

        float head = 54 * S, stats = 92 * S, lineH = 22 * S;
        int sumLines = ins.Summary.Count;
        float badgeH = ins.Badges.Count > 0 ? 40 * S : 0;
        float alertH = ins.Alerts.Count * lineH;
        float goalH = goals.Count > 0 ? 30 * S + ((goals.Count + 1) / 2) * 34 * S : 0;
        float footH = (best != null || streaks.Count > 0) ? 30 * S : 0;
        float h = head + stats + sumLines * lineH + 10 * S + badgeH + alertH + goalH + footH + 14 * S;
        if (!InView(y, h)) return y + h;
        Card(g, new RectangleF(x, y, cw, h));
        Txt(g, Lang.T("Insights"), F(15, true), T.Text, x + 20 * S, y + 18 * S);

        // compare chips (right)
        float cx = x + cw - 20 * S;
        foreach (var opt in new[] { new[] { "week", "vs same day last week" }, new[] { "yesterday", "vs yesterday" } })
        {
            var sz = Measure(g, opt[1], F(12, true));
            var r = new RectangleF(cx - sz.Width - 20 * S, y + 14 * S, sz.Width + 20 * S, 28 * S);
            bool on = compareMode == opt[0];
            FillRound(g, r, 14 * S, on ? T.AccentSoft : hover == "cmp:" + opt[0] ? T.Hover : T.Track);
            Center(g, opt[1], F(12, true), on ? T.Accent : T.Muted, r);
            AddHit(r, "cmp:" + opt[0], true);
            cx = r.Left - 8 * S;
        }

        // focus score ring
        float ry = y + head, rr = 34 * S, rcx = x + 20 * S + rr, rcy = ry + 40 * S;
        using (var p = new Pen(T.Track, 8 * S)) g.DrawEllipse(p, rcx - rr, rcy - rr, rr * 2, rr * 2);
        Color sc = ins.FocusScore >= 70 ? T.Good : ins.FocusScore >= 45 ? T.Warn : T.Danger;
        if (d.Total >= 600)
            using (var p = new Pen(sc, 8 * S)) { p.StartCap = p.EndCap = LineCap.Round; g.DrawArc(p, rcx - rr, rcy - rr, rr * 2, rr * 2, -90, Math.Max(1, 360f * ins.FocusScore / 100)); }
        Center(g, d.Total >= 600 ? ins.FocusScore.ToString() : "—", F(22, true), T.Text, new RectangleF(rcx - rr, rcy - rr - 4 * S, rr * 2, rr * 2));
        Center(g, Lang.T("Focus score"), F(10.5f), T.Muted, new RectangleF(rcx - rr - 10 * S, rcy + 8 * S, rr * 2 + 20 * S, 16 * S));
        if (InView(rcy - rr, rr * 2) && new RectangleF(rcx - rr, rcy - rr, rr * 2, rr * 2).Contains(mouse.X, mouse.Y + scrollY))
            tip = new List<string> { "Focus score " + ins.FocusScore + "/100", "50% deep work share, 25% low app switching,", "25% productive share of your screen time." };

        string[] lab = { Lang.T("Deep work").ToUpperInvariant(), "PRODUCTIVE", "CALLS / MEETINGS", "SWITCHES / HOUR" };
        string[] val = { Util.Fmt(ins.DeepWork), d.Total >= 60 ? Math.Round(ins.Productive / d.Total * 100) + "%" : "—", Util.Fmt(d.MeetingSeconds), Math.Round(ins.SwitchesPerHour).ToString() };
        string[] sub =
        {
            ins.DeepBlocks.Count + (ins.DeepBlocks.Count == 1 ? " block" : " blocks") + " of 25+ min",
            Util.Fmt(ins.Productive),
            "mic in use",
            ins.Switches + " switches today"
        };
        double[] now = { ins.DeepWork, ins.Productive, d.MeetingSeconds, ins.SwitchesPerHour };
        double[] then = { cins.DeepWork, cins.Productive, cd.MeetingSeconds, cins.SwitchesPerHour };
        float sx0 = x + 20 * S + rr * 2 + 28 * S, sw = (x + cw - 20 * S - sx0) / 4;
        for (int i = 0; i < 4; i++)
        {
            float sx = sx0 + i * sw;
            Txt(g, lab[i], F(11, true), T.Muted, sx, ry + 4 * S);
            Txt(g, val[i], F(22, true), T.Text, sx, ry + 20 * S);
            Txt(g, sub[i], F(11.5f), T.Muted, sx, ry + 50 * S);
            if (cd.Total >= 600)
            {
                // when comparing today (partial) with a full day, compare like for like
                double t = isToday && i < 3 ? then[i] * SharePastBy(cd, Clock.Now.TimeOfDay.TotalSeconds) : then[i];
                double delta = now[i] - t;
                bool better = i == 3 ? delta < 0 : delta > 0;
                if (Math.Abs(delta) >= (i == 3 ? 1 : 60))
                {
                    string ds = (delta > 0 ? "▲ " : "▼ ") + (i == 3 ? Math.Abs(Math.Round(delta)).ToString() : Util.Fmt(Math.Abs(delta))) + " vs " + cmpName;
                    g.DrawString(ds, F(11), Brush(better ? T.Good : T.Warn), new RectangleF(sx, ry + 68 * S, sw - 6 * S, 16 * S), sf);
                }
                else Txt(g, "≈ same as " + cmpName, F(11), T.Muted, sx, ry + 68 * S);
            }
        }

        float yy = ry + stats;
        foreach (var l in ins.Summary)
        {
            g.DrawString(l, F(13), Brush(T.Text), new RectangleF(x + 20 * S, yy, cw - 40 * S, lineH), sf);
            yy += lineH;
        }
        yy += 10 * S;

        if (ins.Badges.Count > 0)
        {
            float bx = x + 20 * S;
            foreach (var b in ins.Badges)
            {
                string name = b.Split('–')[0].Trim();
                var bs = Measure(g, "★ " + name, F(12, true));
                if (bx + bs.Width + 22 * S > x + cw - 20 * S) break;
                var br = new RectangleF(bx, yy, bs.Width + 22 * S, 28 * S);
                bool warnBadge = name.StartsWith("Marathon") || name.StartsWith("Night");
                FillRound(g, br, 14 * S, Blend(warnBadge ? T.Warn : T.Good, T.Surface, 0.85f));
                Center(g, "★ " + name, F(12, true), warnBadge ? T.Warn : T.Good, br);
                if (br.Contains(mouse.X, mouse.Y + scrollY)) tip = new List<string> { b };
                bx = br.Right + 8 * S;
            }
            yy += badgeH;
        }
        foreach (var a in ins.Alerts)
        {
            g.DrawString("⚠  " + a, F(12.5f), Brush(T.Warn), new RectangleF(x + 20 * S, yy, cw - 40 * S, lineH), sf);
            yy += lineH;
        }

        if (goals.Count > 0)
        {
            Txt(g, "GOALS", F(11, true), T.Muted, x + 20 * S, yy + 6 * S);
            yy += 30 * S;
            float gw = (cw - 52 * S) / 2;
            for (int i = 0; i < goals.Count; i++)
            {
                var gl = goals[i];
                float gx = x + 20 * S + (i % 2) * (gw + 12 * S), gy = yy + (i / 2) * 34 * S;
                double f = gl.Item2 / gl.Item3;
                bool ok = gl.Item4 ? f <= 1 : f >= 1;
                string txt = gl.Item1 + (gl.Item4 ? " ≤ " : " ≥ ") + Util.Fmt(gl.Item3) + ":  " + Util.Fmt(gl.Item2) + (ok ? (gl.Item4 ? "  ✓" : "  ✓ done") : (gl.Item4 ? "  – over" : ""));
                Txt(g, txt, F(12.5f), ok ? T.Text : gl.Item4 ? T.Danger : T.Text, gx, gy);
                FillRound(g, new RectangleF(gx, gy + 20 * S, gw, 5 * S), 2.5f * S, T.Track);
                Color gc = gl.Item4 ? (f > 1 ? T.Danger : f > 0.8 ? T.Warn : T.Good) : (f >= 1 ? T.Good : T.Accent);
                FillRound(g, new RectangleF(gx, gy + 20 * S, Math.Max(5 * S, (float)Math.Min(1, f) * gw), 5 * S), 2.5f * S, gc);
            }
            yy += ((goals.Count + 1) / 2) * 34 * S;
        }

        if (footH > 0)
        {
            var parts = new List<string>();
            if (best != null) parts.Add("Your best hours: " + best + " (" + Math.Round(share * 100) + "% of your productive time, last 4 weeks)");
            parts.AddRange(streaks);
            g.DrawString(string.Join("   ·   ", parts), F(12.5f), Brush(T.Muted), new RectangleF(x + 20 * S, yy + 6 * S, cw - 40 * S, 20 * S), sf);
        }
        return y + h;
    }

    // Share of a day's screen time that happened before `sec` (to compare a partial today fairly).
    static double SharePastBy(DayData d, double sec)
    {
        double total = 0, before = 0;
        foreach (var s in d.Segments)
        {
            double len = s.End - s.Start;
            total += len;
            if (s.End <= sec) before += len; else if (s.Start < sec) before += sec - s.Start;
        }
        return total > 0 ? before / total : 1;
    }

    // "🔥 5-day streak" style lines, looking back from `date`.
    List<string> Streaks(DateTime date)
    {
        var r = new List<string>();
        Func<Func<DayData, bool>, int> count = test =>
        {
            int n = 0;
            for (int i = date == Clock.Today ? 1 : 0; i < 60; i++)
            {
                var d = Day(date.AddDays(-i));
                if (d.Total < 600) { if (i == 0) continue; break; }
                if (!test(d)) break;
                n++;
            }
            return n;
        };
        int deep = count(d => Insights.ForDay(d, null).DeepWork >= 3600);
        if (deep >= 2) r.Add("🔥 " + deep + "-day deep-work streak (1h+)");
        if (Settings.DailyGoalMinutes > 0)
        {
            int g = count(d => d.Total <= Settings.DailyGoalMinutes * 60);
            if (g >= 2) r.Add("🔥 " + g + " days within your goal");
        }
        if (Settings.GoalSocialMax > 0)
        {
            int s = count(d => Categories.Totals(d.Sorted(), k => d.ItemsFor(k))["Social"] <= Settings.GoalSocialMax * 60);
            if (s >= 2) r.Add("🔥 " + s + " days under your social-media limit");
        }
        return r;
    }
}
