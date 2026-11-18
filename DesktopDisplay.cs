// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using System;
using System.Runtime.InteropServices;
using UnityEngine;
namespace UnknownsSmokescreen;

internal static class DesktopDisplay
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int size;
        public Native.RECT monitor, work;
        public uint flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string device;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string device;
        public ushort specVersion, driverVersion, size, driverExtra;
        public uint fields;
        public int positionX, positionY;
        public uint orientation, fixedOutput;
        public short color, duplex, yResolution, ttOption, collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string form;
        public ushort logPixels;
        public uint bitsPerPixel, width, height, flags, frequency, icmMethod, icmIntent,
            mediaType, ditherType, reserved1, reserved2, panningWidth, panningHeight;
    }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetWindowInfo(IntPtr window, ref WindowInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettingsW(string device, int mode, ref DisplayMode info);
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowInfo
    {
        public uint size;
        public Native.RECT window, client;
        public uint style, extendedStyle, status, borderWidth, borderHeight;
        public ushort type, version;
    }

    internal readonly record struct ClientArea(IntPtr Window, IntPtr Monitor, uint Dpi, int X, int Y, int Width, int Height)
    {
        internal bool SameLayout(ClientArea other) => Window == other.Window && Monitor == other.Monitor
            && Dpi == other.Dpi && Width == other.Width && Height == other.Height;
        internal bool FitsPixelBudget => Width > 0 && Height > 0 && (long)Width * Height <= 3840L * 2160;
    }

    // Der aufrufende Fensterthread arbeitet im Per-Monitor-V2-Kontext.
    internal static ClientArea Client(IntPtr window)
    {
        var info = new WindowInfo { size = (uint)Marshal.SizeOf<WindowInfo>() };
        if (window == IntPtr.Zero || Native.IsIconic(window) || !GetWindowInfo(window, ref info)) return default;
        var monitor = MonitorFromWindow(window, 2);
        var rect = info.client;
        return new ClientArea(window, monitor, GetDpiForWindow(window), rect.left, rect.top,
            rect.right - rect.left, rect.bottom - rect.top);
    }

    internal static (int width, int height) Resolution()
    {
        var info = new MonitorInfo { size = Marshal.SizeOf<MonitorInfo>() };
        if (GetMonitorInfoW(MonitorFromWindow(OverlayHost.GameWindow, 2), ref info))
        {
            // Registry-Modus bleibt die Desktopgröße, wenn exklusives Vollbild
            // die aktuelle Monitorauflösung vorübergehend verändert hat.
            var mode = new DisplayMode { size = (ushort)Marshal.SizeOf<DisplayMode>() };
            if (EnumDisplaySettingsW(info.device, -2, ref mode) && mode.width > 0 && mode.height > 0)
                return ((int)mode.width, (int)mode.height);
            return (info.monitor.right - info.monitor.left, info.monitor.bottom - info.monitor.top);
        }
        var fallback = Screen.currentResolution;
        return (fallback.width, fallback.height);
    }
}

internal readonly struct PerMonitorDpiScope : IDisposable
{
    private static readonly IntPtr PerMonitorV2 = new(-4);
    private readonly IntPtr previous;

    private PerMonitorDpiScope(IntPtr previous) => this.previous = previous;
    internal static PerMonitorDpiScope Enter() => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 15063)
        ? new PerMonitorDpiScope(SetThreadDpiAwarenessContext(PerMonitorV2)) : default;
    internal bool Active => previous != IntPtr.Zero;
    internal static bool IsPerMonitorV2(IntPtr window) =>
        AreDpiAwarenessContextsEqual(GetWindowDpiAwarenessContext(window), PerMonitorV2);

    public void Dispose()
    {
        if (Active) SetThreadDpiAwarenessContext(previous);
    }

    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
    [DllImport("user32.dll")] private static extern bool AreDpiAwarenessContextsEqual(IntPtr a, IntPtr b);
}
