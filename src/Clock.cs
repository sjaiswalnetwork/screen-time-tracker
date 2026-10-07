using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;

// Internet-synced clock. Asks public NTP time servers for the real time and keeps the difference to the PC clock,
// so every recorded time (log in, log out, activity) is correct even if the PC clock drifts or is set wrong.
static class Clock
{
    static long offsetTicks;
    public static bool Synced;
    public static DateTime LastSync;
    public static string Server, Error;
    static readonly string[] Servers = { "time.windows.com", "time.google.com", "pool.ntp.org", "time.cloudflare.com" };

    public static TimeSpan Offset { get { return TimeSpan.FromTicks(Interlocked.Read(ref offsetTicks)); } }
    public static DateTime Now { get { return DateTime.Now.AddTicks(Interlocked.Read(ref offsetTicks)); } }
    public static DateTime UtcNow { get { return DateTime.UtcNow.AddTicks(Interlocked.Read(ref offsetTicks)); } }
    public static DateTime Today { get { return Now.Date; } }

    public static void SyncAsync()
    {
        ThreadPool.QueueUserWorkItem(delegate { Sync(); });
    }

    public static bool Sync()
    {
        foreach (var host in Servers)
        {
            try
            {
                var off = Query(host);
                if (Math.Abs(off.TotalDays) > 3650) continue;
                Interlocked.Exchange(ref offsetTicks, off.Ticks);
                Server = host;
                LastSync = DateTime.Now;
                Synced = true;
                Error = null;
                return true;
            }
            catch (Exception ex) { Error = ex.Message; }
        }
        return false;
    }

    static TimeSpan Query(string host)
    {
        var data = new byte[48];
        data[0] = 0x1B; // client request, NTP v3
        var addr = Dns.GetHostAddresses(host).First(a => a.AddressFamily == AddressFamily.InterNetwork);
        DateTime t1, t4;
        using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
        {
            s.ReceiveTimeout = 3000; s.SendTimeout = 3000;
            s.Connect(new IPEndPoint(addr, 123));
            t1 = DateTime.UtcNow;
            s.Send(data);
            s.Receive(data);
            t4 = DateTime.UtcNow;
        }
        DateTime t2 = NtpTime(data, 32), t3 = NtpTime(data, 40);
        return TimeSpan.FromTicks(((t2 - t1).Ticks + (t3 - t4).Ticks) / 2);
    }

    static DateTime NtpTime(byte[] b, int i)
    {
        ulong sec = ((ulong)b[i] << 24) | ((ulong)b[i + 1] << 16) | ((ulong)b[i + 2] << 8) | b[i + 3];
        ulong frac = ((ulong)b[i + 4] << 24) | ((ulong)b[i + 5] << 16) | ((ulong)b[i + 6] << 8) | b[i + 7];
        double ms = sec * 1000.0 + frac * 1000.0 / 4294967296.0;
        return new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms);
    }

    public static string Status()
    {
        if (!Synced) return Error != null ? "Couldn't reach internet time – using the PC clock" : "Checking internet time…";
        double s = Offset.TotalSeconds, a = Math.Abs(s);
        string drift = a < 0.5 ? "your PC clock is exact" :
            "your PC clock is " + (a < 60 ? a.ToString("0.0") + " s" : Util.Fmt(a)) + (s > 0 ? " slow" : " fast") + " (corrected)";
        return "Synced with " + Server + " · " + drift;
    }

    // ---- world clocks ----

    public static readonly KeyValuePair<string, string>[] Cities =
    {
        new KeyValuePair<string, string>("UTC", "UTC"),
        new KeyValuePair<string, string>("GMT Standard Time", "London"),
        new KeyValuePair<string, string>("W. Europe Standard Time", "Berlin / Paris"),
        new KeyValuePair<string, string>("Russian Standard Time", "Moscow"),
        new KeyValuePair<string, string>("Arabian Standard Time", "Dubai"),
        new KeyValuePair<string, string>("Pakistan Standard Time", "Karachi"),
        new KeyValuePair<string, string>("India Standard Time", "India"),
        new KeyValuePair<string, string>("Nepal Standard Time", "Kathmandu"),
        new KeyValuePair<string, string>("Bangladesh Standard Time", "Dhaka"),
        new KeyValuePair<string, string>("Singapore Standard Time", "Singapore"),
        new KeyValuePair<string, string>("China Standard Time", "Beijing"),
        new KeyValuePair<string, string>("Tokyo Standard Time", "Tokyo"),
        new KeyValuePair<string, string>("AUS Eastern Standard Time", "Sydney"),
        new KeyValuePair<string, string>("Eastern Standard Time", "New York"),
        new KeyValuePair<string, string>("Central Standard Time", "Chicago"),
        new KeyValuePair<string, string>("Pacific Standard Time", "Los Angeles"),
    };

    public static string CityName(string tzId)
    {
        foreach (var c in Cities) if (c.Key == tzId) return c.Value;
        return tzId;
    }

    /// Current time in a time zone, or null if the zone is unknown on this PC.
    public static DateTime? In(string tzId)
    {
        try { return TimeZoneInfo.ConvertTimeFromUtc(UtcNow, TimeZoneInfo.FindSystemTimeZoneById(tzId)); }
        catch { return null; }
    }
}
