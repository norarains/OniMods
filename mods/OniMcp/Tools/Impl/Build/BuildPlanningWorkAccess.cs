using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static Dictionary<string, object> CurrentWorkAccess(PlacementDetails placement)
        {
            var navigators = Components.LiveMinionIdentities.Items
                .Where(dupe => dupe != null && dupe.GetMyWorldId() == placement.WorldId)
                .Select(dupe => dupe.GetComponent<Navigator>()).Where(nav => nav != null).ToList();
            var blocked = new List<Dictionary<string, object>>();
            int reachable = 0, digTotal = 0, digReachable = 0;
            foreach (var cell in placement.Footprint)
            {
                bool dig = IsNaturalDiggableSolidCell(cell.Cell, placement.WorldId);
                if (dig) digTotal++;
                if (OrdersTools.TryFindReachableWorkCell(cell.Cell, placement.WorldId, navigators, out int _))
                { reachable++; if (dig) digReachable++; }
                else blocked.Add(new Dictionary<string, object> { ["x"] = cell.X, ["y"] = cell.Y, ["autoDig"] = dig });
            }
            return new Dictionary<string, object> {
                ["source"] = "current_navigator_adjacent_cells", ["reachableFootprintCells"] = reachable,
                ["footprintCells"] = placement.Footprint.Count, ["allCellsHaveCurrentAccess"] = blocked.Count == 0,
                ["autoDigCells"] = digTotal, ["reachableAutoDigCells"] = digReachable,
                ["blockedCells"] = blocked.Take(16).ToList(), ["blockedCellCount"] = blocked.Count,
                ["completionReachabilityVerified"] = false,
                ["notChecked"] = new[] { "future_blueprint_access", "skill_and_delivery_chore_eligibility" }
            };
        }
    }
}
