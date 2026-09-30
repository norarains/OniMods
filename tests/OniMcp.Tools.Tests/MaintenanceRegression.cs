using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Tools;
using UnityEngine;

internal static class MaintenanceRegression
{
    private static int checks;
    internal static void Run()
    {
        ParseOperations();
        NativePreviews();
        DefineAreas();
        PowerPending();
        PortSafety();
        TileLocationSafety();
        IndexedQueries();
        LadderDependencies();
        Check(!TemperatureSampleReadiness.Ready(new float[8], 0), "uninitialized sensor is unknown");
        Check(!TemperatureSampleReadiness.Ready(new[] { 290f, 291, 0, 0, 0, 0, 0, 0 }, 0), "partly filled sensor is unknown");
        Check(!TemperatureSampleReadiness.Ready(Enumerable.Repeat(290f, 8).ToArray(), 0), "unpublished average stays unknown");
        Check(TemperatureSampleReadiness.Ready(Enumerable.Repeat(290f, 8).ToArray(), 290), "native averaged sample is ready");
        Console.WriteLine("Maintenance contracts: " + checks + " checks passed");
    }

    private static void ParseOperations()
    {
        var args = OperationArguments.Parse("call points=[{\"x\":277, \"y\":91}, [278, 91]] priority=9 threshold=7.5 label=\"quoted \\\"room\\\"\"");
        Check(args["points"] is JArray && (int)args["points"][0]["x"] == 277, "points remain a typed JSON array");
        Check(args["threshold"].Type == JTokenType.Float && (double)args["threshold"] == 7.5, "decimal numeric input preserved");
        Check((string)args["label"] == "quoted \"room\"", "quoted names preserve escapes");
        foreach (string invalid in new[] { "call points=[1,2", "call points=[1}", "call label=\"open", "call x=1 x=2", "call points={broken}" })
            Reject(() => OperationArguments.Parse(invalid), "malformed/duplicate arguments reject");
        var area = AreaOperation.Parse("area label=utility x1=10 y1=20 x2=15 y2=25 worldId=1");
        Check((string)area["domain"] == "area" && (int)area["x2"] == 15, "area compiles only to define");
        foreach (string invalid in new[] { "area x1=1", "area x1=1 y1=2 x2=3.5 y2=4", "area x1=1 y1=2 x2=3 y2=4 tool=game_control" })
            Reject(() => AreaOperation.Parse(invalid), "area rejects missing/ambiguous coordinates or routing override");
    }

    private static void NativePreviews()
    {
        int calls = 0, mutations = 0;
        foreach (string tool in new[] { "building_control", "read_control", "orders_control" })
            OniToolRegistry.Internal[tool] = new McpTool { Name = tool, Handler = args => {
                calls++;
                if (args["domain"]?.ToString() == "planning") Check(BuildingControlTools.FileContext, "semantic build preview enters file context");
                if ((bool?)args["dryRun"] != true || (bool?)args["confirm"] != false) mutations++;
                return CallToolResult.Error("native conflict");
            }};
        foreach (var pair in new[] {
            Tuple.Create("building_control", "{domain:'planning',action:'auto_connect',confirm:true}"),
            Tuple.Create("read_control", "{domain:'area',action:'define',confirm:true}"),
            Tuple.Create("orders_control", "{domain:'area',action:'dig',confirm:true}")
        })
        {
            var result = WorldEditorTools.PreviewFixture(pair.Item1, JObject.Parse(pair.Item2));
            Check((string)result["validationLevel"] == "native_preflight" && !(bool)result["ok"], "previews expose native rejection");
        }
        Check(calls == 3 && mutations == 0, "native previews force dryRun even with confirmation");
        OniToolRegistry.Internal.Clear();
    }

    private static void DefineAreas()
    {
        var receipt = WorldEditorTools.AreaReceiptFixture(JObject.Parse("{areaId:'a42',rect:{x1:10},committed:true,worldId:0}"));
        Check((string)receipt["areaId"] == "a42" && (int)receipt["executed"] == 1, "area receipt keeps the reusable ID and counts the commit");
        AreaHandleRegistry.Defines = 0;
        var args = JObject.Parse("{x1:10,y1:2,x2:12,y2:3,worldId:0,dryRun:true,confirm:true}");
        Check(!AreaDefinition.Define(args).IsError && AreaHandleRegistry.Defines == 0, "area preview does not allocate a handle");
        args["dryRun"] = false;
        Check(!AreaDefinition.Define(args).IsError && AreaHandleRegistry.Defines == 1, "area commit allocates exactly once");
        args["x1"] = -1;
        Check(AreaDefinition.Define(args).IsError && AreaHandleRegistry.Defines == 1, "invalid bounds are rejected rather than clipped");
        args["x1"] = 10; args["worldId"] = 1;
        Check(AreaDefinition.Define(args).IsError && AreaHandleRegistry.Defines == 1, "wrong world does not allocate");
        args["worldId"] = 0; args.Remove("y2");
        Check(AreaDefinition.Define(args).IsError && AreaHandleRegistry.Defines == 1, "missing coordinates do not default to origin");
    }

    private static void PowerPending()
    {
        var connection = new TestCircuitConnection();
        Game.Instance = null;
        Check(PowerConnectionReadiness.Pending(connection, 1), "unloaded graph is pending");
        Game.Instance = new Game { circuitManager = new CircuitManager { Circuit = 1 }, electricalConduitSystem = new TestElectricalSystem() };
        Check(!PowerConnectionReadiness.Pending(connection, 1), "settled matching graph is known");
        Game.Instance.electricalConduitSystem.IsDirty = true;
        Check(PowerConnectionReadiness.Pending(connection, 1), "dirty network is pending");
        Game.Instance.electricalConduitSystem.IsDirty = false; Game.Instance.circuitManager.DirtyForFixture = true;
        Check(PowerConnectionReadiness.Pending(connection, 1), "dirty circuit is pending");
        Game.Instance.circuitManager.DirtyForFixture = false;
        Check(PowerConnectionReadiness.Pending(connection, 2), "component/graph mismatch is pending");
        Game.Instance.circuitManager.Circuit = ushort.MaxValue;
        Check(!PowerConnectionReadiness.Pending(connection, ushort.MaxValue), "settled disconnected component is known");
        Check(PowerConnectionReadiness.Pending(null, ushort.MaxValue), "missing native component is unknown");
        Game.Instance = null;
    }

    private static void PortSafety()
    {
        Array.Clear(Grid.Objects, 0, Grid.Objects.Length);
        var vent = new BuildingDef { PrefabID = "LiquidVent", InputConduitType = ConduitType.Liquid };
        int cell = Grid.XYToCell(30, 3);
        var bridge = new GameObject();
        Grid.Objects[cell, (int)ObjectLayer.LiquidConnection] = bridge;
        Check(BuildPlanningTools.PortConflictsFixture(vent, 30, 3).Count == 1, "vent rejects bridge output connector overlap");
        Check(Grid.Objects[cell, (int)ObjectLayer.LiquidConnection] == bridge, "port preview preserves native registration");
        Check(BuildPlanningTools.PortConflictsFixture(vent, 30, 3, ignored:bridge).Count == 0, "existing owner can be ignored during safe recheck");
        var gas = new BuildingDef { InputConduitType = ConduitType.Gas };
        Check(BuildPlanningTools.PortConflictsFixture(gas, 30, 3).Count == 0, "different conduit families do not conflict");
        var pipe = new BuildingDef { PrefabID = "LiquidConduit" };
        Check(BuildPlanningTools.PortConflictsFixture(pipe, 30, 3).Count == 0, "pipe may connect to a building port");
        Grid.Objects[cell, (int)ObjectLayer.LiquidConnection] = null;
        Grid.Objects[cell, (int)ObjectLayer.LiquidConduit] = bridge;
        Check(BuildPlanningTools.PortConflictsFixture(vent, 30, 3).Count == 0, "pipe layer is separate from connector ownership");
        var shifted = new BuildingDef { OutputConduitType = ConduitType.Liquid, UtilityOutputOffset = new CellOffset(1, 0) };
        Grid.Objects[Grid.XYToCell(30, 4), (int)ObjectLayer.LiquidConnection] = bridge;
        Check(BuildPlanningTools.PortConflictsFixture(shifted, 30, 3, Orientation.R90).Count == 1, "port collision follows rotated offsets");
        var power = new BuildingDef { RequiresPowerInput = true };
        Grid.Objects[cell, (int)ObjectLayer.WireConnectors] = bridge;
        Check(BuildPlanningTools.PortConflictsFixture(power, 30, 3).Count == 1, "power connector overlap rejects too");
        Grid.Hidden.Add(cell);
        Check(BuildPlanningTools.PortConflictsFixture(vent, 30, 3).Count == 1, "invisible port fails closed");
        Grid.Hidden.Clear(); Array.Clear(Grid.Objects, 0, Grid.Objects.Length);
        var joint = new BuildingDef { BuildLocationRule = BuildLocationRule.HighWattBridgeTile, BuildingComplete = new GameObject() };
        joint.BuildingComplete.Components[typeof(UtilityNetworkLink)] = new UtilityNetworkLink();
        Grid.Objects[Grid.XYToCell(31, 3), (int)ObjectLayer.WireConnectors] = bridge;
        Check(BuildPlanningTools.PortConflictsFixture(joint, 30, 3).Count == 1, "joint plate checks its link endpoints without RequiresPower flags");
        joint.BuildingComplete.Components.Clear();
        Check(BuildPlanningTools.PortConflictsFixture(joint, 30, 3).Count == 1, "missing joint link metadata fails closed");
        Array.Clear(Grid.Objects, 0, Grid.Objects.Length);
    }

    private static void TileLocationSafety()
    {
        Array.Clear(Grid.Objects, 0, Grid.Objects.Length);
        Grid.Hidden.Clear();
        int cell = Grid.XYToCell(292, 6), second = cell + 1;
        var tile = new BuildingDef { PrefabID = "Tile", BuildLocationRule = BuildLocationRule.Tile,
            ObjectLayer = ObjectLayer.Building, TileLayer = ObjectLayer.FoundationTile,
            WidthInCells = 1, HeightInCells = 1 };
        var heavy = new BuildingDef { PrefabID = "HighWattageWire", BuildLocationRule = BuildLocationRule.NotInTiles };
        var wire = TileObstruction(heavy, cell, 32538);
        Grid.Objects[cell, (int)ObjectLayer.Wire] = wire;
        var conflicts = BuildPlanningTools.FootprintObstructionsFixture(tile, new[] { cell });
        Check(conflicts.Count == 1 && (string)conflicts[0]["reasonCode"] == "wire_obstruction",
            "actual footprint preflight rejects tile over Heavi-Watt wire before native TryPlace");
        Check((int)conflicts[0]["existingId"] == 32538 && (string)conflicts[0]["actualPrefabId"] == "HighWattageWire"
            && (int)conflicts[0]["x"] == 292 && (int)conflicts[0]["y"] == 6
            && (string)conflicts[0]["layer"] == "Wire", "tile conflict identifies exact native wire and cell");
        Check(Grid.Objects[cell, (int)ObjectLayer.Wire] == wire && tile.NativePlaceCalls == 0
            && tile.NativeReplaceCalls == 0 && wire.NativeTriggers == 0, "preflight does not alter occupancy or dispatch construction");
        Check(BuildPlanningTools.FootprintObstructionsFixture(tile, new[] { cell }, wire).Count == 0,
            "an explicitly ignored source does not obstruct itself");
        heavy.PrefabID = "ModdedHighWattageWire";
        Check(BuildPlanningTools.FootprintObstructionsFixture(tile, new[] { cell }).Count == 1,
            "native NotInTiles rule covers modded wire IDs");
        heavy.BuildLocationRule = BuildLocationRule.Anywhere;
        Check(BuildPlanningTools.FootprintObstructionsFixture(tile, new[] { cell }).Count == 0,
            "ordinary wire is allowed through native tile footprint");
        heavy.BuildLocationRule = BuildLocationRule.NotInTiles;
        foreach (string prefab in new[] { "MeshTile", "GasPermeableMembrane", "InsulationTile" })
        {
            tile.PrefabID = prefab;
            Check(BuildPlanningTools.FootprintObstructionsFixture(tile, new[] { cell }).Count == 1,
                "native Tile rule includes " + prefab);
        }
        tile.PrefabID = "Tile";
        var passable = new BuildingDef { PrefabID = "Ladder", BuildLocationRule = BuildLocationRule.Anywhere,
            ObjectLayer = ObjectLayer.Building };
        Check(BuildPlanningTools.FootprintObstructionsFixture(passable, new[] { cell }).Count == 0,
            "Heavi-Watt wire can overlap a passable building");
        Grid.Objects[cell, (int)ObjectLayer.Wire] = null;
        Grid.Objects[second, (int)ObjectLayer.Wire] = wire;
        Check(BuildPlanningTools.FootprintObstructionsFixture(tile, new[] { cell, second }).Count == 1,
            "preflight checks every native footprint cell, not only the origin");
        Check(BuildPlanningTools.FootprintObstructionsFixture(tile, new[] { cell }).Count == 0,
            "adjacent Heavi-Watt wire does not obstruct a tile");
        Grid.Objects[second, (int)ObjectLayer.Wire] = null;

        var joint = new BuildingDef { PrefabID = "WireBridgeHighWattage",
            BuildLocationRule = BuildLocationRule.HighWattBridgeTile, ObjectLayer = ObjectLayer.Building,
            TileLayer = ObjectLayer.FoundationTile, WidthInCells = 1, HeightInCells = 1 };
        var jointObject = TileObstruction(joint, second, 451);
        Grid.Objects[cell, (int)ObjectLayer.WireConnectors] = jointObject;
        Check(BuildPlanningTools.FootprintObstructionsFixture(tile, new[] { cell }).Count == 1,
            "a joint plate endpoint remains uncovered by a neighboring tile");
        Check(BuildPlanningTools.FootprintObstructionsFixture(joint, new[] { cell }).Count == 1,
            "joint plate center keeps native tile obstruction checks");
        Grid.Objects[cell, (int)ObjectLayer.WireConnectors] = null;
        Grid.Objects[second, (int)ObjectLayer.WireConnectors] = jointObject;
        Check(BuildPlanningTools.FootprintObstructionsFixture(joint, new[] { cell }).Count == 0,
            "an unoccupied joint plate center is valid despite its own adjacent endpoint");
        Check(BuildPlanningTools.FootprintObstructionsFixture(joint, new[] { cell, second }).Count == 0,
            "HighWattBridgeTile native tile-location check applies only at its origin");
        Grid.Objects[second, (int)ObjectLayer.WireConnectors] = null;

        var backwall = TileObstruction(new BuildingDef { PrefabID = "NotInTilesBackwall",
            BuildLocationRule = BuildLocationRule.NotInTiles }, cell, 452);
        Grid.Objects[cell, (int)ObjectLayer.Backwall] = backwall;
        Check((string)BuildPlanningTools.FootprintObstructionsFixture(tile, new[] { cell })[0]["reasonCode"] == "backwall_obstruction",
            "native tile backwall restriction is reflected in the same preflight");
        var oldDef = new BuildingDef { PrefabID = "MeshTile", WidthInCells = 1, HeightInCells = 1,
            TileLayer = ObjectLayer.FoundationTile };
        var oldTile = TileObstruction(oldDef, cell, 453);
        oldTile.Components[typeof(BuildingComplete)] = new BuildingComplete { Def = oldDef };
        tile.ReplacementLayer = ObjectLayer.ReplacementTile;
        tile.ReplacementCandidate = oldTile;
        Grid.Objects[cell, (int)ObjectLayer.Building] = oldTile;
        Grid.Solid[cell] = true;
        Check(BuildPlanningTools.FootprintObstructionsFixture(tile, new[] { cell }).Count == 0,
            "native valid tile replacement retains its backwall exception and solid-cell filter");
        joint.ReplacementLayer = ObjectLayer.ReplacementTile; joint.ReplacementCandidate = oldTile;
        Check(BuildPlanningTools.FootprintObstructionsFixture(joint, new[] { cell }).Count == 0,
            "native joint plate can replace a foundation tile at its center");
        Grid.Objects[cell, (int)ObjectLayer.Wire] = wire;
        Check((string)BuildPlanningTools.FootprintObstructionsFixture(tile, new[] { cell })[0]["reasonCode"] == "wire_obstruction",
            "tile replacement does not suppress unrelated Heavi-Watt obstruction");
        Grid.Objects[cell, (int)ObjectLayer.Wire] = null;
        tile.NativeCanReplace = false;
        Check(BuildPlanningTools.FootprintObstructionsFixture(tile, new[] { cell }).Any(c => (string)c["reasonCode"] == "backwall_obstruction"),
            "replacement exemption requires native CanReplace approval");
        Grid.Hidden.Add(cell);
        Check(BuildPlanningTools.FootprintObstructionsFixture(tile, new[] { cell }).Count == 0,
            "unexplored cell does not expose obstructing building identity");
        Grid.Hidden.Clear(); Grid.Solid[cell] = false;
        Array.Clear(Grid.Objects, 0, Grid.Objects.Length);
    }

    private static GameObject TileObstruction(BuildingDef def, int cell, int id)
    {
        var go = new GameObject { Cell = cell };
        go.Components[typeof(Building)] = new Building { Def = def };
        go.Components[typeof(KPrefabID)] = new KPrefabID { InstanceID = id };
        return go;
    }

    private static void IndexedQueries()
    {
        var rows = Enumerable.Range(1, 1300).Select(id => new FactRow(field => field == "id" ? id : id % 2)).ToArray();
        var data = new FactDataset("buildings", new[] { new FactField("id", "number"), new FactField("worldId", "number") }, () => rows);
        foreach (string where in new[] { "id=41", "worldId=0 AND id=41", "id=41 OR id=42", "id=41 OR worldId=0", "id=41 AND id=42", "id=1.5", "id=2147483648" })
        {
            var plan = FactQueryParser.Parse("SELECT id FROM buildings WHERE " + where + " ORDER BY id LIMIT 200");
            data.IndexedRows = null;
            var baseline = FactQueryExecutor.Execute(plan, data);
            data.IndexedRows = predicate => {
                var ids = FactQueryIdentity.Candidates(predicate);
                return ids == null ? null : ids.Where(id => id > 0 && id <= rows.Length).Select(id => rows[id - 1]);
            };
            var indexed = FactQueryExecutor.Execute(plan, data);
            Check(JToken.DeepEquals(baseline["rows"], indexed["rows"]), "indexed/full predicate parity: " + where);
            if (where == "id=41") Check((int)indexed["scanned"] == 1, "exact identity scans only one candidate");
        }
    }

    private static void LadderDependencies()
    {
        Func<int, int, ISet<int>, bool> adjacent = (cell, prior, ready) => Math.Abs(cell - prior) == 1;
        var chain = PlannedAccessChain.Resolve(new[] { 6, 5, 4, 3 }, new[] { 6 }, adjacent);
        Check(chain.Count == 3 && chain[5] == 6 && chain[3] == 4, "ladder dependencies point back to reachable seed");
        Check(PlannedAccessChain.Resolve(new[] { 3, 4, 5 }, new int[0], adjacent).Count == 0, "unseeded chains remain unreachable");
        Check(PlannedAccessChain.Resolve(new[] { 1, 2, 5, 6 }, new[] { 1 }, adjacent).Count == 1, "disconnected island remains unreachable");
        Check(PlannedAccessChain.Resolve(new[] { 1, 2, 3 }, new[] { 1 }, (cell, prior, ready) => false).Count == 0, "native clearance failure blocks dependent construction");
    }
    private static void Reject(Action action, string label) { bool failed = false; try { action(); } catch (ArgumentException) { failed = true; } Check(failed, label); }
    private static void Check(bool value, string label) { checks++; if (!value) throw new Exception(label); }
}
