// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using System;
using System.Runtime.InteropServices;

namespace UnknownsSmokescreen;

// Ein Fenster, ein Gerät und eine ausschließlich durch DirectComposition aktualisierte Fläche.
// Die Slots entsprechen dcomp.h, dxgi.h und d3d11.h im Windows SDK.
internal sealed unsafe class CompositionImage : IDisposable
{
    private IntPtr composition, target, visual, surface, context;
    private readonly int width, height;
    private int scaledWidth, scaledHeight;
    private static readonly Guid DeviceId = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    private static readonly Guid CompositionId = new("c37ea93a-e7aa-450d-b16f-9746cb0407f3");
    private static readonly Guid SurfaceId = new("cafcb56c-6ac3-4889-bf47-9e23bbd260ec");
    private static readonly Guid TextureId = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    [DllImport("dcomp.dll")] private static extern int DCompositionCreateDevice(IntPtr device, in Guid id, out IntPtr result);
    private static IntPtr Slot(IntPtr obj, int index) => (*(IntPtr**)obj)[index];
    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);
    private static void Release(ref IntPtr obj)
    {
        if (obj != IntPtr.Zero) Marshal.Release(obj);
        obj = IntPtr.Zero;
    }

    internal CompositionImage(IntPtr window, IntPtr texture, int width, int height)
    {
        this.width = width;
        this.height = height;
        IntPtr device = default, dxgi = default, result;
        try
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, void>)Slot(texture, 3))(texture, &device);
            var id = DeviceId;
            Check(Marshal.QueryInterface(device, ref id, out dxgi));
            Check(DCompositionCreateDevice(dxgi, CompositionId, out composition));
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, void>)Slot(device, 40))(device, &result);
            context = result;
            uint* desc = stackalloc uint[11];
            ((delegate* unmanaged[Stdcall]<IntPtr, uint*, void>)Slot(texture, 10))(texture, desc);
            uint format = desc[4] switch { 27 or 28 or 29 => 28u, 87 or 90 or 91 => 87u, _ => throw new NotSupportedException() };
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, IntPtr*, int>)Slot(composition, 6))(composition, window, 1, &result));
            target = result;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(composition, 7))(composition, &result));
            visual = result;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, uint, uint, IntPtr*, int>)Slot(composition, 8))
                (composition, (uint)width, (uint)height, format, 3, &result));
            surface = result;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Slot(visual, 15))(visual, surface));
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Slot(target, 3))(target, visual));
        }
        catch { Dispose(); throw; }
        finally { Release(ref dxgi); Release(ref device); }
    }

    internal void Draw(IntPtr texture, int clientWidth, int clientHeight)
    {
        if (clientWidth != scaledWidth || clientHeight != scaledHeight)
        {
            float* matrix = stackalloc float[6] { (float)clientWidth / width, 0, 0, -(float)clientHeight / height, 0, clientHeight };
            Check(((delegate* unmanaged[Stdcall]<IntPtr, float*, int>)Slot(visual, 8))(visual, matrix));
            scaledWidth = clientWidth;
            scaledHeight = clientHeight;
        }
        Guid iid = SurfaceId;
        IntPtr drawing = default, dest = default;
        Native.POINT offset;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, Native.POINT*, int>)Slot(surface, 3))
            (surface, IntPtr.Zero, &iid, &drawing, &offset));
        try
        {
            iid = TextureId;
            Check(Marshal.QueryInterface(drawing, ref iid, out dest));
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, uint, IntPtr, uint, IntPtr, void>)Slot(context, 46))
                (context, dest, 0, checked((uint)offset.x), checked((uint)offset.y), 0, texture, 0, IntPtr.Zero);
        }
        finally
        {
            Release(ref dest);
            Release(ref drawing);
            Check(((delegate* unmanaged[Stdcall]<IntPtr, int>)Slot(surface, 4))(surface));
        }
        Check(((delegate* unmanaged[Stdcall]<IntPtr, int>)Slot(composition, 3))(composition));
    }

    public void Dispose()
    {
        Release(ref target); Release(ref visual); Release(ref surface); Release(ref composition); Release(ref context);
    }
}
