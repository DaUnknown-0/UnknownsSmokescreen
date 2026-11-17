// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using UnityEngine;

namespace UnknownsSmokescreen;

internal static class OverlayHost
{
    private static readonly object gate = new();
    private static Thread thread;
    private static volatile bool running, show, feedActive, protectedWindow;
    internal static volatile bool ProtectionFailed;
    private static bool wantPrivate, wantRecording;
    private static IntPtr priv, rec, game;
    private static Native.WndProc proc;
    private static readonly Native.EnumProc enumerate = FindCandidate;
    private static readonly StringBuilder className = new(256);
    private static readonly uint pid = (uint)Environment.ProcessId;
    private static byte[] pixels, truth;
    private static int width, height, version;
    private static long publishedAt;
    private static Surface surface, recording;
    private static int presented = -1, recorded = -1;
    private static bool shown;
    private static string diagnosticDirectory;
    private static int dumpCount;
    private static long diagnosticDeadline;
    internal static IntPtr GameWindow => game;
    internal static bool GameForeground => game != IntPtr.Zero && Native.GetForegroundWindow() == game && !Native.IsIconic(game);
    internal static bool CanRender => (wantRecording && feedActive) || (GameForeground && protectedWindow && wantPrivate && show);
    internal static void SetVisible(bool value, bool record = false) { show = value; feedActive = record; }
    internal static void Start(bool privateOverlay, bool recordingFeed)
    {
        if (thread != null || (!privateOverlay && !recordingFeed)) return;
        wantPrivate = privateOverlay; wantRecording = recordingFeed; running = true;
        // Eine alte Testdatei wird absichtlich nie ausgewertet.
        foreach (var arg in NativeArguments.Values)
            if (arg.StartsWith("--smokescreen-dump=", StringComparison.Ordinal))
            {
                diagnosticDirectory = Path.GetFullPath(arg.Substring(19));
                diagnosticDeadline = Environment.TickCount64 + 180000;
            }
        thread = new Thread(Run) { IsBackground = true, Name = "SmokescreenHud" }; thread.Start();
    }
    internal static void Stop()
    {
        show = feedActive = false; running = false;
        var hwnd = priv != IntPtr.Zero ? priv : rec;
        if (hwnd != IntPtr.Zero) Native.PostMessageW(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero);
        thread?.Join(2000); thread = null;
    }
    // Zwei rohe Bildpaare: Hauptthread kopiert, Fensterthread vergleicht.
    // Unity-Zeiger verlassen Publish nie und bleiben nicht ueber einen Frame erhalten.
    private sealed class FramePair
    {
        internal readonly byte[] Real, Masked;
        internal readonly int Width, Height;
        internal long CapturedAt;
        internal int Epoch;
        internal FramePair(int w, int h)
        {
            Width = w; Height = h;
            Real = new byte[checked(w * h * 4)]; Masked = new byte[Real.Length];
        }
    }
    private static FramePair available, pending, reserve;
    private static int bufferWidth, bufferHeight, epoch;
    internal static void Discard()
    {
        lock (gate)
        {
            if (bufferWidth == 0) return;
            epoch++; pending = available = reserve = null;
            bufferWidth = bufferHeight = 0;
            publishedAt = 0;
        }
    }
    internal static void Publish(IntPtr real, IntPtr masked, int w, int h)
    {
        FramePair frame;
        lock (gate)
        {
            if (bufferWidth != w || bufferHeight != h)
            {
                epoch++; available = new FramePair(w, h); pending = null; publishedAt = 0;
                reserve = new FramePair(w, h);
                bufferWidth = w; bufferHeight = h;
            }
            frame = available ?? reserve ?? pending;
            if (frame == null) return;
            if (ReferenceEquals(frame, available)) available = null;
            else if (ReferenceEquals(frame, reserve)) reserve = null;
            else pending = null;
            frame.Epoch = epoch;
        }
        Marshal.Copy(real, frame.Real, 0, frame.Real.Length);
        Marshal.Copy(masked, frame.Masked, 0, frame.Masked.Length);
        frame.CapturedAt = Environment.TickCount64;
        lock (gate) { if (pending != null) available = pending; pending = frame; }
    }
    private static unsafe void ProcessPending()
    {
        FramePair frame;
        lock (gate) { frame = pending; pending = null; }
        if (frame == null) return;
        int length = frame.Real.Length;
        if (pixels == null || pixels.Length != length)
        {
            pixels = new byte[length]; truth = wantRecording ? new byte[length] : null;
        }
        fixed (byte* r = frame.Real)
        fixed (byte* m = frame.Masked)
        fixed (byte* output = pixels)
        fixed (byte* feed = truth)
            PixelDifference.Copy(r, m, output, feed, frame.Width, frame.Height);
        lock (gate)
        {
            if (frame.Epoch != epoch) return;
            width = frame.Width; height = frame.Height; version++; publishedAt = frame.CapturedAt;
            available = frame;
        }
    }
    internal static void EndDiagnostic() { diagnosticDirectory = null; }
    internal static void DumpDiagnostic(Texture2D real, Texture2D masked)
    {
        if (diagnosticDirectory == null || Environment.TickCount64 > diagnosticDeadline || dumpCount >= 12) return;
        Directory.CreateDirectory(diagnosticDirectory);
        string prefix = Path.Combine(diagnosticDirectory, $"hud_{pid}_{++dumpCount:00}");
        var r=real.GetWritableImageData(0); var m=masked.GetWritableImageData(0);
        lock(gate)
        {
            SaveRaw(prefix+"_real.bmp", r, real.width,real.height);
            SaveRaw(prefix+"_masked.bmp", m, real.width,real.height);
        }
    }
    private static unsafe void SaveRaw(string path, IntPtr raw, int w, int h)
    {
        var data=new byte[w*h*4];
        fixed(byte* dst=data) PixelDifference.Copy((byte*)raw,(byte*)raw,dst,dst,w,h);
        BitmapFile.Write(path,data,w,h);
    }
    private static bool FindCandidate(IntPtr hwnd, IntPtr unused)
    {
        Native.GetWindowThreadProcessId(hwnd,out uint owner);
        if(owner!=pid) return true;
        className.Clear(); Native.GetClassNameW(hwnd,className,className.Capacity);
        if(className.ToString()!="UnityWndClass") return true;
        game=hwnd; return false;
    }
    private static bool SupportedProtection()
    {
        var v=new Native.OSVERSION { size=Marshal.SizeOf<Native.OSVERSION>() };
        return Native.RtlGetVersion(ref v)==0 && (v.major>10 || (v.major==10 && v.build>=19041))
            && Native.DwmIsCompositionEnabled(out bool enabled)==0 && enabled;
    }
    private static bool Verify() => priv!=IntPtr.Zero && Native.GetWindowDisplayAffinity(priv,out uint affinity) && affinity==Native.WDA_EXCLUDEFROMCAPTURE;
    private static void Run()
    {
        IntPtr instance=Native.GetModuleHandleW(null);
        string cls="UnknownsSmokescreenHud"+pid;
        try
        {
            proc=WindowProc;
            var wc=new Native.WNDCLASSEXW { cbSize=(uint)Marshal.SizeOf<Native.WNDCLASSEXW>(), lpfnWndProc=Marshal.GetFunctionPointerForDelegate(proc), hInstance=instance, lpszClassName=cls };
            if(Native.RegisterClassExW(ref wc)==0) throw new InvalidOperationException("Fensterklasse nicht registriert");
            Native.EnumWindows(enumerate,IntPtr.Zero);
            if(wantPrivate)
            {
                priv=Native.CreateWindowExW(Native.WS_EX_LAYERED|Native.WS_EX_TRANSPARENT|Native.WS_EX_TOPMOST|Native.WS_EX_TOOLWINDOW|Native.WS_EX_NOACTIVATE,
                    cls,"Smokescreen Private HUD",Native.WS_POPUP,0,0,16,16,IntPtr.Zero,IntPtr.Zero,instance,IntPtr.Zero);
                protectedWindow=priv!=IntPtr.Zero && SupportedProtection() && Native.SetWindowDisplayAffinity(priv,Native.WDA_EXCLUDEFROMCAPTURE) && Verify();
                if(!protectedWindow) { ProtectionFailed=true; if(priv!=IntPtr.Zero) Native.DestroyWindow(priv); priv=IntPtr.Zero; }
                else { surface=new Surface(); SmokescreenPlugin.Logger.LogInfo("Privates HUD: WDA_EXCLUDEFROMCAPTURE gesetzt und verifiziert"); }
            }
            if(wantRecording)
            {
                // Absichtlich verborgen. Benutzer öffnet die Aufnahmequelle separat, nie automatisch über dem Spiel.
                rec=Native.CreateWindowExW(0,cls,"Smokescreen Recording Feed",Native.WS_OVERLAPPEDWINDOW,Native.CW_USEDEFAULT,Native.CW_USEDEFAULT,640,400,IntPtr.Zero,IntPtr.Zero,instance,IntPtr.Zero);
                recording=new Surface();
                Native.ShowWindow(rec,7);
            }
            IntPtr host=priv!=IntPtr.Zero?priv:rec;
            if(host==IntPtr.Zero) return;
            Native.SetTimer(host,(UIntPtr)1,16,IntPtr.Zero);
            while(running && Native.GetMessageW(out var message,IntPtr.Zero,0,0)>0) { Native.TranslateMessage(ref message); Native.DispatchMessageW(ref message); }
        }
        catch(Exception e) { ProtectionFailed=wantPrivate; SmokescreenPlugin.Logger.LogWarning("HUD-Fenster: "+e.Message); }
        finally
        {
            protectedWindow=false;
            if(priv!=IntPtr.Zero) { Native.ShowWindow(priv,0); Native.DestroyWindow(priv); }
            if(rec!=IntPtr.Zero) Native.DestroyWindow(rec);
            surface?.Dispose(); recording?.Dispose(); priv=rec=IntPtr.Zero;
            lock(gate) { pixels=truth=null; pending=available=reserve=null; }
        }
    }
    private static IntPtr WindowProc(IntPtr hwnd,uint msg,IntPtr w,IntPtr l)
    {
        try
        {
            if(msg==Native.WM_TIMER) { Tick(); return IntPtr.Zero; }
            if(msg==Native.WM_NCHITTEST && hwnd==priv) return (IntPtr)(-1);
            if(msg==Native.WM_PAINT && hwnd==rec)
            {
                var ps=new Native.PAINTSTRUCT(); var dc=Native.BeginPaint(hwnd,ref ps);
                try { recording?.Blit(dc, hwnd); } finally { Native.EndPaint(hwnd,ref ps); }
                return IntPtr.Zero;
            }
            if(msg==Native.WM_CLOSE) { if(!running) Native.PostQuitMessage(0); else Native.ShowWindow(hwnd,0); return IntPtr.Zero; }
        }
        catch(Exception e) { if(priv!=IntPtr.Zero) Native.ShowWindow(priv,0); shown=false; SmokescreenPlugin.Logger.LogWarning("HUD-Ausgabe: "+e.Message); }
        return Native.DefWindowProcW(hwnd,msg,w,l);
    }
    private static void Tick()
    {
        ProcessPending();
        if(game==IntPtr.Zero || !Native.IsWindow(game)) Native.EnumWindows(enumerate,IntPtr.Zero);
        bool safe=protectedWindow && Verify();
        if(protectedWindow && !safe) { protectedWindow=false; ProtectionFailed=true; }
        bool visible=show && GameForeground && safe && Native.IsWindowVisible(game) && Environment.TickCount64-Interlocked.Read(ref publishedAt)<500;
        if(!visible) { if(shown) Native.ShowWindow(priv,0); shown=false; }
        {
            if(pixels==null) return;
            if(visible && Native.GetClientRect(game,out var rc))
            {
                var origin=new Native.POINT(); Native.ClientToScreen(game,ref origin);
                // Kein Skalieren eines alten Bildes während eines Auflösungswechsels.
                if(rc.right-rc.left!=width || rc.bottom-rc.top!=height) { Native.ShowWindow(priv,0); shown=false; return; }
                if(presented!=version) { surface.Upload(pixels,width,height); presented=version; }
                if(surface.Present(priv,origin.x,origin.y)) { if(!shown) Native.ShowWindow(priv,4); shown=true; }
                else { Native.ShowWindow(priv,0); shown=false; }
            }
            if(rec!=IntPtr.Zero && truth!=null)
            {
                if(feedActive && recorded!=version) { recording.Upload(truth,width,height); recorded=version; Native.InvalidateRect(rec,IntPtr.Zero,false); }
                else if(!feedActive && recorded!=-1) { Array.Clear(truth,0,truth.Length); recording.Upload(truth,width,height); recorded=-1; Native.InvalidateRect(rec,IntPtr.Zero,false); }
            }
        }
    }
    private sealed class Surface : IDisposable
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
        internal bool Present(IntPtr hwnd,int x,int y)
        {
            var dst=new Native.POINT { x=x,y=y }; var src=new Native.POINT(); var size=new Native.SIZE { cx=width,cy=height };
            var blend=new Native.BLENDFUNCTION { SourceConstantAlpha=255,AlphaFormat=1 };
            return Native.UpdateLayeredWindow(hwnd,IntPtr.Zero,ref dst,ref size,dc,ref src,0,ref blend,2);
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
    public const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
    public const uint ULW_ALPHA = 2;
    public const uint ANTIALIASED_QUALITY = 4;
    public delegate bool EnumProc(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr data);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
    [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern void PostQuitMessage(int exit);
    [DllImport("user32.dll")] public static extern bool KillTimer(IntPtr hwnd, UIntPtr id);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern bool StretchBlt(IntPtr target,int x,int y,int w,int h,IntPtr source,int sx,int sy,int sw,int sh,uint op);
    [DllImport("gdi32.dll")] public static extern bool PatBlt(IntPtr target,int x,int y,int w,int h,uint op);
    [DllImport("dwmapi.dll")] public static extern int DwmIsCompositionEnabled(out bool enabled);
    [DllImport("ntdll.dll")] public static extern int RtlGetVersion(ref OSVERSION version);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct OSVERSION { public int size, major, minor, build, platform; [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string service; }


    public static uint Rgb(byte r, byte g, byte b) => (uint)(r | (g << 8) | (b << 16));   // COLORREF

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

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandleW(string name);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);
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
    [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll")]
    public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dst, ref POINT pos, ref SIZE size, IntPtr src, ref POINT srcPos, uint key, ref BLENDFUNCTION blend, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("user32.dll")] public static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase);
    [DllImport("user32.dll")] public static extern IntPtr BeginPaint(IntPtr hwnd, ref PAINTSTRUCT ps);
    [DllImport("user32.dll")] public static extern bool EndPaint(IntPtr hwnd, ref PAINTSTRUCT ps);
    [DllImport("user32.dll")] public static extern IntPtr LoadCursorW(IntPtr inst, int id);

    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFO bi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateSolidBrush(uint color);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateFontW(int h, int w, int esc, int orient, int weight, uint italic, uint underline, uint strike, uint charset, uint outPrec, uint clipPrec, uint quality, uint pitch, string face);
    [DllImport("gdi32.dll")] public static extern int SetBkMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] public static extern uint SetTextColor(IntPtr dc, uint color);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern bool TextOutW(IntPtr dc, int x, int y, string s, int n);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern bool GetTextExtentPoint32W(IntPtr dc, string s, int n, out SIZE size);
    [DllImport("gdi32.dll")] public static extern bool GdiFlush();
    [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
}
