// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

namespace UnknownsSmokescreen;

// Ein optionaler Abnehmer hält höchstens eine ausstehende Kopie. Polling erfolgt
// auf Unitys Renderthread mit D3D11_MAP_FLAG_DO_NOT_WAIT.
internal sealed unsafe class FrameReadback
{
    internal static readonly FrameReadback Feed = new(0), Replay = new(1), PrivateProbe = new(2), PublicProbe = new(3);
    private static readonly FrameReadback[] Readers = { Feed, Replay, PrivateProbe, PublicProbe };
    private string filename;
    internal bool Available => Volatile.Read(ref state) == Idle;
    private readonly int id;
    private FrameReadback(int id) => this.id = id;
    private IntPtr source, staging, context;
    private int state, width, height, generation, requestedGeneration, format, error;
    private byte[] pixels;
    private const int Idle = 0, Working = 1, Pending = 2, Ready = 3, Failed = 4;
    private struct Mapped { public IntPtr Data; public uint Pitch, DepthPitch; }

    internal void Invalidate() => generation++;
    internal void Request(IntPtr texture, string path = null)
    {
        if (texture == IntPtr.Zero || Volatile.Read(ref state) != Idle) return;
        filename = path;
        requestedGeneration = generation;
        Marshal.AddRef(texture);
        source = texture;
        Schedule();
    }

    internal void Tick(bool enabled)
    {
        int status = Volatile.Read(ref state);
        if (status == Pending) { Schedule(); return; }
        if (status == Failed)
            SmokescreenPlugin.Logger.LogWarning($"GPU-Lesekopie fehlgeschlagen (0x{error:X8})");
        if (status != Ready && status != Failed) return;
        try
        {
            if (status != Ready || !enabled || generation != requestedGeneration) return;
            if (id == 0) pixels = OverlayHost.PublishFeed(pixels, width, height);
            else if (id == 1)
            {
                fixed (byte* data = pixels) DelayedRecording.Push((IntPtr)data, width, height);
            }
            else BitmapFile.Write(filename, pixels, width, height);
        }
        finally { Volatile.Write(ref state, Idle); }
    }

    internal static void Close() =>
        GL.IssuePluginEvent((IntPtr)(delegate* unmanaged[Stdcall]<int, void>)&Render, -1);

    private void Schedule()
    {
        Volatile.Write(ref state, Working);
        try { GL.IssuePluginEvent((IntPtr)(delegate* unmanaged[Stdcall]<int, void>)&Render, id); }
        catch { Release(); Volatile.Write(ref state, Idle); throw; }
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static void Render(int id)
    {
        if (id < 0)
        {
            foreach (var item in Readers) { item.Release(); Volatile.Write(ref item.state, Idle); }
            return;
        }
        var reader = Readers[id];
        int result;
        try
        {
            if (reader.staging == IntPtr.Zero) { reader.BeginCopy(); result = Pending; }
            else result = reader.TryRead() ? Ready : Pending;
        }
        catch (Exception e) { reader.error = e.HResult; result = Failed; }
        if (result != Pending) reader.Release();
        Volatile.Write(ref reader.state, result);
    }

    private static IntPtr Slot(IntPtr p, int i) => (*(IntPtr**)p)[i];
    private void BeginCopy()
    {
        IntPtr device, immediate, texture;
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, void>)Slot(source, 3))(source, &device);
        try
        {
            uint* desc = stackalloc uint[11];
            ((delegate* unmanaged[Stdcall]<IntPtr, uint*, void>)Slot(source, 10))(source, desc);
            width = checked((int)desc[0]);
            height = checked((int)desc[1]);
            format = (int)desc[4];
            if (format is not (27 or 28 or 29 or 87 or 90 or 91)) throw new NotSupportedException();
            desc[7] = 3; desc[8] = 0; desc[9] = 0x20000; desc[10] = 0;
            Marshal.ThrowExceptionForHR(((delegate* unmanaged[Stdcall]<IntPtr, uint*, IntPtr, IntPtr*, int>)Slot(device, 5))(device, desc, IntPtr.Zero, &texture));
            staging = texture;
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, void>)Slot(device, 40))(device, &immediate);
            context = immediate;
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, void>)Slot(context, 47))(context, staging, source);
        }
        finally { Marshal.Release(device); }
    }

    private bool TryRead()
    {
        Mapped map;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, Mapped*, int>)Slot(context, 14))
            (context, staging, 0, 1, 0x100000, &map);
        if (hr == unchecked((int)0x887A000A)) return false;
        Marshal.ThrowExceptionForHR(hr);
        try
        {
            int stride = checked(width * 4), bytes = checked(stride * height);
            if (pixels?.Length != bytes) pixels = new byte[bytes];
            for (int y = 0; y < height; y++)
            {
                var row = new ReadOnlySpan<byte>((byte*)map.Data + y * map.Pitch, stride);
                int targetRow = id is 0 or 2 ? height - 1 - y : y;
                var dest = pixels.AsSpan(targetRow * stride, stride);
                row.CopyTo(dest);
                if ((format is 87 or 90 or 91) != (id != 1))
                    for (int i = 0; i < stride; i += 4) (dest[i], dest[i + 2]) = (dest[i + 2], dest[i]);
            }
            return true;
        }
        finally { ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, void>)Slot(context, 15))(context, staging, 0); }
    }

    private void Release()
    {
        if (source != IntPtr.Zero) Marshal.Release(source);
        if (staging != IntPtr.Zero) Marshal.Release(staging);
        if (context != IntPtr.Zero) Marshal.Release(context);
        staging = context = source = IntPtr.Zero;
    }
}
