using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    internal static class ReachableWirePath
    {
        internal static List<int> Find(int start, int goal, Func<int, IEnumerable<int>> neighbors,
            Func<int, bool> allowed, int maxVisited, int maxLength)
        {
            var previous = new Dictionary<int, int> { [start] = start };
            var pending = new Queue<int>(); pending.Enqueue(start);
            while (pending.Count > 0 && previous.Count <= maxVisited)
            {
                int cell = pending.Dequeue();
                if (cell == goal)
                {
                    var path = new List<int> { cell };
                    while (cell != start) { cell = previous[cell]; path.Add(cell); }
                    path.Reverse();
                    return path.Count <= maxLength ? path : null;
                }
                foreach (int next in neighbors(cell))
                    if (!previous.ContainsKey(next) && allowed(next))
                    { previous[next] = cell; pending.Enqueue(next); }
            }
            return null;
        }
    }

}
