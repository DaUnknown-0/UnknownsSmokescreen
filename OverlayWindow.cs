// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace UnknownsSmokescreen;

// Eigener Win32-Thread ohne Unity- oder Il2Cpp-Zugriffe. Er baut die Inseln aus den Rohbildern,
// setzt sie pro Spielframe an ihre Ankerlage und aktualisiert nur das geaenderte Rechteck.
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
    private const uint WM_POSITIONS = 0x8001, WM_CAPTURE = 0x8002, WM_FEED = 0x8003;
    private static string diagnosticDirectory;
    private static long diagnosticDeadline;
    private static int dumpCount;
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
    internal static void SetVisible(bool value, bool record = false) { show = value; feedActive = record; }

    internal static void Start(bool privateOverlay, bool recordingFeed)
    {
        if (thread is { IsAlive: false }) thread = null;
        if (thread != null) return;
        ProtectionFailed = false;
        AffinityFailure = null;
        if (!privateOverlay && !recordingFeed) return;
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
    // Fenster nach einer Aenderung von PrivateOverlay oder RecordingFeed neu anlegen.
    internal static void Restart(bool privateOverlay, bool recordingFeed)
    {
        Stop();
        if (thread != null) return;
        lock (gate)
        {
            epoch++;
            if (pending != null) { free.Push(pending); pending = null; }
            positionsPending = false; clearRequested = false;
        }
        shown = false; hasContent = false; needFull = true; presentedX = presentedY = int.MinValue;
        historyCount = 0; historyNext = 0;
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

    // --- Uebergabe vom Hauptthread ------------------------------------------------------------------

    internal sealed class CaptureSet
    {
        internal byte[] Atlas = Array.Empty<byte>();
        internal IslandDesc[] Islands = new IslandDesc[64];
        internal int Stride, AtlasWidth, AtlasHeight, Width, Height, Count, Epoch;
        internal float Ortho;
        internal long CapturedAt;
        internal bool Empty;
    }
    private static readonly Stack<CaptureSet> free = new(new[] { new CaptureSet(), new CaptureSet(), new CaptureSet() });
    private static CaptureSet pending;
    private static int epoch;
    private static bool clearRequested;
    private static readonly AnchorPositions incoming = new(), current = new();
    // Die Swapchain des Spiels zeigt einen Frame spaeter als DWM das Overlay. Verankerte Inseln nutzen deshalb
    // die Ankerlage von SyncFrames Frames zuvor. Ringpuffer der letzten acht Lagen, nur Fensterthread.
    private static readonly AnchorPositions[] history = { new(), new(), new(), new(), new(), new(), new(), new() };
    private static int historyNext, historyCount;
    // Gezaehlt wird in Spielframes, nicht in verarbeiteten Updates: faellt der Fensterthread kurz zurueck und
    // fasst Updates zusammen, nimmt er die neueste Lage, die mindestens delay Frames alt ist.
    private static AnchorPositions Delayed(int delay)
    {
        if (historyCount == 0 || delay <= 0) return current;
        int target = current.Frame - delay;
        AnchorPositions oldest = current;
        for (int k = 0; k < historyCount; k++)
        {
            var h = history[(historyNext - 1 - k + history.Length * 2) % history.Length];
            if (h.Frame <= target) return h;
            oldest = h;
        }
        return oldest;
    }
    private static bool positionsPending;
    private static int postedPositions, postedCapture;
    private static byte[] feedFill, feedReady;
    private static int feedWidth, feedHeight;
    private static bool feedPending;

    internal static CaptureSet BeginCapture(int w, int h, int atlasWidth, int atlasHeight)
    {
        CaptureSet set;
        lock (gate)
        {
            if (free.Count > 0) set = free.Pop();
            else if (pending != null) { set = pending; pending = null; }
            else return null;
        }
        int bytes = checked(atlasWidth * atlasHeight * 4);
        if (set.Atlas.Length < bytes) set.Atlas = new byte[bytes];
        set.Width = w; set.Height = h; set.AtlasWidth = atlasWidth; set.AtlasHeight = atlasHeight;
        set.Stride = atlasWidth * 4; set.Count = 0; set.Empty = false; set.Ortho = 0;
        set.CapturedAt = Environment.TickCount64;
        return set;
    }
    internal static void EnsureIslands(CaptureSet set, int count)
    {
        if (set.Islands.Length < count) set.Islands = new IslandDesc[count + 16];
    }
    internal static void CommitCapture(CaptureSet set)
    {
        lock (gate) { set.Epoch = epoch; if (pending != null) free.Push(pending); pending = set; }
        if (Interlocked.Exchange(ref postedCapture, 1) == 0) Post(WM_CAPTURE);
    }
    internal static void PublishEmpty(int w, int h)
    {
        var set = BeginCapture(w, h, 0, 0);
        if (set == null) return;
        set.Empty = true;
        CommitCapture(set);
    }
    internal static void PublishPositions(AnchorPositions positions)
    {
        lock (gate)
        {
            // Eine noch nicht verarbeitete Diagnoseprobe wird nicht von einem spaeteren Frame ueberschrieben.
            if (positionsPending && incoming.Probe != null) return;
            incoming.CopyFrom(positions); positionsPending = true;
        }
        if (Interlocked.Exchange(ref postedPositions, 1) == 0) Post(WM_POSITIONS);
    }
    internal static void PublishFeed(IntPtr rgba, int w, int h)
    {
        if (!running || !FeedWanted) return;
        int bytes = checked(w * h * 4);
        byte[] buffer;
        lock (gate) { buffer = feedFill; feedFill = null; }
        if (buffer == null || buffer.Length != bytes) buffer = new byte[bytes];
        Marshal.Copy(rgba, buffer, 0, bytes);
        lock (gate) { feedFill = feedReady; feedReady = buffer; feedWidth = w; feedHeight = h; feedPending = true; }
        Post(WM_FEED);
    }
    internal static void Discard()
    {
        lock (gate)
        {
            epoch++;
            if (pending != null) { free.Push(pending); pending = null; }
            positionsPending = false; clearRequested = true;
        }
        Post(WM_CAPTURE);
    }
    private static void Post(uint message)
    {
        var host = priv != IntPtr.Zero ? priv : rec;
        if (host != IntPtr.Zero) Native.PostMessageW(host, message, IntPtr.Zero, IntPtr.Zero);
    }

    // --- Fensterthread ------------------------------------------------------------------------------

    private static LayerSurface surface;
    private static FeedSurface recording;
    private static readonly Composer composer = new();
    private static Island[] poolA = new Island[0], poolB = new Island[0];
    private static bool usePoolA;
    private static int[] distance;
    private static byte[] claimed;
    private static byte[] truth;
    private static int presentedWidth, presentedHeight, presentedX = int.MinValue, presentedY = int.MinValue;
    private static volatile bool shown;
    private static bool hasContent, needFull = true;
    private static long buildTicks, captures, captureTicks, moves, moveTicks, presents, platePixels, seeds, workerReportAt, lastCaptureAt;

    private static unsafe void ProcessCapture()
    {
        Interlocked.Exchange(ref postedCapture, 0);
        CaptureSet set; bool clear;
        lock (gate) { set = pending; pending = null; clear = clearRequested; clearRequested = false; }
        if (clear)
        {
            composer.Clear(); hasContent = false; needFull = true;
            Interlocked.Exchange(ref lastCaptureAt, 0);
        }
        if (set == null) return;
        try
        {
            if (set.Epoch != epoch || surface == null) return;
            long start = Stopwatch.GetTimestamp();
            if (surface.Ensure(set.Width, set.Height)) { composer.Attach(surface.Bits, set.Width, set.Height); needFull = true; }
            var pool = usePoolA ? poolA : poolB;
            if (pool.Length < set.Count)
            {
                var grown = new Island[set.Count + 8];
                Array.Copy(pool, grown, pool.Length);
                for (int i = pool.Length; i < grown.Length; i++) grown[i] = new Island();
                pool = grown;
                if (usePoolA) poolA = pool; else poolB = pool;
            }
            usePoolA = !usePoolA;
            int n = set.Empty ? 0 : set.Count;
            if (n > 0)
            {
                int screen = set.Width * set.Height;
                if (claimed == null || claimed.Length != screen) claimed = new byte[screen]; else Array.Clear(claimed, 0, screen);
                fixed (byte* atlas = set.Atlas)
                    for (int i = 0; i < n; i++)
                    {
                        IslandBuilder.Build(atlas, set.Stride, in set.Islands[i], set.Height, pool[i], ref distance, claimed, set.Width);
                        platePixels += pool[i].PlatePixels; seeds += pool[i].Seeds;
                    }
            }
            buildTicks += Stopwatch.GetTimestamp() - start;
            hasContent = true;
            Interlocked.Exchange(ref lastCaptureAt, set.CapturedAt);
            if (composer.Replace(pool, n, set.Ortho, Delayed(current.Delay), out int x0, out int y0, out int x1, out int y1, current.BlockWorld)) Present(x0, y0, x1, y1);
            captures++; captureTicks += Stopwatch.GetTimestamp() - start;
            Report();
        }
        finally { lock (gate) free.Push(set); }
    }

    private static void ProcessPositions()
    {
        Interlocked.Exchange(ref postedPositions, 0);
        lock (gate) { if (!positionsPending) return; current.CopyFrom(incoming); positionsPending = false; }
        history[historyNext].CopyFrom(current); historyNext = (historyNext + 1) % history.Length; historyCount = Math.Min(historyCount + 1, history.Length);
        long start = Stopwatch.GetTimestamp();
        var display = Delayed(current.Delay);
        bool probed = false; int px0 = 0, py0 = 0, px1 = 0, py1 = 0;
        if (current.Probe != null)
        {
            // Diagnose vergleicht mit dem echten Bild genau dieses Frames: dafuer ohne Verzoegerung komponieren.
            // Das dabei geaenderte Rechteck (alte dargestellte Lage) muss mit dargestellt werden, sonst bleibt sie stehen.
            probed = composer.Move(current, out px0, out py0, out px1, out py1, current.BlockWorld);
            try { DumpProbe(current.Probe, current.Frame); }
            catch (Exception e) { diagnosticDirectory = null; SmokescreenPlugin.Logger.LogWarning("HUD-Diagnose: " + e.Message); }
        }
        bool moved = composer.Move(display, out int x0, out int y0, out int x1, out int y1, current.BlockWorld);
        if (probed)
        {
            if (!moved) { x0 = px0; y0 = py0; x1 = px1; y1 = py1; }
            else { x0 = Math.Min(x0, px0); y0 = Math.Min(y0, py0); x1 = Math.Max(x1, px1); y1 = Math.Max(y1, py1); }
            moved = true;
        }
        if (moved) Present(x0, y0, x1, y1);
        moves++; moveTicks += Stopwatch.GetTimestamp() - start;
    }

    private static void ProcessFeed()
    {
        byte[] data; int w, h;
        lock (gate) { if (!feedPending) return; feedPending = false; data = feedReady; w = feedWidth; h = feedHeight; }
        if (!running || rec == IntPtr.Zero || data == null) return;
        if (truth == null || truth.Length != data.Length) truth = new byte[data.Length];
        unsafe
        {
            fixed (byte* source = data)
            fixed (byte* target = truth)
                PixelDifference.Copy(source, source, target, null, w, h);
        }
        recording.Upload(truth, w, h);
        Native.InvalidateRect(rec, IntPtr.Zero, false);
    }

    private static void Report()
    {
        if (Environment.TickCount64 < workerReportAt) return;
        double frequency = Stopwatch.Frequency / 1000.0;
        SmokescreenPlugin.Logger.LogInfo($"HUD-Fenster: Aufnahmen={captures}, Aufbau={captureTicks / frequency / Math.Max(1, captures):F3} ms (Inseln {buildTicks / frequency / Math.Max(1, captures):F3}), Lagen={moves}, Lage={moveTicks / frequency / Math.Max(1, moves):F3} ms, Ausgaben={presents}, Inseln={composer.Count}, Platte/Aufnahme={platePixels / Math.Max(1, captures)} px, Keime/Aufnahme={seeds / Math.Max(1, captures)}");
        workerReportAt = Environment.TickCount64 + 15000;
        buildTicks = captures = captureTicks = moves = moveTicks = presents = platePixels = seeds = 0;
    }

    private static void Present(int x0, int y0, int x1, int y1)
    {
        if (!shown || surface == null) { needFull = true; return; }
        if (!VerifyRuntime()) return;
        if (!Native.GetClientRect(game, out var rc)) return;
        var origin = new Native.POINT(); Native.ClientToScreen(game, ref origin);
        if (rc.right - rc.left != surface.Width || rc.bottom - rc.top != surface.Height) { Hide(); return; }
        bool full = needFull || origin.x != presentedX || origin.y != presentedY || surface.Width != presentedWidth || surface.Height != presentedHeight;
        if (!surface.Present(priv, origin.x, origin.y, full, x0, y0, x1, y1)) { Hide(); return; }
        presentedX = origin.x; presentedY = origin.y; presentedWidth = surface.Width; presentedHeight = surface.Height;
        needFull = false; presents++;
    }
    private static void Hide()
    {
        if (priv != IntPtr.Zero && shown) Native.ShowWindow(priv, 0);
        shown = false; needFull = true;
    }

    private static unsafe void DumpProbe(string label, int frame)
    {
        string directory = DiagnosticDirectory;
        if (directory == null || surface == null || dumpCount >= 64) return;
        dumpCount++;
        Directory.CreateDirectory(directory);
        string prefix = Path.Combine(directory, label + "_" + frame);
        BitmapFile.Write(prefix + "_overlay.bmp", surface.Bits, surface.Width, surface.Height);
        var text = new StringBuilder();
        text.Append($"frame={frame} t={Environment.TickCount64} captured={lastCaptureAt} islands={composer.Count} positions={current.Count} ortho={current.Ortho}\n");
        var pool = usePoolA ? poolB : poolA;
        for (int i = 0; i < composer.Count && i < pool.Length; i++)
        {
            var s = pool[i];
            text.Append($"island key={s.Key:X} x={s.X} y={s.Y} w={s.W} h={s.H} drawn={s.Drawn} dx={s.DrawX - s.X} dy={s.DrawY - s.Y} plate={s.PlatePixels} alpha={s.AlphaPixels} seeds={s.Seeds}\n");
        }
        File.WriteAllText(prefix + "_overlay.txt", text.ToString());
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

    private static void ProtectWindow(string cls, IntPtr instance)
    {
        var system = windowSystem = WindowProtection.Inspect(true);
        var issue = WindowProtection.CheckSystem(system);
        if (issue != WindowProtection.Problem.None)
        {
            FailProtection(issue, issue == WindowProtection.Problem.VersionQuery ? system.VersionStatus : system.DwmStatus, 0, system);
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
                surface = new LayerSurface();
                protectedWindow = true;
                SmokescreenPlugin.Logger.LogInfo($"Privates HUD: WDA_EXCLUDEFROMCAPTURE bestätigt (0x{affinity:X}), {system.Log}");
                return;
            }
            FailProtection(issue, error, affinity, system);
        }
        Native.DestroyWindow(priv);
        priv = IntPtr.Zero;
    }
    private static void Run()
    {
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
            if (wantPrivate) ProtectWindow(cls, instance);
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
            unsafe { composer.Attach(null,0,0); }
            surface?.Dispose(); recording?.Dispose(); priv=rec=IntPtr.Zero; surface=null; recording=null;
            lock(gate) { truth=null; if(pending!=null) free.Push(pending); pending=null; feedFill=feedReady=null; }
            Native.UnregisterClassW(cls,instance);
        }
    }
    private static IntPtr WindowProc(IntPtr hwnd,uint msg,IntPtr w,IntPtr l)
    {
        try
        {
            if(msg==WM_POSITIONS) { ProcessPositions(); return IntPtr.Zero; }
            if(msg==WM_CAPTURE) { ProcessCapture(); return IntPtr.Zero; }
            if(msg==WM_FEED) { ProcessFeed(); return IntPtr.Zero; }
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
        ProcessCapture();
        ProcessPositions();
        ProcessFeed();
        if(game==IntPtr.Zero || !Native.IsWindow(game)) Native.EnumWindows(enumerate,IntPtr.Zero);
        if(priv==IntPtr.Zero) return;
        // Kein Freilegen des Fake-Bilds bei kurzen Renderpausen. Freigabe am Frameende.
        bool visible=show && GameForeground && safe && Native.IsWindowVisible(game) && hasContent && surface!=null && surface.Width>0;
        if(!visible) { Hide(); return; }
        if(!Native.GetClientRect(game,out var rc) || rc.right-rc.left!=surface.Width || rc.bottom-rc.top!=surface.Height) { Hide(); return; }
        if(!shown)
        {
            shown=true; needFull=true;
            Present(0,0,surface.Width,surface.Height);
            if(shown) Native.ShowWindow(priv,4);
        }
        else
        {
            var origin=new Native.POINT(); Native.ClientToScreen(game,ref origin);
            if(origin.x!=presentedX || origin.y!=presentedY) Present(0,0,surface.Width,surface.Height);
        }
    }

    // Persistente DIB, in die der Komponist direkt schreibt.
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
