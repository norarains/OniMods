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
        var policy = new GameContinuePolicy();
        var settings = new ContinueStopEvents();
        var sample = Sample(); sample.PrintingReady = true;
        Check(policy.Observe(sample, settings).Stops, "pending reward stops on the first observation");
        string podId = policy.Findings.Single().Id;
        settings.Update(new[] { "printing_pod_ready" }, null);
        Check(!policy.Observe(sample, settings).Stops && policy.Findings.Single().Id == podId, "ignored rewards remain visible");
        sample = Sample(1); policy.Observe(sample, settings);
        Check(policy.Findings.Count == 0, "claim resolves the finding");
        sample.PrintingReady = true;
        Check(!policy.Observe(sample, settings).Stops, "ignores persist across resolution and recurrence");
        settings.Update(null, new[] { "printing_pod_ready" });
        Check(policy.Observe(sample, settings).Stops, "unignore restores interruption");
        settings.Update(new[] { podId }, null);
        Check(!policy.Observe(sample, settings).Stops, "exact finding IDs can be ignored");
        bool invalid = false;
        try { settings.Update(new[] { "food_low", "bogus" }, null); } catch (ArgumentException) { invalid = true; }
        Check(invalid && !settings.IsIgnored("food_low"), "invalid updates are atomic");
        invalid = false;
        try { settings.Update(new[] { "health" }, new[] { "health" }); } catch (ArgumentException) { invalid = true; }
        Check(invalid, "conflicting changes are rejected");
        var hudSettings = new ContinueStopEvents();
        hudSettings.Update(new[] { "hud:-1:Bad: Known issue" }, null);
        var hudEvents = new ContinueEvents(hudSettings);
        hudEvents.Add("hud", null, "hud:-1:Bad: Known issue");
        hudEvents.Add("hud", null, "hud:-1:Bad: New issue");
        Check(hudEvents.Stops && hudEvents.Items.Count == 2, "distinct HUD findings retain their individual interruption state");
        sample = Sample(); sample.AdvancedResearchBlocked = true; sample.AdvancedResearchBuildingId = 99;
        policy.Observe(sample, settings);
        Check(!policy.Findings.Single().Actionable, "skill blocker can be pending experience");
        var before = policy.Findings.ToDictionary(f => f.Id, f => f.Signature);
        sample.CanLearnAdvancedResearch = true; sample.Dupes[0].SkillPoints = 1;
        policy.Observe(sample, settings);
        var after = policy.Findings.ToDictionary(f => f.Id, f => f.Signature);
        var delta = ColonyObservation.Delta(before, after);
        Check(delta["changed"].Length == 1 && delta["added"].Length == 1, "actionability change and earned skill point have separate net deltas");
        Check(ColonyObservation.Delta(before, before).Values.All(ids => ids.Length == 0), "transient resolve/reappear produces no contradictory net change");
        sample.Dupes[0].Breath = 20;
        Check(policy.Observe(sample, settings).Stops, "unignored danger still stops");
        var shared = ColonyObservation.Findings(sample);
        Check(JToken.DeepEquals(JArray.FromObject(shared.Select(f => f.ToDictionary())),
            JObject.FromObject(ColonyObservation.Serialize(sample))["findings"]), "all observations share one findings contract");
        Check(shared.Select(f => f.Id).Distinct().Count() == shared.Count, "finding IDs are unique");
        sample = Sample(); sample.FoodKcal = 1500;
        Check(ColonyObservation.Findings(sample).Single().Severity == "warning", "food warning threshold");
        sample.FoodKcal = 999;
        Check(ColonyObservation.Findings(sample).Single().Severity == "critical", "food critical threshold");
        sample = Sample(); sample.ResearchId = "Basic"; sample.ResearchQueueCount = 2;
        policy.Reset(sample);
        var next = Sample(5); next.ResearchId = "QueuedNext"; next.ResearchQueueCount = 1;
        Check(!policy.Observe(next).Stops, "automatic queued research transition does not interrupt");
        next = Sample(6);
        Check(policy.Observe(next).Items.Any(r => (string)r["code"] == "research_queue_empty"), "empty research queue is an event");
        sample = Sample(); sample.PendingIds.Add(1); policy.Reset(sample);
        next = Sample(1); next.Dupes[0].Working = true;
        Check(!policy.Observe(next).Stops, "finished construction does not interrupt recurring work");
        settings.Update(new[] { "worker_idle" }, null);
        sample = Sample(); sample.Dupes[0].UnexpectedIdle = true; policy.Reset(sample); policy.Observe(sample, settings);
        sample.GameSeconds = 11;
        Check(!policy.Observe(sample, settings).Stops && policy.Findings.Any(f => f.Code == "worker_idle"), "ignored idle remains visible after grace");
        sample.Dupes[0].UnexpectedIdle = false; policy.Observe(sample, settings);
        sample.Dupes[0].UnexpectedIdle = true; policy.Observe(sample, settings); sample.GameSeconds = 50;
        Check(!policy.Observe(sample, settings).Stops, "transient chore change does not clear explicit idle ignore");
        sample = Sample(); sample.WorldId = -1; sample.Dupes[0].UnexpectedIdle = true;
        Check(ColonyObservation.Findings(sample).Any(item => item.Code == "worker_idle") && sample.Idle == 1, "all-world idle counts agree");
        sample = Sample();
        var station = new ContinueResearchStation { Id = 12, WorldId = 1, ResearchType = "advanced", MissingMaterial = true, Powered = false, Operational = false, DeliveryItem = "Water" };
        sample.ResearchStations.Add(station);
        Check(ColonyObservation.Findings(sample).Count == 0, "unused research station does not report an active stall");
        station.Required = true;
        var codes = ColonyObservation.Findings(sample).Select(f => f.Code).ToArray();
        Check(codes.Contains("research_unpowered") && codes.Contains("research_material_missing"), "active station includes empty storage and missing power");
        Check(!codes.Contains("research_inoperable"), "power failure is not duplicated as a generic inoperable finding");
        Check(((object)ColonyObservation.Serialize(sample)["researchStations"]) != null, "station facts travel with the common observation");
        TestFootprints();
        TestSupplies();
        TestFreshStopEvidence();
        Console.WriteLine("Observation and footprint regression checks passed: " + checks);
    }

    private static void TestFreshStopEvidence()
    {
        var cached = Sample();
        cached.HudFindings.Add(HudFindingPolicy.Notification("BadMinor", "Building lacks resources"));
        var fresh = Sample();
        var covered = HudFindingPolicy.Notification("BadMinor", "Building lacks resources");
        covered.StopEligible = false;
        covered.Details["detailView"] = "building_warnings";
        fresh.HudFindings.Add(covered);
        int refreshes = 0;
        var settings = new ContinueStopEvents();
        var sample = ContinueObservationFreshness.BeforeStop(cached, settings, () => { refreshes++; return fresh; });
        Check(refreshes == 1 && !new GameContinuePolicy().Observe(sample, settings).Stops, "fresh coverage prevents a cached generic shortage from stopping");
        settings.Update(new[] { "hud" }, null);
        ContinueObservationFreshness.BeforeStop(cached, settings, () => { refreshes++; return fresh; });
        Check(refreshes == 1, "ignored findings do not trigger redundant refreshes");
        ContinueObservationFreshness.BeforeStop(Sample(), settings, () => { refreshes++; return fresh; });
        Check(refreshes == 1, "healthy rounds retain inexpensive cached sampling");
    }

    private static void TestSupplies()
    {
        Check(BuildingSupplyFinding.IsShortage("MaterialsUnavailable") && BuildingSupplyFinding.IsShortage("NeedLiquidIn"), "native supply blockers are recognized");
        Check(!BuildingSupplyFinding.IsShortage("WaitingForMaterials") && !BuildingSupplyFinding.IsShortage("FabricatorEmpty")
            && !BuildingSupplyFinding.IsShortage("ElementConverterInput"), "routine deliveries, unused recipes and normal consumption are not shortages");
        var sample = Sample();
        var shortage = new BuildingSupplyFinding { Id = 31, WorldId = 1, X = 200, Y = 42, PrefabId = "Electrolyzer", Construction = false };
        shortage.Statuses.Add("NeedLiquidIn"); shortage.Messages.Add("Water input missing"); shortage.Missing["Water:kg"] = 1;
        sample.BuildingSupplies.Add(shortage);
        var policy = new GameContinuePolicy(); var settings = new ContinueStopEvents();
        Check(policy.Observe(sample, settings).Stops, "general building shortages stop by default");
        var finding = policy.Findings.Single();
        Check(finding.Id == "building_material_missing:1:31" && (string)finding.Details["prefabId"] == "Electrolyzer", "supply findings include a stable target and compact location");
        settings.Update(new[] { finding.Id }, null);
        Check(!policy.Observe(sample, settings).Stops && policy.Findings.Count == 1, "ignoring one building does not hide its shortage");
        shortage.Construction = true;
        Check(!policy.Observe(sample, settings).Stops && policy.Findings.Count == 0, "ordinary construction shortages are on-demand, not routine findings");
        shortage.Construction = false; shortage.PrefabId = "RockCrusher";
        Check(!policy.Observe(sample, settings).Stops && policy.Findings.Count == 0, "ordinary machine shortages remain on-demand");
        shortage.PrefabId = "Electrolyzer";
        policy.Observe(sample, settings);
        var old = policy.Findings.ToDictionary(f => f.Id, f => f.Signature);
        shortage.Missing["Water:kg"] = 0.5; policy.Observe(sample, settings);
        Check(ColonyObservation.Delta(old, policy.Findings.ToDictionary(f => f.Id, f => f.Signature))["changed"].Length == 0, "small delivery changes do not churn finding identities");
        sample.BuildingSupplies.Clear();
        Check(ColonyObservation.Findings(sample).Count == 0, "resolved shortages disappear");
        shortage.Construction = false; sample.BuildingSupplies.Add(shortage);
        var station = new ContinueResearchStation { Id = 31, WorldId = 1, Required = true, MissingMaterial = true, Powered = null,
            PowerNetworkPending = true, Operational = false, DeliveryItem = "Water" };
        sample.ResearchStations.Add(station);
        finding = ColonyObservation.Findings(sample).Single();
        Check(finding.Code == "research_material_missing" && finding.Details != null, "research compatibility event absorbs native supply details without duplicates");
        sample.BuildingSupplies.Clear(); station.MissingMaterial = false;
        Check(ColonyObservation.Findings(sample).Count == 0, "pending power refresh is not a reported outage");
        station.PowerNetworkPending = false; station.Powered = false;
        Check(ColonyObservation.Findings(sample).Single().Code == "research_unpowered", "resolved power state still reports real outages");
        Check(JObject.FromObject(ColonyObservation.Serialize(sample))["coverage"]["notChecked"].Values<string>().Contains("local_atmosphere"), "no routine atmospheric polling added");
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
