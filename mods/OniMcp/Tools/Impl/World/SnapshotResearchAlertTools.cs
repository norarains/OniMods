using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    public static partial class SnapshotTools
    {
        private static Dictionary<string, object> BuildResearch()
        {
            if (Research.Instance == null)
                return new Dictionary<string, object> { ["available"] = false };

            var active = Research.Instance.GetActiveResearch();
            var target = Research.Instance.GetTargetResearch();
            var queue = Research.Instance.GetResearchQueue();
            return new Dictionary<string, object>
            {
                ["available"] = true,
                ["active"] = active != null ? TechSummary(active.tech, includeProgress: true) : null,
                ["target"] = target != null ? TechSummary(target.tech, includeProgress: false) : null,
                ["queueCount"] = queue.Count,
                ["queue"] = queue.Take(5).Select(item => TechSummary(item.tech, includeProgress: false)).ToList()
            };
        }

        private static Dictionary<string, object> TechSummary(Tech tech, bool includeProgress)
        {
            if (tech == null)
                return null;
            var instance = Research.Instance?.Get(tech);
            var result = new Dictionary<string, object>
            {
                ["id"] = tech.Id,
                ["name"] = tech.Name,
                ["complete"] = instance?.IsComplete() ?? false
            };
            if (includeProgress && instance != null)
                result["progress"] = Math.Round(instance.GetTotalPercentageComplete() * 100.0, 1);
            return result;
        }

        private static Dictionary<string, object> BuildAtmosphere(int worldId)
        {
            float oxygenKg = 0f;
            float pollutedOxygenKg = 0f;
            int breathableCells = 0;
            int visibleCells = 0;
            for (int cell = 0; cell < Grid.CellCount; cell++)
            {
                if (!Grid.IsWorldValidCell(cell) || (worldId >= 0 && Grid.WorldIdx[cell] != worldId) || !PlayerVisibility.Cell(cell))
                    continue;
                visibleCells++;
                var element = Grid.Element[cell];
                if (element == null)
                    continue;
                if (element.id == SimHashes.Oxygen)
                {
                    oxygenKg += ToolUtil.SafeFloat(Grid.Mass[cell]);
                    breathableCells++;
                }
                else if (element.id == SimHashes.ContaminatedOxygen)
                {
                    pollutedOxygenKg += ToolUtil.SafeFloat(Grid.Mass[cell]);
                    breathableCells++;
                }
            }

            return new Dictionary<string, object>
            {
                ["visibleCells"] = visibleCells,
                ["breathableCells"] = breathableCells,
                ["oxygenKg"] = Math.Round(oxygenKg, 1),
                ["pollutedOxygenKg"] = Math.Round(pollutedOxygenKg, 1)
            };
        }

        // Compatibility metrics are projections of the shared finding policy, not another rule set.
        private static Dictionary<string, object> BuildAlerts(ContinueSample sample)
        {
            var items = ColonyObservation.Findings(sample).Select(item => item.ToDictionary()).ToList();
            return new Dictionary<string, object> { ["count"] = items.Count, ["items"] = items };
        }

        private static string NormalizeProfile(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "standard";
            string profile = value.Trim().ToLowerInvariant();
            if (profile == "minimal" || profile == "brief" || profile == "standard" || profile == "full")
                return profile;
            if (profile == "mini" || profile == "tiny")
                return "minimal";
            return "standard";
        }

        private static int DefaultDupeLimit(string profile)
        {
            if (profile == "minimal") return 0;
            if (profile == "brief") return 0;
            if (profile == "full") return 50;
            return 12;
        }

        private static int DefaultFoodLimit(string profile)
        {
            if (profile == "minimal") return 0;
            if (profile == "brief") return 0;
            if (profile == "full") return 50;
            return 8;
        }
    }
}
