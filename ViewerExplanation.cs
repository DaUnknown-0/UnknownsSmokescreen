// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using TMPro;
using UnityEngine;

namespace UnknownsSmokescreen;

internal enum ViewerPosition { Top, Bottom }

internal static class ViewerExplanation
{
    private static TextMeshPro text;
    private static ViewerPosition position;

    internal static void Tick(bool active)
    {
        if (!active || !SmokescreenPlugin.ViewerNotice.Value || OverlayContent.ActiveMinigame != System.IntPtr.Zero
            || SettingsMenu.IsOpen)
        {
            if (RenderState.Alive(text) && text.gameObject.activeSelf) text.gameObject.SetActive(false);
            return;
        }
        var hud = HudManager.Instance;
        if (!RenderState.Alive(hud)) return;
        var wanted = SmokescreenPlugin.ViewerNoticePosition.Value;
        if (!RenderState.Alive(text))
        {
            var template = hud.TaskPanel != null ? hud.TaskPanel.taskText : null;
            if (!RenderState.Alive(template)) return;
            text = Object.Instantiate(template, hud.transform);
            text.name = "SmokescreenViewerExplanation";
            foreach (var translator in text.GetComponents<TextTranslatorTMP>()) Object.Destroy(translator);
            text.transform.localScale = Vector3.one;
            text.rectTransform.sizeDelta = new Vector2(6f, 0.45f);
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.enableAutoSizing = true;
            text.fontSizeMin = 0.85f;
            text.fontSizeMax = 1.1f;
            text.color = Color.white;
            position = (ViewerPosition)(-1);
        }
        if (position != wanted)
        {
            position = wanted;
            text.transform.localPosition = new Vector3(0, position == ViewerPosition.Top ? 2.4f : -2.4f, -100f);
        }
        string message = SmokescreenPlugin.HideButtons.Value
            ? Texts.T("viewers.explain.buttons") : Texts.T("viewers.explain.role");
        if (text.text != message) text.text = message;
        if (!text.gameObject.activeSelf) text.gameObject.SetActive(true);
    }

    internal static void Clear()
    {
        if (RenderState.Alive(text)) Object.Destroy(text.gameObject);
        text = null;
    }
}
