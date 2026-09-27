using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static List<Dictionary<string, object>> NativeConstructionDigs(BuildingDef def, PlacementDetails placement)
        {
            if (NativeTileReplacement(def, placement) != null) return new List<Dictionary<string, object>>();
            return placement.Footprint.Where(cell => cell.Valid && cell.Visible && cell.InWorld
                && (Diggable.IsDiggable(cell.Cell) || def.ObjectLayer == ObjectLayer.Backwall && BackwallManager.HasBackwall(cell.Cell)))
                .Select(cell => new Dictionary<string, object> {
                    ["x"] = cell.X, ["y"] = cell.Y,
                    ["reason"] = Grid.Solid[cell.Cell] ? "natural_solid" : "unstable_above_or_backwall",
                    ["alreadyMarked"] = Grid.Objects[cell.Cell, (int)ObjectLayer.DigPlacer] != null
                }).ToList();
        }
    }
}
