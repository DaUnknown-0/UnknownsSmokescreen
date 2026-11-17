// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using UnityEngine;
using UnityEngine.SceneManagement;
using Il2CppSystem.Collections.Generic;
namespace UnknownsSmokescreen;

internal static class SceneRoots
{
    internal static void Fill(Scene scene, List<GameObject> roots)
    {
        // Die Array-Hilfsmethode ist im Spiel entfernt. Ihre nativen internen
        // Aufrufe sind vorhanden und benötigen nur eine wiederverwendete Liste.
        int count = Scene.GetRootCountInternal(scene.m_Handle);
        roots.Clear();
        if (roots.Capacity < count) roots.Capacity = count;
        Scene.GetRootGameObjectsInternal(scene.m_Handle, roots);
    }
}
