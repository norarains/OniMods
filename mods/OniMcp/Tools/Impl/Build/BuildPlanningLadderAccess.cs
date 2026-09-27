using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static void AddLadderAccessDependencies(BuildingDef def, List<Dictionary<string, object>> previews, int worldId)
        {
            // Only a vertical ordinary ladder chain with a current-access seed.
            // Other buildings/terrain/pathfinding remain subject to current access.
            if (def.PrefabID != "Ladder") return;
            var candidates = previews.Where(row => GetBool(row, "valid") && row.ContainsKey("workAccess"))
                .ToDictionary(row => Grid.XYToCell((int)row["x"], (int)row["y"]));
            var seeds = candidates.Where(pair => GetBool((Dictionary<string, object>)pair.Value["workAccess"], "hasCurrentConstructionAccess"))
                .Select(pair => pair.Key).ToArray();
            var dependencies = PlannedAccessChain.Resolve(candidates.Keys, seeds, (cell, prior, ready) =>
            {
                if (Grid.CellColumn(cell) != Grid.CellColumn(prior) || System.Math.Abs(Grid.CellRow(cell) - Grid.CellRow(prior)) != 1
                    || Grid.Solid[cell]) return false;
                int head = Grid.OffsetCell(prior, new CellOffset(0, 1));
                if (!LadderClearCell(prior, worldId, ready) || !LadderClearCell(head, worldId, ready)) return false;
                var rows = ConstructionTable(def, Orientation.Neutral).Select(row => row.Select(offset => Grid.OffsetCell(cell, offset)).ToArray());
                return ConstructionWorkCells.FindReachable(rows, clear => LadderClearCell(clear, worldId, ready), work => work == prior) >= 0;
            });
            foreach (var pair in dependencies)
            {
                var preview = candidates[pair.Key];
                preview["actionable"] = true;
                var access = (Dictionary<string, object>)preview["workAccess"];
                access["plannedAccessDependency"] = new {
                    source = "vertical_ladder_batch", x = Grid.CellColumn(pair.Value), y = Grid.CellRow(pair.Value),
                    condition = "predecessor_ladder_completed", completionReachabilityVerified = false
                };
                // hasCurrentConstructionAccess deliberately remains false.
            }
        }

        private static bool LadderClearCell(int cell, int worldId, ISet<int> ready)
            => Grid.IsValidCell(cell) && Grid.IsVisible(cell) && ToolUtil.CellMatchesWorld(cell, worldId)
                && (!Grid.Solid[cell] || ready.Contains(cell));
    }
}
