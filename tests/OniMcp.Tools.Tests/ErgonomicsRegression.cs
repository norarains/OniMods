using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Tools;

internal static class ErgonomicsRegression
{
    private static int assertions;
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
    internal static void Run()
    {
        TestResponses();
        TestRepeatedDiagnostics();
        TestCapturedDiagnostics();
        TestGeometryCompaction();
        TestPriority();
        TestStorage();
        TestOccupancy();
        TestPreflight();
        TestDiscoveryAndBatch();
        Console.WriteLine("MCP ergonomics regression checks passed: " + assertions);
    }
    private static void TestResponses()
    {
        var failure = new JObject {
            ["ok"] = false, ["partial"] = true, ["applied"] = 2,
            ["obstructions"] = new JArray(new JObject { ["cell"] = 286, ["reason"] = "occupied" }),
            ["materialSelection"] = new JObject { ["available"] = 12 },
            ["planResolution"] = new string('x', 6000)
        };
        JToken nested = failure;
        for (int i = 0; i < 3; i++)
            nested = new JObject { ["result"] = nested.ToString(Formatting.None), ["error"] = nested.ToString(Formatting.None) };
        string raw = nested.ToString(Formatting.None);
        var result = WorldEditorResponsePolicy.Format(CallToolResult.Error(raw), JObject.Parse("{command:'EDIT'}"));
        var body = JObject.Parse(result.Content[0].Text);
        Check(result.IsError, "format must preserve MCP error bit");
        Check(body["result"] is JObject && body["error"] == null, "native JSON and duplicate removal");
        var leaf = body["result"]["result"]["result"];
        Check((int)leaf["applied"] == 2 && (bool)leaf["partial"] && !(bool)leaf["ok"], "partial mutation evidence survives");
        Check((int)leaf["obstructions"][0]["cell"] == 286 && (int)leaf["materialSelection"]["available"] == 12, "safety diagnostics survive");
        Check(result.Content[0].Text.Length < raw.Length / 10, "nested tutorial output should shrink substantially");
        var full = WorldEditorResponsePolicy.Format(CallToolResult.Error(raw), JObject.Parse("{command:'edit',responseMode:'full'}"));
        Check(JObject.Parse(full.Content[0].Text)["result"]["result"]["result"]["planResolution"] != null, "full retains diagnostic detail");
        Check(WorldEditorResponsePolicy.Format(CallToolResult.Error("plain error"), JObject.Parse("{command:'edit'}")).Content[0].Text == "plain error", "plain errors preserved");
        Check(WorldEditorResponsePolicy.Normalize(JObject.Parse("{result:'{bad json',error:'reason'}"), true)["result"].Type == JTokenType.String, "malformed JSON text preserved");
        Check(!WorldEditorResponsePolicy.IncludeHelp(new JObject()) && WorldEditorResponsePolicy.IncludeHelp(JObject.Parse("{includeHelp:true}")), "help is opt-in");
        var material = JObject.Parse("{valid:true,satisfied:true,elements:['CopperOre'],requiredKg:100,selectedAvailableKg:200,shortageKg:0,availableMaterials:[{tag:'IronOre'}],candidateMaterials:[],next:'build more',suggestion:'use auto'}");
        var compact = WorldEditorResponsePolicy.Normalize(material, true);
        Check(compact["availableMaterials"] == null && compact["next"] == null && compact["suggestion"] == null
            && (int)compact["requiredKg"] == 100 && (int)compact["selectedAvailableKg"] == 200, "satisfied material reports keep budgets without repeated choices or advice");
        material["satisfied"] = false;
        Check(WorldEditorResponsePolicy.Normalize(material, true)["availableMaterials"] != null, "unsatisfied material evidence stays available");
        Check(JToken.DeepEquals(WorldEditorResponsePolicy.Normalize(material, false), material), "full mode retains material diagnostics");
        var digging = JObject.Parse("{autoDig:{failed:0,skipped:0,marked:1,uprootMarked:0,kgTotal:100,targets:[{x:2,y:3,cell:3074,worldId:1,status:'marked'}],note:'repeated explanation'}}");
        var dig = WorldEditorResponsePolicy.Normalize(digging, true)["autoDig"];
        Check(dig["note"] == null && dig["uprootMarked"] == null && (int)dig["worldId"] == 1
            && (int)dig["targets"][0]["x"] == 2 && (string)dig["targets"][0]["status"] == "marked"
            && (int)dig["marked"] == 1 && (int)dig["kgTotal"] == 100, "compact digging retains locations, actions and mass");
        digging["autoDig"]["failed"] = 1;
        Check(JToken.DeepEquals(WorldEditorResponsePolicy.Normalize(digging, true), digging), "failed digging keeps complete evidence");
        Console.WriteLine("Nested error fixture: " + raw.Length + " -> " + result.Content[0].Text.Length + " characters");
    }
    private static void TestRepeatedDiagnostics()
    {
        var leaf = JObject.Parse("{valid:false,reasonCode:'occupied',error:'blocked',obstructions:[{cell:286}],details:{valid:false,error:'blocked',obstructions:[{cell:286}],uniqueRisk:'keep'},diagnostics:{reasonCode:'occupied',message:'blocked',obstructions:[{cell:286}]}}");
        var input = new JObject { ["errors"] = new JArray(leaf), ["previews"] = new JArray(leaf.DeepClone(), JObject.Parse("{valid:true,x:287}")) };
        var result = WorldEditorResponsePolicy.Normalize(input, true);
        Check((string)result["errors"][0]["details"]["uniqueRisk"] == "keep", "unique nested risk survives");
        Check(result["errors"][0]["diagnostics"] == null && result["errors"][0]["details"]["obstructions"] == null, "only redundant diagnostic fields removed");
        Check((int)result["errors"][0]["obstructions"][0]["cell"] == 286, "canonical obstruction retained");
        Check((int)result["duplicatePreviewsOmitted"] == 1 && result["previews"].Count() == 1 && (int)result["previews"][0]["x"] == 287, "successful preview retained alongside unique errors");
        Check(JToken.DeepEquals(WorldEditorResponsePolicy.Normalize(input, false), input), "full output retains every diagnostic copy");
    }
    private static void TestCapturedDiagnostics()
    {
        string directory = Environment.GetEnvironmentVariable("ONI_ERGONOMICS_FIXTURES");
        if (string.IsNullOrEmpty(directory)) return;
        foreach (string name in new[] { "occupied_ladder", "unsupported_bed", "unavailable_material", "zero_stock_material" })
        {
            var input = JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(directory, name + ".json")))["payload"];
            var output = WorldEditorResponsePolicy.Normalize(input, true);
            foreach (string key in new[] { "reasonCode", "obstructions", "missingSupportCells", "committed", "applied", "requiredKg", "availableKg" })
            {
                Func<JToken, string[]> values = token => ((JContainer)token).Descendants().OfType<JProperty>()
                    .Where(property => property.Name == key).Select(property => property.Value.ToString(Formatting.None))
                    .Distinct().OrderBy(value => value).ToArray();
                Check(values(input).SequenceEqual(values(output)), name + " retains unique " + key);
            }
            Console.WriteLine(name + " captured response: " + input.ToString(Formatting.None).Length + " -> " + output.ToString(Formatting.None).Length + " characters");
        }
    }
    private static void TestGeometryCompaction()
    {
        var valid = JObject.Parse("{x:288,y:74,valid:true,visible:true,inWorld:true}");
        var source = new JObject { ["anchor"] = JObject.Parse("{x:288,y:74}"),
            ["footprint"] = new JArray(valid, valid.DeepClone()), ["risks"] = new JArray("liquid_adjacent") };
        var compact = WorldEditorResponsePolicy.Normalize(source, true);
        Check(compact["footprint"] == null && (int)compact["footprintCount"] == 2, "valid footprint rows compress to count and bounds");
        Check((string)compact["risks"][0] == "liquid_adjacent" && compact["anchor"] != null, "hazards and exact anchor remain in summary");
        source["footprint"][0]["visible"] = false;
        Check(WorldEditorResponsePolicy.Normalize(source, true)["footprint"] != null, "invalid footprint evidence is never summarized away");
        Check(WorldEditorResponsePolicy.Normalize(source, false)["footprint"] != null, "full mode retains geometry");
        var wire = JObject.Parse("{failed:0,segments:[{from:[1,1],to:[5,1]}],path:[1,2,3,4,5],autoDigQueued:2,connectResult:{failed:0,results:[{valid:true}]}}");
        var result = WorldEditorResponsePolicy.Normalize(wire, true);
        Check(result["connectResult"] == null && result["segments"] != null && (int)result["autoDigQueued"] == 2, "successful wire details compress while keeping path and side effects");
        var receipt = JObject.Parse("{valid:true,planned:true,prefabId:'Tile',id:123,anchor:{x:1,y:2,worldId:0},placement:{anchorDescription:'lower-left',footprintBounds:[1,2,1,2]},support:{valid:true,cells:[]},placementCheck:{valid:true,anchorMatches:true,worldMatches:true,footprintMatches:true},actualPlacement:{registeredFootprint:true,occupiedBounds:[1,2,1,2],occupiedCells:[123]},warnings:['liquid_adjacent'],materialSelection:{valid:true,requiredKg:100,selectedAvailableKg:1000}}");
        var compactReceipt = WorldEditorResponsePolicy.Normalize(receipt, true);
        Check((bool)compactReceipt["placementVerified"] && (int)compactReceipt["id"] == 123 && compactReceipt["occupiedBounds"] != null, "receipt retains object and verified occupancy");
        Check(compactReceipt["placement"] == null && compactReceipt["support"] == null && compactReceipt["placementCheck"] == null, "successful placement uses a short receipt");
        Check(compactReceipt["warnings"] != null && (int)compactReceipt["materialSelection"]["requiredKg"] == 100, "receipt preserves hazards and material budget");
        receipt["support"]["warnings"] = new JArray("requires_planned_floor");
        Check(WorldEditorResponsePolicy.Normalize(receipt, true)["support"] != null, "nested warning evidence is retained");
        receipt["placementCheck"]["valid"] = false;
        receipt["placementCheck"]["pending"] = true;
        Check((bool)WorldEditorResponsePolicy.Normalize(receipt, true)["registrationPending"], "pending registration never claims verified placement");
        Check(ToolBatchTools.CallMany().Parameters["calls"].Items.Type == "object", "batch schema requires object entries, not strings");
        wire["failed"] = 1;
        Check(WorldEditorResponsePolicy.Normalize(wire, true)["connectResult"] != null, "failed wiring retains child diagnostics");
    }

    private static void TestPriority()
    {
        Check(WorldEditorTools.ReadPriority("挖 @(297,73):6") == 6, "priority after coordinates");
        Check(WorldEditorTools.ReadPriority("Ladder:7#火@(1,2)") == 7, "priority before coordinates/material");
        Check(WorldEditorTools.ReadPriority("扫 @(12,34):9 dryRun=true") == 9, "priority followed by arguments");
        Check(WorldEditorTools.ReadPriority("挖 @(297,73)") == null, "no accidental coordinate priority");
        Check(WorldEditorTools.ReadPriority("挖 @(297,73):10") == null, "invalid priority is not prefix-matched");
    }
    private static void TestStorage()
    {
        var rows = new[] {
            new StorageRow(1, 0, "箱 A", "StorageLocker", "Dirt"),
            new StorageRow(2, 0, "水库", "LiquidReservoir", "Water"),
            new StorageRow(3, 1, "箱 B", "StorageLocker", "Dirt") };
        Func<string, int[]> select = json => StorageListFilter.Apply(rows, JObject.Parse(json),
            item => item.Id, item => item.World, (item, q) => item.Identity(q),
            (item, q) => item.Resource.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0, 1).Select(item => item.Id).ToArray();
        Check(select("{id:2}").SequenceEqual(new[] { 2 }), "id filter before limit");
        Check(select("{query:'liquidreservoir'}").SequenceEqual(new[] { 2 }), "prefab query");
        Check(select("{query:'水库'}").SequenceEqual(new[] { 2 }), "localized name query");
        Check(select("{query:'StorageLocker',worldId:1,resource:'dirt'}").SequenceEqual(new[] { 3 }), "combined filters");
        Check(select("{id:1,query:'水库'}").Length == 0 && select("{id:99}").Length == 0, "no widening of missing/conflicting targets");
    }
    private sealed class StorageRow
    {
        internal readonly int Id, World;
        internal readonly string Name, Prefab, Resource;
        internal StorageRow(int id, int world, string name, string prefab, string resource)
        { Id = id; World = world; Name = name; Prefab = prefab; Resource = resource; }
        internal bool Identity(string q) => Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || Prefab.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
    }
    private static void TestOccupancy()
    {
        var pod = new UnityEngine.GameObject();
        var def = new BuildingDef { ObjectLayer = 1, WidthInCells = 4, HeightInCells = 4 };
        for (int cell = 282; cell <= 285; cell++) Grid.Objects[cell, 1] = pod;
        Check(RegisteredBuildingOccupancy.Contains(pod, 282, def), "native left edge occupied");
        Check(RegisteredBuildingOccupancy.Contains(pod, 285, def), "native right edge occupied");
        Check(!RegisteredBuildingOccupancy.Contains(pod, 286, def), "no phantom right edge from pivot arithmetic");
        Check(RegisteredBuildingOccupancy.TryGetBounds(pod, 283, def, out int[] bounds)
            && bounds.SequenceEqual(new[] { 282, 0, 285, 0 }), "bounds follow native registrations, not pivot plus width");
        Grid.Objects[500, 1] = pod;
        Check(RegisteredBuildingOccupancy.Contains(pod, 500, def), "registered rotated footprint honored");
        Check(!RegisteredBuildingOccupancy.Contains(pod, 285, new BuildingDef { ObjectLayer = 0 }), "layers not conflated");
        Check(!RegisteredBuildingOccupancy.Contains(new UnityEngine.GameObject(), 285, def), "instance identity required");
        var replacement = new UnityEngine.GameObject();
        Grid.Objects[510, 0] = replacement;
        var replacementDef = new BuildingDef { ObjectLayer = 1, ReplacementLayer = global::ObjectLayer.LiquidConduit };
        Check(RegisteredBuildingOccupancy.Contains(replacement, 510, replacementDef), "replacement-layer blueprint registration is observed");
        Check(!RegisteredBuildingOccupancy.Contains(pod, 510, replacementDef), "replacement layer still requires exact object identity");
        var rotated = new UnityEngine.GameObject();
        for (int y = 2; y <= 4; y++) Grid.Objects[Grid.XYToCell(20, y), 1] = rotated;
        Check(RegisteredBuildingOccupancy.TryGetBounds(rotated, Grid.XYToCell(20, 3), new BuildingDef { ObjectLayer = 1, WidthInCells = 3, HeightInCells = 1 }, out int[] rotatedBounds)
            && rotatedBounds.SequenceEqual(new[] { 20, 2, 20, 4 }), "rotated native bounds override unrotated definition dimensions");
    }
    private static void TestPreflight()
    {
        var args = JObject.Parse("{confirm:true,dryRun:false,allowPartial:true,task:'preflight test'}");
        int calls = 0;
        WorldEditorTools.ValidateChild = (child, kind) => {
            calls++;
            Check((bool)child["dryRun"] && !(bool)child["confirm"] && !(bool)child["allowPartial"], "preflight forces no mutation and no partial validation");
            return CallToolResult.Text("{ok:true,applied:0}");
        };
        var valid = WorldEditorTools.ValidateFixture(args, "build", "dig");
        Check(!valid.IsError && calls == 2, "build and order game validators both run");
        Check((bool)args["confirm"] && !(bool)args["dryRun"], "caller args unchanged");
        calls = 0;
        WorldEditorTools.ValidateChild = (child, kind) => { calls++; return CallToolResult.Text("{valid:false,hardFailed:1,reason:'occupied'}"); };
        var invalid = WorldEditorTools.ValidateFixture(args, "build", "dig");
        Check(invalid.IsError && calls == 1, "logical validator failure stops preflight");
        var body = JObject.Parse(invalid.Content[0].Text);
        Check((int)body["applied"] == 0 && (string)body["checks"][0]["result"]["reason"] == "occupied", "failed preview returns native reason and applied zero");
    }
    private static void TestDiscoveryAndBatch()
    {
        int calls = 0;
        OniToolRegistry.Internal["dupes_control"] = new McpTool {
            Name = "dupes_control", Mode = "read", Risk = "none",
            Handler = args => { Check((string)args["task"] == "inspect", "batch inherits outer task"); calls++; return CallToolResult.Text("{ok:true}"); }
        };
        OniToolRegistry.Internal["coordinate_control"] = new McpTool { Name = "coordinate_control", Handler = args => throw new Exception("must never execute") };
        Check(!OniToolRegistry.TryGetTool("dupes_control", out _), "internal operation is not public");
        var result = ToolBatchTools.CallMany().Handler(JObject.Parse("{task:'inspect',calls:[{tool:'dupes_control',args:{domain:'info',action:'status_check'}}]}"));
        Check(!result.IsError && calls == 1, "internal aggregate reachable through batch");
        var blocked = ToolBatchTools.CallMany().Handler(JObject.Parse("{calls:[{tool:'dupes_control'},{tool:'coordinate_control'}]}"));
        Check(blocked.IsError && calls == 1, "invalid coordinate gateway rejected before earlier reads execute");
        var caps = ToolCatalogCapabilities.Read();
        Check(caps["batchOperations"].Values<string>().Contains("dupes_control") && !caps["publicTools"].Values<string>().Contains("dupes_control"), "manifest separates routing surfaces");
        Check(!(bool)caps["editMarks"], "unsupported edit marks are not advertised");
        OniToolRegistry.Internal["building_control"] = new McpTool {
            Name = "building_control", Mode = "execute", Risk = "dangerous",
            Handler = args => { calls++; return CallToolResult.Text("{ok:true}"); }
        };
        result = ToolBatchTools.CallMany().Handler(JObject.Parse("{calls:[{tool:'building_control',args:{domain:'config',action:'list',id:2062,capability:'manual_delivery'}}]}"));
        Check(!result.IsError && calls == 2, "read-only building configuration needs no mutation confirmation");
        result = ToolBatchTools.CallMany().Handler(JObject.Parse("{calls:[{tool:'building_control',args:{domain:'config',action:'set',id:2062}}]}"));
        Check(result.IsError && calls == 2, "configuration writes still require confirmation");
        OniToolRegistry.Internal.Clear();
    }
}

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        internal static Func<JObject, string, CallToolResult> ValidateChild;
        internal static int? ReadPriority(string token) => ParsePriority(token);
        internal static CallToolResult ValidateFixture(JObject args, params string[] kinds) =>
            ValidateMapChangesInGame(args, kinds.Select(kind => new MapEditCell { ToToken = kind }).ToList());
        private static string ChangeKind(MapEditCell cell) => cell.ToToken;
        private static CallToolResult ApplyBuildMapEdit(JObject args, IEnumerable<MapEditCell> cells) => ValidateChild(args, "build");
        private static CallToolResult ApplyOrderMapEdit(JObject args, string kind, IEnumerable<MapEditCell> cells) => ValidateChild(args, kind);
        private static CallToolResult JsonResult(JToken token) => CallToolResult.Text(JsonResultText(token));
        private static JObject InheritWorldEditorSandboxPolicy(JObject parent, JObject child) => child;
    }
}
namespace UnityEngine
{
    public sealed class GameObject
    {
        internal readonly Dictionary<Type, object> Components = new Dictionary<Type, object>();
        public T GetComponent<T>() where T : class => Components.Values.OfType<T>().FirstOrDefault();
    }
}
internal sealed class BuildingDef { internal ObjectLayer ReplacementLayer { get; set; } = global::ObjectLayer.NumLayers; internal int ObjectLayer { get; set; } internal int WidthInCells { get; set; } internal int HeightInCells { get; set; } internal UnityEngine.GameObject BuildingComplete { get; set; } }
internal static class Grid
{
    internal const int WidthInCells = 1024, HeightInCells = 8;
    internal static int CellColumn(int cell) => cell % WidthInCells;
    internal static int CellRow(int cell) => cell / WidthInCells;
    internal static int XYToCell(int x, int y) => y * WidthInCells + x;
    internal static readonly UnityEngine.GameObject[,] Objects = new UnityEngine.GameObject[WidthInCells * HeightInCells, 2];
    internal static readonly int[] WorldIdx = new int[WidthInCells * HeightInCells];
    internal static bool IsValidCell(int cell) => cell >= 0 && cell < Objects.GetLength(0);
}
