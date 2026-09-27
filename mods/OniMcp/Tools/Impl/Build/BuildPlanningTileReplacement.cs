using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        // Ordinary one-cell floor replacement only. Native eligibility must pass before any
        // obstruction exception; utility endpoints, doors and other buildings keep strict checks.
        private static GameObject NativeTileReplacement(BuildingDef def, PlacementDetails placement)
        {
            if (def == null || placement == null || placement.Footprint.Count != 1
                || def.TileLayer != ObjectLayer.FoundationTile || def.ReplacementLayer == ObjectLayer.NumLayers)
                return null;
            var cell = placement.Footprint[0];
            if (!cell.Valid || !cell.Visible || !cell.InWorld || def.IsReplacementLayerOccupied(cell.Cell)) return null;
            var target = def.GetReplacementCandidate(cell.Cell);
            var building = target?.GetComponent<BuildingComplete>();
            if (building?.Def == null || building.Def.WidthInCells != 1 || building.Def.HeightInCells != 1
                || building.Def.TileLayer != ObjectLayer.FoundationTile || !def.CanReplace(target)
                || Grid.Objects[cell.Cell, (int)ObjectLayer.Building] != target
                || !def.IsValidPlaceLocation(null, cell.Cell, placement.Orientation, true, out string _)) return null;
            return target;
        }

        private static List<Dictionary<string, object>> RemoveReplacedTileObstructions(
            List<Dictionary<string, object>> obstructions, BuildingDef def, PlacementDetails placement)
        {
            var target = NativeTileReplacement(def, placement);
            if (target == null) return obstructions;
            string prefab = target.GetComponent<BuildingComplete>().Def.PrefabID;
            return obstructions.Where(item => {
                string kind = item["kind"].ToString();
                if (kind == "solid_cell") return false;
                if (kind == "building_layer_conflict" && item["actualPrefabId"].ToString() == prefab) return false;
                if (kind == "building" && item["id"].ToString() == prefab) return false;
                return true;
            }).ToList();
        }

        private static Dictionary<string, object> TileReplacementInfo(GameObject target)
        {
            return target == null ? null : new Dictionary<string, object> {
                ["targetId"] = target.GetComponent<KPrefabID>()?.InstanceID ?? target.GetInstanceID(),
                ["prefabId"] = target.GetComponent<BuildingComplete>().Def.PrefabID,
                ["mode"] = "native_replacement_blueprint", ["existingTileRetainedUntilBuilt"] = true
            };
        }
    }
}
