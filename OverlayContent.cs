// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace UnknownsSmokescreen;

// Das private Overlay enthaelt nur den Beitrag der Elemente, die die Maske aendert, ohne Weltpixel.
// Inhalte kommen mit der eingestellten Rate, ihre Lage folgt jedem Spielframe ueber Anker.
internal static class OverlayContent
{
    internal static bool Rendering;
    private static bool shutdown;
    internal static bool Stopped => shutdown;
    private static Camera copy;
    private static Camera[] cameras = Array.Empty<Camera>();
    private static Camera[] observed = Array.Empty<Camera>();
    private static float[] depths = Array.Empty<float>();
    private static IntPtr[] destinations = Array.Empty<IntPtr>();
    private static Il2CppReferenceArray<Camera> cameraBuffer;
    private static Camera mainCamera, uiCamera;
    private static int privateLayer = -1, occluders;
    // Zwei getrennte Renderziele: je Runde ueber Schwarz und ueber Weiss. Kein Teil-Viewport,
    // weil Unity unter D3D Viewports in RenderTextures gegenueber ReadPixels gespiegelt ablegt.
    private static readonly RenderTexture[] stage = new RenderTexture[2];
    private static double setMs, renderPassMs, readMs, copyMs;
    private static long readPixels;
    // GPU-seitiges Packen: eine eigene Kamera zeichnet je Durchgang Quads mit den Inselausschnitten
    // der Stage-Textur in eine Atlas-Textur. Danach genuegt ein einziges ReadPixels pro Aufnahme.
    private static Camera blitCamera;
    private static GameObject blitObject;
    private static Mesh blitMesh;
    private static Material blitMaterial;
    private static RenderTexture atlasRt;
    private static int blitLayer = -1, blitQuads = -1;
    private static bool blitUnavailable;
    private static Vector3[] blitVertices = Array.Empty<Vector3>();
    private static Vector2[] blitUv = Array.Empty<Vector2>();
    private static float lastIslandCapture;
    private static int skippedCaptures;
    // Im Spiel gemessen (0.3.0.4-Audit): Quellzeilen von RenderTextures zaehlen dort von oben.
    private static readonly bool flipSource = SystemInfo.graphicsUVStartsAtTop;
    private static Texture2D atlasCpu, fullCpu, probeCpu;
    private static bool releaseAfterFrame;
    private static int width, height;
    private static float next, metricsAt;
    private static int frames, islandFrames;
    private static double renderMs;
    private static bool visible = true;
    private static bool diagnostic;
    private static bool diagnosticInitialized, diagnosticConfig;
    private static float diagnosticEnd;
    private static string probe;
    private static int probes;
    private static readonly AnchorPositions positions = new();
    internal static long PublishedFrames;
    internal static double LastFrameMilliseconds;
    internal static void Reset() { next = 0; }

    internal static void Tick(bool active)
    {
        if (shutdown) return;
        BorderlessMode.Tick(Shield.Wanted && SmokescreenPlugin.PrivateOverlay.Value);
        if (!SettingsMenu.KeyConsumed && Input.GetKeyDown(SmokescreenPlugin.OverlayHotkey.Value) && OverlayHost.GameForeground)
            visible = !visible;
        // Das letzte private Bild erst nach dem ersten unmaskierten Bildschirm freigeben.
        releaseAfterFrame = !active;
        if (!active) return;
        InitializeDiagnostics();
        OverlayHost.SetVisible(visible, true);
        IntroPrivacy.Prepare(Mask.Intro);
        TeamColors.Refresh();
        DiscoverCameras();
        bool wantPrivate = OverlayHost.PrivateWanted, wantFeed = OverlayHost.FeedWanted;
        if ((!wantPrivate && !wantFeed) || Time.realtimeSinceStartup < next) return;
        // Die Lage folgt ohnehin jedem Frame. Unveraenderter Inhalt wird nur alle 250 ms neu aufgenommen.
        bool fullNow = RenderState.Alive(Mask.Intro) || RenderState.Alive(MeetingHud.Instance) || privateLayer < 0 || !RenderState.Alive(mainCamera);
        bool islandsDue = !fullNow && wantPrivate && (Time.realtimeSinceStartup - lastIslandCapture >= 0.25f || PrivateSet.ContentChanged());
        if (!fullNow && wantPrivate && !islandsDue) skippedCaptures++;
        if (!fullNow && !wantFeed && !islandsDue) return;
        next = Time.realtimeSinceStartup + 1f / SmokescreenPlugin.OverlayFps.Value;
        var watch = Stopwatch.StartNew();
        try
        {
            int w = Screen.width, h = Screen.height;
            if (w < 1 || h < 1 || (long)w * h > 3840L * 2160) return;
            Ensure(w, h);
            Rendering = true;
            // Intro und Meeting verdecken die Welt: dort bleibt das echte Vollbild deckend.
            bool full = RenderState.Alive(Mask.Intro) || RenderState.Alive(MeetingHud.Instance) || privateLayer < 0 || !RenderState.Alive(mainCamera);
            if (full || wantFeed)
            {
                RenderFull(0);
                Read(fullCpu, 0, 0, 0, w, h, 0);
                var pixels = fullCpu.GetWritableImageData(0);
                if (pixels == IntPtr.Zero) throw new InvalidOperationException("CPU-Texturpuffer fehlt");
                if (wantFeed) OverlayHost.PublishFeed(pixels, w, h);
                if (full && wantPrivate) PublishFull(pixels, w, h);
            }
            if (!full && islandsDue) CaptureIslands(w, h);
            if (diagnostic && PublishedFrames % 90 == 0) RequestProbe("hud");
            PublishedFrames++;
        }
        catch (Exception e) { Failed(e); }
        finally { Rendering = false; }
        watch.Stop();
        LastFrameMilliseconds = watch.Elapsed.TotalMilliseconds;
        renderMs += LastFrameMilliseconds; frames++;
        if (Time.realtimeSinceStartup > metricsAt)
        {
            using var process = Process.GetCurrentProcess();
            SmokescreenPlugin.Logger.LogInfo($"HUD: {width}x{height}, Bilder={PublishedFrames}, Render/Kopie={renderMs / Math.Max(1, frames):F2} ms, Inselbilder={islandFrames}, uebersprungen={skippedCaptures}, Packen={(blitUnavailable ? "ReadPixels je Gruppe" : "GPU-Atlas")}, je Inselbild Menge={setMs / Math.Max(1, islandFrames):F2} Renders={renderPassMs / Math.Max(1, islandFrames):F2} Readback={readMs / Math.Max(1, islandFrames):F2} ({readPixels / 1000 / Math.Max(1, islandFrames)} kpx, Gruppen={PrivateSet.GroupCount}) Kopie={copyMs / Math.Max(1, islandFrames):F2} ms, Inseln={PrivateSet.Count}, Elemente={PrivateSet.LastItems}, Atlas={PrivateSet.AtlasWidth}x{PrivateSet.AtlasHeight}, Layer={privateLayer}, Prozess={process.PrivateMemorySize64 / 1048576} MiB, Masken={CameraMaskScope.Applications}, Restore={CameraMaskScope.Restores}, ApplyGesamt={CameraMaskScope.ApplyMilliseconds:F2} ms");
            renderMs = 0; frames = 0; islandFrames = 0; skippedCaptures = 0; setMs = renderPassMs = readMs = copyMs = 0; readPixels = 0; metricsAt = Time.realtimeSinceStartup + 15;
        }
        if (diagnostic && Time.realtimeSinceStartup > diagnosticEnd) { diagnostic = false; OverlayHost.EndDiagnostic(); }
    }

    // Echt ueber Schwarz/Weiss, maskiert ueber Schwarz/Weiss. Nur private Renderer, HUD und Abdunkelung.
    private static void CaptureIslands(int w, int h)
    {
        long t0 = Stopwatch.GetTimestamp();
        PrivateSet.FewGroups = blitUnavailable || blitLayer < 0;
        PrivateSet.Build(mainCamera, uiCamera, w, h);
        long t1 = Stopwatch.GetTimestamp();
        setMs += (t1 - t0) * 1000.0 / Stopwatch.Frequency;
        int count = PrivateSet.Count;
        if (count == 0) { OverlayHost.PublishEmpty(w, h); return; }
        EnsureAtlas(PrivateSet.AtlasWidth, PrivateSet.AtlasHeight);
        PrivateSet.MoveLayers(privateLayer);
        try
        {
            bool gpu = PrepareBlit();
            long a = Stopwatch.GetTimestamp();
            Round(0, gpu);
            long b = Stopwatch.GetTimestamp();
            try
            {
                Mask.Apply(); RenderState.Flush();
                Round(2, gpu);
            }
            finally { Mask.RestoreAll(); }
            long d = Stopwatch.GetTimestamp();
            if (gpu) ReadAtlas();
            long e = Stopwatch.GetTimestamp();
            double f = 1000.0 / Stopwatch.Frequency;
            renderPassMs += (b - a + d - b) * f; readMs += (e - d) * f;
        }
        finally { PrivateSet.RestoreLayers(); }
        lastIslandCapture = Time.realtimeSinceStartup;
        PrivateSet.RememberContent();
        long copyStart = Stopwatch.GetTimestamp();
        var data = atlasCpu.GetWritableImageData(0);
        if (data == IntPtr.Zero) throw new InvalidOperationException("Atlaspuffer fehlt");
        var set = OverlayHost.BeginCapture(w, h, PrivateSet.AtlasWidth, PrivateSet.AtlasHeight);
        if (set == null) return;
        int rowBytes = PrivateSet.AtlasWidth * 4, sourceStride = atlasCpu.width * 4;
        for (int y = 0; y < PrivateSet.AtlasHeight; y++)
            System.Runtime.InteropServices.Marshal.Copy(data + y * sourceStride, set.Atlas, y * rowBytes, rowBytes);
        set.Stride = rowBytes;
        OverlayHost.EnsureIslands(set, count);
        Array.Copy(PrivateSet.Islands, set.Islands, count); set.Count = count;
        set.Ortho = mainCamera.orthographicSize;
        OverlayHost.CommitCapture(set);
        copyMs += (Stopwatch.GetTimestamp() - copyStart) * 1000.0 / Stopwatch.Frequency;
        islandFrames++;
    }

    private static void PublishFull(IntPtr pixels, int w, int h)
    {
        var set = OverlayHost.BeginCapture(w, h, w, h);
        if (set == null) return;
        System.Runtime.InteropServices.Marshal.Copy(pixels, set.Atlas, 0, w * h * 4);
        set.Stride = w * 4;
        set.Islands[0] = new IslandDesc { X = 0, Y = 0, W = w, H = h, Opaque = true };
        set.Count = 1;
        set.Ortho = RenderState.Alive(mainCamera) ? mainCamera.orthographicSize : 0;
        OverlayHost.CommitCapture(set);
    }

    // Am Frameende: Ankerlagen an den Fensterthread, Diagnoseprobe, Freigabe nach Schutzende.
    internal static void CompleteFrame()
    {
        if (!shutdown && Shield.Active && OverlayHost.PrivateWanted)
        {
            string label = probe; probe = null;
            if (label != null) Probe(label);
            PrivateSet.Track(positions, Screen.height, Time.realtimeSinceStartup);
            positions.Ortho = RenderState.Alive(mainCamera) ? mainCamera.orthographicSize : 0;
            positions.Frame = Time.frameCount; positions.Probe = label; positions.Delay = SmokescreenPlugin.SyncFrames.Value;
            OverlayHost.PublishPositions(positions);
        }
        if (!releaseAfterFrame || Shield.Active || CameraMaskScope.LastMaskedFrame == Time.frameCount) return;
        releaseAfterFrame = false;
        OverlayHost.SetVisible(false);
        OverlayHost.Discard();
        ReleaseImages();
    }

    // Diagnose: das tatsaechlich gezeigte Spielbild dieses Frames (maskiert), das echte Bild und
    // das vom Fensterthread fuer genau diese Ankerlagen komponierte Overlay.
    internal static void RequestProbe(string label)
    {
        if (diagnostic && probes < 48) probe = Path.GetFileName(label);
    }
    private static void Probe(string label)
    {
        string directory = OverlayHost.DiagnosticDirectory;
        if (directory == null || width == 0) return;
        probes++;
        try
        {
            Rendering = true;
            if (!RenderState.Alive(probeCpu)) probeCpu = Cpu(width, height);
            // Der Zustand ist jetzt der des regulaeren, maskierten Frames.
            RenderFull(0); Read(probeCpu, 0, 0, 0, width, height, 0);
            CameraMaskScope.Restore();
            RenderFull(1); Read(fullCpu, 0, 0, 1, width, height, 0);
            Directory.CreateDirectory(directory);
            string prefix = Path.Combine(directory, label + "_" + Time.frameCount);
            Dump(prefix + "_masked.bmp", probeCpu);
            Dump(prefix + "_real.bmp", fullCpu);
            if (RenderState.Alive(atlasCpu) && PrivateSet.AtlasWidth > 0) DumpAtlas(prefix + "_atlas.bmp");
            File.WriteAllText(prefix + "_frame.txt", $"frame={Time.frameCount} capture={PrivateSet.CaptureIndex} islands={PrivateSet.Count} t={Environment.TickCount64}\n");
        }
        catch (Exception e) { SmokescreenPlugin.Logger.LogWarning("HUD-Diagnose: " + e.Message); diagnostic = false; }
        finally { Rendering = false; }
    }
    // Roher Atlas der letzten Aufnahme: je Insel echt schwarz, echt weiss, maskiert schwarz, maskiert weiss.
    private static unsafe void DumpAtlas(string path)
    {
        int w = PrivateSet.AtlasWidth, h = PrivateSet.AtlasHeight, stride = atlasCpu.width * 4;
        var data = new byte[w * h * 4];
        byte* source = (byte*)atlasCpu.GetWritableImageData(0);
        for (int y = 0; y < h; y++)
        {
            byte* row = source + (h - 1 - y) * stride;
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4;
                data[o] = row[x * 4 + 2]; data[o + 1] = row[x * 4 + 1]; data[o + 2] = row[x * 4]; data[o + 3] = 255;
            }
        }
        BitmapFile.Write(path, data, w, h);
    }
    private static unsafe void Dump(string path, Texture2D image)
    {
        var data = new byte[width * height * 4];
        byte* source = (byte*)image.GetWritableImageData(0);
        fixed (byte* d = data) PixelDifference.Copy(source, source, d, d, width, height);
        BitmapFile.Write(path, data, width, height);
    }

    private static void Failed(Exception e)
    {
        // Bei einem Renderfehler bleibt das letzte geschuetzte Bild stehen.
        if (Time.realtimeSinceStartup >= metricsAt)
        {
            SmokescreenPlugin.Logger.LogWarning("HUD-Render: " + e);
            metricsAt = Time.realtimeSinceStartup + 10;
        }
    }

    private static void InitializeDiagnostics()
    {
        // Im Menue umgeschaltet: neu initialisieren, damit die Aenderung sofort gilt.
        if (diagnosticInitialized && diagnosticConfig != SmokescreenPlugin.DiagnosticMode.Value) diagnosticInitialized = false;
        if (diagnosticInitialized) return;
        diagnosticConfig = SmokescreenPlugin.DiagnosticMode.Value;
        diagnosticInitialized = true;
        diagnostic = SmokescreenPlugin.DiagnosticMode.Value || Array.IndexOf(NativeArguments.Values, "--smokescreen-diagnostics") >= 0;
        diagnosticEnd = Time.realtimeSinceStartup + 180f;
        // Diagnose darf ohne Vordergrund aufnehmen und messen, das Fenster bleibt dann trotzdem verborgen.
        OverlayHost.DiagnosticCapture = diagnostic;
    }

    private static void DiscoverCameras()
    {
        // Der native Puffer wird wiederverwendet. Neue Kameras erhalten noch vor ihrem
        // ersten regulären Render eine Maske, ohne sekündliches Erkennungsfenster.
        int count = Camera.allCamerasCount;
        if (cameraBuffer == null || cameraBuffer.Length < count)
            cameraBuffer = new Il2CppReferenceArray<Camera>(count + 8);
        count = Camera.GetAllCameras(cameraBuffer);
        bool changed = count != observed.Length;
        for (int i = 0; !changed && i < count; i++)
        {
            var c = cameraBuffer[i];
            if (!RenderState.Alive(c) || !RenderState.Alive(observed[i]) || c.Pointer != observed[i].Pointer
                || c.depth != depths[i] || Destination(c) != destinations[i]) changed = true;
        }
        if (!changed && RenderState.Alive(mainCamera)) return;
        observed = new Camera[count]; depths = new float[count]; destinations = new IntPtr[count];
        var list = new System.Collections.Generic.List<Camera>(count);
        for (int i = 0; i < count; i++)
        {
            var c = cameraBuffer[i];
            observed[i] = c;
            if (!RenderState.Alive(c) || c == copy) continue;
            depths[i] = c.depth; destinations[i] = Destination(c);
            if (c.GetComponent<CameraMaskScope>() == null) c.gameObject.AddComponent<CameraMaskScope>();
            list.Add(c);
        }
        // Texturkameras liefern unter anderem Überwachungsbilder an das Bildschirm-HUD.
        // Beide Fassungen müssen ihre eigenen aktuellen Texturinhalte verwenden.
        list.Sort((a,b) => {
            int target = (Destination(a) == IntPtr.Zero ? 1 : 0).CompareTo(Destination(b) == IntPtr.Zero ? 1 : 0);
            return target != 0 ? target : a.depth.CompareTo(b.depth);
        });
        cameras = list.ToArray();
        ChooseCameras();
    }

    // Hauptkamera (Welt) und UI-Kamera (HUD, Layer 5). Der private Layer wird von keiner Bildschirmkamera gerendert.
    private static void ChooseCameras()
    {
        mainCamera = uiCamera = null;
        var tagged = Camera.main;
        foreach (var c in cameras)
            if (Destination(c) == IntPtr.Zero && RenderState.Alive(tagged) && c.Pointer == tagged.Pointer) mainCamera = c;
        if (mainCamera == null)
            foreach (var c in cameras) if (Destination(c) == IntPtr.Zero) { mainCamera = c; break; }
        if (mainCamera == null) { privateLayer = -1; return; }
        foreach (var c in cameras)
        {
            if (Destination(c) != IntPtr.Zero || c == mainCamera || (c.cullingMask & (1 << 5)) == 0 || c.depth < mainCamera.depth) continue;
            bool child = c.transform.IsChildOf(mainCamera.transform);
            if (uiCamera == null || (child && !uiCamera.transform.IsChildOf(mainCamera.transform))) uiCamera = c;
        }
        int used = 0;
        foreach (var c in cameras) if (Destination(c) == IntPtr.Zero) used |= c.cullingMask;
        privateLayer = -1;
        int[] candidates = { 30, 29, 28, 27, 26, 25, 24, 23, 22, 21, 20, 7, 6, 3 };
        blitLayer = -1;
        foreach (int layer in candidates)
        {
            if ((used & (1 << layer)) != 0 || !string.IsNullOrEmpty(LayerMask.LayerToName(layer))) continue;
            if (privateLayer < 0) privateLayer = layer;
            else { blitLayer = layer; break; }
        }
        // Bildschirmfeste Ebenen unter der Hauptkamera ausser HUD und Default (z. B. ShadowQuad): Verdecker ueber der Welt.
        occluders = 0;
        var children = mainCamera.GetComponentsInChildren<Renderer>(true);
        int uiMask = RenderState.Alive(uiCamera) ? uiCamera.cullingMask : 0;
        foreach (var r in children)
        {
            int layer = r.gameObject.layer;
            if (layer != 0 && (mainCamera.cullingMask & (1 << layer)) != 0 && (uiMask & (1 << layer)) == 0) occluders |= 1 << layer;
        }
        SmokescreenPlugin.Logger.LogInfo($"HUD-Kameras: Haupt={mainCamera.name}, UI={(RenderState.Alive(uiCamera) ? uiCamera.name : "-")}, privater Layer={privateLayer}, Packlayer={blitLayer}, Verdecker=0x{occluders:X}");
    }

    private static IntPtr Destination(Camera camera)
    {
        var target = camera.targetTexture;
        return RenderState.Alive(target) ? target.Pointer : IntPtr.Zero;
    }
    private static void Ensure(int w, int h)
    {
        if (width == w && height == h && RenderState.Alive(stage[0]) && RenderState.Alive(stage[1])) return;
        ReleaseImages(); width = w; height = h;
        if (!RenderState.Alive(copy))
        {
            var go = new GameObject("SmokescreenPrivateCamera");
            UnityEngine.Object.DontDestroyOnLoad(go);
            copy = go.AddComponent<Camera>(); copy.enabled = false;
        }
        for (int i = 0; i < 2; i++)
        {
            stage[i] = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "SmokescreenStage" + i, hideFlags = HideFlags.DontUnloadUnusedAsset, antiAliasing = 1 };
            stage[i].filterMode = FilterMode.Point;
            stage[i].Create();
        }
        fullCpu = Cpu(w, h);
    }
    private static void EnsureAtlas(int w, int h)
    {
        if (RenderState.Alive(atlasCpu) && atlasCpu.width >= w && atlasCpu.height >= h) return;
        int aw = Math.Max(w, RenderState.Alive(atlasCpu) ? atlasCpu.width : 0), ah = Math.Max(h, RenderState.Alive(atlasCpu) ? atlasCpu.height : 0);
        aw = (aw + 255) / 256 * 256; ah = (ah + 127) / 128 * 128;
        if (RenderState.Alive(atlasCpu)) UnityEngine.Object.Destroy(atlasCpu);
        atlasCpu = new Texture2D(aw, ah, TextureFormat.RGBA32, false) { name = "SmokescreenAtlas", hideFlags = HideFlags.DontUnloadUnusedAsset };
        if (RenderState.Alive(atlasRt)) { atlasRt.Release(); UnityEngine.Object.Destroy(atlasRt); }
        atlasRt = null;
    }
    private static Texture2D Cpu(int w, int h) => new(w,h,TextureFormat.RGBA32,false) { name = "SmokescreenReadback", hideFlags = HideFlags.DontUnloadUnusedAsset };

    private static void Prepare(Camera source, int slot)
    {
        copy.CopyFrom(source); copy.enabled = false;
        copy.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        copy.targetTexture = stage[slot];
        copy.rect = new Rect(0, 0, 1, 1);
        copy.ResetAspect();
    }

    // Eine Runde (echt oder maskiert): erst Welt-Durchgaenge mit abgeschaltetem privatem HUD, dann HUD-Durchgaenge.
    // So enthalten Weltinseln nie HUD-Pixel und umgekehrt, auch wenn sich ihre Rechtecke ueberlappen.
    private static void Round(int column, bool gpu)
    {
        PrivateSet.SilenceHud();
        try
        {
            RenderIslands(0, Color.black, true); RenderIslands(1, Color.white, true);
        }
        finally { PrivateSet.RestoreHud(); }
        if (gpu) { Blit(0, column, false); Blit(1, column + 1, false); } else ReadIslands(column, false);
        RenderIslands(0, Color.black, false); RenderIslands(1, Color.white, false);
        if (gpu) { Blit(0, column, true); Blit(1, column + 1, true); } else ReadIslands(column, true);
    }

    // Welt: Hauptkamera nur privater Layer und Verdecker, darueber der oeffentliche HUD.
    // HUD: nur die UI-Kamera ueber leerem Hintergrund.
    private static void RenderIslands(int slot, Color background, bool world)
    {
        var old = RenderTexture.active;
        try
        {
            bool ui = RenderState.Alive(uiCamera) && uiCamera.enabled && uiCamera.gameObject.activeInHierarchy;
            if (world)
            {
                Prepare(mainCamera, slot);
                copy.cullingMask = (1 << privateLayer) | occluders;
            }
            else
            {
                if (!ui) return;
                Prepare(uiCamera, slot);
            }
            copy.clearFlags = CameraClearFlags.SolidColor;
            copy.backgroundColor = new Color(background.r, background.g, background.b, 0);
            copy.Render();
            if (world && ui)
            {
                Prepare(uiCamera, slot);
                copy.clearFlags = CameraClearFlags.Depth;
                copy.Render();
            }
        }
        finally { copy.targetTexture = null; RenderTexture.active = old; }
    }

    // Vollbild aller Kameras wie im Spiel (Intro, Meeting, Feed, Diagnose).
    private static void RenderFull(int slot)
    {
        var old = RenderTexture.active;
        try
        {
            bool first = true;
            foreach (var c in cameras)
            {
                if (!RenderState.Alive(c) || !c.enabled || !c.gameObject.activeInHierarchy) continue;
                var destination = c.targetTexture;
                if (RenderState.Alive(destination))
                {
                    copy.CopyFrom(c); copy.enabled = false;
                    copy.transform.SetPositionAndRotation(c.transform.position, c.transform.rotation);
                    copy.targetTexture = destination;
                    copy.Render();
                    continue;
                }
                Prepare(c, slot);
                if (first && copy.clearFlags != CameraClearFlags.SolidColor && copy.clearFlags != CameraClearFlags.Skybox)
                {
                    copy.clearFlags = CameraClearFlags.SolidColor; copy.backgroundColor = Color.black;
                }
                first = false;
                copy.Render();
            }
        }
        finally { copy.targetTexture = null; RenderTexture.active = old; }
    }

    private static void Read(Texture2D cpu, int x, int y, int slot, int w, int h, int destination)
    {
        var old = RenderTexture.active;
        try { RenderTexture.active = stage[slot]; cpu.ReadPixels(new Rect(x, y, w, h), destination, 0, false); }
        finally { RenderTexture.active = old; }
    }

    // Je Lesegruppe beide Slots in die Spalten column und column+1 des Atlasblocks.
    // ReadPixels zaehlt die Quellzeile einer RenderTexture unter D3D von oben, das Ziel dagegen
    // von unten. Vollbild-Reads merken davon nichts, Teilrechtecke muessen gespiegelt adressiert werden.
    private static void ReadIslands(int column, bool hud)
    {
        var old = RenderTexture.active;
        try
        {
            for (int slot = 0; slot < 2; slot++)
            {
                RenderTexture.active = stage[slot];
                for (int i = 0; i < PrivateSet.GroupCount; i++)
                {
                    ref var g = ref PrivateSet.Groups[i];
                    if (g.Hud != hud) continue;
                    atlasCpu.ReadPixels(new Rect(g.X, SourceRow(g.Y, g.H), g.W, g.H), g.AtlasX + (column + slot) * g.W, g.AtlasY, false);
                    readPixels += (long)g.W * g.H;
                }
            }
        }
        finally { RenderTexture.active = old; }
    }
    private static int SourceRow(int y, int h) => flipSource ? height - y - h : y;

    // Kamera und Quad-Mesh fuer das Packen; ohne Unlit/Texture-Shader bleibt der Pfad mit einem ReadPixels je Gruppe.
    private static bool PrepareBlit()
    {
        if (blitUnavailable || blitLayer < 0) { blitUnavailable = true; return false; }
        if (!RenderState.Alive(blitCamera))
        {
            var shader = Shader.Find("Unlit/Texture");
            if (!RenderState.Alive(shader)) { blitUnavailable = true; SmokescreenPlugin.Logger.LogWarning("HUD: Unlit/Texture fehlt, Atlas per ReadPixels je Gruppe"); return false; }
            blitMaterial = new Material(shader) { hideFlags = HideFlags.DontUnloadUnusedAsset };
            blitMesh = new Mesh { name = "SmokescreenAtlasQuads", hideFlags = HideFlags.DontUnloadUnusedAsset };
            blitObject = new GameObject("SmokescreenAtlasQuads") { layer = blitLayer };
            UnityEngine.Object.DontDestroyOnLoad(blitObject);
            blitObject.transform.position = new Vector3(-90000, -90000, 0);
            blitObject.AddComponent<MeshFilter>().mesh = blitMesh;
            var renderer = blitObject.AddComponent<MeshRenderer>();
            renderer.material = blitMaterial;
            var go = new GameObject("SmokescreenAtlasCamera");
            UnityEngine.Object.DontDestroyOnLoad(go);
            blitCamera = go.AddComponent<Camera>(); blitCamera.enabled = false;
            blitCamera.orthographic = true; blitCamera.nearClipPlane = 0.1f; blitCamera.farClipPlane = 50f;
            blitCamera.clearFlags = CameraClearFlags.Nothing; blitCamera.cullingMask = 1 << blitLayer;
            blitCamera.allowHDR = false; blitCamera.allowMSAA = false;
        }
        if (!RenderState.Alive(atlasRt))
        {
            atlasRt = new RenderTexture(atlasCpu.width, atlasCpu.height, 0, RenderTextureFormat.ARGB32) { name = "SmokescreenAtlasTarget", hideFlags = HideFlags.DontUnloadUnusedAsset, antiAliasing = 1, filterMode = FilterMode.Point };
            atlasRt.Create();
        }
        blitObject.layer = blitLayer;
        blitCamera.targetTexture = atlasRt;
        blitCamera.rect = new Rect(0, 0, 1, 1);
        blitCamera.orthographicSize = atlasRt.height / 2f;
        blitCamera.ResetAspect();
        var origin = blitObject.transform.position;
        blitCamera.transform.SetPositionAndRotation(origin + new Vector3(atlasRt.width / 2f, atlasRt.height / 2f, -10f), Quaternion.identity);
        return true;
    }

    // Ein Durchgang: Gruppenausschnitte der Stage-Textur slot in die Spalte column ihrer Atlasbloecke.
    private static void Blit(int slot, int column, bool hud)
    {
        int count = PrivateSet.GroupCount;
        if (blitVertices.Length != 4 * count) { blitVertices = new Vector3[4 * count]; blitUv = new Vector2[4 * count]; }
        for (int i = 0; i < count; i++)
        {
            ref var g = ref PrivateSet.Groups[i];
            if (g.Hud != hud)
            {
                // Gruppen der anderen Quelle als leeres Dreieck ausserhalb des Atlas.
                for (int k = 0; k < 4; k++) { blitVertices[4 * i + k] = new Vector3(-10, -10, 0); blitUv[4 * i + k] = Vector2.zero; }
                continue;
            }
            float x0 = g.AtlasX + column * g.W, y0 = g.AtlasY, x1 = x0 + g.W, y1 = y0 + g.H;
            float u0 = (float)g.X / width, v0 = (float)g.Y / height, u1 = (float)(g.X + g.W) / width, v1 = (float)(g.Y + g.H) / height;
            blitVertices[4 * i] = new Vector3(x0, y0, 0); blitVertices[4 * i + 1] = new Vector3(x0, y1, 0);
            blitVertices[4 * i + 2] = new Vector3(x1, y1, 0); blitVertices[4 * i + 3] = new Vector3(x1, y0, 0);
            blitUv[4 * i] = new Vector2(u0, v0); blitUv[4 * i + 1] = new Vector2(u0, v1);
            blitUv[4 * i + 2] = new Vector2(u1, v1); blitUv[4 * i + 3] = new Vector2(u1, v0);
        }
        if (blitQuads != count)
        {
            blitMesh.Clear();
            blitMesh.vertices = blitVertices; blitMesh.uv = blitUv;
            var triangles = new int[6 * count];
            for (int i = 0; i < count; i++)
            {
                triangles[6 * i] = 4 * i; triangles[6 * i + 1] = 4 * i + 1; triangles[6 * i + 2] = 4 * i + 2;
                triangles[6 * i + 3] = 4 * i; triangles[6 * i + 4] = 4 * i + 2; triangles[6 * i + 5] = 4 * i + 3;
            }
            blitMesh.triangles = triangles;
            blitQuads = count;
        }
        else { blitMesh.vertices = blitVertices; blitMesh.uv = blitUv; }
        blitMesh.RecalculateBounds();
        blitMaterial.mainTexture = stage[slot];
        blitCamera.Render();
    }

    // Ein ReadPixels fuer den ganzen genutzten Atlasbereich.
    private static void ReadAtlas()
    {
        var old = RenderTexture.active;
        try
        {
            RenderTexture.active = atlasRt;
            int w = PrivateSet.AtlasWidth, h = PrivateSet.AtlasHeight;
            int y = flipSource ? atlasRt.height - h : 0;
            atlasCpu.ReadPixels(new Rect(0, y, w, h), 0, 0, false);
            readPixels += (long)w * h;
        }
        finally { RenderTexture.active = old; }
    }

    private static void ReleaseImages()
    {
        if (RenderState.Alive(copy)) copy.targetTexture = null;
        for (int i = 0; i < 2; i++) { if (RenderState.Alive(stage[i])) { stage[i].Release(); UnityEngine.Object.Destroy(stage[i]); } stage[i] = null; }
        if (RenderState.Alive(atlasCpu)) UnityEngine.Object.Destroy(atlasCpu);
        if (RenderState.Alive(atlasRt)) { atlasRt.Release(); UnityEngine.Object.Destroy(atlasRt); }
        atlasRt = null;
        if (RenderState.Alive(fullCpu)) UnityEngine.Object.Destroy(fullCpu);
        if (RenderState.Alive(probeCpu)) UnityEngine.Object.Destroy(probeCpu);
        atlasCpu = fullCpu = probeCpu = null; width = height = 0;
        PrivateSet.Anchors.Clear();
    }
    internal static void Dispose()
    {
        shutdown = true;
        releaseAfterFrame = false;
        CameraMaskScope.Restore();
        PrivateSet.RestoreLayers();
        ReleaseImages();
        if (RenderState.Alive(copy)) UnityEngine.Object.Destroy(copy.gameObject);
        copy = null;
        if (RenderState.Alive(blitCamera)) UnityEngine.Object.Destroy(blitCamera.gameObject);
        if (RenderState.Alive(blitObject)) UnityEngine.Object.Destroy(blitObject);
        if (RenderState.Alive(blitMesh)) UnityEngine.Object.Destroy(blitMesh);
        if (RenderState.Alive(blitMaterial)) UnityEngine.Object.Destroy(blitMaterial);
        blitCamera = null; blitObject = null; blitMesh = null; blitMaterial = null; blitQuads = -1;
        foreach (var camera in cameras)
        {
            if (!RenderState.Alive(camera)) continue;
            var scope = camera.GetComponent<CameraMaskScope>();
            if (RenderState.Alive(scope)) UnityEngine.Object.Destroy(scope);
        }
        cameras = Array.Empty<Camera>();
        observed = Array.Empty<Camera>(); depths = Array.Empty<float>(); destinations = Array.Empty<IntPtr>();
        cameraBuffer = null; mainCamera = uiCamera = null;
        BorderlessMode.Tick(false);
    }
}

internal static class BorderlessMode
{
    private static bool changed;
    private static int width, height;
    private static FullScreenMode mode;
    private static float synchronizeAt;
    internal static void Tick(bool wanted)
    {
        if (wanted && !changed)
        {
            mode = Screen.fullScreenMode; width = Screen.width; height = Screen.height;
            changed = true;
            var resolution = DesktopDisplay.Resolution();
            Screen.SetResolution(resolution.width, resolution.height, FullScreenMode.FullScreenWindow);
            synchronizeAt = Time.realtimeSinceStartup + 0.5f;
        }
        else if (!wanted && changed)
        {
            changed = false; Screen.SetResolution(width,height,mode);
        }
        else if (wanted && changed && Screen.fullScreenMode == FullScreenMode.FullScreenWindow
            && Time.realtimeSinceStartup >= synchronizeAt)
        {
            synchronizeAt = Time.realtimeSinceStartup + 0.5f;
            var hwnd = OverlayHost.GameWindow;
            if (hwnd == IntPtr.Zero || Native.IsIconic(hwnd) || !Native.GetClientRect(hwnd, out var client)) return;
            int w = client.right - client.left, h = client.bottom - client.top;
            // Nach exklusivem Vollbild kann Unity zunächst die alte Renderauflösung
            // behalten. Der tatsächliche randlose Client ist für die Pixeldeckung maßgeblich.
            if (w > 0 && h > 0 && (Screen.width != w || Screen.height != h))
                Screen.SetResolution(w, h, FullScreenMode.FullScreenWindow);
        }
    }
}
