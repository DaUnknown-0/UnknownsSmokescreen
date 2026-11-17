// Unknown's Smokescreen - Copyright (C) 2026 DaUnknown-0
// Licensed under GPL-3.0-or-later. See LICENSE for details.
// Works on top of The Other Roles (https://github.com/TheOtherRolesAU/TheOtherRoles), GPL-3.0.

// Das Spielfenster zeigt ausschließlich Crewmate. Die tatsächliche Rolle und der echte HUD
// bleiben im privaten, aufnahmegeschützten Kamerabild erhalten. Keine eigenen RPCs.

global using Il2CppInterop.Runtime;
global using Il2CppInterop.Runtime.Injection;

using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace UnknownsSmokescreen;

[BepInPlugin(Guid, Name, Version)]
[BepInProcess("Among Us.exe")]
[BepInDependency("me.eisbison.theotherroles", BepInDependency.DependencyFlags.HardDependency)]
public class SmokescreenPlugin : BasePlugin
{
    public const string Guid = "com.daunknown.smokescreen";
    public const string Name = "Unknown's Smokescreen";
    public const string Version = "0.3.0.1";

    internal static ManualLogSource Logger;

    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<bool> OnlyInStreamerMode;
    internal static ConfigEntry<bool> HideButtons;
    internal static ConfigEntry<bool> HideFlashes;
    internal static ConfigEntry<bool> ShowRecap;
    internal static ConfigEntry<bool> PrivateOverlay;
    internal static ConfigEntry<bool> RecordingFeed;
    internal static ConfigEntry<KeyCode> OverlayHotkey;
    internal static ConfigEntry<int> OverlayFps;
    internal static ConfigEntry<bool> DiagnosticMode;

    public override void Load()
    {
        Logger = Log;

        Enabled = Config.Bind("General", "Enabled", true, "Load the mod at all.");
        OnlyInStreamerMode = Config.Bind("General", "OnlyInStreamerMode", true,
            "true: protect only while Among Us' own Streamer Mode is on (Settings > Gameplay). false: always protect.");
        HideButtons = Config.Bind("Mask", "HideRoleButtons", true,
            "Role buttons (kill, sabotage, vent, every TOR ability) are invisible but keep working (click spot and hotkey).");
        HideFlashes = Config.Bind("Mask", "HideRoleFlashes", true,
            "Hide the coloured screen flashes of role abilities (Seer, Snitch, Medic ...). They show up in the round recap.");
        ShowRecap = Config.Bind("Recap", "ShowRoundRecap", true,
            "Rückblick nach einer geschützten Runde: öffentlich Crewmate, echte Details nur privat.");

        PrivateOverlay = Config.Bind("Overlay", "PrivateOverlay", true,
            "Echtes HUD pixelgenau über dem Spiel anzeigen. Erfordert verifiziertes WDA_EXCLUDEFROMCAPTURE (Windows 10 2004 oder neuer).");
        RecordingFeed = Config.Bind("Overlay", "RecordingFeed", false,
            "Open an extra window with the same content that OBS CAN capture, for a separate local recording you upload later " +
            "(e.g. OBS plugin Source Record or a second OBS instance). Never put it into the live scene and keep it off the streamed display.");
        OverlayHotkey = Config.Bind("Overlay", "ToggleKey", KeyCode.F8, "Privates HUD einblenden oder ausblenden. Die Spielmaskierung bleibt aktiv.");
        OverlayFps = Config.Bind("Overlay", "FramesPerSecond", 30, new ConfigDescription("Bildrate des privaten HUD, 5 bis 30.", new AcceptableValueRange<int>(5, 30)));
        DiagnosticMode = Config.Bind("Diagnostics", "Enabled", false, "Expliziter Diagnoselauf, maximal 180 Sekunden. Kein Umgehen von Fokus oder Aufnahmeschutz.");

        // Alte Schlüssel zunächst binden, damit sie aus den verwaisten Config-Einträgen
        // übernommen und beim anschließenden Speichern vollständig entfernt werden.
        Config.Bind("Mask", "ShuffleMode", "", "Veralteter Eintrag");
        Config.Remove(new ConfigDefinition("Mask", "ShuffleMode"));
        Config.Bind("Mask", "ShuffleGhostInfo", true, "Veralteter Eintrag");
        Config.Remove(new ConfigDefinition("Mask", "ShuffleGhostInfo"));
        Config.Save();
        RegisterInModManager();
        if (!Enabled.Value)
        {
            Logger.LogInfo($"{Name} is disabled in its config, nothing patched.");
            return;
        }

        ClassInjector.RegisterTypeInIl2Cpp<SmokescreenRunner>();
        ClassInjector.RegisterTypeInIl2Cpp<CameraMaskScope>();
        AddComponent<SmokescreenRunner>();
        OverlayHost.Start(PrivateOverlay.Value, RecordingFeed.Value);
        // Class by class: one patch that does not fit this game or TOR version must not take the others down.
        var harmony = new Harmony(Guid);
        int ok = 0, failed = 0;
        foreach (var type in typeof(SmokescreenPlugin).Assembly.GetTypes())
        {
            if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
            try
            {
                harmony.CreateClassProcessor(type).Patch();
                ok++;
            }
            catch (Exception e)
            {
                failed++;
                Logger.LogError($"patch {type.Name} failed: {e.Message}");
            }
        }
        Logger.LogInfo($"{Name} v{Version} loaded ({ok} patches{(failed > 0 ? $", {failed} FAILED" : "")}).");
    }

    public override bool Unload()
    {
        new Harmony(Guid).UnpatchSelf();
        OverlayContent.Dispose();
        Mask.RestoreAll();
        OverlayHost.Stop();
        return true;
    }

    private void RegisterInModManager()
    {
        try
        {
            var modData = new Dictionary<string, object> {
                { "Guid", Guid },
                { "Name", Name },
                { "Version", System.Version.Parse(Version) },
                { "RepositoryOwner", "DaUnknown-0" },
                { "RepositoryName", "UnknownsSmokescreen" },
                { "ButtonColor", new Color(0.62f, 0.66f, 0.72f) },
                { "Enabled", Enabled },
                { "RuntimeEnabled", Enabled.Value }
            };
            AppDomain.CurrentDomain.SetData($"ModManager.RegisteredMod.{Guid}", modData);
        }
        catch (Exception e)
        {
            Logger.LogWarning($"Mod Manager registration failed: {e.Message}");
        }
    }
}

/// <summary>When the protection is on. Cached per frame, every patch asks this.</summary>
internal static class Shield
{
    private static int frame = -1;
    private static bool cached;
    internal static void Invalidate() => frame = -1;

    /// <summary>Streamer Mode (or "always") is on, independent of whether a round is running.</summary>
    public static bool Wanted
    {
        get
        {
            if (!SmokescreenPlugin.Enabled.Value) return false;
            if (!SmokescreenPlugin.OnlyInStreamerMode.Value) return true;
            try { return AmongUs.Data.DataManager.Settings.Gameplay.StreamerMode; } catch { return false; }
        }
    }

    /// <summary>Wanted AND a normal TOR round (Classic or Guesser) is running.</summary>
    public static bool Active
    {
        get
        {
            if (Time.frameCount == frame) return cached;
            frame = Time.frameCount;
            cached = Compute();
            return cached;
        }
    }

    private static bool Compute()
    {
        try
        {
            if (!Wanted) return false;
            if (EndScreenMask.Active) return true;
            var client = AmongUsClient.Instance;
            if (client == null || client.GameState != InnerNet.InnerNetClient.GameStates.Started) return false;
            var local = PlayerControl.LocalPlayer;
            if (local == null || local.Data == null || local.Data.Role == null) return false;
            if (GameOptionsManager.Instance.currentGameOptions.GameMode == AmongUs.GameOptions.GameModes.HideNSeek) return false;
            var mode = Tor.GameMode;
            return mode == TheOtherRoles.CustomGamemodes.Classic || mode == TheOtherRoles.CustomGamemodes.Guesser;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>A small German/English text table (the mod follows the game's language).</summary>
internal static class Texts
{
    private static bool German
    {
        get
        {
            try { return AmongUs.Data.DataManager.Settings.Language.CurrentLanguage == SupportedLangs.German; }
            catch { return false; }
        }
    }

    private static readonly Dictionary<string, (string en, string de)> table = new() {
        ["recap.title"] = ("Smokescreen: your round", "Smokescreen: deine Runde"),
        ["recap.role"] = ("Role", "Rolle"),
        ["recap.team"] = ("Team", "Team"),
        ["recap.kill"] = ("Kill", "Kill"),
        ["recap.ability"] = ("Ability", "Fähigkeit"),
        ["recap.died"] = ("You died", "Du bist gestorben"),
        ["recap.by"] = ("by", "durch"),
        ["recap.meeting"] = ("Meeting", "Meeting"),
        ["recap.flash"] = ("Flash", "Hinweis"),
        ["recap.more"] = ("... {0} more", "... {0} weitere"),
        ["death.Exile"] = ("voted out", "rausgewählt"),
        ["death.Kill"] = ("killed", "getötet"),
        ["death.Disconnect"] = ("disconnected", "getrennt"),
        ["death.Guess"] = ("guessed", "erraten"),
        ["death.Shift"] = ("shifted", "geshiftet"),
        ["death.LawyerSuicide"] = ("bad Lawyer", "schlechter Anwalt"),
        ["death.LoverSuicide"] = ("lover died", "Liebe gestorben"),
        ["death.WitchExile"] = ("witched", "verhext"),
        ["death.Bomb"] = ("bombed", "gesprengt"),
        ["death.Arson"] = ("burnt", "verbrannt"),
        ["hud.on"] = ("stream protected", "Stream geschützt"),
        ["overlay.private"] = ("only you see this", "nur du siehst das"),
        ["overlay.ghost"] = ("ghost", "Geist"),
        ["overlay.buttons"] = ("Buttons", "Knöpfe"),
        ["overlay.ready"] = ("ready", "bereit"),
        ["overlay.active"] = ("active", "aktiv"),
        ["overlay.blocked"] = ("not usable", "nicht nutzbar"),
        ["overlay.map"] = ("map", "Karte"),
        ["overlay.flash"] = ("flash", "Hinweis"),
        ["overlay.roles"] = ("Roles", "Rollen"),
        ["overlay.waiting"] = ("waiting for a protected round", "wartet auf eine geschützte Runde"),
    };

    public static string T(string key)
    {
        if (!table.TryGetValue(key, out var v)) return key;
        return German ? v.de : v.en;
    }
}

/// <summary>Version line in the PingTracker corner (shared "Unknown's Collective" block).</summary>
[HarmonyPatch(typeof(PingTracker), nameof(PingTracker.Update))]
internal static class SmokescreenVersionLine
{
    [HarmonyPriority(Priority.Low)]
    public static void Postfix(PingTracker __instance)
    {
        if (ReferenceEquals(__instance, null) || ReferenceEquals(__instance.text, null) || __instance.text.m_CachedPtr == IntPtr.Zero) return;
        string text = __instance.text.text;
        if (string.IsNullOrEmpty(text)) return;

        string line = $"<color=#B4BCC8>{SmokescreenPlugin.Name} v{SmokescreenPlugin.Version}</color>";
        if (Shield.Wanted) line += $" <color=#7CD992>({Texts.T("hud.on")})</color>";
        UnknownsCollective.Contribute(SmokescreenPlugin.Guid, line);
        text = UnknownsCollective.Render(__instance.text, text);
        text = HudNotice.Append(text);
        if (!string.Equals(__instance.text.text, text, StringComparison.Ordinal))
            __instance.text.text = text;
    }
}
