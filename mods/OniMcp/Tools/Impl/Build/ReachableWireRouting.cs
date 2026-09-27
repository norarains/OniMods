using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static List<CellCoord> FindReachablePowerRoute(int source, int input, int worldId, int maxCells)
        {
            var navigators = Components.LiveMinionIdentities.Items
                .Where(dupe => dupe != null && dupe.GetMyWorldId() == worldId)
                .Select(dupe => dupe.GetComponent<Navigator>()).Where(nav => nav != null).ToList();
            var cache = new Dictionary<int, bool>();
            Func<int, bool> allowed = cell => {
                if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId)
                    || !ToolUtil.VisibleCellAllowed(cell, true)) return false;
                if (cache.TryGetValue(cell, out bool known)) return known;
                bool existing = Grid.Objects[cell, (int)ObjectLayer.Wire] != null
                    || Grid.Objects[cell, (int)ObjectLayer.WireTile] != null;
                bool natural = Grid.Solid[cell] && Grid.Objects[cell, (int)ObjectLayer.Building] == null;
                bool reachable = existing || (!natural && OrdersTools.TryFindReachableWorkCell(cell, worldId, navigators, out _));
                cache[cell] = reachable;
                return reachable;
            };
            var route = ReachableWirePath.Find(source, input,
                cell => new[] { Grid.CellAbove(cell), Grid.CellBelow(cell), Grid.CellLeft(cell), Grid.CellRight(cell) },
                allowed, 4096, maxCells);
            return route?.Select(CellCoordFromCell).ToList();
        }
    }
}
