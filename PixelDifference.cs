using System;
using System.IO;
namespace UnknownsSmokescreen;

// RGBA von Unity (unten beginnend) zu BGRA für DIB (oben beginnend).
internal static class PixelDifference
{
    internal static unsafe void Copy(byte* real,byte* masked,byte* overlay,byte* feed,int width,int height)
    {
        int stride=checked(width*4);
        for(int y=0;y<height;y++)
        {
            byte* r=real+(height-1-y)*stride; byte* m=masked+(height-1-y)*stride;
            byte* dst=overlay+y*stride; byte* f=feed==null?null:feed+y*stride;
            for(int x=0;x<stride;x+=4)
            {
                bool different=r[x]!=m[x] || r[x+1]!=m[x+1] || r[x+2]!=m[x+2];
                dst[x]=different?r[x+2]:(byte)0; dst[x+1]=different?r[x+1]:(byte)0; dst[x+2]=different?r[x]:(byte)0; dst[x+3]=different?(byte)255:(byte)0;
                if(f!=null) { f[x]=r[x+2]; f[x+1]=r[x+1]; f[x+2]=r[x]; f[x+3]=255; }
            }
        }
    }
}
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

