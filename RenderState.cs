// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;

namespace UnknownsSmokescreen;

// Eine Transaktion umfasst den regulaeren Kamerastapel eines Frames. Keine Action-Closures pro Änderung.
internal static class RenderState
{
    private readonly struct TextState
    {
        public readonly TMP_Text Target;
        public readonly string Value;
        public readonly Color Color;
        public TextState(TMP_Text t) { Target = t; Value = t.text; Color = t.color; }
    }
    private static readonly Dictionary<IntPtr, TextState> texts = new();
    private static readonly List<(Material mat, int id, float value)> floats = new();
    private static readonly List<(Material mat, int id, Color value)> colors = new();
    private static readonly List<(GameObject go, bool active)> objects = new();
    private static readonly List<(SpriteRenderer renderer, Sprite sprite, Color color)> sprites = new();
    internal static bool Alive(UnityEngine.Object o) => !ReferenceEquals(o, null) && o.m_CachedPtr != IntPtr.Zero;
    internal static void Text(TMP_Text t)
    {
        if (Alive(t) && !texts.ContainsKey(t.Pointer)) texts.Add(t.Pointer, new TextState(t));
    }
    internal static void Float(Material m, int id, float value)
    {
        if (!Alive(m) || !m.HasProperty(id)) return;
        float old = m.GetFloat(id);
        if (old == value) return;
        floats.Add((m, id, old)); m.SetFloat(id, value);
    }
    internal static void Color(Material m, int id, Color value)
    {
        if (!Alive(m) || !m.HasProperty(id)) return;
        Color old = m.GetColor(id);
        if (old == value) return;
        colors.Add((m, id, old)); m.SetColor(id, value);
    }
    internal static void Active(GameObject go, bool value)
    {
        if (go.activeSelf == value) return;
        objects.Add((go, go.activeSelf)); go.SetActive(value);
    }
    private static readonly List<(Renderer target, bool enabled)> renderers = new();
    internal static void Enabled(Renderer r, bool value)
    {
        if (!Alive(r) || r.enabled == value) return;
        renderers.Add((r, r.enabled)); r.enabled = value;
    }
    internal static void Sprite(SpriteRenderer r) => sprites.Add((r, r.sprite, r.color));
    private static bool Changed(TextState t) => t.Target.text != t.Value || t.Target.color != t.Color;
    internal static void Flush()
    {
        foreach (var t in texts.Values) if (Alive(t.Target) && Changed(t)) t.Target.ForceMeshUpdate(false, false);
    }
    internal static void Restore()
    {
        foreach (var t in texts.Values)
            if (Alive(t.Target) && Changed(t))
            {
                if (t.Target.text != t.Value) t.Target.text = t.Value;
                if (t.Target.color != t.Color) t.Target.color = t.Color;
                t.Target.ForceMeshUpdate(false, false);
            }
        for (int i = floats.Count - 1; i >= 0; i--) { var x = floats[i]; if (Alive(x.mat)) x.mat.SetFloat(x.id, x.value); }
        for (int i = colors.Count - 1; i >= 0; i--) { var x = colors[i]; if (Alive(x.mat)) x.mat.SetColor(x.id, x.value); }
        foreach (var x in objects) if (Alive(x.go)) x.go.SetActive(x.active);
        foreach (var x in sprites) if (Alive(x.renderer)) { x.renderer.sprite = x.sprite; x.renderer.color = x.color; }
        foreach (var x in renderers) if (Alive(x.target)) x.target.enabled = x.enabled;
        texts.Clear(); floats.Clear(); colors.Clear(); objects.Clear(); sprites.Clear(); renderers.Clear();
    }
}

internal static class StreamNames
{
    private static readonly Regex markup = new("<[^>]*>", RegexOptions.Compiled);
    private static readonly Dictionary<byte, (string input, string plain)> cache = new();
    internal static string Plain(string name) => markup.Replace(name ?? "", "");
    internal static string For(byte id, string name)
    {
        if (cache.TryGetValue(id, out var x) && x.input == name) return x.plain;
        var plain = Plain(name); cache[id] = (name, plain); return plain;
    }
}

internal static class EventVisuals
{
    internal static float FlashUntil;
    private static readonly List<TMP_Text> flashTexts = new();
    internal static void Reset() { flashTexts.Clear(); FlashUntil = 0; }
    internal static void RegisterFlash()
    {
        var hud = HudManager.Instance;
        if (!RenderState.Alive(hud) || !RenderState.Alive(hud.KillButton)) return;
        string cloneName = hud.KillButton.cooldownTimerText.name + "(Clone)";
        foreach (var t in hud.GetComponentsInChildren<TextMeshPro>(true))
            if (t.name == cloneName && t.transform.parent == hud.transform && !flashTexts.Contains(t)) flashTexts.Add(t);
    }
    private static readonly int outline = Shader.PropertyToID("_Outline");
    internal static void Apply()
    {
        var ship = ShipStatus.Instance;
        if (RenderState.Alive(ship))
            foreach (var v in ship.AllVents)
                if (RenderState.Alive(v) && RenderState.Alive(v.myRend)) RenderState.Float(v.myRend.material, outline, 0f);
        var hud = HudManager.Instance;
        if (SmokescreenPlugin.HideFlashes.Value && Time.time < FlashUntil && RenderState.Alive(hud))
        {
            Mask.HideRenderer(hud.FullScreen);
        }
        for (int i = flashTexts.Count - 1; i >= 0; i--)
        {
            var t = flashTexts[i];
            if (!RenderState.Alive(t)) flashTexts.RemoveAt(i);
            else Mask.HideTree(t.gameObject);
        }
        var meeting = MeetingHud.Instance;
        if (!RenderState.Alive(meeting) || !GameManager.Instance.LogicOptions.GetAnonymousVotes()) return;
        foreach (var area in meeting.playerStates)
        {
            if (!RenderState.Alive(area)) continue;
            var spread = area.GetComponent<VoteSpreader>();
            if (!RenderState.Alive(spread) || spread.Votes == null) continue;
            foreach (var vote in spread.Votes)
            {
                if (!RenderState.Alive(vote)) continue;
                var mat = vote.material;
                RenderState.Color(mat, PlayerMaterial.BodyColor, Palette.DisabledGrey);
                RenderState.Color(mat, PlayerMaterial.BackColor, Palette.DisabledGrey);
            }
        }
    }
}

internal static class IntroPrivacy
{
    private static IntPtr introId;
    internal static bool RolePhase;
    private static readonly List<PoolablePlayer> crew = new();
    private static readonly List<PoolablePlayer> actual = new();
    internal static void Reset()
    {
        foreach (var p in crew) if (RenderState.Alive(p)) UnityEngine.Object.Destroy(p.gameObject);
        crew.Clear(); actual.Clear(); names.Clear(); crewRenderers.Clear(); introId = IntPtr.Zero; RolePhase = false;
    }
    internal static void Prepare(IntroCutscene intro)
    {
        if (!RenderState.Alive(intro) || !RenderState.Alive(intro.PlayerPrefab)) return;
        if (intro.Pointer == introId) return;
        introId = intro.Pointer;
        var players = new List<PlayerControl>();
        var local = PlayerControl.LocalPlayer;
        if (!RenderState.Alive(local)) return;
        players.Add(local);
        foreach (var p in PlayerControl.AllPlayerControls)
            if (RenderState.Alive(p) && p.PlayerId != local.PlayerId && p.Data != null && !p.Data.Disconnected) players.Add(p);
        int depth = Math.Max(1, (players.Count + 1) / 2);
        for (int i = 0; i < players.Count; i++)
        {
            PoolablePlayer icon;
            preparing = true;
            try { icon = intro.CreatePlayer(i, depth, players[i].Data, false); }
            finally { preparing = false; }
            if (!RenderState.Alive(icon)) continue;
            icon.name = "SmokescreenCrewIntro";
            icon.gameObject.SetActive(true);
            var arr = icon.GetComponentsInChildren<Renderer>(true);
            var renderers = new Renderer[arr.Length];
            for (int j = 0; j < arr.Length; j++) { renderers[j] = arr[j]; arr[j].enabled = false; }
            crewRenderers[icon.Pointer] = renderers;
            crew.Add(icon);
            CacheNames(icon);
        }
    }
    private static readonly Dictionary<IntPtr, Renderer[]> crewRenderers = new();
    private static readonly Dictionary<IntPtr, TMP_Text[]> names = new();
    private static bool preparing;
    internal static void Register(PoolablePlayer p)
    {
        if (preparing || !RenderState.Alive(p) || actual.Exists(x => x.Pointer == p.Pointer)) return;
        actual.Add(p);
        CacheNames(p);
    }
    private static void CacheNames(PoolablePlayer p)
    {
        var arr = p.GetComponentsInChildren<TMP_Text>(true);
        var list = new TMP_Text[arr.Length];
        for (int i = 0; i < list.Length; i++) list[i] = arr[i];
        names[p.Pointer] = list;
    }
    private static void Clean(PoolablePlayer p)
    {
        if (!names.TryGetValue(p.Pointer, out var list)) return;
        foreach (var t in list)
        {
            if (!RenderState.Alive(t)) continue;
            RenderState.Text(t);
            string plain = StreamNames.Plain(t.text);
            if (t.text != plain) t.text = plain;
            if (t.color != Color.white) t.color = Color.white;
        }
    }
    internal static void Apply(IntroCutscene intro)
    {
        Prepare(intro);
        bool teamPhase = !RolePhase && RenderState.Alive(intro.TeamTitle) && intro.TeamTitle.gameObject.activeInHierarchy;
        foreach (var p in actual)
        {
            if (!RenderState.Alive(p)) continue;
            if (teamPhase && crew.Count > 0) Mask.HideTree(p.gameObject);
            Clean(p);
        }
        if (!teamPhase) return;
        foreach (var p in crew)
        {
            if (!RenderState.Alive(p)) continue;
            if (crewRenderers.TryGetValue(p.Pointer, out var renderers))
                foreach (var r in renderers) RenderState.Enabled(r, true);
            Clean(p);
        }
    }
}

public sealed class CameraMaskScope : MonoBehaviour
{
    public CameraMaskScope(IntPtr p) : base(p) { }
    private static int appliedFrame = -1;
    internal static int LastMaskedFrame => appliedFrame;
    private static bool entered;
    internal static long Applications, Restores;
    internal static double ApplyMilliseconds;
    private void OnPreCull()
    {
        if (OverlayContent.Stopped || OverlayContent.Rendering || !Shield.Active || appliedFrame == Time.frameCount) return;
        appliedFrame = Time.frameCount;
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        try { entered = true; Mask.Apply(); RenderState.Flush(); Applications++; }
        catch (Exception e) { Restore(); SmokescreenPlugin.Logger.LogWarning("Render-Maske: " + e.Message); }
        finally { ApplyMilliseconds += (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency; }
    }
    internal static void RestoreBeforeLogic()
    {
        if (entered && appliedFrame != Time.frameCount) Restore();
    }
    internal static void Restore()
    {
        if (!entered) return;
        entered = false;
        Mask.RestoreAll();
        Restores++;
    }
}
