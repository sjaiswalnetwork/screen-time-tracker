using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

class DashboardForm : Form
{
    public readonly DashView View;
    readonly Timer refresh = new Timer();

    public DashboardForm(Tracker tracker, Icon icon)
    {
        Text = "Screen Time Tracker";
        Icon = icon;
        StartPosition = FormStartPosition.CenterScreen;
        View = new DashView();
        View.Tracker = tracker;
        View.Dock = DockStyle.Fill;
        Controls.Add(View);
        BackColor = Theme.Current.Bg;

        float s;
        using (var g = CreateGraphics()) s = g.DpiX / 96f;
        var wa = Screen.PrimaryScreen.WorkingArea;
        ClientSize = new Size(Math.Min((int)(940 * s), wa.Width - 40), Math.Min((int)(940 * s), wa.Height - 40));
        MinimumSize = new Size((int)(820 * s), (int)(480 * s));

        refresh.Interval = 1000;
        refresh.Tick += delegate { if (Visible && WindowState != FormWindowState.Minimized) View.Invalidate(); };
        refresh.Start();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.DarkTitleBar(Handle, Theme.Current.Dark);
    }

    public void ApplyTheme()
    {
        BackColor = Theme.Current.Bg;
        Native.DarkTitleBar(Handle, Theme.Current.Dark);
        View.Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) refresh.Dispose();
        base.Dispose(disposing);
    }
}

// Small themed dialogs built from standard controls (more in Dialogs2.cs).
static partial class Dialogs
{
    internal static float Scale(Control f) { using (var g = f.CreateGraphics()) return g.DpiX / 96f; }

    internal static Form NewForm(string title, Icon icon)
    {
        var f = new Form();
        f.Text = title;
        f.Icon = icon;
        f.FormBorderStyle = FormBorderStyle.FixedDialog;
        f.MaximizeBox = f.MinimizeBox = false;
        f.ShowInTaskbar = false;
        f.StartPosition = FormStartPosition.CenterScreen;
        f.Font = new Font("Segoe UI", 9.5f);
        f.BackColor = Theme.Current.Surface;
        f.ForeColor = Theme.Current.Text;
        f.HandleCreated += delegate { Native.DarkTitleBar(f.Handle, Theme.Current.Dark); };
        return f;
    }

    internal static Label AddLabel(Control f, string text, int x, int y, int w, bool muted = false)
    {
        var l = new Label();
        l.Text = text; l.AutoSize = false; l.Location = new Point(x, y); l.Size = new Size(w, (int)(f.Font.Height * 1.5));
        if (muted) l.ForeColor = Theme.Current.Muted;
        f.Controls.Add(l);
        return l;
    }

    internal static Button AddButton(Control f, string text, int x, int y, int w, int h, bool primary = false)
    {
        var b = new Button();
        b.Text = text; b.Location = new Point(x, y); b.Size = new Size(w, h);
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderColor = primary ? Theme.Current.Accent : Theme.Current.Border;
        b.BackColor = primary ? Theme.Current.Accent : Theme.Current.Surface;
        b.ForeColor = primary ? Color.White : Theme.Current.Text;
        b.Cursor = Cursors.Hand;
        f.Controls.Add(b);
        return b;
    }

    internal static NumericUpDown AddNumber(Control f, int value, int min, int max, int x, int y, int w)
    {
        var n = new NumericUpDown();
        n.Minimum = min; n.Maximum = max; n.Value = Math.Max(min, Math.Min(max, value));
        n.Location = new Point(x, y); n.Width = w;
        n.BackColor = Theme.Current.Dark ? Theme.Current.Bg : Color.White;
        n.ForeColor = Theme.Current.Text;
        f.Controls.Add(n);
        return n;
    }

    internal static CheckBox AddCheck(Control f, string text, bool on, int x, int y, int w)
    {
        var c = new CheckBox();
        c.Text = text; c.Checked = on; c.Location = new Point(x, y); c.Size = new Size(w, (int)(f.Font.Height * 1.7));
        f.Controls.Add(c);
        return c;
    }

    internal static TextBox AddText(Control f, string text, int x, int y, int w, int h = 0)
    {
        var t = new TextBox();
        t.Text = text; t.Location = new Point(x, y); t.Width = w;
        if (h > 0) { t.Multiline = true; t.Height = h; t.ScrollBars = ScrollBars.Vertical; }
        t.BackColor = Theme.Current.Dark ? Theme.Current.Bg : Color.White;
        t.ForeColor = Theme.Current.Text;
        f.Controls.Add(t);
        return t;
    }

    internal static DateTimePicker AddTime(Control f, int minutes, int x, int y, int w)
    {
        var p = new DateTimePicker();
        p.Format = DateTimePickerFormat.Custom;
        p.CustomFormat = "hh:mm tt";
        p.ShowUpDown = true;
        p.Value = DateTime.Today.AddMinutes(Math.Max(0, Math.Min(1439, minutes)));
        p.Location = new Point(x, y); p.Width = w;
        f.Controls.Add(p);
        return p;
    }

    internal static ComboBox AddCombo(Control f, IEnumerable<string> items, string value, int x, int y, int w, bool editable)
    {
        var c = new ComboBox();
        c.DropDownStyle = editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList;
        foreach (var i in items) c.Items.Add(i);
        if (editable) c.Text = value ?? "";
        else c.SelectedIndex = Math.Max(0, c.Items.IndexOf(value ?? ""));
        c.Location = new Point(x, y); c.Width = w;
        f.Controls.Add(c);
        return c;
    }

    static int Minutes(DateTimePicker p) { return p.Value.Hour * 60 + p.Value.Minute; }

    /// Returns true if the limit changed.
    public static bool EditLimit(IWin32Window owner, Icon icon, string key, double todaySeconds)
    {
        var info = Store.Info(key);
        using (var f = NewForm("Daily limit", icon))
        {
            float s = Scale(f);
            int P = (int)(20 * s), W = (int)(380 * s);
            int cur;
            Settings.Limits.TryGetValue(key, out cur);
            int y = P;
            var title = AddLabel(f, info.Name, P, y, W);
            title.Font = new Font("Segoe UI Semibold", 12f);
            title.Height = (int)(30 * s);
            y += (int)(34 * s);
            AddLabel(f, "Used today: " + Util.Fmt(todaySeconds) + ". You'll get a notification when the daily limit is reached.", P, y, W, true).Height = (int)(42 * s);
            y += (int)(50 * s);
            AddLabel(f, "Daily limit (minutes):", P, y + (int)(3 * s), (int)(170 * s));
            var mins = AddNumber(f, cur > 0 ? cur : 60, 1, 1440, P + (int)(180 * s), y, (int)(90 * s));
            mins.Increment = 15;
            y += (int)(48 * s);
            int bh = (int)(32 * s), bw = (int)(100 * s);
            var ok = AddButton(f, "Save", P + W - bw, y, bw, bh, true);
            var cancel = AddButton(f, "Cancel", P + W - bw * 2 - (int)(8 * s), y, bw, bh);
            Button remove = null;
            if (cur > 0) remove = AddButton(f, "Remove limit", P, y, (int)(120 * s), bh);
            f.ClientSize = new Size(W + P * 2, y + bh + P);
            f.AcceptButton = ok; f.CancelButton = cancel;
            ok.DialogResult = DialogResult.OK; cancel.DialogResult = DialogResult.Cancel;
            if (remove != null) remove.DialogResult = DialogResult.Abort;
            var r = f.ShowDialog(owner);
            if (r == DialogResult.OK) Settings.Limits[key] = (int)mins.Value;
            else if (r == DialogResult.Abort) Settings.Limits.Remove(key);
            else return false;
            Settings.Save();
            return true;
        }
    }

    public enum SettingsResult { None, Saved, DataDeleted }

    // Settings, split over tabs: General · Tracking · Goals & reminders · Work & focus · Reports & data
    public static SettingsResult ShowSettings(IWin32Window owner, Icon icon, Tracker tracker)
    {
        using (var f = NewForm("Settings", icon))
        {
            float s = Scale(f);
            int P = (int)(18 * s), W = (int)(520 * s), gap = (int)(10 * s), row = (int)(34 * s);
            Func<int, int> S = v => (int)(v * s);
            var tabs = new TabControl();
            tabs.Location = new Point(S(10), S(10));
            tabs.Size = new Size(W + P * 2 + S(10), S(560));
            f.Controls.Add(tabs);
            Func<string, TabPage> Page = name =>
            {
                var p = new TabPage(name);
                p.BackColor = Theme.Current.Surface; p.ForeColor = Theme.Current.Text; p.AutoScroll = true;
                tabs.TabPages.Add(p);
                return p;
            };
            bool deleted = false;

            // ---- General ----
            var g = Page("General");
            int y = P;
            var startup = AddCheck(g, "Start automatically when I sign in to Windows", Settings.StartWithWindows, P, y, W); y += row;
            var hotkey = AddCheck(g, "Open the dashboard with Ctrl + Alt + S", Settings.Hotkey, P, y, W); y += row;
            var trayHours = AddCheck(g, "Show today's hours on the tray icon", Settings.TrayHours, P, y, W); y += row;
            var widget = AddCheck(g, "Show the mini widget (today's time, always on top)", Settings.WidgetOn, P, y, W); y += row;
            var updates = AddCheck(g, "Tell me when a new version is available", Settings.CheckUpdates, P, y, W); y += row + S(6);
            AddLabel(g, "Theme", P, y + S(3), S(120));
            var theme = AddCombo(g, new[] { "Match Windows", "Light", "Dark" }, Settings.Theme == "light" ? "Light" : Settings.Theme == "dark" ? "Dark" : "Match Windows", P + S(130), y, S(170), false);
            y += row;
            AddLabel(g, "Language", P, y + S(3), S(120));
            var lang = AddCombo(g, new[] { "English", "हिन्दी (Hindi)" }, Settings.Language == "hi" ? "हिन्दी (Hindi)" : "English", P + S(130), y, S(170), false);
            y += row + S(6);
            AddLabel(g, "World clocks (pick up to 6)", P, y, W); y += S(24);
            var clocks = new CheckedListBox();
            clocks.CheckOnClick = true; clocks.MultiColumn = true; clocks.ColumnWidth = W / 4 - S(6);
            clocks.BorderStyle = BorderStyle.FixedSingle;
            clocks.BackColor = Theme.Current.Dark ? Theme.Current.Bg : Color.White; clocks.ForeColor = Theme.Current.Text;
            foreach (var c in Clock.Cities) clocks.Items.Add(c.Value, Settings.WorldClocks.Contains(c.Key));
            clocks.Location = new Point(P, y); clocks.Size = new Size(W, S(112));
            g.Controls.Add(clocks);
            y += clocks.Height + S(10);
            var clockStatus = AddLabel(g, Clock.Status(), P, y + S(4), W - S(110), true);
            var syncBtn = AddButton(g, "Sync now", P + W - S(100), y, S(100), S(28));
            syncBtn.Click += delegate { clockStatus.Text = "Checking internet time…"; clockStatus.Refresh(); Clock.Sync(); clockStatus.Text = Clock.Status(); };

            // ---- Tracking ----
            var t = Page("Tracking");
            y = P;
            var titles = AddCheck(t, "Record window titles and websites (which sheet, page or file)", Settings.RecordTitles, P, y, W); y += row;
            AddLabel(t, "Count me as away after", P, y + S(3), S(250));
            var idle = AddNumber(t, Settings.IdleMinutes, 1, 60, P + S(260), y, S(70));
            AddLabel(t, "min without input", P + S(340), y + S(3), S(170)); y += row;
            var audio = AddCheck(t, "Keep counting while sound is playing or the microphone is in use", Settings.AudioKeepsActive, P, y, W); y += row;
            AddLabel(t, "Ask what I was doing after being away for", P, y + S(3), S(250));
            var ask = AddNumber(t, Settings.AskReasonMinutes, 0, 600, P + S(260), y, S(70));
            AddLabel(t, "min (0 = never)", P + S(340), y + S(3), S(170)); y += row + S(8);
            AddLabel(t, "Never track these apps (one per line, e.g. KeePass or keepass.exe):", P, y, W); y += S(24);
            var ignore = AddText(t, string.Join("\r\n", Settings.IgnoreApps), P, y, W, S(110)); y += S(120);
            AddLabel(t, "Ignored apps are not counted anywhere – useful for password managers or private apps.", P, y, W, true);

            // ---- Goals & reminders ----
            var r = Page("Goals & reminders");
            y = P;
            Func<Control, string, int, int, int, int, string, NumericUpDown> NumRow = (page, label, val, min, max, yy, unit) =>
            {
                AddLabel(page, label, P, yy + S(3), S(300));
                var n = AddNumber(page, val, min, max, P + S(310), yy, S(80));
                AddLabel(page, unit, P + S(400), yy + S(3), S(130));
                return n;
            };
            var goal = NumRow(r, "Daily screen time goal", Settings.DailyGoalMinutes, 0, 1440, y, "min (0 = off)"); y += row;
            var gProd = NumRow(r, "At least this much productive time", Settings.GoalProductiveMin, 0, 1440, y, "min (0 = off)"); y += row;
            var gEnt = NumRow(r, "At most this much entertainment", Settings.GoalEntertainmentMax, 0, 1440, y, "min (0 = off)"); y += row;
            var gSoc = NumRow(r, "At most this much social media", Settings.GoalSocialMax, 0, 1440, y, "min (0 = off)"); y += row + S(10);
            var brk = NumRow(r, "Break reminder every", Settings.BreakMinutes, 0, 480, y, "min (0 = off)"); y += row;
            var water = NumRow(r, "Water reminder every", Settings.WaterMinutes, 0, 480, y, "min (0 = off)"); y += row;
            var eye = AddCheck(r, "Eye care: 20-20-20 reminder every 20 minutes (at a pause in typing)", Settings.EyeCare, P, y, W); y += row;
            var late = AddCheck(r, "Late-night warning at", Settings.LateNightMin >= 0, P, y, S(200));
            var lateAt = AddTime(r, Settings.LateNightMin >= 0 ? Settings.LateNightMin : 23 * 60, P + S(310), y, S(120));

            // ---- Work & focus ----
            var w = Page("Work & focus");
            y = P;
            AddLabel(w, "Work hours", P, y + S(3), S(140));
            var ws = AddTime(w, Settings.WorkStart, P + S(150), y, S(110));
            AddLabel(w, "to", P + S(268), y + S(3), S(30));
            var we = AddTime(w, Settings.WorkEnd, P + S(300), y, S(110)); y += row;
            AddLabel(w, "Work days", P, y + S(3), S(140));
            string[] dn = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
            var dayChecks = new CheckBox[7];
            for (int i = 0; i < 7; i++) dayChecks[i] = AddCheck(w, dn[i], Settings.WorkDays[i] == '1', P + S(150) + i * S(52), y, S(52));
            y += row;
            AddLabel(w, "Late / early grace", P, y + S(3), S(140));
            var grace = AddNumber(w, Settings.GraceMinutes, 0, 120, P + S(150), y, S(70));
            AddLabel(w, "min", P + S(228), y + S(3), S(60));
            AddLabel(w, "Currency", P + S(300), y + S(3), S(70));
            var cur = AddText(w, Settings.Currency, P + S(375), y, S(60)); y += row + S(4);
            var projBtn = AddButton(w, "Manage projects & clients…", P, y, S(220), S(30));
            projBtn.Click += delegate { ManageProjects(f, icon); };
            AddLabel(w, "Rules that turn screen time into hours per client.", P + S(230), y + S(6), W - S(230), true);
            y += row + S(16);
            AddLabel(w, "Focus mode blocks these categories:", P, y, W); y += S(24);
            var blockCats = new CheckBox[4];
            string[] bc = { "Entertainment", "Social", "Communication", "Browsing" };
            for (int i = 0; i < 4; i++) blockCats[i] = AddCheck(w, bc[i], Settings.FocusBlockCategories.Contains(bc[i]), P + i * S(130), y, S(128));
            y += row;
            AddLabel(w, "…and these apps or sites (one per line, e.g. youtube.com, spotify.exe):", P, y, W); y += S(24);
            var blockExtra = AddText(w, string.Join("\r\n", Settings.FocusBlockExtra), P, y, W, S(90));

            // ---- Reports & data ----
            var dpage = Page("Reports & data");
            y = P;
            var weekly = AddCheck(dpage, "Save a weekly PDF report every Monday", Settings.WeeklyPdf, P, y, W); y += row;
            AddLabel(dpage, "Reports folder", P, y + S(3), S(120));
            var repFolder = AddText(dpage, Settings.ReportDir, P + S(125), y, W - S(225));
            var repBrowse = AddButton(dpage, "Browse…", P + W - S(92), y - S(2), S(92), S(28));
            repBrowse.Click += delegate { var p = PickFolder(f, repFolder.Text); if (p != null) repFolder.Text = p; };
            y += row + S(4);
            int bw3 = (W - gap * 2) / 3, bh = S(32);
            var pdf = AddButton(dpage, "Export PDF report…", P, y, bw3, bh);
            var export = AddButton(dpage, "Export CSV…", P + bw3 + gap, y, bw3, bh);
            var open = AddButton(dpage, "Open data folder", P + (bw3 + gap) * 2, y, bw3, bh);
            y += bh + gap;
            var backup = AddButton(dpage, "Backup now…", P, y, bw3, bh);
            var pinBtn = AddButton(dpage, Settings.PinHash.Length > 0 ? "Change / remove PIN…" : "Protect with a PIN…", P + bw3 + gap, y, bw3, bh);
            var wipe = AddButton(dpage, "Delete all history…", P + (bw3 + gap) * 2, y, bw3, bh);
            wipe.ForeColor = Theme.Current.Danger;
            y += bh + S(14);
            AddLabel(dpage, "Keep history for", P, y + S(3), S(120));
            var keep = AddNumber(dpage, Settings.KeepDays, 0, 3650, P + S(125), y, S(80));
            AddLabel(dpage, "days (0 = forever)", P + S(212), y + S(3), S(200)); y += row + S(6);
            AddLabel(dpage, "Use on more than one PC (e.g. office + home) via a shared folder like OneDrive:", P, y, W); y += S(24);
            AddLabel(dpage, "Copy this PC's data to", P, y + S(3), S(160));
            var mirror = AddText(dpage, Settings.MirrorFolder, P + S(165), y, W - S(265));
            var mirBrowse = AddButton(dpage, "Browse…", P + W - S(92), y - S(2), S(92), S(28));
            mirBrowse.Click += delegate { var p = PickFolder(f, mirror.Text); if (p != null) mirror.Text = p; };
            y += row;
            AddLabel(dpage, "Include other PCs' folders (one per line – their \"<PC name>\" folder inside the shared folder):", P, y, W); y += S(24);
            var extra = AddText(dpage, string.Join("\r\n", Settings.ExtraFolders), P, y, W, S(60)); y += S(66);
            AddLabel(dpage, "Your data stays on this PC: " + Store.Root, P, y, W, true);

            pdf.Click += delegate { ExportReport(f, icon, tracker, Clock.Today, "day"); };
            export.Click += delegate
            {
                using (var d = new SaveFileDialog())
                {
                    d.Filter = "CSV file (*.csv)|*.csv";
                    d.FileName = "screen-time-" + Clock.Today.ToString("yyyy-MM-dd") + ".csv";
                    d.InitialDirectory = Settings.ReportDir;
                    if (d.ShowDialog(f) != DialogResult.OK) return;
                    try
                    {
                        tracker.Save();
                        var files = Store.ExportCsv(d.FileName, tracker.Today);
                        MessageBox.Show(f, "Exported:\n\n" + string.Join("\n", files.Select(System.IO.Path.GetFileName)) +
                            "\n\n(apps per day, the full activity log, and log-in/log-out times)", "Export", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex) { MessageBox.Show(f, "Export failed: " + ex.Message, "Export", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                }
            };
            open.Click += delegate { try { System.Diagnostics.Process.Start("explorer.exe", "\"" + Store.Root + "\""); } catch { } };
            backup.Click += delegate
            {
                var p = PickFolder(f, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
                if (p == null) return;
                try { tracker.Save(); var dest = DataTools.Backup(p); MessageBox.Show(f, "Backed up to:\n" + dest, "Backup", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                catch (Exception ex) { MessageBox.Show(f, "Backup failed: " + ex.Message, "Backup", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            };
            pinBtn.Click += delegate { SetPin(f, icon); pinBtn.Text = Settings.PinHash.Length > 0 ? "Change / remove PIN…" : "Protect with a PIN…"; };
            wipe.Click += delegate
            {
                if (MessageBox.Show(f, "Delete all recorded screen time history? This cannot be undone.", "Delete history",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                Store.DeleteAll();
                tracker.ResetToday();
                deleted = true;
                MessageBox.Show(f, "History deleted.", "Delete history", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            int by = tabs.Bottom + S(12), bw = S(100);
            var ok = AddButton(f, "Save", tabs.Right - bw, by, bw, bh, true);
            var cancel = AddButton(f, "Cancel", tabs.Right - bw * 2 - gap, by, bw, bh);
            ok.DialogResult = DialogResult.OK; cancel.DialogResult = DialogResult.Cancel;
            f.AcceptButton = ok; f.CancelButton = cancel;
            f.ClientSize = new Size(tabs.Right + S(10), by + bh + S(12));

            if (f.ShowDialog(owner) != DialogResult.OK) return deleted ? SettingsResult.DataDeleted : SettingsResult.None;
            if (startup.Checked != Settings.StartWithWindows) Settings.StartWithWindows = startup.Checked;
            Settings.Hotkey = hotkey.Checked; Settings.TrayHours = trayHours.Checked; Settings.WidgetOn = widget.Checked; Settings.CheckUpdates = updates.Checked;
            Settings.Theme = theme.SelectedIndex == 1 ? "light" : theme.SelectedIndex == 2 ? "dark" : "system";
            Settings.Language = lang.SelectedIndex == 1 ? "hi" : "en";
            Settings.WorldClocks = Enumerable.Range(0, Clock.Cities.Length).Where(i => clocks.GetItemChecked(i)).Select(i => Clock.Cities[i].Key).Take(6).ToList();
            Settings.RecordTitles = titles.Checked;
            Settings.IdleMinutes = (int)idle.Value;
            Settings.AudioKeepsActive = audio.Checked;
            Settings.AskReasonMinutes = (int)ask.Value;
            Settings.IgnoreApps = Lines(ignore.Text);
            Settings.DailyGoalMinutes = (int)goal.Value; Settings.GoalProductiveMin = (int)gProd.Value;
            Settings.GoalEntertainmentMax = (int)gEnt.Value; Settings.GoalSocialMax = (int)gSoc.Value;
            Settings.BreakMinutes = (int)brk.Value; Settings.WaterMinutes = (int)water.Value; Settings.EyeCare = eye.Checked;
            Settings.LateNightMin = late.Checked ? Minutes(lateAt) : -1;
            Settings.WorkStart = Minutes(ws); Settings.WorkEnd = Minutes(we);
            Settings.WorkDays = new string(dayChecks.Select(c => c.Checked ? '1' : '0').ToArray());
            Settings.GraceMinutes = (int)grace.Value;
            Settings.Currency = cur.Text.Trim().Length > 0 ? cur.Text.Trim() : "₹";
            Settings.FocusBlockCategories = Enumerable.Range(0, 4).Where(i => blockCats[i].Checked).Select(i => bc[i]).ToList();
            Settings.FocusBlockExtra = Lines(blockExtra.Text);
            Settings.WeeklyPdf = weekly.Checked;
            Settings.ReportFolder = repFolder.Text.Trim() == Settings.ReportDir ? Settings.ReportFolder : repFolder.Text.Trim();
            Settings.KeepDays = (int)keep.Value;
            Settings.MirrorFolder = mirror.Text.Trim();
            Settings.ExtraFolders = Lines(extra.Text);
            Settings.Save();
            tracker.OnSettingsChanged();
            return deleted ? SettingsResult.DataDeleted : SettingsResult.Saved;
        }
    }

    static List<string> Lines(string text)
    {
        return text.Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
    }

    internal static string PickFolder(IWin32Window owner, string start)
    {
        using (var d = new FolderBrowserDialog())
        {
            d.SelectedPath = start ?? "";
            d.ShowNewFolderButton = true;
            return d.ShowDialog(owner) == DialogResult.OK ? d.SelectedPath : null;
        }
    }
}
