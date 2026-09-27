using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    internal static class FactQueryIdentity
    {
        // Return a superset of possible IDs, or null when an index cannot answer
        // the predicate. The ordinary predicate still runs on every candidate.
        internal static HashSet<int> Candidates(FactPredicate predicate)
        {
            if (predicate == null) return null;
            if (predicate.Operator == "AND" || predicate.Operator == "OR")
            {
                var left = Candidates(predicate.Left);
                var right = Candidates(predicate.Right);
                if (predicate.Operator == "AND")
                {
                    if (left == null) return right;
                    if (right != null) left.IntersectWith(right);
                }
                else
                {
                    if (left == null || right == null) return null;
                    left.UnionWith(right);
                }
                return left;
            }
            if (!string.Equals(predicate.Field, "id", StringComparison.OrdinalIgnoreCase) || predicate.Operator != "=") return null;
            double value = predicate.Value.Value<double>();
            return value >= int.MinValue && value <= int.MaxValue && value == Math.Truncate(value)
                ? new HashSet<int> { (int)value } : new HashSet<int>();
        }
    }
}
