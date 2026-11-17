// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using System;
using TMPro;
using UnityEngine;

namespace UnknownsSmokescreen;

internal static class Protection
{
    internal static bool PatchesReady;
    private static float maskAt = -100;
    private static bool crewRendered;
    private static bool MaskFresh => Time.realtimeSinceStartup - maskAt < 1f;
    internal static void ConfirmMask()
    {
        maskAt = Time.realtimeSinceStartup;
        crewRendered = SmokescreenPlugin.CrewVision.Value && VisionMask.Ready;
    }
    internal static void InvalidateMask() => maskAt = -100;
    internal static string PublicStatus => !Shield.Active ? "Schutz wartet" : PatchesReady && MaskFresh ? "Maske aktiv" : "Maske prüfen";
    internal static string Details
    {
        get
        {
            if (!SmokescreenPlugin.Enabled.Value) return "Schutz aus";
            if (!SettingsMenu.PatchedAtStartup) return "Neustart erforderlich";
            if (!PatchesReady) return "Patchfehler, Schutz unvollständig";
            if (!Shield.Wanted) return "Streamer-Modus aus";
            if (!Shield.Active) return "Wartet auf Runde, " + (OverlayHost.Verified ? "Fenster verifiziert" : "Fenster inaktiv");
            if (!MaskFresh) return "Maskenrender unbestätigt";
            if (!SmokescreenPlugin.PrivateOverlay.Value) return "Maske aktiv, privates HUD aus";
            if (OverlayHost.ProtectionFailed) return "Maske aktiv, Fenstersperre fehlgeschlagen";
            if (!OverlayHost.Verified) return "Maske aktiv, Fenster unbestätigt";
            if (OverlayHost.HiddenByUser) return "Maske aktiv, privates HUD verborgen";
            if (!OverlayHost.GameForeground) return "Maske aktiv, privates HUD pausiert";
            if (Environment.TickCount64 - OverlayHost.LastCapture > 1500 || !OverlayHost.Shown)
                return "Maske aktiv, privates Bild fehlt oder ist veraltet";
            if (SmokescreenPlugin.CrewVision.Value && !crewRendered) return "Teilschutz, Crew-Sicht unbestätigt";
            if (OverlayHost.FeedWanted) return "Aufnehmbares Originalfenster aktiv";
            if (!SmokescreenPlugin.HideButtons.Value || !SmokescreenPlugin.HideFlashes.Value
                || !SmokescreenPlugin.PrivateChat.Value || !SmokescreenPlugin.PrivateMeetings.Value
                || !SmokescreenPlugin.NeutralAudio.Value)
                return "Teilschutz, Maskenoptionen prüfen";
            return "Maske und privates HUD bestätigt, OBS ungeprüft";
        }
    }
}

internal static class PrivacyNotice
{
    internal static TextMeshPro Text;
    internal static void Clear()
    {
        if (RenderState.Alive(Text))
        {
            Text.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(Text.gameObject);
        }
        Text = null;
    }

    internal static void Tick(bool active)
    {
        if (!active || OverlayContent.ActiveMinigame != IntPtr.Zero)
        {
            if (RenderState.Alive(Text) && Text.gameObject.activeSelf) Text.gameObject.SetActive(false);
            return;
        }
        var hud = HudManager.Instance;
        if (!RenderState.Alive(hud)) return;
        if (!RenderState.Alive(Text))
        {
            var template = hud.TaskPanel != null ? hud.TaskPanel.taskText : null;
            if (!RenderState.Alive(template)) return;
            Text = UnityEngine.Object.Instantiate(template, hud.transform);
            Text.name = "SmokescreenPrivateNotice";
            foreach (var tr in Text.GetComponents<TextTranslatorTMP>()) UnityEngine.Object.Destroy(tr);
            Text.transform.localPosition = new Vector3(0, -2.05f, -100f);
            Text.transform.localScale = Vector3.one;
            Text.rectTransform.sizeDelta = new Vector2(6, 0.45f);
            Text.enableAutoSizing = true;
            Text.fontSizeMin = 0.8f;
            Text.fontSizeMax = 1.1f;
            Text.alignment = TextAlignmentOptions.Center;
            Text.enableWordWrapping = false;
        }
        if (!Text.gameObject.activeSelf) Text.gameObject.SetActive(true);
        var message = Protection.Details;
        if (SmokescreenPlugin.LeakWatch.Value)
        {
            if (LeakGuard.Failed) message += " | Leckprüfung gestört";
            else if (LeakGuard.Hits > 0) message += " | Leckverdacht: " + LeakGuard.Hits;
        }
        if (Text.text != message) Text.text = message;
        var color = SmokescreenPlugin.LeakWatch.Value && (LeakGuard.Failed || LeakGuard.Hits > 0)
            ? new Color(1, 0.6f, 0.2f) : Color.white;
        if (Text.color != color) Text.color = color;
    }
}
