// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnknownsSmokescreen;

// Die Kameramonitore von Skeld, Polus und Fungle lesen _MainTex.
// Die Materialwerte gelten nur während des privaten Kameradurchlaufs.
internal sealed class CameraScreens
{
    private readonly Dictionary<IntPtr, RenderTexture> targets = new();
    private readonly HashSet<IntPtr> current = new();
    internal void Begin() => current.Clear();
    private readonly List<(Material material, Texture original)> bindings = new();
    private static readonly int MainTexture = Shader.PropertyToID("_MainTex");

    internal RenderTexture Target(RenderTexture original)
    {
        current.Add(original.Pointer);
        if (targets.TryGetValue(original.Pointer, out var target)
            && RenderState.Alive(target) && target.width == original.width && target.height == original.height
            && target.graphicsFormat == original.graphicsFormat && target.IsCreated()) return target;
        if (RenderState.Alive(target)) UnityEngine.Object.Destroy(target);
        target = new RenderTexture(original.descriptor) { name = "SmokescreenPrivateCameraTarget", hideFlags = HideFlags.DontUnloadUnusedAsset };
        targets[original.Pointer] = target;
        if (!target.Create()) throw new InvalidOperationException("Privates Kameraziel fehlt");
        return target;
    }

    private readonly List<Renderer> consumers = new();

    private int cachedScene;
    private readonly List<Renderer> sceneConsumers = new();
    private readonly HashSet<IntPtr> cachedSources = new();
    private IntPtr cachedGame;
    private bool discovered;

    internal void Discover(RenderTexture[] originals)
    {
        var sources = new HashSet<IntPtr>();
        foreach (var source in originals)
            if (RenderState.Alive(source)) sources.Add(source.Pointer);
        int scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
        var game = Minigame.Instance;
        var gameId = RenderState.Alive(game) ? game.Pointer : IntPtr.Zero;
        if (discovered && scene == cachedScene && gameId == cachedGame && cachedSources.SetEquals(sources)) return;
        consumers.Clear();
        cachedSources.Clear();
        cachedSources.UnionWith(sources);
        cachedGame = gameId;
        foreach (var entry in targets)
            if (!sources.Contains(entry.Key))
            {
                if (RenderState.Alive(entry.Value)) UnityEngine.Object.Destroy(entry.Value);
                targets.Remove(entry.Key);
            }

        var known = new HashSet<IntPtr>();
        void Add(Renderer renderer)
        {
            if (RenderState.Alive(renderer) && known.Add(renderer.Pointer)) consumers.Add(renderer);
        }
        if (!discovered || scene != cachedScene)
        {
            cachedScene = scene;
            discovered = true;
            sceneConsumers.Clear();
            foreach (var shadow in UnityEngine.Object.FindObjectsOfType<ShadowCollab>())
                sceneConsumers.Add(shadow.ShadowQuad);
            foreach (var renderer in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                var material = renderer.sharedMaterial;
                if (RenderState.Alive(material) && material.HasProperty(MainTexture)
                    && material.GetTexture(MainTexture)?.TryCast<RenderTexture>() != null)
                    sceneConsumers.Add(renderer);
            }
        }
        foreach (var renderer in sceneConsumers) Add(renderer);
        if (RenderState.Alive(game))
        {
            if (game.TryCast<SurveillanceMinigame>() is { } skeld)
                foreach (var port in skeld.ViewPorts) Add(port);
            else if (game.TryCast<PlanetSurveillanceMinigame>() is { } planet) Add(planet.ViewPort);
            else if (game.TryCast<FungleSurveillanceMinigame>() is { } fungle) Add(fungle.viewport);
        }
        if (!RenderState.Alive(game)) return;
        foreach (var renderer in game.GetComponentsInChildren<Renderer>(true))
        {
            var material = renderer.sharedMaterial;
            if (!RenderState.Alive(material) || !material.HasProperty(MainTexture)) continue;
            var texture = material.GetTexture(MainTexture);
            if (RenderState.Alive(texture) && sources.Contains(texture.Pointer)) Add(renderer);
        }
    }

    internal void Bind()
    {
        if (current.Count == 0) return;
        foreach (var renderer in consumers) Bind(renderer);
    }

    private void Bind(Renderer renderer)
    {
        if (!RenderState.Alive(renderer)) return;
        var material = renderer.sharedMaterial;
        if (!RenderState.Alive(material) || !material.HasProperty(MainTexture)) return;
        var original = material.GetTexture(MainTexture);
        if (!RenderState.Alive(original) || !current.Contains(original.Pointer) || !targets.TryGetValue(original.Pointer, out var target)) return;
        bindings.Add((material, original));
        material.SetTexture(MainTexture, target);
    }

    internal void Restore()
    {
        foreach (var (material, original) in bindings)
            if (RenderState.Alive(material)) material.SetTexture(MainTexture, original);
        bindings.Clear();
    }

    internal void Clear()
    {
        Restore();
        foreach (var target in targets.Values)
            if (RenderState.Alive(target)) UnityEngine.Object.Destroy(target);
        targets.Clear();
    }
}
