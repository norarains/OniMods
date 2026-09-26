using System;
using UnityEngine;

namespace OniMcp.Tools
{
    internal static class RegisteredBuildingOccupancy
    {
        internal static bool TryGetBounds(GameObject instance, int origin, BuildingDef def, out int[] bounds)
        {
            bounds = null;
            if (instance == null || def == null || !Grid.IsValidCell(origin))
                return false;
            int originX = Grid.CellColumn(origin), originY = Grid.CellRow(origin);
            int radius = Math.Max(1, Math.Max(def.WidthInCells, def.HeightInCells));
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            for (int y = Math.Max(0, originY - radius); y <= Math.Min(Grid.HeightInCells - 1, originY + radius); y++)
                for (int x = Math.Max(0, originX - radius); x <= Math.Min(Grid.WidthInCells - 1, originX + radius); x++)
                {
                    if (!Contains(instance, Grid.XYToCell(x, y), def))
                        continue;
                    minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
                }
            if (maxX < 0)
                return false;
            bounds = new[] { minX, minY, maxX, maxY };
            return true;
        }

        internal static bool Contains(GameObject instance, int cell, BuildingDef def)
        {
            if (instance == null || def == null || !Grid.IsValidCell(cell))
                return false;
            return Grid.Objects[cell, (int)def.ObjectLayer] == instance;
        }
    }
}
