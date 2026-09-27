using System;
using System.Collections.Generic;

namespace OniMcp.Tools
{
    internal static class ConstructionWorkCells
    {
        // Each native row is one possible work position followed by its required
        // clear cells. Rows are alternatives, including routes to the same cell.
        internal static int FindReachable(IEnumerable<int[]> rows, Func<int, bool> clear,
            Func<int, bool> reachable)
        {
            foreach (var row in rows)
            {
                if (row == null || row.Length == 0) continue;
                bool valid = true;
                for (int i = 1; i < row.Length; i++)
                    if (!clear(row[i])) { valid = false; break; }
                if (valid && reachable(row[0])) return row[0];
            }
            return -1;
        }
    }
}
