using System;
using OniMcp.Tools;
using UnityEngine;

internal static class UtilityReplacementConnectionsRegression
{
    private static void Check(bool ok, string message)
    { if (!ok) throw new InvalidOperationException("Replacement connections: " + message); }

    internal static void Run()
    {
        BuildPlanningTools.ResetPlacement();
        int cell = Grid.XYToCell(20, 2);
        var oldDef = new BuildingDef { PrefabID = "Wire", WidthInCells = 1, HeightInCells = 1 };
        var heavy = new BuildingDef { PrefabID = "HighWattageWire", ObjectLayer = (int)ObjectLayer.Wire,
            TileLayer = ObjectLayer.WireTile, ReplacementLayer = ObjectLayer.ReplacementWire };
        var source = new GameObject { Cell = cell };
        source.Components[typeof(Building)] = new Building { Def = oldDef };
        source.Components[typeof(BuildingComplete)] = new BuildingComplete { Def = oldDef };
        var oldMask = UtilityConnections.Left | UtilityConnections.Right | UtilityConnections.Up;
        var oldVisualizer = new KAnimGraphTileVisualizer { Connections = oldMask };
        source.Components[typeof(KAnimGraphTileVisualizer)] = oldVisualizer;
        Grid.Objects[cell, (int)ObjectLayer.Wire] = source;
        try
        {
            var replacement = BuildPlanningTools.CreateUtilityBlueprintFixture(heavy, 20, 2, true);
            var visualizer = replacement.GetComponent<KAnimGraphTileVisualizer>();
            heavy.NativePlacementResult = replacement;
            Check(BuildPlanningTools.NativeBlueprintFixture(heavy, source, out string error) == replacement
                && error == null && visualizer.Connections == oldMask
                && heavy.NativeReplaceCalls == 1 && heavy.NativePlaceCalls == 0,
                "single-cell native upgrade keeps the old T-junction before completion");
            Check(Grid.Objects[cell, (int)ObjectLayer.Wire] == source && oldVisualizer.Connections == oldMask,
                "preserving the blueprint cannot modify or remove the old physical wire");

            visualizer.Connections = 0; // Preexisting replacement from the old implementation.
            var left = BuildPlanningTools.CreateUtilityBlueprintFixture(heavy, 19, 2);
            var right = BuildPlanningTools.CreateUtilityBlueprintFixture(heavy, 21, 2);
            Check(BuildPlanningTools.PersistConnectionsFixture(heavy, new[] { cell - 1, cell, cell + 1 }, out error),
                "production persistence accepts the horizontal upgrade path");
            Check(visualizer.Connections == oldMask
                && left.GetComponent<KAnimGraphTileVisualizer>().Connections == UtilityConnections.Right
                && right.GetComponent<KAnimGraphTileVisualizer>().Connections == UtilityConnections.Left,
                "horizontal upgrade retains the off-path upward branch without joining neighboring stubs");
            Check(BuildPlanningTools.PersistConnectionsFixture(heavy, new[] { cell - 1, cell, cell + 1 }, out error)
                && visualizer.Connections == oldMask, "replacement path replay is idempotent");

            visualizer.Connections = UtilityConnections.Down;
            Check(BuildPlanningTools.PreserveReplacementFixture(heavy, replacement, source, out error)
                && visualizer.Connections == (oldMask | UtilityConnections.Down), "new requested bits and original branch bits both survive");

            oldDef.PrefabID = "LiquidConduit"; visualizer.Connections = 0;
            Check(BuildPlanningTools.PreserveReplacementFixture(heavy, replacement, source, out error)
                && visualizer.Connections == 0, "different utility family cannot contribute connection bits");
            oldDef.PrefabID = "Wire";
            replacement.GetComponent<Constructable>().IsReplacementTile = false;
            Check(BuildPlanningTools.PreserveReplacementFixture(heavy, replacement, source, out error)
                && visualizer.Connections == 0, "fresh nonreplacement cells cannot inherit same-family spatial neighbors");
            replacement.GetComponent<Constructable>().IsReplacementTile = true;
            heavy.NativeCanReplace = false;
            Check(BuildPlanningTools.PreserveReplacementFixture(heavy, replacement, source, out error)
                && visualizer.Connections == 0, "native replacement eligibility remains required");
            heavy.NativeCanReplace = true; source.Cell++;
            Check(BuildPlanningTools.PreserveReplacementFixture(heavy, replacement, source, out error)
                && visualizer.Connections == 0, "adjacent cells cannot contribute inherited bits");
            source.Cell--;
            Grid.Objects[cell, (int)ObjectLayer.ReplacementWire] = null;
            Check(BuildPlanningTools.PreserveReplacementFixture(heavy, replacement, source, out error)
                && visualizer.Connections == 0, "stale unregistered replacement blueprints cannot inherit bits");
            Grid.Objects[cell, (int)ObjectLayer.ReplacementWire] = replacement;
            visualizer.RejectUpdates = true;
            Check(!BuildPlanningTools.PreserveReplacementFixture(heavy, replacement, source, out error)
                && error != null, "failed serialized-mask update is reported");
        }
        finally
        {
            Grid.Objects[cell, (int)ObjectLayer.Wire] = null;
            BuildPlanningTools.ResetPlacement();
        }
        Console.WriteLine("Production utility replacement mask regressions passed");
    }
}
