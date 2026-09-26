using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Tools;

internal static class ObservationRegression
{
    private static int checks;
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception("Observation regression: " + message); }

    private static ContinueSample Sample(double seconds = 0)
    {
        var sample = new ContinueSample { WorldId = 1, FoodKcal = 5000, GameSeconds = seconds };
        sample.Dupes.Add(new ContinueDupe { Id = 42, WorldId = 1, Valid = true, Breath = 100,
            Health = 100, Calories = 2000000, Stress = 0, BodyTemperature = 310 });
        return sample;
    }

    internal static void Run()
    {
        var p = new GameContinuePolicy();
        var sample = Sample(); sample.PrintingReady = true;
        p.Reset(sample);
        Check(p.Observe(sample).Reasons.Any(r => (string)r["code"] == "printing_pod_ready"), "an already pending reward must appear on first observation");
        var podId = p.Findings.Single().Id;
        p.Reset(sample, acknowledge: true);
        Check(p.Observe(sample).Decision == "continue" && p.Findings.Single().Id == podId, "reviewed reward persists without a repeated interruption");
        sample = Sample(1); p.Observe(sample);
        Check(p.Resolved.Contains(podId), "claiming reward resolves its finding");
        sample.PrintingReady = true;
        Check(p.Observe(sample).Decision == "replan", "a later reward must interrupt again");
        p = new GameContinuePolicy(); sample = Sample();
        sample.AdvancedResearchBlocked = true; sample.AdvancedResearchBuildingId = 99;
        p.Reset(sample); p.Observe(sample);
        Check(!p.Findings.Single().Actionable, "skill blocker remains visible while waiting for experience");
        string skillId = p.Findings.Single().Id;
        p.Reset(sample, acknowledge: true);
        Check(p.Observe(sample).Decision == "continue", "known pending skill blocker does not repeatedly stop work");
        sample.CanLearnAdvancedResearch = true; sample.Dupes[0].SkillPoints = 1;
        Check(p.Observe(sample).Decision == "replan" && p.Changed.Contains(skillId), "skill point makes the blocker actionable again");
        p.Reset(sample, acknowledge: true); sample.Dupes[0].Breath = 20;
        Check(p.Observe(sample).Decision == "urgent", "urgent change stops despite reviewed findings");
        p.Reset(sample, acknowledge: true);
        Check(p.Observe(sample).Decision == "urgent", "acknowledgement never waives an ongoing emergency");
        var shared = ColonyObservation.Findings(sample);
        Check(JToken.DeepEquals(JArray.FromObject(shared.Select(f => f.ToDictionary())),
            JObject.FromObject(ColonyObservation.Serialize(sample))["findings"]), "paused serialization uses the same findings as the continue policy");
        Check(shared.Select(f => f.Id).Distinct().Count() == shared.Count, "finding IDs are unique");
        sample = Sample(); sample.FoodKcal = 1500;
        Check(ColonyObservation.Findings(sample).Single().Severity == "warning", "food warning and urgent tiers are explicit");
        sample.FoodKcal = 999;
        Check(ColonyObservation.Findings(sample).Single().Severity == "critical", "food below one local nominal cycle is critical");
        sample = Sample(); sample.ResearchId = "Basic"; sample.ResearchQueueCount = 2;
        p = new GameContinuePolicy(); p.Reset(sample);
        var next = Sample(5); next.ResearchId = "QueuedNext"; next.ResearchQueueCount = 1;
        Check(p.Observe(next).Decision == "continue", "automatic queued research transition does not force planning");
        next = Sample(6);
        Check(p.Observe(next).Reasons.Any(r => (string)r["code"] == "research_queue_empty"), "exhausted research queue requests a review");
        sample = Sample(); sample.PendingIds.Add(1); p.Reset(sample);
        next = Sample(1); next.Dupes[0].Working = true;
        Check(p.Observe(next).Decision == "continue", "finished construction does not interrupt recurring work");
        sample = Sample(); sample.Dupes[0].UnexpectedIdle = true; p.Reset(sample); p.Observe(sample);
        sample.GameSeconds = 11;
        Check(p.Observe(sample).Decision == "replan", "idle grace expires");
        p.Reset(sample, acknowledge: true); sample.GameSeconds = 22;
        Check(p.Observe(sample).Decision == "continue", "reviewed idle remains visible without stopping every ten seconds");
        sample.GameSeconds = 312;
        Check(p.Observe(sample).Reasons.Any(r => (string)r["code"] == "review_due"), "reviewed conditions cannot suppress periodic review");
        sample = Sample(); sample.WorldId = -1; sample.Dupes[0].UnexpectedIdle = true;
        Check(ColonyObservation.Findings(sample).Any(item => item.Code == "worker_idle") && sample.Idle == 1, "all-world observation includes idle findings and counts consistently");
        TestFootprints();
        Console.WriteLine("Observation and footprint regression checks passed: " + checks);
    }

    private static void TestFootprints()
    {
        var layout = new BuildingFootprintLayout(new[] { Tuple.Create(-1, 0), Tuple.Create(0, 0), Tuple.Create(1, 0), Tuple.Create(-1, 1) });
        Check(layout.OriginX(288) == 289 && layout.OriginY(74) == 74, "negative native offsets require translating the requested lower-left anchor");
        Check(layout.Cells(288, 74).SequenceEqual(new[] { Tuple.Create(288, 74), Tuple.Create(289, 74), Tuple.Create(290, 74), Tuple.Create(288, 75) }), "footprint preserves nonrectangular native occupancy");
        Check(layout.Width == 3 && layout.Height == 2, "bounds come from native offsets");
        var rotated = new BuildingFootprintLayout(new[] { Tuple.Create(0, -1), Tuple.Create(0, 0), Tuple.Create(0, 1), Tuple.Create(1, -1) });
        Check(rotated.OriginY(74) == 75 && rotated.Width == 2 && rotated.Height == 3, "rotation changes the offset-to-origin conversion");
        Check(rotated.Cells(288, 74).All(point => point.Item1 >= 288 && point.Item2 >= 74), "rotated placement preserves lower-left coordinates");
        var even = new BuildingFootprintLayout(new[] { Tuple.Create(0, 0), Tuple.Create(1, 0) });
        Check(even.OriginX(10) == 10 && even.Cells(10, 20).Count() == 2, "even widths use native offsets rather than guessed half-widths");
        bool threw = false;
        try { new BuildingFootprintLayout(new Tuple<int, int>[0]); } catch (ArgumentException) { threw = true; }
        Check(threw, "missing offsets cannot claim a valid placement");
    }
}
