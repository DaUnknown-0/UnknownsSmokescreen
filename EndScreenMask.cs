// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TheOtherRoles;
using TMPro;
using UnityEngine;
namespace UnknownsSmokescreen;

internal static class EndScreenMask
{
    private static EndGameManager screen;
    private static bool protectedRound;
    private static readonly Il2CppSystem.Collections.Generic.List<GameObject> roots = new();
    private static readonly List<TMP_Text> texts = new();
    private static readonly Dictionary<IntPtr,string> replacements = new();
    private static int refreshFrame=-1;
    private static string summary;
    internal static bool Active => protectedRound && RenderState.Alive(screen) && screen.gameObject.activeInHierarchy;
    internal static void Begin(EndGameManager manager)
    {
        Shield.Invalidate();
        screen=manager;protectedRound=RoundRecap.Protected;refreshFrame=-1;texts.Clear();replacements.Clear();summary=null;
    }
    internal static void Refresh()
    {
        if(!Active || refreshFrame==Time.frameCount)return;
        // Der Endbildschirm ist klein. Auch spät erzeugte Statistik-/Rollenklone
        // werden vor ihrem ersten Render erfasst, unabhängig von Patch-Reihenfolgen.
        refreshFrame=Time.frameCount;texts.Clear();
        SceneRoots.Fill(screen.gameObject.scene, roots);
        foreach(var root in roots)
            foreach(var text in root.GetComponentsInChildren<TMP_Text>(true)) texts.Add(text);
        if(summary==null)
        {
            var sb=new StringBuilder();
            sb.AppendLine(Texts.T("recap.title"));
            foreach(var name in RoundRecap.Names.Values)sb.AppendLine(StreamNames.Plain(name)+": "+CrewAppearance.Name);
            summary=sb.ToString().TrimEnd();
        }
    }
    internal static void Apply()
    {
        Refresh();
        foreach(var text in texts)
        {
            if(!RenderState.Alive(text))continue;
            string original=text.text??"";
            if(original.Length==0)continue;
            string value;
            if(!replacements.TryGetValue(text.Pointer,out value))
            {
                if(text.name=="SmokescreenRecap")value=Texts.T("recap.title")+"\n"+Texts.T("recap.role")+": "+CrewAppearance.Name;
                else
                {
                    string plain=StreamNames.Plain(original);
                    string first=plain.Split('\n')[0].Trim();
                    bool player=RoundRecap.Names.Values.Any(n=>string.Equals(StreamNames.Plain(n),first,StringComparison.Ordinal));
                    if(player&&plain.Contains('\n'))value=first+"\n"+CrewAppearance.Name;
                    else if(text.name == screen.WinText.name + "(Clone)" || ContainsRole(plain)) value=plain.Contains('\n')?summary:CrewAppearance.Name;
                    else continue;
                }
                replacements[text.Pointer]=value;
            }
            RenderState.Text(text);text.text=value;text.color=Color.white;
        }
        if(RenderState.Alive(screen.WinText)) { RenderState.Text(screen.WinText); screen.WinText.color=CrewAppearance.IntroColor; }
        if(RenderState.Alive(screen.BackgroundBar))
            RenderState.Color(screen.BackgroundBar.material,Shader.PropertyToID("_Color"),CrewAppearance.IntroColor);
    }
    private static bool ContainsRole(string text)
    {
        if(text.IndexOf("Players and roles",StringComparison.OrdinalIgnoreCase)>=0)return true;
        foreach(var role in RoleInfo.allRoleInfos)
            if(role!=null&&!string.IsNullOrWhiteSpace(role.name)&&text.IndexOf(role.name,StringComparison.OrdinalIgnoreCase)>=0)return true;
        return text.IndexOf("Impostor",StringComparison.OrdinalIgnoreCase)>=0;
    }
}
