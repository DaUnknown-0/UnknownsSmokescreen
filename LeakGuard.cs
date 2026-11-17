// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TheOtherRoles;
using TMPro;
using UnityEngine;

namespace UnknownsSmokescreen;

internal static class LeakGuard
{
    private static Il2CppArrayBase<TMP_Text> texts;
    private static readonly List<(string word, Color value)> roles = new();
    private static readonly HashSet<string> playerNames = new(StringComparer.Ordinal);
    private static readonly HashSet<string> allowed = new(StringComparer.Ordinal);
    private static readonly HashSet<string> reported = new(StringComparer.Ordinal);
    private static float next, refresh;
    private static int cursor;
    private static string lastHash, allowlist;
    private static TMP_Text ping;
    private static readonly Regex gameMode = new(
        @"(?im)^[ \t]*(?:<color=(?:#[0-9a-f]{6,8}|yellow)>)?(?:Guesser|Hide[ \t]*['’]?[ \t]*N[ \t]*['’]?[ \t]*Seek|Prop[ \t]*Hunt)(?:</color>)?(?=[ \t]*(?:\r?$|PING\b))",
        RegexOptions.CultureInvariant);
    private static readonly Regex colorTags = new(@"<(?:color\s*=\s*)?(?<value>#[0-9a-f]{3,8})>|<color\s*=\s*(?<value>[a-z]+)>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static void ObservePing(TMP_Text text) => ping = text;
    internal static string WithoutPublicGameMode(string text) => gameMode.Replace(text, "");

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
                        roles.Add((r.name, r.color));
            playerNames.Clear();
            foreach (var p in PlayerControl.AllPlayerControls)
                if (p != null && p.Data != null) playerNames.Add(StreamNames.For(p.PlayerId, p.Data.PlayerName));
            MaximumDiscoveryMilliseconds = Math.Max(MaximumDiscoveryMilliseconds, Elapsed(discoveryStart));
        }
        long scanStart = Stopwatch.GetTimestamp();
        if (RenderState.Alive(ping) && ping.gameObject.activeInHierarchy) Inspect(ping, true);
        // Discovery separat. Die eigentliche Stichprobe dreht ueber die gesamte Liste weiter.
        for (int n = 0; n < Math.Min(48, texts.Length); n++)
        {
            if (cursor >= texts.Length) cursor = 0;
            var t = texts[cursor++];
            if (RenderState.Alive(t) && t.Pointer != ping?.Pointer && t.gameObject.activeInHierarchy && OverlayContent.OnScreenLayer(t.gameObject.layer))
                Inspect(t, false);
            if (Elapsed(scanStart) >= 0.75) break;
        }
        MaximumScanMilliseconds = Math.Max(MaximumScanMilliseconds, Elapsed(scanStart));
        MaximumMilliseconds = Math.Max(MaximumMilliseconds, Elapsed(start));
        Failed = false;
    }

    private static double Elapsed(long start) => (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;

    private static void Inspect(TMP_Text t, bool isPing)
    {
        var r = t.GetComponent<Renderer>();
        if (!RenderState.Alive(r) || !r.enabled || t.color.a < 0.01f || !r.isVisible) return;
        string s = t.text ?? "";
        if (s.Length == 0 || s.Length > 4096) return;
        string original = s;
        if (isPing) s = WithoutPublicGameMode(s);
        string plain = StreamNames.Plain(s);
        bool crewTasks = plain.StartsWith(CrewAppearance.TaskLine, StringComparison.Ordinal);
        bool crew = plain == CrewAppearance.Name || crewTasks;
        bool skipWords = playerNames.Contains(plain) || t.Pointer == Mask.PublicIntroCount;
        Checked++;
        bool hit = false;
        if (!skipWords)
        {
            string words = crewTasks ? plain[CrewAppearance.TaskLine.Length..] : plain;
            foreach (var role in roles)
                if (ContainsWord(words, role.word)) { hit = true; break; }
        }
        hit |= IsRoleColor(t.color, crew);
        if (!hit && s.Contains('<'))
            foreach (Match match in colorTags.Matches(s))
                if (ColorUtility.TryParseHtmlString(match.Groups["value"].Value, out var tagColor) && IsRoleColor(tagColor, crew))
                { hit = true; break; }
        // Die Vertex-Stichprobe bezieht sich auf das ganze TMP-Objekt. Ausgenommene Spielmodus-Glyphen bleiben dabei außen vor.
        if (!hit && s == original) hit = InspectVertices(t, crew);
        if (!hit) return;
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plain)));
        if (allowed.Contains(hash) || reported.Count >= 2048 || !reported.Add(hash)) return;
        lastHash = hash;
        Hits++;
        // Keine Rollen, Namen, Textinhalte oder Objektpfade im Log.
        SmokescreenPlugin.Logger.LogWarning("Leckwaechter: sichtbarer Text mit Rollenwort oder Rollenfarbe, Treffer=" + Hits);
    }

    private static bool IsRoleColor(Color color, bool crew)
    {
        if (color.a < 0.01f) return false;
        float chroma = Math.Max(color.r, Math.Max(color.g, color.b)) - Math.Min(color.r, Math.Min(color.g, color.b));
        if (chroma <= 0.3f || (crew && Near(color, CrewAppearance.IntroColor))) return false;
        foreach (var role in roles) if (Near(color, role.value)) return true;
        return false;
    }

    private static bool Near(Color a, Color b) =>
        Math.Abs(a.r - b.r) + Math.Abs(a.g - b.g) + Math.Abs(a.b - b.b) < 0.04f;

    private static bool InspectVertices(TMP_Text t, bool crew)
    {
        if (crew && RenderState.HasTintedTextMaterial(t.fontSharedMaterial, color => IsRoleColor(color, true))) return true;
        var info = t.textInfo;
        if (info == null || info.characterCount == 0) return false;
        var characters = info.characterInfo;
        var meshes = info.meshInfo;
        if (characters == null || meshes == null) return false;
        int length = Math.Min(characters.Length, info.characterCount);
        for (int sample = 0; sample < Math.Min(length, 4); sample++)
        {
            var ch = characters[length <= 4 ? sample : sample * (length - 1) / 3];
            if (!ch.isVisible || ch.materialReferenceIndex < 0 || ch.materialReferenceIndex >= meshes.Length) continue;
            var colors = meshes[ch.materialReferenceIndex].colors32;
            if (colors == null || ch.vertexIndex < 0 || ch.vertexIndex + 3 >= colors.Length) continue;
            for (int v = 0; v < 4; v++)
                if (IsRoleColor(colors[ch.vertexIndex + v], crew)) return true;
        }
        return false;
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
