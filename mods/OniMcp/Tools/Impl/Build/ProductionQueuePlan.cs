using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    internal sealed class ProductionQueuePlan
    {
        internal readonly Dictionary<string, int> Counts;
        internal readonly List<Dictionary<string, object>> Changes = new List<Dictionary<string, object>>();
        private ProductionQueuePlan(Dictionary<string, int> counts) { Counts = counts; }

        // The plan owns a copy. Invalid trailing items cannot alter the caller's live queue,
        // including when clearAll was requested. Native writes happen only after this returns.
        internal static ProductionQueuePlan Create(IDictionary<string, int> initial, IEnumerable<JObject> items,
            bool clearAll, int maximum, int infinite)
        {
            var plan = new ProductionQueuePlan(initial.ToDictionary(pair => pair.Key, pair => pair.Value));
            if (clearAll)
                foreach (string id in plan.Counts.Keys.ToList())
                {
                    if (plan.Counts[id] != 0) plan.Change(id, "clear", 0);
                }
            foreach (var item in items)
            {
                if (item == null) throw new ArgumentException("Each queue item must be an object");
                string id = item["recipeId"]?.ToString()?.Trim();
                if (id == null || !plan.Counts.ContainsKey(id)) throw new ArgumentException("Recipe unavailable on this fabricator: " + id);
                string mode = (item["mode"]?.ToString() ?? "set").Trim().ToLowerInvariant();
                if (item["count"] != null && item["count"].Type != JTokenType.Integer)
                    throw new ArgumentException("Queue count must be an integer");
                long requested = item["count"]?.Value<long>() ?? 1;
                if (requested < 0 || requested > int.MaxValue) throw new ArgumentException("Queue count is out of range");
                long before = plan.Counts[id], after;
                switch (mode)
                {
                    case "clear": after = 0; break;
                    case "infinite": after = infinite; break;
                    case "set": after = Math.Min(maximum, requested); break;
                    case "add": after = before == infinite ? infinite : Math.Min(maximum, before + Math.Max(1, requested)); break;
                    case "remove": after = Math.Max(0, (before == infinite ? maximum : before) - Math.Max(1, requested)); break;
                    default: throw new ArgumentException("mode must be set, add, remove, infinite or clear");
                }
                plan.Change(id, mode, (int)after);
            }
            return plan;
        }

        private void Change(string id, string mode, int after)
        {
            Changes.Add(new Dictionary<string, object> { ["recipeId"] = id, ["mode"] = mode,
                ["before"] = Counts[id], ["after"] = after, ["changed"] = Counts[id] != after });
            Counts[id] = after;
        }
    }
}
