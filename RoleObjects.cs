// Unknown's Smokescreen - Copyright (C) 2026 DaUnknown-0. Licensed under GPL-3.0-or-later.
//
// Things in the world that TOR only shows to the player who has the role. An ordinary crewmate would not
// see them, so on stream they would give the role away:
//   Detective   footprints                     (FootprintHolder)
//   Seer        souls at death spots            (root objects with Seer's soul sprite)
//   Medium      souls of the dead               (Medium.souls)
//   Trapper     its traps                       (Trap.traps, internal)
//   Bomber      the bomb before it is armed     (Bomber.bomb while !Bomber.isActive)
//   Trickster   the boxes before they turn into vents (JackInTheBox.AllJackInTheBoxes)
//   Yo-Yo       the silhouettes only it sees    (Silhouette.silhouettes with visibleForEveryOne false)
//   Portalmaker the first portal before the second one exists
//   Security Guard  cameras and sealed vents before the next meeting
//   Lovers      the chat button during the round (alive lovers can chat, nobody else can)
// Objects that everybody sees (garlic, Ninja traces, armed bombs, finished portals) stay.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TheOtherRoles;
using TheOtherRoles.Objects;
using UnityEngine;
using static TheOtherRoles.TheOtherRoles;

namespace UnknownsSmokescreen;

internal static class RoleObjects
{
    private static readonly Type trapType = AccessTools.TypeByName("TheOtherRoles.Objects.Trap");
    private static readonly FieldInfo fTraps = trapType != null ? AccessTools.Field(trapType, "traps") : null;
    private static readonly FieldInfo fTrapObject = trapType != null ? AccessTools.Field(trapType, "trap") : null;
    private static readonly FieldInfo fBoxObject = AccessTools.Field(typeof(JackInTheBox), "gameObject");
    private static readonly FieldInfo fSilhouetteEveryone = AccessTools.Field(typeof(Silhouette), "visibleForEveryOne");
    private static readonly FieldInfo fPortalObject = AccessTools.Field(typeof(Portal), "portalGameObject");
    private static readonly FieldInfo fCamerasToAdd = AccessTools.Field(AccessTools.TypeByName("TheOtherRoles.TORMapOptions"), "camerasToAdd");

    private static bool Alive(UnityEngine.Object o) => !ReferenceEquals(o, null) && o.m_CachedPtr != IntPtr.Zero;
    private static bool Is(PlayerControl role, PlayerControl local) => Alive(role) && role.PlayerId == local.PlayerId;

    private static readonly Il2CppSystem.Collections.Generic.List<GameObject> sceneRoots = new();
    private static float nextSoulScan;
    private static bool exileWasOpen;
    private static GameObject footprints;
    private static float nextFootprintLookup;

    public static void Reset()
    {
        footprints = null;
        nextSoulScan = 0f;
        sealedLooks.Clear();
    }

    public static void Apply(PlayerControl local, bool dead)
    {
        // Detective footprints: one holder object with every print below it.
        if (!Alive(footprints) && Time.time >= nextFootprintLookup)
        {
            nextFootprintLookup = Time.time + 1f;
            footprints = GameObject.Find("FootprintHolder");
        }
        if (Alive(footprints)) Mask.HideTree(footprints);

        if (Is(Medium.medium, local) && Medium.souls != null)
            foreach (var s in Medium.souls) Mask.HideRenderer(s);

        if (Is(Seer.seer, local)) HideSeerSouls();

        if (Is(Trapper.trapper, local) && fTraps?.GetValue(null) is IList traps)
            foreach (var t in traps)
                if (fTrapObject?.GetValue(t) is GameObject go) Mask.HideTree(go);

        if (Is(Bomber.bomber, local) && !Bomber.isActive && Bomber.bomb != null)
        {
            Mask.HideTree(Bomber.bomb.bomb);
            Mask.HideTree(Bomber.bomb.background);
        }

        if (!JackInTheBox.boxesConvertedToVents && JackInTheBox.AllJackInTheBoxes != null)
            foreach (var box in JackInTheBox.AllJackInTheBoxes)
                if (box != null && fBoxObject?.GetValue(box) is GameObject go) Mask.HideTree(go);

        if ((Is(Yoyo.yoyo, local) || dead) && Silhouette.silhouettes != null)
            foreach (var s in Silhouette.silhouettes)
                if (s != null && fSilhouetteEveryone?.GetValue(s) is bool everyone && !everyone) Mask.HideTree(s.gameObject);

        if (Is(Portalmaker.portalmaker, local) && Portal.firstPortal != null && Portal.secondPortal == null
            && fPortalObject?.GetValue(Portal.firstPortal) is GameObject portal)
            Mask.HideTree(portal);

        if (Is(SecurityGuard.securityGuard, local))
        {
            if (fCamerasToAdd?.GetValue(null) is IList cams)
                foreach (var c in cams)
                    if (c is SurvCamera cam && Alive(cam)) Mask.HideTree(cam.gameObject);
            KeepVentsUnsealed();
        }

        HideLoverChat(local, dead);
    }

    // --- Seer souls: nameless root objects (layer 5) carrying Seer's soul sprite ------------------------------

    private static void HideSeerSouls()
    {
        // Souls spawn when the exile screen closes. Look every frame for two seconds after that, then once a second.
        bool exileOpen = Alive(ExileController.Instance);
        if (exileWasOpen && !exileOpen) exileEnd = Time.time;
        exileWasOpen = exileOpen;
        bool justEnded = Time.time - exileEnd < 2f;
        if (!justEnded && Time.time < nextSoulScan) return;
        nextSoulScan = Time.time + 1f;

        Sprite soul = null;
        try { soul = Seer.getSoulSprite(); } catch { }
        if (!Alive(soul)) return;
        SceneRoots.Fill(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), sceneRoots);
        foreach (var go in sceneRoots)
        {
            if (!Alive(go) || go.layer != 5) continue;
            var r = go.GetComponent<SpriteRenderer>();
            if (Alive(r) && r.sprite != null && r.sprite.Pointer == soul.Pointer) Mask.HideRenderer(r);
        }
    }

    private static float exileEnd = -10f;

    // --- Security Guard: a sealed vent looks sealed to the Guard at once, to everybody else after the meeting ---

    private sealed class Look { public SpriteRenderer Rend; public Sprite Sprite; public Color Color; }
    private static readonly Dictionary<IntPtr, Look> sealedLooks = new();

    /// <summary>Remembers how a vent looked right before TOR gives it the sealed look on the Guard's client.</summary>
    internal static void BeforeSeal(int ventId)
    {
        try
        {
            if (!Shield.Active || !Is(SecurityGuard.securityGuard, PlayerControl.LocalPlayer)) return;
            Vent vent = null;
            foreach (var v in ShipStatus.Instance.AllVents) if (v != null && v.Id == ventId) { vent = v; break; }
            if (vent == null) return;
            var rend = Helpers.isFungle() ? vent.transform.GetChild(3).GetComponent<SpriteRenderer>() : vent.myRend;
            if (!Alive(rend)) return;
            sealedLooks[vent.Pointer] = new Look { Rend = rend, Sprite = rend.sprite, Color = rend.color };
        }
        catch { }
    }

    private static void KeepVentsUnsealed()
    {
        if (sealedLooks.Count == 0) return;
        if (Alive(MeetingHud.Instance) || Alive(ExileController.Instance)) return;   // after the meeting the seal is public
        foreach (var l in sealedLooks.Values)
        {
            if (!Alive(l.Rend)) continue;
            RenderState.Sprite(l.Rend);
            if (l.Rend.sprite != l.Sprite) l.Rend.sprite = l.Sprite;
            if (l.Rend.color != l.Color) l.Rend.color = l.Color;
        }
    }

    /// <summary>The meeting made the seals real for everybody: stop restoring the old look.</summary>
    internal static void SealsArePublic() => sealedLooks.Clear();

    // --- Lovers chat --------------------------------------------------------------------------------------------

    private static void HideLoverChat(PlayerControl local, bool dead)
    {
        if (dead || Alive(MeetingHud.Instance)) return;
        bool lover = Is(Lovers.lover1, local) || Is(Lovers.lover2, local);
        if (!lover) return;
        var chat = HudManager.Instance != null ? HudManager.Instance.Chat : null;
        if (Alive(chat) && Alive(chat.chatButton)) Mask.HideTree(chat.chatButton.gameObject);
    }
}

[HarmonyPatch(typeof(RPCProcedure), nameof(RPCProcedure.sealVent))]
internal static class SealVentLook
{
    public static void Prefix(int ventId) => RoleObjects.BeforeSeal(ventId);
}

[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
internal static class SealsPublicAtMeeting
{
    public static void Postfix() => RoleObjects.SealsArePublic();
}
