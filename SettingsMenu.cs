// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
//
// Eigenes Einstellungsmenue im Stil der Among-Us-Optionen. Erreichbar ueber einen Knopf im Optionsmenue
// (Hauptmenue, Lobby, Esc-Menue im Spiel) und den Menue-Hotkey. Es zeigt nur Einstellungen und neutrale
// Statuszeilen, nie Rollen- oder Teaminformationen, und liegt deshalb normal im Spielfenster.

using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace UnknownsSmokescreen;

internal static class SettingsMenu
{
    private sealed class Entry
    {
        public GameObject Root;
        public SpriteRenderer Background;
        public TextMeshPro Text;
        public Func<string> Label;
        public Func<Color> Color;
        public string Hint;
        public bool Hover;
        public int Fitted;
        public float Width;
    }

    private static GameObject popup;
    private static ToggleButtonBehaviour prefab;
    private static TextMeshPro title, status, hint;
    private static readonly List<Entry> entries = new();
    private static ConfigEntry<KeyCode> listening;
    private static int consumedFrame = -1;
    private static string hover;
    private enum Page { General, Privacy, Capture, Viewers, Hud }
    private static Page page;
    internal static bool PatchedAtStartup = true;

    // In diesem Frame hat das Menue eine Taste verbraucht (Belegung oder Umschalten).
    internal static bool KeyConsumed => consumedFrame == Time.frameCount;
    internal static bool IsOpen => Alive(popup) && popup.activeSelf;
    internal static bool TakesHotkeys => listening != null || KeyConsumed || ChatOpen();
    private static bool Alive(Object o) => RenderState.Alive(o);

    private static readonly Color On = new(0.18f, 0.72f, 0.24f), OnHover = new(0.12f, 0.5f, 0.16f);
    private static readonly Color Off = new(0.78f, 0.16f, 0.16f), OffHover = new(0.52f, 0.1f, 0.1f);
    private static readonly Color Warn = new(1f, 0.55f, 0.05f), WarnHover = new(0.75f, 0.38f, 0.02f);
    private static readonly Color Neutral = Color.white, NeutralHover = new(0.6f, 0.6f, 0.6f);

    // Jede Taste ausser Maustasten; nur waehrend einer Belegung abgefragt.
    private static KeyCode[] keys;

    internal static void Tick()
    {
        SettingsMenuButton.Layout();
        try
        {
            if (listening != null) { Listen(); return; }
            if (IsOpen && Input.GetKeyDown(KeyCode.Escape)) { consumedFrame = Time.frameCount; Close(); return; }
            if (Input.GetKeyDown(SmokescreenPlugin.MenuHotkey.Value) && !ChatOpen())
            {
                consumedFrame = Time.frameCount;
                if (IsOpen) Close(); else Open();
                return;
            }
            if (Input.GetKeyDown(SmokescreenPlugin.ProfileHotkey.Value)
                && !ChatOpen() && (OverlayHost.GameForeground || CapturePopup.ForegroundGame() != IntPtr.Zero))
            {
                consumedFrame = Time.frameCount;
                Profiles.Cycle();
            }
            if (IsOpen) Refresh();
        }
        catch (Exception e) { SmokescreenPlugin.Logger.LogWarning("Einstellungsmenue: " + e.Message); }
    }

    private static bool ChatOpen()
    {
        try
        {
            var hud = HudManager.InstanceExists ? HudManager.Instance : null;
            return Alive(hud) && Alive(hud.Chat) && hud.Chat.IsOpenOrOpening;
        }
        catch { return false; }
    }

    internal static void Open()
    {
        if (!Ensure()) return;
        var hud = HudManager.InstanceExists ? HudManager.Instance : null;
        if (Alive(hud) && hud.gameObject.activeInHierarchy)
        {
            popup.transform.SetParent(hud.transform, false);
            popup.transform.localPosition = new Vector3(0, 0, -850f);
            if (Alive(hud.GameMenu) && hud.GameMenu.IsOpen) hud.GameMenu.Close();
        }
        else
        {
            popup.transform.SetParent(null, false);
            Object.DontDestroyOnLoad(popup);
            popup.transform.localPosition = new Vector3(0, 0, -810f);
        }
        popup.SetActive(true);
        Refresh();
    }

    internal static void Close()
    {
        listening = null;
        if (Alive(popup)) popup.SetActive(false);
    }

    private static OptionsMenuBehaviour FindMenu()
    {
        if (HudManager.InstanceExists && Alive(HudManager.Instance.GameMenu)) return HudManager.Instance.GameMenu;
        foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<OptionsMenuBehaviour>()))
        {
            var m = o.TryCast<OptionsMenuBehaviour>();
            if (Alive(m) && Alive(m.CensorChatButton) && Alive(m.Background)) return m;
        }
        return null;
    }

    private static bool Ensure()
    {
        if (Alive(popup) && entries.Count > 0) return true;
        var menu = FindMenu();
        if (menu == null) { SmokescreenPlugin.Logger.LogWarning("Einstellungsmenue: kein Optionsmenue als Vorlage gefunden"); return false; }
        if (!Alive(prefab))
        {
            prefab = Object.Instantiate(menu.CensorChatButton);
            Object.DontDestroyOnLoad(prefab);
            prefab.name = "SmokescreenButtonPrefab";
            prefab.gameObject.SetActive(false);
        }
        Build(menu);
        return true;
    }

    // Klon des Vanilla-Optionsfensters: nur Hintergrund und Schliessen-Knopf bleiben, darauf eigene Knoepfe.
    private static void Build(OptionsMenuBehaviour menu)
    {
        if (Alive(popup))
        {
            popup.SetActive(false);
            Object.Destroy(popup);
        }
        entries.Clear();
        popup = Object.Instantiate(menu.gameObject);
        popup.name = "SmokescreenSettings";
        Object.DontDestroyOnLoad(popup);
        popup.SetActive(false);
        Object.Destroy(popup.GetComponent<OptionsMenuBehaviour>());
        for (int i = popup.transform.childCount - 1; i >= 0; i--)
        {
            var child = popup.transform.GetChild(i).gameObject;
            if (child.name == "CloseButton")
            {
                var close = child.GetComponent<PassiveButton>();
                if (Alive(close)) { close.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent(); close.OnClick.AddListener((Action)Close); }
            }
            else if (child.name != "Background") Object.Destroy(child);
        }

        title = Label("Title", new Vector3(0, 2.32f, -1), 3.2f, Color.white);
        title.text = "Unknown's Smokescreen";
        status = Label("Status", new Vector3(0, 1.82f, -1), 1.5f, new Color(0.85f, 0.85f, 0.85f));
        status.enableWordWrapping = true;
        status.overflowMode = TextOverflowModes.Ellipsis;
        status.fontSizeMin = 1f;
        status.fontSizeMax = 1.5f;
        status.enableAutoSizing = true;
        hint = Label("Hint", new Vector3(0, -2.42f, -1), 1.35f, new Color(0.85f, 0.85f, 0.85f));
        hint.rectTransform.sizeDelta = new Vector2(5.2f, 0.6f);
        hint.enableWordWrapping = true;

        string[] pages = { "menu.general", "menu.privacy", "menu.capture", "menu.viewers", "menu.hud" };
        string[] names = { "Page", "PrivacyPage", "CapturePage", "ViewersPage", "HudPage" };
        int tab = 0;
        for (int i = 0; i < pages.Length; i++)
        {
            var destination = (Page)i;
            if (destination == page) continue;
            string label = pages[i];
            string name = destination == Page.Privacy && page == Page.General ? "Page" : names[i];
            Button(name, -1.77f + 1.18f * tab++, -1.9f, 1.14f, () => T(label),
                () => Neutral, "menu.page.hint", () => ShowPage(destination));
        }
        if (page == Page.Privacy)
        {
            Choice(-1.17f, 1.25f, "vision", SmokescreenPlugin.CrewVision);
            Toggle(1.17f, 1.25f, () => T("menu.leak"), SmokescreenPlugin.LeakWatch, "menu.leak.hint");
            Choice(-1.17f, 0.62f, "chat", SmokescreenPlugin.PrivateChat);
            Toggle(1.17f, 0.62f, () => T("menu.meeting"), SmokescreenPlugin.PrivateMeetings, "menu.meeting.hint");
            Choice(-1.17f, -0.01f, "audio", SmokescreenPlugin.NeutralAudio);
            Toggle(1.17f, -0.01f, () => T("menu.diagnostics"), SmokescreenPlugin.DiagnosticMode, "menu.diagnostics.hint");
            Button("Allowlist", 0, -0.64f, 4.4f, () => T("menu.allow"), () => Neutral, "menu.allow.hint", LeakGuard.AllowLast);
            Button("AllowClear", 0, -1.27f, 4.4f, () => T("menu.allow.clear"), () => Neutral, "menu.allow.hint", () => SmokescreenPlugin.LeakAllowlist.Value = "");
        }
        else if (page == Page.Capture) BuildCapture();
        else if (page == Page.Viewers) BuildViewers();
        else if (page == Page.Hud) BuildHud();
        else
        {
        float[] row = { 1.25f, 0.62f, -0.01f, -0.64f, -1.27f };
        Toggle(-1.17f, row[0], () => T("menu.enabled"), SmokescreenPlugin.Enabled, "menu.enabled.hint");
        Toggle(1.17f, row[0], () => T("menu.streamer"), SmokescreenPlugin.OnlyInStreamerMode, "menu.streamer.hint");
        Toggle(-1.17f, row[1], () => T("menu.buttons"), SmokescreenPlugin.HideButtons, "menu.buttons.hint");
        Toggle(1.17f, row[1], () => T("menu.flashes"), SmokescreenPlugin.HideFlashes, "menu.flashes.hint");
        Toggle(-1.17f, row[2], () => T("menu.overlay"), SmokescreenPlugin.PrivateOverlay, "menu.overlay.hint");
        Toggle(1.17f, row[2], () => T("menu.feed.permission"), SmokescreenPlugin.RecordingFeed, "menu.feed.hint", warning: true);
        KeyButton(-1.17f, row[3], "menu.key.overlay", SmokescreenPlugin.OverlayHotkey);
        KeyButton(1.17f, row[3], "menu.key.menu", SmokescreenPlugin.MenuHotkey);
        Button("Minus", -2.0f, row[4], 0.55f, () => "-", () => Neutral, "menu.fps.hint", () => Step(SmokescreenPlugin.OverlayFps, -1, 5, 30));
        Button("Rate", -1.17f, row[4], 1.05f, () => SmokescreenPlugin.OverlayFps.Value + " fps", () => Neutral, "menu.fps.hint", null);
        Button("Plus", -0.34f, row[4], 0.55f, () => "+", () => Neutral, "menu.fps.hint", () => Step(SmokescreenPlugin.OverlayFps, 1, 5, 30));
        }
        // Ueber anderen HUD-Elementen (z. B. Hilfe-Knoepfe anderer Mods) zeichnen.
        foreach (var r in popup.GetComponentsInChildren<Renderer>(true)) r.sortingOrder = Math.Max(r.sortingOrder, 0) + 2000;
        SmokescreenPlugin.Logger.LogInfo("Einstellungsmenue aufgebaut (" + entries.Count + " Elemente)");
    }

    private static void ShowPage(Page next)
    {
        var menu = FindMenu();
        if (!Alive(menu)) return;
        page = next;
        hover = null;
        Build(menu);
        Open();
    }

    private static void BuildCapture()
    {
        Toggle(-1.17f, 1.25f, () => T("menu.capture.watch.label"), SmokescreenPlugin.ObsWatch, "menu.capture.watch");
        Button("SelfTest", 1.17f, 1.25f, 2.2f, () => T(CaptureSelfTest.Running ? "menu.capture.testing" : "menu.capture.start"),
            () => Neutral, "menu.capture.test", CaptureSelfTest.Start);
        CaptureVerdict(-1.55f, "GDI", r => r.Gdi);
        CaptureVerdict(0, "DXGI", r => r.Dxgi);
        CaptureVerdict(1.55f, "WGC", r => r.Wgc);
        Button("UnlockFeed", -1.17f, -0.01f, 2.2f, () => T(SmokescreenPlugin.FeedEnabled ? "menu.feed.lock" : "menu.feed.unlock"),
            () => SmokescreenPlugin.FeedEnabled ? Warn : SmokescreenPlugin.FeedAllowed ? Neutral : Off,
            "menu.capture.unlock", () =>
            {
                if (SmokescreenPlugin.FeedAllowed)
                    SmokescreenPlugin.SetFeedUnlocked(!SmokescreenPlugin.FeedUnlocked);
            });
        Toggle(1.17f, -0.01f, () => T("menu.capture.replay.label"), SmokescreenPlugin.DelayedReplay, "menu.capture.replay", () =>
        {
            if (SmokescreenPlugin.DelayedReplay.Value) return;
            DelayedRecording.Reset();
            OverlayContent.ReleaseReplay();
        });
        Button("ExportReplay", 0, -0.64f, 4.4f, () => T("menu.capture.export.label"),
            () => DelayedRecording.CanExport ? On : Neutral, "menu.capture.export", DelayedRecording.Export);
        Button("RetryPrivateHud", 0, -1.27f, 4.4f, () => T("menu.capture.retry"),
            () => OverlayHost.ProtectionFailed && SmokescreenPlugin.PrivateOverlay.Value ? Warn : Neutral,
            "menu.capture.retry.hint", () =>
            {
                if (OverlayHost.ProtectionFailed && SmokescreenPlugin.PrivateOverlay.Value) OverlayChanged();
            });
    }

    private static void BuildHud()
    {
        Button("Profile", -1.17f, 1.25f, 2.2f, () => T("menu.profile." + Profiles.Current),
            () => Neutral, "menu.profile.hint", Profiles.Cycle);
        KeyButton(1.17f, 1.25f, "menu.key.profile", SmokescreenPlugin.ProfileHotkey);
        Choice(-1.17f, 0.62f, "hold", SmokescreenPlugin.HoldHud);
        Button("ProfileDetails", 0, -0.64f, 4.4f, () => T("menu.profile.detail." + Profiles.Current),
            () => Neutral, "menu.profile.hint", null);
    }

    private static void CaptureVerdict(float x, string name, Func<CaptureSelfTest.Report, CaptureSelfTest.Verdict> get)
    {
        CaptureSelfTest.Verdict Verdict() => CaptureSelfTest.Results is { } result && !CaptureSelfTest.Running
            ? get(result) : CaptureSelfTest.Verdict.Untested;
        Button("Test" + name, x, 0.62f, 1.45f, () => name + " " + T(Verdict() switch
        {
            CaptureSelfTest.Verdict.Excluded => "menu.capture.excluded",
            CaptureSelfTest.Verdict.Visible => "menu.capture.visible",
            _ => "menu.capture.untested"
        }), () => Verdict() switch
        {
            CaptureSelfTest.Verdict.Excluded => On,
            CaptureSelfTest.Verdict.Visible => Off,
            _ => Neutral
        }, "menu.capture.result", null);
    }

    private static void BuildViewers()
    {
        Toggle(0, 1.25f, () => T("menu.viewers.server.label"), SmokescreenPlugin.ViewerPage, "menu.viewers.server");
        Button("ViewerPortMinus", -1.75f, 0.62f, 0.85f, () => "-", () => Neutral, "menu.viewers.port", () => StepPort(-1));
        Button("ViewerPort", 0, 0.62f, 2.55f, () => "Port " + SmokescreenPlugin.ViewerPort.Value, () => Neutral, "menu.viewers.port", null);
        Button("ViewerPortPlus", 1.75f, 0.62f, 0.85f, () => "+", () => Neutral, "menu.viewers.port", () => StepPort(1));
        Toggle(-1.17f, -0.01f, () => T("menu.viewers.explanation.label"), SmokescreenPlugin.ViewerNotice, "menu.viewers.explanation");
        Button("ViewerPosition", 1.17f, -0.01f, 2.2f,
            () => T(SmokescreenPlugin.ViewerNoticePosition.Value == ViewerPosition.Top ? "menu.viewers.top" : "menu.viewers.bottom"),
            () => Neutral, "menu.viewers.position", () => SmokescreenPlugin.ViewerNoticePosition.Value =
                SmokescreenPlugin.ViewerNoticePosition.Value == ViewerPosition.Top ? ViewerPosition.Bottom : ViewerPosition.Top);
        Button("ViewerAddress", 0, -0.64f, 4.4f, () => ViewerServer.Address,
            () => ViewerServer.Running ? On : ViewerServer.Failed ? Off : Neutral, "menu.viewers.server", null);
    }

    private static void StepPort(int direction)
    {
        int delta = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 100 : 1;
        int port = Math.Clamp(SmokescreenPlugin.ViewerPort.Value + direction * delta, 1024, 65535);
        SmokescreenPlugin.ViewerPort.Value = port == 8765 ? port + direction : port;
    }

    private static TextMeshPro Label(string name, Vector3 position, float size, Color color)
    {
        var text = Object.Instantiate(prefab.Text, popup.transform);
        text.name = name;
        foreach (var tr in text.GetComponents<TextTranslatorTMP>()) Object.Destroy(tr);
        text.transform.localPosition = position;
        text.transform.localScale = Vector3.one;
        text.alignment = TextAlignmentOptions.Center;
        text.enableAutoSizing = false;
        text.characterSpacing = 0;
        text.fontSize = size;
        text.color = color;
        var rect = text.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(5.4f, 0.5f);
        text.margin = Vector4.zero;
        text.gameObject.SetActive(true);
        return text;
    }

    private static void Toggle(float x, float y, Func<string> label, ConfigEntry<bool> entry, string hintKey, Action changed = null, bool warning = false)
    {
        Button(entry.Definition.Key == "Enabled" && entry.Definition.Section == "Diagnostics" ? "Diagnostics" : entry.Definition.Key, x, y, 2.2f, () => label() + ": " + T(entry.Value ? "menu.on" : "menu.off"),
            () => entry.Value ? (warning ? Warn : On) : Off, hintKey, () => { entry.Value = !entry.Value; changed?.Invoke(); });
    }

    private static void Choice(float x, float y, string key, ConfigEntry<bool> entry)
    {
        string prefix = "menu." + key;
        Button(entry.Definition.Key, x, y, 2.2f, () => T(prefix + (entry.Value ? ".on" : ".off")),
            () => Neutral, prefix + ".hint", () => entry.Value = !entry.Value);
    }

    private static void KeyButton(float x, float y, string labelKey, ConfigEntry<KeyCode> entry)
    {
        Button(entry.Definition.Key, x, y, 2.2f,
            () => listening == entry ? T("menu.key.press") : T(labelKey) + ": " + entry.Value,
            () => Neutral, labelKey + ".hint", () => { listening = entry; consumedFrame = Time.frameCount; });
    }

    private static void Button(string name, float x, float y, float width, Func<string> label, Func<Color> color, string hintKey, Action click)
    {
        var button = Object.Instantiate(prefab, popup.transform);
        button.name = "Smokescreen" + name;
        var entry = new Entry { Root = button.gameObject, Background = button.Background, Text = button.Text, Label = label, Color = color, Hint = hintKey };
        // Eigene Texte und Farben: Uebersetzer, Rollover und Umschaltlogik des Vanilla-Knopfs entfernen.
        foreach (var tr in button.GetComponentsInChildren<TextTranslatorTMP>(true)) Object.Destroy(tr);
        if (Alive(button.Rollover)) Object.Destroy(button.Rollover);
        Object.Destroy(button);
        var transform = entry.Root.transform;
        transform.localPosition = new Vector3(x, y, -0.5f);
        transform.localScale = Vector3.one;
        // Wie die Vanilla-Schalter: eine Zeile, Groesse wird beim Setzen des Textes an die Breite angepasst.
        entry.Text.enableAutoSizing = false;
        entry.Text.fontSize = MaxFont;
        entry.Width = Math.Max(width - 0.5f, width * 0.6f);
        entry.Text.enableWordWrapping = false;
        entry.Text.characterSpacing = 0;
        var rect = entry.Text.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(width - 0.3f, 0.45f);
        entry.Text.margin = Vector4.zero;
        entry.Text.overflowMode = TextOverflowModes.Overflow;
        entry.Text.alignment = TextAlignmentOptions.Center;
        entry.Text.color = Color.white;
        foreach (var sprite in entry.Root.GetComponentsInChildren<SpriteRenderer>(true)) sprite.size = new Vector2(width, 0.52f);
        var collider = entry.Root.GetComponent<BoxCollider2D>();
        if (Alive(collider)) collider.size = new Vector2(width, 0.52f);
        var passive = entry.Root.GetComponent<PassiveButton>();
        passive.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
        passive.OnMouseOver = new UnityEvent();
        passive.OnMouseOut = new UnityEvent();
        if (click != null) passive.OnClick.AddListener((Action)(() => { click(); Refresh(); }));
        passive.OnMouseOver.AddListener((Action)(() => { entry.Hover = true; hover = entry.Hint; Refresh(); }));
        passive.OnMouseOut.AddListener((Action)(() => { entry.Hover = false; if (hover == entry.Hint) hover = null; Refresh(); }));
        entry.Root.SetActive(true);
        entries.Add(entry);
    }

    private const float MaxFont = 1.4f;
    private static void Fit(Entry e)
    {
        e.Text.fontSize = MaxFont;
        var preferred = e.Text.GetPreferredValues(e.Text.text);
        if (preferred.x > e.Width && preferred.x > 0) e.Text.fontSize = MaxFont * e.Width / preferred.x;
        e.Text.ForceMeshUpdate(false, false);
    }

    private static void Step(ConfigEntry<int> entry, int delta, int min, int max)
    {
        entry.Value = Math.Clamp(entry.Value + delta, min, max);
    }

    // PrivateOverlay und RecordingFeed legen Fenster an oder entfernen sie: Fensterthread neu starten.
    internal static void OverlayChanged()
    {
        if (!PatchedAtStartup) return;
        OverlayContent.Reset();
        OverlayHost.Restart(SmokescreenPlugin.PrivateOverlay.Value, SmokescreenPlugin.FeedEnabled);
    }

    private static void Listen()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) { listening = null; consumedFrame = Time.frameCount; Refresh(); return; }
        keys ??= BuildKeys();
        foreach (var key in keys)
        {
            if (!Input.GetKeyDown(key)) continue;
            listening.Value = key;
            listening = null;
            consumedFrame = Time.frameCount;
            Refresh();
            return;
        }
    }

    private static KeyCode[] BuildKeys()
    {
        var list = new List<KeyCode>();
        foreach (KeyCode key in Enum.GetValues(typeof(KeyCode)))
        {
            if (key == KeyCode.None || key == KeyCode.Escape || key >= KeyCode.Mouse0) continue;
            if (!list.Contains(key)) list.Add(key);
        }
        return list.ToArray();
    }

    internal static void Refresh()
    {
        if (!IsOpen) return;
        foreach (var e in entries)
        {
            if (!Alive(e.Root)) continue;
            string text = e.Label();
            bool changed = e.Text.text != text;
            if (changed) { e.Text.text = text; e.Fitted = 0; }
            var c = e.Color();
            if (e.Hover) c = c == On ? OnHover : c == Off ? OffHover : c == Warn ? WarnHover : NeutralHover;
            e.Background.color = c;
            if (e.Fitted == 0) { Fit(e); e.Fitted = 1; }
        }
        bool feed = SmokescreenPlugin.FeedEnabled;
        if (Alive(status))
        {
            status.text = feed ? T("menu.feed.warning.short") : Status();
            status.color = feed ? Warn : new Color(0.85f, 0.85f, 0.85f);
        }
        if (Alive(hint))
        {
            string text = hover != null ? T(hover) : feed ? T("menu.feed.warning") : T("menu.saved");
            if (hover == null && !feed && page == Page.Capture)
                text = DelayedRecording.Status ?? CaptureResultHint() ?? T("menu.capture.test");
            if (!feed && hover == "menu.capture.result") text = CaptureResultHint() ?? text;
            if (!feed && hover == "menu.capture.export" && DelayedRecording.Status is string exportStatus) text = exportStatus;
            if (hover == null && SmokescreenPlugin.PrivateOverlay.Value && OverlayHost.AffinityFailure is { } failure)
                text = failure.Description;
            hint.text = text;
            hint.color = hover == "menu.feed.hint" || (hover == null && feed) ? Warn : new Color(0.85f, 0.85f, 0.85f);
        }
    }

    // Neutral: nur Schutzzustand und Fensterzustand, nie Rolle oder Team.
    private static string Status()
    {
        return Protection.Details;
    }

    private static string T(string key) => Texts.T(key);
    private static string CaptureResultHint() => CaptureSelfTest.Results?.Message is { } key ? T(key) : null;
}

// Knopf "Smokescreen..." im Vanilla-Optionsmenue, neben dem Streamer-Modus-Schalter.
[HarmonyPatch(typeof(OptionsMenuBehaviour), nameof(OptionsMenuBehaviour.Start))]
internal static class SettingsMenuButton
{
    // Direkt unter der Reiterzeile (General, Graphics, Data) in der rechten Spalte: dort ist im Hauptmenue
    // und im Esc-Menue Platz, waehrend TOR und Vanilla die unteren Zeilen unterschiedlich belegen.
    // Das Optionsmenue oeffnet mit einer Skalierungsanimation; platziert wird erst bei stabiler Groesse.
    private sealed class Pending { public OptionsMenuBehaviour Menu; public GameObject Button; public SpriteRenderer Background; public Transform Anchor; public float Scale = -1; }
    private static readonly System.Collections.Generic.List<Pending> pending = new();

    internal static void Layout()
    {
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            var p = pending[i];
            if (!RenderState.Alive(p.Button) || !RenderState.Alive(p.Menu) || !RenderState.Alive(p.Anchor)) { pending.RemoveAt(i); continue; }
            if (!p.Menu.gameObject.activeInHierarchy) continue;
            float scale = p.Menu.transform.lossyScale.y;
            bool stable = scale > 0.001f && Math.Abs(scale - p.Scale) < scale * 0.001f;
            p.Scale = scale;
            if (!stable) continue;
            Place(p);
            pending.RemoveAt(i);
        }
    }

    private static void Place(Pending p)
    {
        var go = p.Button; var background = p.Background; var anchor = p.Anchor;
        var parent = go.transform.parent;
        var size = background.size;
        float height = size.y * 0.7f;
        background.size = new Vector2(size.x, height);
        var collider = go.GetComponent<BoxCollider2D>();
        if (RenderState.Alive(collider)) collider.size = new Vector2(collider.size.x, collider.size.y * 0.7f);
        float tabsBottom = float.MaxValue;
        if (p.Menu.Tabs != null)
            foreach (var tab in p.Menu.Tabs)
            {
                if (!RenderState.Alive(tab) || !tab.gameObject.activeInHierarchy) continue;
                foreach (var r in tab.GetComponentsInChildren<SpriteRenderer>())
                    if (r.enabled && r.gameObject.activeInHierarchy) tabsBottom = Math.Min(tabsBottom, r.bounds.min.y);
            }
        var local = anchor.localPosition;
        if (tabsBottom == float.MaxValue) local += new Vector3(0, 3.3f, 0);
        else
        {
            float bottom = parent.InverseTransformPoint(new Vector3(anchor.position.x, tabsBottom, anchor.position.z)).y;
            float own = height * go.transform.localScale.y;
            local = new Vector3(local.x, bottom - own * 0.75f, local.z);
        }
        go.transform.localPosition = local;
        go.SetActive(true);
        SmokescreenPlugin.Logger.LogInfo($"Optionsknopf: lokal y={local.y:F2}, Streamer-Modus y={anchor.localPosition.y:F2}");
    }

    public static void Postfix(OptionsMenuBehaviour __instance)
    {
        try
        {
            var anchor = __instance.StreamerModeButton;
            if (!RenderState.Alive(anchor) || anchor.transform.parent.Find("SmokescreenMenuButton") != null) return;
            var button = Object.Instantiate(anchor, anchor.transform.parent);
            var go = button.gameObject;
            go.name = "SmokescreenMenuButton";
            foreach (var tr in go.GetComponentsInChildren<TextTranslatorTMP>(true)) Object.Destroy(tr);
            var text = button.Text;
            var background = button.Background;
            if (RenderState.Alive(button.Rollover)) Object.Destroy(button.Rollover);
            Object.Destroy(button);
            text.text = "Smokescreen...";
            background.color = new Color(0.62f, 0.66f, 0.72f);
            text.color = Color.white;
            pending.Add(new Pending { Menu = __instance, Button = go, Background = background, Anchor = anchor.transform });
            var passive = go.GetComponent<PassiveButton>();
            passive.OnClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            passive.OnClick.AddListener((Action)(() => { __instance.Close(); SettingsMenu.Open(); }));
            passive.OnMouseOver = new UnityEvent();
            passive.OnMouseOut = new UnityEvent();
            passive.OnMouseOver.AddListener((Action)(() => background.color = new Color(0.45f, 0.48f, 0.53f)));
            passive.OnMouseOut.AddListener((Action)(() => background.color = new Color(0.62f, 0.66f, 0.72f)));
            go.SetActive(false);
        }
        catch (Exception e) { SmokescreenPlugin.Logger.LogWarning("Optionsknopf: " + e.Message); }
    }
}

internal static class SettingsMenuPlacement { }
