// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using System;
using System.Diagnostics;
using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace UnknownsSmokescreen;

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
    private static RenderTexture real;
    private static bool releaseAfterFrame;
    private static Texture2D realCpu, maskedCpu;
    private static int width, height;
    private static float next, metricsAt;
    private static int frames;
    private static double renderMs;
    private static bool visible = true;
    private static bool diagnostic;
    private static bool diagnosticInitialized;
    private static float diagnosticEnd;
    internal static long PublishedFrames;
    internal static double LastFrameMilliseconds;
    internal static void Reset() { next = 0; }

    internal static void Tick(bool active)
    {
        if (shutdown) return;
        BorderlessMode.Tick(Shield.Wanted && SmokescreenPlugin.PrivateOverlay.Value);
        if (Input.GetKeyDown(SmokescreenPlugin.OverlayHotkey.Value) && OverlayHost.GameForeground)
            visible = !visible;
        // Das letzte private Bild erst nach dem ersten unmaskierten Bildschirm freigeben.
        releaseAfterFrame = !active;
        if (!active) return;
        OverlayHost.SetVisible(visible, true);
        IntroPrivacy.Prepare(Mask.Intro);
        TeamColors.Refresh();
        DiscoverCameras();
        if (!OverlayHost.CanRender || Time.realtimeSinceStartup < next) return;
        next = Time.realtimeSinceStartup + 1f / SmokescreenPlugin.OverlayFps.Value;
        var watch = Stopwatch.StartNew();
        try
        {
            int w = Screen.width, h = Screen.height;
            // Beide Fassungen unmittelbar nacheinander, ohne Update oder Backbuffer-Lesen.
            if (w < 1 || h < 1 || (long)w * h > 3840L * 2160) return;
            Ensure(w, h);
            Rendering = true;
            Render(real);
            Read(real, realCpu);
            try
            {
                Mask.Apply(); RenderState.Flush();
                Render(real);
                Read(real, maskedCpu);
            }
            finally { Mask.RestoreAll(); }
            var r = realCpu.GetWritableImageData(0);
            var m = maskedCpu.GetWritableImageData(0);
            if (r == IntPtr.Zero || m == IntPtr.Zero) throw new InvalidOperationException("CPU-Texturpuffer fehlt");
            if (diagnostic && PublishedFrames % 90 == 0) OverlayHost.RequestDiagnostic("hud");
            OverlayHost.Publish(r, m, width, height, RenderState.Alive(Mask.Intro) || RenderState.Alive(MeetingHud.Instance));
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
            SmokescreenPlugin.Logger.LogInfo($"HUD: {width}x{height}, Bilder={PublishedFrames}, Render/Kopie={renderMs / Math.Max(1, frames):F2} ms, Prozess={process.PrivateMemorySize64 / 1048576} MiB, Readback=ReadPixels, ExtraRender=2, Paar=Synchron, Masken={CameraMaskScope.Applications}, Restore={CameraMaskScope.Restores}, ApplyGesamt={CameraMaskScope.ApplyMilliseconds:F2} ms");
            renderMs = 0; frames = 0; metricsAt = Time.realtimeSinceStartup + 15;
        }
        if (diagnostic && Time.realtimeSinceStartup > diagnosticEnd) { diagnostic = false; OverlayHost.EndDiagnostic(); }
    }
    internal static void CompleteFrame()
    {
        if (!releaseAfterFrame || Shield.Active || CameraMaskScope.LastMaskedFrame == Time.frameCount) return;
        releaseAfterFrame = false;
        OverlayHost.SetVisible(false);
        OverlayHost.Discard();
        ReleaseImages();
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
        if (!changed) return;
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
    }
    private static IntPtr Destination(Camera camera)
    {
        var target = camera.targetTexture;
        return RenderState.Alive(target) ? target.Pointer : IntPtr.Zero;
    }
    private static void Ensure(int w, int h)
    {
        if (width == w && height == h && RenderState.Alive(real)) return;
        ReleaseImages(); width = w; height = h;
        if (!RenderState.Alive(copy))
        {
            var go = new GameObject("SmokescreenPrivateCamera");
            UnityEngine.Object.DontDestroyOnLoad(go);
            copy = go.AddComponent<Camera>(); copy.enabled = false;
        }
        real = Target("SmokescreenReal", w, h);
        realCpu = Cpu(w, h); maskedCpu = Cpu(w, h);
        if (!diagnosticInitialized)
        {
            diagnosticInitialized = true;
            diagnostic = SmokescreenPlugin.DiagnosticMode.Value || Array.IndexOf(NativeArguments.Values, "--smokescreen-diagnostics") >= 0;
            diagnosticEnd = Time.realtimeSinceStartup + 180f;
        }
    }
    private static RenderTexture Target(string name, int w, int h)
    {
        var t = new RenderTexture(w,h,24, RenderTextureFormat.ARGB32) { name = name, hideFlags = HideFlags.DontUnloadUnusedAsset, antiAliasing = 1 };
        t.Create(); return t;
    }
    private static Texture2D Cpu(int w, int h) => new(w,h,TextureFormat.RGBA32,false) { name = "SmokescreenReadback", hideFlags = HideFlags.DontUnloadUnusedAsset };
    private static void Render(RenderTexture target)
    {
        var old = RenderTexture.active;
        try
        {
            RenderTexture.active = target; GL.Clear(true, true, Color.clear);
            foreach (var c in cameras)
            {
                if (!RenderState.Alive(c) || !c.enabled || !c.gameObject.activeInHierarchy) continue;
                copy.CopyFrom(c); copy.enabled = false;
                copy.transform.SetPositionAndRotation(c.transform.position, c.transform.rotation);
                var destination = c.targetTexture;
                copy.targetTexture = RenderState.Alive(destination) ? destination : target;
                copy.Render();
            }
        }
        finally { copy.targetTexture = null; RenderTexture.active = old; }
    }
    private static void Read(RenderTexture target, Texture2D cpu)
    {
        var old = RenderTexture.active;
        try { RenderTexture.active = target; cpu.ReadPixels(new Rect(0,0,width,height),0,0,false); }
        finally { RenderTexture.active = old; }
    }
    private static void ReleaseImages()
    {
        if (RenderState.Alive(copy)) copy.targetTexture = null;
        if (RenderState.Alive(real)) { real.Release(); UnityEngine.Object.Destroy(real); }
        if (RenderState.Alive(realCpu)) UnityEngine.Object.Destroy(realCpu);
        if (RenderState.Alive(maskedCpu)) UnityEngine.Object.Destroy(maskedCpu);
        real = null; realCpu = maskedCpu = null; width = height = 0;
    }
    internal static void Dispose()
    {
        shutdown = true;
        releaseAfterFrame = false;
        CameraMaskScope.Restore();
        ReleaseImages();
        if (RenderState.Alive(copy)) UnityEngine.Object.Destroy(copy.gameObject);
        copy = null;
        foreach (var camera in cameras)
        {
            if (!RenderState.Alive(camera)) continue;
            var scope = camera.GetComponent<CameraMaskScope>();
            if (RenderState.Alive(scope)) UnityEngine.Object.Destroy(scope);
        }
        cameras = Array.Empty<Camera>();
        observed = Array.Empty<Camera>(); depths = Array.Empty<float>(); destinations = Array.Empty<IntPtr>();
        cameraBuffer = null;
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
