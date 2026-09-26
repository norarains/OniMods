using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    // One vocabulary and threshold policy for continue, snapshots and diagnostics.
    // A finding describes a current condition; acknowledging it never removes it.
    internal sealed class ColonyFinding
    {
        internal string Id, Code, Severity, Message, Revision = "";
        internal int WorldId;
        internal int? TargetId;
        internal bool Actionable = true;
        internal string Signature => Severity + ":" + Actionable + ":" + Revision;
        internal Dictionary<string, object> ToDictionary() => new Dictionary<string, object>
        {
            ["id"] = Id, ["code"] = Code, ["severity"] = Severity,
            ["worldId"] = WorldId, ["targetId"] = TargetId,
            ["actionable"] = Actionable, ["message"] = Message
        };
    }

    internal static class ColonyObservation
    {
        internal static List<ColonyFinding> Findings(ContinueSample sample)
        {
            var result = new List<ColonyFinding>();
            Action<string, string, string, bool, int?, int> add = (code, severity, message, actionable, target, world) =>
                result.Add(new ColonyFinding { Id = code + ":" + world + (target.HasValue ? ":" + target : ""),
                    Code = code, Severity = severity, Message = message, Actionable = actionable,
                    TargetId = target, WorldId = world });
            int local = sample.LocalDupeCount;
            if (!sample.Available) add("monitor_unavailable", "critical", "Some required observations are unavailable.", true, null, sample.WorldId);
            if (sample.Dupes.Count == 0) add("no_dupes", "critical", "No live duplicants.", true, null, -1);
            if (sample.RedAlert) add("red_alert", "critical", "Red alert is active.", true, null, -1);
            if (sample.FoodKcal < local * 2000)
                add("food_low", sample.FoodKcal < local * 1000 ? "critical" : "warning",
                    "Visible food below " + (sample.FoodKcal < local * 1000 ? "1" : "2") + " nominal cycles per local dupe; fetchability unverified.", true, null, sample.WorldId);
            foreach (var dupe in sample.Dupes)
            {
                Action<string, string, string> dupeFinding = (code, severity, message) => add(code, severity, message, true, dupe.Id, dupe.WorldId);
                if (!dupe.Valid) dupeFinding("invalid_dupe_cell", "critical", "Duplicant position is invalid.");
                if (dupe.Breath >= 0 && dupe.Breath < 35) dupeFinding("low_breath", "critical", "Breath below 35%.");
                if (dupe.Health >= 0 && dupe.Health <= 25) dupeFinding("health", "critical", "Health at or below 25%.");
                if (dupe.Calories >= 0 && dupe.Calories < 100000) dupeFinding("starving", "critical", "Duplicant calories below 100 kcal.");
                if (dupe.Stress > 40) dupeFinding("stress", dupe.Stress >= 80 ? "critical" : "warning", "Elevated stress.");
                if (dupe.BodyTemperature >= 318.15 || (dupe.BodyTemperature >= 0 && dupe.BodyTemperature < 305.15))
                    dupeFinding("body_temperature", "critical", "Unsafe body temperature.");
                if (dupe.SkillPoints > 0)
                {
                    dupeFinding("skill_points_available", "info", "Skill points available: " + dupe.SkillPoints + ". Review skills and morale.");
                    result[result.Count - 1].Revision = dupe.SkillPoints.ToString();
                }
                if ((sample.WorldId < 0 || dupe.WorldId == sample.WorldId) && dupe.UnexpectedIdle)
                    dupeFinding("worker_idle", "warning", "Idle during work time; inspect errands and priorities.");
            }
            if (sample.InfrastructureKnown)
            {
                if (sample.Beds < local) add("beds_short", "warning", "Fewer beds than local duplicants.", true, null, sample.WorldId);
                if (local > 0 && sample.Toilets == 0) add("no_toilet", "warning", "No completed toilet in this world.", true, null, sample.WorldId);
                if (local > 0 && sample.OxygenProducers == 0) add("no_oxygen_producer", "warning", "No completed oxygen producer; inspect passive oxygen reserves.", true, null, sample.WorldId);
            }
            if (sample.PrintingReady) add("printing_pod_ready", "info", "Printing Pod choices ready; inspect care packages before claiming.", true, null, -1);
            if (sample.AdvancedResearchBlocked)
                add("advanced_research_skill_missing", "warning", sample.CanLearnAdvancedResearch
                    ? "No local advanced researcher. A local duplicant has a skill point; inspect Researching1 eligibility and morale."
                    : "No local advanced researcher. Waiting for a skill point; keep other work queued.",
                    sample.CanLearnAdvancedResearch, sample.AdvancedResearchBuildingId, sample.WorldId);
            foreach (string alert in sample.Alerts.OrderBy(item => item, StringComparer.Ordinal))
                add("hud", alert.StartsWith("DuplicantThreatening:", StringComparison.Ordinal) ? "critical" : "warning",
                    alert, true, null, -1);
            // Stable native HUD keys may contain localized text; coalesce exact duplicates only.
            foreach (var finding in result.Where(item => item.Code == "hud")) finding.Id += ":" + finding.Message;
            return result.GroupBy(item => item.Id).Select(group => group.First()).ToList();
        }

        internal static Dictionary<string, object> Serialize(ContinueSample sample, List<ColonyFinding> findings = null)
        {
            return new Dictionary<string, object>
            {
                ["schema"] = 1, ["worldId"] = sample.WorldId, ["cycle"] = Math.Round(sample.GameSeconds / 600, 3),
                ["findings"] = (findings ?? Findings(sample)).Select(item => item.ToDictionary()).ToList(),
                ["coverage"] = new Dictionary<string, object>
                {
                    ["available"] = sample.Available, ["vitals"] = "all_live_dupes",
                    ["foodAndWork"] = sample.WorldId < 0 ? "all_worlds_aggregate" : "selected_world", ["foodMaxAgeSeconds"] = 2, ["infrastructureMaxAgeSeconds"] = 2,
                    ["notChecked"] = new[] { "local_atmosphere", "navigation", "resource_fetchability" }
                },
                ["metrics"] = new Dictionary<string, object>
                {
                    ["dupes"] = sample.LocalDupeCount, ["foodKcal"] = Math.Round(sample.FoodKcal),
                    ["maxStress"] = sample.Dupes.Select(dupe => dupe.Stress).DefaultIfEmpty(-1).Max(),
                    ["minBreath"] = sample.Dupes.Where(dupe => dupe.Breath >= 0).Select(dupe => dupe.Breath).DefaultIfEmpty(-1).Min(),
                    ["minHealthPercent"] = sample.Dupes.Where(dupe => dupe.Health >= 0).Select(dupe => dupe.Health).DefaultIfEmpty(-1).Min(),
                    ["working"] = sample.Working, ["unexpectedIdle"] = sample.Idle,
                    ["pendingBuilds"] = sample.PendingBuilds, ["pendingDigs"] = sample.PendingDigs,
                    ["researchId"] = sample.ResearchId, ["researchPercent"] = Math.Round(sample.ResearchProgress, 1)
                }
            };
        }
    }
}
