// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace UnknownsSmokescreen;

internal static class WindowProtection
{
    internal enum Problem
    {
        None, Wine, VersionQuery, OldWindows, DwmQuery, NoComposition, WindowClass,
        CreateWindow, SetAffinity, ReadAffinity, WrongAffinity, LostRead, LostAffinity, Thread
    }

    internal readonly record struct SystemInfo(int VersionStatus, int Major, int Build, int DwmStatus,
        bool Composited, bool Wine, bool Remote, string Vm)
    {
        internal string Log => $"Windows={Major}.{Build}, RtlGetVersion=0x{VersionStatus:X8}, DWM=0x{DwmStatus:X8}/{Composited}, "
            + $"Wine/Proton={Wine}, RemoteDesktop={Remote}, VM={Vm ?? "kein Hinweis"}";
    }

    internal sealed record Failure(Problem Cause, int Code, uint Value, SystemInfo System, DateTimeOffset At, string Exception)
    {
        internal string Description
        {
            get
            {
                string reason = Texts.F("affinity." + Cause, System.Build, Code, Value);
                if (System.Remote && Cause != Problem.Wine) return reason + " " + Texts.T("affinity.remote");
                if (System.Vm != null && Cause != Problem.Wine) return reason + " " + Texts.T("affinity.vm");
                return reason;
            }
        }

        internal string Log => $"Fenstersperre {Cause}, UTC={At:O}, Code={Code} (0x{Code:X8}), Affinity=0x{Value:X}, {System.Log}"
            + (Exception == null ? "" : ", Ausnahme=" + Exception);
    }

    internal static SystemInfo Inspect(bool vmHint)
    {
        var version = new Native.OSVERSION { size = Marshal.SizeOf<Native.OSVERSION>() };
        int versionStatus = Native.RtlGetVersion(ref version);
        int dwmStatus = Native.DwmIsCompositionEnabled(out bool composed);
        var ntdll = Native.GetModuleHandleW("ntdll.dll");
        return new SystemInfo(versionStatus, version.major, version.build, dwmStatus, composed,
            ntdll != IntPtr.Zero && GetProcAddress(ntdll, "wine_get_version") != IntPtr.Zero,
            GetSystemMetrics(0x1000) != 0, vmHint ? VirtualMachineHint() : null);
    }

    internal static Problem CheckSystem(SystemInfo info)
    {
        if (info.Wine) return Problem.Wine;
        if (info.VersionStatus != 0) return Problem.VersionQuery;
        if (info.Major < 10 || (info.Major == 10 && info.Build < 19041)) return Problem.OldWindows;
        if (info.DwmStatus != 0) return Problem.DwmQuery;
        return info.Composited ? Problem.None : Problem.NoComposition;
    }

    internal static SystemInfo RefreshSession(SystemInfo info) => info with { Remote = GetSystemMetrics(0x1000) != 0 };

    internal static Problem CheckRead(bool success, uint value, bool running)
    {
        if (!success) return running ? Problem.LostRead : Problem.ReadAffinity;
        if (value != Native.WDA_EXCLUDEFROMCAPTURE) return running ? Problem.LostAffinity : Problem.WrongAffinity;
        return Problem.None;
    }

    private static string VirtualMachineHint()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var bios = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            if (bios == null) return null;
            string hardware = $"{bios.GetValue("SystemManufacturer")} {bios.GetValue("SystemProductName")}";
            foreach (string marker in new[] { "VMware", "VirtualBox", "Virtual Machine", "KVM", "QEMU", "Parallels", "Xen", "Bochs", "HVM domU" })
                if (hardware.Contains(marker, StringComparison.OrdinalIgnoreCase)) return marker;
        }
        catch (Exception) { }
        return null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
}
