using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static BuildingFootprintLayout PlacementLayout(BuildingDef def, Orientation orientation)
        {
            return new BuildingFootprintLayout((def.PlacementOffsets ?? new CellOffset[0]).Select(offset =>
            {
                var rotated = Rotatable.GetRotatedCellOffset(offset, orientation);
                return Tuple.Create(rotated.x, rotated.y);
            }));
        }

        private static int PlacementOriginCell(BuildingDef def, int x, int y, Orientation orientation)
        {
            var layout = PlacementLayout(def, orientation);
            return Grid.XYToCell(layout.OriginX(x), layout.OriginY(y));
        }

        private static Vector3 BuildPlacementPosition(int cell, BuildingDef def, Orientation orientation = Orientation.Neutral)
        {
            int origin = PlacementOriginCell(def, Grid.CellColumn(cell), Grid.CellRow(cell), orientation);
            return Grid.CellToPosCBC(origin, def.SceneLayer);
        }

        internal static Dictionary<string, object> BuildDefPlacementToDictionary(BuildingDef def)
        {
            int width = Math.Max(1, def.WidthInCells);
            int height = Math.Max(1, def.HeightInCells);
            return new Dictionary<string, object>
            {
                ["anchor"] = "lowerLeftCell",
                ["anchorDescription"] = "building_control planning treats each anchor as the lower-left footprint cell, not the visual center",
                ["width"] = width,
                ["height"] = height,
                ["footprintCells"] = width * height,
                ["intake"] = PumpIntakeInfo(def),
                ["singleCellDragSafe"] = width == 1 && height == 1,
                ["dragGuidance"] = width == 1 && height == 1
                    ? "Use world_editor map SEARCH/REPLACE tokens with :priority for repeated tiles, ladders, or buildings; raw anchors are not public aggregate parameters."
                    : "Use world_editor map SEARCH/REPLACE tokens covering the full footprint, or one lower-left cell. Preview with dryRun=true."
            };
        }

        private static PlacementDetails BuildPlacementDetails(BuildingDef def, int x, int y, int worldId,
            Orientation orientation = Orientation.Neutral)
        {
            int cell = Grid.XYToCell(x, y);
            var layout = PlacementLayout(def, orientation);
            return new PlacementDetails
            {
                PrefabId = def.PrefabID,
                AnchorX = x,
                AnchorY = y,
                WorldId = worldId,
                Orientation = orientation,
                Width = layout.Width,
                Height = layout.Height,
                PlacementPoint = BuildPlacementPosition(cell, def, orientation),
                Footprint = FootprintCells(def, x, y, worldId, orientation).ToList(),
                Intake = PumpIntakeInfo(def, PlacementOriginCell(def, x, y, orientation))
            };
        }

        private static IEnumerable<FootprintCell> FootprintCells(BuildingDef def, int x, int y, int worldId,
            Orientation orientation = Orientation.Neutral)
        {
            foreach (var point in PlacementLayout(def, orientation).Cells(x, y))
            {
                int fx = point.Item1, fy = point.Item2;
                bool inBounds = fx >= 0 && fy >= 0 && fx < Grid.WidthInCells && fy < Grid.HeightInCells;
                int cell = inBounds ? Grid.XYToCell(fx, fy) : Grid.InvalidCell;
                yield return new FootprintCell
                {
                    X = fx, Y = fy, Cell = cell, WorldId = worldId,
                    Valid = inBounds && Grid.IsValidCell(cell),
                    Visible = inBounds && Grid.IsValidCell(cell) && Grid.IsVisible(cell),
                    InWorld = inBounds && Grid.IsValidCell(cell) && ToolUtil.CellMatchesWorld(cell, worldId)
                };
            }
        }

        private static FootprintValidation ValidateFootprint(PlacementDetails placement, GameObject ignored = null)
        {
            var invalid = placement.Footprint
                .Where(cell => !cell.Valid || !cell.Visible || !cell.InWorld)
                .Select(cell => cell.ToDictionary())
                .ToList();

            var obstructions = FindFootprintObstructions(placement, ignored);
            AddBackwallFoundationFailure(placement, obstructions);

            if (invalid.Count == 0 && obstructions.Count == 0)
                return FootprintValidation.Success();

            var backwallFailure = obstructions.FirstOrDefault(obstruction =>
                obstruction.ContainsKey("reasonCode")
                && string.Equals(obstruction["reasonCode"]?.ToString(), "backwall_required", StringComparison.Ordinal));
            string error = invalid.Count > 0
                ? "Invalid footprint: every occupied cell must be visible, valid, and inside the selected world"
                : backwallFailure != null
                    ? backwallFailure["reason"]?.ToString()
                    : "Obstructed footprint: occupied terrain, building, or blueprint overlaps the requested cells";
            return FootprintValidation.Invalid(error, invalid, obstructions);
        }

        private static void AddBackwallFoundationFailure(PlacementDetails placement, List<Dictionary<string, object>> obstructions)
        {
            var def = ResolveBuildingDefForPlacement(placement);
            if (def == null)
                return;

            string rule = def.BuildLocationRule.ToString();
            if (!string.Equals(rule, "OnBackWall", StringComparison.OrdinalIgnoreCase))
                return;

            int cell = PlacementOriginCell(def, placement.AnchorX, placement.AnchorY, placement.Orientation);
            bool nativeFoundationValid = Grid.IsValidCell(cell)
                && BuildingDef.CheckFoundation(
                    cell,
                    placement.Orientation,
                    def.BuildLocationRule,
                    def.WidthInCells,
                    def.HeightInCells);
            var decision = BuildPlanningBackwallSupportPolicy.Evaluate(rule, nativeFoundationValid);
            if (decision.Valid)
                return;

            obstructions.Insert(0, new Dictionary<string, object>
            {
                ["kind"] = "missing_backwall",
                ["x"] = placement.AnchorX,
                ["y"] = placement.AnchorY,
                ["cell"] = cell,
                ["buildLocationRule"] = rule,
                ["reasonCode"] = decision.ReasonCode,
                ["reason"] = decision.Error,
                ["nativeFoundationCheck"] = true
            });
        }

        private static Dictionary<string, object> ActualPlacementDetails(GameObject go, BuildingDef def, int expectedX, int expectedY)
        {
            int cell = Grid.PosToCell(go);
            int x = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1;
            int y = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1;
            bool registered = RegisteredBuildingOccupancy.TryGetBounds(go, cell, def, out int[] bounds);
            int originX = registered ? bounds[0] : -1;
            int originY = registered ? bounds[1] : -1;

            int worldId = Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell) ? Grid.WorldIdx[cell] : -1;

            return new Dictionary<string, object>
            {
                ["objectCell"] = cell,
                ["objectX"] = x,
                ["objectY"] = y,
                ["derivedAnchorX"] = originX,
                ["derivedAnchorY"] = originY,
                ["worldId"] = worldId,
                ["registeredFootprint"] = registered,
                ["registrationPending"] = !registered && go.GetComponent<Constructable>() != null && !go.GetComponent<Constructable>().isSpawned,
                ["occupiedBounds"] = bounds,
                ["occupiedCells"] = RegisteredBuildingOccupancy.Cells(go, cell, def),
                ["note"] = "Anchor is derived from cells actually registered to this object; missing registration is not a successful placement."
            };
        }

        private static Dictionary<string, object> ComparePlacement(PlacementDetails expected, Dictionary<string, object> actual)
        {
            int actualX = actual.ContainsKey("derivedAnchorX") ? Convert.ToInt32(actual["derivedAnchorX"]) : -1;
            int actualY = actual.ContainsKey("derivedAnchorY") ? Convert.ToInt32(actual["derivedAnchorY"]) : -1;
            int actualWorld = actual.ContainsKey("worldId") ? Convert.ToInt32(actual["worldId"]) : -1;
            bool anchorMatches = actualX == expected.AnchorX && actualY == expected.AnchorY;
            bool worldMatches = actualWorld >= 0 && (expected.WorldId < 0 || actualWorld == expected.WorldId);
            var bounds = actual.ContainsKey("occupiedBounds") ? actual["occupiedBounds"] as int[] : null;
            bool footprintMatches = bounds != null && actual["occupiedCells"] is List<int> cells
                && new HashSet<int>(cells).SetEquals(expected.Footprint.Select(point => point.Cell));
            bool valid = anchorMatches && worldMatches && footprintMatches;
            bool pending = actual.TryGetValue("registrationPending", out var pendingValue) && Equals(pendingValue, true)
                && worldMatches && Convert.ToInt32(actual["objectCell"]) == PlacementOriginCell(
                    Assets.GetBuildingDef(expected.PrefabId), expected.AnchorX, expected.AnchorY, expected.Orientation);
            return new Dictionary<string, object>
            {
                ["valid"] = valid,
                ["pending"] = pending,
                ["anchorMatches"] = anchorMatches,
                ["footprintMatches"] = footprintMatches,
                ["worldMatches"] = worldMatches,
                ["expectedAnchor"] = new { x = expected.AnchorX, y = expected.AnchorY },
                ["actualDerivedAnchor"] = new { x = actualX, y = actualY },
                ["expectedWorldId"] = expected.WorldId,
                ["actualWorldId"] = actualWorld,
                ["next"] = valid
                    ? "Placement verified against registered cells. Verify completed work at milestones."
                    : pending ? "Blueprint exists; native footprint registration awaits OnSpawn. Verify with a subsequent paused read after OnSpawn before retrying."
                    : "Placement differs from the request. Inspect the returned object ID before any retry or cancellation."
            };
        }

        private         static Dictionary<string, object> BuildPlacementFailureDetails(PlacementDetails placement, MaterialSelection materialResult)
        {
            return new Dictionary<string, object>
            {
                ["placement"] = placement.ToDictionary(),
                ["obstructions"] = FindFootprintObstructions(placement).Take(50).ToList(),
                ["materialSelection"] = materialResult.ToDictionary(),
                ["materials"] = materialResult.ToDictionary(),
                ["reasonHint"] = "TryPlace returned null after preflight; inspect obstructions/support/materialSelection for likely cause."
            };
        }
    }
}
