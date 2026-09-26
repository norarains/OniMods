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

        internal void Reset(ContinueSample sample)
        {
            idleSince.Clear();
            previous = sample;
            lastProgress = lastWorkProgress = reviewStarted = sample.GameSeconds;
        }

        internal ContinueDecision Observe(ContinueSample sample)
        {
            if (previous == null || sample.GameSeconds < previous.GameSeconds) Reset(sample);
            var decision = new ContinueDecision();
            if (sample.Dupes.Count == 0) decision.Add("urgent", "no_dupes");
            if (!sample.Available) decision.Add("replan", "monitor_unavailable");
            if (sample.RedAlert) decision.Add("urgent", "red_alert");
            foreach (string alert in sample.Alerts)
                if (alert.StartsWith("DuplicantThreatening:", StringComparison.Ordinal)) decision.Add("urgent", alert);
                else if (!previous.Alerts.Contains(alert)) decision.Add("replan", alert);
            foreach (var dupe in sample.Dupes)
            {
                var old = previous.Dupes.FirstOrDefault(item => item.Id == dupe.Id);
                if (!dupe.Valid) decision.Add("urgent", "invalid_dupe_cell", dupe.Id);
                if (dupe.Breath >= 0 && dupe.Breath < 35) decision.Add("urgent", "low_breath", dupe.Id);
                if (dupe.Health >= 0 && (dupe.Health <= 25 || (old != null && old.Health - dupe.Health >= 1)))
                    decision.Add("urgent", "health", dupe.Id);
                if (dupe.Calories >= 0 && dupe.Calories < 100000) decision.Add("urgent", "starving", dupe.Id);
                if (dupe.Stress >= 80) decision.Add("urgent", "stress", dupe.Id);
                if (dupe.BodyTemperature >= 318.15 || (dupe.BodyTemperature >= 0 && dupe.BodyTemperature < 305.15))
                    decision.Add("urgent", "body_temperature", dupe.Id);
                if (dupe.WorldId != sample.WorldId) continue;
                if (dupe.UnexpectedIdle)
                {
                    if (!idleSince.ContainsKey(dupe.Id)) idleSince[dupe.Id] = sample.GameSeconds;
                    if (sample.GameSeconds - idleSince[dupe.Id] >= 10)
                        decision.Add("replan", "worker_idle", dupe.Id);
                }
                else idleSince.Remove(dupe.Id);
                // Movement and chore changes are activity evidence, never proof of completed orders.
                if (old != null && dupe.Working && (dupe.Cell != old.Cell || dupe.Chore != old.Chore))
                    decision.Activity++;
            }
            foreach (int id in previous.Dupes.Select(item => item.Id).Except(sample.Dupes.Select(item => item.Id)))
                decision.Add("urgent", "dupe_missing", id);
            if (sample.FoodKcal < sample.LocalDupeCount * 1000) decision.Add("urgent", "food_low");
            decision.Completed = previous.PendingIds.Except(sample.PendingIds).Count();
            decision.WorkProgress = sample.WorkProgress > previous.WorkProgress
                || (sample.ResearchId == previous.ResearchId && sample.ResearchProgress > previous.ResearchProgress);
            if (decision.Activity > 0 || decision.Completed > 0 || decision.WorkProgress)
                lastProgress = sample.GameSeconds;
            if (decision.Completed > 0 || decision.WorkProgress) lastWorkProgress = sample.GameSeconds;
            if (previous.PendingIds.Count > 0 && sample.PendingIds.Count == 0)
                decision.Add("replan", "orders_finished");
            if (!string.IsNullOrEmpty(previous.ResearchId) && sample.ResearchId != previous.ResearchId)
                decision.Add("replan", "research_changed");
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
        internal bool RedAlert;
        internal string ResearchId;
        internal readonly List<ContinueDupe> Dupes = new List<ContinueDupe>();
        internal readonly HashSet<int> PendingIds = new HashSet<int>();
        internal readonly HashSet<string> Alerts = new HashSet<string>();
        internal int PendingBuilds, PendingDigs;
        internal int LocalDupeCount => Dupes.Count(item => item.WorldId == WorldId);
        internal int Working => Dupes.Count(item => item.WorldId == WorldId && item.Working);
        internal int Idle => Dupes.Count(item => item.WorldId == WorldId && item.UnexpectedIdle);
    }

    internal sealed class ContinueDupe
    {
        internal int Id, WorldId, Cell;
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
        internal void Add(string decision, string reason, int? dupeId = null)
        {
            if (Decision != "urgent") Decision = decision;
            var item = new Dictionary<string, object> { ["code"] = reason };
            if (dupeId.HasValue) item["dupeId"] = dupeId.Value;
            Reasons.Add(item);
        }
    }
}
