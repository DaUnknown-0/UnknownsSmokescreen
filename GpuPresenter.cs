// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

namespace UnknownsSmokescreen;

// Unity ordnet die Plugin-Ereignisse hinter den Kamerabefehlen ein. Nur der Renderthread
// greift auf den Immediate Context und die DirectComposition-Objekte zu.
internal static unsafe class GpuPresenter
{
    private struct Frame
    {
        internal IntPtr Texture, Window;
        internal int Width, Height, Epoch;
        internal bool MenuProbe;
    }

    private static readonly Frame[] queue = new Frame[16];
    private static int next;
    private static IntPtr source, window;
    private static CompositionImage image;
    internal static volatile int Error;
    internal static long Presented, Missed;

    internal static void Submit(IntPtr texture, int w, int h, int epoch, bool menuProbe = false)
    {
        var hwnd = OverlayHost.PrivateWindow;
        if (hwnd == IntPtr.Zero || texture == IntPtr.Zero || Error != 0) return;
        int id = next++ & (queue.Length - 1);
        ref var frame = ref queue[id];
        if (Volatile.Read(ref frame.Texture) != IntPtr.Zero) { Interlocked.Increment(ref Missed); return; }
        frame.Window = hwnd;
        frame.Width = w;
        frame.Height = h;
        frame.Epoch = epoch;
        frame.MenuProbe = menuProbe;
        Marshal.AddRef(texture);
        Volatile.Write(ref frame.Texture, texture);
        try { Issue(id); }
        catch { Marshal.Release(Interlocked.Exchange(ref frame.Texture, IntPtr.Zero)); throw; }
    }

    internal static void Reset() => Issue(-1);
    private static void Issue(int id) =>
        GL.IssuePluginEvent((IntPtr)(delegate* unmanaged[Stdcall]<int, void>)&Render, id);

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static void Render(int id)
    {
        try
        {
            if (id < 0) { Close(); Error = 0; return; }
            var frame = queue[id];
            try
            {
                if (frame.Epoch != OverlayHost.Epoch || frame.Window != OverlayHost.PrivateWindow) return;
                if (!OverlayHost.IsCaptureExcluded(frame.Window) || Error != 0) return;
                if (source != frame.Texture || window != frame.Window)
                {
                    Close();
                    image = new CompositionImage(frame.Window, frame.Texture, frame.Width, frame.Height);
                    SmokescreenPlugin.Logger.LogInfo($"DComp HWND=0x{frame.Window:X} Quelle={frame.Width}x{frame.Height}");
                    Marshal.AddRef(frame.Texture);
                    source = frame.Texture;
                    window = frame.Window;
                }
                long size = OverlayHost.ClientSize;
                if ((int)(size >> 32) <= 0 || (int)size <= 0) return;
                image.Draw(frame.Texture, frame.MenuProbe ? frame.Width : (int)(size >> 32), frame.MenuProbe ? frame.Height : (int)size);
                Interlocked.Increment(ref Presented);
                OverlayHost.FrameReady(frame.Epoch);
            }
            finally { Marshal.Release(Interlocked.Exchange(ref queue[id].Texture, IntPtr.Zero)); }
        }
        catch (Exception e) { Error = e.HResult; Close(); }
    }

    private static void Close()
    {
        image?.Dispose();
        image = null;
        if (source != IntPtr.Zero) Marshal.Release(source);
        window = source = IntPtr.Zero;
    }
}
