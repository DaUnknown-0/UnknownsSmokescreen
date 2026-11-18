// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnknownsSmokescreen;

/// <summary>Optionale Reflection-Schnittstelle für private Kameraelemente. Auf dem Unity-Hauptthread verwenden.</summary>
public static class PrivateVisuals
{
    public const int ApiVersion = 1;
    public static bool IsMaskActive => SettingsMenu.PatchedAtStartup && Protection.PatchesReady
        && !OverlayContent.Stopped && Shield.Active;

    private sealed record Visual(Object Target, string PublicText);
    private static readonly Dictionary<long, Visual> visuals = new();
    private static readonly List<long> destroyed = new();
    private static long lastToken;
    internal static int Revision { get; private set; }

    public static long RegisterRenderer(Renderer renderer) => Add(renderer, null);
    public static long RegisterTree(GameObject root) => Add(root, null);
    public static long RegisterText(TextMeshPro text, string publicText) => Add(text, publicText);

    public static bool Unregister(long token)
    {
        if (!visuals.Remove(token)) return false;
        Revision++;
        return true;
    }

    private static long Add(Object target, string publicText)
    {
        if (!RenderState.Alive(target) || target == null) return 0;
        long token = ++lastToken;
        visuals.Add(token, new Visual(target, publicText));
        Revision++;
        return token;
    }

    internal static void Apply()
    {
        foreach (var (token, visual) in visuals)
        {
            if (!RenderState.Alive(visual.Target) || visual.Target == null)
            {
                destroyed.Add(token);
                continue;
            }
            switch (visual.Target)
            {
                case Renderer renderer:
                    Mask.HideRenderer(renderer);
                    break;
                case GameObject root:
                    Mask.HideLiveTree(root);
                    break;
                case TextMeshPro text when visual.PublicText == null:
                    Mask.HideLiveTree(text.gameObject);
                    break;
                case TextMeshPro text:
                    RenderState.PublicText(text, visual.PublicText, Color.white, true);
                    break;
            }
        }
        foreach (long token in destroyed) Unregister(token);
        destroyed.Clear();
    }

    internal static void Clear()
    {
        visuals.Clear();
        destroyed.Clear();
        Revision++;
    }
}
