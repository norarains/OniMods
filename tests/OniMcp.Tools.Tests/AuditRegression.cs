using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Tools;

internal static class AuditRegression
{
    private static int count;
    private static void Check(bool condition, string message)
    { count++; if (!condition) throw new Exception("Audit regression: " + message); }

    internal static void Run()
    {
        var payload = JObject.Parse("{cycle:2,paused:true,worldId:1,alertLevel:'critical',metrics:{food_kcal:0,stress:99,red_alert:true},watch:{alert:true,triggered:['food_kcal','red_alert'],values:{food_kcal:0,red_alert:true}}}");
        OniToolRegistry.Tools["audit_snapshot"] = new McpTool { Name = "audit_snapshot", Mode = "read", Risk = "low",
            Handler = args => CallToolResult.Text(payload.ToString()) };
        Func<string, JToken> call = mode => JObject.Parse(ToolBatchTools.CallMany().Handler(JObject.Parse(
            "{calls:[{tool:'audit_snapshot'}],responseMode:'" + mode + "'}")).Content[0].Text)["results"][0];
        var summary = call("summary")["summary"];
        Check((bool)summary["paused"] && (int)summary["cycle"] == 2 && (string)summary["alertLevel"] == "critical", "snapshot time and alert level survive batching");
        Check((int)summary["metrics"]["food_kcal"] == 0 && (bool)summary["metrics"]["red_alert"], "dangerous metrics retained");
        Check(JToken.DeepEquals(payload["watch"], summary["watch"]), "watch evidence retained");
        var full = call("full");
        Check(full["text"] == null && full["content"] == null && JToken.DeepEquals(payload, full["result"]), "one lossless structured child payload");
        payload = new JObject { ["delta"] = true, ["baseline"] = true, ["snapshot"] = payload.DeepClone() };
        Check((bool)call("summary")["summary"]["snapshot"]["watch"]["alert"], "delta baseline preserves safety");
        payload = JObject.Parse("{delta:true,unchanged:false,changed:{paused:true,watch:{alert:true},metrics:{food_kcal:0}}}");
        Check((int)call("summary")["summary"]["changed"]["metrics"]["food_kcal"] == 0, "changed delta preserves safety");
        OniToolRegistry.Tools.Remove("audit_snapshot");

        var savedGame = OniToolRegistry.Tools.ContainsKey("game_control") ? OniToolRegistry.Tools["game_control"] : null;
        int executed = 0;
        OniToolRegistry.Tools["game_control"] = new McpTool { Name = "game_control", Mode = "execute", Risk = "dangerous",
            Handler = args => { executed++; return CallToolResult.Text("{paused:true}"); } };
        var time = ToolBatchTools.CallMany().Handler(JObject.Parse("{calls:[{tool:'game_control',args:{domain:'speed',action:'time'}}]}"));
        Check(!time.IsError && executed == 1, "read-only time needs no confirmation");
        var resume = ToolBatchTools.CallMany().Handler(JObject.Parse("{calls:[{tool:'game_control',args:{domain:'speed',action:'resume'}}]}"));
        Check(resume.IsError && executed == 1, "mutation still requires confirmation");
        if (savedGame == null) OniToolRegistry.Tools.Remove("game_control"); else OniToolRegistry.Tools["game_control"] = savedGame;

        var budget = new BuildMaterialBudget();
        Check(BuildMaterialRequirements.SingleMaterialKg(new[] { 100f }, 1) == 100, "ladder mass comes from recipe");
        Check(BuildMaterialRequirements.SingleMaterialKg(new[] { 100f, 50f }, 2) == 0, "multiple ingredients cannot be charged to one selected stock");
        Check(BuildMaterialRequirements.SingleMaterialKg(new[] { 100f }, 2) == 0, "malformed multi-category recipe stays unknown");
        Check(BuildMaterialRequirements.SingleMaterialKg(new[] { float.NaN }, 1) == 0, "invalid mass stays unknown");
        Check(budget.TryReserve("Rock", 100, 150, out _), "first anchor affordable");
        Check(!budget.TryReserve("Rock", 100, 150, out double shortage) && shortage == 50, "batch cannot spend same inventory twice");
        Check(budget.TryReserve("rock", 50, 150, out _), "failed reservation does not consume stock");
        Check(!budget.TryReserve("Rock", 1, double.NaN, out _), "unknown quantity cannot pass budget");
        Check(BuildingIndexFilter.Matches("Ladder", "梯子", "梯", "structure", false), "localized filtered construction");
        Check(!BuildingIndexFilter.Matches("PropGravitasWall", "Wall", "", "", false), "POI hidden by default");
        Check(BuildingIndexFilter.Matches("PropGravitasWall", "Wall", "", "poi", true), "POI opt-in");
        var repeated = JObject.Parse("{previews:[{x:1,valid:true,materialSelection:{requiredKg:100}},{x:2,valid:false,error:'blocked',materialSelection:{requiredKg:100}}]}");
        var compact = WorldEditorResponsePolicy.Normalize(repeated, true);
        Check((int)compact["shared"]["previews"]["materialSelection"]["requiredKg"] == 100, "equal material diagnostics hoisted");
        Check((string)compact["previews"][1]["error"] == "blocked" && (int)compact["previews"][1]["x"] == 2, "unique anchor failure retained");
        Check(JToken.DeepEquals(repeated, WorldEditorResponsePolicy.Normalize(repeated, false)), "full evidence preserved");
        ((JObject)repeated["previews"][1]["materialSelection"])["requiredKg"] = 200;
        Check(WorldEditorResponsePolicy.Normalize(repeated, true)["shared"] == null, "different material reports never merged");
        Console.WriteLine("Gameplay audit regression checks passed: " + count);
    }
}
