using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

// The whole dashboard is drawn by hand: a fixed header (Day/Week/Log switch, date navigation, settings)
// over a scrollable page of cards. Views live in DashDay.cs, DashWeek.cs and DashLog.cs.
partial class DashView : Control
{
    public Tracker Tracker;
    public int Mode; // 0 = Day, 1 = Week, 2 = Month, 3 = Log, 4 = Work
    public DateTime Date = Clock.Today;
    public event Action OpenSettings, TogglePause;
    public event Action<string> EditLimit;
    public event Action OpenSearch, ManageProjects, StopFocus;
    public event Action<int> StartFocus;
    public event Action<int, DateTime> ExportPdf;             // mode, date
    public event Action<DateTime, ManualEntry> EditManual;     // entry null = add new
    public event Action<DateTime> CopyNote, Timesheet;
    public event Action<DateTime, DateTime, string> MakeWrapped;

    float S = 1f;
    int scrollY, contentH;
    float viewTop, viewBottom;
    Point mouse = new Point(-9999, -9999);
    string hover;
    bool dragThumb, hideShort = true;
    string compareMode = "yesterday";
    int dragY0, dragScroll0;
    RectangleF thumbRect;
    List<string> tip;
    Dictionary<string, Color> badgeColors;

    class Hit { public RectangleF R; public string Id; }
    readonly List<Hit> hits = new List<Hit>();
    readonly List<ItemUsage> tipItems = new List<ItemUsage>();
    readonly HashSet<string> expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<DateTime, DayData> cache = new Dictionary<DateTime, DayData>();
    readonly Dictionary<string, Bitmap> icons = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
    readonly StringFormat sf;
    Theme T { get { return Theme.Current; } }
    float HeaderH { get { return 64 * S; } }
    static readonly CultureInfo Cur = CultureInfo.CurrentCulture;

    public DashView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        sf = (StringFormat)StringFormat.GenericTypographic.Clone();
        sf.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
        sf.Trimming = StringTrimming.EllipsisCharacter;
    }

    public void SetScale(float s) { S = s; ClearFonts(); }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        using (var g = CreateGraphics()) SetScale(g.DpiX / 96f);
    }

    public void ClearCache() { cache.Clear(); }

    void ClearFonts()
    {
        foreach (var f in fonts.Values) f.Dispose();
        fonts.Clear();
    }

    Font F(float px, bool bold = false, string family = null)
    {
        string fam = family ?? (bold ? "Segoe UI Semibold" : "Segoe UI");
        string k = fam + px;
        Font f;
        if (!fonts.TryGetValue(k, out f)) { f = new Font(fam, px * S, FontStyle.Regular, GraphicsUnit.Pixel); fonts[k] = f; }
        return f;
    }

    Font Glyph(float px) { return F(px, false, "Segoe MDL2 Assets"); }

    DayData Day(DateTime d)
    {
        d = d.Date;
        if (Tracker != null && d == Tracker.Today.Date) return DataTools.WithOtherPcs(Tracker.Today);
        if (d > Clock.Today) { var e = new DayData(); e.Date = d; return e; }
        DayData r;
        if (!cache.TryGetValue(d, out r)) { r = Store.ViewDay(d); cache[d] = r; }
        return r;
    }

    static DateTime WeekStart(DateTime d) { return d.Date.AddDays(-(((int)d.DayOfWeek + 6) % 7)); }

    bool AtCurrent
    {
        get
        {
            if (Mode == 1) return WeekStart(Date) >= WeekStart(Clock.Today);
            if (Mode == 2) return Date.Year * 12 + Date.Month >= Clock.Today.Year * 12 + Clock.Today.Month;
            return Date >= Clock.Today;
        }
    }

    bool ActiveNow { get { return Tracker != null && !Tracker.Paused && !Tracker.Locked && !Tracker.Idle && Tracker.CurrentKey != null; } }

    // ================= painting =================

    protected override void OnPaint(PaintEventArgs e)
    {
        PaintAll(e.Graphics, Width, Height);
        int max = Math.Max(0, contentH - Height);
        if (scrollY > max) { scrollY = max; Invalidate(); }
    }

    public Bitmap RenderFull(int width)
    {
        scrollY = 0;
        using (var tmp = new Bitmap(width, 10))
        using (var g = Graphics.FromImage(tmp)) PaintAll(g, width, 100000);
        var bmp = new Bitmap(width, contentH);
        using (var g = Graphics.FromImage(bmp)) PaintAll(g, width, contentH);
        return bmp;
    }

    void PaintAll(Graphics g, int w, int h)
    {
        hits.Clear();
        tipItems.Clear();
        tip = null;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(T.Bg);
        viewTop = scrollY + HeaderH;
        viewBottom = scrollY + h;

        var state = g.Save();
        g.TranslateTransform(0, -scrollY);
        float y = HeaderH + 16 * S;
        switch (Mode)
        {
            case 1: y = PaintWeek(g, w, y); break;
            case 2: y = PaintMonth(g, w, y); break;
            case 3: y = PaintLog(g, w, y); break;
            case 4: y = PaintWork(g, w, y); break;
            default: y = PaintDay(g, w, y); break;
        }
        contentH = (int)(y + 24 * S);
        g.Restore(state);

        PaintHeader(g, w);
        if (contentH > h) PaintScrollbar(g, w, h);
        if (tip == null && hover != null && hover.StartsWith("itm:"))
        {
            int i;
            if (int.TryParse(hover.Substring(4), out i) && i < tipItems.Count)
            {
                var it = tipItems[i];
                tip = new List<string> { it.Title, (it.Site ?? Store.Info(it.AppKey).Name) + "   " + Util.Fmt(it.Seconds) };
            }
        }
        if (tip != null) PaintTip(g, w, h);
    }

    bool InView(float y, float height) { return y + height >= viewTop && y <= viewBottom; }

    void AddHit(RectangleF r, string id, bool content)
    {
        var h = new Hit();
        h.R = content ? new RectangleF(r.X, r.Y - scrollY, r.Width, r.Height) : r;
        if (content && h.R.Bottom < HeaderH) return;
        if (content && h.R.Top < HeaderH) h.R = RectangleF.FromLTRB(h.R.Left, HeaderH, h.R.Right, h.R.Bottom);
        h.Id = id;
        hits.Add(h);
    }

    string ItemHit(ItemUsage it)
    {
        tipItems.Add(it);
        return "itm:" + (tipItems.Count - 1);
    }

    // ---------- header ----------

    void PaintHeader(Graphics g, int w)
    {
        float hh = HeaderH, M = 24 * S;
        using (var b = new SolidBrush(T.Surface)) g.FillRectangle(b, 0, 0, w, hh);
        using (var p = new Pen(T.Border)) g.DrawLine(p, 0, hh - 0.5f, w, hh - 0.5f);

        float xr = w - M, cy = hh / 2, bh = 34 * S;

        var gear = new RectangleF(xr - bh, cy - bh / 2, bh, bh);
        Button(g, gear, "settings", true);
        Center(g, "", Glyph(15), T.Text, gear);
        var pdf = new RectangleF(gear.Left - 6 * S - bh, cy - bh / 2, bh, bh);
        Button(g, pdf, "pdf", true);
        Center(g, "", Glyph(15), T.Text, pdf);
        var search = new RectangleF(pdf.Left - 6 * S - bh, cy - bh / 2, bh, bh);
        Button(g, search, "search", true);
        Center(g, "", Glyph(14), T.Text, search);
        if (hover == "pdf") tip = new List<string> { "Export a PDF report" };
        else if (hover == "search") tip = new List<string> { "Search your history (Ctrl+F)" };
        xr = search.Left - 10 * S;

        string todayTxt = Lang.T(Mode == 1 ? "This week" : Mode == 2 ? "This month" : "Today");
        float tw = Measure(g, todayTxt, F(13, true)).Width + 26 * S;
        var todayR = new RectangleF(xr - tw, cy - bh / 2, tw, bh);
        bool todayOn = !AtCurrent;
        Button(g, todayR, "today", todayOn);
        Center(g, todayTxt, F(13, true), todayOn ? T.Text : T.Muted, todayR);
        xr = todayR.Left - 12 * S;

        var next = new RectangleF(xr - bh, cy - bh / 2, bh, bh);
        Button(g, next, "next", !AtCurrent);
        Center(g, "", Glyph(12), AtCurrent ? T.Border : T.Text, next);
        float lw = 150 * S;
        var lab = new RectangleF(next.Left - lw, cy - bh / 2, lw, bh);
        Center(g, DateLabel(), F(14.5f, true), T.Text, lab);
        var prev = new RectangleF(lab.Left - bh, cy - bh / 2, bh, bh);
        Button(g, prev, "prev", true);
        Center(g, "", Glyph(12), T.Text, prev);
        xr = prev.Left - 14 * S;

        string[] modes = { Lang.T("Day"), Lang.T("Week"), Lang.T("Month"), Lang.T("Log"), Lang.T("Work") };
        float segW = 56 * S, segH = 34 * S;
        var seg = new RectangleF(xr - segW * 5 - 8 * S, cy - segH / 2, segW * 5 + 8 * S, segH);
        FillRound(g, seg, 9 * S, T.Track);
        for (int i = 0; i < 5; i++)
        {
            var r = new RectangleF(seg.X + 4 * S + i * segW, seg.Y + 4 * S, segW, segH - 8 * S);
            bool on = i == Mode;
            if (on) FillRound(g, r, 7 * S, T.Surface);
            else if (hover == "mode:" + i) FillRound(g, r, 7 * S, T.Hover);
            Center(g, modes[i], F(13, true), on ? T.Text : T.Muted, r);
            AddHit(r, "mode:" + i, false);
        }

        float lx = M, ls = 30 * S;
        DrawLogo(g, lx, cy - ls / 2, ls);
        if (seg.Left - (lx + ls + 10 * S) > 120 * S)
            Txt(g, Lang.T("Screen Time"), F(17, true), T.Text, lx + ls + 10 * S, cy - 12 * S);
    }

    string DateLabel()
    {
        if (Mode == 1)
        {
            var a = WeekStart(Date); var b = a.AddDays(6);
            return a.ToString("d MMM", Cur) + " – " + b.ToString("d MMM", Cur);
        }
        if (Mode == 2) return Date.ToString("MMMM yyyy", Cur);
        if (Date == Clock.Today) return Lang.T("Today");
        if (Date == Clock.Today.AddDays(-1)) return Lang.T("Yesterday");
        return Date.ToString("ddd, d MMM yyyy", Cur);
    }

    void Button(Graphics g, RectangleF r, string id, bool enabled)
    {
        if (enabled && hover == id) FillRound(g, r, 9 * S, T.Hover);
        using (var p = new Pen(T.Border)) using (var path = Round(r, 9 * S)) g.DrawPath(p, path);
        if (enabled) AddHit(r, id, false);
    }

    void DrawLogo(Graphics g, float x, float y, float s)
    {
        using (var path = Round(new RectangleF(x, y, s, s), s * 0.26f))
        using (var br = new LinearGradientBrush(new PointF(x, y), new PointF(x + s, y + s), Color.FromArgb(92, 110, 255), Color.FromArgb(20, 184, 166)))
            g.FillPath(br, path);
        float c = x + s / 2, cyy = y + s / 2, r = s * 0.31f;
        g.FillEllipse(Brushes.White, c - r, cyy - r, r * 2, r * 2);
        using (var wb = new SolidBrush(Color.FromArgb(255, 190, 60))) g.FillPie(wb, c - r * 0.82f, cyy - r * 0.82f, r * 1.64f, r * 1.64f, -90, 125);
        using (var pen = new Pen(Color.FromArgb(40, 48, 90), s * 0.07f))
        {
            pen.StartCap = pen.EndCap = LineCap.Round;
            g.DrawLine(pen, c, cyy, c, cyy - r * 0.62f);
            g.DrawLine(pen, c, cyy, c + r * 0.48f, cyy + r * 0.18f);
        }
    }

    // ---------- shared cards ----------

    float PausedBanner(Graphics g, float x, float y, float cw)
    {
        if (Tracker != null && Tracker.Focusing)
        {
            var fr = new RectangleF(x, y, cw, 46 * S);
            FillRound(g, fr, 12 * S, T.AccentSoft);
            Txt(g, "", Glyph(14), T.Accent, x + 18 * S, y + 15 * S);
            var left = Tracker.FocusUntil - Clock.Now;
            string msg = "Focus mode – " + (int)left.TotalMinutes + ":" + left.Seconds.ToString("00") + " left · blocking " + string.Join(", ", Settings.FocusBlockCategories.Select(Lang.T));
            Txt(g, msg, F(13.5f, true), T.Text, x + 44 * S, y + 13 * S);
            string st = "Stop";
            var ssz = Measure(g, st, F(13.5f, true));
            var sb = new RectangleF(x + cw - ssz.Width - 40 * S, y + 8 * S, ssz.Width + 24 * S, 30 * S);
            FillRound(g, sb, 8 * S, hover == "stopfocus" ? Blend(T.Accent, Color.Black, 0.15f) : T.Accent);
            Center(g, st, F(13.5f, true), Color.White, sb);
            AddHit(sb, "stopfocus", true);
            y += fr.Height + 12 * S;
        }
        if (Tracker == null || !Tracker.Paused) return y;
        var r = new RectangleF(x, y, cw, 46 * S);
        FillRound(g, r, 12 * S, T.AccentSoft);
        Txt(g, "", Glyph(14), T.Accent, x + 18 * S, y + 15 * S);
        Txt(g, "Tracking is paused – screen time is not being recorded.", F(13.5f, true), T.Text, x + 44 * S, y + 13 * S);
        string rs = "Resume";
        var sz = Measure(g, rs, F(13.5f, true));
        var br = new RectangleF(x + cw - sz.Width - 40 * S, y + 8 * S, sz.Width + 24 * S, 30 * S);
        FillRound(g, br, 8 * S, hover == "pause" ? Blend(T.Accent, Color.Black, 0.15f) : T.Accent);
        Center(g, rs, F(13.5f, true), Color.White, br);
        AddHit(br, "pause", true);
        return y + r.Height + 12 * S;
    }

    float SummaryCard(Graphics g, float x, float y, float cw, string label, string big, string sub, bool live,
                      double value, double compareTo, bool canCompare, string compareName, string compareDetail, double goalSec)
    {
        float h = (goalSec > 0 ? 164 : 128) * S;
        Card(g, new RectangleF(x, y, cw, h));
        Txt(g, label, F(11.5f, true), T.Muted, x + 22 * S, y + 20 * S);
        Txt(g, big, F(42, true), T.Text, x + 20 * S, y + 36 * S);
        float sy = y + 94 * S, sx = x + 22 * S;
        if (live && Tracker != null)
        {
            Color dot = Tracker.Paused ? T.Warn : ActiveNow ? T.Good : T.Muted;
            using (var b = new SolidBrush(dot)) g.FillEllipse(b, sx, sy + 5 * S, 8 * S, 8 * S);
            string st = Tracker.State;
            if (st.Length > 34) st = st.Substring(0, 33) + "…";
            Txt(g, st, F(13), T.Text, sx + 14 * S, sy);
            sx += 14 * S + Measure(g, st, F(13)).Width + 12 * S;
            Txt(g, "·", F(13), T.Muted, sx - 7 * S, sy);
        }
        g.DrawString(sub, F(13), Brush(T.Muted), new RectangleF(sx, sy, x + cw * 0.66f - sx, 20 * S), sf);

        if (canCompare && compareTo >= 60)
        {
            double pct = (value - compareTo) / compareTo * 100;
            bool up = pct >= 0, same = Math.Abs(pct) < 1;
            string chip = same ? "≈ same" : (up ? "▲ " : "▼ ") + Math.Abs(Math.Round(pct)).ToString("0") + "%";
            Color c = same ? T.Muted : up ? T.Warn : T.Good;
            var csz = Measure(g, chip, F(15, true));
            var cr = new RectangleF(x + cw - 22 * S - csz.Width - 22 * S, y + 36 * S, csz.Width + 22 * S, 32 * S);
            FillRound(g, cr, 16 * S, Blend(c, T.Surface, 0.86f));
            Center(g, chip, F(15, true), c, cr);
            string l1 = same ? "as " + compareName : (up ? "above " : "below ") + compareName;
            TextRight(g, l1, F(12.5f), T.Muted, x + cw - 22 * S, y + 76 * S);
            TextRight(g, compareDetail, F(12.5f), T.Muted, x + cw - 22 * S, y + 94 * S);
        }

        if (goalSec > 0)
        {
            float gy = y + 128 * S, gw = cw - 44 * S;
            double frac = value / goalSec;
            Color gc = frac >= 1 ? T.Danger : frac >= 0.8 ? T.Warn : T.Good;
            string gt = "Daily goal " + Util.Fmt(goalSec) + " · " + Math.Round(frac * 100) + "% used" + (frac >= 1 ? " – over by " + Util.Fmt(value - goalSec) : "");
            Txt(g, gt, F(12), frac >= 1 ? T.Danger : T.Muted, x + 22 * S, gy - 4 * S);
            FillRound(g, new RectangleF(x + 22 * S, gy + 16 * S, gw, 6 * S), 3 * S, T.Track);
            FillRound(g, new RectangleF(x + 22 * S, gy + 16 * S, Math.Max(6 * S, (float)Math.Min(1, frac) * gw), 6 * S), 3 * S, gc);
        }
        return y + h;
    }

    float Tiles(Graphics g, float x, float y, float cw, AppUsage top, double topSec, string[] labels, string[] values)
    {
        float gap = 12 * S, th = 88 * S, tw = (cw - gap * 3) / 4;
        var r0 = new RectangleF(x, y, tw, th);
        Card(g, r0);
        Txt(g, "MOST USED", F(11, true), T.Muted, x + 16 * S, y + 16 * S);
        if (top != null)
        {
            float isz = 22 * S;
            DrawAppIcon(g, top.Key, x + 16 * S, y + 40 * S, isz);
            var nameR = new RectangleF(x + 16 * S + isz + 8 * S, y + 38 * S, tw - isz - 40 * S, 22 * S);
            g.DrawString(Store.Info(top.Key).Name, F(14, true), Brush(T.Text), nameR, sf);
            Txt(g, Util.Fmt(topSec), F(12.5f), T.Muted, x + 16 * S + isz + 8 * S, y + 58 * S);
        }
        else Txt(g, "—", F(24, true), T.Text, x + 16 * S, y + 38 * S);

        for (int i = 0; i < 3; i++)
        {
            var r = new RectangleF(x + (tw + gap) * (i + 1), y, tw, th);
            Card(g, r);
            Txt(g, labels[i], F(11, true), T.Muted, r.X + 16 * S, y + 16 * S);
            var vr = new RectangleF(r.X + 14 * S, y + 36 * S, tw - 28 * S, 36 * S);
            g.DrawString(values[i], F(24, true), Brush(T.Text), vr, sf);
        }
        return y + th;
    }

    // Category split: one stacked bar plus a legend with times.
    float CategoryCard(Graphics g, float x, float y, float cw, List<AppUsage> apps, Func<string, List<ItemUsage>> itemsFor)
    {
        var totals = Categories.Totals(apps, itemsFor);
        double sum = totals.Values.Sum();
        if (sum < 1) return y;
        var cats = Categories.Names.Where(n => totals[n] >= 1).OrderByDescending(n => totals[n]).ToList();
        int cols = 3, rows = (cats.Count + cols - 1) / cols;
        float h = 96 * S + rows * 28 * S + 30 * S;
        Card(g, new RectangleF(x, y, cw, h));
        Txt(g, "Categories", F(15, true), T.Text, x + 20 * S, y + 18 * S);
        double prod = totals["Productive"] / sum * 100;
        TextRight(g, "Productive share " + Math.Round(prod) + "%", F(13, true), prod >= 50 ? T.Good : T.Muted, x + cw - 20 * S, y + 20 * S);

        float bx = x + 20 * S, bw = cw - 40 * S, by = y + 56 * S, bh = 14 * S;
        var st = g.Save();
        using (var clip = Round(new RectangleF(bx, by, bw, bh), 7 * S))
        {
            g.SetClip(clip, CombineMode.Intersect);
            float cx = bx;
            foreach (var c in cats)
            {
                float sw = (float)(totals[c] / sum) * bw;
                using (var b = new SolidBrush(Categories.ColorFor(c, T))) g.FillRectangle(b, cx, by, sw + 0.5f, bh);
                cx += sw;
            }
        }
        g.Restore(st);

        float colW = bw / cols, ly = by + bh + 18 * S;
        for (int i = 0; i < cats.Count; i++)
        {
            float cx = bx + (i % cols) * colW, cy = ly + (i / cols) * 28 * S;
            FillRound(g, new RectangleF(cx, cy + 4 * S, 10 * S, 10 * S), 3 * S, Categories.ColorFor(cats[i], T));
            Txt(g, cats[i], F(13, true), T.Text, cx + 16 * S, cy);
            string v = Util.Fmt(totals[cats[i]]) + "  ·  " + Math.Round(totals[cats[i]] / sum * 100) + "%";
            TextRight(g, v, F(12.5f), T.Muted, cx + colW - 16 * S, cy + 1 * S);
        }
        Txt(g, "Click an app below, or a website, to change its category.", F(11.5f), T.Muted, bx, y + h - 26 * S);
        return y + h;
    }

    // Websites (browser time grouped by site) next to the most-used pages, sheets and files.
    float SitesCards(Graphics g, float x, float y, float cw, List<ItemUsage> items)
    {
        float gap = 12 * S, colW = (cw - gap) / 2, rowH = 34 * S, head = 52 * S;
        var sites = new Dictionary<string, double>();
        foreach (var it in items)
        {
            if (it.Seconds < 1 || !Browser.Is(it.AppKey)) continue;
            string k = it.Title == Browser.PrivateLabel ? "Private windows" : it.Site ?? "Other pages";
            double v; sites.TryGetValue(k, out v); sites[k] = v + it.Seconds;
        }
        var topSites = sites.OrderByDescending(kv => kv.Value).Take(8).ToList();
        var topItems = items.Where(i => i.Seconds >= 1).OrderByDescending(i => i.Seconds).Take(8).ToList();
        int n = Math.Max(1, Math.Max(topSites.Count, topItems.Count));
        float h = head + n * rowH + 12 * S;
        if (!InView(y, h)) return y + h;

        // websites
        Card(g, new RectangleF(x, y, colW, h));
        Txt(g, "Websites", F(15, true), T.Text, x + 20 * S, y + 18 * S);
        float ry = y + head;
        if (topSites.Count == 0) Txt(g, "No browser use recorded.", F(13), T.Muted, x + 20 * S, ry + 6 * S);
        double maxS = topSites.Count > 0 ? topSites[0].Value : 1;
        foreach (var kv in topSites)
        {
            bool real = kv.Key != "Other pages" && kv.Key != "Private windows";
            string cat = real ? Categories.ForSite(kv.Key) : "Browsing";
            var row = new RectangleF(x + 8 * S, ry, colW - 16 * S, rowH);
            string id = real ? "sitecat:" + kv.Key : null;
            if (id != null) { if (hover == id) FillRound(g, row, 8 * S, T.Hover); AddHit(row, id, true); }
            FillRound(g, new RectangleF(x + 20 * S, ry + 12 * S, 10 * S, 10 * S), 3 * S, Categories.ColorFor(cat, T));
            string time = Util.Fmt(kv.Value);
            var tsz = Measure(g, time, F(13, true));
            Txt(g, time, F(13, true), T.Text, x + colW - 20 * S - tsz.Width, ry + 8 * S);
            var csz = Measure(g, cat, F(11.5f));
            Txt(g, cat, F(11.5f), T.Muted, x + colW - 32 * S - tsz.Width - csz.Width, ry + 10 * S);
            g.DrawString(kv.Key, F(13), Brush(T.Text), new RectangleF(x + 38 * S, ry + 8 * S, colW - 90 * S - tsz.Width - csz.Width, 20 * S), sf);
            FillRound(g, new RectangleF(x + 38 * S, ry + rowH - 5 * S, (float)(kv.Value / maxS) * (colW - 58 * S), 2.5f * S), 1.2f * S, Blend(Categories.ColorFor(cat, T), T.Surface, 0.35f));
            ry += rowH;
        }

        // pages & files
        float x2 = x + colW + gap;
        Card(g, new RectangleF(x2, y, colW, h));
        Txt(g, "Pages, sheets & files", F(15, true), T.Text, x2 + 20 * S, y + 18 * S);
        ry = y + head;
        if (topItems.Count == 0)
            Txt(g, Settings.RecordTitles ? "No window titles recorded." : "Window titles are turned off in Settings.", F(13), T.Muted, x2 + 20 * S, ry + 6 * S);
        foreach (var it in topItems)
        {
            var row = new RectangleF(x2 + 8 * S, ry, colW - 16 * S, rowH);
            string id = ItemHit(it);
            if (hover == id) FillRound(g, row, 8 * S, T.Hover);
            AddHit(row, id, true);
            DrawAppIcon(g, it.AppKey, x2 + 18 * S, ry + 8 * S, 18 * S);
            string time = Util.Fmt(it.Seconds);
            var tsz = Measure(g, time, F(13, true));
            Txt(g, time, F(13, true), T.Text, x2 + colW - 20 * S - tsz.Width, ry + 8 * S);
            g.DrawString(it.Title, F(13), Brush(T.Text), new RectangleF(x2 + 44 * S, ry + 8 * S, colW - 76 * S - tsz.Width, 20 * S), sf);
            ry += rowH;
        }
        return y + h;
    }

    float AppList(Graphics g, float x, float y, float cw, List<AppUsage> apps, Dictionary<string, Color> colors,
                  double total, int avgDays, string empty, Func<string, List<ItemUsage>> itemsFor)
    {
        float rowH = 62 * S, head = 56 * S;
        var expH = new float[apps.Count];
        var lists = new List<ItemUsage>[apps.Count];
        float body = 0;
        for (int i = 0; i < apps.Count; i++)
        {
            if (expanded.Contains(apps[i].Key)) { lists[i] = itemsFor(apps[i].Key); expH[i] = ExpandedHeight(lists[i].Count); }
            body += rowH + expH[i];
        }
        float h = head + (apps.Count == 0 ? 70 * S : body) + 10 * S;
        Card(g, new RectangleF(x, y, cw, h));
        Txt(g, "Apps", F(15, true), T.Text, x + 20 * S, y + 18 * S);
        TextRight(g, apps.Count == 0 ? "" : "Click an app to see its pages & files, set a limit or change its category", F(12.5f), T.Muted, x + cw - 20 * S, y + 21 * S);
        float ry = y + head;
        if (apps.Count == 0)
        {
            Txt(g, empty, F(13.5f), T.Muted, x + 20 * S, ry + 16 * S);
            return y + h;
        }
        double max = apps[0].Seconds;
        for (int i = 0; i < apps.Count; i++)
        {
            var a = apps[i];
            if (!InView(ry, rowH + expH[i])) { ry += rowH + expH[i]; continue; }
            var info = Store.Info(a.Key);
            var row = new RectangleF(x + 8 * S, ry, cw - 16 * S, rowH);
            string id = "app:" + a.Key;
            bool open = expH[i] > 0;
            if (hover == id || open) FillRound(g, row, 10 * S, T.Hover);
            AddHit(row, id, true);
            if (i > 0 && !open) using (var p = new Pen(T.Grid)) g.DrawLine(p, x + 64 * S, ry, x + cw - 20 * S, ry);

            float isz = 30 * S;
            DrawAppIcon(g, a.Key, x + 20 * S, ry + (rowH - isz) / 2, isz);
            float tx = x + 64 * S, tr = x + cw - 44 * S;
            Center(g, open ? "" : "", Glyph(11), T.Muted, new RectangleF(x + cw - 40 * S, ry + 8 * S, 24 * S, 24 * S));

            int lim;
            bool hasLim = Settings.Limits.TryGetValue(a.Key, out lim) && lim > 0;
            bool over = hasLim && avgDays == 0 && a.Seconds >= lim * 60;
            string time = Util.Fmt(a.Seconds);
            var tsz = Measure(g, time, F(14, true));
            Txt(g, time, F(14, true), over ? T.Danger : T.Text, tr - tsz.Width, ry + 11 * S);
            g.DrawString(info.Name, F(14, true), Brush(T.Text), new RectangleF(tx, ry + 11 * S, tr - tsz.Width - tx - 16 * S, 20 * S), sf);

            string sub = "Opened " + a.Opens + (a.Opens == 1 ? " time" : " times");
            if (total > 0) sub += " · " + Math.Round(a.Seconds / total * 100) + "%";
            if (avgDays > 0) sub += " · avg " + Util.Fmt(a.Seconds / avgDays) + "/day";
            sub += " · " + Categories.ForApp(a.Key);
            if (hasLim) sub += over ? " · Limit reached (" + Util.Fmt(lim * 60) + ")" : " · Limit " + Util.Fmt(lim * 60);
            Txt(g, sub, F(12), over ? T.Danger : T.Muted, tx, ry + 32 * S);

            float barY = ry + 51 * S, barW = tr - tx;
            FillRound(g, new RectangleF(tx, barY, barW, 4 * S), 2 * S, T.Track);
            Color c;
            if (!colors.TryGetValue(a.Key, out c)) c = T.Other;
            float fw = Math.Max(4 * S, (float)(a.Seconds / max) * barW);
            FillRound(g, new RectangleF(tx, barY, fw, 4 * S), 2 * S, over ? T.Danger : c);
            if (hasLim && avgDays == 0 && lim * 60 < max)
            {
                float lx = tx + (float)(lim * 60 / max) * barW;
                using (var p = new Pen(T.Danger, 2 * S)) g.DrawLine(p, lx, barY - 3 * S, lx, barY + 7 * S);
            }
            ry += rowH;
            if (open)
            {
                DrawExpanded(g, a.Key, x, ry, cw, lists[i], expH[i], hasLim ? lim : 0);
                ry += expH[i];
            }
        }
        return y + h;
    }

    const int MaxItemRows = 12;

    float ExpandedHeight(int n)
    {
        return 50 * S + Math.Min(n, MaxItemRows) * 30 * S + (n > MaxItemRows ? 26 * S : 0) + (n == 0 ? 30 * S : 0) + 10 * S;
    }

    void DrawExpanded(Graphics g, string key, float x, float y, float cw, List<ItemUsage> items, float h, int lim)
    {
        float px = x + 64 * S, pr = x + cw - 44 * S;
        // action chips
        float cx = px, cy = y + 8 * S;
        cx = Chip(g, cx, cy, lim > 0 ? "  Limit " + Util.Fmt(lim * 60) + " – change" : "  Set daily limit", "limit:" + key);
        cx = Chip(g, cx + 8 * S, cy, "Category: " + Categories.ForApp(key) + "  ↻", "cat:" + key);
        float ry = y + 50 * S;
        if (items.Count == 0)
        {
            Txt(g, Settings.RecordTitles ? "No separate windows, pages or files recorded for this app." : "Window titles are turned off in Settings.", F(12.5f), T.Muted, px, ry + 4 * S);
            return;
        }
        double max = items[0].Seconds;
        for (int i = 0; i < items.Count && i < MaxItemRows; i++)
        {
            var it = items[i];
            string id = ItemHit(it);
            var row = new RectangleF(px - 8 * S, ry, pr - px + 16 * S, 30 * S);
            if (hover == id) FillRound(g, row, 6 * S, T.Surface);
            AddHit(row, id, true);
            string time = Util.Fmt(it.Seconds);
            var tsz = Measure(g, time, F(12.5f, true));
            Txt(g, time, F(12.5f, true), T.Text, pr - tsz.Width, ry + 6 * S);
            float siteW = 0;
            if (it.Site != null)
            {
                siteW = Math.Min(160 * S, Measure(g, it.Site, F(12)).Width);
                g.DrawString(it.Site, F(12), Brush(T.Muted), new RectangleF(pr - tsz.Width - 16 * S - siteW, ry + 7 * S, siteW + 2, 18 * S), sf);
                siteW += 16 * S;
            }
            FillRound(g, new RectangleF(px, ry + 12 * S, 4 * S, 4 * S), 2 * S, T.Muted);
            g.DrawString(it.Title, F(12.5f), Brush(T.Text), new RectangleF(px + 12 * S, ry + 6 * S, pr - px - 28 * S - tsz.Width - siteW, 18 * S), sf);
            ry += 30 * S;
        }
        if (items.Count > MaxItemRows)
            Txt(g, "+ " + (items.Count - MaxItemRows) + " more (see the Log tab or export CSV for everything)", F(12), T.Muted, px + 12 * S, ry + 4 * S);
    }

    float Chip(Graphics g, float x, float y, string text, string id)
    {
        bool glyph = text.Length > 0 && text[0] >= '' && text[0] <= '';
        string body = glyph ? text.Substring(1) : text;
        var sz = Measure(g, body, F(12.5f, true));
        float w = sz.Width + (glyph ? 34 : 24) * S;
        var r = new RectangleF(x, y, w, 30 * S);
        FillRound(g, r, 15 * S, hover == id ? T.AccentSoft : T.Surface);
        using (var p = new Pen(T.Border)) using (var path = Round(r, 15 * S)) g.DrawPath(p, path);
        float tx = x + 12 * S;
        if (glyph) { Txt(g, text.Substring(0, 1), Glyph(12), T.Accent, tx, y + 9 * S); tx += 14 * S; }
        Txt(g, body.TrimStart(), F(12.5f, true), T.Text, tx, y + 6 * S);
        AddHit(r, id, true);
        return x + w;
    }

    // ---------- chart helpers ----------

    Dictionary<string, Color> ColorsFor(IEnumerable<string> keys)
    {
        var d = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        int i = 0;
        foreach (var k in keys) { if (i >= T.Series.Length) break; d[k] = T.Series[i++]; }
        return d;
    }

    void StackedBar(Graphics g, float bx, float bottom, float bw, float perSec, List<KeyValuePair<Color, double>> segs)
    {
        double sum = segs.Sum(s => s.Value);
        float fullH = Math.Max(3 * S, (float)sum * perSec);
        var outer = new RectangleF(bx, bottom - fullH, bw, fullH);
        var st = g.Save();
        using (var clip = Round(outer, Math.Min(5 * S, bw / 2)))
        {
            g.SetClip(clip, CombineMode.Intersect);
            float yb = bottom;
            foreach (var s in segs)
            {
                float sh = (float)(s.Value / sum) * fullH;
                using (var b = new SolidBrush(s.Key)) g.FillRectangle(b, bx, yb - sh - 0.5f, bw, sh + 0.5f);
                yb -= sh;
            }
        }
        g.Restore(st);
    }

    float LegendHeight(Graphics g, List<string> keys, bool other, float width)
    {
        int rows = 1; float cx = 0;
        foreach (var w in LegendWidths(g, keys, other)) { if (cx + w > width && cx > 0) { rows++; cx = 0; } cx += w; }
        return rows * 22 * S;
    }

    List<float> LegendWidths(Graphics g, List<string> keys, bool other)
    {
        var l = new List<float>();
        foreach (var k in keys) l.Add(18 * S + Measure(g, Short(Store.Info(k).Name), F(12.5f)).Width + 18 * S);
        if (other) l.Add(18 * S + Measure(g, "Other", F(12.5f)).Width + 18 * S);
        return l;
    }

    void Legend(Graphics g, List<string> keys, Dictionary<string, Color> colors, bool other, float x0, float y0, float width)
    {
        var widths = LegendWidths(g, keys, other);
        float cx = 0, cy = y0;
        for (int i = 0; i < widths.Count; i++)
        {
            if (cx + widths[i] > width && cx > 0) { cx = 0; cy += 22 * S; }
            bool isOther = i >= keys.Count;
            Color c = isOther ? T.Other : colors[keys[i]];
            FillRound(g, new RectangleF(x0 + cx, cy + 4 * S, 10 * S, 10 * S), 3 * S, c);
            Txt(g, isOther ? "Other" : Short(Store.Info(keys[i]).Name), F(12.5f), T.Muted, x0 + cx + 16 * S, cy);
            cx += widths[i];
        }
    }

    static string Short(string s) { return s.Length > 22 ? s.Substring(0, 21) + "…" : s; }

    static string HourName(int h)
    {
        h = ((h % 24) + 24) % 24;
        if (h == 0) return "12 AM";
        if (h == 12) return "12 PM";
        return h < 12 ? h + " AM" : (h - 12) + " PM";
    }

    void PaintScrollbar(Graphics g, int w, int h)
    {
        float trackTop = HeaderH + 4 * S, trackH = h - trackTop - 4 * S;
        float view = h - HeaderH, total = contentH - HeaderH;
        float th = Math.Max(40 * S, trackH * view / total);
        int max = Math.Max(1, contentH - h);
        float ty = trackTop + (trackH - th) * scrollY / max;
        thumbRect = new RectangleF(w - 10 * S, ty, 6 * S, th);
        FillRound(g, thumbRect, 3 * S, Color.FromArgb(dragThumb || hover == "thumb" ? 150 : 90, T.Muted));
        hits.Add(new Hit { R = new RectangleF(w - 14 * S, ty, 14 * S, th), Id = "thumb" });
    }

    void PaintTip(Graphics g, int w, int h)
    {
        var f0 = F(13, true); var f1 = F(12.5f);
        float maxW = 460 * S, tw = 0, lh = 20 * S;
        for (int i = 0; i < tip.Count; i++) tw = Math.Max(tw, Math.Min(maxW, Measure(g, tip[i], i == 0 ? f0 : f1).Width));
        float bw = tw + 24 * S, bh = tip.Count * lh + 16 * S;
        float bx = mouse.X + 16 * S, by = mouse.Y + 16 * S;
        if (bx + bw > w - 8 * S) bx = Math.Max(8 * S, mouse.X - bw - 12 * S);
        if (by + bh > h - 8 * S) by = mouse.Y - bh - 12 * S;
        var r = new RectangleF(bx, by, bw, bh);
        FillRound(g, new RectangleF(bx + 2 * S, by + 3 * S, bw, bh), 9 * S, Color.FromArgb(T.Dark ? 90 : 30, 0, 0, 0));
        FillRound(g, r, 9 * S, T.Dark ? Color.FromArgb(38, 42, 52) : Color.FromArgb(24, 28, 40));
        for (int i = 0; i < tip.Count; i++)
            g.DrawString(tip[i], i == 0 ? f0 : f1, Brush(i == 0 ? Color.White : Color.FromArgb(200, 205, 215)),
                new RectangleF(bx + 12 * S, by + 8 * S + i * lh, tw + 2, lh), sf);
    }

    // ---------- icons ----------

    void DrawAppIcon(Graphics g, string key, float x, float y, float size)
    {
        if (key == "screen-time-tracker") { DrawLogo(g, x, y, size); return; }
        var bmp = IconFor(key);
        if (bmp != null)
        {
            var im = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(bmp, x, y, size, size);
            g.InterpolationMode = im;
            return;
        }
        string name = Store.Info(key).Name;
        Color c;
        if (badgeColors == null || !badgeColors.TryGetValue(key, out c)) c = T.Series[(int)((uint)StableHash(name) % (uint)T.Series.Length)];
        var r = new RectangleF(x, y, size, size);
        FillRound(g, r, size * 0.28f, c);
        Center(g, name.Substring(0, 1).ToUpperInvariant(), F(size / S * 0.5f, true), Color.White, r);
    }

    static int StableHash(string s) { int h = 17; foreach (char ch in s) h = unchecked(h * 31 + ch); return h; }

    Bitmap IconFor(string key)
    {
        Bitmap b;
        if (icons.TryGetValue(key, out b)) return b;
        b = null;
        string path = Store.Info(key).Path;
        if (key == "desktop") path = Environment.ExpandEnvironmentVariables(@"%WINDIR%\explorer.exe");
        try
        {
            if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                using (var ic = Icon.ExtractAssociatedIcon(path)) if (ic != null) b = ic.ToBitmap();
        }
        catch { }
        icons[key] = b;
        return b;
    }

    // ---------- drawing primitives ----------

    readonly Dictionary<Color, SolidBrush> brushes = new Dictionary<Color, SolidBrush>();
    Brush Brush(Color c)
    {
        SolidBrush b;
        if (!brushes.TryGetValue(c, out b)) { b = new SolidBrush(c); brushes[c] = b; }
        return b;
    }

    SizeF Measure(Graphics g, string s, Font f) { return g.MeasureString(s, f, PointF.Empty, sf); }
    void Txt(Graphics g, string s, Font f, Color c, float x, float y) { g.DrawString(s, f, Brush(c), x, y, sf); }
    void TextRight(Graphics g, string s, Font f, Color c, float xr, float y) { Txt(g, s, f, c, xr - Measure(g, s, f).Width, y); }
    void Center(Graphics g, string s, Font f, Color c, RectangleF r)
    {
        var sz = Measure(g, s, f);
        g.DrawString(s, f, Brush(c), r.X + (r.Width - sz.Width) / 2, r.Y + (r.Height - sz.Height) / 2, sf);
    }

    void Card(Graphics g, RectangleF r)
    {
        if (!InView(r.Y, r.Height)) return;
        using (var path = Round(r, 14 * S))
        {
            g.FillPath(Brush(T.Surface), path);
            using (var p = new Pen(T.Border)) g.DrawPath(p, path);
        }
    }

    void FillRound(Graphics g, RectangleF r, float rad, Color c)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        using (var p = Round(r, rad)) g.FillPath(Brush(c), p);
    }

    static GraphicsPath Round(RectangleF r, float rad)
    {
        rad = Math.Max(0.5f, Math.Min(rad, Math.Min(r.Width, r.Height) / 2));
        var p = new GraphicsPath(); float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }

    static Color Blend(Color a, Color b, float t)
    {
        return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    // ================= input =================

    string HitAt(Point p)
    {
        for (int i = hits.Count - 1; i >= 0; i--) if (hits[i].R.Contains(p)) return hits[i].Id;
        return null;
    }

    static bool IsHoverOnly(string id)
    {
        return id.StartsWith("hour:") || id.StartsWith("itm:") || id == "timeline" || id == "thumb";
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        mouse = e.Location;
        if (dragThumb)
        {
            float trackH = Height - HeaderH - 8 * S - thumbRect.Height;
            int max = Math.Max(0, contentH - Height);
            if (trackH > 0) SetScroll(dragScroll0 + (int)((e.Y - dragY0) * max / trackH));
            return;
        }
        string h = HitAt(e.Location);
        Cursor = h != null && !IsHoverOnly(h) ? Cursors.Hand : Cursors.Default;
        if (h != hover || (h != null && (h.StartsWith("hour:") || h.StartsWith("wday:") || h.StartsWith("itm:") || h == "timeline" || h.StartsWith("logday:") || h.StartsWith("mday:"))))
        { hover = h; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        mouse = new Point(-9999, -9999);
        if (hover != null) { hover = null; Invalidate(); }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button == MouseButtons.Left && HitAt(e.Location) == "thumb") { dragThumb = true; dragY0 = e.Y; dragScroll0 = scrollY; }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (dragThumb) { dragThumb = false; Invalidate(); return; }
        if (e.Button != MouseButtons.Left) return;
        string id = HitAt(e.Location);
        if (id == null) return;
        if (id.StartsWith("mode:")) { Mode = id[5] - '0'; scrollY = 0; }
        else if (id == "prev") Step(-1);
        else if (id == "next") Step(1);
        else if (id == "today") { Date = Clock.Today; scrollY = 0; }
        else if (id == "settings") { if (OpenSettings != null) OpenSettings(); }
        else if (id == "search") { if (OpenSearch != null) OpenSearch(); }
        else if (id == "pdf") { if (ExportPdf != null) ExportPdf(Mode, Date); }
        else if (id == "stopfocus") { if (StopFocus != null) StopFocus(); }
        else if (id.StartsWith("focus:")) { if (StartFocus != null) StartFocus(int.Parse(id.Substring(6))); }
        else if (id == "projects") { if (ManageProjects != null) ManageProjects(); }
        else if (id == "note") { if (CopyNote != null) CopyNote(Date); }
        else if (id == "timesheet") { if (Timesheet != null) Timesheet(Date); }
        else if (id == "addman") { if (EditManual != null) EditManual(Date, null); }
        else if (id.StartsWith("man:"))
        {
            int mi = int.Parse(id.Substring(4));
            var dm = Day(Date).Manual.OrderBy(m => m.Start).ToList();
            if (mi < dm.Count && EditManual != null) EditManual(Date, dm[mi]);
        }
        else if (id == "wrapped:month") { if (MakeWrapped != null) MakeWrapped(new DateTime(Date.Year, Date.Month, 1), new DateTime(Date.Year, Date.Month, 1).AddMonths(1).AddDays(-1), Date.ToString("MMMM yyyy", Cur)); }
        else if (id == "wrapped:year") { if (MakeWrapped != null) MakeWrapped(new DateTime(Date.Year, 1, 1), new DateTime(Date.Year, 12, 31), Date.Year + " so far"); }
        else if (id.StartsWith("cmp:")) compareMode = id.Substring(4);
        else if (id == "pause") { if (TogglePause != null) TogglePause(); }
        else if (id == "short") hideShort = !hideShort;
        else if (id == "sync") Clock.SyncAsync();
        else if (id.StartsWith("app:"))
        {
            string k = id.Substring(4);
            if (!expanded.Remove(k)) expanded.Add(k);
        }
        else if (id.StartsWith("limit:")) { if (EditLimit != null) EditLimit(id.Substring(6)); }
        else if (id.StartsWith("cat:")) { string k = id.Substring(4); Categories.Cycle("app:" + k, Categories.ForApp(k)); }
        else if (id.StartsWith("sitecat:")) { string k = id.Substring(8); Categories.Cycle("site:" + k, Categories.ForSite(k)); }
        else if (id.StartsWith("wday:") || id.StartsWith("logday:") || id.StartsWith("mday:"))
        {
            int p = id.IndexOf(':') + 1;
            Date = DateTime.ParseExact(id.Substring(p), "yyyy-MM-dd", CultureInfo.InvariantCulture);
            Mode = id.StartsWith("wday:") || id.StartsWith("mday:") ? 0 : 3;
            scrollY = 0;
        }
        hover = null;
        Invalidate();
    }

    void Step(int dir)
    {
        if (dir > 0 && AtCurrent) return;
        Date = Mode == 2 ? Date.AddMonths(dir) : Date.AddDays(dir * (Mode == 1 ? 7 : 1));
        if (Date > Clock.Today) Date = Clock.Today;
        Invalidate();
    }

    void SetScroll(int v)
    {
        int max = Math.Max(0, contentH - Height);
        v = Math.Max(0, Math.Min(max, v));
        if (v != scrollY) { scrollY = v; Invalidate(); }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        SetScroll(scrollY - (int)(e.Delta / 120f * 60 * S));
        hover = HitAt(e.Location);
    }

    protected override bool IsInputKey(Keys k)
    {
        return k == Keys.Left || k == Keys.Right || k == Keys.Up || k == Keys.Down || base.IsInputKey(k);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int page = Height - (int)HeaderH - 40;
        switch (e.KeyCode)
        {
            case Keys.Left: Step(-1); break;
            case Keys.Right: Step(1); break;
            case Keys.Up: SetScroll(scrollY - 50); break;
            case Keys.Down: SetScroll(scrollY + 50); break;
            case Keys.PageUp: SetScroll(scrollY - page); break;
            case Keys.PageDown: case Keys.Space: SetScroll(scrollY + page); break;
            case Keys.Home: SetScroll(0); break;
            case Keys.End: SetScroll(int.MaxValue); break;
            case Keys.D: Mode = 0; Invalidate(); break;
            case Keys.W: Mode = 1; Invalidate(); break;
            case Keys.M: Mode = 2; Invalidate(); break;
            case Keys.L: Mode = 3; Invalidate(); break;
            case Keys.K: Mode = 4; Invalidate(); break;
            case Keys.F: if (e.Control && OpenSearch != null) OpenSearch(); break;
            case Keys.T: Date = Clock.Today; Invalidate(); break;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ClearFonts();
            foreach (var b in icons.Values) if (b != null) b.Dispose();
            foreach (var b in brushes.Values) b.Dispose();
            sf.Dispose();
        }
        base.Dispose(disposing);
    }
}
