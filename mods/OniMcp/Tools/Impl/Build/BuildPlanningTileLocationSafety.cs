using System.Collections.Generic;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        // BuildingDef.IsValidTileLocation checks other layers even though they
        // otherwise overlap a building footprint. Keep the native location rule,
        // not a wire-name or isSolidTile heuristic: mesh tiles and joint plates
        // also use this check, while ordinary passable buildings do not.
        private static List<Dictionary<string, object>> FindTileLocationConflicts(
            BuildingDef def, PlacementDetails placement, GameObject ignored)
        {
            var conflicts = new List<Dictionary<string, object>>();
            if (def == null || placement == null
                || (def.BuildLocationRule != BuildLocationRule.Tile
                    && def.BuildLocationRule != BuildLocationRule.HighWattBridgeTile))
                return conflicts;

            bool replacement = NativeTileReplacement(def, placement) != null;
            // Native Tile checks every occupied cell; HighWattBridgeTile checks
            // its origin only before validating the two external link endpoints.
            int jointOrigin = def.BuildLocationRule == BuildLocationRule.HighWattBridgeTile
                ? PlacementOriginCell(def, placement.AnchorX, placement.AnchorY, placement.Orientation)
                : Grid.InvalidCell;
            foreach (var part in placement.Footprint)
            {
                if (!part.Valid || !part.Visible || !part.InWorld
                    || (def.BuildLocationRule == BuildLocationRule.HighWattBridgeTile && part.Cell != jointOrigin))
                    continue;
                AddTileLocationConflict(conflicts, def, part.Cell, ObjectLayer.Wire,
                    BuildLocationRule.NotInTiles, ignored, "wire_obstruction",
                    "Native tile placement cannot overlap a wire that cannot pass through tiles.");
                AddTileLocationConflict(conflicts, def, part.Cell, ObjectLayer.WireConnectors,
                    BuildLocationRule.HighWattBridgeTile, ignored, "wire_obstruction",
                    "Native tile placement cannot cover a Heavi-Watt joint plate endpoint.");
                if (!replacement)
                    AddTileLocationConflict(conflicts, def, part.Cell, ObjectLayer.Backwall,
                        BuildLocationRule.NotInTiles, ignored, "backwall_obstruction",
                        "Native tile placement cannot overlap this backwall's NotInTiles location rule.");
            }
            return conflicts;
        }

        private static void AddTileLocationConflict(List<Dictionary<string, object>> conflicts,
            BuildingDef def, int cell, ObjectLayer layer, BuildLocationRule blockedRule,
            GameObject ignored, string reasonCode, string reason)
        {
            var existing = Grid.Objects[cell, (int)layer];
            if (existing == null || existing == ignored
                || existing.GetComponent<Building>()?.Def?.BuildLocationRule != blockedRule)
                return;
            bool known = PlayerVisibility.Object(existing);
            conflicts.Add(new Dictionary<string, object>
            {
                ["kind"] = "tile_location_conflict",
                ["reasonCode"] = reasonCode,
                ["reason"] = reason,
                ["expectedPrefabId"] = def.PrefabID,
                ["actualPrefabId"] = known ? PlacementObjectPrefabId(existing) : null,
                ["existingId"] = known ? existing.GetComponent<KPrefabID>()?.InstanceID : null,
                ["layer"] = layer.ToString(),
                ["x"] = Grid.CellColumn(cell),
                ["y"] = Grid.CellRow(cell),
                ["cell"] = cell,
                ["nativeLocationCheck"] = "BuildingDef.IsValidTileLocation"
            });
        }
    }
}
