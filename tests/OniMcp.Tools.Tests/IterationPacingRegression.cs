using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Tools;

internal static class IterationPacingRegression
{
    private static int checks;
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception("Iteration pacing: " + message); }

    internal static void Run()
    {
        var idle = HudFindingPolicy.Diagnostic(1, "IdleDiagnostic", "Concern", "Native cached idle");
        Check(!idle.StopEligible && (bool)idle.Details["cached"], "cached idle cannot bypass worker grace");
        Check(idle.Details["ageGameSeconds"] == null, "native cache age is not invented");
        var trend = HudFindingPolicy.Diagnostic(1, "PowerUseDiagnostic", "Concern", "Power use changed", true);
        Check(!trend.StopEligible && trend.Severity == "info", "routine power trend stays visible without stopping");
        var overload = HudFindingPolicy.Diagnostic(1, "PowerUseDiagnostic", "Concern", "Circuit overloaded");
        Check(overload.StopEligible, "power overload is still a stopping condition");
        Check(HudFindingPolicy.Diagnostic(1, "NewDiagnostic", "Warning", "Unknown warning").StopEligible,
            "unrecognized warnings must not be silently demoted");
        Check(ContinueResponse.FinalReason("window_complete", true) == "event", "final event wins deadline collision");
        Check(ContinueResponse.FinalReason("window_complete", false) == "window_complete", "quiet deadline unchanged");
        Check(ContinueResponse.FinalReason("monitor_failed", true) == "monitor_failed", "event cannot mask execution failure");

        Check(HudFindingPolicy.MatchesTemplate("• Power changed 149 W → 440 W", "• Power changed {0} → {1}"),
            "native translated power template matches variable numbers");
        Check(!HudFindingPolicy.MatchesTemplate("Circuit overloaded", "• Power changed {0} → {1}"),
            "overload cannot be confused with trend text");
        var covered = HudFindingPolicy.Notification("BadMinor", "缺少资源");
        covered.StopEligible = false; covered.Details["coveredBy"] = "building_material_missing:1:2";
        var unknown = HudFindingPolicy.Notification("BadMinor", "缺少资源");
        var merged = HudFindingPolicy.CoalesceNotifications(new[] { covered, unknown }).Single();
        Check(merged.StopEligible, "one covered building cannot suppress another uncovered target");
        covered = HudFindingPolicy.Notification("BadMinor", "缺少资源");
        covered.StopEligible = false; covered.Details["coveredBy"] = "building_material_missing:1:2";
        merged = HudFindingPolicy.CoalesceNotifications(new[] { covered }).Single();
        Check(!merged.StopEligible && merged.Message == "缺少资源", "covered warning remains visible without duplicate stop");

        var previewCall = JObject.FromObject(PlanInspectionCalls.Preview("Build two sandstone tiles near the base"));
        Check(previewCall["tool"].ToString() == "world_editor"
            && previewCall["arguments"]["path"].ToString() == "/active/buildings/plans.oni",
            "parse-plan example uses the admitted virtual edit surface");
        Check((bool)previewCall["arguments"]["dryRun"] && !(bool)previewCall["arguments"]["confirm"],
            "generated construction validation cannot mutate");
        var searchCall = JObject.FromObject(PlanInspectionCalls.WorldSearch("Dirt-Oxygen"));
        Check(searchCall["tool"].ToString() == "server_control"
            && searchCall["arguments"]["calls"][0]["tool"].ToString() == "read_control",
            "internal world read is wrapped in the supported batch route");

        var settings = JObject.Parse("{priority:7,defaults:{actionKey:'deconstruct'}}");
        var defaults = MenuBatchDefaults.From(settings);
        Check(defaults["priority"].Value<int>() == 7, "outer work priority inherited");
        Check(settings["defaults"]["priority"] == null, "defaults projection is pure");
        Check(MenuBatchDefaults.From(JObject.Parse("{priority:7,defaults:{priority:8}}"))["priority"].Value<int>() == 8,
            "explicit defaults override outer priority");

        var sample = new ContinueSample { WorldId = 1, FoodKcal = 10000, GameSeconds = 12000 };
        sample.Dupes.Add(new ContinueDupe { Id = 5, WorldId = 1, Valid = true, Health = 100,
            Breath = 100, Stress = 2, Morale = 1, MoraleExpectation = 2 });
        sample.ResearchStations.Add(new ContinueResearchStation { Id = 9, WorldId = 1 });
        sample.HudFindings.Add(trend);
        sample.HudFindings.Add(overload);
        var findings = ColonyObservation.Findings(sample);
        var deficit = findings.Single(item => item.Code == "morale_deficit");
        Check(!deficit.StopEligible && (double)deficit.Details["expectation"] == 2, "morale deficit visible as current fact");
        var full = ContinueResponse.Observation(sample, findings, true, false);
        var brief = ContinueResponse.Observation(sample, findings, false, false);
        Check(JToken.DeepEquals(full["findings"], brief["findings"]), "summary retains every current finding and detail");
        Check(JToken.DeepEquals(full["metrics"], brief["metrics"]), "summary retains all vitals and progress");
        Check(brief["researchStations"] == null && full["researchStations"].HasValues, "healthy station detail is full-only");
        Check(brief["dupeConcerns"].Count() == 1, "stress and morale facts survive summary");
        Check(brief["coverage"]["profile"].ToString() == "colony_v1", "compact coverage references documented profile");
        Check(ContinueResponse.Observation(sample, findings, false, true)["coverage"]["notChecked"] != null,
            "first window includes full coverage limits");

        var old = new Dictionary<string, string> { ["supply:1"] = "same", ["resolved:1"] = "old", ["changed:1"] = "old" };
        var current = new Dictionary<string, string> { ["supply:1"] = "same", ["changed:1"] = "new" };
        var events = new[] { Event("supply:1", true), Event("resolved:1", true), Event("changed:1", true), Event("supply:1", false) };
        Check(ContinueResponse.Events(events, false, old, current).Count == 3,
            "only unchanged ignored finding events are omitted; transient/changed/enabled events remain");
        Check(ContinueResponse.Events(events, true, old, current).Count == 4, "full response retains all events");
        Check(ContinueResponse.Events(events, false, old, current, new HashSet<string> { "supply:1" }).Count == 4,
            "ignored condition that changed and returned within the window is retained");
        Console.WriteLine("Iteration pacing regression: " + checks + " checks passed");
    }

    private static Dictionary<string, object> Event(string id, bool ignored) => new Dictionary<string, object> {
        ["code"] = "building_material_missing", ["findingId"] = id, ["ignored"] = ignored
    };
}
