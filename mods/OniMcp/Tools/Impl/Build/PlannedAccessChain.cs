using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    internal static class PlannedAccessChain
    {
        // Each dependency points to an already accepted seed/predecessor. Disjoint
        // islands and cycles without current access never become executable.
        internal static Dictionary<int, int> Resolve(IEnumerable<int> candidates, IEnumerable<int> seeds,
            Func<int, int, ISet<int>, bool> canFollow)
        {
            var pending = new HashSet<int>(candidates);
            var ready = new HashSet<int>(seeds);
            pending.ExceptWith(ready);
            var result = new Dictionary<int, int>();
            bool changed;
            do
            {
                changed = false;
                foreach (int cell in pending.ToArray())
                    foreach (int prior in ready.ToArray())
                        if (canFollow(cell, prior, ready))
                        {
                            result[cell] = prior;
                            pending.Remove(cell); ready.Add(cell); changed = true;
                            break;
                        }
            } while (changed);
            return result;
        }
    }
}
