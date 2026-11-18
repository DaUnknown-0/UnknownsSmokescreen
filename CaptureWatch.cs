// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace UnknownsSmokescreen;

internal static class CaptureWatch
{
    private static readonly HashSet<string> CaptureProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "obs64", "obs32", "obs", "Streamlabs OBS", "Streamlabs Desktop", "slobs",
        "XSplit.Core", "XSplit.Broadcaster", "XSplitBroadcaster", "XSplit.Gamecaster",
        "PRISMLiveStudio", "PRISM Live Studio", "TwitchStudio"
    };
    private static Task<bool> scan;
    private static Task notification;
    private static long scanAfter, evaluateAfter, startedAt;
    private static bool detected, wasStarted;
    private static volatile string warning;
    private static volatile bool stopped;

    internal static void Tick(bool started, bool active)
    {
        if (stopped) return;
        if (!SmokescreenPlugin.ObsWatch.Value)
        {
            warning = null;
            detected = false;
            wasStarted = false;
            return;
        }
        long now = Environment.TickCount64;
        if (started && !wasStarted) startedAt = now;
        wasStarted = started;
        if (now < evaluateAfter) return;
        evaluateAfter = now + 150;
        if (scan?.IsCompleted == true)
        {
            detected = scan.Status == TaskStatus.RanToCompletion && scan.Result;
            scan = null;
        }
        if (scan == null && now >= scanAfter)
        {
            scanAfter = now + 3000;
            scan = Task.Run(FindCaptureProcess);
        }
        string message = null;
        if (detected)
        {
            if (!SettingsMenu.PatchedAtStartup || !Protection.PatchesReady || !Shield.Wanted || (started && !active && now - startedAt > 1500))
                message = Texts.T("capture.watch.protection");
            else if (!SmokescreenPlugin.PrivateOverlay.Value || OverlayHost.ProtectionFailed
                || (started && now - startedAt > 1500 && (!OverlayHost.Verified
                    || (OverlayHost.GameForeground && !OverlayHost.HiddenByUser && (!OverlayHost.Shown
                        || now - OverlayHost.LastCapture > 1500)))))
                message = Texts.T("capture.watch.overlay");
            if (SmokescreenPlugin.FeedEnabled)
                message = (message ?? Texts.T("capture.watch.detected")) + " " + Texts.T("capture.watch.feed");
            if (started && active && now - startedAt > 1500 && !Protection.MaskConfirmed)
                message = (message ?? Texts.T("capture.watch.detected")) + " " + Texts.T("protection.mask.pending");
        }
        if (warning != message)
        {
            warning = message;
            if (message != null) SmokescreenPlugin.Logger.LogWarning("OBS-Wächter: " + message);
        }
        if (message != null && (notification == null || notification.IsCompleted))
            notification = Task.Factory.StartNew(ShowWarning, TaskCreationOptions.LongRunning);
    }

    private static bool FindCaptureProcess()
    {
        try
        {
            var processes = Process.GetProcesses();
            try
            {
                foreach (var process in processes)
                {
                    try { if (CaptureProcesses.Contains(process.ProcessName)) return true; }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { }
                }
                return false;
            }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    private static void ShowWarning()
    {
        CapturePopup popup = null;
        string previous = null;
        int lastX = 0, lastY = 0;
        bool attempted = false;
        try
        {
            using var dpi = PerMonitorDpiScope.Enter();
            if (!dpi.Active) return;
            while (!stopped && warning is string message)
            {
                var game = CapturePopup.ForegroundGame();
                var area = DesktopDisplay.Client(game);
                if (area.Width < 280 || CaptureSelfTest.Running)
                {
                    popup?.Hide();
                    previous = null;
                }
                else
                {
                    if (!attempted) { attempted = true; popup = new CapturePopup(true); }
                    int width = Math.Min(area.Width - 24, 520);
                    int x = area.X + (area.Width - width) / 2, y = area.Y + 18;
                    if (!popup.Verified) { popup.Hide(); previous = null; }
                    else if (message != previous || x != lastX || y != lastY)
                    {
                        if (popup.Warning(x, y, width, message)) previous = message;
                        lastX = x; lastY = y;
                    }
                }
                CapturePopup.PumpMessages();
                Thread.Sleep(100);
            }
        }
        catch (Exception e)
        {
            SmokescreenPlugin.Logger.LogWarning("Privates Warnfenster: " + e.GetType().Name);
            while (!stopped && warning != null) Thread.Sleep(200);
        }
        finally { popup?.Dispose(); }
    }

    internal static void Stop()
    {
        stopped = true;
        warning = null;
        CaptureSelfTest.Stop();
    }
}
