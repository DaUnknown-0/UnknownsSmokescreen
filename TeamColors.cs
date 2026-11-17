// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
namespace UnknownsSmokescreen;

internal static class TeamColors
{
    private static readonly List<Action> refresh = new();
    private static bool initialized;
    internal static void Refresh()
    {
        if (!initialized)
        {
            initialized = true;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name != "UnknownsCollection") continue;
                Type[] types;
                try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types; }
                foreach (var type in types)
                {
                    if (type == null || type.Namespace != "UnknownsCollection") continue;
                    var method = type.GetMethod("NameColorTick", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                    if (method?.ReturnType != typeof(void)) continue;
                    try { refresh.Add((Action)method.CreateDelegate(typeof(Action))); } catch { }
                }
            }
            SmokescreenPlugin.Logger.LogInfo($"Native Teamfarben: {refresh.Count} optionale Namensadapter geladen");
        }
        // Die Module prüfen selbst Mitgliedschaft und Sichtbarkeit. Keine erfundenen Gruppenzugehörigkeiten.
        foreach (var action in refresh) action();
    }
}
internal static class HudNotice
{
    private static bool seen;
    private static float until;
    internal static string Append(string text)
    {
        if (OverlayHost.ProtectionFailed && !seen) { seen = true; until = Time.realtimeSinceStartup + 8f; }
        return seen && Time.realtimeSinceStartup < until ? text + "\n<size=65%><color=#B4BCC8>Anzeige nicht verfügbar</color></size>" : text;
    }
}
