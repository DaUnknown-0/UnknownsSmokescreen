// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using System.IO;
namespace UnknownsSmokescreen;

internal static class BitmapFile
{
    internal static void Write(string path,byte[] bgra,int width,int height)
    {
        using var f=new BinaryWriter(File.Create(path));
        f.Write((ushort)0x4D42); f.Write(54+bgra.Length); f.Write(0); f.Write(54);
        f.Write(40); f.Write(width); f.Write(-height); f.Write((ushort)1); f.Write((ushort)32);
        f.Write(0); f.Write(bgra.Length); f.Write(0); f.Write(0); f.Write(0); f.Write(0); f.Write(bgra);
    }
}
