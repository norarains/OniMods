using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static readonly Dictionary<BuildingDef, Dictionary<Orientation, CellOffset[][]>> ConstructionTables
            = new Dictionary<BuildingDef, Dictionary<Orientation, CellOffset[][]>>();

        private static Dictionary<string, object> CurrentWorkAccess(BuildingDef def, PlacementDetails placement)
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
            int origin = PlacementOriginCell(def, placement.AnchorX, placement.AnchorY, placement.Orientation);
            int workCell = navigators.Count == 0 || !Grid.IsValidCell(origin)
                || placement.Footprint.Any(cell => !cell.Valid || !cell.InWorld) ? -1 : ConstructionWorkCells.FindReachable(
                ConstructionTable(def, placement.Orientation).Select(row => row.Select(offset =>
                {
                    int cell = Grid.OffsetCell(origin, offset);
                    return Grid.IsValidCell(cell) && Grid.CellColumn(cell) == Grid.CellColumn(origin) + offset.x
                        && Grid.CellRow(cell) == Grid.CellRow(origin) + offset.y ? cell : -1;
                }).ToArray()),
                cell => Grid.IsValidCell(cell) && Grid.IsVisible(cell)
                    && ToolUtil.CellMatchesWorld(cell, placement.WorldId) && !Grid.Solid[cell],
                cell => Grid.IsValidCell(cell) && Grid.IsVisible(cell)
                    && ToolUtil.CellMatchesWorld(cell, placement.WorldId) && HasConstructionNavigator(cell, navigators));
            return new Dictionary<string, object> {
                ["source"] = "native_construction_offset_table",
                ["hasCurrentConstructionAccess"] = workCell >= 0,
                ["constructionWorkCell"] = workCell >= 0 ? (object)new {
                    x = Grid.CellColumn(workCell), y = Grid.CellRow(workCell) } : null,
                ["footprintAccessSource"] = "current_navigator_adjacent_cells",
                ["reachableFootprintCells"] = reachable,
                ["footprintCells"] = placement.Footprint.Count, ["allCellsHaveCurrentAccess"] = blocked.Count == 0,
                ["autoDigCells"] = digTotal, ["reachableAutoDigCells"] = digReachable,
                ["blockedCells"] = blocked.Take(16).ToList(), ["blockedCellCount"] = blocked.Count,
                ["completionReachabilityVerified"] = false,
                ["notChecked"] = new[] { "future_blueprint_access", "skill_and_delivery_chore_eligibility" }
            };
        }

        private static CellOffset[][] ConstructionTable(BuildingDef def, Orientation orientation)
        {
            if (!ConstructionTables.TryGetValue(def, out var orientations))
                ConstructionTables[def] = orientations = new Dictionary<Orientation, CellOffset[][]>();
            if (!orientations.TryGetValue(orientation, out var table))
            {
                // Cache each rotation once: the native cache keys offset arrays by
                // identity, so allocating them for every preview would leak entries.
                var offsets = orientation == Orientation.Neutral ? def.PlacementOffsets
                    : def.PlacementOffsets.Select(offset => Rotatable.GetRotatedCellOffset(offset, orientation)).ToArray();
                table = OffsetGroups.BuildReachabilityTable(offsets,
                    def.IsTilePiece ? OffsetGroups.InvertedStandardTableWithCorners : OffsetGroups.InvertedStandardTable,
                    def.ConstructionOffsetFilter);
                orientations[orientation] = table;
            }
            return table;
        }

        private static bool HasConstructionNavigator(int cell, List<Navigator> navigators)
        {
            var grid = Pathfinding.Instance?.GetNavGrid("MinionNavGrid");
            if (grid == null || !grid.ValidNavTypes.Any(type => type != NavType.Tube && grid.NavTable.IsValid(cell, type)))
                return false;
            foreach (var navigator in navigators)
            {
                try { if (navigator.CanReach(cell)) return true; }
                catch { /* A refreshing navigator is not proof of access. */ }
            }
            return false;
        }
    }
}
