// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TheOtherRoles;
using TMPro;
using UnityEngine;

namespace UnknownsSmokescreen;

internal static class LeakGuard
{
    private static Il2CppArrayBase<TMP_Text> texts;
    private static readonly List<(string word, string color, Color value)> roles = new();
    private static readonly HashSet<string> playerNames = new(StringComparer.Ordinal);
    private static readonly HashSet<string> allowed = new(StringComparer.Ordinal);
    private static readonly HashSet<string> reported = new(StringComparer.Ordinal);
    private static float next, refresh;
    private static int cursor;
    private static string lastHash, allowlist;

    internal static void Reset()
    {
        texts = null;
        refresh = next = 0f;
        cursor = Hits = Checked = 0;
        MaximumMilliseconds = MaximumScanMilliseconds = MaximumDiscoveryMilliseconds = 0;
        Failed = false;
        lastHash = null;
        reported.Clear();
        roles.Clear();
    }

    internal static void AllowLast()
    {
        if (lastHash == null || allowed.Contains(lastHash)) return;
        string current = SmokescreenPlugin.LeakAllowlist.Value;
        SmokescreenPlugin.LeakAllowlist.Value = current.Length == 0 ? lastHash : current + "|" + lastHash;
        lastHash = null;
    }
    internal static int Hits, Checked;
    internal static bool Failed;
    internal static double MaximumMilliseconds, MaximumScanMilliseconds, MaximumDiscoveryMilliseconds;

    internal static void Check()
    {
        if (!SmokescreenPlugin.LeakWatch.Value || !Shield.Active || Time.realtimeSinceStartup < next) return;
        next = Time.realtimeSinceStartup + 0.5f;
        long start = Stopwatch.GetTimestamp();
        string config = SmokescreenPlugin.LeakAllowlist.Value;
        if (config != allowlist)
        {
            allowlist = config;
            allowed.Clear();
            foreach (string hash in config.Split('|')) allowed.Add(hash);
        }
        if (Time.realtimeSinceStartup >= refresh)
        {
            long discoveryStart = Stopwatch.GetTimestamp();
            texts = UnityEngine.Object.FindObjectsOfType<TMP_Text>();
            refresh = Time.realtimeSinceStartup + 3f;
            if (roles.Count == 0)
                foreach (var r in RoleInfo.allRoleInfos)
                    if (!string.IsNullOrEmpty(r.name) && r.name != CrewAppearance.Name)
                        roles.Add((r.name, ColorUtility.ToHtmlStringRGB(r.color), r.color));
            playerNames.Clear();
            foreach (var p in PlayerControl.AllPlayerControls)
                if (p != null && p.Data != null) playerNames.Add(StreamNames.For(p.PlayerId, p.Data.PlayerName));
            MaximumDiscoveryMilliseconds = Math.Max(MaximumDiscoveryMilliseconds, Elapsed(discoveryStart));
        }
        long scanStart = Stopwatch.GetTimestamp();
        // Discovery separat. Die eigentliche Stichprobe dreht ueber die gesamte Liste weiter.
        for (int n = 0; n < Math.Min(48, texts.Length); n++)
        {
            if (cursor >= texts.Length) cursor = 0;
            var t = texts[cursor++];
            if (RenderState.Alive(t) && t.gameObject.activeInHierarchy && OverlayContent.OnScreenLayer(t.gameObject.layer)) Inspect(t);
            if (Elapsed(scanStart) >= 0.75) break;
        }
        MaximumScanMilliseconds = Math.Max(MaximumScanMilliseconds, Elapsed(scanStart));
        MaximumMilliseconds = Math.Max(MaximumMilliseconds, Elapsed(start));
        Failed = false;
    }

    private static double Elapsed(long start) => (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;

    private static void Inspect(TMP_Text t)
    {
        var r = t.GetComponent<Renderer>();
        if (!RenderState.Alive(r) || !r.enabled || t.color.a < 0.01f || !r.isVisible) return;
        string s = t.text ?? "";
        if (s.Length == 0 || s.Length > 4096) return;
        string plain = StreamNames.Plain(s);
        if (plain == CrewAppearance.Name || plain == CrewAppearance.TaskLine || playerNames.Contains(plain)) return;
        Checked++;
        bool hit = false;
        foreach (var role in roles)
        {
            if (ContainsWord(plain, role.word)) hit = true;
            Color c = role.value;
            float chroma = Math.Max(c.r, Math.Max(c.g, c.b)) - Math.Min(c.r, Math.Min(c.g, c.b));
            if (chroma > 0.3f && (s.IndexOf("<color=#" + role.color, StringComparison.OrdinalIgnoreCase) >= 0
                || Math.Abs(t.color.r - c.r) + Math.Abs(t.color.g - c.g) + Math.Abs(t.color.b - c.b) < 0.025f)) hit = true;
            if (hit) break;
        }
        if (!hit) return;
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plain)));
        if (allowed.Contains(hash) || reported.Count >= 2048 || !reported.Add(hash)) return;
        lastHash = hash;
        Hits++;
        // Keine Rollen, Namen, Textinhalte oder Objektpfade im Log.
        SmokescreenPlugin.Logger.LogWarning("Leckwaechter: sichtbarer Text mit Rollenwort oder Rollenfarbe, Treffer=" + Hits);
    }

    private static bool ContainsWord(string text, string word)
    {
        int start = 0;
        while (start <= text.Length - word.Length)
        {
            int match = text.IndexOf(word, start, StringComparison.OrdinalIgnoreCase);
            if (match < 0) return false;
            int end = match + word.Length;
            if ((match == 0 || !char.IsLetterOrDigit(text[match - 1]))
                && (end == text.Length || !char.IsLetterOrDigit(text[end]))) return true;
            start = match + 1;
        }
        return false;
    }
}
