// Unknown's Smokescreen - Copyright (C) 2026 DaUnknown-0
// Licensed under GPL-3.0-or-later. See LICENSE for details.
// Works on top of The Other Roles (https://github.com/TheOtherRolesAU/TheOtherRoles), GPL-3.0.

// Waehrend der Runde zeigt das Spielfenster ausschliesslich Crewmate. Die tatsächliche Rolle und der echte HUD
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
    public const string Version = "0.3.0.7";

    internal static ManualLogSource Logger;

    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<bool> OnlyInStreamerMode;
    internal static ConfigEntry<bool> HideButtons;
    internal static ConfigEntry<bool> HideFlashes;
    internal static ConfigEntry<bool> PrivateOverlay;
    internal static ConfigEntry<bool> RecordingFeed;
    internal static ConfigEntry<KeyCode> OverlayHotkey;
    internal static ConfigEntry<KeyCode> MenuHotkey;
    internal static ConfigEntry<int> OverlayFps;
    internal static ConfigEntry<int> SyncFrames;
    internal static ConfigEntry<bool> DiagnosticMode;
    internal static ConfigEntry<bool> CrewVision, LeakWatch, PrivateChat, PrivateMeetings, NeutralAudio;
    internal static ConfigEntry<string> LeakAllowlist;

    public override void Load()
    {
        Logger = Log;
        CrewVision = Config.Bind("Mask", "CrewVision", false, "Crew-Sicht für Spielfenster und privates HUD. Der separate RecordingFeed behält den Originalradius.");
        PrivateChat = Config.Bind("Mask", "PrivateChat", true, "Gesamten Chat mit Eingabe, Hinweisen und Knopf nur privat zeigen.");
        PrivateMeetings = Config.Bind("Mask", "PrivateMeetings", true, "Guesser-Auswahl, Swapper und Mayor-Auswahl nur privat zeigen.");
        NeutralAudio = Config.Bind("Audio", "NeutralRoleSounds", true, "Private TOR-Faehigkeitstoene lokal unterdruecken, auch fuer den Spieler.");
        LeakWatch = Config.Bind("Diagnostics", "LeakWatch", true, "Maskierte sichtbare Texte begrenzt auf Rollennamen und Rollenfarben pruefen.");
        LeakAllowlist = Config.Bind("Diagnostics", "LeakAllowlist", "", "SHA256-Prüfsummen exakt erlaubter Texte, getrennt mit |.");

        Enabled = Config.Bind("General", "Enabled", true, "Load the mod at all.");
        OnlyInStreamerMode = Config.Bind("General", "OnlyInStreamerMode", true,
            "true: protect only while Among Us' own Streamer Mode is on (Settings > Gameplay). false: always protect.");
        HideButtons = Config.Bind("Mask", "HideRoleButtons", true,
            "Role buttons (kill, sabotage, vent, every TOR ability) are invisible but keep working (click spot and hotkey).");
        HideFlashes = Config.Bind("Mask", "HideRoleFlashes", true,
            "Hide the coloured screen flashes of role abilities (Seer, Snitch, Medic ...). The result screen remains unchanged.");
        Config.Bind("Recap", "ShowRoundRecap", true, "Veralteter Eintrag");
        Config.Remove(new ConfigDefinition("Recap", "ShowRoundRecap"));

        PrivateOverlay = Config.Bind("Overlay", "PrivateOverlay", true,
            "Echtes HUD pixelgenau über dem Spiel anzeigen. Erfordert verifiziertes WDA_EXCLUDEFROMCAPTURE (Windows 10 2004 oder neuer).");
        RecordingFeed = Config.Bind("Overlay", "RecordingFeed", false,
            "Open an extra window with the same content that OBS CAN capture, for a separate local recording you upload later " +
            "(e.g. OBS plugin Source Record or a second OBS instance). Never put it into the live scene and keep it off the streamed display.");
        OverlayHotkey = Config.Bind("Overlay", "ToggleKey", KeyCode.F8, "Privates HUD einblenden oder ausblenden. Die Spielmaskierung bleibt aktiv.");
        MenuHotkey = Config.Bind("General", "MenuKey", KeyCode.F3, "Opens the Smokescreen settings menu (also reachable through the game's options menu).");
        OverlayFps = Config.Bind("Overlay", "FramesPerSecond", 15, new ConfigDescription("Bildrate des privaten HUD, 5 bis 30.", new AcceptableValueRange<int>(5, 30)));
        SyncFrames = Config.Bind("Overlay", "SyncFrames", 1, new ConfigDescription("Frames the game image is shown later than the private HUD. Names and labels that move with players use the position from this many frames ago (0 to 4).", new AcceptableValueRange<int>(0, 4)));
        DiagnosticMode = Config.Bind("Diagnostics", "Enabled", false, "Expliziter Diagnoselauf, maximal 180 Sekunden. Kein Umgehen von Fokus oder Aufnahmeschutz.");

        // Alte Schlüssel zunächst binden, damit sie aus den verwaisten Config-Einträgen
        // übernommen und beim anschließenden Speichern vollständig entfernt werden.
        Config.Bind("Mask", "ShuffleMode", "", "Veralteter Eintrag");
        Config.Remove(new ConfigDefinition("Mask", "ShuffleMode"));
        Config.Bind("Mask", "ShuffleGhostInfo", true, "Veralteter Eintrag");
        Config.Remove(new ConfigDefinition("Mask", "ShuffleGhostInfo"));
        Config.Save();
        RegisterInModManager();
        ClassInjector.RegisterTypeInIl2Cpp<SmokescreenRunner>();
        ClassInjector.RegisterTypeInIl2Cpp<CameraMaskScope>();
        AddComponent<SmokescreenRunner>();
        if (!Enabled.Value)
        {
            // Nur das Einstellungsmenue, damit der Schutz dort wieder eingeschaltet werden kann.
            SettingsMenu.PatchedAtStartup = false;
            try { new Harmony(Guid).CreateClassProcessor(typeof(SettingsMenuButton)).Patch(); }
            catch (Exception e) { Logger.LogError("patch SettingsMenuButton failed: " + e.Message); }
            Logger.LogInfo($"{Name} is disabled in its config, only the settings menu is active.");
            return;
        }
        OverlayHost.Start(PrivateOverlay.Value, RecordingFeed.Value);
        // Class by class: one patch that does not fit this game or TOR version must not take the others down.
        var harmony = new Harmony(Guid);
        int ok = 0, failed = 0, corePatched = 0;
        foreach (var type in typeof(SmokescreenPlugin).Assembly.GetTypes())
        {
            if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
            try
            {
                var methods = harmony.CreateClassProcessor(type).Patch();
                ok += methods?.Count ?? 0;
                if (methods?.Count > 0 && (type == typeof(IntroStart)
                    || type == typeof(IntroReset) || type == typeof(RestoreBeforeGameLogic))) corePatched++;
            }
            catch (Exception e)
            {
                failed++;
                Logger.LogError($"patch {type.Name} failed: {e.Message}");
            }
        }
        Protection.PatchesReady = corePatched == 3 && failed == 0;
        Logger.LogInfo($"{Name} v{Version} loaded ({ok} patches{(failed > 0 ? $", {failed} FAILED" : "")}).");
    }

    public override bool Unload()
    {
        new Harmony(Guid).UnpatchSelf();
        PrivacyNotice.Clear();
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
    internal static bool ResultScreen;
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
            if (ResultScreen) return false;
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
        ["menu.general"] = ("General", "Allgemein"),
        ["menu.privacy"] = ("Privacy", "Privatsphäre"),
        ["menu.page.hint"] = ("Switch settings page.", "Einstellungsseite wechseln."),
        ["menu.vision.on"] = ("Vision: Crew", "Sicht: Crew"),
        ["menu.vision.off"] = ("Vision: Original", "Sicht: Original"),
        ["menu.leak"] = ("Leak watch", "Leckwächter"),
        ["menu.chat.on"] = ("Chat: Private", "Chat: Privat"),
        ["menu.chat.off"] = ("Chat: Public", "Chat: Öffentlich"),
        ["menu.meeting"] = ("Private meetings", "Meeting privat"),
        ["menu.audio.on"] = ("Role sounds: Muted", "Rollentöne: Stumm"),
        ["menu.audio.off"] = ("Role sounds: Audible", "Rollentöne: Hörbar"),
        ["menu.allow"] = ("Allow last flagged text", "Letzten Treffer erlauben"),
        ["menu.allow.clear"] = ("Clear allowlist", "Ausnahmen löschen"),
        ["menu.vision.hint"] = ("Crew hides your real vision range from viewers. You also see only the crew radius.", "Crew verbirgt deine Sichtweite vor Zuschauern. Auch du siehst nur den Crew-Radius."),
        ["menu.leak.hint"] = ("Samples masked visible text for role words and colours. Reports privately.", "Prueft maskierte Texte begrenzt auf Rollenwoerter und Farben. Meldung privat."),
        ["menu.chat.hint"] = ("Private hides even meeting chat from viewers. Public role logs and team chats can reveal roles.", "Privat verbirgt auch den Meeting-Chat vor Zuschauern. Öffentliche Rollenlogs und Teamchats können Rollen verraten."),
        ["menu.meeting.hint"] = ("Guesser selection, Swapper and Mayor controls become private.", "Guesser-Auswahl, Swapper und Mayor-Bedienung werden privat."),
        ["menu.audio.hint"] = ("Muting known TOR role sounds silences them for you too. Public alarms stay audible.", "Stumm schaltet bekannte TOR-Rollentöne auch für dich aus. Öffentliche Alarme bleiben hörbar."),
        ["menu.allow.hint"] = ("Allow the last flagged text by hash, or clear the allowlist.", "Letzten Treffer per Prüfsumme erlauben oder Ausnahmen löschen."),
        ["menu.on"] = ("On", "An"),
        ["menu.off"] = ("Off", "Aus"),
        ["menu.enabled"] = ("Protection", "Schutz"),
        ["menu.enabled.hint"] = ("Masks your role for the camera in protected rounds. Off: the game window shows everything.", "Maskiert deine Rolle für die Kamera in geschützten Runden. Aus: das Spielfenster zeigt alles."),
        ["menu.streamer"] = ("Streamer-only", "Nur Streamer"),
        ["menu.streamer.hint"] = ("On: protect only while the Among Us Streamer Mode is on (Settings, General). Off: always protect.", "An: nur schützen, solange der Streamer-Modus von Among Us an ist. Aus: immer schützen."),
        ["menu.buttons"] = ("Hide role buttons", "Rollenknöpfe verbergen"),
        ["menu.buttons.hint"] = ("Kill, sabotage, vent and ability buttons are invisible on stream but keep working.", "Kill-, Sabotage-, Vent- und Fähigkeitsknöpfe sind im Stream unsichtbar, funktionieren aber weiter."),
        ["menu.flashes"] = ("Hide role flashes", "Rollenblitze verbergen"),
        ["menu.flashes.hint"] = ("Hides coloured screen flashes of role abilities from the camera.", "Verbirgt farbige Bildschirmblitze von Rollenfähigkeiten vor der Kamera."),
        ["menu.overlay"] = ("Private HUD", "Privates HUD"),
        ["menu.overlay.hint"] = ("Shows you the real HUD on top of the game. Windows excludes it from capture, OBS cannot see it.", "Zeigt dir das echte HUD über dem Spiel. Windows schließt es von Aufnahmen aus, OBS sieht es nicht."),
        ["menu.feed"] = ("Recording feed", "Aufnahme-Feed"),
        ["menu.feed.hint"] = ("WARNING: opens a capturable window with your REAL view for a separate local recording. Never add it to a live scene.", "WARNUNG: öffnet ein aufnehmbares Fenster mit deiner ECHTEN Sicht für eine getrennte lokale Aufnahme. Nie in eine Live-Szene aufnehmen."),
        ["menu.feed.warning"] = ("Recording feed is ON: the feed window shows your real view. Keep it out of every live scene.", "Aufnahme-Feed ist AN: das Feed-Fenster zeigt deine echte Sicht. Aus jeder Live-Szene heraushalten."),
        ["menu.key.overlay"] = ("HUD key", "HUD-Taste"),
        ["menu.key.overlay.hint"] = ("Key that shows or hides the private HUD. Click, then press a key (Esc cancels).", "Taste zum Ein- und Ausblenden des privaten HUD. Klicken, dann Taste drücken (Esc bricht ab)."),
        ["menu.key.menu"] = ("Menu key", "Menütaste"),
        ["menu.key.menu.hint"] = ("Key that opens this menu. Click, then press a key (Esc cancels).", "Taste, die dieses Menü öffnet. Klicken, dann Taste drücken (Esc bricht ab)."),
        ["menu.key.press"] = ("Press a key...", "Taste drücken..."),
        ["menu.fps.hint"] = ("How often the private HUD content is refreshed (5 to 30). Its position follows every frame.", "Wie oft der Inhalt des privaten HUD erneuert wird (5 bis 30). Die Lage folgt jedem Frame."),
        ["menu.sync"] = ("HUD sync", "HUD-Sync"),
        ["menu.sync.frames"] = ("{0} frames", "{0} Frames"),
        ["menu.sync.hint"] = ("If private names or labels run ahead of players while walking, increase; if they trail, decrease. Applies instantly.", "Laufen private Namen beim Gehen den Spielern voraus, erhöhen; hängen sie hinterher, verringern. Wirkt sofort."),
        ["menu.diagnostics"] = ("Diagnostics", "Diagnose"),
        ["menu.diagnostics.hint"] = ("Logs overlay timings for 180 seconds of the next protected round. Only for troubleshooting.", "Protokolliert 180 Sekunden lang Overlay-Zeiten der nächsten geschützten Runde. Nur zur Fehlersuche."),
        ["menu.saved"] = ("Changes apply immediately and are saved to the config file.", "Änderungen wirken sofort und werden in die Config gespeichert."),
        ["menu.status.off"] = ("Protection: off", "Schutz: aus"),
        ["menu.status.restart"] = ("Protection: restart Among Us to load it", "Schutz: Among Us neu starten, um ihn zu laden"),
        ["menu.status.streamer"] = ("Protection: Streamer Mode is off", "Schutz: Streamer-Modus ist aus"),
        ["menu.status.active"] = ("Protection: active", "Schutz: aktiv"),
        ["menu.status.waiting"] = ("Protection: ready for the next round", "Schutz: bereit für die nächste Runde"),
        ["menu.overlay.disabled"] = ("Private HUD: off", "Privates HUD: aus"),
        ["menu.overlay.failed"] = ("Private HUD: not available on this system", "Privates HUD: auf diesem System nicht verfügbar"),
        ["menu.overlay.ok"] = ("Private HUD: capture-protected", "Privates HUD: aufnahmegeschützt"),
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
        LeakGuard.ObservePing(__instance.text);
        string text = __instance.text.text;
        if (string.IsNullOrEmpty(text)) return;

        string line = $"<color=#B4BCC8>{SmokescreenPlugin.Name} v{SmokescreenPlugin.Version}</color>";
        if (Shield.Wanted) line += " <color=#B4BCC8>(" + Protection.PublicStatus + ")</color>";
        UnknownsCollective.Contribute(SmokescreenPlugin.Guid, line);
        text = UnknownsCollective.Render(__instance.text, text);
        text = HudNotice.Append(text);
        if (!string.Equals(__instance.text.text, text, StringComparison.Ordinal))
            __instance.text.text = text;
    }
}
