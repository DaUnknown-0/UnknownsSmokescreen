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
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettingsW(string device, int mode, ref DisplayMode info);
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
