// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace UnknownsSmokescreen;

internal static unsafe class CaptureSelfTest
{
    private const int Size = 48, Tile = Size / 4;
    private const uint PublicFirst = 0xff137ed4, PublicSecond = 0xffe76e21;
    private const uint PrivateFirst = 0xffe31fb6, PrivateSecond = 0xff1de25c;
    internal enum Verdict { Untested, Excluded, Visible }
    internal sealed record Report(Verdict Gdi, Verdict Dxgi, Verdict Wgc, string Message);
    private static volatile Task<Report> test;
    private static Report last;
    private static volatile bool stopped;
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
        test = Task.Factory.StartNew(() =>
        {
            using var dpi = PerMonitorDpiScope.Enter();
            if (!dpi.Active) return Unavailable("capture.test.window");
            if (!Center(game, out var point)) return Unavailable("capture.test.window");
            var report = ProbeAt(point.x, point.y, () => !stopped && CapturePopup.ForegroundGame() == game
                && OverlayHost.Verified && Center(game, out var current) && point.x == current.x && point.y == current.y);
            SmokescreenPlugin.Logger.LogInfo($"Aufnahmetest: GDI={report.Gdi}, DXGI={report.Dxgi}, WGC={report.Wgc}");
            return report;
        }, TaskCreationOptions.LongRunning);
    }

    private static bool Center(IntPtr game, out Native.POINT point)
    {
        var area = DesktopDisplay.Client(game);
        point = new Native.POINT { x = area.X + (area.Width - Size) / 2, y = area.Y + (area.Height - Size) / 2 };
        return area.Width >= Size && area.Height >= Size;
    }

    internal static Report ProbeAt(int x, int y, Func<bool> stable)
    {
        try
        {
            using var dpi = PerMonitorDpiScope.Enter();
            if (!dpi.Active) return Unavailable("capture.test.window");
            if (!stable()) return Unavailable("capture.test.foreground");
            // Das öffentliche Muster am selben Ort bestätigt, dass der Capture-Pfad den richtigen Ausschnitt liest.
            using var reference = new CapturePopup(false);
            using var overlay = new CapturePopup(true);
            if (!overlay.Verified || !reference.Pattern(x, y, Size, PublicFirst, PublicSecond)
                || !overlay.Pattern(x, y, Size, PrivateFirst, PrivateSecond) || DwmFlush() < 0)
                return Unavailable("capture.test.pattern");
            long presentedAt = Environment.TickCount64;
            CapturePopup.PumpMessages();
            var gdi = Measure("GDI", () => ReadGdi(x, y));
            var dxgi = Verdict.Untested;
            var wgc = Verdict.Untested;
            try
            {
                using var gpu = new DesktopCaptureProbe(x, y, Size);
                dxgi = Measure("DXGI", gpu.Duplicate);
                if (stable()) wgc = Measure("WGC", gpu.WindowsGraphics);
            }
            catch (Exception e) { LogFailure("DXGI/WGC", e); }
            while (Environment.TickCount64 - presentedAt < 300 && stable())
            {
                CapturePopup.PumpMessages();
                Thread.Sleep(15);
            }
            if (!stable() || !overlay.Verified) return Unavailable("capture.test.changed");
            return new Report(gdi, dxgi, wgc, "menu.capture.result");
        }
        catch (Exception e)
        {
            LogFailure("Prüfmuster", e);
            return Unavailable("capture.test.pattern");
        }
    }

    private static Report Unavailable(string message) => new(Verdict.Untested, Verdict.Untested, Verdict.Untested, message);

    private static Verdict Measure(string method, Func<uint[]> read)
    {
        try { return Classify(read()); }
        catch (Exception e) { LogFailure(method, e); return Verdict.Untested; }
    }

    private static void LogFailure(string method, Exception e)
        => SmokescreenPlugin.Logger?.LogWarning($"Aufnahmetest {method}: {e.GetType().Name} (0x{e.HResult:X8})");

    private static Verdict Classify(uint[] pixels)
    {
        if (pixels == null || pixels.Length != Size * Size) return Verdict.Untested;
        int control = 0, visible = 0;
        for (int row = 0; row < 4; row++)
        for (int col = 0; col < 4; col++)
        {
            bool even = ((row + col) & 1) == 0;
            uint color = pixels[(row * Tile + Tile / 2) * Size + col * Tile + Tile / 2];
            if (Close(color, even ? PrivateFirst : PrivateSecond)) visible++;
            if (Close(color, even ? PublicFirst : PublicSecond)) control++;
        }
        if (visible >= 2) return Verdict.Visible;
        return visible == 0 && control == 16 ? Verdict.Excluded : Verdict.Untested;
    }

    private static bool Close(uint a, uint b)
    {
        for (int shift = 0; shift < 24; shift += 8)
            if (Math.Abs((int)((a >> shift) & 255) - (int)((b >> shift) & 255)) > 24) return false;
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

    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sx, int sy, uint op);
}
