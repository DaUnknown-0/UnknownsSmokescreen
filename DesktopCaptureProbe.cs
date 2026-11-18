// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace UnknownsSmokescreen;

// Slots und Strukturen entsprechen dxgi1_2.h, d3d11.h und windows.graphics.capture.h.
// Die GPU-Kopie liest allein das kleine Prüfrechteck zurück.
internal sealed unsafe class DesktopCaptureProbe : IDisposable
{
    private const int Bgra8 = 87;
    private const int NotFound = unchecked((int)0x887a0002);
    private const int WaitTimeout = unchecked((int)0x887a0027);
    private static readonly Guid FactoryId = new("770aae78-f26f-4dba-a829-253c83d1b387");
    private static readonly Guid Output1Id = new("00cddea8-939b-4b83-a340-a685226666cc");
    private static readonly Guid DxgiDeviceId = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    private static readonly Guid TextureId = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    private static readonly Guid ItemInteropId = new("3628e81b-3cac-4c60-b7f4-23ce0e0c3356");
    private static readonly Guid ItemId = new("79c3f95b-31f7-4ec2-a464-632ef5d30760");
    private static readonly Guid FramePoolFactoryId = new("589b103f-6bbc-5df5-a991-02e28b3b66d5");
    private static readonly Guid SurfaceInteropId = new("a9b3d012-3df2-4ee3-b8d1-8695f457d3c1");
    private static readonly Guid ClosableId = new("30d5a829-7fa4-4026-83bb-d75bae4ea99e");

    private IntPtr device, context, output, staging;
    private readonly Native.RECT region;
    private readonly IntPtr monitor;
    private readonly int size;

    [StructLayout(LayoutKind.Sequential)]
    private struct OutputDescription
    {
        public fixed char Name[32];
        public Native.RECT Bounds;
        public int Attached, Rotation;
        public IntPtr Monitor;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct FrameInfo
    {
        public long PresentAt, MouseAt;
        public uint Count;
        public int Merged, ProtectedMasked, MouseX, MouseY, MouseVisible;
        public uint MetadataSize, PointerSize;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct TextureDescription
    {
        public int Width, Height, Mips, ArraySize, Format, Samples, Quality, Usage, Bind, Cpu, Misc;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Box
    {
        public int Left, Top, Front, Right, Bottom, Back;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Mapped
    {
        public IntPtr Data;
        public int Pitch, DepthPitch;
    }

    internal DesktopCaptureProbe(int x, int y, int size)
    {
        this.size = size;
        monitor = MonitorFromPoint(new Native.POINT { x = x, y = y }, 0);
        IntPtr factory = IntPtr.Zero;
        try
        {
            Check(CreateDXGIFactory1(FactoryId, out factory));
            for (uint i = 0; ; i++)
            {
                IntPtr adapter = IntPtr.Zero;
                int hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Slot(factory, 12))(factory, i, &adapter);
                if (hr == NotFound) break;
                Check(hr);
                try
                {
                    for (uint j = 0; ; j++)
                    {
                        IntPtr current = IntPtr.Zero;
                        hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Slot(adapter, 7))(adapter, j, &current);
                        if (hr == NotFound) break;
                        Check(hr);
                        try
                        {
                            OutputDescription desc;
                            Check(((delegate* unmanaged[Stdcall]<IntPtr, OutputDescription*, int>)Slot(current, 7))(current, &desc));
                            if (desc.Monitor != monitor || desc.Attached == 0) continue;
                            if (desc.Rotation != 1 || x + size > desc.Bounds.right || y + size > desc.Bounds.bottom)
                                throw new NotSupportedException("Gedrehter Monitor oder Prüfmuster über Monitorgrenze");
                            region = new Native.RECT
                            {
                                left = x - desc.Bounds.left, top = y - desc.Bounds.top,
                                right = x - desc.Bounds.left + size, bottom = y - desc.Bounds.top + size
                            };
                            output = Query(current, Output1Id);
                            Check(D3D11CreateDevice(adapter, 0, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out device, IntPtr.Zero, out context));
                            var description = new TextureDescription
                            {
                                Width = size, Height = size, Mips = 1, ArraySize = 1, Format = Bgra8,
                                Samples = 1, Usage = 3, Cpu = 0x20000
                            };
                            IntPtr texture = IntPtr.Zero;
                            Check(((delegate* unmanaged[Stdcall]<IntPtr, TextureDescription*, IntPtr, IntPtr*, int>)Slot(device, 5))
                                (device, &description, IntPtr.Zero, &texture));
                            staging = texture;
                            return;
                        }
                        finally { Release(current); }
                    }
                }
                finally { Release(adapter); }
            }
            throw new NotSupportedException("Monitor fehlt in DXGI");
        }
        catch { Dispose(); throw; }
        finally { Release(factory); }
    }

    internal uint[] Duplicate()
    {
        IntPtr duplication = IntPtr.Zero, resource = IntPtr.Zero;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Slot(output, 22))(output, device, &duplication));
        try
        {
            FrameInfo info;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, FrameInfo*, IntPtr*, int>)Slot(duplication, 8))
                (duplication, 400, &info, &resource);
            if (hr == WaitTimeout) return null;
            Check(hr);
            try { return ReadTexture(resource); }
            finally { ((delegate* unmanaged[Stdcall]<IntPtr, int>)Slot(duplication, 14))(duplication); }
        }
        finally { Release(resource); Release(duplication); }
    }

    internal uint[] WindowsGraphics()
    {
        Check(RoInitialize(1));
        IntPtr interop = IntPtr.Zero, item = IntPtr.Zero, factory = IntPtr.Zero, graphicsDevice = IntPtr.Zero;
        IntPtr pool = IntPtr.Zero, session = IntPtr.Zero;
        try
        {
            interop = Factory("Windows.Graphics.Capture.GraphicsCaptureItem", ItemInteropId);
            var itemId = ItemId;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)Slot(interop, 4))(interop, monitor, &itemId, &item));
            Native.SIZE dimensions;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, Native.SIZE*, int>)Slot(item, 7))(item, &dimensions));
            if (region.right > dimensions.cx || region.bottom > dimensions.cy) return null;
            var dxgiDevice = Query(device, DxgiDeviceId);
            try { Check(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out graphicsDevice)); }
            finally { Release(dxgiDevice); }
            factory = Factory("Windows.Graphics.Capture.Direct3D11CaptureFramePool", FramePoolFactoryId);
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, int, Native.SIZE, IntPtr*, int>)Slot(factory, 6))
                (factory, graphicsDevice, Bgra8, 2, dimensions, &pool));
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Slot(pool, 10))(pool, item, &session));
            Check(((delegate* unmanaged[Stdcall]<IntPtr, int>)Slot(session, 6))(session));
            long deadline = Environment.TickCount64 + 800;
            do
            {
                IntPtr frame = IntPtr.Zero;
                Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(pool, 7))(pool, &frame));
                if (frame != IntPtr.Zero)
                {
                    IntPtr surface = IntPtr.Zero, access = IntPtr.Zero, texture = IntPtr.Zero;
                    try
                    {
                        Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, int>)Slot(frame, 6))(frame, &surface));
                        access = Query(surface, SurfaceInteropId);
                        var textureId = TextureId;
                        Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)Slot(access, 3))(access, &textureId, &texture));
                        return CopyRegion(texture);
                    }
                    finally { Release(texture); Release(access); Release(surface); Close(frame); }
                }
                CapturePopup.PumpMessages();
                Thread.Sleep(15);
            } while (Environment.TickCount64 < deadline);
            return null;
        }
        finally
        {
            Close(session);
            Close(pool);
            Release(graphicsDevice);
            Release(factory);
            Release(item);
            Release(interop);
            RoUninitialize();
        }
    }

    private uint[] ReadTexture(IntPtr resource)
    {
        var texture = Query(resource, TextureId);
        try { return CopyRegion(texture); }
        finally { Release(texture); }
    }

    private uint[] CopyRegion(IntPtr texture)
    {
        TextureDescription desc;
        ((delegate* unmanaged[Stdcall]<IntPtr, TextureDescription*, void>)Slot(texture, 10))(texture, &desc);
        if (desc.Format != Bgra8 || region.right > desc.Width || region.bottom > desc.Height) return null;
        var box = new Box { Left = region.left, Top = region.top, Right = region.right, Bottom = region.bottom, Back = 1 };
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, uint, IntPtr, uint, Box*, void>)Slot(context, 46))
            (context, staging, 0, 0, 0, 0, texture, 0, &box);
        Mapped map;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, Mapped*, int>)Slot(context, 14))
            (context, staging, 0, 1, 0, &map));
        try
        {
            var pixels = new uint[size * size];
            for (int y = 0; y < size; y++)
                new ReadOnlySpan<uint>((void*)(map.Data + y * map.Pitch), size).CopyTo(pixels.AsSpan(y * size, size));
            return pixels;
        }
        finally { ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, void>)Slot(context, 15))(context, staging, 0); }
    }

    public void Dispose()
    {
        Release(staging);
        Release(context);
        Release(device);
        Release(output);
        staging = context = device = output = IntPtr.Zero;
    }

    private static IntPtr Factory(string name, Guid iid)
    {
        Check(WindowsCreateString(name, name.Length, out var hstring));
        try { Check(RoGetActivationFactory(hstring, iid, out var factory)); return factory; }
        finally { WindowsDeleteString(hstring); }
    }

    private static void Close(IntPtr value)
    {
        if (value == IntPtr.Zero) return;
        var iid = ClosableId;
        if (Marshal.QueryInterface(value, ref iid, out var closable) >= 0)
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, int>)Slot(closable, 6))(closable);
            Release(closable);
        }
        Release(value);
    }

    private static IntPtr Slot(IntPtr obj, int index) => (*(IntPtr**)obj)[index];
    private static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
    private static void Release(IntPtr obj) { if (obj != IntPtr.Zero) Marshal.Release(obj); }
    private static IntPtr Query(IntPtr obj, Guid iid)
    {
        Check(Marshal.QueryInterface(obj, ref iid, out var value));
        return value;
    }

    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(in Guid iid, out IntPtr factory);
    [DllImport("d3d11.dll")] private static extern int D3D11CreateDevice(IntPtr adapter, int driver, IntPtr software, uint flags,
        IntPtr levels, uint count, uint sdk, out IntPtr device, IntPtr level, out IntPtr context);
    [DllImport("d3d11.dll")] private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr device, out IntPtr graphicsDevice);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Native.POINT point, uint flags);
    [DllImport("combase.dll")] private static extern int RoInitialize(uint mode);
    [DllImport("combase.dll")] private static extern void RoUninitialize();
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string text, int length, out IntPtr hstring);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(IntPtr hstring);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(IntPtr hstring, in Guid iid, out IntPtr factory);
}
