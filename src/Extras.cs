using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

// A tiny always-on-top bar: today's screen time and what you're doing. Drag to move, double-click to open the dashboard.
class MiniWidget : Form
{
    readonly Tracker tracker;
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    Point dragFrom;
    bool dragging;
    float s;
    public event Action OpenDashboard;

    public MiniWidget(Tracker tr)
    {
        tracker = tr;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        using (var g = CreateGraphics()) s = g.DpiX / 96f;
        Size = new Size((int)(250 * s), (int)(54 * s));
        var wa = Screen.PrimaryScreen.WorkingArea;
        var loc = Settings.WidgetX >= 0 ? new Point(Settings.WidgetX, Settings.WidgetY) : new Point(wa.Right - Width - (int)(16 * s), wa.Bottom - Height - (int)(16 * s));
        if (!Screen.AllScreens.Any(sc => sc.WorkingArea.Contains(loc))) loc = new Point(wa.Right - Width - 16, wa.Bottom - Height - 16);
        Location = loc;
        using (var p = Round(new RectangleF(0, 0, Width, Height), 12 * s)) Region = new Region(p);
        timer.Interval = 2000;
        timer.Tick += delegate { Invalidate(); };
        timer.Start();
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open dashboard", null, delegate { if (OpenDashboard != null) OpenDashboard(); });
        menu.Items.Add("Hide widget", null, delegate { Settings.WidgetOn = false; Settings.Save(); Close(); });
        ContextMenuStrip = menu;
    }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x80 /* WS_EX_TOOLWINDOW */ | 0x08000000 /* WS_EX_NOACTIVATE */; return cp; }
    }

    protected override bool ShowWithoutActivation { get { return true; } }

    protected override void OnPaint(PaintEventArgs e)
    {
        var T = Theme.Current;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(T.Surface);
        using (var p = Round(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 12 * s)) using (var pen = new Pen(T.Border)) g.DrawPath(pen, p);
        Brand.DrawLogo(g, 12 * s, 12 * s, 30 * s);
        using (var f = new Font("Segoe UI Semibold", 15 * s, GraphicsUnit.Pixel))
        using (var b = new SolidBrush(T.Text))
            g.DrawString(Util.Fmt(tracker.Today.Total) + " today", f, b, 52 * s, 7 * s);
        string sub = tracker.Focusing ? "Focus · " + Math.Ceiling((tracker.FocusUntil - Clock.Now).TotalMinutes) + " min left" : tracker.State;
        if (sub.Length > 32) sub = sub.Substring(0, 31) + "…";
        using (var f = new Font("Segoe UI", 12 * s, GraphicsUnit.Pixel))
        using (var b = new SolidBrush(tracker.Focusing ? T.Accent : T.Muted))
            g.DrawString(sub, f, b, 53 * s, 29 * s);
        if (Settings.DailyGoalMinutes > 0)
        {
            float frac = (float)Math.Min(1, tracker.Today.Total / (Settings.DailyGoalMinutes * 60.0));
            using (var b = new SolidBrush(T.Track)) g.FillRectangle(b, 0, Height - 3 * s, Width, 3 * s);
            using (var b = new SolidBrush(frac >= 1 ? T.Danger : T.Good)) g.FillRectangle(b, 0, Height - 3 * s, Width * frac, 3 * s);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left) { dragging = true; dragFrom = e.Location; }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (dragging) Location = new Point(Location.X + e.X - dragFrom.X, Location.Y + e.Y - dragFrom.Y);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!dragging) return;
        dragging = false;
        Settings.WidgetX = Location.X; Settings.WidgetY = Location.Y;
        Settings.Save();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (OpenDashboard != null) OpenDashboard();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }

    internal static GraphicsPath Round(RectangleF r, float rad)
    {
        var p = new GraphicsPath(); float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }
}

// "Screen Time Wrapped": a shareable 1080 x 1350 image summarising a month or year (for LinkedIn / Instagram).
static class Wrapped
{
    public static string Make(DateTime from, DateTime to, string title, Func<DateTime, DayData> getDay, string path)
    {
        var days = new List<DayData>();
        for (var d = from.Date; d <= to.Date && d <= Clock.Today; d = d.AddDays(1)) days.Add(getDay(d));
        var agg = new DayData(); agg.Date = from;
        foreach (var d in days) DataTools.MergeInto(agg, d, null);
        var active = days.Where(d => d.Total >= 60).ToList();
        double total = agg.Total, avg = active.Count > 0 ? total / active.Count : 0;
        var apps = agg.Sorted().Take(5).ToList();
        var sites = Insights.SiteTotals(agg).OrderByDescending(k => k.Value).Take(3).ToList();
        var ins = days.Where(d => d.Total >= 600).Select(d => Insights.ForDay(d, null)).ToList();
        double deep = ins.Sum(i => i.DeepWork), prod = ins.Sum(i => i.Productive);
        var best = days.Where(d => d.Total >= 600).OrderByDescending(d => Insights.ForDay(d, null).FocusScore).FirstOrDefault();
        double share; string bestHours = Insights.BestHours(to, Math.Min(90, (to - from).Days + 1), getDay, out share);
        int badges = ins.Sum(i => i.Badges.Count);

        const int W = 1080, H = 1350;
        using (var bmp = new Bitmap(W, H))
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var br = new LinearGradientBrush(new Point(0, 0), new Point(W, H), Color.FromArgb(30, 27, 75), Color.FromArgb(13, 80, 90)))
                g.FillRectangle(br, 0, 0, W, H);
            using (var glow = new SolidBrush(Color.FromArgb(40, 124, 156, 255))) g.FillEllipse(glow, 620, -220, 700, 700);
            using (var glow = new SolidBrush(Color.FromArgb(30, 45, 212, 191))) g.FillEllipse(glow, -260, 900, 700, 700);
            Brand.DrawLogo(g, 80, 80, 72);
            T(g, "MY SCREEN TIME", 26, true, Color.FromArgb(180, 200, 255), 172, 86);
            T(g, title, 40, true, Color.White, 172, 118);

            T(g, Util.Fmt(total).Replace(" ", " "), 120, true, Color.White, 74, 210);
            T(g, "on screen · " + Util.Fmt(avg) + " a day on " + active.Count + " active " + (active.Count == 1 ? "day" : "days"), 28, false, Color.FromArgb(200, 210, 235), 84, 380);

            T(g, "TOP APPS", 22, true, Color.FromArgb(150, 170, 220), 84, 460);
            Color[] pal = { Color.FromArgb(124, 156, 255), Color.FromArgb(45, 212, 191), Color.FromArgb(251, 191, 36), Color.FromArgb(244, 114, 182), Color.FromArgb(167, 139, 250) };
            double max = apps.Count > 0 ? apps[0].Seconds : 1;
            int y = 500;
            for (int i = 0; i < apps.Count; i++)
            {
                T(g, (i + 1) + "  " + Short(Store.Info(apps[i].Key).Name, 26), 30, true, Color.White, 84, y);
                var tw = Util.Fmt(apps[i].Seconds);
                TR(g, tw, 30, false, Color.FromArgb(220, 228, 245), W - 84, y);
                using (var b = new SolidBrush(Color.FromArgb(50, 255, 255, 255))) g.FillRectangle(b, 84, y + 48, W - 168, 8);
                using (var b = new SolidBrush(pal[i])) g.FillRectangle(b, 84, y + 48, (float)((W - 168) * apps[i].Seconds / max), 8);
                y += 78;
            }

            y = Math.Max(y + 10, 900);
            string[] labels = { "TOP WEBSITE", "DEEP WORK", "PRODUCTIVE", "BEST FOCUS DAY", "BEST HOURS", "BADGES EARNED" };
            string[] values =
            {
                sites.Count > 0 ? Short(sites[0].Key, 18) : "—", Util.Fmt(deep), total > 0 ? Math.Round(prod / total * 100) + "%" : "—",
                best != null ? best.Date.ToString("d MMM", CultureInfo.CurrentCulture) : "—", bestHours ?? "—", badges.ToString()
            };
            for (int i = 0; i < 6; i++)
            {
                int cx = 84 + (i % 3) * 312, cy = y + (i / 3) * 150;
                using (var b = new SolidBrush(Color.FromArgb(28, 255, 255, 255))) using (var p = MiniWidget.Round(new RectangleF(cx, cy, 288, 128), 22)) g.FillPath(b, p);
                T(g, labels[i], 18, true, Color.FromArgb(160, 180, 225), cx + 22, cy + 20);
                T(g, values[i], 34, true, Color.White, cx + 20, cy + 52);
            }
            T(g, "Made with Screen Time Tracker · private, on-device tracking", 20, false, Color.FromArgb(150, 165, 200), 84, H - 70);
            bmp.Save(path, ImageFormat.Png);
        }
        return path;
    }

    static string Short(string s, int n) { return s.Length > n ? s.Substring(0, n - 1) + "…" : s; }

    static void T(Graphics g, string s, float px, bool bold, Color c, float x, float y)
    {
        using (var f = new Font(bold ? "Segoe UI Semibold" : "Segoe UI", px, GraphicsUnit.Pixel)) using (var b = new SolidBrush(c)) g.DrawString(s, f, b, x, y);
    }

    static void TR(Graphics g, string s, float px, bool bold, Color c, float xr, float y)
    {
        using (var f = new Font(bold ? "Segoe UI Semibold" : "Segoe UI", px, GraphicsUnit.Pixel))
        using (var b = new SolidBrush(c)) g.DrawString(s, f, b, xr - g.MeasureString(s, f).Width, y);
    }
}

// Checks GitHub for a newer release (only works once the project is published there).
static class Updater
{
    public const string Repo = "sjaiswalnetwork/screen-time-tracker";
    public static string ReleasesUrl { get { return "https://github.com/" + Repo + "/releases/latest"; } }

    public const string AppVersion = "2.0.0";
    public static Version Current { get { return new Version(AppVersion); } }

    public static void CheckAsync(Action<string> onNewer)
    {
        ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
                using (var wc = new WebClient())
                {
                    wc.Headers[HttpRequestHeader.UserAgent] = "ScreenTimeTracker";
                    string json = wc.DownloadString("https://api.github.com/repos/" + Repo + "/releases/latest");
                    var m = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?([0-9.]+)\"");
                    Version v;
                    if (m.Success && Version.TryParse(m.Groups[1].Value, out v) && v > Current) onNewer(m.Groups[1].Value);
                }
            }
            catch { }
        });
    }
}

// Ctrl + Alt + S opens the dashboard from anywhere.
class Hotkey : NativeWindow, IDisposable
{
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    public event Action Pressed;
    public readonly bool Ok;

    public Hotkey()
    {
        CreateHandle(new CreateParams());
        Ok = RegisterHotKey(Handle, 1, 0x0001 | 0x0002 | 0x4000 /* ALT | CTRL | NOREPEAT */, (uint)Keys.S);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312 && Pressed != null) Pressed();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterHotKey(Handle, 1);
        DestroyHandle();
    }
}

// The tray icon: the app logo, or today's hours on the logo's colours.
static class TrayIcons
{
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
    static IntPtr last = IntPtr.Zero;

    public static Icon Hours(double seconds, Size size)
    {
        int s = size.Width;
        using (var bmp = new Bitmap(s, s))
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var p = MiniWidget.Round(new RectangleF(0, 0, s - 0.5f, s - 0.5f), s * 0.25f))
            using (var br = new LinearGradientBrush(new PointF(0, 0), new PointF(s, s), Color.FromArgb(92, 110, 255), Color.FromArgb(20, 184, 166)))
                g.FillPath(br, p);
            int h = (int)(seconds / 3600);
            string txt = h < 10 ? h + "h" : h.ToString();
            using (var f = new Font("Segoe UI", s * (txt.Length > 2 ? 0.45f : 0.56f), FontStyle.Bold, GraphicsUnit.Pixel))
            {
                var sz = g.MeasureString(txt, f, PointF.Empty, StringFormat.GenericTypographic);
                g.DrawString(txt, f, Brushes.White, (s - sz.Width) / 2, (s - sz.Height) / 2, StringFormat.GenericTypographic);
            }
            IntPtr hi = bmp.GetHicon();
            var icon = (Icon)Icon.FromHandle(hi).Clone();
            DestroyIcon(hi);
            return icon;
        }
    }
}
