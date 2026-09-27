using UnityEngine;

namespace OniMcp.Tools
{
    // Adjacent lines and dangling native bits alone do not prove a connection.
    internal static class UtilityConnectionRead
    {
        internal static bool HasLine(int cell, ObjectLayer[] layers) => Line(cell, layers) != null;
        internal static bool IsBuilt(int cell, ObjectLayer[] layers) => Line(cell, layers)?.GetComponent<BuildingComplete>() != null;

        internal static UtilityConnections Read(int cell, ObjectLayer[] layers)
        {
            var go = Line(cell, layers);
            if (go == null) return (UtilityConnections)0;
            UtilityConnections result = 0;
            Add(ref result, cell, go, layers, 0, 1, UtilityConnections.Up, UtilityConnections.Down);
            Add(ref result, cell, go, layers, 0, -1, UtilityConnections.Down, UtilityConnections.Up);
            Add(ref result, cell, go, layers, -1, 0, UtilityConnections.Left, UtilityConnections.Right);
            Add(ref result, cell, go, layers, 1, 0, UtilityConnections.Right, UtilityConnections.Left);
            return result;
        }

        private static void Add(ref UtilityConnections result, int cell, GameObject go, ObjectLayer[] layers,
            int dx, int dy, UtilityConnections direction, UtilityConnections opposite)
        {
            int neighbor = Grid.XYToCell(Grid.CellColumn(cell) + dx, Grid.CellRow(cell) + dy);
            var other = Line(neighbor, layers);
            if (other == null || Grid.WorldIdx[cell] != Grid.WorldIdx[neighbor]) return;
            bool physical = go.GetComponent<BuildingComplete>() != null && other.GetComponent<BuildingComplete>() != null;
            if ((Raw(go, cell, physical) & direction) != 0 && (Raw(other, neighbor, physical) & opposite) != 0)
                result |= direction;
        }

        private static UtilityConnections Raw(GameObject go, int cell, bool physical)
        {
            var provider = go.GetComponent<IHaveUtilityNetworkMgr>()
                ?? go.GetComponent<Building>()?.Def?.BuildingComplete?.GetComponent<IHaveUtilityNetworkMgr>();
            var manager = provider?.GetNetworkManager();
            return manager == null ? (UtilityConnections)0 : manager.GetConnections(cell, physical);
        }

        private static GameObject Line(int cell, ObjectLayer[] layers)
        {
            if (!Grid.IsValidCell(cell)) return null;
            foreach (var layer in layers)
            {
                var go = Grid.Objects[cell, (int)layer];
                if (go != null) return go;
            }
            return null;
        }
    }
}
