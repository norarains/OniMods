using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    // Event detection only. The client decides whether to plan, act, or advance again.
    internal sealed class GameContinuePolicy
    {
        internal const double MaxSeconds = 20;
        private readonly Dictionary<int, double> idleSince = new Dictionary<int, double>();
        private double lastProgress, lastWorkProgress;
        private ContinueSample previous;
        internal List<ColonyFinding> Findings = new List<ColonyFinding>();

        internal void Reset(ContinueSample sample)
        {
            previous = sample;
            lastProgress = lastWorkProgress = sample.GameSeconds;
            idleSince.Clear();
        }

        internal ContinueEvents Observe(ContinueSample sample, ContinueStopEvents settings = null)
        {
            if (previous == null || sample.GameSeconds < previous.GameSeconds) Reset(sample);
            var events = new ContinueEvents(settings ?? new ContinueStopEvents());
            Findings = ColonyObservation.Findings(sample);
            foreach (var finding in Findings)
                if (finding.Code != "worker_idle")
                    events.Add(finding.Code, finding.TargetId, finding.Id);
            foreach (var dupe in sample.Dupes)
            {
                var old = previous.Dupes.FirstOrDefault(item => item.Id == dupe.Id);
                if (old != null && dupe.Health >= 0 && old.Health - dupe.Health >= 1)
                    events.Add("health", dupe.Id, "health:" + dupe.WorldId + ":" + dupe.Id);
                if (sample.WorldId >= 0 && dupe.WorldId != sample.WorldId) continue;
                if (dupe.UnexpectedIdle)
                {
                    if (!idleSince.ContainsKey(dupe.Id)) idleSince[dupe.Id] = sample.GameSeconds;
                    if (sample.GameSeconds - idleSince[dupe.Id] >= 10)
                        events.Add("worker_idle", dupe.Id, "worker_idle:" + dupe.WorldId + ":" + dupe.Id);
                }
                else idleSince.Remove(dupe.Id);
                if (old != null && dupe.Working && (dupe.Cell != old.Cell || dupe.Chore != old.Chore))
                    events.Activity++;
            }
            foreach (int id in previous.Dupes.Select(item => item.Id).Except(sample.Dupes.Select(item => item.Id)))
                events.Add("dupe_missing", id);
            events.Completed = previous.PendingIds.Except(sample.PendingIds).Count();
            events.WorkProgress = sample.WorkProgress > previous.WorkProgress
                || (sample.ResearchId == previous.ResearchId && sample.ResearchProgress > previous.ResearchProgress);
            bool newWork = sample.PendingIds.Except(previous.PendingIds).Any() || sample.ResearchId != previous.ResearchId;
            if (newWork || events.Activity > 0 || events.Completed > 0 || events.WorkProgress)
                lastProgress = sample.GameSeconds;
            if (newWork || events.Completed > 0 || events.WorkProgress) lastWorkProgress = sample.GameSeconds;
            if (previous.PendingIds.Count > 0 && sample.PendingIds.Count == 0 && sample.Working == 0 && string.IsNullOrEmpty(sample.ResearchId))
                events.Add("orders_finished");
            if (!string.IsNullOrEmpty(previous.ResearchId) && string.IsNullOrEmpty(sample.ResearchId) && sample.ResearchQueueCount == 0)
                events.Add("research_queue_empty");
            if (sample.Working > 0 && sample.GameSeconds - lastProgress >= 60)
                events.Add("no_observed_progress");
            else if (sample.Working > 0 && sample.PendingIds.Count > 0 && sample.GameSeconds - lastWorkProgress >= 120)
                events.Add("orders_not_progressing");
            previous = sample;
            return events;
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
        internal readonly List<ContinueResearchStation> ResearchStations = new List<ContinueResearchStation>();
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

    internal sealed class ContinueEvents
    {
        private readonly ContinueStopEvents settings;
        internal bool Stops { get; private set; }
        internal int Activity, Completed;
        internal bool WorkProgress;
        internal readonly List<Dictionary<string, object>> Items = new List<Dictionary<string, object>>();
        internal ContinueEvents(ContinueStopEvents settings) { this.settings = settings; }
        internal void Add(string code, int? targetId = null, string findingId = null)
        {
            if (!ContinueStopEvents.Defaults.Contains(code)) return;
            bool ignored = settings.IsIgnored(code, findingId);
            Stops |= !ignored;
            var item = new Dictionary<string, object> { ["code"] = code };
            if (targetId.HasValue) item["targetId"] = targetId.Value;
            if (findingId != null) item["findingId"] = findingId;
            if (ignored) item["ignored"] = true;
            if (!Items.Any(existing => existing["code"].Equals(code)
                && Equals(existing.ContainsKey("targetId") ? existing["targetId"] : null, targetId)
                && Equals(existing.ContainsKey("findingId") ? existing["findingId"] : null, findingId)))
                Items.Add(item);
        }
    }
}
