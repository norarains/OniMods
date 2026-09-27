using System.Collections.Generic;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private sealed class BridgeEndpointTarget
        {
            public int Cell;
            public ObjectLayer Layer;
            public string Role;
        }

        private static BridgeEndpointTarget BridgeEndpointTargetForOffset(
            int anchorCell, Orientation orientation, CellOffset offset, ObjectLayer layer, string role)
        {
            var rotated = Rotatable.GetRotatedCellOffset(offset, orientation);
            int cell = Grid.OffsetCell(anchorCell, rotated);
            bool valid = Grid.IsValidCell(cell)
                && Grid.CellColumn(cell) == Grid.CellColumn(anchorCell) + rotated.x
                && Grid.CellRow(cell) == Grid.CellRow(anchorCell) + rotated.y;
            return new BridgeEndpointTarget
            {
                Cell = valid ? cell : -1,
                Layer = layer,
                Role = role
            };
        }

        // BuildingDef.TryPlace(null, ...) deliberately bypasses the native ghost
        // object's port checks. Check connector ownership without creating a ghost,
        // touching BuildTool, or registering anything in Grid.Objects.
        private static List<Dictionary<string, object>> FindBuildingPortConflicts(
            BuildingDef def, PlacementDetails placement, GameObject ignored)
        {
            var conflicts = new List<Dictionary<string, object>>();
            if (def == null) return conflicts;
            foreach (var target in BuildingPortTargets(def, placement))
            {
                bool valid = Grid.IsValidCell(target.Cell) && PlayerVisibility.Cell(target.Cell)
                    && ToolUtil.CellMatchesWorld(target.Cell, placement.WorldId);
                var existing = valid ? Grid.Objects[target.Cell, (int)target.Layer] : null;
                if (valid && (existing == null || existing == ignored)) continue;
                conflicts.Add(new Dictionary<string, object> {
                    ["kind"] = "port_conflict",
                    ["reasonCode"] = valid ? "port_overlap" : "port_cell_invalid",
                    ["role"] = target.Role, ["layer"] = target.Layer.ToString(),
                    ["cell"] = target.Cell,
                    ["x"] = Grid.IsValidCell(target.Cell) ? Grid.CellColumn(target.Cell) : -1,
                    ["y"] = Grid.IsValidCell(target.Cell) ? Grid.CellRow(target.Cell) : -1,
                    ["existingId"] = PlayerVisibility.Object(existing) ? existing.GetComponent<KPrefabID>()?.InstanceID : null,
                    ["existingPrefabId"] = PlayerVisibility.Object(existing) ? PlacementObjectPrefabId(existing) : null
                });
            }
            return conflicts;
        }

        private static IEnumerable<BridgeEndpointTarget> BuildingPortTargets(BuildingDef def, PlacementDetails placement)
        {
            int origin = PlacementOriginCell(def, placement.AnchorX, placement.AnchorY, placement.Orientation);
            if (def.InputConduitType != ConduitType.None)
                yield return BridgeEndpointTargetForOffset(origin, placement.Orientation, def.UtilityInputOffset,
                    Grid.GetObjectLayerForConduitType(def.InputConduitType), "input");
            if (def.OutputConduitType != ConduitType.None)
                yield return BridgeEndpointTargetForOffset(origin, placement.Orientation, def.UtilityOutputOffset,
                    Grid.GetObjectLayerForConduitType(def.OutputConduitType), "output");
            if (def.RequiresPowerInput)
                yield return BridgeEndpointTargetForOffset(origin, placement.Orientation, def.PowerInputOffset,
                    ObjectLayer.WireConnectors, "power_input");
            if (def.RequiresPowerOutput)
                yield return BridgeEndpointTargetForOffset(origin, placement.Orientation, def.PowerOutputOffset,
                    ObjectLayer.WireConnectors, "power_output");
            if (def.BuildingComplete == null) yield break;
            // Joint plates register wire connectors at the link endpoints even
            // though RequiresPowerInput/Output are both false. Keep their physical
            // tile footprint rules separate from ordinary bridge placement.
            if (def.BuildLocationRule == BuildLocationRule.HighWattBridgeTile)
            {
                var link = def.BuildingComplete.GetComponent<UtilityNetworkLink>();
                if (link == null)
                    yield return new BridgeEndpointTarget { Cell = -1, Layer = ObjectLayer.WireConnectors, Role = "link_metadata_missing" };
                else
                {
                    link.GetCells(origin, placement.Orientation, out int first, out int second);
                    yield return new BridgeEndpointTarget { Cell = first, Layer = ObjectLayer.WireConnectors, Role = "link1" };
                    yield return new BridgeEndpointTarget { Cell = second, Layer = ObjectLayer.WireConnectors, Role = "link2" };
                }
            }
            foreach (var secondary in def.BuildingComplete.GetComponents<ISecondaryInput>())
                foreach (var type in new[] { ConduitType.Liquid, ConduitType.Gas, ConduitType.Solid })
                    if (secondary.HasSecondaryConduitType(type))
                        yield return BridgeEndpointTargetForOffset(origin, placement.Orientation,
                            secondary.GetSecondaryConduitOffset(type), Grid.GetObjectLayerForConduitType(type), "secondary_input");
            foreach (var secondary in def.BuildingComplete.GetComponents<ISecondaryOutput>())
                foreach (var type in new[] { ConduitType.Liquid, ConduitType.Gas, ConduitType.Solid })
                    if (secondary.HasSecondaryConduitType(type))
                        yield return BridgeEndpointTargetForOffset(origin, placement.Orientation,
                            secondary.GetSecondaryConduitOffset(type), Grid.GetObjectLayerForConduitType(type), "secondary_output");
        }
    }
}
