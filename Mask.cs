// Unknown's Smokescreen - Copyright (C) 2026 DaUnknown-0. Licensed under GPL-3.0-or-later.
//
// Reversible Maske im Kamerarender. Update und Rollenlogik behalten den echten Zustand.
// Collider, Hotkeys und aktive Rollenobjekte werden nicht verändert.

using System;
using System.Collections.Generic;
using System.Collections;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using TheOtherRoles;
using TheOtherRoles.Objects;
using TMPro;
using UnityEngine;
using static TheOtherRoles.TheOtherRoles;

namespace UnknownsSmokescreen;

public class SmokescreenRunner : MonoBehaviour
{
    public SmokescreenRunner(IntPtr ptr) : base(ptr) { }

    private bool wasStarted;
    private void Start() => StartCoroutine(AfterRendering().WrapToIl2Cpp());
    [Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp]
    private IEnumerator AfterRendering()
    {
        var end = new WaitForEndOfFrame();
        while (true)
        {
            yield return end;
            try
            {
                CameraMaskScope.Complete();
                OverlayContent.CompleteFrame();
            }
            finally { CameraMaskScope.Restore(); }
        }
    }
    // Sicherheitsnetz, falls Unity bei Minimieren keinen EndOfFrame ausfuehrt.
    private void Update() => CameraMaskScope.Restore();
    private void FixedUpdate() => CameraMaskScope.Restore();

    private void OnApplicationQuit() { OverlayContent.Dispose(); OverlayHost.Stop(); }
    private void OnDestroy() { PrivacyNotice.Clear(); OverlayContent.Dispose(); OverlayHost.Stop(); }

    private void LateUpdate()
    {
        bool started = false;
        try
        {
            started = AmongUsClient.Instance != null
                && AmongUsClient.Instance.GameState == InnerNet.InnerNetClient.GameStates.Started;
        }
        catch { }
        if (started && !wasStarted)
        {
            // Reversiblen Zustand fuer die neue Runde zuruecksetzen.
            Mask.ResetRound();
            LeakGuard.Reset();
            Shield.ResultScreen = false;
            Shield.Invalidate();
            OverlayContent.Reset();
        }
        wasStarted = started;

        SettingsMenu.Tick();
        bool active = Shield.Active;
        try { PrivacyNotice.Tick(active); }
        catch (Exception) { PrivacyNotice.Clear(); }
        OverlayContent.Tick(active);

    }
}

internal static class Mask
{
    // --- bookkeeping of what we switched off --------------------------------------------------------

    private static readonly Dictionary<IntPtr, Renderer> hidden = new();
    internal static Dictionary<IntPtr, Renderer>.ValueCollection Hidden => hidden.Values;
    private static readonly Dictionary<IntPtr, (int frame, Renderer[] list)> rendererCache = new();
    internal static readonly List<Arrow> Arrows = new();

    public static void ResetRound()
    {
        RestoreAll();
        RenderState.ReleaseNeutralMaterials();
        rendererCache.Clear();
        PrivateSet.Reset();
        Arrows.RemoveAll(a => a == null || !Alive(a.arrow));
        phoneUi = null;
        infoCache.Clear();
        ResetIntroLine();
        RoleObjects.Reset();
        EventVisuals.Reset();
    }

    private static bool Alive(UnityEngine.Object o) => !ReferenceEquals(o, null) && o.m_CachedPtr != IntPtr.Zero;

    internal static void HideRenderer(Renderer r)
    {
        if (!Alive(r) || !r.enabled) return;
        r.enabled = false;
        hidden[r.Pointer] = r;
    }

    /// <summary>Every renderer below <paramref name="go"/> off. The renderer list is cached for 30 frames
    /// (GetComponentsInChildren allocates an Il2Cpp array, not something to do 20 times per frame).</summary>
    internal static void HideTree(GameObject go)
    {
        if (!Alive(go)) return;
        IntPtr key = go.Pointer;
        if (!rendererCache.TryGetValue(key, out var entry) || Time.frameCount - entry.frame > 30)
        {
            var arr = go.GetComponentsInChildren<Renderer>(true);
            var list = new Renderer[arr.Length];
            for (int i = 0; i < arr.Length; i++) list[i] = arr[i];
            entry = (Time.frameCount, list);
            rendererCache[key] = entry;
        }
        foreach (var r in entry.list) HideRenderer(r);
    }

    internal static void HideLiveTree(GameObject go)
    {
        if (!Alive(go) || !go.activeInHierarchy) return;
        foreach (var r in go.GetComponentsInChildren<Renderer>()) HideRenderer(r);
    }

    public static void RestoreAll()
    {
        RenderState.Restore();
        foreach (var r in hidden.Values)
            if (Alive(r)) r.enabled = true;
        hidden.Clear();

    }

    // --- the frame ------------------------------------------------------------------------------------

    public static void Apply()
    {
        var local = PlayerControl.LocalPlayer;
        if (!Alive(local) || local.Data == null) return;
        bool dead = local.Data.IsDead;
        hideWorldNames = Tor.HidePlayerNames;
        var meeting = MeetingHud.Instance;

        foreach (var p in PlayerControl.AllPlayerControls)
        {
            if (!Alive(p) || p.Data == null || p.cosmetics == null) continue;
            if (Alive(p.cosmetics.nameText)) CleanName(p.cosmetics.nameText, p.PlayerId);
            var info = InfoAbove(p);
            if (info != null) RenderState.PublicText(info, InfoFor(p, local, dead, false), Color.white, true);
            ClearOutline(p);
        }

        if (Alive(meeting))
        {
            PrivateUi.ShowPublicVotes(meeting);
            MaskMeeting(meeting, local, dead);
        }
        MaskTaskTab();
        EventVisuals.Apply();
        if (SmokescreenPlugin.HideButtons.Value) HideButtons(dead);
        HideRoleHints();
        RoleObjects.Apply(local, dead);
        CollectionVisuals.Apply();
        MaskSabotageMap();
        MaskIntro();
        PrivateUi.Apply();
    }

    // --- names ----------------------------------------------------------------------------------------


    private static string loverSuffix, lawyerSuffix, thiefSuffix, medicOpen, medicClose;
    private static bool hideWorldNames;
    private static void CleanName(TMP_Text t, byte playerId, bool inMeeting = false)
    {
        loverSuffix ??= Helpers.cs(Lovers.color, " ♥");
        lawyerSuffix ??= Helpers.cs(Lawyer.color, " §");
        thiefSuffix ??= Helpers.cs(Thief.color, " $");
        medicOpen ??= Helpers.cs(Medic.color, "[");
        medicClose ??= Helpers.cs(Medic.color, "]");
        string s = t.text ?? "";
        // Verkleidungen und absichtlich leere Namen bewahren. Versteckte Namen haben keine Team-Ausnahmen.
        if (!inMeeting && hideWorldNames && playerId != PlayerControl.LocalPlayer.PlayerId) s = "";
        else
        {
            s = s.Replace(loverSuffix, "").Replace(lawyerSuffix, "").Replace(thiefSuffix, "");
            if (s.StartsWith(medicOpen) && s.EndsWith(medicClose))
                s = s.Substring(medicOpen.Length, s.Length - medicOpen.Length - medicClose.Length);
            if (Is(Godfather.godfather, playerId)) s = s.Replace(" (G)", "");
            else if (Is(Mafioso.mafioso, playerId)) s = s.Replace(" (M)", "");
            else if (Is(Janitor.janitor, playerId)) s = s.Replace(" (J)", "");
        }
        SetText(t, StreamNames.For(playerId, s));
    }

    private static bool Is(PlayerControl p, byte id) => Alive(p) && p.PlayerId == id;

    // --- the "Info" line above heads and in the meeting ---------------------------------------------------

    private static readonly Dictionary<IntPtr, TMP_Text> infoCache = new();

    private static TMP_Text InfoAbove(PlayerControl p)
    {
        var parent = p.cosmetics.nameText.transform.parent;
        return Alive(parent) ? InfoUnder(parent) : null;
    }

    private static TMP_Text InfoUnder(Transform parent)
    {
        if (infoCache.TryGetValue(parent.Pointer, out var cached) && Alive(cached)) return cached;
        var t = parent.FindChild("Info");
        var tmp = Alive(t) ? t.GetComponent<TMP_Text>() : null;
        if (tmp != null) infoCache[parent.Pointer] = tmp;
        return tmp;
    }

    private static string InfoFor(PlayerControl p, PlayerControl local, bool localDead, bool inMeeting)
    {
        if (p.PlayerId == local.PlayerId)
            return CrewAppearance.Name;

        // Alive: TOR only fills this for the Lawyer's client (on the client). A text above exactly one other
        // player would give the Lawyer away, so nothing is shown at all.
        if (!localDead) return "";

        return Tor.GhostsSeeRoles ? CrewAppearance.Name : "";
    }

    private static void SetText(TMP_Text t, string s, Color? color = null) =>
        RenderState.PublicText(t, s, color ?? Color.white);

    // --- task tab --------------------------------------------------------------------------------------

    private static TMP_Text taskTab;
    private static void MaskTaskTab()
    {
        // TOR writes "Tasks (3/7)" for crewmates and "Tasks " for everyone else: that difference alone tells
        // the team. Everyone gets the plain word.
        var hud = HudManager.Instance;
        if (!Alive(hud) || !Alive(hud.TaskPanel) || !Alive(hud.TaskPanel.tab)) return;
        if (!Alive(taskTab))
        {
            var tab = hud.TaskPanel.tab.transform.FindChild("TabText_TMP");
            taskTab = Alive(tab) ? tab.GetComponent<TMP_Text>() : null;
        }
        if (Alive(taskTab)) SetText(taskTab, "Tasks");
        if (Alive(hud.TaskPanel.taskText))
            SetText(hud.TaskPanel.taskText, TaskListMask.Masked(hud.TaskPanel.taskText.text));
    }

    // --- meeting ---------------------------------------------------------------------------------------

    private static Transform phoneUi;

    private static void MaskMeeting(MeetingHud meeting, PlayerControl local, bool dead)
    {
        bool results = meeting.state == MeetingHud.VoteStates.Results;
        foreach (var area in meeting.playerStates)
        {
            if (!Alive(area)) continue;
            if (Alive(area.NameText))
            {
                CleanName(area.NameText, area.TargetPlayerId, true);
                var info = Alive(area.NameText.transform.parent) ? InfoUnder(area.NameText.transform.parent) : null;
                if (info != null)
                {
                    var p = Helpers.playerById(area.TargetPlayerId);
                    RenderState.PublicText(info, results || p == null ? "" : InfoFor(p, local, dead, true), Color.white, true);
                }
            }

            if (!SmokescreenPlugin.PrivateMeetings.Value) continue;
            // Guesser "ShootButton" and the Swapper check boxes (clones of the CancelButton) sit directly on
            // the vote area; the real CancelButton lives under area.Buttons and stays visible.
            var tr = area.transform;
            for (int i = 0; i < tr.childCount; i++)
            {
                var c = tr.GetChild(i);
                if (!Alive(c)) continue;
                string n = c.name;
                if (n == "ShootButton" || n == "CancelButton(Clone)") HideTree(c.gameObject);
            }
        }

        if (!SmokescreenPlugin.PrivateMeetings.Value) return;
        // The Swapper "Confirm Swap" / Mayor "Double Vote" button: a nameless holder under PhoneUI with a
        // "votePlayerBase(Clone)" in it.
        if (!Alive(phoneUi)) phoneUi = FindDeep(meeting.transform, "PhoneUI");
        if (!Alive(phoneUi)) return;
        for (int i = 0; i < phoneUi.childCount; i++)
        {
            var c = phoneUi.GetChild(i);
            if (Alive(c) && c.name == "New Game Object" && Alive(c.FindChild("votePlayerBase(Clone)")))
                HideTree(c.gameObject);
        }
    }

    private static Transform FindDeep(Transform root, string name)
    {
        if (!Alive(root)) return null;
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var hit = FindDeep(root.GetChild(i), name);
            if (hit != null) return hit;
        }
        return null;
    }

    // --- buttons ---------------------------------------------------------------------------------------

    private static void HideButtons(bool dead)
    {
        var hud = HudManager.Instance;
        if (!Alive(hud)) return;
        if (Alive(hud.KillButton)) HideTree(hud.KillButton.gameObject);
        if (Alive(hud.SabotageButton)) HideTree(hud.SabotageButton.gameObject);
        if (Alive(hud.ImpostorVentButton)) HideTree(hud.ImpostorVentButton.gameObject);
        // The ghost "Haunt" button is the same for every dead player; alive it is a role ability.
        if (!dead && Alive(hud.AbilityButton)) HideTree(hud.AbilityButton.gameObject);

        foreach (var b in CustomButton.buttons)
            if (b != null && Alive(b.actionButton)) HideTree(b.actionButton.gameObject);
    }

    // --- arrows, icons, meters, outlines -------------------------------------------------------------------

    private static void HideRoleHints()
    {
        for (int i = Arrows.Count - 1; i >= 0; i--)
        {
            var a = Arrows[i];
            if (a == null || !Alive(a.arrow)) { Arrows.RemoveAt(i); continue; }
            HideRenderer(a.image);
        }

        // Bounty Hunter, Arsonist, Pursuer ... show player icons in the lower left.
        var icons = Tor.PlayerIcons;
        if (icons != null)
            foreach (var pp in icons.Values)
                if (Alive(pp)) HideTree(pp.gameObject);

        // Tracker's danger meter.
        if (Alive(Tracker.DangerMeterParent)) HideTree(Tracker.DangerMeterParent);
    }

    private static readonly int OutlineId = Shader.PropertyToID("_Outline");

    private static void ClearOutline(PlayerControl p)
    {
        // Kill target (red), Sheriff/Medic/Tracker/... targets (role colour): all through this material value.
        var body = p.cosmetics.currentBodySprite;
        if (body == null || !Alive(body.BodySprite)) return;
        var mat = body.BodySprite.material;
        if (Alive(mat)) RenderState.Float(mat, OutlineId, 0f, body.BodySprite);
    }

    // --- sabotage map --------------------------------------------------------------------------------------

    private static void MaskSabotageMap()
    {
        var map = MapBehaviour.Instance;
        if (!Alive(map) || !map.IsOpen || !Alive(map.infectedOverlay)) return;
        if (Alive(map.ColorControl))
        {
            var renderer = map.ColorControl.GetComponent<SpriteRenderer>();
            if (Alive(renderer)) { RenderState.Sprite(renderer); renderer.color = new Color(0.05f, 0.2f, 1f, renderer.color.a); }
        }
        if (map.infectedOverlay.gameObject.activeInHierarchy) HideTree(map.infectedOverlay.gameObject);
    }

    // --- intro -----------------------------------------------------------------------------------------

    /// <summary>The running intro. IntroCutscene.Instance stays null online for the whole intro (seen 2026-10-16
    /// in the real draft), so the intro patches hand the instance over themselves.</summary>
    internal static IntroCutscene Intro;
    internal static IntPtr PublicIntroCount => Alive(Intro) && Alive(Intro.ImpostorText) ? Intro.ImpostorText.Pointer : IntPtr.Zero;

    private static void MaskIntro()
    {
        var intro = Alive(Intro) ? Intro : IntroCutscene.Instance;
        if (!Alive(intro)) return;


        Color team = CrewAppearance.IntroColor;
        string title = CrewAppearance.Name;

        RenderState.Text(intro.TeamTitle);
        RenderState.Text(intro.ImpostorText);
        RenderState.Text(intro.YouAreText);
        RenderState.Text(intro.RoleText);
        RenderState.Text(intro.RoleBlurbText);
        IntroPrivacy.Apply(intro);
        if (Alive(intro.TeamTitle)) SetText(intro.TeamTitle, title, team);
        // "There is 1 Impostor among us": the crewmate intro has it, the impostor intro does not. Everybody gets it.
        if (Alive(intro.ImpostorText))
        {
            if (!intro.ImpostorText.gameObject.activeSelf && Alive(intro.TeamTitle) && intro.TeamTitle.gameObject.activeInHierarchy)
                RenderState.Active(intro.ImpostorText.gameObject, true);
            if (intro.ImpostorText.gameObject.activeSelf) SetText(intro.ImpostorText, ImpostorLine());
        }
        if (Alive(intro.BackgroundBar) && Alive(intro.BackgroundBar.material)) RenderState.Color(intro.BackgroundBar.material, Shader.PropertyToID("_Color"), team, intro.BackgroundBar);
        if (Alive(intro.YouAreText)) SetText(intro.YouAreText, StreamNames.Plain(intro.YouAreText.text), team);
        if (Alive(intro.RoleText)) SetText(intro.RoleText, CrewAppearance.Name, team);
        if (Alive(intro.RoleBlurbText)) SetText(intro.RoleBlurbText, CrewAppearance.Description, team);
    }

    private static string impostorLine;

    /// <summary>The vanilla crewmate subtitle, built the way IntroCutscene builds it.</summary>
    private static string ImpostorLine()
    {
        if (impostorLine != null) return impostorLine;
        int n = 1;
        try { n = GameOptionsManager.Instance.currentNormalGameOptions.NumImpostors; } catch { }
        string s;
        try
        {
            var tc = TranslationController.Instance;
            s = n == 1
                ? tc.GetString(StringNames.NumImpostorsS, new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppSystem.Object>(0))
                : tc.GetString(StringNames.NumImpostorsP, new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppSystem.Object>(0));
            if (s.Contains("{0}")) s = s.Replace("{0}", n.ToString());
            s = StreamNames.Plain(s).Replace("[FF1919FF]", "").Replace("[]", "");
        }
        catch
        {
            s = n == 1 ? "There is 1 Impostor among us" : $"There are {n} Impostors among us";
        }
        impostorLine = s;
        return s;
    }

    internal static void ResetIntroLine() => impostorLine = null;

    // The intro sting differs between crewmates and impostors: that would be audible on stream. The local
    // Öffentliche Intro-Audios verwenden stets den Crew-Sound bis zum Ende des Intros.
    private static RoleBehaviour soundRole;
    private static AudioClip soundOriginal;

    internal static void SwapIntroSound()
    {
        try
        {
            var local = PlayerControl.LocalPlayer;
            var role = local?.Data?.Role;
            if (!Alive(role) || !Alive(RoleManager.Instance)) return;
            var wanted = AmongUs.GameOptions.RoleTypes.Crewmate;
            AudioClip clip = null;
            foreach (var rb in RoleManager.Instance.AllRoles)
                if (Alive(rb) && rb.Role == wanted) { clip = rb.IntroSound; break; }
            if (!Alive(clip)) return;
            if (!Alive(soundRole)) { soundRole = role; soundOriginal = role.IntroSound; }
            role.IntroSound = clip;
        }
        catch (Exception e)
        {
            SmokescreenPlugin.Logger.LogWarning($"intro sound: {e.Message}");
        }
    }

    internal static void RestoreIntroSound()
    {
        if (Alive(soundRole) && Alive(soundOriginal)) soundRole.IntroSound = soundOriginal;
        soundRole = null;
        soundOriginal = null;
    }
}
