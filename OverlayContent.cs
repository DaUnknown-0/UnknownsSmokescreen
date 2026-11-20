// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppInterop.Runtime;

namespace UnknownsSmokescreen;

internal static class OverlayContent
{
    internal static bool Rendering;
    private static bool shutdown;
    internal static bool Stopped => shutdown;
    private static Camera copy;
    private static readonly CameraScreens screens = new();
    private static readonly OverlayCanvases canvases = new();
    private static RenderTexture[] cameraTargets = Array.Empty<RenderTexture>();
    private static Camera[] cameras = Array.Empty<Camera>();
    private static Camera[] observed = Array.Empty<Camera>();
    private static float[] depths = Array.Empty<float>();
    private static int[] cameraMasks = Array.Empty<int>();
    private static IntPtr[] destinations = Array.Empty<IntPtr>();
    private static Il2CppReferenceArray<Camera> cameraBuffer;
    private static Camera mainCamera;
    private static readonly IntPtr targetGetter = IL2CPP.il2cpp_class_get_method_from_name(
        Il2CppClassPointerStore<Camera>.NativeClassPtr, "get_targetTexture", 0);
    private static int screenLayers;
    internal static bool OnScreenLayer(int layer) => (screenLayers & (1 << layer)) != 0;
    internal static IntPtr ActiveMinigame
    {
        get
        {
            var minigame = Minigame.Instance;
            return RenderState.Alive(minigame) && minigame.gameObject.activeInHierarchy ? minigame.Pointer : IntPtr.Zero;
        }
    }
    internal static readonly bool DirectPresentation = SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Direct3D11;
    private static RenderTexture stage;
    private static Transform copyTransform;
    private static Transform[] poses = Array.Empty<Transform>();
    private static float retryAt, logAt;
    private static int retries;
    private static bool warnedBackend;
    private static RenderTexture publicProbe;
    private static IntPtr publicProbeTexture;
    private static IntPtr nativeTexture;
    private static int width, height, privateFrame = -1, privateEpoch;
    private static bool releaseAfterFrame, wasVisible;
    private static float nextFeed, metricsAt;
    private static readonly HudVisibility visibility = new();
    private static bool diagnostic, diagnosticInitialized, diagnosticConfig;
    private static float diagnosticEnd;
    private static string probe;
    private static int probes;
    internal static long PublishedFrames;
    internal static double LastFrameMilliseconds;
    internal static void Reset() { nextFeed = 0; retries = 0; retryAt = 0; }
    internal static void CancelHold()
    {
        visibility.CancelHold();
        if (SmokescreenPlugin.HoldHud.Value) OverlayHost.HidePrivate();
    }

    internal static void Tick(bool active)
    {
        if (shutdown) return;
        BorderlessMode.Tick(Shield.Wanted && SmokescreenPlugin.PrivateOverlay.Value);
        var key = SmokescreenPlugin.OverlayHotkey.Value;
        bool holdMode = SmokescreenPlugin.HoldHud.Value;
        bool down = Input.GetKeyDown(key);
        bool pressed = holdMode && Input.GetKey(key);
        bool acceptsKey = (down || pressed) && OverlayHost.GameForeground && !SettingsMenu.TakesHotkeys;
        bool visible = visibility.Update(holdMode, active, acceptsKey, down, pressed);
        // Das letzte private Bild erst nach dem ersten unmaskierten Bildschirm freigeben.
        releaseAfterFrame = !active && (wasVisible || stage is not null);
        FrameReadback.PrivateProbe.Tick(true);
        FrameReadback.PublicProbe.Tick(true);
        FrameReadback.Feed.Tick(active && OverlayHost.FeedWanted);
        FrameReadback.Replay.Tick(active && SmokescreenPlugin.ReplayEnabled);
        if (!active) return;
        InitializeDiagnostics();
        if (visible && !wasVisible)
        {
            OverlayHost.Discard();
            nextFeed = 0;
        }
        wasVisible = visible;
        OverlayHost.SetVisible(visible, true);
        IntroPrivacy.Prepare(Mask.Intro);
        TeamColors.Refresh();
        DiscoverCameras();
        try { VisionMask.Prepare(); }
        catch (Exception) { VisionMask.Ready = false; }
        if (diagnostic && Time.realtimeSinceStartup > diagnosticEnd)
        {
            diagnostic = false;
            OverlayHost.DiagnosticCapture = false;
            OverlayHost.EndDiagnostic();
        }
        float now = Time.realtimeSinceStartup;
        if (!DirectPresentation && !warnedBackend)
        {
            warnedBackend = true;
            SmokescreenPlugin.Logger.LogWarning("Private Bildausgabe benötigt D3D11.");
        }
        if (GpuPresenter.Error != 0 && retries < 3 && now >= retryAt)
        {
            retries++;
            retryAt = now + 2;
            OverlayHost.Discard();
            SmokescreenPlugin.Logger.LogWarning($"Private GPU-Ausgabe 0x{GpuPresenter.Error:X8}, Neuaufbau {retries}/3");
            ResetOutput();
        }
        if (diagnostic && now >= logAt)
        {
            logAt = now + 15;
            SmokescreenPlugin.Logger.LogInfo($"HUD-Diagnose Kameras={cameras.Length} PrivatCPUms={LastFrameMilliseconds:F3} MaskenCPUmsGesamt={CameraMaskScope.ApplyMilliseconds:F3} Ausgaben={GpuPresenter.Presented} Ausgelassen={GpuPresenter.Missed} HRESULT=0x{GpuPresenter.Error:X8}");
        }
    }

    // Unmittelbar vor der ersten öffentlichen Kameramaske. Kein Update zwischen den Stapeln.
    internal static void BeforeMask()
    {
        bool feed = OverlayHost.FeedWanted && Time.realtimeSinceStartup >= nextFeed;
        if (!DirectPresentation || !RenderState.Alive(mainCamera)) return;
        bool privateWanted = !CaptureSelfTest.Running && OverlayHost.PrivateWanted && GpuPresenter.Error == 0
            && (CaptureSelfTest.Approved || (OverlayHost.DiagnosticCapture && !OverlayHost.GameForeground));
        bool replay = SmokescreenPlugin.ReplayEnabled && DelayedRecording.TryBeginSample();
        if (!privateWanted && !feed && !replay) return;
        long start = Stopwatch.GetTimestamp();
        var previousTarget = RenderTexture.active;
        try
        {
            Rendering = true;
            Ensure(Screen.width, Screen.height);

            if (feed || replay)
            {
                RenderFull(stage);
                if (feed)
                {
                    nextFeed = Time.realtimeSinceStartup + 1f / SmokescreenPlugin.OverlayFps.Value;
                    FrameReadback.Feed.Request(nativeTexture);
                }
                if (replay) CaptureDelayed();
            }
            if (!privateWanted) return;
            privateEpoch = OverlayHost.Epoch;
            if (!(feed || replay) || VisionMask.Different)
            {
                // Der unmittelbar folgende öffentliche Pass nutzt denselben Radius.
                VisionMask.Apply();
                RenderFull(stage);
            }
            privateFrame = Time.frameCount;
            PublishedFrames++;
        }
        catch (Exception e) { Failed(e); }
        finally
        {
            Rendering = false;
            RenderTexture.active = previousTarget;
            LastFrameMilliseconds = (Stopwatch.GetTimestamp() - start) * (1000.0 / Stopwatch.Frequency);
        }
    }

    internal static void RestorePublicBindings()
    {
        try { canvases.Restore(); }
        finally { screens.Restore(); }
    }

    internal static void CompleteFrame()
    {
        if (privateFrame == Time.frameCount && CameraMaskScope.LastMaskedFrame == privateFrame && OverlayHost.PrivateWanted)
        {
            GpuPresenter.Submit(nativeTexture, width, height, privateEpoch);
            if (diagnostic && PublishedFrames % 90 == 0) RequestProbe("hud");
            if (probe != null)
            {
                string label = probe;
                probe = null;
                try
                {
                    var dir = OverlayHost.DiagnosticDirectory;
                    if (dir != null && FrameReadback.PrivateProbe.Available && FrameReadback.PublicProbe.Available)
                    {
                        Directory.CreateDirectory(dir);
                        if (!RenderState.Alive(publicProbe))
                        {
                            publicProbe = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
                            publicProbe.Create();
                            publicProbeTexture = Retain(publicProbe);
                        }
                        string prefix = Path.Combine(dir, label + "_" + privateFrame);
                        ScreenCapture.CaptureScreenshotIntoRenderTexture(publicProbe);
                        FrameReadback.PrivateProbe.Request(nativeTexture, prefix + "_real.bmp");
                        FrameReadback.PublicProbe.Request(publicProbeTexture, prefix + "_masked.bmp");
                        probes++;
                    }
                }
                catch (Exception e) { Failed(e); }
            }
        }
        if (!releaseAfterFrame || Shield.Active || CameraMaskScope.LastMaskedFrame == Time.frameCount) return;
        releaseAfterFrame = false;
        wasVisible = false;
        OverlayHost.SetVisible(false);
        OverlayHost.Discard();
        ReleaseImages();
    }

    internal static void RequestProbe(string label)
    {
        if (diagnostic && probes < 48) probe = Path.GetFileName(label);
    }
    private static void Failed(Exception e)
    {
        OverlayHost.Discard();
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

    private static readonly System.Collections.Generic.HashSet<int> inspected = new();

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
            var c = observed[i];
            // Il2CppReferenceArray verwendet vier native Zeiger vor dem ersten Element.
            var pointer = System.Runtime.InteropServices.Marshal.ReadIntPtr(cameraBuffer.Pointer, (4 + i) * IntPtr.Size);
            if (!RenderState.Alive(c) || c.Pointer != pointer
                || c.depth != depths[i] || c.cullingMask != cameraMasks[i] || Destination(c) != destinations[i]) changed = true;
        }
        if (!changed && RenderState.Alive(mainCamera)) return;
        canvases.Invalidate();
        observed = new Camera[count]; depths = new float[count]; destinations = new IntPtr[count];
        cameraMasks = new int[count];
        nextFeed = 0;
        var list = new System.Collections.Generic.List<Camera>(count);
        for (int i = 0; i < count; i++)
        {
            var c = cameraBuffer[i];
            observed[i] = c;
            if (!RenderState.Alive(c) || c.Pointer == copy?.Pointer) continue;
            depths[i] = c.depth; destinations[i] = Destination(c);
            cameraMasks[i] = c.cullingMask;
            if (!RenderState.Alive(c.GetComponent<CameraMaskScope>())) c.gameObject.AddComponent<CameraMaskScope>();
            if (inspected.Add(c.GetInstanceID()))
            {
                if (c.commandBufferCount != 0)
                    SmokescreenPlugin.Logger.LogWarning($"Kamera {c.name}: Eigene CommandBuffer fehlen im Privatbild");
                foreach (var component in c.GetComponents<MonoBehaviour>())
                    if (RenderState.Alive(component) && IL2CPP.il2cpp_class_get_method_from_name(
                        IL2CPP.il2cpp_object_get_class(component.Pointer), "OnRenderImage", 2) != IntPtr.Zero)
                        SmokescreenPlugin.Logger.LogWarning($"Kamera {c.name}: OnRenderImage auf {component.GetIl2CppType().Name} fehlt im Privatbild");
            }
            list.Add(c);
        }
        // Texturkameras liefern unter anderem Überwachungsbilder an das Bildschirm-HUD.
        // Beide Fassungen müssen ihre eigenen aktuellen Texturinhalte verwenden.
        list.Sort((a,b) => {
            int target = (Destination(a) == IntPtr.Zero ? 1 : 0).CompareTo(Destination(b) == IntPtr.Zero ? 1 : 0);
            return target != 0 ? target : a.depth.CompareTo(b.depth);
        });
        cameras = list.ToArray();
        poses = Array.ConvertAll(cameras, camera => camera.transform);
        cameraTargets = Array.ConvertAll(cameras, camera => camera.targetTexture);
        screens.Discover(cameraTargets);
        ChooseCameras();
    }

    private static void ChooseCameras()
    {
        mainCamera = null;
        screenLayers = 0;
        foreach (var camera in cameras)
        {
            if (Destination(camera) != IntPtr.Zero) continue;
            mainCamera ??= camera;
            screenLayers |= camera.cullingMask;
        }
    }

    private static unsafe IntPtr Destination(Camera camera)
    {
        if (targetGetter != IntPtr.Zero)
        {
            IntPtr exception = IntPtr.Zero;
            IntPtr pointer = IL2CPP.il2cpp_runtime_invoke(targetGetter, camera.Pointer, null, ref exception);
            Il2CppException.RaiseExceptionIfNecessary(exception);
            return pointer;
        }
        var target = camera.targetTexture;
        return RenderState.Alive(target) ? target.Pointer : IntPtr.Zero;
    }
    private static void Ensure(int w, int h)
    {
        if (width == w && height == h && RenderState.Alive(stage) && stage.IsCreated()) return;
        ReleaseImages();
        width = w; height = h;
        EnsureCameraCopy();
        stage = new RenderTexture(w,h,24,RenderTextureFormat.ARGB32)
            { name = "SmokescreenPrivate", hideFlags = HideFlags.DontUnloadUnusedAsset, antiAliasing = 1 };
        if (!stage.Create()) throw new InvalidOperationException("Privates Renderziel fehlt");
        nativeTexture = DirectPresentation ? Retain(stage) : IntPtr.Zero;
    }

    private static void EnsureCameraCopy()
    {
        if (RenderState.Alive(copy)) return;
        var go = new GameObject("SmokescreenPrivateCamera");
        UnityEngine.Object.DontDestroyOnLoad(go);
        copy = go.AddComponent<Camera>();
        copy.enabled = false;
        copyTransform = go.transform;
    }

    private static RenderTexture replayTarget;
    private static IntPtr replayTexture;

    private static void CaptureDelayed()
    {
        try
        {
            if (Screen.width < 1 || Screen.height < 1) return;
            float scale = Math.Min((float)DelayedRecording.MaxWidth / Screen.width, (float)DelayedRecording.MaxHeight / Screen.height);
            int w = Math.Max(1, (int)(Screen.width * scale)), h = Math.Max(1, (int)(Screen.height * scale));
            if (!RenderState.Alive(replayTarget) || !replayTarget.IsCreated() || replayTarget.width != w || replayTarget.height != h)
            {
                ReleaseReplay();
                replayTarget = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32)
                    { name = "SmokescreenReplay", hideFlags = HideFlags.DontUnloadUnusedAsset };
                replayTarget.Create();
                replayTexture = DirectPresentation ? Retain(replayTarget) : IntPtr.Zero;
            }
            Graphics.Blit(stage, replayTarget);
            FrameReadback.Replay.Request(replayTexture);
        }
        catch (Exception e) { Failed(e); }
    }

    internal static void ReleaseReplay()
    {
        if (RenderState.Alive(replayTarget))
        {
            replayTarget.Release();
            UnityEngine.Object.Destroy(replayTarget);
        }
        FrameReadback.Replay.Invalidate();
        replayTarget = null;
        Drop(ref replayTexture);
    }
    private static void CopyPose(Transform source)
    {
        source.get_position_Injected(out var position);
        source.get_rotation_Injected(out var rotation);
        copyTransform.SetPositionAndRotation(position, rotation);
    }

    private static void RenderFull(RenderTexture target)
    {
        bool first = true;
        int last = cameras.Length - 1;
        while (last >= 0 && (RenderState.Alive(cameraTargets[last]) || !RenderState.Alive(cameras[last]) || !cameras[last].isActiveAndEnabled)) last--;
        screens.Begin();
        try
        {
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera c = cameras[i];
                if (!RenderState.Alive(c) || !c.isActiveAndEnabled) continue;
                copy.CopyFrom(c);
                copy.enabled = false;
                CopyPose(poses[i]);
                bool offscreen = RenderState.Alive(cameraTargets[i]);
                copy.targetTexture = offscreen ? screens.Target(cameraTargets[i]) : target;
                if (!offscreen && first) screens.Bind();
                if ((offscreen || first) && copy.clearFlags != CameraClearFlags.SolidColor && copy.clearFlags != CameraClearFlags.Skybox)
                {
                    copy.clearFlags = CameraClearFlags.SolidColor;
                    copy.backgroundColor = Color.black;
                }
                if (!offscreen && i == last) canvases.Bind(copy);
                if (!offscreen) first = false;
                copy.Render();
            }
            if (first) throw new InvalidOperationException("Keine aktive Bildschirmkamera");
        }
        finally
        {
            try { RestorePublicBindings(); }
            finally { copy.targetTexture = null; }
        }
    }

    private static IntPtr Retain(RenderTexture target)
    {
        var pointer = target.GetNativeTexturePtr();
        if (pointer == IntPtr.Zero) throw new InvalidOperationException("D3D11-Textur fehlt");
        System.Runtime.InteropServices.Marshal.AddRef(pointer);
        return pointer;
    }

    private static void Drop(ref IntPtr pointer)
    {
        if (pointer != IntPtr.Zero) System.Runtime.InteropServices.Marshal.Release(pointer);
        pointer = IntPtr.Zero;
    }

    internal static void ResetOutput()
    {
        ReleaseReplay();
        ReleaseImages(false);
        GpuPresenter.Reset();
    }

    private static void ReleaseImages(bool resetPresenter = true)
    {
        privateFrame = -1;
        screens.Clear();
        FrameReadback.Feed.Invalidate();
        if (nativeTexture != IntPtr.Zero && resetPresenter) GpuPresenter.Reset();
        Drop(ref nativeTexture);
        Drop(ref publicProbeTexture);
        if (RenderState.Alive(stage)) { stage.Release(); UnityEngine.Object.Destroy(stage); }
        if (RenderState.Alive(publicProbe)) { publicProbe.Release(); UnityEngine.Object.Destroy(publicProbe); }
        stage = null; publicProbe = null; publicProbeTexture = IntPtr.Zero; width = height = 0;
    }
    internal static void Dispose()
    {
        Mask.RestoreAll();
        RenderState.ReleaseNeutralMaterials();
        ReleaseReplay();
        shutdown = true;
        FrameReadback.Close();
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
        cameraBuffer = null; mainCamera = null;
        BorderlessMode.Tick(false);
    }
}

internal sealed class HudVisibility
{
    private bool toggledVisible = true, held;

    internal bool Update(bool holdMode, bool active, bool acceptsKey, bool down, bool pressed)
    {
        if (!holdMode && acceptsKey && down) toggledVisible = !toggledVisible;
        held = holdMode && active && acceptsKey && pressed && (held || down);
        return holdMode ? held : toggledVisible;
    }

    internal void CancelHold() => held = false;
}

internal static class BorderlessMode
{
    private static bool changed;
    private static int width, height;
    private static FullScreenMode mode;
    private static float synchronizeAt;
    internal static void Tick(bool wanted)
    {
        if (wanted && Screen.fullScreenMode == FullScreenMode.ExclusiveFullScreen
            && Time.realtimeSinceStartup >= synchronizeAt)
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
    }
}
