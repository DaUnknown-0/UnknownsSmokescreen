// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace UnknownsSmokescreen;

internal static class OverlayHost
{
    private static readonly object gate = new();
    private static Thread thread;
    private static volatile bool running, show, feedActive, protectedWindow;
    internal static volatile bool ProtectionFailed;
    internal static volatile WindowProtection.Failure AffinityFailure;
    internal static volatile bool DiagnosticCapture;
    private static bool wantPrivate, wantRecording;
    private static IntPtr priv, rec, game;
    private static WindowProtection.SystemInfo windowSystem;
    private static Native.WndProc proc;
    private static readonly Native.EnumProc enumerate = FindCandidate;
    private static readonly StringBuilder className = new(256);
    private static readonly uint pid = (uint)Environment.ProcessId;
    private const uint WM_FEED = 0x8003, WM_VISIBILITY = 0x8004;
    private static string diagnosticDirectory;
    private static long diagnosticDeadline;
    internal static IntPtr GameWindow => game;
    internal static bool GameForeground => game != IntPtr.Zero && Native.GetForegroundWindow() == game && !Native.IsIconic(game);
    // Aufnahme nur fuer ein verifiziert geschuetztes Fenster. Diagnose misst auch ohne Vordergrund, zeigt dann aber nichts.
    internal static bool PrivateWanted => wantPrivate && protectedWindow && show && (GameForeground || DiagnosticCapture);
    internal static bool Verified => protectedWindow && !ProtectionFailed;
    internal static long LastCapture => Interlocked.Read(ref lastCaptureAt);
    internal static bool Shown => shown;
    internal static bool HiddenByUser => !show;
    internal static bool FeedWanted => wantRecording && feedActive;
    internal static string DiagnosticDirectory => diagnosticDirectory != null && Environment.TickCount64 <= diagnosticDeadline ? diagnosticDirectory : null;
    internal static void SetVisible(bool value, bool record = false)
    {
        if (value) show = true;
        else HidePrivate();
        feedActive = record;
    }
    internal static void HidePrivate()
    {
        if (!show) return;
        show = false;
        Discard();
    }

    internal static void Start(bool privateOverlay, bool recordingFeed)
    {
        if (thread is { IsAlive: false }) thread = null;
        if (thread != null) return;
        ProtectionFailed = false;
        AffinityFailure = null;
        wantPrivate = privateOverlay; wantRecording = recordingFeed;
        if (!privateOverlay && !recordingFeed) return;
        LayoutKey = null;
        Interlocked.Exchange(ref clientSize, 0);
        running = true;
        clientArea = default;
        // Eine alte Testdatei wird absichtlich nie ausgewertet.
        foreach (var arg in NativeArguments.Values)
            if (arg.StartsWith("--smokescreen-dump=", StringComparison.Ordinal))
            {
                diagnosticDirectory = Path.GetFullPath(arg.Substring(19));
                diagnosticDeadline = Environment.TickCount64 + 180000;
            }
        thread = new Thread(Run) { IsBackground = true, Name = "SmokescreenHud" }; thread.Start();
    }
    // Fenster nach einer Aenderung von PrivateOverlay oder RecordingFeed neu anlegen.
    internal static void Restart(bool privateOverlay, bool recordingFeed)
    {
        Stop();
        if (thread != null) return;
        Interlocked.Increment(ref epoch);
        shown = false; presentedX = int.MinValue;
        OverlayContent.ResetOutput();
        Start(privateOverlay, recordingFeed);
        SmokescreenPlugin.Logger.LogInfo($"HUD-Fenster neu gestartet: privat={privateOverlay}, Feed={recordingFeed}");
    }
    internal static void Stop()
    {
        show = feedActive = false; running = false;
        var hwnd = priv != IntPtr.Zero ? priv : rec;
        if (hwnd != IntPtr.Zero) Native.PostMessageW(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero);
        if (thread == null || thread.Join(2000)) thread = null;
    }
    internal static void EndDiagnostic() { diagnosticDirectory = null; }

    internal static volatile string LayoutKey;
    private static int layout;
    internal static int Layout => Volatile.Read(ref layout);
    private static int epoch, readyEpoch = -1;
    private static long lastCaptureAt;
    internal static int Epoch => Volatile.Read(ref epoch);
    internal static IntPtr PrivateWindow => protectedWindow ? priv : IntPtr.Zero;
    internal static void FrameReady(int frameEpoch)
    {
        Volatile.Write(ref readyEpoch, frameEpoch);
        Interlocked.Exchange(ref lastCaptureAt, Environment.TickCount64);
    }

    private static byte[] feedFill, feedReady;
    private static int feedWidth, feedHeight;
    private static bool feedPending;

    internal static byte[] PublishFeed(byte[] image, int w, int h)
    {
        if (!running || !FeedWanted) return image;
        byte[] spare;
        lock (gate)
        {
            spare = feedReady ?? feedFill;
            if (feedReady == null) feedFill = null;
            feedReady = image;
            feedWidth = w;
            feedHeight = h;
            feedPending = true;
        }
        Post(WM_FEED);
        return spare;
    }
    internal static void Discard()
    {
        Interlocked.Increment(ref epoch);
        Interlocked.Exchange(ref lastCaptureAt, 0);
        Post(WM_VISIBILITY);
    }
    private static void Post(uint message)
    {
        var host = priv != IntPtr.Zero ? priv : rec;
        if (host != IntPtr.Zero) Native.PostMessageW(host, message, IntPtr.Zero, IntPtr.Zero);
    }

    // --- Fensterthread ------------------------------------------------------------------------------

    private static DesktopDisplay.ClientArea clientArea;
    private static bool layoutChanged;
    private static long clientSize;
    internal static long ClientSize => Interlocked.Read(ref clientSize);
    private static FeedSurface recording;
    private static volatile bool shown;
    private static int presentedWidth, presentedHeight, presentedX = int.MinValue, presentedY;
    private static void ProcessFeed()
    {
        byte[] data; int w, h;
        lock (gate) { if (!feedPending) return; feedPending = false; data = feedReady; feedReady = null; w = feedWidth; h = feedHeight; }
        if (!running || rec == IntPtr.Zero || data == null) return;
        recording.Upload(data, w, h);
        lock (gate) feedFill ??= data;
        Native.InvalidateRect(rec, IntPtr.Zero, false);
    }

    private static void RefreshGeometry()
    {
        var area = DesktopDisplay.Client(game);
        bool newLayout = layoutChanged || !area.SameLayout(clientArea);
        layoutChanged = false;
        clientArea = area;
        if (!newLayout) return;
        LayoutKey = DesktopDisplay.ApprovalKey(area);
        Interlocked.Increment(ref layout);
        Hide();
        Discard();
        Interlocked.Exchange(ref clientSize, ((long)area.Width << 32) | (uint)area.Height);
        if (area.Window != IntPtr.Zero)
            SmokescreenPlugin.Logger.LogInfo($"HUD-Fensterfläche: {area.Width}x{area.Height} bei {area.X},{area.Y}, Fenster-DPI={area.Dpi}, Monitor=0x{area.Monitor:X}");
    }
    private static void Hide()
    {
        if (priv != IntPtr.Zero && shown) Native.ShowWindow(priv, 0);
        shown = false;
    }

    private static bool FindCandidate(IntPtr hwnd, IntPtr unused)
    {
        Native.GetWindowThreadProcessId(hwnd,out uint owner);
        if(owner!=pid) return true;
        className.Clear(); Native.GetClassNameW(hwnd,className,className.Capacity);
        if(className.ToString()!="UnityWndClass") return true;
        game=hwnd; return false;
    }
    internal static bool SupportedProtection() => WindowProtection.CheckSystem(WindowProtection.Inspect(false)) == WindowProtection.Problem.None;
    internal static bool IsCaptureExcluded(IntPtr hwnd) => hwnd != IntPtr.Zero
        && Native.GetWindowDisplayAffinity(hwnd, out uint affinity) && affinity == Native.WDA_EXCLUDEFROMCAPTURE;
    private static void FailProtection(WindowProtection.Problem cause, int code, uint affinity, WindowProtection.SystemInfo system, Exception exception = null)
    {
        protectedWindow = false;
        Hide();
        var failure = new WindowProtection.Failure(cause, code, affinity, system, DateTimeOffset.UtcNow,
            exception == null ? null : exception.GetType().Name + ": " + exception.Message);
        AffinityFailure = failure;
        ProtectionFailed = true;
        SmokescreenPlugin.Logger.LogWarning(failure.Log);
    }

    private static bool VerifyRuntime()
    {
        if (!protectedWindow) return false;
        bool read = Native.GetWindowDisplayAffinity(priv, out uint affinity);
        int error = read ? 0 : Marshal.GetLastWin32Error();
        var issue = WindowProtection.CheckRead(read, affinity, true);
        if (issue == WindowProtection.Problem.None) return true;
        protectedWindow = false;
        Hide();
        FailProtection(issue, error, affinity, WindowProtection.RefreshSession(windowSystem));
        return false;
    }

    private static void ProtectWindow(string cls, IntPtr instance, bool dpiActive)
    {
        var system = windowSystem = WindowProtection.Inspect(true);
        var issue = WindowProtection.CheckSystem(system);
        if (issue != WindowProtection.Problem.None)
        {
            FailProtection(issue, issue == WindowProtection.Problem.VersionQuery ? system.VersionStatus : system.DwmStatus, 0, system);
            return;
        }
        if (!dpiActive)
        {
            FailProtection(WindowProtection.Problem.DpiContext, 0, 0, system);
            return;
        }
        priv = Native.CreateWindowExW(CapturePopup.Styles, cls, "Smokescreen Private HUD", Native.WS_POPUP,
            0, 0, 16, 16, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        if (priv == IntPtr.Zero)
        {
            FailProtection(WindowProtection.Problem.CreateWindow, Marshal.GetLastWin32Error(), 0, system);
            return;
        }
        if (!Native.SetWindowDisplayAffinity(priv, Native.WDA_EXCLUDEFROMCAPTURE))
        {
            FailProtection(WindowProtection.Problem.SetAffinity, Marshal.GetLastWin32Error(), 0, system);
        }
        else
        {
            bool read = Native.GetWindowDisplayAffinity(priv, out uint affinity);
            int error = read ? 0 : Marshal.GetLastWin32Error();
            issue = WindowProtection.CheckRead(read, affinity, false);
            if (issue == WindowProtection.Problem.None)
            {
                if (!SetLayeredWindowAttributes(priv, 0, 255, 2)) throw new InvalidOperationException("Fensterdeckkraft fehlt");
                protectedWindow = true;
                SmokescreenPlugin.Logger.LogInfo($"Privates HUD: WDA_EXCLUDEFROMCAPTURE bestätigt (0x{affinity:X}), Per-Monitor V2={PerMonitorDpiScope.IsPerMonitorV2(priv)}, {system.Log}");
                return;
            }
            FailProtection(issue, error, affinity, system);
        }
        Native.DestroyWindow(priv);
        priv = IntPtr.Zero;
    }
    private static void Run()
    {
        using var dpi = PerMonitorDpiScope.Enter();
        IntPtr instance=Native.GetModuleHandleW(null);
        string cls="UnknownsSmokescreenHud"+pid;
        try
        {
            proc=WindowProc;
            var wc=new Native.WNDCLASSEXW { cbSize=(uint)Marshal.SizeOf<Native.WNDCLASSEXW>(), lpfnWndProc=Marshal.GetFunctionPointerForDelegate(proc), hInstance=instance, lpszClassName=cls };
            if (Native.RegisterClassExW(ref wc) == 0)
            {
                int error = Marshal.GetLastWin32Error();
                if (wantPrivate) FailProtection(WindowProtection.Problem.WindowClass, error, 0, WindowProtection.Inspect(true));
                else SmokescreenPlugin.Logger.LogWarning("Fensterklasse nicht registriert: Win32=" + error);
                return;
            }
            Native.EnumWindows(enumerate,IntPtr.Zero);
            if (wantPrivate) ProtectWindow(cls, instance, dpi.Active);
            if(wantRecording)
            {
                // Absichtlich verborgen. Benutzer öffnet die Aufnahmequelle separat, nie automatisch über dem Spiel.
                rec=Native.CreateWindowExW(0,cls,"Smokescreen Recording Feed",Native.WS_OVERLAPPEDWINDOW,Native.CW_USEDEFAULT,Native.CW_USEDEFAULT,640,400,IntPtr.Zero,IntPtr.Zero,instance,IntPtr.Zero);
                recording=new FeedSurface();
                Native.ShowWindow(rec,7);
            }
            IntPtr host=priv!=IntPtr.Zero?priv:rec;
            if(host==IntPtr.Zero) return;
            Native.SetTimer(host,(UIntPtr)1,16,IntPtr.Zero);
            while(running && Native.GetMessageW(out var message,IntPtr.Zero,0,0)>0) { Native.TranslateMessage(ref message); Native.DispatchMessageW(ref message); }
        }
        catch (Exception e)
        {
            if (wantPrivate) FailProtection(WindowProtection.Problem.Thread, e.HResult, 0, windowSystem, e);
            else SmokescreenPlugin.Logger.LogWarning("HUD-Fenster: " + e.Message);
        }
        finally
        {
            protectedWindow=false;
            if(priv!=IntPtr.Zero) { Native.ShowWindow(priv,0); Native.DestroyWindow(priv); }
            if(rec!=IntPtr.Zero) Native.DestroyWindow(rec);

            recording?.Dispose(); priv=rec=IntPtr.Zero; recording=null;

            lock(gate) { feedFill=feedReady=null; feedPending=false; }
            Native.UnregisterClassW(cls,instance);
        }
    }
    private static IntPtr WindowProc(IntPtr hwnd,uint msg,IntPtr w,IntPtr l)
    {
        try
        {


            if(msg==WM_FEED) { ProcessFeed(); return IntPtr.Zero; }
            if(msg==WM_VISIBILITY) { if (!show || readyEpoch != Epoch) Hide(); return IntPtr.Zero; }
            if (hwnd == priv && (msg == Native.WM_DPICHANGED || msg == Native.WM_DISPLAYCHANGE))
            {
                layoutChanged = true;
                return IntPtr.Zero;
            }
            if(msg==Native.WM_TIMER) { Tick(); return IntPtr.Zero; }
            if(msg==Native.WM_NCHITTEST && hwnd==priv) return (IntPtr)(-1);
            if(msg==Native.WM_PAINT && hwnd==rec)
            {
                var ps=new Native.PAINTSTRUCT(); var dc=Native.BeginPaint(hwnd,ref ps);
                try { if (running) recording?.Blit(dc, hwnd); } finally { Native.EndPaint(hwnd,ref ps); }
                return IntPtr.Zero;
            }
            if(msg==Native.WM_CLOSE) { if(!running) Native.PostQuitMessage(0); else Native.ShowWindow(hwnd,0); return IntPtr.Zero; }
        }
        catch (Exception e)
        {
            if (wantPrivate && !ProtectionFailed)
                FailProtection(WindowProtection.Problem.Thread, e.HResult, 0, windowSystem, e);
            else SmokescreenPlugin.Logger.LogWarning("HUD-Ausgabe: " + e.Message);
        }
        return Native.DefWindowProcW(hwnd,msg,w,l);
    }
    private static void Tick()
    {
        bool safe = VerifyRuntime();
        if(game==IntPtr.Zero || !Native.IsWindow(game)) Native.EnumWindows(enumerate,IntPtr.Zero);
        if (priv != IntPtr.Zero) RefreshGeometry();
        ProcessFeed();
        if (safe && CaptureSelfTest.ProbePosition(readyEpoch, out int x, out int y, out int size))
        {
            if (!shown) shown = SetWindowPos(priv, new IntPtr(-1), x, y, size, size, 0x0050);
            presentedX = int.MinValue;
            return;
        }
        if (!safe || !CaptureSelfTest.Approved || !show || !GameForeground || !clientArea.HasArea ||
            readyEpoch != Epoch || GpuPresenter.Error != 0) { Hide(); return; }
        if (!shown || presentedX != clientArea.X || presentedY != clientArea.Y ||
            presentedWidth != clientArea.Width || presentedHeight != clientArea.Height)
        {
            shown = SetWindowPos(priv, new IntPtr(-1), clientArea.X, clientArea.Y,
                clientArea.Width, clientArea.Height, 0x0010 | 0x0040);
            presentedX = clientArea.X; presentedY = clientArea.Y;
            presentedWidth = clientArea.Width; presentedHeight = clientArea.Height;
        }
    }

    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] internal static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);

    // Persistente DIB für Warnungen und Aufnahmeprüfungen.
    internal sealed unsafe class LayerSurface : IDisposable
    {
        private IntPtr dc,bmp,previous,bits;
        internal int Width, Height;
        internal uint* Bits => (uint*)bits;
        internal IntPtr Dc => dc;
        internal bool Ensure(int w,int h)
        {
            if(dc==IntPtr.Zero) dc=Native.CreateCompatibleDC(IntPtr.Zero);
            if(w==Width && h==Height && bmp!=IntPtr.Zero) return false;
            if(bmp!=IntPtr.Zero) { Native.SelectObject(dc,previous); Native.DeleteObject(bmp); }
            var bi=new Native.BITMAPINFO { biSize=40,biWidth=w,biHeight=-h,biPlanes=1,biBitCount=32 };
            bmp=Native.CreateDIBSection(dc,ref bi,0,out bits,IntPtr.Zero,0);
            if(bmp==IntPtr.Zero) { Width=Height=0; throw new InvalidOperationException("DIB konnte nicht angelegt werden"); }
            previous=Native.SelectObject(dc,bmp); Width=w; Height=h;
            return true;
        }
        internal bool Present(IntPtr hwnd,int x,int y,bool full,int x0,int y0,int x1,int y1)
        {
            var dst=new Native.POINT { x=x,y=y }; var src=new Native.POINT(); var size=new Native.SIZE { cx=Width,cy=Height };
            var blend=new Native.BLENDFUNCTION { SourceConstantAlpha=255,AlphaFormat=1 };
            var dirty=new Native.RECT { left=x0,top=y0,right=x1,bottom=y1 };
            var info=new Native.ULWINFO { cbSize=(uint)sizeof(Native.ULWINFO), pptDst=&dst, psize=&size, hdcSrc=dc, pptSrc=&src, pblend=&blend, dwFlags=Native.ULW_ALPHA, prcDirty=full?null:&dirty };
            Native.GdiFlush();
            return Native.UpdateLayeredWindowIndirect(hwnd,&info);
        }
        public void Dispose() { if(bmp!=IntPtr.Zero) { Native.SelectObject(dc,previous); Native.DeleteObject(bmp); } if(dc!=IntPtr.Zero) Native.DeleteDC(dc); dc=bmp=bits=IntPtr.Zero; Width=Height=0; }
    }

    // Aufnehmbares Feed-Fenster, unveraendert gegenueber 0.3.0.3.
    private sealed class FeedSurface : IDisposable
    {
        private IntPtr dc,bmp,previous,bits;
        private int width,height;
        internal void Upload(byte[] pixels,int w,int h)
        {
            if(dc==IntPtr.Zero) dc=Native.CreateCompatibleDC(IntPtr.Zero);
            if(w!=width || h!=height || bmp==IntPtr.Zero)
            {
                if(bmp!=IntPtr.Zero) { Native.SelectObject(dc,previous); Native.DeleteObject(bmp); }
                var bi=new Native.BITMAPINFO { biSize=40,biWidth=w,biHeight=-h,biPlanes=1,biBitCount=32 };
                bmp=Native.CreateDIBSection(dc,ref bi,0,out bits,IntPtr.Zero,0);
                if(bmp==IntPtr.Zero) throw new InvalidOperationException("DIB konnte nicht angelegt werden");
                previous=Native.SelectObject(dc,bmp); width=w; height=h;
            }
            Marshal.Copy(pixels,0,bits,pixels.Length);
        }
        internal void Blit(IntPtr target, IntPtr hwnd)
        {
            if(bmp==IntPtr.Zero || !Native.GetClientRect(hwnd,out var rc)) return;
            Native.PatBlt(target,0,0,rc.right,rc.bottom,0x00000042);
            double scale=Math.Min((double)rc.right/width,(double)rc.bottom/height);
            int w=(int)(width*scale), h=(int)(height*scale);
            Native.StretchBlt(target,(rc.right-w)/2,(rc.bottom-h)/2,w,h,dc,0,0,width,height,0x00CC0020);
        }
        public void Dispose() { if(bmp!=IntPtr.Zero) { Native.SelectObject(dc,previous); Native.DeleteObject(bmp); } if(dc!=IntPtr.Zero) Native.DeleteDC(dc); dc=bmp=IntPtr.Zero; }
    }
}

internal static class Native
{
    public const uint WS_POPUP = 0x80000000, WS_OVERLAPPEDWINDOW = 0x00CF0000;
    public const uint WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    public const int CW_USEDEFAULT = unchecked((int)0x80000000);
    public const int SW_HIDE = 0, SW_SHOWNOACTIVATE = 4, SW_MINIMIZE = 6;
    public const uint WM_TIMER = 0x0113, WM_NCHITTEST = 0x0084, WM_PAINT = 0x000F, WM_CLOSE = 0x0010;
    public const uint WM_DISPLAYCHANGE = 0x007E, WM_DPICHANGED = 0x02E0;
    public const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
    public const uint ULW_ALPHA = 2;
    public delegate bool EnumProc(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr data);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
    [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern void PostQuitMessage(int exit);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern bool StretchBlt(IntPtr target,int x,int y,int w,int h,IntPtr source,int sx,int sy,int sw,int sh,uint op);
    [DllImport("gdi32.dll")] public static extern bool PatBlt(IntPtr target,int x,int y,int w,int h,uint op);
    [DllImport("dwmapi.dll")] public static extern int DwmIsCompositionEnabled(out bool enabled);
    [DllImport("ntdll.dll")] public static extern int RtlGetVersion(ref OSVERSION version);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct OSVERSION { public int size, major, minor, build, platform; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string service; }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEXW
    {
        public uint cbSize, style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string lpszMenuName, lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam, lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public int fErase;
        public RECT rcPaint;
        public int fRestore, fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFO
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
        public uint bmiColors;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    // UPDATELAYEREDWINDOWINFO: nur das geaenderte Rechteck wird uebertragen.
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct ULWINFO
    {
        public uint cbSize;
        public IntPtr hdcDst;
        public POINT* pptDst;
        public SIZE* psize;
        public IntPtr hdcSrc;
        public POINT* pptSrc;
        public uint crKey;
        public BLENDFUNCTION* pblend;
        public uint dwFlags;
        public RECT* prcDirty;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandleW(string name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool UnregisterClassW(string cls, IntPtr instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowExW(uint exStyle, string cls, string title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] public static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetMessageW(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] public static extern IntPtr DispatchMessageW(ref MSG msg);
    [DllImport("user32.dll")] public static extern UIntPtr SetTimer(IntPtr hwnd, UIntPtr id, uint ms, IntPtr proc);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hwnd, out RECT rc);
    [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hwnd, ref POINT p);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll")] public static extern unsafe bool UpdateLayeredWindowIndirect(IntPtr hwnd, ULWINFO* info);
    [DllImport("user32.dll")] public static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase);
    [DllImport("user32.dll")] public static extern IntPtr BeginPaint(IntPtr hwnd, ref PAINTSTRUCT ps);
    [DllImport("user32.dll")] public static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT ps);

    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFO bi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool GdiFlush();
}
