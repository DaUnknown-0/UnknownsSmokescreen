// Unknown's Smokescreen - Copyright (C) 2026 DaUnknown-0. Licensed under GPL-3.0-or-later.
//
// Der echte Rückblick bleibt privat. Die öffentliche Endbild-Maske zeigt nur Crewmate.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TheOtherRoles;
using TheOtherRoles.Objects;
using TMPro;
using UnityEngine;
using static TheOtherRoles.TheOtherRoles;

namespace UnknownsSmokescreen;

internal static class RoundRecap
{
    private const int MaxLines = 16;
    internal static bool Protected => shieldSeen;
    internal static IReadOnlyDictionary<byte, string> Names => names;

    private static DateTime start = DateTime.UtcNow;
    private static bool shieldSeen;
    private static readonly List<(DateTime at, string line)> events = new();
    private static string lastRoles;
    private static string team = "";
    private static float nextPoll;
    private static int deadSeen;
    private static string lastAbility;
    private static DateTime lastAbilityAt;
    private static readonly Dictionary<byte, string> names = new();

    public static void BeginRound()
    {
        start = DateTime.UtcNow;
        shieldSeen = false;
        events.Clear();
        lastRoles = null;
        team = "";
        nextPoll = 0f;
        deadSeen = 0;
        lastAbility = null;
        names.Clear();
    }

    private static void Add(string line) => events.Add((DateTime.UtcNow, line));

    public static void Tick(bool active)
    {
        if (active) shieldSeen = true;
        if (Time.time < nextPoll) return;
        nextPoll = Time.time + 0.5f;
        try
        {
            var local = PlayerControl.LocalPlayer;
            if (local == null || local.Data == null || local.Data.Role == null) return;

            foreach (var p in PlayerControl.AllPlayerControls)
                if (p != null && p.Data != null) names[p.PlayerId] = p.Data.PlayerName;

            string roles = string.Join(" ", RoleInfo.getRoleInfoForPlayer(local, true).Select(CrewAppearance.RealColored));
            if (roles != lastRoles)
            {
                lastRoles = roles;
                Add($"{Texts.T("recap.role")}: {roles}");
            }
            team = TeamOf(local);
            ScanDeaths(local.PlayerId);
        }
        catch { }
    }

    private static string NameOf(PlayerControl p)
    {
        if (p == null) return "?";
        try
        {
            if (names.TryGetValue(p.PlayerId, out var n)) return n;
            return p.Data?.PlayerName ?? "?";
        }
        catch
        {
            return "?";
        }
    }

    private static void ScanDeaths(byte localId)
    {
        var dead = Tor.DeadPlayers;
        if (dead == null) return;
        if (deadSeen > dead.Count) deadSeen = 0;
        for (int i = deadSeen; i < dead.Count; i++)
        {
            var d = dead[i];
            if (d == null || d.player == null) continue;
            string reason = Texts.T("death." + d.deathReason);
            bool mine = d.player.PlayerId == localId;
            bool myKill = d.killerIfExisting != null && d.killerIfExisting.PlayerId == localId && !mine;
            if (mine)
            {
                string by = d.killerIfExisting != null && d.killerIfExisting.PlayerId != localId
                    ? $" {Texts.T("recap.by")} {NameOf(d.killerIfExisting)}" : "";
                events.Add((d.timeOfDeath, $"<color=#FF6B6B>{Texts.T("recap.died")}</color> ({reason}{by})"));
            }
            else if (myKill)
            {
                string how = d.deathReason == DeadPlayer.CustomDeathReason.Kill ? "" : $" ({reason})";
                events.Add((d.timeOfDeath, $"{Texts.T("recap.kill")}: {NameOf(d.player)}{how}"));
            }
        }
        deadSeen = dead.Count;
    }

    private static string TeamOf(PlayerControl local) =>
        string.Join(", ", Mates(local).Select(m => Helpers.cs(m.color, m.text)));

    /// <summary>Who the local player knows to be on its side: impostor mates, Jackal/Sidekick, lover, client.</summary>
    internal static List<(string text, Color color)> Mates(PlayerControl local)
    {
        var mates = new List<(string, Color)>();
        if (local.Data.Role.IsImpostor)
            foreach (var p in PlayerControl.AllPlayerControls)
                if (p != null && p != local && p.Data?.Role != null && p.Data.Role.IsImpostor)
                    mates.Add((NameOf(p), Palette.ImpostorRed));
        if (Jackal.jackal == local && Sidekick.sidekick != null) mates.Add((NameOf(Sidekick.sidekick), Sidekick.color));
        if (Sidekick.sidekick == local && Jackal.jackal != null) mates.Add((NameOf(Jackal.jackal), Jackal.color));
        if (Lovers.lover1 == local && Lovers.lover2 != null) mates.Add(("♥ " + NameOf(Lovers.lover2), Lovers.color));
        if (Lovers.lover2 == local && Lovers.lover1 != null) mates.Add(("♥ " + NameOf(Lovers.lover1), Lovers.color));
        if (Lawyer.lawyer == local && Lawyer.target != null) mates.Add(("§ " + NameOf(Lawyer.target), Lawyer.color));
        return mates;
    }

    public static void Ability(CustomButton b)
    {
        string label = ButtonLabel(b);
        // One click can fire twice (mouse + hotkey in the same frame).
        if (label == lastAbility && (DateTime.UtcNow - lastAbilityAt).TotalSeconds < 1) return;
        lastAbility = label;
        lastAbilityAt = DateTime.UtcNow;
        Add($"{Texts.T("recap.ability")}: {label}");
    }

    /// <summary>A readable name for a TOR button: its text, its label, or its sprite name.</summary>
    internal static string ButtonLabel(CustomButton b)
    {
        string label = b.buttonText;
        if (string.IsNullOrWhiteSpace(label) && b.actionButton != null && b.actionButton.buttonLabelText != null)
            label = b.actionButton.buttonLabelText.text;
        if (string.IsNullOrWhiteSpace(label) && b.Sprite != null)
        {
            // "TheOtherRoles.Resources.SheriffKillButton.png" -> "SheriffKill"
            string n = b.Sprite.name ?? "";
            if (n.EndsWith(".png")) n = n.Substring(0, n.Length - 4);
            int dot = n.LastIndexOf('.');
            if (dot >= 0) n = n.Substring(dot + 1);
            label = n.Replace("Button", "").Trim();
        }
        if (string.IsNullOrWhiteSpace(label)) label = "?";
        return label.Trim();
    }

    public static void Flash(Color color, string message)
    {
        string text = string.IsNullOrWhiteSpace(message) ? "" : " " + message.Trim();
        Add($"{Texts.T("recap.flash")}: {Helpers.cs(color, "■")}{text}");

    }

    public static void Meeting()
    {
        if (Shield.Active) Add($"<color=#9AA0A6>{Texts.T("recap.meeting")}</color>");
    }

    public static void Show(EndGameManager em)
    {
        if (!shieldSeen || !SmokescreenPlugin.ShowRecap.Value || em == null || em.WinText == null) return;
        try
        {
            try { if (PlayerControl.LocalPlayer != null) ScanDeaths(PlayerControl.LocalPlayer.PlayerId); } catch { }

            var sb = new StringBuilder();
            sb.AppendLine($"<b>{Texts.T("recap.title")}</b>");
            if (!string.IsNullOrEmpty(team)) sb.AppendLine($"{Texts.T("recap.team")}: {team}");
            sb.AppendLine();

            var ordered = events.OrderBy(e => e.at).ToList();
            int shown = 0;
            foreach (var (at, line) in ordered)
            {
                if (shown == MaxLines)
                {
                    sb.AppendLine(string.Format(Texts.T("recap.more"), ordered.Count - shown));
                    break;
                }
                var t = at - start;
                if (t < TimeSpan.Zero) t = TimeSpan.Zero;
                sb.AppendLine($"<color=#FAD934FF>{(int)t.TotalMinutes:00}:{t.Seconds:00}</color>  {line}");
                shown++;
            }

            var go = UnityEngine.Object.Instantiate(em.WinText.gameObject);
            go.name = "SmokescreenRecap";
            var tmp = go.GetComponent<TMP_Text>();
            // Lines left-aligned (times in one column), the block itself flush with the right edge: the rect
            // gets exactly the width of the longest line and hangs from its top-right corner.
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.color = Color.white;
            tmp.enableWordWrapping = false;
            tmp.fontSizeMin = tmp.fontSizeMax = tmp.fontSize = 1.5f;
            tmp.text = sb.ToString();
            var rect = tmp.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.pivot = new Vector2(1f, 1f);
                tmp.ForceMeshUpdate();
                rect.sizeDelta = new Vector2(Mathf.Clamp(tmp.preferredWidth + 0.1f, 2f, 9f), 6f);
            }
            go.transform.localScale = Vector3.one;
            var cam = Camera.main;
            Vector3 corner = cam != null ? cam.ViewportToWorldPoint(new Vector3(1f, 1f, cam.nearClipPlane)) : new Vector3(5f, 3f, 0f);
            go.transform.position = new Vector3(corner.x - 0.25f, corner.y - 0.25f, -214f);
        }
        catch (Exception e)
        {
            SmokescreenPlugin.Logger.LogWarning($"recap: {e}");
        }
    }
}
