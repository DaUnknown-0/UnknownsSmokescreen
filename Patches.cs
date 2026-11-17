// Unknown's Smokescreen - Copyright (C) 2026 DaUnknown-0. Licensed under GPL-3.0-or-later.
//
// The event-driven half of the mask (things that are not redrawn every frame). Each patch does nothing
// unless Shield.Active.

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TheOtherRoles;
using TheOtherRoles.Objects;
using UnityEngine;

namespace UnknownsSmokescreen;

/// <summary>Task list: TOR puts the role ("Sheriff: Shoot the Impostors") at the top as ImportantTextTasks.
/// Öffentlich erscheint stattdessen ausschließlich die Crewmate-Beschreibung.</summary>
internal static class TaskListMask
{
    private static string previous, result;

    public static string Masked(string str)
    {
        if (string.IsNullOrEmpty(str)) return CrewAppearance.TaskLine;
        try
        {
            var local = PlayerControl.LocalPlayer;
            if (str == previous) return result;
            previous = str;
            foreach (var t in local.myTasks)
            {
                var text = t != null ? t.TryCast<ImportantTextTask>() : null;
                if (text == null) continue;
                var sb = new Il2CppSystem.Text.StringBuilder();
                text.AppendTaskText(sb);
                string line = sb.ToString();
                if (!string.IsNullOrEmpty(line)) str = str.Replace(line, "");
            }
            str = CrewAppearance.TaskLine + "\n" + str.TrimStart('\r', '\n');
        }
        catch (Exception e)
        {
            SmokescreenPlugin.Logger.LogWarning($"task list: {e.Message}");
        }
        result = str;
        return str;
    }
}

// Flash-Coroutine bleibt echt, nur ihr Renderbild wird maskiert.
[HarmonyPatch(typeof(Helpers), nameof(Helpers.showFlash))]
internal static class FlashMask
{
    public static void Postfix()
    {
        if (Shield.Active && SmokescreenPlugin.HideFlashes.Value) EventVisuals.RegisterFlash();
    }
    public static void Prefix(Color color, float duration, string message)
    {
        if (Shield.Active && SmokescreenPlugin.HideFlashes.Value)
        {
            EventVisuals.FlashUntil = Mathf.Max(EventVisuals.FlashUntil, Time.time + duration);
        }
    }
}

/// <summary>Every TOR arrow (Tracker, Snitch, Vulture, Bounty Hunter ...) is registered for hiding.</summary>
[HarmonyPatch(typeof(Arrow), MethodType.Constructor, new[] { typeof(Color) })]
internal static class ArrowRegistry
{
    public static void Postfix(Arrow __instance) => Mask.Arrows.Add(__instance);
}

// --- intro ------------------------------------------------------------------------------------------------

// Die echte Intro-Logik bleibt privat erhalten; die Render-Maske verwendet immer Crew.
[HarmonyPatch]
internal static class IntroStart
{
    // Mehrere HarmonyPatch-Methodenattribute werden zusammengefuehrt, nicht als Liste gepatcht.
    public static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(IntroCutscene), nameof(IntroCutscene.CoBegin));
        yield return AccessTools.Method(typeof(IntroCutscene), nameof(IntroCutscene.ShowTeam));
        yield return AccessTools.Method(typeof(IntroCutscene), nameof(IntroCutscene.BeginImpostor));
        yield return AccessTools.Method(typeof(IntroCutscene), nameof(IntroCutscene.BeginCrewmate));
    }
    public static void Prefix(IntroCutscene __instance)
    {
        Mask.Intro = __instance;
        Shield.ResultScreen = false;
        Shield.Invalidate();
        if (!Shield.Active) return;
        Mask.SwapIntroSound();
    }
}

[HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.ShowRole))]
internal static class IntroRoleStart
{
    public static void Prefix(IntroCutscene __instance)
    {
        Mask.Intro = __instance;
        IntroPrivacy.RolePhase = true;
    }
}

[HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.OnDestroy))]
internal static class IntroEnd
{
    public static void Postfix()
    {
        IntroPrivacy.Reset();
        Mask.Intro = null;
        Mask.RestoreIntroSound();
    }
}

// Ergebnisbild gehoert unveraendert dem Spiel und TOR, auch bei einem Wechsel im Renderframe.
[HarmonyPatch(typeof(EndGameManager), nameof(EndGameManager.SetEverythingUp))]
internal static class ResultScreen
{
    [HarmonyPriority(Priority.First)]
    public static void Prefix()
    {
        CameraMaskScope.Restore();
        Shield.ResultScreen = true;
        Shield.Invalidate();
        OverlayContent.Reset();
    }
}

[HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.CreatePlayer))]
internal static class IntroPlayerCreated
{
    public static void Postfix(PoolablePlayer __result) => IntroPrivacy.Register(__result);
}

[HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.CoBegin))]
internal static class IntroReset
{
    [HarmonyPriority(Priority.First)]
    public static void Prefix() => IntroPrivacy.Reset();
}

// TOR fuehrt Rollenlogik in diesen Updates aus. Auch bei ausbleibendem Frameende
// werden die echten Werte vor diesen Patches wiederhergestellt.
[HarmonyPatch]
internal static class RestoreBeforeGameLogic
{
    public static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(HudManager), nameof(HudManager.Update));
        yield return AccessTools.Method(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate));
    }
    [HarmonyPriority(Priority.First)]
    public static void Prefix() => CameraMaskScope.RestoreBeforeLogic();
}
