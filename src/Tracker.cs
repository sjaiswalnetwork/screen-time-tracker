using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

// Samples the foreground window once a second and credits the elapsed time to its app, to the window/page inside it,
// and to a timeline segment. Time is not counted while the PC is locked, asleep, paused, or idle (no input for
// Settings.IdleMinutes, unless sound is playing or the microphone is in use - videos, music, calls). When idle kicks in,
// the idle stretch already credited is taken back. All times use the internet-synced Clock.
// Also runs focus mode, reminders (breaks, eye care, water, late night), limits, goals and unusual-use alerts.
class Tracker
{
    public DayData Today;
    public bool Paused, Locked, Idle;
    public DateTime PausedUntil = DateTime.MaxValue;
    public string CurrentKey, CurrentTitle, CurrentSite, CallApp;
    public double ContinuousSeconds;
    public DateTime LastActiveAt = DateTime.MinValue;
    public DateTime FocusUntil = DateTime.MinValue, FocusStarted;
    public int FocusMinutes;
    public event Action<string, string> Notify;
    public event Action<double, double> AwayEnded; // start, end (seconds since midnight) of a long absence

    class Credit { public DateTime At; public DayData Day; public string Key; public ItemUsage Item; public int Hour; public double Dt; }

    readonly Stopwatch sw = Stopwatch.StartNew();
    readonly List<Credit> ledger = new List<Credit>();
    readonly Dictionary<string, string> nameCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, DateTime> blockedAt = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
    readonly uint selfPid = (uint)Process.GetCurrentProcess().Id;
    double lastT, nextBreakAt, eyeAt, waterAt, awayStartSec = -1;
    string lastKey, lastItemKey;
    bool dirty;
    int ticks;
    DateTime lastSave = DateTime.Now, lastAudio = DateTime.MinValue, lockedAt, lastResolve = DateTime.MinValue, callStarted;
    Dictionary<string, double> usualApps, usualSites;

    static readonly Dictionary<string, string> KnownNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "calculatorapp.exe", "Calculator" }, { "whatsapp.root.exe", "WhatsApp" }, { "whatsapp.exe", "WhatsApp" },
        { "ms-teams.exe", "Microsoft Teams" }, { "olk.exe", "Outlook (new)" }, { "windowsterminal.exe", "Windows Terminal" },
        { "systemsettings.exe", "Settings" }, { "photos.exe", "Photos" }, { "microsoft.photos.exe", "Photos" },
        { "notepad.exe", "Notepad" }, { "mspaint.exe", "Paint" }, { "snippingtool.exe", "Snipping Tool" },
        { "tally.exe", "TallyPrime" }, { "tallyprime.exe", "TallyPrime" }, { "taskmgr.exe", "Task Manager" },
    };

    public Tracker()
    {
        Today = Store.LoadDay(Clock.Today);
        ResetContinuous();
        PreWarn();
    }

    public bool Focusing { get { return Clock.Now < FocusUntil; } }

    public string State
    {
        get
        {
            if (Paused) return PausedUntil < DateTime.MaxValue ? "Paused until " + PausedUntil.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture) : "Paused";
            if (Locked) return "Locked";
            if (CallApp != null) return "On a call (" + CallApp + ")";
            if (Idle) return "Away";
            return CurrentKey == null ? "Waiting" : "Using " + Store.Info(CurrentKey).Name;
        }
    }

    public void Log(string kind, string text) { Log(kind, text, Clock.Now); }

    void Log(string kind, string text, DateTime at)
    {
        if (at.Date != Today.Date) return;
        Today.AddEvent((at - at.Date).TotalSeconds, kind, text);
        dirty = true;
    }

    public void Tick()
    {
        double t = sw.Elapsed.TotalSeconds, dt = t - lastT;
        lastT = t;
        if (dt <= 0) return;
        if (dt > 3) dt = 1; // the PC slept or the timer stalled - never credit the gap
        ticks++;
        DateTime now = Clock.Now;

        if (Today.Date != now.Date)
        {
            Store.SaveDay(Today);
            Today = Store.LoadDay(now.Date);
            ledger.Clear();
            warned.Clear();
            usualApps = null;
            lastItemKey = null;
            ResetContinuous();
        }

        if (Paused && now >= PausedUntil) SetPaused(false);
        if (FocusMinutes > 0 && !Focusing) EndFocus(true);
        if (Paused || Locked) { CurrentKey = null; Idle = false; MaybeSave(now); return; }

        // calls: the microphone being in use means you're on a call / in a meeting
        if (ticks % 3 == 0)
        {
            string mic = Mic.InUseBy();
            if (mic != CallApp)
            {
                if (mic != null && CallApp == null) { callStarted = now; Log("call", "On a call / meeting (" + mic + ")"); }
                else if (mic == null && CallApp != null) Log("callend", "Call ended (" + CallApp + ", " + Util.Fmt((now - callStarted).TotalSeconds) + ")");
                CallApp = mic;
            }
        }
        if (CallApp != null) { lastAudio = now; Today.MeetingSeconds += dt; dirty = true; }

        uint idleMs = Native.IdleMs();
        if (Settings.AudioKeepsActive && idleMs >= 30000 && CallApp == null && AudioMeter.IsPlaying()) lastAudio = now;
        bool idleNow = idleMs >= (uint)Settings.IdleMinutes * 60000u && (now - lastAudio).TotalSeconds > 15;
        if (idleNow)
        {
            if (!Idle)
            {
                Idle = true;
                double back = Math.Min(idleMs / 1000.0, (now - lastAudio).TotalSeconds);
                Rewind(now, back);
                var from = now.AddSeconds(-back);
                Log("away", "Went idle – no keyboard or mouse input", from);
                awayStartSec = from.Date == Today.Date ? (from - from.Date).TotalSeconds : 0;
                ResetContinuous();
            }
            CurrentKey = null;
            MaybeSave(now);
            return;
        }
        if (Idle)
        {
            Idle = false;
            Log("back", "Back at the PC");
            RaiseAway((now - now.Date).TotalSeconds);
        }

        IntPtr hwnd; string key, name, path, title;
        if (!Foreground(out hwnd, out key, out name, out path, out title)) { CurrentKey = null; MaybeSave(now); return; }
        if (key == "lockapp.exe") { CurrentKey = null; MaybeSave(now); return; } // lock screen showing
        if (Settings.IgnoreApps.Any(a => string.Equals(a, key, StringComparison.OrdinalIgnoreCase) || string.Equals(a, name, StringComparison.OrdinalIgnoreCase)))
        { CurrentKey = null; lastKey = null; MaybeSave(now); return; }

        Store.Remember(key, name, path);
        var u = Today.Get(key);
        u.Seconds += dt;
        u.Hours[now.Hour] += dt;
        if (key != lastKey) u.Opens++;
        lastKey = key;
        CurrentKey = key;

        // which window / page / file inside the app
        ItemUsage item = null;
        string itemKey = null;
        CurrentTitle = CurrentSite = null;
        if (Settings.RecordTitles && key != "desktop")
        {
            string clean = Browser.Clean(title, key, name);
            if (clean.Length > 0)
            {
                item = Today.GetItem(key, clean);
                itemKey = DayData.ItemKey(key, clean);
                CurrentTitle = clean;
                if (Browser.Is(key) && clean != Browser.PrivateLabel)
                {
                    string site;
                    if (Browser.Resolved.TryGetValue(itemKey, out site)) item.Site = site;
                    else
                    {
                        if (item.Site == null) item.Site = Browser.SiteFromTitle(clean);
                        if (itemKey != lastItemKey || (now - lastResolve).TotalSeconds > 10)
                        {
                            Browser.Request(hwnd, itemKey, title);
                            lastResolve = now;
                        }
                    }
                }
                item.Seconds += dt;
                if (itemKey != lastItemKey) item.Opens++;
                CurrentSite = item.Site;
            }
        }
        lastItemKey = itemKey;

        // timeline segment
        double nowSec = (now - now.Date).TotalSeconds;
        var segs = Today.Segments;
        Segment last = segs.Count > 0 ? segs[segs.Count - 1] : null;
        if (last != null && last.Key == key && last.Item == item && nowSec - last.End <= 3) last.End = nowSec;
        else
        {
            var s = new Segment();
            s.Start = Math.Max(0, nowSec - dt); s.End = nowSec; s.Key = key; s.Item = item;
            segs.Add(s);
        }
        dirty = true;
        LastActiveAt = now;

        var c = new Credit(); c.At = now; c.Day = Today; c.Key = key; c.Item = item; c.Hour = now.Hour; c.Dt = dt;
        ledger.Add(c);
        DateTime keepFrom = now.AddSeconds(-(Settings.IdleMinutes * 60 + 60));
        int drop = 0;
        while (drop < ledger.Count && ledger[drop].At < keepFrom) drop++;
        if (drop > 0) ledger.RemoveRange(0, drop);

        ContinuousSeconds += dt;
        if (Focusing) EnforceFocus(hwnd, key, name, item);
        Reminders(now, idleMs, u, name);
        MaybeSave(now);
    }

    // ---------- reminders, limits, goals, alerts ----------

    void Reminders(DateTime now, uint idleMs, AppUsage u, string name)
    {
        if (Settings.BreakMinutes > 0 && ContinuousSeconds >= nextBreakAt)
        {
            nextBreakAt = ContinuousSeconds + Settings.BreakMinutes * 60;
            Raise("Time for a short break", "You've been on screen for " + Util.Fmt(ContinuousSeconds) + " straight. Stand up, stretch and walk for a few minutes.");
        }
        // 20-20-20: every 20 minutes of screen time, at a natural pause in typing
        if (Settings.EyeCare && ContinuousSeconds >= eyeAt && idleMs >= 2000)
        {
            eyeAt = ContinuousSeconds + 20 * 60;
            Raise("Rest your eyes", "20-20-20: look at something 20 feet (6 m) away for 20 seconds.");
        }
        if (Settings.WaterMinutes > 0 && ContinuousSeconds >= waterAt)
        {
            waterAt = ContinuousSeconds + Settings.WaterMinutes * 60;
            Raise("Drink some water 💧", "A quick glass of water keeps you fresh.");
        }
        int lim;
        if (Settings.Limits.TryGetValue(u.Key, out lim) && lim > 0 && u.Seconds >= lim * 60 && warned.Add("limit:" + u.Key))
            Raise("Daily limit reached: " + name, "You've used " + name + " for " + Util.Fmt(u.Seconds) + " today (limit " + Util.Fmt(lim * 60) + ").");

        if (ticks % 30 != 0) return; // the checks below are heavier; every 30 s is plenty
        double total = Today.Total;
        if (Settings.DailyGoalMinutes > 0 && total >= Settings.DailyGoalMinutes * 60 && warned.Add("goal"))
            Raise("Daily screen time goal reached", "You've been on screen for " + Util.Fmt(total) + " today (goal " + Util.Fmt(Settings.DailyGoalMinutes * 60) + ").");
        if (Settings.LateNightMin >= 0 && (now - now.Date).TotalMinutes >= Settings.LateNightMin && total >= 3600 && warned.Add("late"))
            Raise("It's getting late 🌙", "It's " + now.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture) + " and you've been on screen for " + Util.Fmt(total) + " today. Time to wind down?");
        if (Settings.GoalEntertainmentMax > 0 || Settings.GoalSocialMax > 0)
        {
            var cats = Categories.Totals(Today.Apps.Values, k => Today.ItemsFor(k));
            if (Settings.GoalEntertainmentMax > 0 && cats["Entertainment"] >= Settings.GoalEntertainmentMax * 60 && warned.Add("cat:ent"))
                Raise("Entertainment goal reached", Util.Fmt(cats["Entertainment"]) + " of entertainment today (your limit " + Util.Fmt(Settings.GoalEntertainmentMax * 60) + ").");
            if (Settings.GoalSocialMax > 0 && cats["Social"] >= Settings.GoalSocialMax * 60 && warned.Add("cat:soc"))
                Raise("Social media goal reached", Util.Fmt(cats["Social"]) + " of social media today (your limit " + Util.Fmt(Settings.GoalSocialMax * 60) + ").");
        }
        // unusual use: 3x the usual for this app or site, and at least 30 minutes
        if (usualApps == null) BuildUsual();
        double avg;
        if (u.Seconds >= 1800 && usualApps.TryGetValue(u.Key, out avg) && u.Seconds >= 3 * Math.Max(avg, 300) && warned.Add("unusual:" + u.Key))
            Raise("Heavier than usual: " + name, Util.Fmt(u.Seconds) + " today – about " + Math.Round(u.Seconds / Math.Max(avg, 300), 1) + "x your usual " + Util.Fmt(avg) + ".");
        if (CurrentSite != null)
        {
            double sv = Insights.SiteTotals(Today).TryGetValue(CurrentSite, out sv) ? sv : 0;
            if (sv >= 1800 && usualSites.TryGetValue(CurrentSite, out avg) && sv >= 3 * Math.Max(avg, 300) && warned.Add("unusual:site:" + CurrentSite))
                Raise("Heavier than usual: " + CurrentSite, Util.Fmt(sv) + " today – about " + Math.Round(sv / Math.Max(avg, 300), 1) + "x your usual " + Util.Fmt(avg) + ".");
        }
    }

    void BuildUsual()
    {
        usualApps = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        usualSites = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        int n = 0;
        for (int i = 1; i <= 14; i++)
        {
            var d = Store.LoadDay(Today.Date.AddDays(-i));
            if (d.Total < 600) continue;
            n++;
            foreach (var a in d.Apps.Values) { double v; usualApps.TryGetValue(a.Key, out v); usualApps[a.Key] = v + a.Seconds; }
            foreach (var kv in Insights.SiteTotals(d)) { double v; usualSites.TryGetValue(kv.Key, out v); usualSites[kv.Key] = v + kv.Value; }
        }
        if (n < 3) { usualApps.Clear(); usualSites.Clear(); return; }
        foreach (var k in usualApps.Keys.ToList()) usualApps[k] /= n;
        foreach (var k in usualSites.Keys.ToList()) usualSites[k] /= n;
    }

    // Don't re-announce goals/limits that were already passed before the tracker started.
    void PreWarn()
    {
        if (Settings.DailyGoalMinutes > 0 && Today.Total >= Settings.DailyGoalMinutes * 60) warned.Add("goal");
        foreach (var kv in Settings.Limits)
        {
            AppUsage u;
            if (Today.Apps.TryGetValue(kv.Key, out u) && u.Seconds >= kv.Value * 60) warned.Add("limit:" + kv.Key);
        }
        if (Settings.LateNightMin >= 0 && (Clock.Now - Clock.Today).TotalMinutes >= Settings.LateNightMin) warned.Add("late");
    }

    // ---------- focus mode ----------

    public void StartFocus(int minutes)
    {
        FocusMinutes = minutes;
        FocusStarted = Clock.Now;
        FocusUntil = FocusStarted.AddMinutes(minutes);
        Log("focus", "Focus session started (" + minutes + " min)");
        Raise("Focus mode on for " + minutes + " min", "Distracting apps and sites (" + string.Join(", ", Settings.FocusBlockCategories) + ") will be minimised until " +
              FocusUntil.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture) + ".");
    }

    public void EndFocus(bool completed)
    {
        if (FocusMinutes == 0) return;
        double len = (Clock.Now - FocusStarted).TotalSeconds;
        Log("focusend", (completed ? "Focus session completed" : "Focus session stopped") + " (" + Util.Fmt(len) + ")");
        if (completed) Raise("Focus session done 🎯", "Great work – " + FocusMinutes + " minutes of focus. Take a 5-minute break.");
        FocusMinutes = 0;
        FocusUntil = DateTime.MinValue;
    }

    public static bool IsBlocked(string key, string appName, ItemUsage item)
    {
        string cat = Insights.CategoryOf(key, item);
        if (Settings.FocusBlockCategories.Contains(cat)) return true;
        foreach (var b in Settings.FocusBlockExtra)
        {
            if (string.Equals(b, key, StringComparison.OrdinalIgnoreCase) || string.Equals(b, appName, StringComparison.OrdinalIgnoreCase)) return true;
            if (item != null && item.Site != null && item.Site.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }

    void EnforceFocus(IntPtr hwnd, string key, string name, ItemUsage item)
    {
        if (key == "screen-time-tracker" || key == "desktop" || key == "windows-shell") return;
        if (!IsBlocked(key, name, item)) return;
        Native.ShowWindow(hwnd, 6); // SW_MINIMIZE
        string what = item != null && item.Site != null ? item.Site : name;
        DateTime at;
        if (!blockedAt.TryGetValue(what, out at) || (Clock.Now - at).TotalSeconds > 60)
        {
            blockedAt[what] = Clock.Now;
            Log("blocked", "Focus mode minimised " + what);
            Raise("Stay focused 🎯", what + " is blocked until " + FocusUntil.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture) + ".");
        }
    }

    // ---------- bookkeeping ----------

    void Rewind(DateTime now, double seconds)
    {
        DateTime from = now.AddSeconds(-seconds);
        for (int i = ledger.Count - 1; i >= 0 && ledger[i].At > from; i--)
        {
            var c = ledger[i];
            AppUsage u;
            if (c.Day.Apps.TryGetValue(c.Key, out u))
            {
                u.Seconds = Math.Max(0, u.Seconds - c.Dt);
                u.Hours[c.Hour] = Math.Max(0, u.Hours[c.Hour] - c.Dt);
            }
            if (c.Item != null) c.Item.Seconds = Math.Max(0, c.Item.Seconds - c.Dt);
            ledger.RemoveAt(i);
        }
        if (from.Date == Today.Date)
        {
            double cut = (from - from.Date).TotalSeconds;
            var segs = Today.Segments;
            while (segs.Count > 0 && segs[segs.Count - 1].End > cut)
            {
                var s = segs[segs.Count - 1];
                if (s.Start >= cut) segs.RemoveAt(segs.Count - 1);
                else { s.End = cut; break; }
            }
        }
        dirty = true;
    }

    void RaiseAway(double endSec)
    {
        if (awayStartSec < 0) return;
        double start = awayStartSec;
        awayStartSec = -1;
        if (Settings.AskReasonMinutes <= 0 || endSec - start < Settings.AskReasonMinutes * 60) return;
        var h = AwayEnded;
        if (h != null) h(start, endSec);
    }

    public void AddManual(ManualEntry m, DateTime date)
    {
        var d = date.Date == Today.Date ? Today : Store.LoadDay(date);
        d.Manual.Add(m);
        if (d != Today) Store.SaveDay(d); else { dirty = true; Save(); }
    }

    public void RemoveManual(ManualEntry m, DateTime date)
    {
        var d = date.Date == Today.Date ? Today : Store.LoadDay(date);
        d.Manual.RemoveAll(x => Math.Abs(x.Start - m.Start) < 1 && Math.Abs(x.End - m.End) < 1 && x.Label == m.Label);
        if (d != Today) Store.SaveDay(d); else { dirty = true; Save(); }
    }

    void ResetContinuous()
    {
        ContinuousSeconds = 0;
        nextBreakAt = Settings.BreakMinutes * 60;
        eyeAt = 20 * 60;
        waterAt = Math.Max(1, Settings.WaterMinutes) * 60;
    }

    public void OnLimitChanged(string key) { warned.Remove("limit:" + key); }

    public void OnSettingsChanged()
    {
        nextBreakAt = ContinuousSeconds + Math.Max(1, Settings.BreakMinutes) * 60;
        waterAt = ContinuousSeconds + Math.Max(1, Settings.WaterMinutes) * 60;
        warned.Remove("goal"); warned.Remove("cat:ent"); warned.Remove("cat:soc");
        PreWarn();
    }

    public void SetLocked(bool locked)
    {
        if (locked == Locked) return;
        Locked = locked;
        if (locked)
        {
            lockedAt = DateTime.Now;
            Log("lock", "PC locked");
            if (awayStartSec < 0) awayStartSec = (Clock.Now - Clock.Today).TotalSeconds;
            Save();
        }
        else
        {
            Today.Unlocks++;
            Log("unlock", "PC unlocked");
            if ((DateTime.Now - lockedAt).TotalMinutes >= 2) ResetContinuous();
            Idle = false;
            RaiseAway((Clock.Now - Clock.Today).TotalSeconds);
        }
    }

    public void SetPaused(bool paused, int minutes = 0)
    {
        Paused = paused;
        PausedUntil = paused && minutes > 0 ? Clock.Now.AddMinutes(minutes) : DateTime.MaxValue;
        Log(paused ? "pause" : "resume", paused ? "Tracking paused" + (minutes > 0 ? " for " + minutes + " min" : "") : "Tracking resumed");
        if (paused) Save();
    }

    void Raise(string title, string text)
    {
        var h = Notify;
        if (h != null) h(title, text);
    }

    void MaybeSave(DateTime now)
    {
        if (dirty && (DateTime.Now - lastSave).TotalSeconds >= 30) Save();
    }

    public void Save()
    {
        try { Store.SaveDay(Today); dirty = false; lastSave = DateTime.Now; } catch { }
    }

    public void ResetToday()
    {
        Today = new DayData();
        Today.Date = Clock.Today;
        ledger.Clear();
        warned.Clear();
        lastKey = lastItemKey = null;
    }

    bool Foreground(out IntPtr h, out string key, out string name, out string path, out string title)
    {
        key = name = path = title = null;
        h = Native.GetForegroundWindow();
        if (h == IntPtr.Zero) return false;
        uint pid;
        Native.GetWindowThreadProcessId(h, out pid);
        if (pid == 0) return false;
        title = Native.WindowText(h);
        if (pid == selfPid) { key = "screen-time-tracker"; name = "Screen Time Tracker"; title = ""; return true; }
        path = Native.ProcessPath(pid);
        string exe = path != null ? Path.GetFileName(path).ToLowerInvariant() : null;

        if (exe == "applicationframehost.exe")
        {
            uint real = Native.ChildProcessOtherThan(h, pid);
            if (real == 0) return false;
            path = Native.ProcessPath(real);
            exe = path != null ? Path.GetFileName(path).ToLowerInvariant() : null;
            pid = real;
        }
        if (exe == null)
        {
            try { exe = Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant() + ".exe"; }
            catch { return false; }
        }

        if (exe == "explorer.exe")
        {
            string cls = Native.ClassName(h);
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd")
            { key = "desktop"; name = "Desktop & Taskbar"; return true; }
            key = exe; name = "File Explorer"; return true;
        }
        if (exe == "searchhost.exe" || exe == "startmenuexperiencehost.exe" || exe == "shellexperiencehost.exe" || exe == "searchapp.exe")
        { key = "windows-shell"; name = "Start & Search"; path = null; title = ""; return true; }

        key = exe;
        name = FriendlyName(path, exe);
        return true;
    }

    string FriendlyName(string path, string exe)
    {
        string n;
        if (KnownNames.TryGetValue(exe, out n)) return n;
        if (string.IsNullOrEmpty(path)) return Util.TitleFromExe(exe);
        if (nameCache.TryGetValue(path, out n)) return n;
        n = null;
        try
        {
            var vi = FileVersionInfo.GetVersionInfo(path);
            n = CleanName(vi.FileDescription);
            if (n == null || n.Length > 40) n = CleanName(vi.ProductName) ?? n;
        }
        catch { }
        if (n == null || n.Length > 40) n = Util.TitleFromExe(exe);
        nameCache[path] = n;
        return n;
    }

    static string CleanName(string s)
    {
        if (s == null) return null;
        s = s.Trim();
        return s.Length == 0 ? null : s;
    }
}
