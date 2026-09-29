using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static GameObject TryPlaceNativeBlueprint(BuildingDef def, Vector3 position,
            Orientation orientation, IList<Tag> elements, string facadeId, GameObject replacementTarget, out string error)
        {
            error = null;
            var blueprint = replacementTarget != null
                ? def.TryReplaceTile(null, position, orientation, elements, facadeId)
                : def.TryPlace(null, position, orientation, elements, facadeId);
            if (blueprint != null && replacementTarget != null)
                PreserveUtilityReplacementConnections(def, blueprint, replacementTarget, out error);
            return blueprint;
        }

        private static bool PreserveUtilityReplacementConnections(
            BuildingDef def, GameObject blueprint, GameObject source, out string error)
        {
            error = null;
            if (!IsExactConnectionUtilityPrefab(def?.PrefabID)
                || blueprint?.GetComponent<Constructable>()?.IsReplacementTile != true
                || def.ReplacementLayer == ObjectLayer.NumLayers)
                return true;
            int cell = Grid.PosToCell(blueprint);
            if (!Grid.IsValidCell(cell) || Grid.Objects[cell, (int)def.ReplacementLayer] != blueprint)
                return true;
            var sourceDef = source?.GetComponent<BuildingComplete>()?.Def;
            if (!UtilityPrefabPolicy.SameFamily(def.PrefabID, sourceDef?.PrefabID)
                || !EqualsIgnoreCase(blueprint.GetComponent<Building>()?.Def?.PrefabID, def.PrefabID)
                || !def.CanReplace(source) || Grid.PosToCell(source) != cell)
                return true;
            var oldVisualizer = source.GetComponent<KAnimGraphTileVisualizer>();
            var newVisualizer = blueprint.GetComponent<KAnimGraphTileVisualizer>();
            if (oldVisualizer == null || newVisualizer == null)
            {
                error = "Utility replacement is missing its original or blueprint visualizer.";
                return false;
            }
            // Native completion copies only the blueprint mask. Retain existing
            // branches even when the requested upgrade path does not traverse them.
            var desired = newVisualizer.Connections | oldVisualizer.Connections;
            newVisualizer.UpdateConnections(desired);
            if (newVisualizer.Connections == desired) return true;
            error = "Utility replacement did not retain its original connection bits.";
            return false;
        }

        // Constructable copies the blueprint visualizer's serialized Connections to
        // the completed object. Updating only the network's planned grid loses them.
        private static bool PersistUtilityPathConnections(BuildingDef def, List<CellCoord> path, out string error)
        {
            error = null;
            if (!IsExactConnectionUtilityPrefab(def?.PrefabID)) return true;
            var blueprints = UnityEngine.Object.FindObjectsByType<BuildingUnderConstruction>(FindObjectsSortMode.None)
                .Where(building => building != null && building.Def?.PrefabID == def.PrefabID)
                .GroupBy(building => Grid.PosToCell(building)).ToDictionary(group => group.Key, group => group.First().gameObject);
            var visualizers = new Dictionary<int, KAnimGraphTileVisualizer>();
            foreach (var point in path)
            {
                int cell = Grid.XYToCell(point.x, point.y);
                var source = Grid.Objects[cell, (int)def.ObjectLayer];
                var go = source;
                if (go == null || go.GetComponent<Building>()?.Def?.PrefabID != def.PrefabID)
                    blueprints.TryGetValue(cell, out go);
                if (!PreserveUtilityReplacementConnections(def, go, source, out error))
                    return false;
                var visualizer = go?.GetComponent<KAnimGraphTileVisualizer>();
                if (visualizer == null)
                {
                    error = "Path cell has no utility visualizer: " + point.x + "," + point.y;
                    return false;
                }
                visualizers[cell] = visualizer;
            }
            var desired = visualizers.ToDictionary(pair => pair.Key, pair => pair.Value.Connections);
            for (int i = 1; i < path.Count; i++)
            {
                int from = Grid.XYToCell(path[i - 1].x, path[i - 1].y);
                int to = Grid.XYToCell(path[i].x, path[i].y);
                if (!TryExpectedConnectionBits(from, to, out var fromBit, out var toBit))
                {
                    error = "Path contains non-adjacent cells.";
                    return false;
                }
                desired[from] |= fromBit;
                desired[to] |= toBit;
            }
            foreach (var pair in visualizers)
            {
                // Before OnSpawn, UpdateConnections still persists the serialized bits.
                pair.Value.UpdateConnections(desired[pair.Key]);
                if (pair.Value.Connections != desired[pair.Key])
                {
                    error = "Utility visualizer did not retain requested connection bits.";
                    return false;
                }
            }
            return !IsCompletedUtilityPath(def, path) || RefreshAndValidateUtilityPathNetwork(def, path, out error);
        }
    }
}
