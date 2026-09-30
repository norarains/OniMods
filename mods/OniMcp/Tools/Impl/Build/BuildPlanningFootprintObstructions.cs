using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static List<Dictionary<string, object>> FindFootprintObstructions(
            PlacementDetails placement, GameObject ignored = null)
        {
            var result = new List<Dictionary<string, object>>();
            var placementDef = ResolveBuildingDefForPlacement(placement);
            var safetyFootprint = PlacementSafetyFootprint(placementDef, placement).ToList();
            var footprintCells = new HashSet<int>(safetyFootprint.Where(cell => cell.Valid && cell.Visible).Select(cell => cell.Cell));
            bool utility = IsUtilityPrefab(placement.PrefabId);
            bool endpointBridge = UsesNativeBridgeEndpointRegistration(placementDef);
            bool physical = placementDef?.ObjectLayer == ObjectLayer.Building && !endpointBridge && !IsLinearUtilityPrefab(placement.PrefabId);

            foreach (var cellInfo in safetyFootprint)
            {
                if (!cellInfo.Valid || !cellInfo.Visible)
                    continue;
                if (Grid.Solid[cellInfo.Cell] && (!utility || physical || IsNaturalDiggableSolidCell(cellInfo.Cell, placement.WorldId)))
                {
                    bool diggable = IsNaturalDiggableSolidCell(cellInfo.Cell, placement.WorldId);
                    bool alreadyMarked = Grid.Objects[cellInfo.Cell, (int)ObjectLayer.DigPlacer] != null;
                    result.Add(new Dictionary<string, object>
                    {
                        ["kind"] = "solid_cell",
                        ["x"] = cellInfo.X,
                        ["y"] = cellInfo.Y,
                        ["cell"] = cellInfo.Cell,
                        ["diggable"] = diggable,
                        ["alreadyMarkedForDig"] = alreadyMarked,
                        ["reasonCode"] = "solid_cell",
                        ["reason"] = diggable
                            ? "target footprint contains natural solid terrain that can be marked for digging"
                            : "target footprint contains solid terrain or constructed tile that cannot be auto-dug"
                    });
                }

                if (physical)
                foreach (var uproot in UprootableObstructionsAtCell(cellInfo.Cell, placement.WorldId))
                {
                    uproot["x"] = cellInfo.X;
                    uproot["y"] = cellInfo.Y;
                    uproot["cell"] = cellInfo.Cell;
                    result.Add(uproot);
                }
            }

            foreach (var conflict in FindUtilityLayerConflicts(placementDef, placement, ignored))
                result.Add(conflict);
            foreach (var conflict in FindBuildingLayerConflicts(placementDef, placement))
                result.Add(conflict);
            foreach (var conflict in FindLogicEndpointConflicts(placementDef, placement))
                result.Add(conflict);

            result.AddRange(FindTileLocationConflicts(placementDef, placement, ignored));

            var seen = new HashSet<string>();
            foreach (var obstruction in ExistingBuildingFootprintObstructions(placement.WorldId, footprintCells))
            {
                string id = obstruction.ContainsKey("id") ? obstruction["id"]?.ToString() : "";
                if ((utility && !physical) || endpointBridge || IsUtilityPrefab(id))
                    continue;
                string key = obstruction["kind"] + "|" + id + "|" + obstruction["objectX"] + "|" + obstruction["objectY"] + "|" + obstruction["x"] + "|" + obstruction["y"];
                if (seen.Add(key))
                    result.Add(obstruction);
            }

            return RemoveReplacedTileObstructions(result, placementDef, placement);
        }

    }
}
