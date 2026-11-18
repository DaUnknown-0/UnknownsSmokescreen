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
    public const string Version = "0.3.0.8";

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
    internal static ConfigEntry<bool> ObsWatch, ViewerPage, DelayedReplay, ViewerNotice;
    internal static ConfigEntry<int> ViewerPort;
    internal static ConfigEntry<ViewerPosition> ViewerNoticePosition;
    internal static bool FeedUnlocked { get; private set; }
    internal static bool FeedEnabled => FeedUnlocked && RecordingFeed.Value;

    internal static void SetFeedUnlocked(bool enabled)
    {
        FeedUnlocked = enabled && SettingsMenu.PatchedAtStartup;
        SettingsMenu.OverlayChanged();
    }

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
            "Erlaubt die manuelle Freigabe des aufnehmbaren Originalfensters für diese Sitzung.");
        FeedUnlocked = false;
        ObsWatch = Config.Bind("Capture", "ObsWatch", true, "Warnt privat bei erkannten Aufnahmeprogrammen und Schutzproblemen.");
        ViewerPage = Config.Bind("Viewers", "BrowserSource", false, "Lokale OBS-Browserquelle mit Auswertung nach Rundenende.");
        ViewerPort = Config.Bind("Viewers", "Port", ViewerServer.DefaultPort,
            new ConfigDescription("Port der Browserquelle auf 127.0.0.1. Port 8765 ist reserviert.", new AcceptableValueRange<int>(1024, 65535)));
        if (ViewerPort.Value == 8765) ViewerPort.Value = ViewerServer.DefaultPort;
        DelayedReplay = Config.Bind("Capture", "DelayedReplay", false, "Die letzten 30 Sekunden der Originalsicht im Speicher halten. Export erst nach Rundenende.");
        ViewerNotice = Config.Bind("Viewers", "Explanation", false, "Rollenfreien Hinweis im geschützten Spielfenster anzeigen.");
        ViewerNoticePosition = Config.Bind("Viewers", "ExplanationPosition", ViewerPosition.Top, "Position des öffentlichen Zuschauerhinweises.");
        PrivateOverlay.SettingChanged += (_, _) => SettingsMenu.OverlayChanged();
        RecordingFeed.SettingChanged += (_, _) =>
        {
            if (!RecordingFeed.Value) SetFeedUnlocked(false);
        };
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
        OverlayHost.Start(PrivateOverlay.Value, false);
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
        CaptureWatch.Stop();
        ViewerServer.Stop();
        DelayedRecording.Reset();
        ViewerExplanation.Clear();
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
    private static bool german;
    private static long languageAfter;

    private static bool German
    {
        get
        {
            long now = Environment.TickCount64;
            if (now < languageAfter) return german;
            languageAfter = now + 500;
            try { german = AmongUs.Data.DataManager.Settings.Language.CurrentLanguage == SupportedLangs.German; }
            catch { german = false; }
            return german;
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
        ["menu.overlay.hint"] = ("Shows the real HUD in a Windows capture-excluded window. Verify your OBS source under Aufnahme.", "Zeigt das echte HUD in einem von Windows aufnahmegeschützten Fenster. Prüfe deine OBS-Quelle unter Aufnahme."),
        ["menu.feed"] = ("Recording feed", "Aufnahme-Feed"),
        ["menu.feed.hint"] = ("Allows manual session unlock under Aufnahme. The unlocked feed can reveal your real view.", "Erlaubt die manuelle Sitzungsfreigabe unter Aufnahme. Der freigegebene Feed zeigt die echte Sicht."),
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
        ["protection.wait"] = ("Protection waiting", "Schutz wartet"),
        ["protection.mask.ok"] = ("Mask active", "Maske aktiv"),
        ["protection.mask.check"] = ("Check mask", "Maske prüfen"),
        ["protection.off"] = ("Protection off", "Schutz aus"),
        ["protection.restart"] = ("Restart Among Us to load protection", "Among Us neu starten, um den Schutz zu laden"),
        ["protection.patch"] = ("Patch failed, protection incomplete", "Patchfehler, Schutz unvollständig"),
        ["protection.streamer"] = ("Streamer Mode off", "Streamer-Modus aus"),
        ["protection.wait.verified"] = ("Waiting for round, window verified", "Wartet auf Runde, Fenster verifiziert"),
        ["protection.wait.inactive"] = ("Waiting for round, window inactive", "Wartet auf Runde, Fenster inaktiv"),
        ["protection.mask.pending"] = ("Mask render unconfirmed", "Maskenrender unbestätigt"),
        ["protection.hud.off"] = ("Mask active, private HUD off", "Maske aktiv, privates HUD aus"),
        ["protection.hud.pending"] = ("Mask active, window unconfirmed", "Maske aktiv, Fenster unbestätigt"),
        ["protection.hud.hidden"] = ("Mask active, private HUD hidden", "Maske aktiv, privates HUD verborgen"),
        ["protection.hud.paused"] = ("Mask active, private HUD paused", "Maske aktiv, privates HUD pausiert"),
        ["protection.hud.stale"] = ("Mask active, private frame missing or stale", "Maske aktiv, privates Bild fehlt oder ist veraltet"),
        ["protection.hud.unavailable"] = ("Private display unavailable", "Private Anzeige nicht verfügbar"),
        ["protection.vision"] = ("Partial protection, crew vision unconfirmed", "Teilschutz, Crew-Sicht unbestätigt"),
        ["protection.feed"] = ("Capturable original feed active", "Aufnehmbares Originalfenster aktiv"),
        ["protection.partial"] = ("Partial protection, check mask options", "Teilschutz, Maskenoptionen prüfen"),
        ["protection.ready"] = ("Mask and private HUD confirmed, OBS untested", "Maske und privates HUD bestätigt, OBS ungeprüft"),
        ["protection.leak.failed"] = ("Leak check unavailable", "Leckprüfung gestört"),
        ["protection.leak.hits"] = ("Possible leaks: {0}", "Leckverdacht: {0}"),
        ["affinity.Wine"] = ("Private HUD unavailable on Wine or Proton. Use native Windows.", "Privates HUD unter Wine oder Proton nicht unterstützt. Natives Windows verwenden."),
        ["affinity.VersionQuery"] = ("Windows version check failed (0x{1:X8}). Update Windows and retry.", "Windows-Version nicht ermittelbar (0x{1:X8}). Windows aktualisieren und erneut versuchen."),
        ["affinity.OldWindows"] = ("Build {0}. Windows 10 2004 or later is required.", "Build {0}. Windows 10 2004 oder neuer nötig."),
        ["affinity.DwmQuery"] = ("Desktop composition check failed (0x{1:X8}). Sign in locally and retry.", "DWM-Abfrage fehlgeschlagen (0x{1:X8}). Lokal anmelden und erneut versuchen."),
        ["affinity.NoComposition"] = ("Desktop composition is off. Sign in locally and retry.", "Desktop-Komposition ist aus. Lokal anmelden und erneut versuchen."),
        ["affinity.WindowClass"] = ("Window class registration failed (Win32 {1}). Retry under Capture.", "Fensterklasse fehlgeschlagen (Win32 {1}). Unter Aufnahme erneut versuchen."),
        ["affinity.CreateWindow"] = ("Private window creation failed (Win32 {1}). Retry under Capture.", "Privates Fenster fehlt (Win32 {1}). Unter Aufnahme erneut versuchen."),
        ["affinity.SetAffinity"] = ("Windows rejected capture exclusion (Win32 {1}). Retry under Capture.", "Windows lehnt die Fenstersperre ab (Win32 {1}). Unter Aufnahme erneut versuchen."),
        ["affinity.ReadAffinity"] = ("Could not verify capture exclusion (Win32 {1}). Retry under Capture.", "Fenstersperre nicht prüfbar (Win32 {1}). Unter Aufnahme erneut versuchen."),
        ["affinity.WrongAffinity"] = ("Windows returned 0x{2:X} instead of 0x11. Retry under Capture.", "Windows meldet 0x{2:X} statt 0x11. Unter Aufnahme erneut versuchen."),
        ["affinity.LostRead"] = ("Capture exclusion check stopped working (Win32 {1}). Retry under Capture.", "Prüfung der Fenstersperre ist ausgefallen (Win32 {1}). Unter Aufnahme erneut versuchen."),
        ["affinity.LostAffinity"] = ("Capture exclusion was lost (0x{2:X}). Retry under Capture.", "Fenstersperre verloren (0x{2:X}). Unter Aufnahme erneut versuchen."),
        ["affinity.Thread"] = ("Private window stopped (0x{1:X8}). Retry under Capture.", "Privates Fenster ausgefallen (0x{1:X8}). Unter Aufnahme erneut versuchen."),
        ["affinity.remote"] = ("End Remote Desktop and try at the local console.", "Remote Desktop beenden und am lokalen Gerät erneut versuchen."),
        ["affinity.vm"] = ("Possible virtual machine. Try on the Windows host.", "Hinweis auf eine virtuelle Maschine. Auf dem Windows-Host prüfen."),
        ["menu.capture.retry"] = ("Retry private HUD", "Erneut versuchen"),
        ["menu.capture.retry.hint"] = ("Recreates the private HUD. It stays hidden until Windows verifies capture exclusion.", "Legt das private HUD neu an. Es bleibt bis zur bestätigten Fenstersperre verborgen."),
        ["menu.capture"] = ("Capture", "Aufnahme"),
        ["menu.viewers"] = ("Viewers", "Zuschauer"),
        ["menu.feed.permission"] = ("Allow feed", "Feed erlauben"),
        ["menu.feed.unlock"] = ("Unlock feed", "Feed freigeben"),
        ["menu.feed.lock"] = ("Lock feed", "Feed sperren"),
        ["menu.feed.warning.short"] = ("Feed unlocked. Original view is capturable.", "Aufnahme-Feed frei. Originalsicht aufnehmbar."),
        ["menu.capture.watch.label"] = ("OBS watcher", "OBS-Wächter"),
        ["menu.capture.testing"] = ("Testing ...", "Prüfe ..."),
        ["menu.capture.start"] = ("Start capture test", "Aufnahmetest starten"),
        ["menu.capture.replay.label"] = ("Buffer replay", "Rückblick puffern"),
        ["menu.capture.export.label"] = ("Export replay frames", "Rückblick als Bildfolge speichern"),
        ["menu.capture.excluded"] = ("excluded", "frei"),
        ["menu.capture.visible"] = ("visible", "sichtbar"),
        ["menu.capture.untested"] = ("untested", "ungeprüft"),
        ["menu.viewers.server.label"] = ("OBS browser page", "OBS-Browserseite"),
        ["menu.viewers.explanation.label"] = ("Viewer message", "Zuschauerhinweis"),
        ["menu.viewers.top"] = ("Position: Top", "Position: Oben"),
        ["menu.viewers.bottom"] = ("Position: Bottom", "Position: Unten"),
        ["capture.test.failed"] = ("Capture test failed.", "Aufnahmetest fehlgeschlagen."),
        ["capture.test.affinity"] = ("Enable the private HUD and verify capture exclusion first.", "Privates HUD zuerst einschalten und Fenstersperre prüfen."),
        ["capture.test.foreground"] = ("Keep the game in the foreground during the capture test.", "Spielfenster während des Aufnahmetests im Vordergrund lassen."),
        ["capture.test.window"] = ("Game window unavailable.", "Spielfenster nicht verfügbar."),
        ["capture.test.pattern"] = ("Could not show the test pattern.", "Prüfmuster konnte nicht dargestellt werden."),
        ["capture.test.changed"] = ("Window, position or protection changed during the test.", "Fenster, Position oder Schutz hat sich im Test geändert."),
        ["capture.watch.protection"] = ("Capture app detected. Round protection is not ready.", "Aufnahmeprogramm erkannt. Der Rundenschutz ist nicht bereit."),
        ["capture.watch.overlay"] = ("Capture app detected. Private HUD unavailable.", "Aufnahmeprogramm erkannt. Privates HUD derzeit nicht verfügbar."),
        ["capture.watch.detected"] = ("Capture app detected.", "Aufnahmeprogramm erkannt."),
        ["capture.watch.feed"] = ("The unlocked recording feed can show your real view.", "Der freigegebene Aufnahme-Feed kann die Originalsicht zeigen."),
        ["capture.replay.ready"] = ("Replay ready to export", "Rückblick zum Export bereit"),
        ["capture.replay.saving"] = ("Exporting replay frames", "Bildfolge wird gespeichert"),
        ["capture.replay.saved"] = ("Saved to BepInEx/SmokescreenReplays/{0}", "Gespeichert in BepInEx/SmokescreenReplays/{0}"),
        ["capture.replay.failed"] = ("Replay export failed", "Bildexport fehlgeschlagen"),
        ["menu.capture.watch"] = ("Warns privately when known capture software and a protection issue are detected.", "Warnt privat bei erkannten Aufnahmeprogrammen und Schutzproblemen."),
        ["menu.capture.test"] = ("Tests GDI, Desktop Duplication and Windows Graphics Capture on this monitor.", "Prüft GDI, Desktop Duplication und Windows Graphics Capture auf diesem Monitor."),
        ["menu.capture.result"] = ("Green applies to the tested monitor method. Verify Game Capture and Window Capture in your OBS scene.", "Grün gilt für den getesteten Bildschirmweg. Spiel- und Fensteraufnahme in der OBS-Szene prüfen."),
        ["menu.capture.unlock"] = ("First allow the feed under General. Unlock makes the real view capturable until you lock it or quit.", "Zuerst den Feed unter Allgemein erlauben. Die Freigabe macht die echte Sicht bis zur Sperre oder zum Beenden aufnehmbar."),
        ["menu.capture.replay"] = ("Keeps up to 30 seconds of the real camera view in memory in protected rounds.", "Hält bis zu 30 Sekunden der echten Kamerasicht in geschützten Runden im Speicher."),
        ["menu.capture.export"] = ("After the result screen, saves images and a player under BepInEx/SmokescreenReplays.", "Speichert nach dem Ergebnis Bilder und Player unter BepInEx/SmokescreenReplays."),
        ["menu.viewers.server"] = ("Add this address as an OBS Browser Source. The page shows names and roles after the round.", "Diese Adresse als OBS-Browserquelle eintragen. Nach Rundenende erscheinen Namen und Rollen."),
        ["menu.viewers.port"] = ("Click plus or minus to change the local port. Hold Shift for steps of 100.", "Plus oder Minus ändert den lokalen Port. Umschalt wechselt in Hunderterschritte."),
        ["menu.viewers.explanation"] = ("Shows a public explanation of the Crewmate display during protected rounds.", "Erklärt Zuschauern in geschützten Runden die feste Crewmate-Anzeige."),
        ["menu.viewers.position"] = ("Moves the public explanation to the top or bottom of the game window.", "Platziert die Zuschauer-Erklärung oben oder unten im Spielfenster."),
        ["viewers.explain.buttons"] = ("Crewmate display and hidden abilities for the stream. Roles revealed after the round.", "Crewmate-Anzeige und verborgene Fähigkeiten für den Stream. Auflösung nach der Runde."),
        ["viewers.explain.role"] = ("Crewmate display for the stream. Roles revealed after the round.", "Crewmate-Anzeige für den Stream. Auflösung nach der Runde."),
    };

    public static string T(string key)
    {
        if (!table.TryGetValue(key, out var v)) return key;
        return German ? v.de : v.en;
    }

    internal static string F(string key, params object[] values) => string.Format(T(key), values);
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
