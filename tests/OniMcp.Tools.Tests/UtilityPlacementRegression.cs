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
        Console.WriteLine("Utility placement handler regression checks passed");
    }
}
