using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

static partial class Dialogs
{
    // ---------- PDF report: any day, week, month or custom range ----------

    public static string ExportReport(IWin32Window owner, Icon icon, Tracker tracker, DateTime date, string kind)
    {
        DateTime from, to; string k;
        using (var f = NewForm("Export PDF report", icon))
        {
            float s = Scale(f);
            Func<int, int> S = v => (int)(v * s);
            int P = S(20), W = S(400), y = P;
            AddLabel(f, "Create a PDF report with screen time, log-in / log-out, timeline, apps, websites, files and the full activity log.", P, y, W, true).Height = S(40);
            y += S(48);
            var rDay = new RadioButton { Text = "One day", Location = new Point(P, y), AutoSize = true, Checked = kind == "day" };
            var rWeek = new RadioButton { Text = "The week of", Location = new Point(P, y + S(30)), AutoSize = true, Checked = kind == "week" };
            var rMonth = new RadioButton { Text = "The month of", Location = new Point(P, y + S(60)), AutoSize = true, Checked = kind == "month" };
            var rCustom = new RadioButton { Text = "From … to …", Location = new Point(P, y + S(90)), AutoSize = true, Checked = kind == "range" };
            f.Controls.AddRange(new Control[] { rDay, rWeek, rMonth, rCustom });
            var pick = new DateTimePicker { Format = DateTimePickerFormat.Long, Location = new Point(P + S(140), y + S(28)), Width = S(250), Value = date, MaxDate = Clock.Today };
            f.Controls.Add(pick);
            y += S(122);
            var pFrom = new DateTimePicker { Format = DateTimePickerFormat.Short, Location = new Point(P + S(30), y), Width = S(150), Value = date.AddDays(-6), MaxDate = Clock.Today };
            var pTo = new DateTimePicker { Format = DateTimePickerFormat.Short, Location = new Point(P + S(220), y), Width = S(150), Value = date, MaxDate = Clock.Today };
            AddLabel(f, "–", P + S(190), y + S(3), S(20));
            f.Controls.Add(pFrom); f.Controls.Add(pTo);
            Action sync = () => { pick.Enabled = !rCustom.Checked; pFrom.Enabled = pTo.Enabled = rCustom.Checked; };
            foreach (var rb in new[] { rDay, rWeek, rMonth, rCustom }) rb.CheckedChanged += delegate { sync(); };
            sync();
            y += S(44);
            int bh = S(32), bw = S(120);
            var ok = AddButton(f, "Create PDF", P + W - bw, y, bw, bh, true);
            var cancel = AddButton(f, "Cancel", P + W - bw * 2 - S(8), y, S(100), bh);
            ok.DialogResult = DialogResult.OK; cancel.DialogResult = DialogResult.Cancel;
            f.AcceptButton = ok; f.CancelButton = cancel;
            f.ClientSize = new Size(W + P * 2, y + bh + P);
            if (f.ShowDialog(owner) != DialogResult.OK) return null;
            var d = pick.Value.Date;
            if (rDay.Checked) { from = to = d; k = "day"; }
            else if (rWeek.Checked) { from = d.AddDays(-(((int)d.DayOfWeek + 6) % 7)); to = from.AddDays(6); k = "week"; }
            else if (rMonth.Checked) { from = new DateTime(d.Year, d.Month, 1); to = from.AddMonths(1).AddDays(-1); k = "month"; }
            else { from = pFrom.Value.Date; to = pTo.Value.Date; if (to < from) { var x = from; from = to; to = x; } k = "range"; }
        }
        using (var sd = new SaveFileDialog())
        {
            sd.Filter = "PDF file (*.pdf)|*.pdf";
            sd.InitialDirectory = Settings.ReportDir;
            sd.FileName = PdfReport.DefaultName(from, to, k);
            if (sd.ShowDialog(owner) != DialogResult.OK) return null;
            return MakeReport(sd.FileName, from, to, k, tracker, true);
        }
    }

    public static string MakeReport(string path, DateTime from, DateTime to, string kind, Tracker tracker, bool open)
    {
        var old = Cursor.Current;
        Cursor.Current = Cursors.WaitCursor;
        try
        {
            tracker.Save();
            var get = Program.DayGetter(tracker);
            if (kind == "day") PdfReport.DayReport(from, tracker, get, path);
            else PdfReport.RangeReport(from, to, kind, tracker, get, path);
            if (open) try { Process.Start(path); } catch { }
            return path;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Couldn't create the report: " + ex.Message, "PDF report", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }
        finally { Cursor.Current = old; }
    }

    // ---------- projects & clients ----------

    public static bool ManageProjects(IWin32Window owner, Icon icon)
    {
        using (var f = NewForm("Projects & clients", icon))
        {
            float s = Scale(f);
            Func<int, int> S = v => (int)(v * s);
            int P = S(16), W = S(720);
            f.FormBorderStyle = FormBorderStyle.Sizable;
            f.MinimumSize = new Size(S(600), S(380));
            AddLabel(f, "Time counts towards a project when a window title or website contains one of its keywords, or when one of its apps is in front. " +
                        "Separate several keywords or apps with commas. The hourly rate is used for timesheets and invoice amounts (" + Settings.Currency + ").", P, P, W, true).Height = S(44);
            var grid = new DataGridView();
            grid.Location = new Point(P, P + S(52)); grid.Size = new Size(W, S(300));
            grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.BackgroundColor = Theme.Current.Surface;
            grid.RowHeadersVisible = false;
            grid.Columns.Add("name", "Project / client"); grid.Columns.Add("rate", "Rate per hour");
            grid.Columns.Add("kw", "Keywords in titles / sites"); grid.Columns.Add("apps", "Apps (optional)");
            grid.Columns[0].FillWeight = 25; grid.Columns[1].FillWeight = 12; grid.Columns[2].FillWeight = 40; grid.Columns[3].FillWeight = 23;
            foreach (var p in Projects.All)
                grid.Rows.Add(p.Name, p.Rate > 0 ? p.Rate.ToString("0.##", CultureInfo.InvariantCulture) : "", string.Join(", ", p.Keywords), string.Join(", ", p.Apps));
            if (Projects.All.Count == 0) grid.Rows.Add("Sharma Traders", "", "Sharma, ST-", "");
            f.Controls.Add(grid);
            int bh = S(32), bw = S(100), by = grid.Bottom + S(12);
            var ok = AddButton(f, "Save", P + W - bw, by, bw, bh, true);
            var cancel = AddButton(f, "Cancel", P + W - bw * 2 - S(8), by, bw, bh);
            ok.Anchor = cancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            ok.DialogResult = DialogResult.OK; cancel.DialogResult = DialogResult.Cancel;
            f.CancelButton = cancel;
            f.ClientSize = new Size(W + P * 2, by + bh + P);
            if (f.ShowDialog(owner) != DialogResult.OK) return false;
            var list = new List<Project>();
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow) continue;
                string name = Convert.ToString(row.Cells[0].Value ?? "").Trim();
                if (name.Length == 0) continue;
                var p = new Project();
                p.Name = name;
                double.TryParse(Convert.ToString(row.Cells[1].Value ?? "").Replace(Settings.Currency, "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out p.Rate);
                p.Keywords = Split(Convert.ToString(row.Cells[2].Value ?? ""));
                p.Apps = Split(Convert.ToString(row.Cells[3].Value ?? ""));
                list.Add(p);
            }
            Projects.All = list;
            Projects.Save();
            return true;
        }
    }

    static List<string> Split(string s) { return s.Split(',', ';').Select(x => x.Trim()).Where(x => x.Length > 0).ToList(); }

    // ---------- offline work / breaks ----------

    static readonly string[] Activities = { "Meeting", "Phone call", "Client visit", "Site visit", "Travel", "Training", "Paper work", "Lunch", "Tea break", "Personal" };

    /// Add or edit an entry. Returns true if something changed.
    public static bool EditManual(IWin32Window owner, Icon icon, Tracker tracker, DateTime date, ManualEntry existing, double defStart, double defEnd, string defLabel = null)
    {
        using (var f = NewForm(existing == null ? "Add offline work or a break" : "Edit entry", icon))
        {
            float s = Scale(f);
            Func<int, int> S = v => (int)(v * s);
            int P = S(20), W = S(400), y = P, lw = S(90);
            AddLabel(f, "Date", P, y + S(3), lw);
            var day = new DateTimePicker { Format = DateTimePickerFormat.Long, Location = new Point(P + lw, y), Width = W - lw, Value = date, MaxDate = Clock.Today };
            f.Controls.Add(day); y += S(36);
            AddLabel(f, "From", P, y + S(3), lw);
            var from = AddTime(f, (int)((existing != null ? existing.Start : defStart) / 60), P + lw, y, S(120));
            AddLabel(f, "to", P + lw + S(130), y + S(3), S(24));
            var to = AddTime(f, (int)((existing != null ? existing.End : defEnd) / 60), P + lw + S(158), y, S(120)); y += S(36);
            AddLabel(f, "What", P, y + S(3), lw);
            var what = AddCombo(f, Activities, existing != null ? existing.Label : defLabel ?? "Meeting", P + lw, y, W - lw, true); y += S(36);
            AddLabel(f, "Client", P, y + S(3), lw);
            var client = AddCombo(f, new[] { "" }.Concat(Projects.All.Select(p => p.Name)), existing != null ? existing.Client : "", P + lw, y, W - lw, true); y += S(36);
            var work = new RadioButton { Text = "Work (counts towards projects & attendance)", Location = new Point(P + lw, y), AutoSize = true };
            var brk = new RadioButton { Text = "Break / personal", Location = new Point(P + lw, y + S(26)), AutoSize = true };
            bool isWork = existing != null ? existing.IsWork : !(defLabel == "Lunch" || defLabel == "Tea break" || defLabel == "Personal");
            work.Checked = isWork; brk.Checked = !isWork;
            f.Controls.Add(work); f.Controls.Add(brk);
            what.TextChanged += delegate { if (what.Text == "Lunch" || what.Text == "Tea break" || what.Text == "Personal") brk.Checked = true; };
            y += S(64);
            int bh = S(32), bw = S(100);
            var ok = AddButton(f, "Save", P + W - bw, y, bw, bh, true);
            var cancel = AddButton(f, "Cancel", P + W - bw * 2 - S(8), y, bw, bh);
            Button del = existing != null ? AddButton(f, "Delete", P, y, bw, bh) : null;
            ok.DialogResult = DialogResult.OK; cancel.DialogResult = DialogResult.Cancel;
            if (del != null) { del.DialogResult = DialogResult.Abort; del.ForeColor = Theme.Current.Danger; }
            f.AcceptButton = ok; f.CancelButton = cancel;
            f.ClientSize = new Size(W + P * 2, y + bh + P);
            while (true)
            {
                var r = f.ShowDialog(owner);
                if (r == DialogResult.Abort) { tracker.RemoveManual(existing, date); return true; }
                if (r != DialogResult.OK) return false;
                double a = from.Value.Hour * 3600 + from.Value.Minute * 60, b = to.Value.Hour * 3600 + to.Value.Minute * 60;
                if (b <= a) { MessageBox.Show(f, "The end time must be after the start time.", "Offline work", MessageBoxButtons.OK, MessageBoxIcon.Warning); continue; }
                if (existing != null) tracker.RemoveManual(existing, date);
                var m = new ManualEntry();
                m.Start = a; m.End = b; m.Label = what.Text.Trim().Length > 0 ? what.Text.Trim() : "Offline work";
                m.Client = client.Text.Trim(); m.Kind = work.Checked ? "work" : "break";
                tracker.AddManual(m, day.Value.Date);
                return true;
            }
        }
    }

    // ---------- PIN ----------

    public static void SetPin(IWin32Window owner, Icon icon)
    {
        using (var f = NewForm("Dashboard PIN", icon))
        {
            float s = Scale(f);
            Func<int, int> S = v => (int)(v * s);
            int P = S(20), W = S(320), y = P;
            AddLabel(f, "Anyone opening the dashboard will need this PIN. Leave both boxes empty to remove the PIN.", P, y, W, true).Height = S(40);
            y += S(48);
            AddLabel(f, "New PIN", P, y + S(3), S(110));
            var a = AddText(f, "", P + S(120), y, S(200)); a.UseSystemPasswordChar = true; y += S(36);
            AddLabel(f, "Repeat PIN", P, y + S(3), S(110));
            var b = AddText(f, "", P + S(120), y, S(200)); b.UseSystemPasswordChar = true; y += S(44);
            int bh = S(32), bw = S(100);
            var ok = AddButton(f, "Save", P + W - bw, y, bw, bh, true);
            var cancel = AddButton(f, "Cancel", P + W - bw * 2 - S(8), y, bw, bh);
            ok.DialogResult = DialogResult.OK; cancel.DialogResult = DialogResult.Cancel;
            f.AcceptButton = ok; f.CancelButton = cancel;
            f.ClientSize = new Size(W + P * 2, y + bh + P);
            while (f.ShowDialog(owner) == DialogResult.OK)
            {
                if (a.Text != b.Text) { MessageBox.Show(f, "The two PINs are different.", "PIN", MessageBoxButtons.OK, MessageBoxIcon.Warning); continue; }
                Settings.PinHash = a.Text.Length == 0 ? "" : DataTools.HashPin(a.Text);
                Settings.Save();
                MessageBox.Show(f, a.Text.Length == 0 ? "PIN removed." : "PIN set.", "PIN", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
        }
    }

    public static bool AskPin(IWin32Window owner, Icon icon)
    {
        if (Settings.PinHash.Length == 0) return true;
        using (var f = NewForm("Screen Time Tracker", icon))
        {
            float s = Scale(f);
            Func<int, int> S = v => (int)(v * s);
            int P = S(20), W = S(280), y = P;
            f.TopMost = true;
            AddLabel(f, "Enter your PIN to open the dashboard", P, y, W); y += S(32);
            var a = AddText(f, "", P, y, W); a.UseSystemPasswordChar = true; y += S(40);
            int bh = S(32), bw = S(100);
            var ok = AddButton(f, "Open", P + W - bw, y, bw, bh, true);
            var cancel = AddButton(f, "Cancel", P + W - bw * 2 - S(8), y, bw, bh);
            ok.DialogResult = DialogResult.OK; cancel.DialogResult = DialogResult.Cancel;
            f.AcceptButton = ok; f.CancelButton = cancel;
            f.ClientSize = new Size(W + P * 2, y + bh + P);
            for (int tries = 0; tries < 5; tries++)
            {
                a.Text = "";
                if (f.ShowDialog(owner) != DialogResult.OK) return false;
                if (DataTools.HashPin(a.Text) == Settings.PinHash) return true;
                MessageBox.Show(f, "Wrong PIN.", "Screen Time Tracker", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            return false;
        }
    }
}

// "You were away 10:32–11:15 – what were you doing?" Appears above the tray after a long absence.
class AwayPromptForm : Form
{
    public AwayPromptForm(Tracker tracker, Icon icon, double start, double end)
    {
        Text = "Screen Time – you were away";
        Icon = icon;
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Theme.Current.Surface; ForeColor = Theme.Current.Text;
        float s = Dialogs.Scale(this);
        Func<int, int> S = v => (int)(v * s);
        int P = S(14), W = S(360), y = P;
        var head = Dialogs.AddLabel(this, "You were away " + Util.Time(start) + " – " + Util.Time(end) + " (" + Util.Fmt(end - start) + ")", P, y, W);
        head.Font = new Font("Segoe UI Semibold", 10.5f); head.Height = S(24);
        y += S(26);
        Dialogs.AddLabel(this, "What were you doing? It goes into your log and timesheet.", P, y, W, true); y += S(28);
        Dialogs.AddLabel(this, "Client (optional)", P, y + S(3), S(120));
        var client = Dialogs.AddCombo(this, new[] { "" }.Concat(Projects.All.Select(p => p.Name)), "", P + S(125), y, W - S(125), true);
        y += S(36);
        string[] work = { "Meeting", "Phone call", "Client visit", "Paper work" }, rest = { "Lunch", "Tea break", "Personal" };
        int bw = (W - S(8) * 3) / 4, bh = S(30);
        for (int i = 0; i < work.Length; i++)
        {
            string label = work[i];
            var b = Dialogs.AddButton(this, label, P + i * (bw + S(8)), y, bw, bh, i == 0);
            b.Click += delegate { Add(tracker, start, end, label, client.Text, "work"); };
        }
        y += bh + S(8);
        for (int i = 0; i < rest.Length; i++)
        {
            string label = rest[i];
            var b = Dialogs.AddButton(this, label, P + i * (bw + S(8)), y, bw, bh);
            b.Click += delegate { Add(tracker, start, end, label, client.Text, "break"); };
        }
        var other = Dialogs.AddButton(this, "Other…", P + 3 * (bw + S(8)), y, bw, bh);
        other.Click += delegate
        {
            Hide();
            Dialogs.EditManual(null, Icon, tracker, Clock.Today, null, start, end, "");
            Close();
        };
        y += bh + S(8);
        var skip = Dialogs.AddButton(this, "Don't record", P, y, W, S(28));
        skip.ForeColor = Theme.Current.Muted;
        skip.Click += delegate { Close(); };
        ClientSize = new Size(W + P * 2, y + S(28) + P);
        var wa = Screen.PrimaryScreen.WorkingArea;
        Location = new Point(wa.Right - Width - S(12), wa.Bottom - Height - S(12));
        var t = new Timer { Interval = 5 * 60 * 1000 };
        t.Tick += delegate { t.Stop(); Close(); };
        t.Start();
        FormClosed += delegate { t.Dispose(); };
    }

    void Add(Tracker tracker, double start, double end, string label, string client, string kind)
    {
        var m = new ManualEntry();
        m.Start = start; m.End = end; m.Label = label; m.Client = client.Trim(); m.Kind = kind;
        tracker.AddManual(m, Clock.Today);
        Close();
    }

    protected override bool ShowWithoutActivation { get { return true; } }
}

// Search everything you've done: pages, files, sites and apps, across all days. Also "what was I doing at …?".
class SearchForm : Form
{
    public event Action<DateTime> OpenDay;
    readonly Tracker tracker;
    readonly TextBox query;
    readonly ComboBox range;
    readonly ListView list;
    readonly Label status;
    readonly DateTimePicker atDate, atTime;

    public SearchForm(Tracker tr, Icon icon)
    {
        tracker = tr;
        Text = "Search your history";
        Icon = icon;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Theme.Current.Surface; ForeColor = Theme.Current.Text;
        float s = Dialogs.Scale(this);
        Func<int, int> S = v => (int)(v * s);
        int P = S(14), W = S(860);
        query = Dialogs.AddText(this, "", P, P, S(470));
        range = Dialogs.AddCombo(this, new[] { "Last 7 days", "Last 30 days", "Last 90 days", "Everything" }, "Last 30 days", P + S(480), P, S(150), false);
        var go = Dialogs.AddButton(this, "Search", P + S(640), P - S(2), S(100), S(30), true);
        go.Click += delegate { Run(); };
        AcceptButton = go;
        int y = P + S(40);
        Dialogs.AddLabel(this, "What was I doing at", P, y + S(3), S(140));
        atDate = new DateTimePicker { Format = DateTimePickerFormat.Short, Location = new Point(P + S(145), y), Width = S(130), MaxDate = Clock.Today, Value = Clock.Today };
        atTime = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "hh:mm tt", ShowUpDown = true, Location = new Point(P + S(285), y), Width = S(110), Value = DateTime.Today.AddHours(Clock.Now.Hour) };
        Controls.Add(atDate); Controls.Add(atTime);
        var at = Dialogs.AddButton(this, "Show", P + S(405), y - S(2), S(80), S(30));
        at.Click += delegate { WhatAt(); };
        y += S(40);
        list = new ListView { View = View.Details, FullRowSelect = true, Location = new Point(P, y), Size = new Size(W, S(420)) };
        list.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        list.BackColor = Theme.Current.Dark ? Theme.Current.Bg : Color.White; list.ForeColor = Theme.Current.Text;
        list.Columns.Add("Date", S(110)); list.Columns.Add("From", S(80)); list.Columns.Add("To", S(80));
        list.Columns.Add("App", S(130)); list.Columns.Add("Window / page / file", S(300)); list.Columns.Add("Site", S(110)); list.Columns.Add("Time", S(60));
        list.DoubleClick += delegate
        {
            if (list.SelectedItems.Count == 0 || OpenDay == null) return;
            OpenDay((DateTime)list.SelectedItems[0].Tag);
        };
        Controls.Add(list);
        status = Dialogs.AddLabel(this, "Type a word – a sheet name, website, client or app – and press Enter. Double-click a result to open that day's log.", P, list.Bottom + S(8), W, true);
        status.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        ClientSize = new Size(W + P * 2, status.Bottom + P);
        MinimumSize = new Size(S(640), S(400));
        HandleCreated += delegate { Native.DarkTitleBar(Handle, Theme.Current.Dark); };
    }

    void Run()
    {
        string q = query.Text.Trim();
        if (q.Length == 0) return;
        int days = range.SelectedIndex == 0 ? 7 : range.SelectedIndex == 1 ? 30 : range.SelectedIndex == 2 ? 90 : 100000;
        var get = Program.DayGetter(tracker);
        var dates = Store.AllDays().Where(d => (Clock.Today - d).TotalDays < days).ToList();
        if (!dates.Contains(Clock.Today)) dates.Add(Clock.Today);
        list.BeginUpdate();
        list.Items.Clear();
        double total = 0; int hits = 0;
        foreach (var date in dates.OrderByDescending(d => d))
        {
            var d = get(date);
            // consecutive segments of the same window form one result
            Segment cur = null; double end = 0;
            Action flush = () =>
            {
                if (cur == null) return;
                if (!Matches(cur, q)) return;
                var it = new ListViewItem(new[]
                {
                    date.ToString("ddd d MMM yyyy", CultureInfo.CurrentCulture), Util.Time(cur.Start), Util.Time(end), Store.Info(cur.Key).Name,
                    cur.Item != null ? cur.Item.Title : "", cur.Item != null ? cur.Item.Site ?? "" : "", Util.Fmt(end - cur.Start)
                });
                it.Tag = date;
                list.Items.Add(it);
                total += end - cur.Start; hits++;
            };
            foreach (var sg in d.Segments)
            {
                if (cur != null && sg.Key == cur.Key && sg.Item == cur.Item && sg.Start - end <= 60) { end = sg.End; continue; }
                flush();
                cur = sg; end = sg.End;
            }
            flush();
            foreach (var m in d.Manual)
                if ((m.Label + " " + m.Client).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var it = new ListViewItem(new[] { date.ToString("ddd d MMM yyyy", CultureInfo.CurrentCulture), Util.Time(m.Start), Util.Time(m.End), m.IsWork ? "Offline work" : "Break", m.Label, m.Client, Util.Fmt(m.End - m.Start) });
                    it.Tag = date;
                    list.Items.Add(it);
                    total += m.End - m.Start; hits++;
                }
            if (hits > 3000) break;
        }
        list.EndUpdate();
        status.Text = hits == 0 ? "Nothing found for \"" + q + "\"." : hits + " results · " + Util.Fmt(total) + " in total for \"" + q + "\". Double-click a result to open that day.";
    }

    static bool Matches(Segment s, string q)
    {
        if (Store.Info(s.Key).Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || s.Key.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (s.Item == null) return false;
        return s.Item.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || (s.Item.Site != null && s.Item.Site.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    void WhatAt()
    {
        var date = atDate.Value.Date;
        double t = atTime.Value.Hour * 3600 + atTime.Value.Minute * 60;
        var d = Program.DayGetter(tracker)(date);
        Segment best = null; double bd = double.MaxValue;
        foreach (var s in d.Segments)
        {
            double dist = t < s.Start ? s.Start - t : t > s.End ? t - s.End : 0;
            if (dist < bd) { bd = dist; best = s; }
        }
        var man = d.Manual.FirstOrDefault(m => t >= m.Start && t <= m.End);
        string answer;
        if (man != null) answer = Util.Time(t) + ": " + (man.IsWork ? "offline work – " : "break – ") + man.Label + (man.Client.Length > 0 ? " (" + man.Client + ")" : "");
        else if (best == null || bd > 600) answer = Util.Time(t) + ": no activity recorded (away, locked or PC off).";
        else
            answer = (bd < 1 ? "At " + Util.Time(t) : "Nearest activity to " + Util.Time(t)) + ": " + Store.Info(best.Key).Name +
                     (best.Item != null ? " – " + best.Item.Title + (best.Item.Site != null ? " (" + best.Item.Site + ")" : "") : "") +
                     ", " + Util.Time(best.Start) + " – " + Util.Time(best.End);
        status.Text = answer;
        MessageBox.Show(this, answer, date.ToString("dddd, d MMM yyyy", CultureInfo.CurrentCulture), MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
