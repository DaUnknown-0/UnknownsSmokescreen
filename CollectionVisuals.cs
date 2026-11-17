// Copyright (C) 2026 DaUnknown-0. GPL-3.0-or-later.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
namespace UnknownsSmokescreen;
internal static class CollectionVisuals
{
    private sealed class Source { public FieldInfo Field, Object; }
    private static readonly List<Source> sources=new();
    private static bool initialized;
    private static void Add(string type,string field,string element=null)
    {
        var t=AccessTools.TypeByName("UnknownsCollection."+type);
        var f=t==null?null:AccessTools.Field(t,field);
        if(f==null) return;
        FieldInfo member=null;
        if(element!=null)
        {
            var args=f.FieldType.GetGenericArguments();
            if(args.Length==0) return;
            member=AccessTools.Field(args[args.Length-1],element);
            if(member==null)return;
        }
        sources.Add(new Source { Field=f,Object=member });
    }
    internal static void Apply()
    {
        if(!initialized)
        {
            initialized=true;
            Add("SaboteurTrap","traps","obj");
            Add("CollectorRelics","relics","go");
            Add("CollectorRelics","bursts","go");
            Add("CursedPirate","mapPoints");
            Add("Shade","bodyMarkers");
            Add("PelicanHud","bellyIcon");
            Add("GamblerUI","panel");
            Add("SaboteurScanUI","root");
            Add("ManiacDefuse","panel");
        }
        foreach(var source in sources)
        {
            var value=source.Field.GetValue(null);
            if(value is IDictionary dictionary) value=dictionary.Values;
            if(value is IEnumerable collection)
                foreach(var element in collection) Hide(source.Object==null?element:source.Object.GetValue(element));
            else Hide(value);
        }
    }
    private static void Hide(object value)
    {
        if(value is GameObject go) Mask.HideTree(go);
        else if(value is Renderer r) Mask.HideRenderer(r);
    }
}
