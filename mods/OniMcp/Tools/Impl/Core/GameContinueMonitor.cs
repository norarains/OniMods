using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    // Cheap per-dupe samples every half second. Food is cached for two seconds;
    // pending order objects are found after each planning reset, never by scanning Grid.
    internal sealed class GameContinueMonitor
    {
        private readonly int worldId;
        private readonly Constructable[] builds;
        private readonly Diggable[] digs;
        private readonly Dictionary<int, float> remainingWork = new Dictionary<int, float>();
        private double nextFoodRead, foodKcal, workProgress;

        internal GameContinueMonitor(int worldId)
        {
            this.worldId = worldId;
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
                    if (edible != null && edible.GetMyWorldId() == worldId
                        && ToolUtil.VisibleCellAllowed(Grid.PosToCell(edible), true))
                        foodKcal += ToolUtil.SafeFloat(edible.Calories) / 1000.0;
                nextFoodRead = wallSeconds + 2;
            }
            sample.FoodKcal = foodKcal;
            foreach (var notification in NotificationTools.GetNotifications(includePending: false))
            {
                string type = notification.Type.ToString();
                if (notification.Type == NotificationType.Bad || notification.Type == NotificationType.DuplicantThreatening)
                    sample.Alerts.Add(type + ": " + ToolUtil.CleanName(notification.titleText));
            }
            foreach (var item in builds)
                if (item != null && item.GetMyWorldId() == worldId)
                {
                    sample.PendingBuilds++;
                    sample.PendingIds.Add(item.GetInstanceID());
                    ReadWork(item.GetComponent<Workable>());
                }
            foreach (var item in digs)
                if (item != null && item.GetMyWorldId() == worldId)
                {
                    sample.PendingDigs++;
                    sample.PendingIds.Add(item.GetInstanceID());
                    ReadWork(item.GetComponent<Workable>());
                }
            var research = Research.Instance?.GetActiveResearch();
            sample.ResearchId = research?.tech?.Id;
            sample.ResearchProgress = research == null ? 0 : research.GetTotalPercentageComplete() * 100.0;
            sample.WorkProgress = workProgress;
            return sample;
        }

        private ContinueDupe ReadDupe(MinionIdentity dupe, ContinueSample sample)
        {
            int cell = Grid.PosToCell(dupe);
            var health = dupe.GetComponent<Health>();
            var consumer = dupe.GetComponent<ChoreConsumer>();
            var chore = consumer?.choreDriver?.GetCurrentChore();
            string choreType = chore?.choreType?.Id ?? chore?.GetType().Name;
            string schedule = dupe.GetComponent<Schedulable>()?.GetSchedule()?.GetCurrentScheduleBlock()?.GroupId;
            bool worktime = string.Equals(schedule, "Worktime", StringComparison.OrdinalIgnoreCase)
                || string.Equals(schedule, "Work", StringComparison.OrdinalIgnoreCase);
            bool idle = chore == null || (choreType ?? "").IndexOf("Idle", StringComparison.OrdinalIgnoreCase) >= 0;
            double stamina = DupeAmountUtil.AmountValueByName(dupe, "Stamina", -1);
            double calories = DupeAmountUtil.AmountValueByName(dupe, "Calories", -1);
            bool personal = new[] { "Sleep", "Eat", "Toilet", "Recover", "Breathe", "Relax", "Shower" }
                .Any(name => (choreType ?? "").IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
            if (consumer == null || health == null || string.IsNullOrEmpty(schedule)) sample.Available = false;
            var world = ClusterManager.Instance?.GetWorld(dupe.GetMyWorldId());
            if (world?.AlertManager != null && world.AlertManager.IsRedAlert()) sample.RedAlert = true;
            ReadWork(chore?.target as Workable);
            return new ContinueDupe
            {
                Id = dupe.GetComponent<KPrefabID>()?.InstanceID ?? dupe.GetInstanceID(),
                WorldId = dupe.GetMyWorldId(), Cell = cell,
                Valid = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell),
                Breath = DupeAmountUtil.AmountValueByName(dupe, "Breath", -1),
                Health = health == null ? -1 : health.hitPoints / Math.Max(1f, health.maxHitPoints) * 100.0,
                Calories = calories, Stress = DupeAmountUtil.StressValue(dupe, -1),
                BodyTemperature = DupeAmountUtil.AmountValueByName(dupe, "Temperature", -1),
                Chore = choreType,
                Working = worktime && !idle && !personal,
                UnexpectedIdle = worktime && idle && (stamina < 0 || stamina >= 15) && (calories < 0 || calories >= 1000000)
            };
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
