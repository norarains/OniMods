using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Tools;

internal static class LiveAuditRegression
{
    private static int checks;
    private static void Check(bool ok, string message)
    { checks++; if (!ok) throw new Exception("Live audit regression: " + message); }
    internal static void Run()
    {
        Warnings(); SupplyTiming(); QueuePlanning(); WireRouting(); Receipts();
        Check(WorldEditorTools.AppliedCountFixture(CallToolResult.Text("{triggeredObjects:2}")) == 2, "cancel receipts count native affected objects");
        Check(WorldEditorTools.AppliedCountFixture(CallToolResult.Text("{dryRun:true,triggeredObjects:2}")) == 0, "cancel preview never claims mutations");
        Check(BatteryThresholdPolicy.Valid(20, 80) && BatteryThresholdPolicy.Valid(0, 100), "battery accepts native percentage range");
        Check(!BatteryThresholdPolicy.Valid(90, 20) && !BatteryThresholdPolicy.Valid(-1, 90)
            && !BatteryThresholdPolicy.Valid(20.1, 90) && !BatteryThresholdPolicy.Valid(double.NaN, 50), "battery rejects invalid hysteresis without clamping");
        Console.WriteLine("Cycle 15 audit regression checks passed: " + checks);
    }
    private static ContinueSample Sample()
    {
        var sample = new ContinueSample { WorldId = 1, FoodKcal = 8000 };
        sample.Dupes.Add(new ContinueDupe { Id = 7, WorldId = 1, Valid = true, Health = 100, Breath = 100 });
        return sample;
    }
    private static void Warnings()
    {
        foreach (string type in new[] { "Bad", "BadMinor", "Tutorial", "DuplicantThreatening" })
            Check(HudFindingPolicy.IsNotificationWarning(type), "native warning category " + type);
        Check(!HudFindingPolicy.IsNotificationWarning("Good") && !HudFindingPolicy.IsNotificationWarning("Messages"), "history and good news do not become warnings");
        Check(HudFindingPolicy.IsDiagnosticWarning("Concern") && !HudFindingPolicy.IsDiagnosticWarning("Normal"), "diagnostic opinion is distinct from notification type");
        var sample = Sample();
        sample.HudFindings.Add(HudFindingPolicy.Notification("Tutorial", "氧气生成不足"));
        sample.HudFindings.Add(HudFindingPolicy.Diagnostic(1, "FoodDiagnostic", "Warning", "Food shortage"));
        var policy = new GameContinuePolicy(); var settings = new ContinueStopEvents();
        Check(policy.Observe(sample, settings).Stops && policy.Findings.Count == 2, "yellow HUD and diagnostic warnings enter canonical findings and stop");
        settings.Update(new[] { "hud" }, null);
        Check(!policy.Observe(sample, settings).Stops && policy.Findings.Count == 2, "HUD ignore changes stopping only");
        Check((string)policy.Findings[0].Details["type"] == "Tutorial", "native category remains inspectable");
    }
    private static void SupplyTiming()
    {
        var sample = Sample(); var policy = new GameContinuePolicy();
        var supply = new BuildingSupplyFinding { Id = 33, WorldId = 1, PrefabId = "AdvancedResearchCenter" };
        supply.Statuses.Add("MaterialsUnavailableForRefill");
        sample.BuildingSupplies.Add(supply);
        sample.ResearchStations.Add(new ContinueResearchStation { Id = 33, WorldId = 1, Required = true });
        Check(!policy.Observe(sample).Stops && policy.Findings.Single().Code == "research_material_missing", "partial research refill is visible without stop churn");
        string id = policy.Findings.Single().Id;
        sample.ResearchStations[0].MissingMaterial = true;
        Check(policy.Observe(sample).Stops && policy.Findings.Single().Id == id, "empty station keeps the refill identity and stops");
        var settings = new ContinueStopEvents(); settings.Update(new[] { id }, null);
        Check(!policy.Observe(sample, settings).Stops && policy.Findings.Count == 1, "exact station ignore survives empty/refill transition");
        sample.ResearchStations.Clear(); supply.Statuses.Clear(); supply.Statuses.Add("LiquidPipeEmpty");
        var timing = new SupplyStatusTiming(); timing.Apply(sample.BuildingSupplies, 100);
        Check(!policy.Observe(sample).Stops && policy.Findings.Count == 1 && !policy.Findings[0].StopEligible, "new native supply status visible while confirming");
        timing.Apply(sample.BuildingSupplies, 100);
        Check(supply.ConfirmationPending, "paused reads do not age native state");
        timing.Apply(sample.BuildingSupplies, 102);
        Check(policy.Observe(sample).Stops, "persistent empty pipe stops after two simulation seconds");
        timing.Apply(new BuildingSupplyFinding[0], 103); timing.Apply(sample.BuildingSupplies, 104);
        Check(supply.ConfirmationPending, "resolved status resets confirmation timer");
        timing.Apply(sample.BuildingSupplies, 10);
        Check(supply.ConfirmationPending, "loading older time resets persistence evidence");
    }
    private static void QueuePlanning()
    {
        var initial = new Dictionary<string, int> { ["food"] = 8, ["metal"] = -1 };
        var items = new[] { JObject.Parse("{recipeId:'food',mode:'add',count:2}"), JObject.Parse("{recipeId:'food',mode:'remove',count:1}") };
        var plan = ProductionQueuePlan.Create(initial, items, false, 99, -1);
        Check(plan.Counts["food"] == 9 && plan.Counts["metal"] == -1, "staged edits compose and preserve untouched recipes");
        Check(initial["food"] == 8 && plan.Changes.Count == 2, "preflight does not alter original queue");
        bool rejected = false;
        try { ProductionQueuePlan.Create(initial, items.Concat(new[] { JObject.Parse("{recipeId:'missing'}") }), true, 99, -1); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected && initial["food"] == 8 && initial["metal"] == -1, "invalid last recipe with clearAll cannot erase live orders");
        plan = ProductionQueuePlan.Create(initial, items, true, 99, -1);
        Check(plan.Counts["food"] == 1 && plan.Counts["metal"] == 0, "clearAll affects projected state before sequential edits");
        plan = ProductionQueuePlan.Create(initial, new[] { JObject.Parse("{recipeId:'food',mode:'add',count:2147483647}") }, false, 99, -1);
        Check(plan.Counts["food"] == 99, "large queue addition does not overflow");
        rejected = false;
        try { ProductionQueuePlan.Create(initial, new[] { JObject.Parse("{recipeId:'food',count:-1}") }, false, 99, -1); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "negative counts reject rather than silently erase orders");
    }
    private static void WireRouting()
    {
        Func<int, IEnumerable<int>> adjacent = n => new[] { n - 1, n + 1, n - 5, n + 5 }
            .Where(m => m >= 0 && m < 25 && Math.Abs(m % 5 - n % 5) + Math.Abs(m / 5 - n / 5) == 1);
        var blocked = new HashSet<int> { 1, 2, 3, 6, 7, 8 };
        var route = ReachableWirePath.Find(0, 4, adjacent, n => !blocked.Contains(n), 25, 25);
        Check(route != null && route.Count > 5 && !route.Any(blocked.Contains), "route goes around obstructed/unreachable shortcut");
        Check(ReachableWirePath.Find(0, 4, adjacent, n => !blocked.Contains(n), 25, 5) == null, "long route never exceeds requested build budget");
        Check(ReachableWirePath.Find(0, 4, adjacent, n => false, 25, 25) == null, "no reachable path cannot claim connectivity");
    }
    private static void Receipts()
    {
        OniToolRegistry.Internal.Clear();
        int calls = 0;
        OniToolRegistry.Internal["building_control"] = new McpTool { Name = "building_control", Mode = "execute", Risk = "dangerous",
            Handler = args => { calls++; return CallToolResult.Text("{id:33,requestedSeed:'BasicPlantSeed',hasActiveRequest:true,harvestWhenReady:true,after:{acceptedTags:['Food']},queue:['A','B']}"); } };
        foreach (string args in new[] { "{domain:'receptacle',action:'list'}", "{domain:'production',action:'list_recipes'}",
            "{domain:'storage',action:'detail'}", "{domain:'side_surface',surface:'user_menu',action:'list'}" })
        {
            var response = ToolBatchTools.CallMany().Handler(new JObject { ["calls"] = new JArray(new JObject {
                ["tool"] = "building_control", ["args"] = JObject.Parse(args) }) });
            Check(!response.IsError, "read-only aggregate needs no confirmation " + args);
            var row = JObject.Parse(response.Content[0].Text)["results"][0];
            Check(row["text"] == null && (bool)row["summary"]["hasActiveRequest"] && row["summary"]["after"] != null,
                "summary retains postconditions without duplicate text");
        }
        var blocked = ToolBatchTools.CallMany().Handler(JObject.Parse("{calls:[{tool:'building_control',args:{domain:'production',action:'batch'}}]}"));
        Check(blocked.IsError && calls == 4, "queue write still requires confirmation");
        OniToolRegistry.Internal.Clear();
    }
}

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        internal static int AppliedCountFixture(CallToolResult result) => ResultAppliedCount(result);
    }
}
