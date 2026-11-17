// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using System;
using System.Collections.Generic;
using HarmonyLib;
using TheOtherRoles;
using UnityEngine;

namespace UnknownsSmokescreen;

internal static class RoleAudio
{
    // Aus TOR-Aufrufern in Buttons, RPC, Trap und UsablesPatch. Oeffentliche Bomben und Alarme bleiben.
    private static readonly HashSet<string> names = new(StringComparer.OrdinalIgnoreCase) {
        "engineerRepair", "shifterShift", "morphlingSample", "morphlingMorph", "medicShield", "hackerHack", "trackerTrackPlayer",
        "trackerTrackCorpses", "vampireBite", "garlic", "tricksterPlaceBox", "portalUse",
        "jackalSidekick", "ligherLight", "lighterLight", "eraserErase", "cleanerClean",
        "warlockCurse", "securityGuardPlaceCam", "arsonistDouse", "vultureEat", "mediumAsk",
        "pursuerBlank", "witchSpell", "trapperTrap", "timemasterShield", "deputyHandcuff", "tricksterUseBoxVent"
    };
    internal static int Suppressed;
    internal static bool Block(string path)
    {
        if (!Shield.Active || !SmokescreenPlugin.NeutralAudio.Value || path == null) return false;
        string key = path.Replace('\\', '/');
        key = key.Substring(key.LastIndexOf('/') + 1);
        if (key.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase)) key = key.Substring(0, key.Length - 4);
        if (!names.Contains(key)) return false;
        Suppressed++;
        return true;
    }
}

[HarmonyPatch(typeof(SoundEffectsManager), nameof(SoundEffectsManager.play))]
internal static class PrivateRoleSound
{
    static bool Prefix(string path, ref AudioSource __result)
    {
        if (!RoleAudio.Block(path)) return true;
        __result = null;
        return false;
    }
}

[HarmonyPatch(typeof(SoundEffectsManager), nameof(SoundEffectsManager.playAtPosition))]
internal static class PrivatePositionSound
{
    static bool Prefix(string path) => !RoleAudio.Block(path);
}
