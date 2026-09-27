using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        // One-cell floor or linear utility replacement only, gated by native replacement
        // tags/layers and location checks. Bridges and endpoints keep strict checks.
        private static GameObject NativeTileReplacement(BuildingDef def, PlacementDetails placement)
        {
            if (def == null || placement == null || placement.Footprint.Count != 1
                || (def.TileLayer != ObjectLayer.FoundationTile && !IsLinearUtilityPrefab(def.PrefabID))
                || def.ReplacementLayer == ObjectLayer.NumLayers)
                return null;
            var cell = placement.Footprint[0];
            if (!cell.Valid || !cell.Visible || !cell.InWorld || def.IsReplacementLayerOccupied(cell.Cell)) return null;
            var target = def.GetReplacementCandidate(cell.Cell);
            var building = target?.GetComponent<BuildingComplete>();
            if (building?.Def == null || building.Def.WidthInCells != 1 || building.Def.HeightInCells != 1
                || !def.CanReplace(target)
                || (IsLinearUtilityPrefab(def.PrefabID)
                    ? !UtilityPrefabPolicy.SameFamily(def.PrefabID, building.Def.PrefabID)
                    : building.Def.TileLayer != ObjectLayer.FoundationTile || Grid.Objects[cell.Cell, (int)ObjectLayer.Building] != target)
                || !def.IsValidPlaceLocation(null, cell.Cell, placement.Orientation, true, out string _)) return null;
            return target;
        }

        private static bool HasMatchingUtilityReplacement(BuildingDef def, PlacementDetails placement, GameObject existing)
        {
            if (!IsLinearUtilityPrefab(def?.PrefabID) || placement.Footprint.Count != 1
                || def.ReplacementLayer == ObjectLayer.NumLayers || !def.CanReplace(existing)) return false;
            var replacement = Grid.Objects[placement.Footprint[0].Cell, (int)def.ReplacementLayer];
            return replacement?.GetComponent<Constructable>() != null
                && replacement.GetComponent<Building>()?.Def?.PrefabID == def.PrefabID
                && UtilityPrefabPolicy.SameFamily(def.PrefabID, existing.GetComponent<Building>()?.Def?.PrefabID);
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
