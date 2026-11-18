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
    internal static bool MaskConfirmed => PatchesReady && MaskFresh;
    internal static void ConfirmMask()
    {
        maskAt = Time.realtimeSinceStartup;
        crewRendered = SmokescreenPlugin.CrewVision.Value && VisionMask.Ready;
    }
    internal static void InvalidateMask() => maskAt = -100;
    internal static string PublicStatus => Texts.T(!Shield.Active ? "protection.wait" : MaskConfirmed ? "protection.mask.ok" : "protection.mask.check");
    internal static string Details
    {
        get
        {
            if (!SmokescreenPlugin.Enabled.Value) return Texts.T("protection.off");
            if (!SettingsMenu.PatchedAtStartup) return Texts.T("protection.restart");
            if (!PatchesReady) return Texts.T("protection.patch");
            if (SmokescreenPlugin.PrivateOverlay.Value && OverlayHost.AffinityFailure is { } failure)
                return (Shield.Active && MaskFresh ? Texts.T("protection.mask.ok") + " · " : "") + failure.Description;
            if (!Shield.Wanted) return Texts.T("protection.streamer");
            if (!Shield.Active) return Texts.T(OverlayHost.Verified ? "protection.wait.verified" : "protection.wait.inactive");
            if (!MaskFresh) return Texts.T("protection.mask.pending");
            if (!SmokescreenPlugin.PrivateOverlay.Value) return Texts.T("protection.hud.off");
            if (!OverlayHost.Verified) return Texts.T("protection.hud.pending");
            if (OverlayHost.HiddenByUser) return Texts.T("protection.hud.hidden");
            if (!OverlayHost.GameForeground) return Texts.T("protection.hud.paused");
            if (Environment.TickCount64 - OverlayHost.LastCapture > 1500 || !OverlayHost.Shown)
                return Texts.T("protection.hud.stale");
            if (SmokescreenPlugin.CrewVision.Value && !crewRendered) return Texts.T("protection.vision");
            if (OverlayHost.FeedWanted) return Texts.T("protection.feed");
            if (!SmokescreenPlugin.HideButtons.Value || !SmokescreenPlugin.HideFlashes.Value
                || !SmokescreenPlugin.PrivateChat.Value || !SmokescreenPlugin.PrivateMeetings.Value
                || !SmokescreenPlugin.NeutralAudio.Value)
                return Texts.T("protection.partial");
            return Texts.T("protection.ready");
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
            if (LeakGuard.Failed) message += " | " + Texts.T("protection.leak.failed");
            else if (LeakGuard.Hits > 0) message += " | " + Texts.F("protection.leak.hits", LeakGuard.Hits);
        }
        if (Text.text != message) Text.text = message;
        var color = SmokescreenPlugin.LeakWatch.Value && (LeakGuard.Failed || LeakGuard.Hits > 0)
            ? new Color(1, 0.6f, 0.2f) : Color.white;
        if (Text.color != color) Text.color = color;
    }
}
