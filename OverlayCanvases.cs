// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using System.Collections.Generic;
using UnityEngine;

namespace UnknownsSmokescreen;

internal sealed class OverlayCanvases
{
    private readonly List<(Canvas canvas, Camera camera, float distance)> changed = new();

    private readonly List<Canvas> roots = new();
    private int refreshAt;
    internal void Invalidate() => refreshAt = 0;

    // Overlay-Canvas werden nach der letzten Bildschirmkamera gezeichnet.
    // Für den manuellen Durchlauf an diese Kamera binden und danach sofort zurückstellen.
    internal void Bind(Camera camera)
    {
        if (Time.frameCount >= refreshAt)
        {
            roots.Clear();
            foreach (var canvas in Object.FindObjectsOfType<Canvas>(true))
                if (canvas.isRootCanvas) roots.Add(canvas);
            refreshAt = Time.frameCount + 120;
        }
        foreach (var canvas in roots)
        {
            if (!RenderState.Alive(canvas) || !canvas.isActiveAndEnabled || !canvas.isRootCanvas || canvas.targetDisplay != camera.targetDisplay
                || canvas.renderMode != RenderMode.ScreenSpaceOverlay) continue;
            changed.Add((canvas, canvas.worldCamera, canvas.planeDistance));
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = Mathf.Lerp(camera.nearClipPlane, camera.farClipPlane, 0.001f);
            camera.cullingMask |= 1 << canvas.gameObject.layer;
        }
        if (changed.Count > 0) Canvas.ForceUpdateCanvases();
    }

    internal void Restore()
    {
        if (changed.Count == 0) return;
        foreach (var (canvas, camera, distance) in changed)
        {
            if (!RenderState.Alive(canvas)) continue;
            canvas.worldCamera = camera;
            canvas.planeDistance = distance;
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }
        changed.Clear();
        Canvas.ForceUpdateCanvases();
    }
}
