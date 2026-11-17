// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using System;
using TheOtherRoles;
using TheOtherRoles.Patches;
using UnityEngine;
using static TheOtherRoles.TheOtherRoles;

namespace UnknownsSmokescreen;

internal static class VisionMask
{
    private static LightSource light;
    private static float saved;
    private static bool applied;
    internal static float RealRadius, CrewRadius;
    internal static bool Ready;
    internal static bool Different => Ready && SmokescreenPlugin.CrewVision.Value && Math.Abs(RealRadius - CrewRadius) > 0.005f;

    internal static void Prepare()
    {
        Ready = false;
        if (!Shield.Active || !SmokescreenPlugin.CrewVision.Value) return;
        var options = GameOptionsManager.Instance;
        if (options == null || options.currentNormalGameOptions == null) return;
        var local = PlayerControl.LocalPlayer;
        var ship = ShipStatus.Instance;
        if (!RenderState.Alive(local) || !RenderState.Alive(ship)) return;
        if (!RenderState.Alive(light) || !light.transform.IsChildOf(local.transform))
            light = local.GetComponentInChildren<LightSource>(true);
        if (!RenderState.Alive(light) || !RenderState.Alive(light.LightCutawayMaterial)) return;
        RealRadius = light.ViewDistance;
        // TORs neutraler Crew-Pfad enthaelt CrewLightMod, Electrical und die optionale Kartenintegration.
        CrewRadius = ShipStatusPatch.GetNeutralLightRadius(ship, false);
        if (Trickster.trickster != null && Trickster.lightsOutTimer > 0f)
        {
            float fade = 1f;
            if (Trickster.lightsOutDuration - Trickster.lightsOutTimer < 0.5f)
                fade = Mathf.Clamp01((Trickster.lightsOutDuration - Trickster.lightsOutTimer) * 2f);
            else if (Trickster.lightsOutTimer < 0.5f) fade = Mathf.Clamp01(Trickster.lightsOutTimer * 2f);
            CrewRadius = Mathf.Lerp(ship.MinLightRadius, ship.MaxLightRadius, 1 - fade) * options.currentNormalGameOptions.CrewLightMod;
        }
        // Keine Lighter-, Lawyer-, Sonnenbrillen- oder Neutral-Killer-Ausnahme fuer die Kamera.
        Ready = float.IsFinite(RealRadius) && float.IsFinite(CrewRadius) && CrewRadius >= 0;
    }

    internal static void Apply()
    {
        if (applied || !Different) return;
        saved = light.ViewDistance;
        applied = true;
        try { Set(CrewRadius); }
        catch (Exception)
        {
            Ready = false;
            Restore();
        }
    }

    private static void Set(float radius)
    {
        if (!RenderState.Alive(light)) return;
        light.SetViewDistance(radius);
        // Aktualisiert Material und Schattengeometrie vor dem Kamerastapel, ohne Rollenwerte zu aendern.
        light.Update();
    }

    internal static void Restore()
    {
        if (!applied) return;
        applied = false;
        Set(saved);
    }
}
