using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Tools;

internal static class UtilityPlacementRegression
{
    private static JObject Args() => JObject.Parse("{confirm:true,points:[[293,88],[292,88],[292,89]],prefabId:'GasConduit'}");
    private static JObject Body(CallToolResult result) => JObject.Parse(result.Content[0].Text);
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Utility placement: " + message); }

    internal static void Run()
    {
        foreach (bool? legacyNative in new bool?[] { null, true, false })
        {
            BuildPlanningTools.ResetPlacement();
            var args = Args();
            if (legacyNative.HasValue) args["nativePathPlacement"] = legacyNative.Value;
            var result = BuildPlanningTools.AutoConnectUtility().Handler(args);
            var body = Body(result);
            Check(!result.IsError && (bool)body["complete"], "complete exact L-shaped path");
            Check(BuildPlanningTools.UiCalls == 0, "never activate a UI tool, including legacy native flag");
            Check(BuildPlanningTools.Requested.SequenceEqual(new[] { "293,88", "292,88", "292,89" }), "only requested cells");
            Check(BuildPlanningTools.Placed.Count == 3 && BuildPlanningTools.PersistCalls == 1, "place and connect blueprints");
            Check(!(bool)body["networkConnected"], "blueprints do not claim a completed physical network");
            result = BuildPlanningTools.AutoConnectUtility().Handler(args);
            Check(!result.IsError && (int)Body(result)["planned"] == 0 && (int)Body(result)["reusedExisting"] == 3,
                "idempotent replay with no extra blueprints");
        }
        BuildPlanningTools.ResetPlacement();
        var preview = Args(); preview["dryRun"] = true; preview.Remove("confirm");
        var dryRun = BuildPlanningTools.AutoConnectUtility().Handler(preview);
        Check(!dryRun.IsError && (bool)Body(dryRun)["complete"], "preview validates path");
        Check(BuildPlanningTools.Placed.Count == 0 && BuildPlanningTools.PersistCalls == 0 && BuildPlanningTools.UiCalls == 0,
            "preview has no world, network or UI writes");

        BuildPlanningTools.ResetPlacement();
        var unconfirmed = Args(); unconfirmed.Remove("confirm");
        Check(BuildPlanningTools.AutoConnectUtility().Handler(unconfirmed).IsError && BuildPlanningTools.Requested.Count == 0,
            "unconfirmed commit rejected before placement");
        foreach (bool conflict in new[] { true, false })
        {
            BuildPlanningTools.ResetPlacement();
            BuildPlanningTools.Conflict = conflict;
            BuildPlanningTools.Available = 50; // Full three-cell path costs 75.
            var result = BuildPlanningTools.AutoConnectUtility().Handler(Args());
            Check(result.IsError && BuildPlanningTools.Placed.Count == 0 && BuildPlanningTools.UiCalls == 0,
                "whole-path conflict or material shortage rejects before first write");
        }
        BuildPlanningTools.ResetPlacement(); BuildPlanningTools.FailAt = 2;
        var partial = BuildPlanningTools.AutoConnectUtility().Handler(Args());
        Check(partial.IsError && !(bool)Body(partial)["complete"] && (int)Body(partial)["planned"] == 2,
            "partial placement is an error with honest mutation count");
        Check(BuildPlanningTools.PersistCalls == 0, "do not connect an incomplete route");
        BuildPlanningTools.ResetPlacement(); BuildPlanningTools.NetworkFails = true;
        var network = BuildPlanningTools.AutoConnectUtility().Handler(Args());
        Check(network.IsError && !(bool)Body(network)["connectionsPersisted"], "network persistence failure is surfaced");
        TestBacktracking();
        TestUtilityVariants();
        Console.WriteLine("Utility placement handler regression checks passed");
    }

    private static void TestBacktracking()
    {
        foreach (bool preview in new[] { true, false })
        {
            BuildPlanningTools.ResetPlacement();
            BuildPlanningTools.Available = 75;
            var args = JObject.Parse("{confirm:true,points:[[1,1],[2,1],[1,1],[1,2]],prefabId:'WireRefined',material:'Aluminum'}");
            args["dryRun"] = preview;
            var result = BuildPlanningTools.AutoConnectUtility().Handler(args);
            var body = Body(result);
            Check(!result.IsError && (bool)body["complete"], "backtracking must not falsely exhaust the exact three-cell budget");
            Check((int)body["pathCells"] == 3 && (int)body["pathSteps"] == 4,
                "unique cells and connection-walk steps have distinct counts");
            Check((float)body["materialSelection"]["RequiredKg"] == 75,
                "duplicate coordinate charged once for material");
            Check(BuildPlanningTools.Requested.SequenceEqual(new[] { "1,1", "2,1", "1,2" }),
                "placement validates or commits each cell only once");
            Check(BuildPlanningTools.SelectedMaterial == "Aluminum" && BuildPlanningTools.SelectedPrefab == "WireRefined",
                "variant and requested material reach native material selection unchanged");
            if (preview)
                Check(BuildPlanningTools.Placed.Count == 0 && BuildPlanningTools.PersistedPath.Count == 0,
                    "confirmed preview cannot place or persist a backtracking path");
            else
            {
                Check((int)body["planned"] == 3 && BuildPlanningTools.Placed.Count == 3,
                    "committed mutation count is unique-cell count");
                Check(BuildPlanningTools.PersistedPath.SequenceEqual(new[] { "1,1", "2,1", "1,1", "1,2" }),
                    "connection persistence retains the original walk so branches are connected");
                BuildPlanningTools.Available = 0;
                result = BuildPlanningTools.AutoConnectUtility().Handler(args);
                body = Body(result);
                Check(!result.IsError && (int)body["reusedExisting"] == 3 && (int)body["planned"] == 0
                    && (float)body["materialSelection"]["RequiredKg"] == 0,
                    "replaying a branch consumes zero new material and reports each existing cell once");
            }
        }
    }

    private static void TestUtilityVariants()
    {
        foreach (var family in new[] {
            new[] { "Wire", "Wire", "WireRefined", "WireHighWattage", "WireRefinedHighWattage" },
            new[] { "GasConduit", "GasConduit", "InsulatedGasConduit", "GasConduitRadiant" },
            new[] { "LiquidConduit", "LiquidConduit", "InsulatedLiquidConduit", "LiquidConduitRadiant" },
            new[] { "LogicWire", "LogicWire", "LogicRibbon" },
            new[] { "SolidConduit", "SolidConduit" } })
        {
            foreach (string prefab in family.Skip(1))
            {
                Check(UtilityPrefabPolicy.IsLinear(prefab), "linear variant recognized: " + prefab);
                string selected;
                Check(UtilityPrefabPolicy.TrySelect(family[0], prefab, out selected) && selected == prefab,
                    "layer must preserve exact requested variant: " + prefab);
                Check(!UtilityPrefabPolicy.TrySelect(family[0] == "Wire" ? "GasConduit" : "Wire", prefab, out selected),
                    "cross-layer variant rejected: " + prefab);
            }
            string fallback;
            Check(UtilityPrefabPolicy.TrySelect(family[0], null, out fallback) && fallback == family[0],
                "omitted variant uses layer default");
        }
        foreach (string unsupported in new[] { "WireBridge", "GasConduitBridge", "PowerTransformerSmall", "Unknown", "" })
            Check(!UtilityPrefabPolicy.IsLinear(unsupported), "nonlinear object rejected: " + unsupported);
        string ignored;
        Check(!UtilityPrefabPolicy.TrySelect("Wire", "InsulatedLiquidConduit", out ignored), "power path cannot silently place liquid pipe");
    }
}
