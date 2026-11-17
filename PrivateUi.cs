// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace UnknownsSmokescreen;

internal static class PrivateUi
{
    private static readonly FieldInfo guesser = AccessTools.Field(AccessTools.TypeByName("TheOtherRoles.Patches.MeetingHudPatch"), "guesserUI");
    private static readonly List<GameObject> voteAreas = new();
    private static MeetingHud guesserMeeting;
    internal static bool GuesserOpen => RenderState.Alive(guesser?.GetValue(null) as GameObject);

    internal static void RememberPublicVotes(MeetingHud meeting)
    {
        if (!RenderState.Alive(meeting) || GuesserOpen) return;
        guesserMeeting = meeting;
        voteAreas.Clear();
        foreach (var area in meeting.playerStates)
            if (RenderState.Alive(area) && area.gameObject.activeSelf) voteAreas.Add(area.gameObject);
    }

    internal static void ShowPublicVotes(MeetingHud meeting)
    {
        if (!SmokescreenPlugin.PrivateMeetings.Value || meeting != guesserMeeting || !GuesserOpen) return;
        foreach (var area in voteAreas)
            if (RenderState.Alive(area)) RenderState.Active(area, true);
    }

    internal static void Apply()
    {
        var hud = HudManager.Instance;
        if (SmokescreenPlugin.PrivateChat.Value && RenderState.Alive(hud) && RenderState.Alive(hud.Chat))
        {
            Mask.HideLiveTree(hud.Chat.gameObject);
            if (RenderState.Alive(hud.Chat.chatButton)) Mask.HideTree(hud.Chat.chatButton.gameObject);
            if (RenderState.Alive(hud.Chat.chatNotifyDot)) Mask.HideTree(hud.Chat.chatNotifyDot.gameObject);
        }
        if (SmokescreenPlugin.PrivateMeetings.Value && RenderState.Alive(MeetingHud.Instance))
        {
            var selection = guesser?.GetValue(null) as GameObject;
            Mask.HideLiveTree(selection);
        }
        if (RenderState.Alive(PrivacyNotice.Text)) Mask.HideTree(PrivacyNotice.Text.gameObject);
    }
}

[HarmonyPatch]
internal static class GuesserPublicVotes
{
    static MethodBase TargetMethod() => AccessTools.Method(
        AccessTools.TypeByName("TheOtherRoles.Patches.MeetingHudPatch"), "guesserOnClick");

    static void Prefix(MeetingHud __1) => PrivateUi.RememberPublicVotes(__1);
}
