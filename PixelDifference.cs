using System;
using System.IO;
namespace UnknownsSmokescreen;

// RGBA von Unity (unten beginnend) zu BGRA für DIB (oben beginnend). Diagnose und RecordingFeed.
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

// Eine aufgenommene Insel. Rechteck in Unity-Bildschirmpixeln (Y von unten).
internal struct IslandDesc
{
    public int X, Y, W, H;
    public int AtlasX, AtlasY;      // Ursprung im Atlas, Zeilen von unten
    public int Column;              // Abstand der vier Durchgaenge im Atlas in Pixeln (0 = W)
    public long Key;                // Anker, 0 = bildschirmfest
    public float AnchorX, AnchorY;  // Ankerlage bei der Aufnahme, Pixel von oben links
    public int Radius;              // Plattenradius in Pixeln
    public bool Opaque;             // Vollbild: echtes Bild deckend, nur ein Slot
    public bool Hud;                // aus den HUD-Durchgaengen; Pixelbesitz nur innerhalb derselben Quelle
    public bool Plate;              // verankerter Text: jede Aenderung bekommt ein Band (Schutz gegen Restversatz)
}

// Fertige Insel: vormultipliziertes BGRA, Zeilen von oben. Keine Unity-Objekte.
internal sealed class Island
{
    public uint[] Pixels = Array.Empty<uint>();
    public int X, Y, W, H;          // Lage bei der Aufnahme, oben links
    public long Key;
    public float AnchorX, AnchorY;
    public int PlatePixels, AlphaPixels, Seeds;
    public bool Empty;
    public int DrawX, DrawY;
    public bool Drawn;
}

// Zerlegt vier Durchgaenge (echt/maskiert ueber Schwarz/Weiss) in die Schicht, die ueber dem
// live maskierten Spielbild das echte Bild ergibt. Jede Mischung ist affin im Hintergrund:
//   Bild(c) = Bild(0) + k * c.  Mit kE = echtWeiss-echtSchwarz, kM = maskiertWeiss-maskiertSchwarz:
//   1 - a = kE / kM,  Farbe = echtSchwarz - (1 - a) * maskiertSchwarz.
// Ist das nicht darstellbar (Fake deckender oder Fake-Farbe muesste abgezogen werden), entsteht
// ein Plattenkeim: dort liegt eine schwarze, deckende Platte mit dem echten Inhalt darauf.
internal static class IslandBuilder
{
    internal const int Feather = 2, Tolerance = 6;

    internal static unsafe void Build(byte* atlas, int stride, in IslandDesc d, int screenHeight, Island into, ref int[] distance, byte[] claimed, int screenWidth)
    {
        int w = d.W, h = d.H, n = checked(w * h), col = d.Column > 0 ? d.Column : d.W;
        if (into.Pixels.Length < n) into.Pixels = new uint[n];
        into.X = d.X; into.Y = screenHeight - d.Y - h; into.W = w; into.H = h;
        into.Key = d.Key; into.AnchorX = d.AnchorX; into.AnchorY = d.AnchorY;
        into.PlatePixels = into.AlphaPixels = into.Seeds = 0; into.Drawn = false;
        var output = into.Pixels;
        if (d.Opaque)
        {
            for (int y = 0; y < h; y++)
            {
                uint* s = (uint*)(atlas + (d.AtlasY + y) * stride) + d.AtlasX;
                int o = (h - 1 - y) * w;
                for (int x = 0; x < w; x++) output[o + x] = Bgra(s[x]) | 0xFF000000u;
            }
            into.AlphaPixels = n; into.Empty = false;
            return;
        }
        if (distance == null || distance.Length < 2 * n) distance = new int[Math.Max(2 * n, 8192)];
        // Art je Pixel: 0 gleich, 1 darstellbarer Unterschied, 2 Plattenkeim.
        int seeds = 0;
        for (int y = 0; y < h; y++)
        {
            uint* s = (uint*)(atlas + (d.AtlasY + y) * stride) + d.AtlasX;
            int o = (h - 1 - y) * w;
            for (int x = 0; x < w; x++)
            {
                uint rb = s[x], rw = s[col + x], mb = s[2 * col + x], mw = s[3 * col + x];
                if (((rb ^ mb) & 0xFFFFFFu) == 0 && ((rw ^ mw) & 0xFFFFFFu) == 0) { output[o + x] = 0; distance[o + x] = 0; continue; }
                if (Solve(rb, rw, mb, mw, out uint pixel) && !d.Plate) { output[o + x] = pixel; distance[o + x] = 1; }
                else if (d.Plate) { output[o + x] = pixel; distance[o + x] = 2; seeds++; }
                else { output[o + x] = 0; distance[o + x] = 2; seeds++; }
            }
        }
        into.Seeds = seeds;
        int plates = seeds > 0 ? Plates(distance, w, h, d.Radius) : 0;
        if (plates > 0) Rasterize(distance, w, h, plates, d.Radius);
        for (int y = 0; y < h; y++)
        {
            uint* s = (uint*)(atlas + (d.AtlasY + y) * stride) + d.AtlasX;
            int o = (h - 1 - y) * w;
            int sy = into.Y + (h - 1 - y);
            for (int x = 0; x < w; x++)
            {
                int i = o + x;
                uint pixel = output[i];
                if (plates > 0)
                {
                    int f = distance[n + i];
                    if (f > 0)
                    {
                        // Platte: echter Durchgang ueber Schwarz als abgerundetes Band, Rand 2 px weich.
                        pixel = Mix(pixel, Bgra(s[x]), f);
                        into.PlatePixels++;
                    }
                }
                if (pixel == 0) continue;
                // Ueberlappende Inseln verschiedener Anker: kleinste Insel besitzt das Pixel.
                int sx = into.X + x;
                if (claimed != null && sx >= 0 && sx < screenWidth && sy >= 0 && (uint)(sy * screenWidth + sx) < (uint)claimed.Length)
                {
                    int c = sy * screenWidth + sx;
                    byte mark = d.Hud ? (byte)2 : (byte)1;
                    if ((claimed[c] & mark) != 0) { output[i] = 0; continue; }
                    claimed[c] |= mark;
                }
                output[i] = pixel;
                if (pixel >> 24 != 0) into.AlphaPixels++;
            }
        }
        into.Empty = into.AlphaPixels == 0;
    }

    // RGBA (Unity, little endian R im untersten Byte) zu BGRA fuer die DIB.
    internal static uint Bgra(uint v) => ((v & 255u) << 16) | (v & 0xFF00u) | ((v >> 16) & 255u);

    // (1-f) * Overlay + f * Platte, Alpha 1 - (1-f)(1-a).
    private static uint Mix(uint overlay, uint plate, int f)
    {
        if (f >= 255) return plate | 0xFF000000u;
        int g = 255 - f;
        int a = 255 - (g * (255 - (int)(overlay >> 24)) + 127) / 255;
        int b = ((int)(overlay & 255) * g + (int)(plate & 255) * f + 127) / 255;
        int gr = ((int)((overlay >> 8) & 255) * g + (int)((plate >> 8) & 255) * f + 127) / 255;
        int r = ((int)((overlay >> 16) & 255) * g + (int)((plate >> 16) & 255) * f + 127) / 255;
        if (b > a) b = a; if (gr > a) gr = a; if (r > a) r = a;
        return (uint)(b | gr << 8 | r << 16 | a << 24);
    }

    // Ein Pixel: true mit vormultipliziertem BGRA, false = Plattenkeim.
    internal static bool Solve(uint rb, uint rw, uint mb, uint mw, out uint pixel)
    {
        pixel = 0;
        int rbR = (int)(rb & 255), rbG = (int)((rb >> 8) & 255), rbB = (int)((rb >> 16) & 255);
        int rwR = (int)(rw & 255), rwG = (int)((rw >> 8) & 255), rwB = (int)((rw >> 16) & 255);
        int mbR = (int)(mb & 255), mbG = (int)((mb >> 8) & 255), mbB = (int)((mb >> 16) & 255);
        int mwR = (int)(mw & 255), mwG = (int)((mw >> 8) & 255), mwB = (int)((mw >> 16) & 255);
        int kE = (rwR - rbR) + (rwG - rbG) + (rwB - rbB);
        int kM = (mwR - mbR) + (mwG - mbG) + (mwB - mbB);
        if (kE < 0) kE = 0;
        if (kM <= 3 * 2)
        {
            // Maskiert deckend: nur loesbar, wenn das Echte ebenfalls deckt.
            if (kE > 3 * 2) return false;
            pixel = (uint)(rbB | rbG << 8 | rbR << 16) | 0xFF000000u;
            return true;
        }
        if (kE > kM + 3 * Tolerance) return false;
        if (kE > kM) kE = kM;
        // t = 1 - a in 1/65536
        long t = ((long)kE << 16) / kM;
        int a = 255 - (int)((t * 255 + 32768) >> 16);
        int slack = Tolerance + (int)((255L * 3 * 2 + kM / 2) / kM);
        if (!Channel(rbR, rwR, mbR, mwR, t, a, slack, out int r)) return false;
        if (!Channel(rbG, rwG, mbG, mwG, t, a, slack, out int g)) return false;
        if (!Channel(rbB, rwB, mbB, mwB, t, a, slack, out int b)) return false;
        if (a <= 2 && r <= 2 && g <= 2 && b <= 2) { pixel = 0; return true; }
        pixel = (uint)(b | g << 8 | r << 16 | a << 24);
        return true;
    }

    private static bool Channel(int rb, int rw, int mb, int mw, long t, int a, int slack, out int value)
    {
        int predicted = (int)((t * (mw - mb) + 32768) >> 16);
        value = rb - (int)((t * mb + 32768) >> 16);
        // Jeder Kanal muss dieselbe Transparenz zeigen, die Farbe darf weder negativ noch heller als Alpha sein.
        if (Math.Abs((rw - rb) - predicted) > slack) return false;
        if (value < -slack || value > a + slack) return false;
        if (value < 0) value = 0; else if (value > a) value = a;
        return true;
    }

    // Platten als abgerundete Baender: alle geaenderten Pixel einer Zeile (horizontal bis 4R, vertikal bis R
    // Abstand verbunden) bilden eine Gruppe; Gruppen mit Keim erhalten ein Band um ihr Rechteck plus R.
    [ThreadStatic] private static int[] rects;
    [ThreadStatic] private static int[] stack;
    private static int Plates(int[] kind, int w, int h, int radius)
    {
        int n = w * h, gapX = Math.Max(12, 4 * radius), gapY = Math.Max(3, radius);
        // Schliessen: Luecken zwischen geaenderten Pixeln markieren (Wert 3), Originale bleiben 1/2.
        for (int y = 0; y < h; y++)
        {
            int last = -1, row = y * w;
            for (int x = 0; x < w; x++)
            {
                if (kind[row + x] == 0 || kind[row + x] == 3) continue;
                if (last >= 0 && x - last - 1 <= gapX) for (int k = last + 1; k < x; k++) if (kind[row + k] == 0) kind[row + k] = 3;
                last = x;
            }
        }
        for (int x = 0; x < w; x++)
        {
            int last = -1;
            for (int y = 0; y < h; y++)
            {
                if (kind[y * w + x] == 0) continue;
                if (last >= 0 && y - last - 1 <= gapY) for (int k = last + 1; k < y; k++) if (kind[k * w + x] == 0) kind[k * w + x] = 3;
                last = y;
            }
        }
        // Zusammenhangskomponenten; Markierung im zweiten Teil des Puffers.
        rects ??= new int[256]; stack ??= new int[4096];
        Array.Clear(kind, n, n);
        int count = 0;
        for (int start = 0; start < n; start++)
        {
            if (kind[start] == 0 || kind[n + start] != 0) continue;
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1, top = 0; bool seed = false;
            kind[n + start] = 1; stack[top++] = start;
            while (top > 0)
            {
                int i = stack[--top], x = i % w, y = i / w, v = kind[i];
                if (v == 1 || v == 2)
                {
                    if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y;
                    if (v == 2) seed = true;
                }
                if (top + 4 > stack.Length) Array.Resize(ref stack, stack.Length * 2);
                if (x > 0 && kind[i - 1] != 0 && kind[n + i - 1] == 0) { kind[n + i - 1] = 1; stack[top++] = i - 1; }
                if (x < w - 1 && kind[i + 1] != 0 && kind[n + i + 1] == 0) { kind[n + i + 1] = 1; stack[top++] = i + 1; }
                if (y > 0 && kind[i - w] != 0 && kind[n + i - w] == 0) { kind[n + i - w] = 1; stack[top++] = i - w; }
                if (y < h - 1 && kind[i + w] != 0 && kind[n + i + w] == 0) { kind[n + i + w] = 1; stack[top++] = i + w; }
            }
            if (!seed || x1 < 0) continue;
            if (4 * count + 4 > rects.Length) Array.Resize(ref rects, rects.Length * 2);
            rects[4 * count] = x0 - radius; rects[4 * count + 1] = y0 - radius;
            rects[4 * count + 2] = x1 + 1 + radius; rects[4 * count + 3] = y1 + 1 + radius;
            count++;
        }
        return count;
    }

    // Deckung 0..255 der abgerundeten Baender in den zweiten Pufferteil (Zeilen von oben), Maximum je Pixel.
    private static void Rasterize(int[] buffer, int w, int h, int count, int radius)
    {
        int n = w * h;
        Array.Clear(buffer, n, n);
        float corner = radius + 4;
        for (int k = 0; k < count; k++)
        {
            float x0 = rects[4 * k], y0 = rects[4 * k + 1], x1 = rects[4 * k + 2], y1 = rects[4 * k + 3];
            float c = Math.Min(corner, Math.Min((x1 - x0) / 2, (y1 - y0) / 2));
            int ax = Math.Max(0, (int)x0 - Feather), ay = Math.Max(0, (int)y0 - Feather);
            int bx = Math.Min(w, (int)x1 + Feather + 1), by = Math.Min(h, (int)y1 + Feather + 1);
            for (int y = ay; y < by; y++)
            {
                float py = y + 0.5f, dy = Math.Max(Math.Max(y0 + c - py, py - (y1 - c)), 0);
                for (int x = ax; x < bx; x++)
                {
                    float px = x + 0.5f, dx = Math.Max(Math.Max(x0 + c - px, px - (x1 - c)), 0);
                    float sd = (dx == 0 && dy == 0) ? Math.Max(Math.Max(x0 - px, px - x1), Math.Max(y0 - py, py - y1)) : MathF.Sqrt(dx * dx + dy * dy) - c;
                    int v = sd <= 0 ? 255 : sd >= Feather ? 0 : (int)((1 - sd / Feather) * 255);
                    int i = n + y * w + x;
                    if (v > buffer[i]) buffer[i] = v;
                }
            }
        }
    }
}

// Ankerlagen eines Spielframes, Pixel von oben links.
internal sealed class AnchorPositions
{
    internal readonly long[] Keys = new long[64];
    internal readonly float[] X = new float[64], Y = new float[64];
    internal int Count;
    internal float Ortho;
    internal int Frame, Delay;
    internal string Probe;
    internal bool Find(long key, out float x, out float y)
    {
        for (int i = 0; i < Count; i++) if (Keys[i] == key) { x = X[i]; y = Y[i]; return true; }
        x = y = 0; return false;
    }
    internal void CopyFrom(AnchorPositions o)
    {
        Count = o.Count; Ortho = o.Ortho; Frame = o.Frame; Probe = o.Probe; Delay = o.Delay;
        Array.Copy(o.Keys, Keys, Count); Array.Copy(o.X, X, Count); Array.Copy(o.Y, Y, Count);
    }
}

// Setzt die Inseln an ihre aktuelle Ankerlage in die Fensterflaeche und meldet das geaenderte Rechteck.
internal sealed unsafe class Composer
{
    private uint* bits;
    private int width, height;
    private Island[] islands = new Island[0];
    private int count;
    private float captureOrtho;
    internal int Width => width;
    internal int Height => height;
    internal int Count => count;

    internal void Attach(uint* target, int w, int h)
    {
        bits = target; width = w; height = h;
        for (int i = 0; i < count; i++) islands[i].Drawn = false;
        new Span<uint>(bits, checked(w * h)).Clear();
    }

    // Neue Inselmenge. Das alte Bild wird in den betroffenen Rechtecken ersetzt.
    internal bool Replace(Island[] next, int n, float ortho, AnchorPositions positions, out int x0, out int y0, out int x1, out int y1)
    {
        x0 = y0 = int.MaxValue; x1 = y1 = int.MinValue; dirtyCount = 0;
        for (int i = 0; i < count; i++) if (islands[i].Drawn) Grow(islands[i].DrawX, islands[i].DrawY, islands[i].W, islands[i].H, ref x0, ref y0, ref x1, ref y1);
        if (islands.Length < n) islands = new Island[Math.Max(n, 16)];
        Array.Copy(next, islands, n); count = n; captureOrtho = ortho;
        for (int i = 0; i < count; i++) islands[i].Drawn = false;
        Place(positions, ref x0, ref y0, ref x1, ref y1, true);
        return Paint(ref x0, ref y0, ref x1, ref y1);
    }

    // Nur neue Ankerlagen.
    internal bool Move(AnchorPositions positions, out int x0, out int y0, out int x1, out int y1)
    {
        x0 = y0 = int.MaxValue; x1 = y1 = int.MinValue; dirtyCount = 0;
        Place(positions, ref x0, ref y0, ref x1, ref y1, false);
        return Paint(ref x0, ref y0, ref x1, ref y1);
    }

    internal void Clear()
    {
        count = 0;
        if (bits != null) new Span<uint>(bits, width * height).Clear();
    }

    private void Place(AnchorPositions positions, ref int x0, ref int y0, ref int x1, ref int y1, bool all)
    {
        // Zoomwechsel (TOR-Zoom fuer Geister): weltverankerte Inseln bis zur naechsten Aufnahme ausblenden.
        bool scaled = positions != null && captureOrtho > 0 && Math.Abs(positions.Ortho - captureOrtho) > captureOrtho * 0.01f;
        for (int i = 0; i < count; i++)
        {
            var island = islands[i];
            bool visible = !island.Empty;
            int dx = 0, dy = 0;
            if (visible && island.Key != 0)
            {
                if (positions == null || scaled || !positions.Find(island.Key, out float ax, out float ay)) visible = false;
                else { dx = (int)MathF.Round(ax - island.AnchorX); dy = (int)MathF.Round(ay - island.AnchorY); }
            }
            int nx = island.X + dx, ny = island.Y + dy;
            bool changed = all || visible != island.Drawn || (visible && (nx != island.DrawX || ny != island.DrawY));
            if (!changed) continue;
            if (island.Drawn) Grow(island.DrawX, island.DrawY, island.W, island.H, ref x0, ref y0, ref x1, ref y1);
            if (visible) Grow(nx, ny, island.W, island.H, ref x0, ref y0, ref x1, ref y1);
            island.Drawn = visible; island.DrawX = nx; island.DrawY = ny;
        }
    }

    // Geaenderte Rechtecke einzeln; das Gesamtrechteck geht nur an UpdateLayeredWindowIndirect.
    private int[] dirty = new int[64];
    private int dirtyCount;
    private void Grow(int x, int y, int w, int h, ref int x0, ref int y0, ref int x1, ref int y1)
    {
        if (x < x0) x0 = x; if (y < y0) y0 = y;
        if (x + w > x1) x1 = x + w; if (y + h > y1) y1 = y + h;
        for (int k = 0; k < dirtyCount; k++)
            if (dirty[4 * k] == x && dirty[4 * k + 1] == y && dirty[4 * k + 2] == x + w && dirty[4 * k + 3] == y + h) return;
        if (4 * dirtyCount + 4 > dirty.Length) Array.Resize(ref dirty, dirty.Length * 2);
        dirty[4 * dirtyCount] = x; dirty[4 * dirtyCount + 1] = y; dirty[4 * dirtyCount + 2] = x + w; dirty[4 * dirtyCount + 3] = y + h;
        dirtyCount++;
    }

    private bool Paint(ref int x0, ref int y0, ref int x1, ref int y1)
    {
        int rects = dirtyCount; dirtyCount = 0;
        if (bits == null) return false;
        if (x0 < 0) x0 = 0; if (y0 < 0) y0 = 0;
        if (x1 > width) x1 = width; if (y1 > height) y1 = height;
        if (x0 >= x1 || y0 >= y1) return false;
        for (int r = 0; r < rects; r++)
        {
            int rx0 = Math.Max(0, dirty[4 * r]), ry0 = Math.Max(0, dirty[4 * r + 1]);
            int rx1 = Math.Min(width, dirty[4 * r + 2]), ry1 = Math.Min(height, dirty[4 * r + 3]);
            if (rx0 >= rx1 || ry0 >= ry1) continue;
            for (int y = ry0; y < ry1; y++) new Span<uint>(bits + y * width + rx0, rx1 - rx0).Clear();
        }
        for (int r = 0; r < rects; r++)
            PaintRect(Math.Max(0, dirty[4 * r]), Math.Max(0, dirty[4 * r + 1]), Math.Min(width, dirty[4 * r + 2]), Math.Min(height, dirty[4 * r + 3]), r, rects);
        return true;
    }

    // Zeichnet alle Inseln im Rechteck r; Pixel, die ein frueheres Rechteck schon gezeichnet hat, werden uebersprungen.
    private void PaintRect(int x0, int y0, int x1, int y1, int r, int rects)
    {
        if (x0 >= x1 || y0 >= y1) return;
        for (int i = 0; i < count; i++)
        {
            var island = islands[i];
            if (!island.Drawn) continue;
            int ax = Math.Max(x0, island.DrawX), ay = Math.Max(y0, island.DrawY);
            int bx = Math.Min(x1, island.DrawX + island.W), by = Math.Min(y1, island.DrawY + island.H);
            if (ax >= bx || ay >= by) continue;
            var pixels = island.Pixels;
            for (int y = ay; y < by; y++)
            {
                uint* dst = bits + y * width;
                int row = (y - island.DrawY) * island.W - island.DrawX;
                for (int x = ax; x < bx; x++)
                {
                    if (Covered(x, y, r)) continue;
                    uint s = pixels[row + x];
                    if (s == 0) continue;
                    uint a = s >> 24;
                    if (a == 255 || dst[x] == 0) { dst[x] = s; continue; }
                    dst[x] = Over(s, dst[x]);
                }
            }
        }
    }

    private bool Covered(int x, int y, int r)
    {
        for (int k = 0; k < r; k++)
            if (x >= dirty[4 * k] && x < dirty[4 * k + 2] && y >= dirty[4 * k + 1] && y < dirty[4 * k + 3]) return true;
        return false;
    }

    internal static uint Over(uint s, uint d)
    {
        uint inv = 255 - (s >> 24);
        uint b = (s & 255) + ((d & 255) * inv + 127) / 255;
        uint g = ((s >> 8) & 255) + (((d >> 8) & 255) * inv + 127) / 255;
        uint r = ((s >> 16) & 255) + (((d >> 16) & 255) * inv + 127) / 255;
        uint a = (s >> 24) + ((d >> 24) * inv + 127) / 255;
        return Math.Min(b, 255u) | Math.Min(g, 255u) << 8 | Math.Min(r, 255u) << 16 | Math.Min(a, 255u) << 24;
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
    internal static unsafe void Write(string path, uint* bgra, int width, int height)
    {
        var data = new byte[checked(width * height * 4)];
        fixed (byte* d = data) Buffer.MemoryCopy(bgra, d, data.Length, data.Length);
        Write(path, data, width, height);
    }
}
