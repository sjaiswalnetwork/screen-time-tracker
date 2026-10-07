using System;
using System.Diagnostics;
using System.IO;
using System.Text;

// --selftest <file>: checks internet time, the Windows event log and browser address-bar reading, and writes a report.
static class SelfTest
{
    public static void Run(string outFile)
    {
        var sb = new StringBuilder();
        var sw = Stopwatch.StartNew();
        bool ok = Clock.Sync();
        sb.AppendLine("NTP sync: " + ok + " in " + sw.ElapsedMilliseconds + " ms · " + Clock.Status() + " · offset " + Clock.Offset.TotalMilliseconds.ToString("0") + " ms");
        sb.AppendLine("Sound playing on any output device right now: " + AudioMeter.IsPlaying());
        WinEvents.Enabled = true;
        sw.Restart();
        var evs = WinEvents.For(Clock.Today);
        sb.AppendLine("Windows events today: " + evs.Count + " (" + sw.ElapsedMilliseconds + " ms)");
        foreach (var e in evs) sb.AppendLine("  " + Util.Time(e.At) + "  " + e.Kind + "  " + e.Text);
        var y = WinEvents.For(Clock.Today.AddDays(-1));
        sb.AppendLine("Windows events yesterday: " + y.Count);
        foreach (var e in y) sb.AppendLine("  " + Util.Time(e.At) + "  " + e.Kind + "  " + e.Text);
        foreach (var p in Process.GetProcesses())
        {
            string exe = p.ProcessName.ToLowerInvariant() + ".exe";
            if (!Browser.Is(exe) || p.MainWindowHandle == IntPtr.Zero) continue;
            string title = Native.WindowText(p.MainWindowHandle);
            sw.Restart();
            string url = null, err = null;
            try { url = Browser.ReadUrlNow(p.MainWindowHandle); } catch (Exception ex) { err = ex.GetType().Name; }
            string clean = Browser.Clean(title, exe, exe);
            bool priv = false;
            try { priv = Browser.CheckPrivateNow(p.MainWindowHandle); } catch { }
            sb.AppendLine(exe + ": private window = " + priv);
            sb.AppendLine(exe + ": title='" + title + "' -> item='" + clean + "' url-site=" + (Browser.SiteFromUrl(url) ?? "(none)") +
                          " title-site=" + (Browser.SiteFromTitle(clean) ?? "(none)") + " (" + sw.ElapsedMilliseconds + " ms" + (err != null ? ", " + err : "") + ")");
        }
        string[] tests = { "Budget - Google Sheets - Google Chrome", "(3) WhatsApp - Brave", "Inbox (1,204) - me@x.com - Gmail - Personal - Microsoft\u200b Edge",
                           "New Incognito Tab - Google Chrome (Incognito)", "Book1 - Excel", "Program.cs - screen-time - Visual Studio Code", "*notes.txt - Notepad" };
        string[] apps = { "chrome.exe", "brave.exe", "msedge.exe", "chrome.exe", "excel.exe", "code.exe", "notepad.exe" };
        string[] names = { "Google Chrome", "Brave Browser", "Microsoft Edge", "Google Chrome", "Microsoft Excel", "Visual Studio Code", "Notepad" };
        for (int i = 0; i < tests.Length; i++) sb.AppendLine("clean: '" + tests[i] + "' -> '" + Browser.Clean(tests[i], apps[i], names[i]) + "'");
        string[] urls = { "https://docs.google.com/spreadsheets/d/abc/edit", "www.youtube.com/watch?v=x", "https://www.amazon.in/dp/1", "brave://newtab", "hello world search" };
        foreach (var u in urls) sb.AppendLine("site: '" + u + "' -> " + (Browser.SiteFromUrl(u) ?? "(none)"));
        File.WriteAllText(outFile, sb.ToString());
    }
}
