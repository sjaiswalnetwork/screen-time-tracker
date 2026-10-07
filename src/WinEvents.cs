using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Linq;
using System.Security.Principal;

// Reads start-up, shut-down, sleep, wake, sign-in and sign-out times from the Windows System event log,
// so log-in/log-out times are known even for days before the tracker was running.
static class WinEvents
{
    public static bool Enabled = true;
    static readonly Dictionary<DateTime, List<LogEvent>> cache = new Dictionary<DateTime, List<LogEvent>>();
    static readonly Dictionary<DateTime, DateTime> cachedAt = new Dictionary<DateTime, DateTime>();
    static string mySid;

    public static List<LogEvent> For(DateTime day)
    {
        day = day.Date;
        if (!Enabled || day > Clock.Today) return new List<LogEvent>();
        List<LogEvent> list;
        DateTime at;
        if (cache.TryGetValue(day, out list) && (day < Clock.Today || (cachedAt.TryGetValue(day, out at) && (DateTime.Now - at).TotalSeconds < 60)))
            return list;
        list = Read(day);
        cache[day] = list;
        cachedAt[day] = DateTime.Now;
        return list;
    }

    static List<LogEvent> Read(DateTime day)
    {
        var list = new List<LogEvent>();
        try
        {
            if (mySid == null) { var u = WindowsIdentity.GetCurrent().User; mySid = u == null ? "" : u.Value; }
            // the log uses the PC clock; shift the query window and the results by the internet-time correction
            TimeSpan off = Clock.Offset;
            DateTime from = (day - off).ToUniversalTime(), to = (day.AddDays(1) - off).ToUniversalTime();
            string xp = "*[System[(EventID=12 or EventID=13 or EventID=42 or EventID=1 or EventID=506 or EventID=507 or EventID=107 or EventID=7001 or EventID=7002 or EventID=6008) and " +
                        "TimeCreated[@SystemTime>='" + from.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture) +
                        "' and @SystemTime<'" + to.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture) + "']]]";
            var q = new EventLogQuery("System", PathType.LogName, xp);
            using (var r = new EventLogReader(q))
            {
                for (EventRecord e = r.ReadEvent(); e != null; e = r.ReadEvent())
                {
                    using (e)
                    {
                        if (e.TimeCreated == null) continue;
                        string prov = e.ProviderName ?? "", kind = null, text = null;
                        switch (e.Id)
                        {
                            case 12: if (prov.EndsWith("Kernel-General")) { kind = "boot"; text = "PC started"; } break;
                            case 13: if (prov.EndsWith("Kernel-General")) { kind = "shutdown"; text = "PC shut down"; } break;
                            case 42: if (prov.EndsWith("Kernel-Power")) { kind = "sleep"; text = "PC went to sleep"; } break;
                            case 1: if (prov.EndsWith("Power-Troubleshooter")) { kind = "wake"; text = "PC woke up"; } break;
                            case 107: if (prov.EndsWith("Kernel-Power")) { kind = "wake"; text = "PC woke up"; } break;
                            case 506: if (prov.EndsWith("Kernel-Power")) { kind = "sleep"; text = "PC went to standby (screen off)"; } break;
                            case 507: if (prov.EndsWith("Kernel-Power")) { kind = "wake"; text = "PC woke from standby"; } break;
                            case 6008: kind = "crash"; text = "The previous shut-down was unexpected (power cut or crash)"; break;
                            case 7001: if (prov.EndsWith("Winlogon") && IsMine(e)) { kind = "logon"; text = "Signed in to Windows"; } break;
                            case 7002: if (prov.EndsWith("Winlogon") && IsMine(e)) { kind = "logoff"; text = "Signed out of Windows"; } break;
                        }
                        if (kind == null) continue;
                        DateTime t = e.TimeCreated.Value + off;
                        var ev = new LogEvent();
                        ev.At = (t - day).TotalSeconds;
                        ev.Kind = kind; ev.Text = text; ev.FromWindows = true;
                        if (ev.At >= 0 && ev.At < 86400) list.Add(ev);
                    }
                }
            }
        }
        catch { }
        return list.OrderBy(x => x.At).ToList();
    }

    static bool IsMine(EventRecord e)
    {
        try
        {
            bool sawSid = false;
            foreach (var p in e.Properties)
            {
                var sid = p.Value as SecurityIdentifier;
                if (sid == null) continue;
                sawSid = true;
                if (sid.Value == mySid) return true;
            }
            return !sawSid;
        }
        catch { return true; }
    }
}

class SessionInfo
{
    public double LogIn = -1, LogOut = -1, First = -1, Last = -1;
    public string InVia = "", OutVia = "";
    public bool StillOn;

    /// How long the PC session lasted (up to `nowSec` if still on).
    public double OnFor(double nowSec)
    {
        if (LogIn < 0) return 0;
        double end = StillOn ? nowSec : LogOut;
        return end > LogIn ? end - LogIn : 0;
    }
}

// Works out a day's log-in and log-out times from the tracker's own events, the Windows log, and activity.
static class Sessions
{
    public static List<LogEvent> Merged(DayData d)
    {
        var list = new List<LogEvent>(d.Events);
        foreach (var e in WinEvents.For(d.Date))
            if (!d.Events.Any(o => o.Kind == e.Kind && Math.Abs(o.At - e.At) < 180)) list.Add(e);
        return list.OrderBy(e => e.At).ToList();
    }

    public static string Label(string kind)
    {
        switch (kind)
        {
            case "logon": return "Signed in";
            case "boot": return "PC started";
            case "unlock": return "Unlocked";
            case "wake": return "Woke from sleep";
            case "start": return "Tracker started";
            case "logoff": return "Signed out";
            case "shutdown": return "Shut down";
            case "sleep": return "Went to sleep";
            case "lock": return "Locked";
            case "stop": return "Tracker closed";
            case "away": return "Went idle";
            default: return "Last activity";
        }
    }

    public static SessionInfo Compute(DayData d, List<LogEvent> evs, bool isToday, Tracker tr)
    {
        var s = new SessionInfo();
        if (d.Segments.Count > 0) { s.First = d.FirstActive; s.Last = d.LastActive; }

        var inEv = evs.FirstOrDefault(e => e.Kind == "logon" || e.Kind == "boot");
        if (inEv != null && (s.First < 0 || inEv.At <= s.First)) { s.LogIn = inEv.At; s.InVia = Label(inEv.Kind); }
        else if (s.First >= 0)
        {
            var alt = evs.LastOrDefault(e => (e.Kind == "unlock" || e.Kind == "wake" || e.Kind == "start" || e.Kind == "logon" || e.Kind == "boot")
                                             && e.At <= s.First && s.First - e.At < 3600);
            if (alt != null) { s.LogIn = alt.At; s.InVia = Label(alt.Kind); }
            else { s.LogIn = s.First; s.InVia = "First activity"; }
        }

        bool activeNow = isToday && tr != null && !tr.Locked && !tr.Paused && d.Segments.Count > 0 && (Clock.Now - tr.LastActiveAt).TotalMinutes < 10;
        if (activeNow) s.StillOn = true;
        else if (s.Last >= 0)
        {
            var outEv = evs.FirstOrDefault(e => (e.Kind == "logoff" || e.Kind == "shutdown" || e.Kind == "sleep" || e.Kind == "lock" || e.Kind == "stop" || e.Kind == "away")
                                                && e.At >= s.Last - 5);
            if (outEv != null && outEv.At - s.Last < 3 * 3600) { s.LogOut = outEv.At; s.OutVia = Label(outEv.Kind); }
            else { s.LogOut = s.Last; s.OutVia = "Last activity"; }
        }
        else if (!activeNow)
        {
            // no tracked activity (e.g. before the tracker was installed): use what Windows logged
            if (s.LogIn < 0)
            {
                var w = evs.FirstOrDefault(e => e.Kind == "wake" || e.Kind == "unlock" || e.Kind == "start");
                if (w != null) { s.LogIn = w.At; s.InVia = Label(w.Kind); }
            }
            var o = evs.LastOrDefault(e => e.Kind == "sleep" || e.Kind == "shutdown" || e.Kind == "logoff" || e.Kind == "lock");
            var lastWake = evs.LastOrDefault(e => e.Kind == "wake" || e.Kind == "unlock" || e.Kind == "start");
            if (isToday && s.LogIn >= 0 && lastWake != null && (o == null || lastWake.At > o.At)) s.StillOn = true;
            else if (s.LogIn >= 0 && o != null && o.At > s.LogIn) { s.LogOut = o.At; s.OutVia = Label(o.Kind); }
            else if (s.LogIn >= 0 && isToday) s.StillOn = true;
        }
        return s;
    }
}
