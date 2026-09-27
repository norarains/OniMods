using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static Dictionary<string, object> PumpIntakeInfo(BuildingDef def, int? origin = null)
        {
            if (def?.PrefabID != "LiquidPumpingStation") return null;
            var result = new Dictionary<string, object> {
                ["width"] = 2, ["maxDepthBelowOrigin"] = LiquidPumpingStationConfig.TAIL_LENGTH,
                ["bodySize"] = new[] { def.WidthInCells, def.HeightInCells },
                ["occupancy"] = "completed_pump_extends_building_layer_down_to_first_solid_or_building",
                ["excavationIncluded"] = false
            };
            if (!origin.HasValue) return result;
            int cell = origin.Value;
            if (!Grid.IsValidCell(cell)) { result["rangeVisible"] = false; return result; }
            bool visible = Enumerable.Range(1, LiquidPumpingStationConfig.TAIL_LENGTH)
                .All(depth => Enumerable.Range(0, 2).All(x => {
                    int target = Grid.OffsetCell(cell, x, -depth);
                    return Grid.IsValidCell(target) && ToolUtil.VisibleCellAllowed(target, true)
                        && Grid.WorldIdx[target] == Grid.WorldIdx[cell];
                }));
            result["rangeVisible"] = visible;
            if (visible)
            {
                int depth = PumpingStationGuide.GetDepthAvailable(cell, null);
                result["availableDepth"] = depth;
                result["intakeBounds"] = depth == 0 ? null : new[] {
                    Grid.CellColumn(cell), Grid.CellRow(cell) - depth, Grid.CellColumn(cell) + 1, Grid.CellRow(cell) - 1 };
            }
            return result;
        }
    }
}
