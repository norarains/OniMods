using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    // Pure decision logic: no Unity access, no tool calls, no planning in the fast path.
    internal sealed class GameContinuePolicy
    {
        internal const double MaxSeconds = 20;
        internal const double ReviewGameSeconds = 300;
        private readonly Dictionary<int, double> idleSince = new Dictionary<int, double>();
        private double lastProgress, lastWorkProgress;
        private double reviewStarted;
        private ContinueSample previous;
        private readonly Dictionary<string, string> reviewed = new Dictionary<string, string>();
        private Dictionary<string, string> lastFindings = new Dictionary<string, string>();
        internal List<ColonyFinding> Findings = new List<ColonyFinding>();
        internal string[] Added = new string[0], Resolved = new string[0], Changed = new string[0];

        internal void Reset(ContinueSample sample, bool acknowledge = false)
        {
            if (acknowledge)
                foreach (var item in lastFindings) reviewed[item.Key] = item.Value;
            else { reviewed.Clear(); lastFindings.Clear(); idleSince.Clear(); }
            previous = sample;
            lastProgress = lastWorkProgress = reviewStarted = sample.GameSeconds;
        }

        internal ContinueDecision Observe(ContinueSample sample)
        {
            if (previous == null || sample.GameSeconds < previous.GameSeconds) Reset(sample);
            var decision = new ContinueDecision();
            Findings = ColonyObservation.Findings(sample);
            var current = Findings.ToDictionary(item => item.Id, item => item.Signature);
            Added = current.Keys.Except(lastFindings.Keys).ToArray();
            Resolved = lastFindings.Keys.Except(current.Keys).ToArray();
            Changed = current.Keys.Where(id => lastFindings.ContainsKey(id) && lastFindings[id] != current[id]).ToArray();
            foreach (string id in Resolved) reviewed.Remove(id);
            foreach (var finding in Findings)
            {
                if (finding.Code == "worker_idle") continue; // short chore transitions get a grace period
                bool acknowledged = reviewed.TryGetValue(finding.Id, out string signature) && signature == finding.Signature;
                if (finding.Severity == "critical" || !acknowledged)
                    decision.Add(finding.Severity == "critical" ? "urgent" : "replan", finding.Code, finding.TargetId, finding.Id);
            }
            lastFindings = current;
            foreach (var dupe in sample.Dupes)
            {
                var old = previous.Dupes.FirstOrDefault(item => item.Id == dupe.Id);
                if (old != null && dupe.Health >= 0 && old.Health - dupe.Health >= 1)
                    decision.Add("urgent", "health", dupe.Id);
                if (dupe.WorldId != sample.WorldId) continue;
                if (dupe.UnexpectedIdle)
                {
                    if (!idleSince.ContainsKey(dupe.Id)) idleSince[dupe.Id] = sample.GameSeconds;
                    if (sample.GameSeconds - idleSince[dupe.Id] >= 10 && !reviewed.ContainsKey("worker_idle:" + dupe.WorldId + ":" + dupe.Id))
                        decision.Add("replan", "worker_idle", dupe.Id, "worker_idle:" + dupe.WorldId + ":" + dupe.Id);
                }
                else idleSince.Remove(dupe.Id);
                // Movement and chore changes are activity evidence, never proof of completed orders.
                if (old != null && dupe.Working && (dupe.Cell != old.Cell || dupe.Chore != old.Chore))
                    decision.Activity++;
            }
            foreach (int id in previous.Dupes.Select(item => item.Id).Except(sample.Dupes.Select(item => item.Id)))
                decision.Add("urgent", "dupe_missing", id);
            decision.Completed = previous.PendingIds.Except(sample.PendingIds).Count();
            decision.WorkProgress = sample.WorkProgress > previous.WorkProgress
                || (sample.ResearchId == previous.ResearchId && sample.ResearchProgress > previous.ResearchProgress);
            if (decision.Activity > 0 || decision.Completed > 0 || decision.WorkProgress)
                lastProgress = sample.GameSeconds;
            if (decision.Completed > 0 || decision.WorkProgress) lastWorkProgress = sample.GameSeconds;
            if (previous.PendingIds.Count > 0 && sample.PendingIds.Count == 0 && sample.Working == 0 && string.IsNullOrEmpty(sample.ResearchId))
                decision.Add("replan", "orders_finished");
            if (!string.IsNullOrEmpty(previous.ResearchId) && string.IsNullOrEmpty(sample.ResearchId) && sample.ResearchQueueCount == 0)
                decision.Add("replan", "research_queue_empty");
            if (sample.Working > 0 && sample.GameSeconds - lastProgress >= 60)
                decision.Add("replan", "no_observed_progress");
            else if (sample.Working > 0 && sample.PendingIds.Count > 0 && sample.GameSeconds - lastWorkProgress >= 120)
                decision.Add("replan", "orders_not_progressing");
            if (sample.GameSeconds - reviewStarted >= ReviewGameSeconds)
                decision.Add("replan", "review_due");
            previous = sample;
            return decision;
        }
    }

    internal sealed class ContinueSample
    {
        internal bool Available = true;
        internal int WorldId;
        internal double GameSeconds, FoodKcal, WorkProgress, ResearchProgress;
        internal bool RedAlert, PrintingReady, InfrastructureKnown, AdvancedResearchBlocked, CanLearnAdvancedResearch;
        internal int Beds, Toilets, OxygenProducers, ResearchQueueCount;
        internal int? AdvancedResearchBuildingId;
        internal string ResearchId;
        internal readonly List<ContinueDupe> Dupes = new List<ContinueDupe>();
        internal readonly HashSet<int> PendingIds = new HashSet<int>();
        internal readonly HashSet<string> Alerts = new HashSet<string>();
        internal int PendingBuilds, PendingDigs;
        internal int LocalDupeCount => Dupes.Count(item => (WorldId < 0 || item.WorldId == WorldId));
        internal int Working => Dupes.Count(item => (WorldId < 0 || item.WorldId == WorldId) && item.Working);
        internal int Idle => Dupes.Count(item => (WorldId < 0 || item.WorldId == WorldId) && item.UnexpectedIdle);
    }

    internal sealed class ContinueDupe
    {
        internal int Id, WorldId, Cell, SkillPoints;
        internal bool AdvancedResearchSkill;
        internal bool Valid, Working, UnexpectedIdle;
        internal double Breath = -1, Health = -1, Calories = -1, Stress = -1, BodyTemperature = -1;
        internal string Chore;
    }

    internal sealed class ContinueDecision
    {
        internal string Decision = "continue";
        internal int Activity, Completed;
        internal bool WorkProgress;
        internal readonly List<Dictionary<string, object>> Reasons = new List<Dictionary<string, object>>();
        internal void Add(string decision, string reason, int? dupeId = null, string findingId = null)
        {
            if (Decision != "urgent") Decision = decision;
            var item = new Dictionary<string, object> { ["code"] = reason };
            if (dupeId.HasValue) item["targetId"] = dupeId.Value;
            if (findingId != null) item["findingId"] = findingId;
            Reasons.Add(item);
        }
    }
}
