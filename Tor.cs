// Unknown's Smokescreen - Copyright (C) 2026 DaUnknown-0. Licensed under GPL-3.0-or-later.
//
// Access to the parts of TOR that are not public (TORMapOptions, GameHistory, CustomOptionHolder),
// resolved once by reflection. Everything returns a harmless default when TOR changes underneath.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TheOtherRoles;

namespace UnknownsSmokescreen;

internal static class Tor
{
    private static readonly Type mapOptions = AccessTools.TypeByName("TheOtherRoles.TORMapOptions");
    private static readonly Type gameHistory = AccessTools.TypeByName("TheOtherRoles.GameHistory");

    private static readonly FieldInfo fHideNames = Field(mapOptions, "hidePlayerNames");
    private static readonly FieldInfo fGhostsSeeRoles = Field(mapOptions, "ghostsSeeRoles");
    private static readonly FieldInfo fGhostsSeeInformation = Field(mapOptions, "ghostsSeeInformation");
    private static readonly FieldInfo fPlayerIcons = Field(mapOptions, "playerIcons");
    private static readonly FieldInfo fGameMode = Field(mapOptions, "gameMode");
    private static readonly FieldInfo fDeadPlayers = Field(gameHistory, "deadPlayers");

    private static FieldInfo Field(Type t, string name)
    {
        var f = t != null ? AccessTools.Field(t, name) : null;
        if (f == null) SmokescreenPlugin.Logger?.LogWarning($"TOR field not found: {t?.Name}.{name}");
        return f;
    }

    public static bool HidePlayerNames => fHideNames?.GetValue(null) is bool b && b;

    public static bool GhostsSeeRoles => fGhostsSeeRoles?.GetValue(null) is bool b && b;
    public static bool GhostsSeeInformation => fGhostsSeeInformation?.GetValue(null) is bool b && b;

    public static CustomGamemodes GameMode =>
        fGameMode?.GetValue(null) is CustomGamemodes m ? m : CustomGamemodes.Classic;

    public static Dictionary<byte, PoolablePlayer> PlayerIcons =>
        fPlayerIcons?.GetValue(null) as Dictionary<byte, PoolablePlayer>;

    public static List<DeadPlayer> DeadPlayers =>
        fDeadPlayers?.GetValue(null) as List<DeadPlayer>;

}
