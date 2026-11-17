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

// Nur verwaltete Rohbilder und monotone Millisekunden, keine Unity-Zugriffe.
internal sealed class OpaqueTiles
{
    internal const int TileSize = 32, HoldMilliseconds = 350;
    private int width, height, columns, rows;
    private bool[] different, previous;
    private long[] until;
    private long lastFrame, fullUntil;
    internal int CoveredTiles { get; private set; }
    internal int MarginTiles { get; private set; }
    internal bool FullCoverage { get; private set; }

    internal unsafe void Copy(byte* real, byte* masked, byte* overlay, byte* feed,
        int w, int h, long now, bool coverAll = false)
    {
        if (w != width || h != height)
        {
            width = w; height = h;
            columns = (w + TileSize - 1) / TileSize; rows = (h + TileSize - 1) / TileSize;
            different = new bool[checked(columns * rows)]; previous = new bool[different.Length];
            until = new long[different.Length]; lastFrame = fullUntil = 0;
        }
        Array.Clear(different, 0, different.Length);
        int stride = checked(w * 4);
        for (int y = 0; y < h; y++)
        {
            uint* r = (uint*)(real + (h - 1 - y) * stride), m = (uint*)(masked + (h - 1 - y) * stride);
            int row = (y / TileSize) * columns;
            for (int tx = 0; tx < columns; tx++)
            {
                if (different[row + tx]) continue;
                int end = Math.Min(w, (tx + 1) * TileSize);
                for (int x = tx * TileSize; x < end; x++)
                    if (((r[x] ^ m[x]) & 0x00FFFFFFu) != 0) { different[row + tx] = true; break; }
            }
        }

        // Mindestens 192 px Rand. Neue Diffpositionen erweitern den Rand entsprechend
        // ihrem Abstand zum vorigen Diff. Ein langer Ausgabestillstand deckt alles.
        int travel = 0;
        bool hadPrevious = Array.IndexOf(previous, true) >= 0;
        for (int ty = 0; ty < rows; ty++)
        for (int tx = 0; tx < columns; tx++)
        {
            int index = ty * columns + tx;
            if (!different[index] || previous[index] || !hadPrevious) continue;
            int nearest = 12;
            for (int dy = -12; dy <= 12; dy++)
            for (int dx = -12; dx <= 12; dx++)
            {
                int distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                if (distance >= nearest) continue;
                int x = tx + dx, y = ty + dy;
                if (x >= 0 && x < columns && y >= 0 && y < rows && previous[y * columns + x]) nearest = distance;
            }
            travel = Math.Max(travel, nearest);
        }
        long interval = lastFrame == 0 ? 67 : Math.Max(16, now - lastFrame);
        MarginTiles = Math.Clamp(1 + (int)Math.Ceiling(travel * Math.Max(1.0, (double)HoldMilliseconds / interval)), 6, 16);
        for (int ty = 0; ty < rows; ty++)
        for (int tx = 0; tx < columns; tx++)
        {
            if (!different[ty * columns + tx]) continue;
            for (int y = Math.Max(0, ty - MarginTiles); y <= Math.Min(rows - 1, ty + MarginTiles); y++)
            for (int x = Math.Max(0, tx - MarginTiles); x <= Math.Min(columns - 1, tx + MarginTiles); x++)
                until[y * columns + x] = now + HoldMilliseconds;
        }
        CoveredTiles = 0;
        for (int i = 0; i < until.Length; i++) if (until[i] > now) CoveredTiles++;
        if (coverAll || CoveredTiles * 100L >= until.Length * 55L || (lastFrame != 0 && interval > 250))
            fullUntil = now + HoldMilliseconds;
        FullCoverage = fullUntil > now;
        if (FullCoverage) CoveredTiles = until.Length;

        for (int y = 0; y < h; y++)
        {
            uint* r = (uint*)(real + (h - 1 - y) * stride), dst = (uint*)(overlay + y * stride);
            uint* f = feed == null ? null : (uint*)(feed + y * stride);
            int row = (y / TileSize) * columns;
            for (int tx = 0; tx < columns; tx++)
            {
                bool opaque = FullCoverage || until[row + tx] > now;
                int begin = tx * TileSize, end = Math.Min(w, begin + TileSize);
                if (!opaque) new Span<uint>(dst + begin, end - begin).Clear();
                if (!opaque && f == null) continue;
                for (int x = begin; x < end; x++)
                {
                    uint v = r[x];
                    uint bgra = ((v & 255u) << 16) | (v & 0xFF00u) | ((v >> 16) & 255u) | 0xFF000000u;
                    if (opaque) dst[x] = bgra;
                    if (f != null) f[x] = bgra;
                }
            }
        }
        var swap = previous; previous = different; different = swap; lastFrame = now;
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

