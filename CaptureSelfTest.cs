// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.IO;
using System.Threading.Tasks;

namespace UnknownsSmokescreen;

internal static unsafe class CaptureSelfTest
{
    private const int Size = 48;
    internal enum Verdict { Untested, Excluded, Visible }
    internal sealed record Report(Verdict Gdi, Verdict Dxgi, Verdict Wgc, string Message);
    private static volatile Task<Report> test;
    private static Report last;
    private static volatile bool stopped;
    private static readonly string ApprovalFile = Path.Combine(BepInEx.Paths.ConfigPath, "smokescreen-layout.txt");
    private static string approvedKey = LoadApproval();
    private static string LoadApproval()
    {
        try { return File.Exists(ApprovalFile) ? File.ReadAllText(ApprovalFile) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
    internal static bool Approved => OverlayHost.LayoutKey is { } key && key == Volatile.Read(ref approvedKey);
    private static volatile bool inRound;
    private sealed record MenuProbe(uint[] Pixels, int X, int Y, int Epoch, int Layout, IntPtr Window);
    private static MenuProbe pending, display;
    internal static void SetRound(bool started)
    {
        inRound = started;
        var request = Interlocked.Exchange(ref pending, null);
        if (request == null || !Valid(request)) return;
        var texture = new UnityEngine.Texture2D(Size, Size, UnityEngine.TextureFormat.RGBA32, false, true);
        try
        {
            var colors = new UnityEngine.Color32[Size*Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                uint pixel = request.Pixels[y*Size+x];
                colors[(Size-1-y)*Size+x] = new UnityEngine.Color32((byte)(pixel>>16),(byte)(pixel>>8),(byte)pixel,255);
            }
            texture.SetPixels32(colors);
            texture.Apply(false, false);
            GpuPresenter.Submit(texture.GetNativeTexturePtr(), Size, Size, request.Epoch, true);
            Volatile.Write(ref display, request);
        }
        finally { UnityEngine.Object.Destroy(texture); }
    }
    private static bool Valid(MenuProbe request) => !stopped && Running && !Approved
        && request.Layout == OverlayHost.Layout && request.Epoch == OverlayHost.Epoch
        && request.Window == OverlayHost.PrivateWindow && OverlayHost.GameForeground;
    internal static bool ProbePosition(int ready, out int x, out int y, out int size)
    {
        x = y = 0;
        size = Size;
        var request = Volatile.Read(ref display);
        if (request == null || ready != request.Epoch || !Valid(request)) return false;
        x = request.X;
        y = request.Y;
        return true;
    }
    internal static string Status
    {
        get
        {
            if (Running) return Texts.T("menu.capture.testing");
            var result = Results;
            if (result == null || attemptedLayout != OverlayHost.Layout) return Texts.T("capture.test.preflight");
            if (result.Message != "menu.capture.result") return Texts.T(result.Message);
            string path = result.Gdi == Verdict.Visible ? "GDI" : result.Dxgi == Verdict.Visible ? "DXGI"
                : result.Wgc == Verdict.Visible ? "WGC" : result.Gdi != Verdict.Excluded ? "GDI"
                : result.Dxgi != Verdict.Excluded ? "DXGI" : "WGC";
            return Texts.F("capture.test.blocked", path);
        }
    }
    private static int attemptedLayout = -1;

    internal static bool Running => test is { IsCompleted: false };

    internal static Report Results
    {
        get
        {
            if (test?.IsCompleted == true)
            {
                last = test.IsCompletedSuccessfully ? test.Result : Unavailable("capture.test.failed");
                test = null;
            }
            return last;
        }
    }

    internal static void Start()
    {
        if (Running || stopped) return;
        _ = Results;
        bool menu = !inRound && UnityEngine.Object.FindObjectOfType<MainMenuManager>();
        Volatile.Write(ref approvedKey, null);
        try { File.Delete(ApprovalFile); }
        catch (Exception) { last = Unavailable("capture.test.failed"); return; }
        int layout = attemptedLayout = OverlayHost.Layout;
        if (!OverlayHost.Verified || !OverlayHost.SupportedProtection())
        {
            last = Unavailable("capture.test.affinity");
            return;
        }
        var game = CapturePopup.ForegroundGame();
        if (game == IntPtr.Zero)
        {
            last = Unavailable("capture.test.foreground");
            return;
        }
        last = null;
        // Ein frisches HWND hat noch nie Rollenpixel enthalten.
        OverlayHost.Restart(true, SmokescreenPlugin.FeedEnabled);
        test = Task.Factory.StartNew(() =>
        {
            using var dpi = PerMonitorDpiScope.Enter();
            if (!dpi.Active) return Unavailable("capture.test.window");
            long deadline = Environment.TickCount64 + 2500;
            while (!stopped && (!OverlayHost.Verified || OverlayHost.ClientSize == 0)
                && Environment.TickCount64 < deadline) Thread.Sleep(16);
            if (!OverlayHost.Verified || OverlayHost.ClientSize == 0) return Unavailable("capture.test.affinity");
            Thread.Sleep(80);
            layout = attemptedLayout = OverlayHost.Layout;
            var area = DesktopDisplay.Client(game);
            var key = OverlayHost.LayoutKey;
            var report = ProbeMenu(area, menu, () => !stopped && (!menu || !inRound) && CapturePopup.ForegroundGame() == game
                && OverlayHost.Verified && OverlayHost.Layout == layout && DesktopDisplay.Client(game) == area);
            var paths = new[] { report.Gdi, report.Dxgi, report.Wgc };
            if (key != null && OverlayHost.Layout == layout && Array.IndexOf(paths, Verdict.Visible) < 0
                && Array.FindAll(paths, v => v == Verdict.Excluded).Length >= 2)
            {
                File.WriteAllText(ApprovalFile, key);
                Volatile.Write(ref approvedKey, key);
            }
            SmokescreenPlugin.Logger.LogInfo($"Aufnahmetest: GDI={report.Gdi}, DXGI={report.Dxgi}, WGC={report.Wgc}");
            return report;
        }, TaskCreationOptions.LongRunning);
    }

    private sealed record Tile(int X, int Y, uint[] Pixels);

    // Nur das Spiel-HWND lesen. Ein fremdes Fenster darf nie in der offenen Probe erscheinen.
    private static (Tile source, Tile background) ReadMenu(DesktopDisplay.ClientArea area)
    {
        using var frame = new OverlayHost.LayerSurface();
        frame.Ensure(area.Width, area.Height);
        if (!PrintWindow(area.Window, frame.Dc, 3) || !Native.GdiFlush()) return default;
        Tile source = null, background = null;
        int best = 0, difference = 0;
        for (int y = Size; y + Size < area.Height; y += Size)
        for (int x = Size; x + Size < area.Width; x += Size)
        {
            int edges = 0;
            for (int j = 3; j < Size; j += 4)
            for (int i = 3; i < Size; i += 4)
            {
                var pixel = frame.Bits + (y+j)*area.Width+x+i;
                if (!Close(*pixel, pixel[-3], 64)) edges++;
            }
            if (edges <= best) continue;
            best = edges;
            source = ReadTile(x,y);
        }
        if (source == null || best < 8) return default;
        for (int y = Size; y + Size < area.Height; y += Size)
        for (int x = Size; x + Size < area.Width; x += Size)
        {
            int count = 0, edges = 0;
            for (int j = 3; j < Size; j += 4)
            for (int i = 3; i < Size; i += 4)
            {
                var pixel = frame.Bits + (y+j)*area.Width+x+i;
                if (!Close(*pixel, pixel[-3], 64)) edges++;
                if (!Close(*pixel, source.Pixels[j*Size+i], 80)) count++;
            }
            if (edges < 8 || count <= difference) continue;
            difference = count;
            background = ReadTile(x,y);
        }
        return difference >= 32 ? (source, background) : default;

        Tile ReadTile(int x, int y)
        {
            var pixels = new uint[Size*Size];
            for (int j = 0; j < Size; j++)
                new ReadOnlySpan<uint>(frame.Bits+(y+j)*area.Width+x,Size).CopyTo(pixels.AsSpan(j*Size));
            return new Tile(area.X+x,area.Y+y,pixels);
        }
    }

    private static (Tile, Tile) SyntheticProbe(DesktopDisplay.ClientArea area)
    {
        var source = new uint[Size * Size];
        var background = new uint[source.Length];
        Array.Fill(source, 0xffb580c0u);
        Array.Fill(background, 0xff242524u);
        int x = area.X + area.Width / 2, y = area.Y + area.Height / 2;
        return (new Tile(x, y, source), new Tile(x, y, background));
    }

    private static Report ProbeMenu(DesktopDisplay.ClientArea area, bool menu, Func<bool> stable)
    {
        try
        {
            if (!stable() || !area.HasArea) return Unavailable("capture.test.window");
            var (source, background) = menu ? ReadMenu(area) : SyntheticProbe(area);
            if (source == null || !stable()) return Unavailable("capture.test.menu");
            // Zwei aufeinanderfolgende Kennungen unterscheiden beide Messphasen
            // vom animierten Menü und von einem verspäteten DXGI-Bild.
            uint[] Reference(int phase)
            {
                var pixels = (uint[])background.Pixels.Clone();
                for (int y = 3; y < Size; y += 12)
                for (int x = 3; x < Size; x += 12)
                {
                    int k = y * Size + x;
                    uint green = ((pixels[k] >> 8) + (uint)(phase * 80)) & 255;
                    pixels[k] = (pixels[k] & 0xffff00ffu) | (green << 8);
                }
                return pixels;
            }
            var reference = Reference(1);
            Verdict gdiControl, dxgiControl, wgcControl;
            using (var gpu = new DesktopCaptureProbe(background.X, background.Y, Size))
            using (var controlWindow = new CapturePopup(false))
            using (var image = gpu.MenuImage(controlWindow.Window, reference))
            {
                if (!stable() || !controlWindow.ShowComposition(background.X, background.Y, Size) || DwmFlush() < 0)
                    return Unavailable("capture.test.menu");
                gdiControl = Measure(() => ReadGdi(background.X,background.Y), true);
                dxgiControl = MeasureDesktop(gpu, true);
                wgcControl = Measure(() => gpu.WindowsGraphics(), true);
            }
            reference = Reference(2);
            using var probe = new DesktopCaptureProbe(background.X, background.Y, Size);
            using var backdrop = new CapturePopup(false);
            using var backdropImage = probe.MenuImage(backdrop.Window, reference);
            if (!stable() || !backdrop.ShowComposition(background.X, background.Y, Size) || DwmFlush() < 0)
                return Unavailable("capture.test.menu");
            var window = OverlayHost.PrivateWindow;
            OverlayHost.Discard();
            var request = new MenuProbe(source.Pixels, background.X, background.Y, OverlayHost.Epoch, OverlayHost.Layout, window);
            Volatile.Write(ref pending,request);
            long deadline = Environment.TickCount64 + 2000;
            while (stable() && Environment.TickCount64 < deadline && !Native.IsWindowVisible(window)) Thread.Sleep(16);
            if (!stable() || !Native.IsWindowVisible(window) || DwmFlush() < 0) return Unavailable("capture.test.changed");
            Verdict Evaluate(Verdict control, Func<uint[]> read)
            {
                var result = Measure(read, false);
                return result == Verdict.Excluded && control != Verdict.Visible ? Verdict.Untested : result;
            }
            var gdi = Evaluate(gdiControl, () => ReadGdi(background.X,background.Y));
            var dxgi = MeasureDesktop(probe, false);
            if (dxgi == Verdict.Excluded && dxgiControl != Verdict.Visible) dxgi = Verdict.Untested;
            var wgc = Evaluate(wgcControl, () => probe.WindowsGraphics());
            if (!stable() || !Valid(request) || !Native.IsWindowVisible(window) || !OverlayHost.IsCaptureExcluded(window)) return Unavailable("capture.test.changed");
            if (dxgi == Verdict.Untested) SmokescreenPlugin.Logger.LogInfo($"DXGI-Menükontrolle: {dxgiControl}");
            return new Report(gdi, dxgi, wgc, "menu.capture.result");

            Verdict MeasureDesktop(DesktopCaptureProbe device, bool control)
            {
                try
                {
                    device.StartDuplication();
                    var verdict = Measure(device.Duplicate, control);
                    if (verdict == Verdict.Untested)
                        SmokescreenPlugin.Logger.LogInfo($"AcquireNextFrame HRESULT=0x{device.LastAcquireResult:X8}");
                    return verdict;
                }
                catch (Exception e)
                {
                    SmokescreenPlugin.Logger.LogWarning($"DXGI-Menüprobe: {e.Message} (0x{e.HResult:X8})");
                    return Verdict.Untested;
                }
            }

            Verdict Measure(Func<uint[]> read, bool control)
            {
                int readbacks = 0, best = 0;
                long deadline = Environment.TickCount64 + 3000;
                do
                {
                    if (!stable()) break;
                    uint[] pixels;
                    try { pixels = read(); }
                    catch (Exception e)
                    {
                        SmokescreenPlugin.Logger.LogWarning($"Aufnahmeprobe {read.Method.Name}: 0x{e.HResult:X8}");
                        return Verdict.Untested;
                    }
                    if (pixels != null)
                    {
                        readbacks++;
                        int matches = 0;
                        for (int j = 3; j < Size; j += 4)
                        for (int i = 3; i < Size; i += 4)
                        {
                            int k = j*Size+i;
                            if (!control && !Close(source.Pixels[k],reference[k],80)
                                && Close(pixels[k],source.Pixels[k])) return Verdict.Visible;
                            if (Close(pixels[k], reference[k])) matches++;
                        }
                        best = Math.Max(best, matches);
                        if (matches == Size * Size / 16) return control ? Verdict.Visible : Verdict.Excluded;
                    }
                    CapturePopup.PumpMessages();
                    Thread.Sleep(16);
                } while (Environment.TickCount64 < deadline);
                SmokescreenPlugin.Logger.LogInfo($"Menüprobe {read.Method.Name}: {readbacks} Bilder, {best}/{Size * Size / 16} Treffer");
                return Verdict.Untested;
            }
        }
        catch (Exception e)
        {
            SmokescreenPlugin.Logger.LogWarning($"Aufnahmetest: {e.Message} (0x{e.HResult:X8})");
            return Unavailable("capture.test.failed");
        }
        finally
        {
            Interlocked.Exchange(ref pending,null);
            Volatile.Write(ref display,null);
            OverlayHost.Discard();
        }
    }

    private static Report Unavailable(string message) => new(Verdict.Untested, Verdict.Untested, Verdict.Untested, message);

    private static bool Close(uint a, uint b, int tolerance = 24)
    {
        for (int shift = 0; shift < 24; shift += 8)
            if (Math.Abs((int)((a >> shift) & 255) - (int)((b >> shift) & 255)) > tolerance) return false;
        return true;
    }

    private static uint[] ReadGdi(int x, int y)
    {
        var screen = GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero) return null;
        try
        {
            using var target = new OverlayHost.LayerSurface();
            target.Ensure(Size, Size);
            if (!BitBlt(target.Dc, 0, 0, Size, Size, screen, x, y, 0x40cc0020) || !Native.GdiFlush()) return null;
            return new ReadOnlySpan<uint>(target.Bits, Size * Size).ToArray();
        }
        finally { ReleaseDC(IntPtr.Zero, screen); }
    }

    internal static void Stop()
    {
        stopped = true;
        try { test?.Wait(1800); } catch (AggregateException) { }
    }

    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sx, int sy, uint op);
}
