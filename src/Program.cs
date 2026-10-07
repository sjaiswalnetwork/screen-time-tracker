using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

public static class Program
{
    public static string IconFile, RunCommand;
    public static bool DemoMode; // test/demo data folder: never show the real user or PC name
    public const string GuideUrl = "https://sjaiswalnetwork.github.io/screen-time-tracker/guide.html";

    /// The program folder (contains assets\, docs\, src\).
    public static string AppDir
    {
        get
        {
            if (IconFile != null) return Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(IconFile)));
            return Path.GetDirectoryName(Path.GetDirectoryName(Application.ExecutablePath));
        }
    }

    /// Opens the how-to guide (the local copy if installed, otherwise the website).
    public static void OpenGuide()
    {
        try
        {
            string local = Path.Combine(AppDir, "docs", "guide.html");
            Process.Start(File.Exists(local) ? local : GuideUrl);
        }
        catch { }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetProcessDPIAware();

    const string MutexName = "Local\\ScreenTimeTracker.SingleInstance";
    const string ShowEventName = "Local\\ScreenTimeTracker.Show";

    [STAThread]
    public static int Main(string[] args)
    {
        try { SetProcessDPIAware(); } catch { }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        string dataDir = null, snapshot = null, view = "day", theme = null, dialog = null;
        bool minimized = false, seed = false;
        float scale = 1f;
        DateTime? date = null;
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string next = i + 1 < args.Length ? args[i + 1] : null;
            if (a == "--minimized") minimized = true;
            else if (a == "--data" && next != null) { dataDir = next; i++; }
            else if (a == "--seed-demo") seed = true;
            else if (a == "--icon" && next != null) { IconFile = next; i++; }
            else if (a == "--run-cmd" && next != null) { RunCommand = next; i++; }
            else if (a == "--snapshot" && next != null) { snapshot = next; i++; }
            else if (a == "--view" && next != null) { view = next; i++; }
            else if (a == "--dialog" && next != null) { dialog = next; i++; }
            else if (a == "--theme" && next != null) { theme = next; i++; }
            else if (a == "--scale" && next != null) { float.TryParse(next, NumberStyles.Float, CultureInfo.InvariantCulture, out scale); i++; }
            else if (a == "--date" && next != null)
            {
                DateTime d;
                if (DateTime.TryParseExact(next, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) date = d;
                i++;
            }
        }

        DemoMode = dataDir != null;
        Store.Init(dataDir);
        if (dataDir != null && Array.IndexOf(args, "--win-events") < 0) WinEvents.Enabled = false; // test data: don't mix in this PC's real Windows events
        Theme.Apply(theme ?? Settings.Theme);
        if (seed) Demo.Seed();
        if (snapshot != null) { Snapshot(snapshot, view, date ?? Clock.Today, scale); return 0; }

        int rp = Array.IndexOf(args, "--report");
        if (rp >= 0 && rp + 1 < args.Length)
        {
            var tr = new Tracker();
            DateTime day = date ?? Clock.Today;
            if (view == "week") { var ws = day.AddDays(-(((int)day.DayOfWeek + 6) % 7)); PdfReport.RangeReport(ws, ws.AddDays(6), "week", tr, DayGetter(tr), args[rp + 1]); }
            else if (view == "month") { var ms = new DateTime(day.Year, day.Month, 1); PdfReport.RangeReport(ms, ms.AddMonths(1).AddDays(-1), "month", tr, DayGetter(tr), args[rp + 1]); }
            else PdfReport.DayReport(day, tr, DayGetter(tr), args[rp + 1]);
            return 0;
        }
        int wr = Array.IndexOf(args, "--wrapped");
        if (wr >= 0 && wr + 1 < args.Length)
        {
            var tr = new Tracker();
            DateTime day = date ?? Clock.Today;
            var ms = new DateTime(day.Year, day.Month, 1);
            Wrapped.Make(ms, ms.AddMonths(1).AddDays(-1), day.ToString("MMMM yyyy", CultureInfo.CurrentCulture), DayGetter(tr), args[wr + 1]);
            return 0;
        }
        int st = Array.IndexOf(args, "--selftest");
        if (st >= 0 && st + 1 < args.Length) { SelfTest.Run(args[st + 1]); return 0; }
        if (seed) return 0;
        if (dialog != null)
        {
            // test hook: open one dialog on its own
            var t = new Tracker();
            var ic = LoadIcon(new Size(32, 32));
            if (dialog == "limit") Dialogs.EditLimit(null, ic, "chrome.exe", 5000);
            else if (dialog == "report") Dialogs.ExportReport(null, ic, t, Clock.Today, "day");
            else if (dialog == "projects") Dialogs.ManageProjects(null, ic);
            else if (dialog == "manual") Dialogs.EditManual(null, ic, t, Clock.Today, null, 36000, 39600);
            else if (dialog == "away") Application.Run(new AwayPromptForm(t, ic, 37920, 40500));
            else if (dialog == "search") Application.Run(new SearchForm(t, ic));
            else if (dialog == "widget") Application.Run(new MiniWidget(t));
            else Dialogs.ShowSettings(null, ic, t);
            return 0;
        }

        // keep going after unexpected errors instead of showing a crash dialog; log them for diagnosis
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (s, e) => LogError(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (s, e) => LogError(e.ExceptionObject as Exception);

        bool created;
        string suffix = dataDir == null ? "" : "." + ((uint)dataDir.ToLowerInvariant().GetHashCode()).ToString("x"); // test copies run alongside the real one
        using (var mutex = new Mutex(true, MutexName + suffix, out created))
        {
            if (!created)
            {
                try { using (var ev = EventWaitHandle.OpenExisting(ShowEventName + suffix)) ev.Set(); } catch { }
                return 0;
            }
            using (var ev = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName + suffix))
            {
                try { Application.Run(new TrayApp(minimized, ev)); }
                catch (Exception ex)
                {
                    LogError(ex);
                    mutex.ReleaseMutex();
                    Restart();
                    return 3;
                }
            }
        }
        return 0;
    }

    public static void LogError(Exception ex)
    {
        if (ex == null) return;
        try { File.AppendAllText(Path.Combine(Store.Root, "errors.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + ex + "\r\n\r\n"); } catch { }
    }

    // Crash protection: start a fresh copy (at most 3 times in 10 minutes).
    static void Restart()
    {
        try
        {
            string marker = Path.Combine(Store.Root, "restarts.txt");
            var recent = File.Exists(marker) ? File.ReadAllLines(marker).Select(l => { DateTime d; return DateTime.TryParse(l, CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d : DateTime.MinValue; })
                                                .Where(d => (DateTime.Now - d).TotalMinutes < 10).ToList() : new List<DateTime>();
            if (recent.Count >= 3) return;
            recent.Add(DateTime.Now);
            File.WriteAllLines(marker, recent.Select(d => d.ToString("o", CultureInfo.InvariantCulture)));
            if (RunCommand != null)
            {
                int sp = RunCommand.IndexOf(' ');
                Process.Start(new ProcessStartInfo(RunCommand.Substring(0, sp), RunCommand.Substring(sp + 1)) { UseShellExecute = false, CreateNoWindow = true });
            }
            else Process.Start(Application.ExecutablePath, "--minimized");
        }
        catch { }
    }

    static void Snapshot(string path, string view, DateTime date, float scale)
    {
        var tracker = new Tracker();
        using (var v = new DashView())
        {
            v.Tracker = tracker;
            v.Mode = view == "week" ? 1 : view == "month" ? 2 : view == "log" ? 3 : view == "work" ? 4 : 0;
            v.Date = date;
            v.SetScale(scale);
            using (var bmp = v.RenderFull((int)(940 * scale))) bmp.Save(path, ImageFormat.Png);
        }
    }

    /// Day loader for views and reports: today comes from the live tracker, other days from disk (plus other PCs).
    internal static Func<DateTime, DayData> DayGetter(Tracker tr)
    {
        return d => d.Date == tr.Today.Date ? DataTools.WithOtherPcs(tr.Today) : d.Date > Clock.Today ? EmptyDay(d) : Store.ViewDay(d);
    }

    static DayData EmptyDay(DateTime d) { var e = new DayData(); e.Date = d.Date; return e; }

    public static Icon LoadIcon(Size size)
    {
        try
        {
            if (IconFile != null && File.Exists(IconFile)) return new Icon(IconFile, size);
            using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("ScreenTime.ico"))
                if (st != null) return new Icon(st, size);
        }
        catch { }
        return SystemIcons.Application;
    }
}

// Lives in the notification area, runs the 1-second sampler, and owns the dashboard window and helpers.
class TrayApp : ApplicationContext
{
    readonly Tracker tracker;
    readonly NotifyIcon tray = new NotifyIcon();
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    readonly Control invoker = new Control();
    readonly Icon bigIcon, smallIcon;
    readonly ToolStripMenuItem pauseMenu, focusMenu, widgetItem;
    readonly RegisteredWaitHandle showWait;
    DashboardForm dash;
    MiniWidget widget;
    SearchForm search;
    AwayPromptForm awayPrompt;
    Hotkey hotkey;
    Action balloonAction;
    DateTime pinOkUntil = DateTime.MinValue;
    Icon hoursIcon;
    int ticks;

    public TrayApp(bool minimized, EventWaitHandle showEvent)
    {
        invoker.CreateControl();
        AbsorbStartupShowState();
        Clock.SyncAsync();
        tracker = new Tracker();
        tracker.Log("start", "Screen Time Tracker started");
        bigIcon = Program.LoadIcon(new Size(32, 32));
        smallIcon = Program.LoadIcon(SystemInformation.SmallIconSize);
        Settings.RefreshStartupPath();
        DataTools.Prune();

        var menu = new ContextMenuStrip();
        menu.Items.Add(Lang.T("Open Screen Time"), null, delegate { ShowDashboard(); }).Font = new Font(menu.Font, FontStyle.Bold);
        menu.Items.Add(Lang.T("Export report (PDF)…"), null, delegate { Dialogs.ExportReport(null, bigIcon, tracker, Clock.Today, "day"); });
        menu.Items.Add("Today's report (PDF) – open now", null, delegate { QuickTodayPdf(); });
        menu.Items.Add(Lang.T("Copy today's work note"), null, delegate { CopyNote(Clock.Today); });
        menu.Items.Add(Lang.T("Search history…"), null, delegate { ShowSearch(); });
        menu.Items.Add(Lang.T("Add offline work…"), null, delegate { EditManual(Clock.Today, null); });
        menu.Items.Add(new ToolStripSeparator());
        focusMenu = new ToolStripMenuItem(Lang.T("Focus mode"));
        foreach (var m in new[] { 25, 50, 90 }) { int mm = m; focusMenu.DropDownItems.Add(m + " minutes", null, delegate { StartFocus(mm); }); }
        focusMenu.DropDownItems.Add(Lang.T("Stop focus"), null, delegate { StopFocus(); });
        menu.Items.Add(focusMenu);
        pauseMenu = new ToolStripMenuItem(Lang.T("Pause tracking"));
        foreach (var m in new[] { 15, 30, 60 }) { int mm = m; pauseMenu.DropDownItems.Add("For " + (m < 60 ? m + " minutes" : "1 hour"), null, delegate { Pause(mm); }); }
        pauseMenu.DropDownItems.Add("Until I resume", null, delegate { Pause(0); });
        pauseMenu.DropDownItems.Add(Lang.T("Resume tracking"), null, delegate { Resume(); });
        menu.Items.Add(pauseMenu);
        widgetItem = new ToolStripMenuItem(Lang.T("Show mini widget"), null, delegate { ToggleWidget(); });
        menu.Items.Add(widgetItem);
        var wrapped = new ToolStripMenuItem(Lang.T("Make my Screen Time Wrapped card"));
        wrapped.DropDownItems.Add("This month", null, delegate { var ms = new DateTime(Clock.Today.Year, Clock.Today.Month, 1); MakeWrapped(ms, ms.AddMonths(1).AddDays(-1), ms.ToString("MMMM yyyy", CultureInfo.CurrentCulture)); });
        wrapped.DropDownItems.Add("Last month", null, delegate { var ms = new DateTime(Clock.Today.Year, Clock.Today.Month, 1).AddMonths(-1); MakeWrapped(ms, ms.AddMonths(1).AddDays(-1), ms.ToString("MMMM yyyy", CultureInfo.CurrentCulture)); });
        wrapped.DropDownItems.Add("This year", null, delegate { MakeWrapped(new DateTime(Clock.Today.Year, 1, 1), new DateTime(Clock.Today.Year, 12, 31), Clock.Today.Year + " so far"); });
        menu.Items.Add(wrapped);
        menu.Items.Add(new ToolStripSeparator());
        var autoItem = new ToolStripMenuItem("Start automatically with Windows", null, delegate
        {
            Settings.StartWithWindows = !Settings.StartWithWindows;
            Notify(Settings.StartWithWindows ? "Will start with Windows" : "Won't start with Windows",
                Settings.StartWithWindows ? "Screen Time Tracker will start by itself every time you sign in." : "Start it yourself from the Start menu or Desktop shortcut when you want tracking.", null);
        });
        menu.Items.Add(autoItem);
        menu.Items.Add("How to use (guide)", null, delegate { Program.OpenGuide(); });
        menu.Items.Add(Lang.T("Settings…"), null, delegate { OpenSettings(null); });
        menu.Items.Add(Lang.T("Exit"), null, delegate { Quit(); });
        menu.Opening += delegate
        {
            widgetItem.Checked = widget != null && !widget.IsDisposed;
            autoItem.Checked = Settings.StartWithWindows;
            pauseMenu.Text = tracker.Paused ? tracker.State : Lang.T("Pause tracking");
            focusMenu.Text = tracker.Focusing ? "Focus · " + Math.Ceiling((tracker.FocusUntil - Clock.Now).TotalMinutes) + " min left" : Lang.T("Focus mode");
        };
        tray.ContextMenuStrip = menu;
        tray.Icon = smallIcon;
        tray.Text = "Screen Time Tracker";
        tray.Visible = true;
        tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowDashboard(); };
        tray.BalloonTipClicked += delegate { var a = balloonAction; balloonAction = null; (a ?? ShowDashboard)(); };

        tracker.Notify += (title, text) => Notify(title, text, null);
        tracker.AwayEnded += (start, end) => invoker.BeginInvoke((Action)delegate { AskAway(start, end); });

        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPower;
        SystemEvents.SessionEnding += OnSessionEnding;
        SystemEvents.UserPreferenceChanged += OnPrefs;
        SystemEvents.TimeChanged += OnTimeChanged;

        showWait = ThreadPool.RegisterWaitForSingleObject(showEvent, delegate { invoker.BeginInvoke((Action)ShowDashboard); }, null, -1, false);
        ApplyExtras();

        timer.Interval = 1000;
        timer.Tick += delegate
        {
            tracker.Tick();
            ticks++;
            if (ticks % 5 == 0) UpdateTray();
            if (ticks % (6 * 3600) == 0 || (!Clock.Synced && ticks % 300 == 0)) Clock.SyncAsync();
            if (ticks == 120 || ticks % 3600 == 0) WeeklyReport();
            if (ticks == 90 || ticks % (24 * 3600) == 0) CheckUpdates();
        };
        timer.Start();
        UpdateTray();

        if (!Settings.FirstRunDone)
        {
            Settings.FirstRunDone = true;
            if (!Settings.InstallerChoseAutostart) Settings.StartWithWindows = true; // can be turned off in Settings or the tray menu
            Settings.Save();
            Notify("Screen Time Tracker is running", "It keeps tracking quietly in the notification area" + (Settings.StartWithWindows ? " and starts by itself with Windows" : "") +
                ". Click the clock icon (or press Ctrl+Alt+S) to see your screen time.", null);
        }
        if (!minimized) ShowDashboard();
    }

    // A process started hidden (e.g. "powershell -WindowStyle Hidden") has its FIRST shown window forced hidden by Windows.
    // Let an invisible throwaway window take that hit so the dashboard shows normally.
    static void AbsorbStartupShowState()
    {
        using (var f = new Form())
        {
            f.FormBorderStyle = FormBorderStyle.None;
            f.ShowInTaskbar = false;
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(-32000, -32000);
            f.Size = new Size(1, 1);
            f.Opacity = 0;
            Native.ShowWindow(f.Handle, 1);
            Native.ShowWindow(f.Handle, 0);
        }
    }

    void Notify(string title, string text, Action onClick)
    {
        balloonAction = onClick;
        tray.ShowBalloonTip(10000, title, text, ToolTipIcon.Info);
    }

    // hotkey, widget - things that follow settings
    void ApplyExtras()
    {
        if (Settings.Hotkey && hotkey == null)
        {
            hotkey = new Hotkey();
            hotkey.Pressed += ShowDashboard;
        }
        else if (!Settings.Hotkey && hotkey != null) { hotkey.Dispose(); hotkey = null; }
        bool showing = widget != null && !widget.IsDisposed;
        if (Settings.WidgetOn && !showing)
        {
            widget = new MiniWidget(tracker);
            widget.OpenDashboard += ShowDashboard;
            widget.Show();
        }
        else if (!Settings.WidgetOn && showing) widget.Close();
        UpdateTray();
    }

    void ToggleWidget()
    {
        Settings.WidgetOn = !(widget != null && !widget.IsDisposed);
        Settings.Save();
        ApplyExtras();
    }

    void UpdateTray()
    {
        string t = "Screen time today: " + Util.Fmt(tracker.Today.Total);
        if (tracker.Paused) t += " (paused)";
        else if (tracker.Focusing) t += " · focus " + Math.Ceiling((tracker.FocusUntil - Clock.Now).TotalMinutes) + "m left";
        tray.Text = t.Length > 63 ? t.Substring(0, 63) : t;
        if (Settings.TrayHours)
        {
            var old = hoursIcon;
            hoursIcon = TrayIcons.Hours(tracker.Today.Total, SystemInformation.SmallIconSize);
            tray.Icon = hoursIcon;
            if (old != null) old.Dispose();
        }
        else if (tray.Icon != smallIcon) tray.Icon = smallIcon;
    }

    void ShowDashboard()
    {
        if (Settings.PinHash.Length > 0 && DateTime.Now > pinOkUntil && (dash == null || dash.IsDisposed || !dash.Visible))
        {
            if (!Dialogs.AskPin(null, bigIcon)) return;
            pinOkUntil = DateTime.Now.AddMinutes(10);
        }
        if (dash == null || dash.IsDisposed)
        {
            dash = new DashboardForm(tracker, bigIcon);
            var v = dash.View;
            v.OpenSettings += delegate { OpenSettings(dash); };
            v.TogglePause += delegate { if (tracker.Paused) Resume(); else Pause(0); };
            v.EditLimit += key =>
            {
                AppUsage u;
                double today = tracker.Today.Apps.TryGetValue(key, out u) ? u.Seconds : 0;
                if (Dialogs.EditLimit(dash, bigIcon, key, today)) tracker.OnLimitChanged(key);
                v.Invalidate();
            };
            v.OpenSearch += ShowSearch;
            v.ExportPdf += (mode, date) =>
            {
                string kind = mode == 1 ? "week" : mode == 2 ? "month" : "day";
                Dialogs.ExportReport(dash, bigIcon, tracker, date, kind);
            };
            v.ManageProjects += delegate { if (Dialogs.ManageProjects(dash, bigIcon)) { v.ClearCache(); v.Invalidate(); } };
            v.StartFocus += StartFocus;
            v.StopFocus += StopFocus;
            v.CopyNote += CopyNote;
            v.Timesheet += Timesheet;
            v.EditManual += EditManual;
            v.MakeWrapped += MakeWrapped;
            dash.FormClosed += delegate { dash = null; };
        }
        if (!dash.Visible) dash.Show();
        if (dash.WindowState == FormWindowState.Minimized || Native.IsIconic(dash.Handle)) Native.ShowWindow(dash.Handle, 9); // SW_RESTORE
        dash.Activate();
        Native.SetForegroundWindow(dash.Handle);
    }

    void ShowSearch()
    {
        if (search == null || search.IsDisposed)
        {
            search = new SearchForm(tracker, bigIcon);
            search.OpenDay += d => { ShowDashboard(); if (dash != null) { dash.View.Date = d; dash.View.Mode = 3; dash.View.Invalidate(); } };
        }
        search.Show();
        search.Activate();
    }

    void EditManual(DateTime date, ManualEntry m)
    {
        double now = (Clock.Now - Clock.Today).TotalSeconds;
        if (Dialogs.EditManual(dash, bigIcon, tracker, date, m, Math.Max(0, now - 3600), now))
            if (dash != null && !dash.IsDisposed) { dash.View.ClearCache(); dash.View.Invalidate(); }
    }

    void AskAway(double start, double end)
    {
        if (awayPrompt != null && !awayPrompt.IsDisposed) awayPrompt.Close();
        awayPrompt = new AwayPromptForm(tracker, bigIcon, start, end);
        awayPrompt.FormClosed += delegate { if (dash != null && !dash.IsDisposed) dash.View.Invalidate(); };
        awayPrompt.Show();
    }

    void StartFocus(int minutes)
    {
        tracker.StartFocus(minutes);
        UpdateTray();
        if (dash != null && !dash.IsDisposed) dash.View.Invalidate();
    }

    void StopFocus()
    {
        tracker.EndFocus(false);
        UpdateTray();
        if (dash != null && !dash.IsDisposed) dash.View.Invalidate();
    }

    void Pause(int minutes)
    {
        tracker.SetPaused(true, minutes);
        UpdateTray();
        if (dash != null && !dash.IsDisposed) dash.View.Invalidate();
    }

    void Resume()
    {
        if (tracker.Paused) tracker.SetPaused(false);
        UpdateTray();
        if (dash != null && !dash.IsDisposed) dash.View.Invalidate();
    }

    void CopyNote(DateTime date)
    {
        var note = Insights.StandupNote(Program.DayGetter(tracker)(date));
        try { Clipboard.SetText(note); } catch { }
        Notify("Work note copied", "Paste it into WhatsApp, Slack, email or a timesheet:\n" + (note.Length > 180 ? note.Substring(0, 180) + "…" : note), null);
    }

    void Timesheet(DateTime month)
    {
        var ms = new DateTime(month.Year, month.Month, 1);
        using (var d = new SaveFileDialog())
        {
            d.Filter = "CSV file (*.csv)|*.csv";
            d.InitialDirectory = Settings.ReportDir;
            d.FileName = "Timesheet " + ms.ToString("yyyy-MM", CultureInfo.InvariantCulture) + ".csv";
            if (d.ShowDialog(dash) != DialogResult.OK) return;
            try
            {
                tracker.Save();
                Projects.ExportTimesheet(d.FileName, ms, ms.AddMonths(1).AddDays(-1), Program.DayGetter(tracker));
                Process.Start(d.FileName);
            }
            catch (Exception ex) { MessageBox.Show("Timesheet export failed: " + ex.Message, "Timesheet", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }

    void MakeWrapped(DateTime from, DateTime to, string title)
    {
        try
        {
            tracker.Save();
            string path = Path.Combine(Settings.ReportDir, "Screen Time Wrapped " + title.Replace(" so far", "") + ".png");
            Wrapped.Make(from, to, title, Program.DayGetter(tracker), path);
            Process.Start(path);
        }
        catch (Exception ex) { MessageBox.Show("Couldn't make the card: " + ex.Message, "Screen Time Wrapped", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    void QuickTodayPdf()
    {
        string path = Path.Combine(Settings.ReportDir, PdfReport.DefaultName(Clock.Today, Clock.Today, "day"));
        Dialogs.MakeReport(path, Clock.Today, Clock.Today, "day", tracker, true);
    }

    // Every Monday: last week's PDF is saved to the reports folder.
    void WeeklyReport()
    {
        if (!Settings.WeeklyPdf) return;
        var thisWeek = Clock.Today.AddDays(-(((int)Clock.Today.DayOfWeek + 6) % 7));
        var lastWeek = thisWeek.AddDays(-7);
        string key = lastWeek.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (Settings.LastWeeklyReport == key) return;
        bool any = false;
        for (int i = 0; i < 7 && !any; i++) any = Store.LoadDay(lastWeek.AddDays(i)).Total >= 600;
        Settings.LastWeeklyReport = key;
        Settings.Save();
        if (!any) return;
        string path = Path.Combine(Settings.ReportDir, PdfReport.DefaultName(lastWeek, lastWeek.AddDays(6), "week"));
        if (Dialogs.MakeReport(path, lastWeek, lastWeek.AddDays(6), "week", tracker, false) != null)
            Notify("Your weekly report is ready 📄", "Last week's screen time report was saved. Click to open it.", () => { try { Process.Start(path); } catch { } });
    }

    void CheckUpdates()
    {
        if (!Settings.CheckUpdates) return;
        Updater.CheckAsync(v => invoker.BeginInvoke((Action)delegate
        {
            Notify("Update available: version " + v, "A new version of Screen Time Tracker is out. Click to see what's new and download it.",
                () => { try { Process.Start(Updater.ReleasesUrl); } catch { } });
        }));
    }

    void OpenSettings(IWin32Window owner)
    {
        var r = Dialogs.ShowSettings(owner, bigIcon, tracker);
        if (r == Dialogs.SettingsResult.None) return;
        Theme.Apply(Settings.Theme);
        ApplyExtras();
        if (dash != null && !dash.IsDisposed)
        {
            dash.View.ClearCache();
            dash.ApplyTheme();
        }
        UpdateTray();
    }

    void OnSessionSwitch(object s, SessionSwitchEventArgs e)
    {
        invoker.BeginInvoke((Action)delegate
        {
            if (e.Reason == SessionSwitchReason.SessionLock || e.Reason == SessionSwitchReason.ConsoleDisconnect || e.Reason == SessionSwitchReason.RemoteDisconnect)
            { tracker.SetLocked(true); pinOkUntil = DateTime.MinValue; }
            else if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.ConsoleConnect || e.Reason == SessionSwitchReason.RemoteConnect)
                tracker.SetLocked(false);
        });
    }

    void OnPower(object s, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
            invoker.BeginInvoke((Action)delegate { tracker.Log("sleep", "PC went to sleep"); tracker.Save(); });
        else if (e.Mode == PowerModes.Resume)
            invoker.BeginInvoke((Action)delegate { tracker.Log("wake", "PC woke up"); Clock.SyncAsync(); });
    }

    void OnSessionEnding(object s, SessionEndingEventArgs e)
    {
        tracker.Log("logoff", e.Reason == SessionEndReasons.SystemShutdown ? "Shutting down" : "Signing out");
        tracker.Save();
        Settings.Save();
    }

    void OnTimeChanged(object s, EventArgs e) { Clock.SyncAsync(); }

    void OnPrefs(object s, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General || Settings.Theme != "system") return;
        invoker.BeginInvoke((Action)delegate
        {
            Theme.Apply(Settings.Theme);
            if (dash != null && !dash.IsDisposed) dash.ApplyTheme();
        });
    }

    void Quit()
    {
        timer.Stop();
        tracker.EndFocus(false);
        tracker.Log("stop", "Screen Time Tracker closed");
        tracker.Save();
        Settings.Save();
        tray.Visible = false;
        if (dash != null && !dash.IsDisposed) dash.Close();
        if (widget != null && !widget.IsDisposed) widget.Close();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.PowerModeChanged -= OnPower;
            SystemEvents.SessionEnding -= OnSessionEnding;
            SystemEvents.UserPreferenceChanged -= OnPrefs;
            SystemEvents.TimeChanged -= OnTimeChanged;
            if (showWait != null) showWait.Unregister(null);
            if (hotkey != null) hotkey.Dispose();
            timer.Dispose();
            tray.Dispose();
            invoker.Dispose();
        }
        base.Dispose(disposing);
    }
}

// Fills a data folder with two weeks of realistic sample usage (only used with --data <folder> --seed-demo).
static class Demo
{
    class A { public string Key, Name, Path; public int W; public string[][] Items; }

    public static void Seed()
    {
        string win = Environment.GetEnvironmentVariable("WINDIR") ?? @"C:\Windows";
        var apps = new[]
        {
            new A { Key = "chrome.exe", Name = "Google Chrome", Path = "", W = 30, Items = new[] {
                new[] { "Sales Tracker FY26", "Google Sheets" }, new[] { "Inbox (3) - rahul@example.com", "Gmail" },
                new[] { "Monthly GST summary", "Google Sheets" }, new[] { "lofi hip hop radio - beats to relax", "YouTube" },
                new[] { "Amazon.in : office chair", "amazon.in" }, new[] { "ChatGPT", "chatgpt.com" }, new[] { "Instagram", "instagram.com" } } },
            new A { Key = "brave.exe", Name = "Brave Browser", Path = "", W = 12, Items = new[] {
                new[] { "Client proposal – Sharma Traders", "Google Docs" }, new[] { "Stock market today - Moneycontrol", "moneycontrol.com" },
                new[] { "IPL highlights", "YouTube" }, new[] { "LinkedIn Feed", "linkedin.com" } } },
            new A { Key = "excel.exe", Name = "Microsoft Excel", Path = "", W = 14, Items = new[] {
                new[] { "Budget FY26.xlsx", null }, new[] { "Sharma invoice register.xlsx", null }, new[] { "Stock ledger.xlsx", null } } },
            new A { Key = "tallyprime.exe", Name = "TallyPrime", Path = "", W = 8, Items = new string[0][] },
            new A { Key = "whatsapp.exe", Name = "WhatsApp", Path = "", W = 9, Items = new string[0][] },
            new A { Key = "teams.exe", Name = "Microsoft Teams", Path = "", W = 6, Items = new[] { new[] { "Chat | Accounts team", null }, new[] { "Weekly review | Meeting", null } } },
            new A { Key = "explorer.exe", Name = "File Explorer", Path = Path.Combine(win, "explorer.exe"), W = 4, Items = new[] { new[] { "Downloads", null }, new[] { "Invoices 2026", null } } },
            new A { Key = "calculatorapp.exe", Name = "Calculator", Path = "", W = 2, Items = new string[0][] },
            new A { Key = "notepad.exe", Name = "Notepad", Path = Path.Combine(win, @"System32\notepad.exe"), W = 2, Items = new[] { new[] { "todo.txt", null } } },
            new A { Key = "spotify.exe", Name = "Spotify", Path = "", W = 3, Items = new string[0][] },
        };
        foreach (var a in apps) Store.Remember(a.Key, a.Name, a.Path);
        if (Projects.All.Count == 0)
        {
            Projects.All.Add(new Project { Name = "Sharma Traders", Rate = 1500, Keywords = new List<string> { "Sharma" } });
            Projects.All.Add(new Project { Name = "Accounts & GST", Rate = 800, Keywords = new List<string> { "GST", "Stock ledger" }, Apps = new List<string> { "tallyprime.exe" } });
            Projects.All.Add(new Project { Name = "Sales", Rate = 0, Keywords = new List<string> { "Sales Tracker", "Budget" } });
            Projects.Save();
        }
        var rnd = new Random(7);
        var now = Clock.Now;
        for (int back = 30; back >= 0; back--)
        {
            var d = new DayData();
            d.Date = Clock.Today.AddDays(-back);
            bool weekend = d.Date.DayOfWeek == DayOfWeek.Saturday || d.Date.DayOfWeek == DayOfWeek.Sunday;
            if (d.Date.DayOfWeek == DayOfWeek.Sunday && rnd.NextDouble() < 0.6) continue;
            double dayStart = (weekend ? 10.5 : 9) * 3600 + rnd.Next(-600, 1500), dayEnd = (weekend ? 15 : 18.6) * 3600 + rnd.Next(0, 4000);
            if (back == 0) dayEnd = Math.Min(dayEnd, (now - now.Date).TotalSeconds - 60);
            d.AddEvent(dayStart - 90, "boot", "PC started");
            d.AddEvent(dayStart - 40, "start", "Screen Time Tracker started");
            double t = dayStart;
            bool lunchDone = false, meetDone = weekend;
            while (t < dayEnd)
            {
                if (!lunchDone && t > 13.2 * 3600)
                {
                    lunchDone = true;
                    d.AddEvent(t, "lock", "PC locked");
                    double back2 = t + 2400 + rnd.Next(0, 900);
                    d.Manual.Add(new ManualEntry { Start = t, End = back2, Kind = "break", Label = "Lunch" });
                    t = back2;
                    d.AddEvent(t, "unlock", "PC unlocked");
                    d.Unlocks++;
                    continue;
                }
                if (!meetDone && t > 15.5 * 3600)
                {
                    meetDone = true;
                    double len = 1800 + rnd.Next(0, 1800);
                    d.Manual.Add(new ManualEntry { Start = t, End = t + len, Kind = "work", Label = "Client visit", Client = "Sharma Traders" });
                    t += len;
                    continue;
                }
                if (rnd.NextDouble() < 0.03)
                {
                    d.AddEvent(t, "call", "On a call / meeting (Microsoft Teams)");
                    double len = 900 + rnd.Next(0, 1500);
                    d.MeetingSeconds += len;
                    var u0 = d.Get("teams.exe"); u0.Seconds += len; u0.Opens++;
                    for (double s = t; s < t + len; ) { int h = (int)(s / 3600); double e = Math.Min(t + len, (h + 1) * 3600.0); u0.Hours[Math.Min(23, h)] += e - s; s = e; }
                    var it0 = d.GetItem("teams.exe", "Weekly review | Meeting"); it0.Seconds += len; it0.Opens++;
                    d.Segments.Add(new Segment { Start = t, End = t + len, Key = "teams.exe", Item = it0 });
                    t += len;
                    d.AddEvent(t, "callend", "Call ended (Microsoft Teams, " + Util.Fmt(len) + ")");
                    continue;
                }
                if (rnd.NextDouble() < 0.04)
                {
                    d.AddEvent(t, "away", "Went idle – no keyboard or mouse input");
                    t += 600 + rnd.Next(0, 1200);
                    d.AddEvent(t, "back", "Back at the PC");
                    continue;
                }
                int wsum = 0;
                foreach (var a in apps) wsum += Weight(a, weekend);
                int pick = rnd.Next(wsum);
                A app = apps[0];
                foreach (var a in apps) { pick -= Weight(a, weekend); if (pick < 0) { app = a; break; } }
                double dur = app.Key == "calculatorapp.exe" ? 20 + rnd.Next(60) : 60 + rnd.Next(0, 1500);
                if (rnd.NextDouble() < 0.25) dur = 5 + rnd.Next(10);
                dur = Math.Min(dur, dayEnd - t);
                if (dur < 1) break;
                ItemUsage item = null;
                if (app.Items.Length > 0)
                {
                    var it = app.Items[rnd.Next(app.Items.Length)];
                    item = d.GetItem(app.Key, it[0]);
                    item.Site = it[1];
                    item.Seconds += dur;
                    item.Opens++;
                }
                var u = d.Get(app.Key);
                u.Seconds += dur; u.Opens++;
                for (double s = t; s < t + dur; )
                {
                    int h = (int)(s / 3600);
                    double e = Math.Min(t + dur, (h + 1) * 3600.0);
                    u.Hours[Math.Min(23, h)] += e - s;
                    s = e;
                }
                d.Segments.Add(new Segment { Start = t, End = t + dur, Key = app.Key, Item = item });
                t += dur + (rnd.NextDouble() < 0.3 ? rnd.Next(1, 40) : 0);
            }
            if (back > 0)
            {
                d.AddEvent(dayEnd + 20, "lock", "PC locked");
                d.AddEvent(dayEnd + 60, "logoff", "Shutting down");
                d.AddEvent(dayEnd + 75, "shutdown", "PC shut down");
            }
            Store.SaveDay(d);
        }
        Store.SaveInfos();
    }

    static int Weight(A a, bool weekend)
    {
        if (weekend && (a.Key == "excel.exe" || a.Key == "teams.exe" || a.Key == "tallyprime.exe")) return a.W / 4;
        return a.W;
    }
}
