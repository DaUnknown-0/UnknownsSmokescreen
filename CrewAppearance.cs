// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using TheOtherRoles;
using UnityEngine;
namespace UnknownsSmokescreen;

// Eine feste öffentliche Rolle. Kein Zufall, Rollenpool, Teamfilter oder Wechselintervall.
internal static class CrewAppearance
{
    internal const string Name = "Crewmate";
    internal const string Description = "Find the Impostors";
    internal static string TaskLine => Name + ": " + Description;
    internal static Color IntroColor => Palette.CrewmateBlue;
    internal static string RealColored(RoleInfo role) => role == null ? "" : Helpers.cs(role.color, role.name);
}
