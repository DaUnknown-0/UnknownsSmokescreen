// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace UnknownsSmokescreen;

// Die Renderer, die die Maske tatsaechlich aendert, gruppiert nach Anker und als Inseln angeordnet.
// Weltinhalt ausserhalb dieser Menge wird nie aufgenommen.
internal static class PrivateSet
{
    private sealed class Item
    {
        public Renderer R;
        public bool Main;
        public long Key;
        public Transform Anchor;
        public Camera Camera;
        public float X0, Y0, X1, Y1;
        public bool HasRect;
        public SpriteRenderer Sprite;
        public TMP_Text Text;
        public bool Percent, Outline;
    }
    internal sealed class Anchor
    {
        public Transform Transform;
        public Camera Camera;
        public int Capture;
        public float X, Y, Speed, Time;
        public bool Known;
    }
    // Hud: Elemente der UI-Kamera. Sie werden getrennt von den Weltelementen aufgenommen.
    private struct Box { public long Key; public bool Hud, Text; public int X0, Y0, X1, Y1; public int Radius; public Anchor Anchor; }

    private static readonly List<Item> items = new();
    private static readonly Dictionary<IntPtr, Item> byPointer = new();
    private static readonly Dictionary<IntPtr, (long key, Transform anchor)> anchorCache = new();
    private static readonly HashSet<IntPtr> players = new();
    private static readonly List<TMP_Text> texts = new();
    private static readonly Dictionary<IntPtr, (int frame, Renderer[] list)> textRenderers = new();
    private static readonly List<(GameObject go, int layer)> moved = new();
    private static readonly HashSet<IntPtr> movedObjects = new();
    private static readonly List<Box> boxes = new();
    internal static readonly Dictionary<long, Anchor> Anchors = new();
    private static readonly List<long> stale = new();
    internal static IslandDesc[] Islands = new IslandDesc[64];
    internal static int Count, AtlasWidth, AtlasHeight, CaptureIndex;
    internal static int LastItems;

    private static readonly int PercentId = Shader.PropertyToID("_Percent"), OutlineId = Shader.PropertyToID("_Outline");
    private static long rememberedContent;

    // Billige Signatur des sichtbaren Inhalts der zuletzt aufgenommenen Menge: Sichtbarkeit, Drehung,
    // Skalierung, Sprite, Farbe, Text, Cooldown-Anteil (1/100) und Umriss.
    private static long Content()
    {
        long h = items.Count;
        foreach (var item in items)
        {
            var r = item.R;
            if (!Alive(r)) { h = h * 31 + 7; continue; }
            bool on = r.enabled && r.gameObject.activeInHierarchy;
            h = h * 31 + (on ? 3 : 5);
            if (!on) continue;
            var t = r.transform;
            var q = t.localRotation; var sc = t.localScale;
            h = h * 31 + (int)(q.z * 4096) ; h = h * 31 + (int)(q.w * 4096);
            h = h * 31 + (int)(sc.x * 4096); h = h * 31 + (int)(sc.y * 4096);
            if (item.Sprite != null && Alive(item.Sprite))
            {
                var sprite = item.Sprite.sprite;
                h = h * 31 + (Alive(sprite) ? sprite.Pointer.ToInt64() : 0);
                var c = item.Sprite.color; h = h * 31 + (int)(c.r * 255) + 256 * (int)(c.g * 255) + 65536 * (int)(c.b * 255) + 16777216L * (int)(c.a * 255);
            }
            if (item.Text != null && Alive(item.Text))
            {
                h = h * 31 + (item.Text.text ?? "").GetHashCode();
                var c = item.Text.color; h = h * 31 + (int)(c.r * 255) + 256 * (int)(c.g * 255) + 65536 * (int)(c.b * 255) + 16777216L * (int)(c.a * 255);
            }
            if (item.Percent || item.Outline)
            {
                // Das Spiel legt beim Setzen eines Cooldowns eine Materialinstanz an: immer frisch lesen.
                var material = r.sharedMaterial;
                if (Alive(material))
                {
                    if (item.Percent && material.HasProperty(PercentId)) h = h * 31 + (int)(material.GetFloat(PercentId) * 100);
                    if (item.Outline && material.HasProperty(OutlineId)) h = h * 31 + (int)(material.GetFloat(OutlineId) * 100);
                }
            }
        }
        // Umrisse, die erst nach der letzten Aufnahme angehen (Kill-Ziel, Vent in Reichweite), sind noch
        // nicht in der Menge. Ihr Einschalten loest deshalb ebenfalls eine Aufnahme aus.
        foreach (var p in PlayerControl.AllPlayerControls)
        {
            var body = Alive(p) && p.cosmetics != null ? p.cosmetics.currentBodySprite : null;
            if (body != null && Alive(body.BodySprite)) h = h * 31 + Outline(body.BodySprite);
        }
        var ship = ShipStatus.Instance;
        if (Alive(ship))
            foreach (var v in ship.AllVents)
                if (Alive(v) && Alive(v.myRend)) h = h * 31 + Outline(v.myRend);
        return h;
    }
    private static int Outline(Renderer r)
    {
        var material = r.sharedMaterial;
        return Alive(material) && material.HasProperty(OutlineId) ? (int)(material.GetFloat(OutlineId) * 100) : 0;
    }
    // Am Bildrand abgeschnittene, verankerte Inseln: kommt der Anker ins Bild, fehlt der neue Teil bis zur naechsten Aufnahme.
    private static readonly List<(long key, float x, float y)> clipped = new();
    internal static bool ContentChanged()
    {
        foreach (var c in clipped)
            if (Anchors.TryGetValue(c.key, out var a) && a.Known && Math.Abs(a.X - c.x) + Math.Abs(a.Y - c.y) > 2) return true;
        return Content() != rememberedContent;
    }
    internal static void RememberContent() => rememberedContent = Content();

    internal static void Reset()
    {
        items.Clear(); byPointer.Clear(); anchorCache.Clear(); textRenderers.Clear(); Anchors.Clear();
        Count = 0;
    }

    private static bool Alive(UnityEngine.Object o) => RenderState.Alive(o);

    // Maskiert und echt vermessen, gruppieren und im Atlas anordnen. Der Spielzustand ist danach wieder echt.
    internal static void Build(Camera main, Camera ui, int width, int height)
    {
        items.Clear(); byPointer.Clear(); Count = 0; AtlasWidth = AtlasHeight = 0;
        CaptureIndex++;
        players.Clear();
        foreach (var p in PlayerControl.AllPlayerControls)
            if (Alive(p)) players.Add(p.transform.Pointer);
        try
        {
            Mask.Apply(); RenderState.Flush();
            foreach (var r in Mask.Hidden) Add(r, main, ui, width, height, false);
            texts.Clear(); RenderState.ChangedTexts(texts);
            foreach (var t in texts)
                foreach (var r in TextRenderers(t)) Add(r, main, ui, width, height, true);
            foreach (var r in RenderState.Owners) Add(r, main, ui, width, height, true);
        }
        finally { Mask.RestoreAll(); }
        LastItems = items.Count;
        if (items.Count == 0) return;
        // Echte Lage aller sichtbaren Elemente.
        foreach (var item in items) Measure(item, width, height);

        int baseRadius = Math.Max(2, (int)Math.Round(2.0 * height / 1080));
        boxes.Clear();
        foreach (var item in items)
        {
            if (!item.HasRect) continue;
            Anchor anchor = null;
            int radius = baseRadius;
            if (item.Key != 0)
            {
                if (!Anchors.TryGetValue(item.Key, out anchor)) Anchors[item.Key] = anchor = new Anchor();
                anchor.Transform = item.Anchor; anchor.Camera = item.Camera; anchor.Capture = CaptureIndex;
                // Rest-Versatz zwischen Overlay und Spielbild ist hoechstens etwa ein Frame (bei 60 Hz rund 17 ms).
                radius += Math.Min(item.Text != null ? 14 : 8, (int)Math.Ceiling(anchor.Speed * (item.Text != null ? 0.017f : 0.012f)));
            }
            int margin = radius + IslandBuilder.Feather + 2;
            AddBox(new Box
            {
                Key = item.Key, Hud = !item.Main, Text = item.Text != null, Radius = radius, Anchor = anchor,
                X0 = Math.Max(0, (int)Math.Floor(item.X0) - margin), Y0 = Math.Max(0, (int)Math.Floor(item.Y0) - margin),
                X1 = Math.Min(width, (int)Math.Ceiling(item.X1) + margin), Y1 = Math.Min(height, (int)Math.Ceiling(item.Y1) + margin),
            });
        }
        // Anker, die in den letzten zwei Aufnahmen nicht vorkamen, werden nicht mehr verfolgt.
        stale.Clear();
        foreach (var pair in Anchors) if (pair.Value.Capture < CaptureIndex - 1 || !Alive(pair.Value.Transform)) stale.Add(pair.Key);
        foreach (var key in stale) Anchors.Remove(key);
        if (boxes.Count > 48) Collapse();
        Layout(width, height);
    }

    private static void AddBox(Box box)
    {
        if (box.X1 <= box.X0 || box.Y1 <= box.Y0) return;
        // Ueberlappende oder sehr nahe Rechtecke desselben Ankers verschmelzen.
        for (int i = 0; i < boxes.Count; i++)
        {
            var b = boxes[i];
            if (b.Key != box.Key || b.Hud != box.Hud) continue;
            if (box.X0 > b.X1 + 8 || b.X0 > box.X1 + 8 || box.Y0 > b.Y1 + 8 || b.Y0 > box.Y1 + 8) continue;
            boxes.RemoveAt(i);
            box.X0 = Math.Min(box.X0, b.X0); box.Y0 = Math.Min(box.Y0, b.Y0);
            box.X1 = Math.Max(box.X1, b.X1); box.Y1 = Math.Max(box.Y1, b.Y1);
            box.Radius = Math.Max(box.Radius, b.Radius);
            box.Text = box.Text && b.Text;
            AddBox(box);
            return;
        }
        boxes.Add(box);
    }

    private static void Collapse()
    {
        var merged = new Dictionary<long, Box>();
        foreach (var b in boxes)
        {
            if (!merged.TryGetValue(b.Key, out var m)) { merged[b.Key] = b; continue; }
            m.X0 = Math.Min(m.X0, b.X0); m.Y0 = Math.Min(m.Y0, b.Y0); m.X1 = Math.Max(m.X1, b.X1); m.Y1 = Math.Max(m.Y1, b.Y1);
            m.Radius = Math.Max(m.Radius, b.Radius); merged[b.Key] = m;
        }
        boxes.Clear(); boxes.AddRange(merged.Values);
    }

    // Lesegruppe: ein Rechteck, das pro Durchgang mit einem einzigen ReadPixels gelesen wird.
    internal struct ReadGroup { public int X, Y, W, H, AtlasX, AtlasY; public bool Hud; }
    private static readonly List<bool> groupHud = new();
    internal static ReadGroup[] Groups = new ReadGroup[4];
    internal static int GroupCount;
    private static readonly List<(int x0, int y0, int x1, int y1)> groupBoxes = new();
    private static readonly List<int> owner = new();
    // Jeder ReadPixels-Aufruf kostet im Spiel etwa 0,5 ms fest. Inseln werden deshalb zu hoechstens
    // drei Gruppen zusammengelegt, solange die zusaetzliche Flaeche klein bleibt.
    // Mit GPU-Packen werden nur ueberlappende Inseln verbunden; ohne es (Fallback) bis zu drei Gruppen.
    internal static bool FewGroups;
    private static long MergeArea => FewGroups ? 600_000 : 0;
    private static int MaxGroups => FewGroups ? 3 : 64;

    // Bloecke [echt schwarz | echt weiss | maskiert schwarz | maskiert weiss] je Gruppe in Regalen.
    private static void Layout(int width, int height)
    {
        // Kleinste Insel zuerst: sie besitzt bei Ueberlappung die Pixel.
        // Weltinseln vor HUD-Inseln (der HUD liegt im Spiel darueber), innerhalb jeweils die kleinste zuerst.
        boxes.Sort((a, b) => a.Hud != b.Hud ? a.Hud.CompareTo(b.Hud) : ((long)(a.X1 - a.X0) * (a.Y1 - a.Y0)).CompareTo((long)(b.X1 - b.X0) * (b.Y1 - b.Y0)));
        if (Islands.Length < boxes.Count) Islands = new IslandDesc[boxes.Count + 16];
        groupBoxes.Clear(); owner.Clear();
        groupHud.Clear();
        for (int i = 0; i < boxes.Count; i++) { groupBoxes.Add((boxes[i].X0, boxes[i].Y0, boxes[i].X1, boxes[i].Y1)); groupHud.Add(boxes[i].Hud); owner.Add(i); }
        while (groupBoxes.Count > 1)
        {
            int bi = -1, bj = -1; long best = long.MaxValue;
            for (int i = 0; i < groupBoxes.Count; i++)
            for (int j = i + 1; j < groupBoxes.Count; j++)
            {
                if (groupHud[i] != groupHud[j]) continue;
                var a = groupBoxes[i]; var b = groupBoxes[j];
                long union = (long)(Math.Max(a.x1, b.x1) - Math.Min(a.x0, b.x0)) * (Math.Max(a.y1, b.y1) - Math.Min(a.y0, b.y0));
                long added = union - (long)(a.x1 - a.x0) * (a.y1 - a.y0) - (long)(b.x1 - b.x0) * (b.y1 - b.y0);
                if (added < best) { best = added; bi = i; bj = j; }
            }
            if (bi < 0 || (best > MergeArea && groupBoxes.Count <= MaxGroups)) break;
            var p = groupBoxes[bi]; var q = groupBoxes[bj];
            groupBoxes[bi] = (Math.Min(p.x0, q.x0), Math.Min(p.y0, q.y0), Math.Max(p.x1, q.x1), Math.Max(p.y1, q.y1));
            groupBoxes.RemoveAt(bj); groupHud.RemoveAt(bj);
            for (int k = 0; k < owner.Count; k++) { if (owner[k] == bj) owner[k] = bi; else if (owner[k] > bj) owner[k]--; }
        }
        GroupCount = groupBoxes.Count;
        if (Groups.Length < GroupCount) Groups = new ReadGroup[GroupCount + 4];
        int widest = 0;
        foreach (var g in groupBoxes) widest = Math.Max(widest, g.x1 - g.x0);
        int shelfWidth = Math.Max(4 * widest, Math.Min(4096, 4 * width));
        int x = 0, y = 0, shelf = 0;
        for (int i = 0; i < GroupCount; i++)
        {
            var g = groupBoxes[i];
            int w = g.x1 - g.x0, h = g.y1 - g.y0;
            if (x + 4 * w > shelfWidth) { y += shelf; x = 0; shelf = 0; }
            Groups[i] = new ReadGroup { X = g.x0, Y = g.y0, W = w, H = h, AtlasX = x, AtlasY = y, Hud = groupHud[i] };
            x += 4 * w; shelf = Math.Max(shelf, h);
            AtlasWidth = Math.Max(AtlasWidth, x);
        }
        AtlasHeight = y + shelf;
        Count = 0;
        clipped.Clear();
        for (int i = 0; i < boxes.Count; i++)
        {
            var b = boxes[i]; var g = Groups[owner[i]];
            float ax = 0, ay = 0;
            if (b.Key != 0 && b.Anchor != null)
            {
                var p = b.Anchor.Camera.WorldToScreenPoint(b.Anchor.Transform.position);
                ax = p.x; ay = height - p.y;
                if (b.X0 <= 0 || b.Y0 <= 0 || b.X1 >= width || b.Y1 >= height) clipped.Add((b.Key, ax, ay));
            }
            Islands[Count++] = new IslandDesc
            {
                X = b.X0, Y = b.Y0, W = b.X1 - b.X0, H = b.Y1 - b.Y0, Column = g.W,
                AtlasX = g.AtlasX + (b.X0 - g.X), AtlasY = g.AtlasY + (b.Y0 - g.Y),
                Key = b.Key, AnchorX = ax, AnchorY = ay, Radius = b.Radius, Hud = b.Hud, Plate = b.Text && b.Key != 0,
            };
        }
    }

    private static void Add(Renderer r, Camera main, Camera ui, int width, int height, bool measure)
    {
        if (!Alive(r)) return;
        var ptr = r.Pointer;
        if (!byPointer.TryGetValue(ptr, out var item))
        {
            int layer = r.gameObject.layer;
            bool onUi = Alive(ui) && (ui.cullingMask & (1 << layer)) != 0;
            bool onMain = !onUi && Alive(main) && (main.cullingMask & (1 << layer)) != 0;
            if (!onUi && !onMain) return;
            item = new Item { R = r, Main = onMain, Camera = onUi ? ui : main };
            item.Sprite = r.TryCast<SpriteRenderer>();
            item.Text = r.GetComponent<TMP_Text>();
            var shared = r.sharedMaterial;
            item.Percent = Alive(shared) && shared.HasProperty(PercentId);
            item.Outline = Alive(shared) && shared.HasProperty(OutlineId);
            ResolveAnchor(r, main, out item.Key, out item.Anchor);
            if (item.Key != 0 && !Alive(item.Anchor)) return;
            byPointer[ptr] = item; items.Add(item);
        }
        if (measure) Measure(item, width, height);
    }

    private static void Measure(Item item, int width, int height)
    {
        var r = item.R;
        if (!Alive(r) || !r.enabled || !r.gameObject.activeInHierarchy) return;
        var b = r.bounds;
        var a = item.Camera.WorldToScreenPoint(b.min);
        var c = item.Camera.WorldToScreenPoint(b.max);
        float x0 = Math.Min(a.x, c.x), x1 = Math.Max(a.x, c.x), y0 = Math.Min(a.y, c.y), y1 = Math.Max(a.y, c.y);
        if (float.IsNaN(x0) || float.IsNaN(y0) || x1 < 0 || y1 < 0 || x0 > width || y0 > height) return;
        if (!item.HasRect) { item.X0 = x0; item.Y0 = y0; item.X1 = x1; item.Y1 = y1; item.HasRect = true; return; }
        item.X0 = Math.Min(item.X0, x0); item.Y0 = Math.Min(item.Y0, y0);
        item.X1 = Math.Max(item.X1, x1); item.Y1 = Math.Max(item.Y1, y1);
    }

    // Bildschirmfest: alles unter der Hauptkamera (HUD). Sonst der Spieler oder die Wurzel des Objekts.
    private static void ResolveAnchor(Renderer r, Camera main, out long key, out Transform anchor)
    {
        var ptr = r.Pointer;
        if (anchorCache.TryGetValue(ptr, out var cached) && (cached.key == 0 || Alive(cached.anchor))) { key = cached.key; anchor = cached.anchor; return; }
        var camera = Alive(main) ? main.transform.Pointer : IntPtr.Zero;
        Transform t = r.transform, last = t;
        key = -1; anchor = null;
        for (int depth = 0; Alive(t) && depth < 64; depth++, t = t.parent)
        {
            if (t.Pointer == camera) { key = 0; break; }
            if (players.Contains(t.Pointer)) { anchor = t; break; }
            last = t;
        }
        if (key != 0)
        {
            anchor ??= last;
            key = anchor.Pointer.ToInt64() | 1L << 62;
        }
        anchorCache[ptr] = (key, anchor);
    }

    private static Renderer[] TextRenderers(TMP_Text t)
    {
        if (textRenderers.TryGetValue(t.Pointer, out var e) && Time.frameCount - e.frame < 120) return e.list;
        var arr = t.GetComponentsInChildren<Renderer>(true);
        var list = new Renderer[arr.Length];
        for (int i = 0; i < arr.Length; i++) list[i] = arr[i];
        textRenderers[t.Pointer] = (Time.frameCount, list);
        return list;
    }

    // Weltrenderer der Hauptkamera fuer die Aufnahme auf den privaten Layer legen.
    internal static void MoveLayers(int layer)
    {
        foreach (var item in items)
        {
            if (!item.Main || !Alive(item.R)) continue;
            var go = item.R.gameObject;
            if (!movedObjects.Add(go.Pointer)) continue;
            moved.Add((go, go.layer));
            go.layer = layer;
        }
    }

    // Waehrend der Welt-Durchgaenge sind die privaten HUD-Renderer aus; der oeffentliche HUD bleibt als Verdecker.
    private static readonly List<Renderer> silenced = new();
    internal static void SilenceHud()
    {
        foreach (var item in items)
            if (!item.Main && Alive(item.R) && item.R.enabled) { item.R.enabled = false; silenced.Add(item.R); }
    }
    internal static void RestoreHud()
    {
        foreach (var r in silenced) if (Alive(r)) r.enabled = true;
        silenced.Clear();
    }

    internal static void RestoreLayers()
    {
        for (int i = moved.Count - 1; i >= 0; i--) if (Alive(moved[i].go)) moved[i].go.layer = moved[i].layer;
        moved.Clear(); movedObjects.Clear();
    }

    // Ankerlagen am Frameende, Pixel von oben links. Geschwindigkeit fuer den Plattenradius.
    internal static void Track(AnchorPositions into, int height, float now)
    {
        into.BlockWorld = OverlayContent.ActiveMinigame != IntPtr.Zero;
        into.Count = 0;
        foreach (var pair in Anchors)
        {
            var a = pair.Value;
            if (!Alive(a.Transform) || !Alive(a.Camera) || into.Count >= into.Keys.Length) continue;
            var p = a.Camera.WorldToScreenPoint(a.Transform.position);
            float x = p.x, y = height - p.y;
            if (a.Known && now > a.Time)
            {
                float dx = x - a.X, dy = y - a.Y;
                float speed = MathF.Sqrt(dx * dx + dy * dy) / Math.Max(0.004f, now - a.Time);
                a.Speed = speed > a.Speed ? speed : a.Speed * 0.85f + speed * 0.15f;
            }
            a.X = x; a.Y = y; a.Time = now; a.Known = true;
            into.Keys[into.Count] = pair.Key; into.X[into.Count] = x; into.Y[into.Count] = y; into.Count++;
        }
    }
}
