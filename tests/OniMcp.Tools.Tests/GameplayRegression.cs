using System;
using System.Collections.Generic;
using System.Linq;
using Klei.AI;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Tools;

internal static class GameplayRegression
{
    private static int assertions;
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    internal static void Run()
    {
        TestBuildingNames();
        TestGlyphCase();
        TestAppliedCounts();
        TestNeeds();
        TestIdlePolicy();
        Console.WriteLine("MCP gameplay regression checks passed: " + assertions);
    }

    private static void TestBuildingNames()
    {
        Assets.BuildingDefs.Add(new TestBuildingDef { PrefabID = "LadderBed", Name = "床梯" });
        Assets.BuildingDefs.Add(new TestBuildingDef { PrefabID = "LadderFast", Name = "塑料梯" });
        Assets.BuildingDefs.Add(new TestBuildingDef { PrefabID = "Ladder", Name = "梯子" });
        Check(WorldEditorTools.ResolveBuildFixture("Ladder:7#火") == "Ladder", "exact ID must beat earlier substring matches");
        Check(WorldEditorTools.ResolveBuildFixture("ladder:7") == "Ladder", "prefab IDs remain case insensitive");
        Check(WorldEditorTools.ResolveBuildFixture("梯子:7") == "Ladder", "localized exact name resolves");
        Check(WorldEditorTools.ResolveBuildFixture("Ladd:7") == null, "ambiguous shorthand must fail closed");
        Check(WorldEditorTools.ResolveBuildFixture("LadderTypo:7") == null, "unknown full name must not fall back to its first glyph");
        Check(WorldEditorTools.ResolveBuildFixture("梯:7") == "Ladder", "single glyph shorthand remains available");
        Check(WorldEditorTools.ResolveBuildFixture("derFast:7") == "LadderFast", "unique legacy substring remains available");
        Assets.BuildingDefs.Add(new TestBuildingDef { PrefabID = "Tile", Name = "Tile" });
        Assets.BuildingDefs.Add(new TestBuildingDef { PrefabID = "Tile", Name = "Tile" });
        Check(WorldEditorTools.ResolveBuildFixture("砖块:6#火") == "Tile", "generated display names resolve even with English runtime names and duplicate registration");
        Assets.BuildingDefs.Add(new TestBuildingDef { PrefabID = "TileComplete", Name = "砖块" });
        Assets.BuildingDefs.Add(new TestBuildingDef { PrefabID = "Tile", Name = "砖块" });
        Check(WorldEditorTools.ResolveBuildFixture("砖块:5#火") == "Tile", "native and Complete aliases resolve to one prefab");
        Check(PrefabIdentity.BaseId("RationBoxComplete(Clone)") == "RationBox", "runtime object names normalize before glyph lookup");
        Check(PrefabIdentity.BaseId("TileUnderConstruction(Clone)") == "Tile", "blueprint instance names normalize");
        Assets.BuildingDefs.Clear();
    }

    private static void TestGlyphCase()
    {
        var rows = new List<JObject> {
            JObject.Parse("{symbol:'x',id:'Oxygen'}"), JObject.Parse("{symbol:'X',id:'LogicGateXOR'}"),
            JObject.Parse("{symbol:'R',id:'OxyRock'}"), JObject.Parse("{symbol:'r',id:'IridiumGas'}") };
        foreach (string mode in new[] { "auto", "exact", "contains" })
        {
            var result = WorldEditorTools.GlyphFixture(rows, "x", "code_to_meaning", mode);
            Check((int)result["count"] == 1 && (string)result["matches"][0]["id"] == "Oxygen", "glyph case: " + mode);
        }
        Check((string)WorldEditorTools.GlyphFixture(rows, "R", "auto", "auto")["matches"][0]["id"] == "OxyRock", "automatic glyph direction preserves uppercase");
        Check((string)WorldEditorTools.GlyphFixture(rows, "oxygen", "meaning_to_code", "auto")["matches"][0]["symbol"] == "x", "name lookup remains case insensitive");
    }

    private static void TestAppliedCounts()
    {
        Check(WorldEditorTools.AppliedFixture("{dryRun:false,changed:1,matched:1}") == 1, "harvest changed count is actual applied work");
        Check(WorldEditorTools.AppliedFixture("{dryRun:true,changed:1,matched:1}") == 0, "harvest dry-run is never counted as applied");
        Check(WorldEditorTools.AppliedFixture("{preview:true,planned:2}") == 0, "preview counts are not mutations");
        Check(WorldEditorTools.AppliedFixture("{dryRun:false,changed:0,matched:1}") == 0, "matched-only target is not a mutation");
        Check(WorldEditorTools.AppliedFixture("{applied:1,changed:1}") == 1, "aliases must not be summed");
        Check(WorldEditorTools.SatisfiedFixture("{dryRun:false,execution:{skipped:{not_solid:2,not_visible:3,foundation_or_constructed_tile:1}}}", "dig") == 2, "only native non-solid dig skips count as already satisfied");
        Check(WorldEditorTools.SatisfiedFixture("{dryRun:true,execution:{skipped:{not_solid:2}}}", "dig") == 0, "preview is not completion");
        Check(WorldEditorTools.SatisfiedFixture("{execution:{skipped:{not_solid:2}}}", "sweep") == 0, "other order skips cannot masquerade as completed digging");
        Check(WorldEditorTools.SatisfiedFixture("{execution:{unreachableTargets:2,skipped:{not_visible:2}}}", "dig") == 0, "unreachable/hidden targets remain unresolved");
        var partialBuild = "{dryRun:false,failed:1,results:[{planned:true,valid:true,x:10,y:10,footprint:[{x:10,y:10},{x:11,y:10}]},{valid:false,x:12,y:10}]}";
        Check(WorldEditorTools.BuildCellsFixture(partialBuild) == 2, "partial build retains only successful footprint cells even when the child returns an error");
        Check(WorldEditorTools.BuildCellsFixture("{dryRun:true,results:[{planned:true,x:10,y:10}]}") == 0, "build previews never count as applied cells");
        Check(WorldEditorTools.BuildCellsFixture("{results:[{planned:false,valid:false,x:10,y:10},{planned:true,x:12,y:10}]}") == 1, "failed anchor is excluded from applied cell count");
        var material = JObject.Parse("{valid:false,shortageKg:20}");
        var body = new JObject { ["materialSelection"] = material, ["materials"] = material.DeepClone() };
        var compact = WorldEditorResponsePolicy.Normalize(body, true);
        Check(compact["materials"] == null && (int)compact["materialSelection"]["shortageKg"] == 20, "identical materials deduplicate without losing shortage");
        Check(WorldEditorResponsePolicy.Normalize(body, false)["materials"] != null, "full detail preserves compatibility aliases");
    }

    private static void TestIdlePolicy()
    {
        foreach (string schedule in new[] { "Hygene", "Recreation", "Sleep" })
        {
            string reason = DuplicantIdlePolicy.Reason(true, 17, 99f, false, schedule);
            Check(!DuplicantIdlePolicy.NeedsAttention(reason), "scheduled " + schedule + " is not a safety warning");
            Check(DuplicantIdlePolicy.NeedsAttention(DuplicantIdlePolicy.Reason(true, 0, 99f, false, schedule)), "scheduled " + schedule + " does not hide trapped risk");
        }
        Check(DuplicantIdlePolicy.Reason(true, 17, 99f, false, "Worktime") == "no_current_chore", "actual work schedule ID uses work-idle diagnostics");
        Check(DuplicantIdlePolicy.Reason(true, 17, 99f, true, "Hygene") == "low_calories", "scheduled hygiene does not hide hunger");
        Check(DuplicantIdlePolicy.Reason(true, 17, 10f, false, "Sleep") == "low_stamina", "scheduled sleep does not hide exhaustion");
    }

    private static void TestNeeds()
    {
        var dupe = new MinionIdentity { StoredAmounts = new Amounts() };
        dupe.StoredAmounts.Add("Stamina", 72f);
        dupe.StoredAmounts.Add("Calories", 850000f);
        dupe.StoredAmounts.Add("Stress", 12f);
        dupe.StoredAmounts.Add("Bladder", 30f);
        dupe.StoredAmounts.Add("Breath", 20f);
        dupe.StoredAmounts.Add("Temperature", 310f);
        var needs = DuplicantTools.NeedsFixture(dupe);
        Check((double)needs["stamina"] == 72d && (double)needs["stress"] == 12d, "needs read actual modifier amounts");
        Check((double)needs["caloriesKcal"] == 850d, "calories convert to kcal for readers");
        Check(DuplicantTools.NeedsFlagsFixture(dupe).SequenceEqual(new[] { true, true, true }), "live needs drive hunger and breath checks");
        dupe.StoredAmounts.Get(new Amount { Id = "Calories" }).value = 0f;
        Check(DuplicantTools.NeedsFlagsFixture(dupe)[1], "zero calories is hunger, not missing data");
        dupe.StoredAmounts.Get(new Amount { Id = "Calories" }).value = 1000000f;
        Check(!DuplicantTools.NeedsFlagsFixture(dupe)[1], "hunger threshold is 1000 kcal");
        var missing = new MinionIdentity();
        Check(DuplicantTools.NeedsFixture(missing)["breath"] == null, "missing breath must not claim 100 percent");
        Check(DuplicantTools.NeedsFlagsFixture(missing).All(flag => !flag), "missing data is distinguishable from known low values");
    }
}

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        internal static string ResolveBuildFixture(string token) => TryResolveBuildPrefabFromToken(token, token[0], out string id) ? id : null;
        internal static int SatisfiedFixture(string json, string action) => AlreadySatisfiedOrderCells(CallToolResult.Text(json), action);
        internal static int BuildCellsFixture(string json) => AppliedBuildMapCells(CallToolResult.Error(json), new[] { System.Tuple.Create(10, 10), System.Tuple.Create(11, 10), System.Tuple.Create(12, 10) });
        internal static int AppliedFixture(string json) => ResultAppliedCount(CallToolResult.Text(json));
        internal static JObject GlyphFixture(List<JObject> rows, string input, string direction, string mode) => LookupGlyphQuery(rows, input, direction, mode, null, 20);
        private static List<JObject> FilterGlyphRowsByView(List<JObject> rows, string view) => rows;
        private static string GlyphText(JObject row, string key) => row?[key]?.ToString() ?? "";
    }
    public static partial class DuplicantTools
    {
        internal static Dictionary<string, object> NeedsFixture(MinionIdentity dupe) => KeyNeedValues(dupe).ToDictionary();
        internal static bool[] NeedsFlagsFixture(MinionIdentity dupe)
        {
            var needs = KeyNeedValues(dupe);
            return new[] { needs.HasData, needs.LowCalories, needs.LowBreath };
        }
    }
}

internal sealed class MinionIdentity
{
    internal Amounts StoredAmounts;
    // The old Unity component path returns nothing: Amounts is not a component.
    public T GetComponent<T>() where T : class => null;
}
internal sealed class Db
{
    internal static Db Get() => new Db();
    internal DbAmounts Amounts { get; } = new DbAmounts();
}
internal sealed class DbAmounts { internal Amount Stress { get; } = new Amount { Id = "Stress" }; }
namespace Klei.AI
{
    internal sealed class Amount { internal string Id; internal string Name => "localized_" + Id; }
    internal sealed class AmountInstance { internal Amount amount; internal float value; }
    internal sealed class Amounts
    {
        internal List<AmountInstance> ModifierList { get; } = new List<AmountInstance>();
        internal AmountInstance Get(Amount amount) => ModifierList.FirstOrDefault(item => item.amount.Id == amount.Id);
        internal void Add(string id, float value) => ModifierList.Add(new AmountInstance { amount = new Amount { Id = id }, value = value });
    }
    internal static class ModifiersExtensions
    {
        internal static Amounts GetAmounts(this MinionIdentity dupe) => dupe.StoredAmounts;
    }
}
