using System;
using Newtonsoft.Json.Linq;
using OniMcp.Tools;
using UnityEngine;

internal static class TypedBuildContractRegression
{
    private static void Check(bool ok, string message)
    { if (!ok) throw new InvalidOperationException("Typed build contract: " + message); }

    internal static void Run()
    {
        BuildingPlans(); InfrastructurePlans(); NativeHeavyWireReplacement();
        Console.WriteLine("Typed build routing and native heavy-wire replacement regressions passed");
    }

    private static void BuildingPlans()
    {
        foreach (string prefab in new[] { "WireBridgeHighWattage", "WireBridge", "GasConduitBridge", "Tile" })
        {
            foreach (bool preview in new[] { true, false })
            {
                var args = JObject.Parse("{confirm:true,x:292,y:63,orientation:'R90',material:'AluminumOre',priority:8}");
                var result = WorldEditorTools.BuildCommandFixture(args, "buildings/plans.oni", prefab, preview);
                var routed = BuildingControlTools.LastBuildCommand;
                Check(!result.IsError && (string)routed["action"] == "build_area",
                    prefab + " must use building placement in both preview and commit");
                Check((string)routed["orientation"] == "R90" && (int)routed["x"] == 292
                    && (int)routed["y"] == 63 && (int)routed["priority"] == 8,
                    "typed building plan preserves anchor, rotation and priority");
                Check((bool?)routed["dryRun"] == (preview ? true : (bool?)null)
                    && (bool)routed["confirm"] == !preview,
                    "preview is forced read-only while commit preserves confirmation");
                if (preview) Check((bool)args["confirm"] && args["action"] == null,
                    "preview cannot mutate the original request");
            }
        }
    }

    private static void InfrastructurePlans()
    {
        foreach (bool preview in new[] { true, false })
        {
            var args = JObject.Parse("{confirm:true,prefabId:'HighWattageWire',material:'AluminumOre',priority:8}");
            var result = WorldEditorTools.BuildCommandFixture(args, "infrastructure/power.oni",
                "connect (292,60) -> (292,62) -> (295,62)", preview);
            var routed = BuildingControlTools.LastBuildCommand;
            Check(!result.IsError && (string)routed["action"] == "auto_connect"
                && (string)routed["prefabId"] == "HighWattageWire", "native 20kW infrastructure route remains supported");
            Check(((JArray)routed["points"]).Count == 3
                && (int)routed["points"][2][0] == 295 && (int)routed["points"][2][1] == 62,
                "infrastructure text becomes the exact requested path");
            Check((string)routed["material"] == "AluminumOre" && (int)routed["priority"] == 8,
                "infrastructure preserves material and construction priority");

            foreach (string unsupported in new[] { "WireHighWattage", "WireBridgeHighWattage", "GasConduit" })
            {
                int calls = BuildingControlTools.BuildCommandCalls;
                args = JObject.Parse("{confirm:true}"); args["prefabId"] = unsupported;
                result = WorldEditorTools.BuildCommandFixture(args, "infrastructure/power.oni",
                    "connect (1,2) -> (2,2)", preview);
                Check(result.IsError && calls == BuildingControlTools.BuildCommandCalls,
                    "wrong prefab cannot reach placement: " + unsupported);
            }
        }
    }

    private static void NativeHeavyWireReplacement()
    {
        int cell = Grid.XYToCell(10, 2);
        var oldDef = new BuildingDef { PrefabID = "Wire", WidthInCells = 1, HeightInCells = 1 };
        var wire = new GameObject { Cell = cell };
        wire.Components[typeof(Building)] = new Building { Def = oldDef };
        wire.Components[typeof(BuildingComplete)] = new BuildingComplete { Def = oldDef };
        var heavy = new BuildingDef { PrefabID = "HighWattageWire", TileLayer = ObjectLayer.WireTile,
            ReplacementLayer = ObjectLayer.ReplacementWire, ReplacementCandidate = wire };
        Check(BuildPlanningTools.ReplacementFixture(heavy, cell) == wire,
            "canonical heavy wire enters the native replacement route while retaining the old wire");
        heavy.NativeCanReplace = false;
        Check(BuildPlanningTools.ReplacementFixture(heavy, cell) == null, "native replacement tags remain required");
        heavy.NativeCanReplace = true; heavy.NativeLocationValid = false;
        Check(BuildPlanningTools.ReplacementFixture(heavy, cell) == null, "native replacement placement remains required");
        heavy.NativeLocationValid = true; heavy.ReplacementOccupied = true;
        Check(BuildPlanningTools.ReplacementFixture(heavy, cell) == null, "occupied replacement layer remains protected");
        heavy.ReplacementOccupied = false;
        Check(BuildPlanningTools.ReplacementFixture(heavy, cell, false) == null, "hidden replacement target stays rejected");
        oldDef.PrefabID = "LiquidConduit";
        Check(BuildPlanningTools.ReplacementFixture(heavy, cell) == null, "wire replacement cannot replace a pipe");
        oldDef.PrefabID = "Wire";
        var blueprint = new GameObject { Cell = cell };
        blueprint.Components[typeof(Constructable)] = new Constructable();
        blueprint.Components[typeof(Building)] = new Building { Def = heavy };
        Grid.Objects[cell, (int)ObjectLayer.ReplacementWire] = blueprint;
        try
        {
            Check(BuildPlanningTools.MatchingReplacementFixture(heavy, cell, wire),
                "replaying a canonical heavy-wire upgrade reuses the native replacement blueprint");
        }
        finally { Grid.Objects[cell, (int)ObjectLayer.ReplacementWire] = null; }
    }
}
