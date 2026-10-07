using System;
using System.Runtime.InteropServices;
using System.Text;

// Win32 calls: foreground window, its title and process, idle time, window state, dark title bar.
static class Native
{
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr hWnd);
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hWnd, int attr, ref int value, int size);

    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    /// Milliseconds since the last keyboard/mouse input anywhere in the session.
    public static uint IdleMs()
    {
        var li = new LASTINPUTINFO();
        li.cbSize = (uint)Marshal.SizeOf(li);
        if (!GetLastInputInfo(ref li)) return 0;
        return unchecked((uint)Environment.TickCount - li.dwTime);
    }

    /// Full exe path of a process; works for elevated processes too thanks to the limited-information right.
    public static string ProcessPath(uint pid)
    {
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }

    public static string ClassName(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string WindowText(IntPtr hWnd)
    {
        int n = GetWindowTextLength(hWnd);
        if (n <= 0) return "";
        var sb = new StringBuilder(Math.Min(n, 2048) + 1);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// UWP apps sit inside ApplicationFrameHost; the real app owns one of its child windows.
    public static uint ChildProcessOtherThan(IntPtr hWnd, uint hostPid)
    {
        uint found = 0;
        EnumChildWindows(hWnd, delegate(IntPtr child, IntPtr l)
        {
            uint p;
            GetWindowThreadProcessId(child, out p);
            if (p != 0 && p != hostPid) { found = p; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    public static void DarkTitleBar(IntPtr hWnd, bool dark)
    {
        try
        {
            int v = dark ? 1 : 0;
            if (DwmSetWindowAttribute(hWnd, 20, ref v, 4) != 0) DwmSetWindowAttribute(hWnd, 19, ref v, 4);
        }
        catch { }
    }
}

// Reads the peak level of every active output device (speakers, wired earphones, Bluetooth headsets, HDMI...),
// so watching a video, listening to music or being on a call is not counted as idle time - whichever device it plays on.
static class AudioMeter
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
    }

    public static bool IsPlaying()
    {
        object en = null, col = null;
        try
        {
            en = new MMDeviceEnumeratorCom();
            IMMDeviceCollection c;
            if (((IMMDeviceEnumerator)en).EnumAudioEndpoints(0 /* render */, 1 /* active */, out c) != 0 || c == null) return false;
            col = c;
            uint n;
            if (c.GetCount(out n) != 0) return false;
            for (uint i = 0; i < n; i++)
            {
                object dev = null, meter = null;
                try
                {
                    IMMDevice d;
                    if (c.Item(i, out d) != 0 || d == null) continue;
                    dev = d;
                    Guid iid = typeof(IAudioMeterInformation).GUID;
                    if (d.Activate(ref iid, 23, IntPtr.Zero, out meter) != 0 || meter == null) continue;
                    float peak;
                    if (((IAudioMeterInformation)meter).GetPeakValue(out peak) == 0 && peak > 0.0005f) return true;
                }
                catch { }
                finally
                {
                    if (meter != null) Marshal.ReleaseComObject(meter);
                    if (dev != null) Marshal.ReleaseComObject(dev);
                }
            }
            return false;
        }
        catch { return false; }
        finally
        {
            if (col != null) Marshal.ReleaseComObject(col);
            if (en != null) Marshal.ReleaseComObject(en);
        }
    }
}
