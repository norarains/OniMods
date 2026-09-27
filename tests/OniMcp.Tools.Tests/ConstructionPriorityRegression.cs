using System;
using System.Reflection;
using OniMcp.Tools;
using UnityEngine;

internal static class ConstructionPriorityRegression
{
    internal static void Run()
    {
        foreach (bool supported in new[] { true, false })
        foreach (bool marked in new[] { true, false })
        {
            var sourceObject = new GameObject();
            var source = sourceObject.AddOrGet<Constructable>();
            var identity = sourceObject.AddOrGet<KPrefabID>();
            var from = sourceObject.AddOrGet<Prioritizable>();
            if (marked) ConstructionPriorityIntent.Mark(sourceObject);
            Check(identity.SerializableTags.Count == (marked ? 1 : 0), "intent is persisted only for opted-in blueprints");
            // Simulate later UI reprioritization after the initial build request.
            from.Master = new PrioritySetting(PriorityScreen.PriorityClass.basic, 9);
            var finishedObject = new GameObject();
            var completed = finishedObject.AddOrGet<BuildingComplete>();
            var to = finishedObject.AddOrGet<Prioritizable>(); to.Supported = supported;
            typeof(ConstructionPriorityIntent).GetNestedType("Install", BindingFlags.NonPublic)
                .GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { completed });
            finishedObject.Trigger((int)GameHashes.NewConstruction, source);
            Check(to.Calls == (marked && supported ? 1 : 0), "only opted-in supported native priorities are changed");
            Check(to.Master.priority_value == (marked && supported ? 9 : 5), "completion uses current blueprint priority");
        }
        var ordinary = new GameObject(); ordinary.AddOrGet<KPrefabID>(); ConstructionPriorityIntent.Mark(ordinary);
        Check(ordinary.GetComponent<KPrefabID>().Tags.Count == 0, "marking non-blueprint cannot opt in an unrelated object");
        Console.WriteLine("Construction priority event regression checks passed");
    }
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException("Construction priority: " + message); }
}
