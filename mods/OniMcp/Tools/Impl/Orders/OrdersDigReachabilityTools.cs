using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    public static partial class OrdersTools
{
        internal static bool TryFindReachableWorkCell(int targetCell, int worldId, List<Navigator> navigators, out int workCell)
        {
            workCell = -1;
            if (navigators == null || navigators.Count == 0 || !Grid.IsValidCell(targetCell))
                return false;

            foreach (int candidate in TargetAndAdjacentCells(targetCell))
            {
                if (!Grid.IsValidCell(candidate) || !PlayerVisibility.Cell(candidate) || !ToolUtil.CellMatchesWorld(candidate, worldId))
                    continue;
                if (Grid.Solid[candidate] || Grid.Foundation[candidate])
                    continue;

                foreach (var navigator in navigators)
                {
                    if (SafeCanReach(navigator, candidate))
                    {
                        workCell = candidate;
                        return true;
                    }
                }
            }

            return false;
        }

        private static IEnumerable<int> TargetAndAdjacentCells(int cell)
        {
            yield return cell;
            foreach (int adjacent in AdjacentDigWorkCells(cell))
                yield return adjacent;
        }

        private static Dictionary<string, object> ReachabilitySample(int targetCell, int workCell, string status)
        {
            var sample = CellResult(targetCell, status);
            if (Grid.IsValidCell(workCell))
            {
                sample["workCell"] = new Dictionary<string, object>
                {
                    ["cell"] = workCell,
                    ["x"] = Grid.CellColumn(workCell),
                    ["y"] = Grid.CellRow(workCell)
                };
            }
            return sample;
        }

        private static List<Navigator> ActiveNavigators(int worldId)
        {
            var navigators = new List<Navigator>();
            foreach (var dupe in Components.LiveMinionIdentities.Items)
            {
                if (dupe == null || (worldId >= 0 && dupe.GetMyWorldId() != worldId))
                    continue;
                var navigator = dupe.GetComponent<Navigator>();
                if (navigator != null)
                    navigators.Add(navigator);
            }
            return navigators;
        }

        private static bool TryFindReachableDigWorkCell(int targetCell, int worldId, List<Navigator> navigators, out int workCell)
        {
            workCell = -1;
            if (navigators == null || navigators.Count == 0 || !Grid.IsValidCell(targetCell))
                return false;

            foreach (int candidate in AdjacentDigWorkCells(targetCell))
            {
                if (!Grid.IsValidCell(candidate) || !PlayerVisibility.Cell(candidate) || !ToolUtil.CellMatchesWorld(candidate, worldId))
                    continue;
                if (Grid.Solid[candidate] || Grid.Foundation[candidate])
                    continue;

                foreach (var navigator in navigators)
                {
                    if (SafeCanReach(navigator, candidate))
                    {
                        workCell = candidate;
                        return true;
                    }
                }
            }

            return false;
        }

        private static IEnumerable<int> AdjacentDigWorkCells(int cell)
        {
            int x = Grid.CellColumn(cell);
            int y = Grid.CellRow(cell);
            for (int yy = y - 1; yy <= y + 1; yy++)
            {
                for (int xx = x - 1; xx <= x + 1; xx++)
                {
                    if (xx == x && yy == y)
                        continue;
                    yield return Grid.XYToCell(xx, yy);
                }
            }
        }

        private static bool SafeCanReach(Navigator navigator, int cell)
        {
            try
            {
                return navigator != null && Grid.IsValidCell(cell) && navigator.CanReach(cell);
            }
            catch
            {
                return false;
            }
        }

        private static Dictionary<string, object> DigReachabilitySample(int targetCell, int workCell, string status)
        {
            var sample = DigTarget(targetCell, Grid.CellColumn(targetCell), Grid.CellRow(targetCell), status);
            if (Grid.IsValidCell(workCell))
            {
                sample["workCell"] = new Dictionary<string, object>
                {
                    ["cell"] = workCell,
                    ["x"] = Grid.CellColumn(workCell),
                    ["y"] = Grid.CellRow(workCell)
                };
            }
            return sample;
        }

        private static void IncrementSkip(Dictionary<string, int> skipped, string reason)
        {
            int count;
            skipped[reason] = skipped.TryGetValue(reason, out count) ? count + 1 : 1;
        }

    }
}
