// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace UnknownsSmokescreen;

// Eigene klickdurchlässige Fenster für Prüfmuster und Warnung, ohne Unity-Aufrufe.
internal sealed unsafe class CapturePopup : IDisposable
{
    internal const uint Styles = Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOPMOST
        | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
    private static readonly Native.WndProc Procedure = Native.DefWindowProcW;
    private readonly IntPtr instance = Native.GetModuleHandleW(null);
    private readonly string className = "SmokescreenCapture" + Guid.NewGuid().ToString("N");
    private readonly OverlayHost.LayerSurface surface = new();
    private readonly bool protect;
    private IntPtr window;

    internal CapturePopup(bool protect)
    {
        this.protect = protect;
        var wc = new Native.WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<Native.WNDCLASSEXW>(), lpfnWndProc = Marshal.GetFunctionPointerForDelegate(Procedure),
            hInstance = instance, lpszClassName = className
        };
        if (Native.RegisterClassExW(ref wc) == 0) return;
        window = Native.CreateWindowExW(Styles, className, "Smokescreen", Native.WS_POPUP,
            0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        if (window != IntPtr.Zero && protect && (!OverlayHost.SupportedProtection()
            || !Native.SetWindowDisplayAffinity(window, Native.WDA_EXCLUDEFROMCAPTURE) || !Verified))
        {
            Native.DestroyWindow(window);
            window = IntPtr.Zero;
        }
    }

    internal bool Exists => window != IntPtr.Zero;
    internal bool Verified => OverlayHost.IsCaptureExcluded(window);

    internal bool Pattern(int x, int y, int size, uint first, uint second)
    {
        if (!Exists) return false;
        surface.Ensure(size, size);
        for (int row = 0; row < size; row++)
        for (int col = 0; col < size; col++)
            surface.Bits[row * size + col] = ((row / (size / 4) + col / (size / 4)) & 1) == 0 ? first : second;
        return Present(x, y);
    }

    internal bool Warning(int x, int y, int w, string message)
    {
        if (!Exists) return false;
        const int h = 72;
        surface.Ensure(w, h);
        for (int i = 0; i < w * h; i++) surface.Bits[i] = 0xff322317;
        SetBkMode(surface.Dc, 1);
        SetTextColor(surface.Dc, 0x00e5f2ff);
        var rect = new Native.RECT { left = 12, top = 12, right = w - 12, bottom = h - 6 };
        DrawTextW(surface.Dc, message, -1, ref rect, 0x0010 | 0x0800);
        Native.GdiFlush();
        for (int i = 0; i < w * h; i++) surface.Bits[i] |= 0xff000000;
        return Present(x, y);
    }

    private bool Present(int x, int y)
    {
        if ((!protect || Verified) && surface.Present(window, x, y, true, 0, 0, surface.Width, surface.Height))
        {
            Native.ShowWindow(window, 4);
            if (SetWindowPos(window, new IntPtr(-1), 0, 0, 0, 0, 0x0013) && Native.IsWindowVisible(window)) return true;
        }
        Hide();
        return false;
    }

    internal void Hide()
    {
        if (Exists) Native.ShowWindow(window, 0);
    }

    public void Dispose()
    {
        if (Exists) Native.DestroyWindow(window);
        window = IntPtr.Zero;
        surface.Dispose();
        Native.UnregisterClassW(className, instance);
    }

    internal static IntPtr ForegroundGame()
    {
        var hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero || Native.IsIconic(hwnd) || !Native.IsWindowVisible(hwnd)) return IntPtr.Zero;
        Native.GetWindowThreadProcessId(hwnd, out uint owner);
        if (owner != Environment.ProcessId) return IntPtr.Zero;
        if (hwnd == OverlayHost.GameWindow) return hwnd;
        var name = new StringBuilder(64);
        Native.GetClassNameW(hwnd, name, name.Capacity);
        return name.ToString() == "UnityWndClass" ? hwnd : IntPtr.Zero;
    }

    internal static void PumpMessages()
    {
        while (PeekMessageW(out var message, IntPtr.Zero, 0, 0, 1))
        {
            Native.TranslateMessage(ref message);
            Native.DispatchMessageW(ref message);
        }
    }

    [DllImport("user32.dll")] private static extern bool PeekMessageW(out Native.MSG message, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int DrawTextW(IntPtr dc, string text, int length, ref Native.RECT rect, uint format);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(IntPtr dc, int mode);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr dc, uint color);
}
