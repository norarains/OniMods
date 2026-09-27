using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
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
                var go = Grid.Objects[cell, (int)def.ObjectLayer];
                if (go == null || go.GetComponent<Building>()?.Def?.PrefabID != def.PrefabID)
                    blueprints.TryGetValue(cell, out go);
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
