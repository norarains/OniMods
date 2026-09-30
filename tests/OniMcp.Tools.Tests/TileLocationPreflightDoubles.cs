using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private sealed partial class PlacementDetails
        {
            internal string PrefabId;
            internal BuildingDef FixtureDef;
        }

        internal static List<Dictionary<string, object>> FootprintObstructionsFixture(
            BuildingDef def, IEnumerable<int> cells, GameObject ignored = null)
        {
            var placement = new PlacementDetails { PrefabId = def.PrefabID, FixtureDef = def };
            foreach (int cell in cells)
                placement.Footprint.Add(new ReplacementCell
                {
                    Cell = cell,
                    Valid = Grid.IsValidCell(cell),
                    Visible = PlayerVisibility.Cell(cell),
                    InWorld = ToolUtil.CellMatchesWorld(cell, 0)
                });
            int origin = placement.Footprint.First().Cell;
            placement.AnchorX = Grid.CellColumn(origin); placement.AnchorY = Grid.CellRow(origin);
            return FindFootprintObstructions(placement, ignored);
        }

        // Native geometry, terrain and unrelated object families are boundaries.
        // Production FindFootprintObstructions owns the new cross-layer check and
        // the native replacement filter; neither is duplicated by the fixture.
        private static BuildingDef ResolveBuildingDefForPlacement(PlacementDetails placement)
            => placement.FixtureDef;
        private static IEnumerable<ReplacementCell> PlacementSafetyFootprint(BuildingDef def, PlacementDetails placement)
            => placement.Footprint;
        private static bool IsUtilityPrefab(string id)
            => UtilityPrefabPolicy.IsLinear(id) || id != null && (id.Contains("Wire") || id.Contains("Conduit"));
        private static bool UsesNativeBridgeEndpointRegistration(BuildingDef def) => false;
        private static bool IsNaturalDiggableSolidCell(int cell, int world) => false;
        private static IEnumerable<Dictionary<string, object>> UprootableObstructionsAtCell(int cell, int world)
            => Enumerable.Empty<Dictionary<string, object>>();
        private static IEnumerable<Dictionary<string, object>> ExistingBuildingFootprintObstructions(int world, HashSet<int> cells)
            => Enumerable.Empty<Dictionary<string, object>>();
        private static List<Dictionary<string, object>> FindUtilityLayerConflicts(BuildingDef def, PlacementDetails placement, GameObject ignored)
            => new List<Dictionary<string, object>>();
        private static List<Dictionary<string, object>> FindBuildingLayerConflicts(BuildingDef def, PlacementDetails placement)
            => new List<Dictionary<string, object>>();
        private static List<Dictionary<string, object>> FindLogicEndpointConflicts(BuildingDef def, PlacementDetails placement)
            => new List<Dictionary<string, object>>();
    }
}

internal static partial class Grid
{
    internal const int InvalidCell = -1;
}
