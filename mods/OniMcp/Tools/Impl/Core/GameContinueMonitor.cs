using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    // Cheap per-dupe samples every half second. Food is cached for two seconds;
    // pending order objects are found once per requested window, never by scanning Grid.
    internal sealed class GameContinueMonitor
    {
        private readonly int worldId;
        private static Game timingGame;
        private static readonly Dictionary<int, SupplyStatusTiming> supplyTiming = new Dictionary<int, SupplyStatusTiming>();
        private Constructable[] builds;
        private Diggable[] digs;
        private readonly Dictionary<int, float> remainingWork = new Dictionary<int, float>();
        private double nextFoodRead, foodKcal, workProgress, nextInfrastructureRead;
        private int beds, toilets, oxygenProducers;
        private string infrastructureResearchId;
        private List<ContinueResearchStation> researchStations = new List<ContinueResearchStation>();
        private readonly List<BuildingSupplyFinding> buildingSupplies = new List<BuildingSupplyFinding>();

        internal GameContinueMonitor(int worldId)
        {
            this.worldId = worldId;
            RefreshOrders();
        }

        internal void RefreshOrders()
        {
            builds = UnityEngine.Object.FindObjectsByType<Constructable>(FindObjectsSortMode.None);
            digs = UnityEngine.Object.FindObjectsByType<Diggable>(FindObjectsSortMode.None);
        }

        internal ContinueSample Read(double wallSeconds, bool refreshFood = false)
        {
            var sample = new ContinueSample
            {
                WorldId = worldId,
                GameSeconds = (GameUtil.GetCurrentCycle() + (GameClock.Instance?.GetCurrentCycleAsPercentage() ?? 0f)) * 600.0,
                Available = GameClock.Instance != null && ClusterManager.Instance != null
            };
            foreach (var dupe in Components.LiveMinionIdentities.Items)
            {
                if (dupe == null) continue;
                sample.Dupes.Add(ReadDupe(dupe, sample));
            }
            if (refreshFood || wallSeconds >= nextFoodRead)
            {
                foodKcal = 0;
                foreach (var edible in Components.Edibles.Items)
                    if (edible != null && (worldId < 0 || edible.GetMyWorldId() == worldId)
                        && ToolUtil.VisibleCellAllowed(Grid.PosToCell(edible), true))
                        foodKcal += ToolUtil.SafeFloat(edible.Calories) / 1000.0;
                nextFoodRead = wallSeconds + 2;
            }
            sample.FoodKcal = foodKcal;
            foreach (var item in builds)
                if (item != null && (worldId < 0 || item.GetMyWorldId() == worldId))
                {
                    sample.PendingBuilds++;
                    sample.PendingIds.Add(item.GetInstanceID());
                    ReadWork(item.GetComponent<Workable>());
                }
            foreach (var item in digs)
                if (item != null && (worldId < 0 || item.GetMyWorldId() == worldId))
                {
                    sample.PendingDigs++;
                    sample.PendingIds.Add(item.GetInstanceID());
                    ReadWork(item.GetComponent<Workable>());
                }
            sample.PrintingReady = Immigration.Instance != null && Immigration.Instance.ImmigrantsAvailable;
            sample.ResearchQueueCount = Research.Instance?.GetResearchQueue()?.Count ?? 0;
            var research = Research.Instance?.GetActiveResearch();
            sample.ResearchId = research?.tech?.Id;
            sample.ResearchProgress = research == null ? 0 : research.GetTotalPercentageComplete() * 100.0;
            ReadInfrastructure(sample, wallSeconds, refreshFood || sample.ResearchId != infrastructureResearchId);
            HudObservation.Read(sample);
            sample.WorkProgress = workProgress;
            return sample;
        }

        private ContinueDupe ReadDupe(MinionIdentity dupe, ContinueSample sample)
        {
            int cell = Grid.PosToCell(dupe);
            var health = dupe.GetComponent<Health>();
            var resume = dupe.GetComponent<MinionResume>();
            var consumer = dupe.GetComponent<ChoreConsumer>();
            var chore = consumer?.choreDriver?.GetCurrentChore();
            string choreType = chore?.choreType?.Id ?? chore?.GetType().Name;
            string schedule = dupe.GetComponent<Schedulable>()?.GetSchedule()?.GetCurrentScheduleBlock()?.GroupId;
            bool worktime = string.Equals(schedule, "Worktime", StringComparison.OrdinalIgnoreCase)
                || string.Equals(schedule, "Work", StringComparison.OrdinalIgnoreCase);
            bool idle = chore == null || (choreType ?? "").IndexOf("Idle", StringComparison.OrdinalIgnoreCase) >= 0;
            double stamina = DupeAmountUtil.AmountValueByName(dupe, "Stamina", -1);
            double calories = DupeAmountUtil.AmountValueByName(dupe, "Calories", -1);
            bool personal = new[] { "Sleep", "Eat", "Toilet", "Recover", "Breathe", "Relax", "Shower", "Narcolepsy" }
                .Any(name => (choreType ?? "").IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
            if (consumer == null || health == null || string.IsNullOrEmpty(schedule)) sample.Available = false;
            var world = ClusterManager.Instance?.GetWorld(dupe.GetMyWorldId());
            if (world?.AlertManager != null && world.AlertManager.IsRedAlert()) sample.RedAlert = true;
            ReadWork(chore?.target as Workable);
            return new ContinueDupe
            {
                Id = dupe.GetComponent<KPrefabID>()?.InstanceID ?? dupe.GetInstanceID(),
                WorldId = dupe.GetMyWorldId(), Cell = cell,
                SkillPoints = resume?.AvailableSkillpoints ?? 0,
                AdvancedResearchSkill = resume != null && resume.MasteryBySkillID.TryGetValue("Researching1", out bool mastered) && mastered,
                Valid = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell),
                Breath = DupeAmountUtil.AmountValueByName(dupe, "Breath", -1),
                Health = health == null ? -1 : health.hitPoints / Math.Max(1f, health.maxHitPoints) * 100.0,
                Calories = calories, Stress = DupeAmountUtil.StressValue(dupe, -1),
                BodyTemperature = DupeAmountUtil.AmountValueByName(dupe, "Temperature", -1),
                Morale = Db.Get().Attributes.QualityOfLife.Lookup(dupe)?.GetTotalValue() ?? -1,
                MoraleExpectation = Db.Get().Attributes.QualityOfLifeExpectation.Lookup(dupe)?.GetTotalValue() ?? -1,
                StressDetails = DupeStressObservation.Read(dupe),
                Chore = choreType,
                Working = worktime && !idle && !personal,
                UnexpectedIdle = worktime && idle && (stamina < 0 || stamina >= 15) && (calories < 0 || calories >= 1000000)
            };
        }

        private void ReadInfrastructure(ContinueSample sample, double wallSeconds, bool refresh)
        {
            if (refresh || wallSeconds >= nextInfrastructureRead)
            {
                beds = toilets = oxygenProducers = 0;
                buildingSupplies.Clear();
                researchStations = ResearchStationObservation.Read(worldId);
                infrastructureResearchId = sample.ResearchId;
                foreach (var building in Components.BuildingCompletes.Items)
                {
                    if (building == null || (worldId >= 0 && building.GetMyWorldId() != worldId)) continue;
                    BuildingSupplyObservation.Read(building.gameObject, false, buildingSupplies);
                    string id = building.Def?.PrefabID ?? "";
                    if (id == "Bed" || id == "LuxuryBed") beds++;
                    if (id == "Outhouse" || id == "FlushToilet") toilets++;
                    if (id == "OxygenDiffuser" || id == "MineralDeoxidizer" || id == "Electrolyzer") oxygenProducers++;
                }
                foreach (var building in builds)
                    if (building != null && (worldId < 0 || building.GetMyWorldId() == worldId))
                        BuildingSupplyObservation.Read(building.gameObject, true, buildingSupplies);
                // Only fresh native reads can confirm persistence; cached samples are not evidence.
                if (timingGame != Game.Instance) { timingGame = Game.Instance; supplyTiming.Clear(); }
                if (!supplyTiming.ContainsKey(worldId)) supplyTiming[worldId] = new SupplyStatusTiming();
                supplyTiming[worldId].Apply(buildingSupplies, sample.GameSeconds);
                nextInfrastructureRead = wallSeconds + 2;
            }
            sample.InfrastructureKnown = true;
            sample.Beds = beds; sample.Toilets = toilets; sample.OxygenProducers = oxygenProducers;
            sample.ResearchStations.AddRange(researchStations);
            sample.BuildingSupplies.AddRange(buildingSupplies);
            sample.AdvancedResearchBuildingId = researchStations.FirstOrDefault(station => station.ResearchType == "advanced" && station.Required)?.Id;
            var local = sample.Dupes.Where(dupe => worldId < 0 || dupe.WorldId == worldId).ToList();
            sample.AdvancedResearchBlocked = sample.AdvancedResearchBuildingId.HasValue && !local.Any(dupe => dupe.AdvancedResearchSkill);
            sample.CanLearnAdvancedResearch = Components.LiveMinionIdentities.Items.Any(dupe => dupe != null
                && (worldId < 0 || dupe.GetMyWorldId() == worldId)
                && dupe.GetComponent<MinionResume>() is MinionResume resume
                && resume.CanMasterSkill(resume.GetSkillMasteryConditions("Researching1")));
            if (sample.AdvancedResearchBlocked)
                sample.Alerts.Remove("Bad: " + ToolUtil.CleanName(global::STRINGS.RESEARCH.MESSAGING.NO_RESEARCHER_SKILL));
        }

        private void ReadWork(Workable workable)
        {
            if (workable == null) return;
            int id = workable.GetInstanceID();
            float current = ToolUtil.SafeFloat(workable.WorkTimeRemaining);
            if (remainingWork.TryGetValue(id, out float previous) && current < previous)
                workProgress += previous - current;
            remainingWork[id] = current;
        }
    }
}
