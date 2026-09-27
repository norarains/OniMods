using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    internal static class FactQueryExecutor
    {
        internal const int MaxScan = 50000, MaxOutputChars = 48000;
        internal static JObject Execute(FactQueryPlan plan, FactDataset dataset, int milliseconds = 250)
        {
            plan.Validate(dataset);
            var clock = Stopwatch.StartNew();
            System.Action budget = () => {
                if (clock.Elapsed.TotalMilliseconds > milliseconds)
                    throw new ArgumentException("Query time budget exceeded; narrow the dataset/area or requested fields. No partial aggregate returned.");
            };
            int scanned = 0, matched = 0;
            bool complete = true;
            var rows = new List<FactRow>();
            var groups = new Dictionary<string, AggregateGroup>(StringComparer.Ordinal);
            if (plan.Aggregates && plan.GroupBy == null) groups[""] = new AggregateGroup(plan, JValue.CreateNull());
            foreach (var row in dataset.Rows())
            {
                budget();
                if (++scanned > MaxScan) throw new ArgumentException("Query scan budget exceeded; no partial aggregate returned.");
                if (plan.Where != null && !plan.Where.Matches(row)) continue;
                if (dataset.Required != null && !dataset.Required(row)) continue;
                matched++;
                if (plan.Aggregates)
                {
                    JToken groupValue = plan.GroupBy == null ? JValue.CreateNull() : row.Get(plan.GroupBy);
                    string key = plan.GroupBy == null ? "" : groupValue.Type + ":" + groupValue.ToString(Formatting.None);
                    if (!groups.TryGetValue(key, out var group))
                    {
                        if (groups.Count >= 5000) throw new ArgumentException("Query group budget exceeded");
                        groups[key] = group = new AggregateGroup(plan, groupValue);
                    }
                    group.Add(row);
                }
                else if (plan.OrderBy != null) rows.Add(row);
                else if (matched > plan.Offset)
                {
                    if (rows.Count == plan.Limit) { complete = false; break; }
                    rows.Add(row);
                }
            }
            budget();
            var output = new List<JArray>();
            var missing = new Dictionary<string, int>();
            int totalRows;
            if (plan.Aggregates)
            {
                var values = groups.Values.Select(g => g.Values()).ToList();
                totalRows = values.Count;
                if (plan.OrderBy != null)
                {
                    int column = plan.Select.FindIndex(p => string.Equals(p.Label, plan.OrderBy, StringComparison.OrdinalIgnoreCase));
                    values.Sort((a, b) => FactPredicate.Compare(a[column], b[column]) * (plan.Descending ? -1 : 1));
                }
                output.AddRange(values.Skip(plan.Offset).Take(plan.Limit));
                // Report unknown input values even when their group is outside this output page.
                foreach (var g in groups.Values)
                    foreach (var entry in g.Unknown)
                        missing[entry.Key] = (missing.TryGetValue(entry.Key, out int n) ? n : 0) + entry.Value;
            }
            else
            {
                totalRows = matched;
                if (plan.OrderBy != null)
                {
                    // Charge each requested native getter before sorting; the comparer only reads cached keys.
                    foreach (var row in rows) { budget(); row.Get(plan.OrderBy); }
                    budget();
                    rows.Sort((a, b) => FactPredicate.Compare(a.Get(plan.OrderBy), b.Get(plan.OrderBy)) * (plan.Descending ? -1 : 1));
                    rows = rows.Skip(plan.Offset).Take(plan.Limit).ToList();
                }
                foreach (var row in rows)
                {
                    budget();
                    output.Add(new JArray(plan.Select.Select(p => row.Get(p.Field))));
                }
            }
            budget();
            bool truncated = !complete || plan.Offset + output.Count < totalRows;
            var result = new JObject {
                ["dataset"] = dataset.Name,
                ["columns"] = new JArray(plan.Select.Select(p => p.Label)),
                ["rows"] = new JArray(output), ["returned"] = output.Count,
                ["matched"] = complete ? (JToken)matched : JValue.CreateNull(),
                ["truncated"] = truncated, ["scanned"] = scanned,
                ["elapsedMs"] = Math.Round(clock.Elapsed.TotalMilliseconds, 2)
            };
            if (truncated) result["nextOffset"] = plan.Offset + output.Count;
            if (missing.Count > 0) result["unknownInputs"] = JObject.FromObject(missing);
            // A large single field must not silently bypass output limits.
            if (result.ToString(Formatting.None).Length > MaxOutputChars)
                throw new ArgumentException("Query output budget exceeded; select fewer fields or reduce LIMIT");
            return result;
        }

        private sealed class AggregateGroup
        {
            private readonly FactQueryPlan plan;
            private readonly JToken key;
            private readonly double[] sums;
            private readonly int[] counts;
            internal readonly Dictionary<string, int> Unknown = new Dictionary<string, int>();
            internal AggregateGroup(FactQueryPlan plan, JToken key)
            { this.plan = plan; this.key = key; sums = new double[plan.Select.Count]; counts = new int[sums.Length]; }
            internal void Add(FactRow row)
            {
                for (int i = 0; i < plan.Select.Count; i++)
                {
                    var p = plan.Select[i];
                    if (p.Aggregate == null) continue;
                    if (p.Field == "*") { counts[i]++; continue; }
                    var value = row.Get(p.Field);
                    if (FactPredicate.IsNull(value))
                    { Unknown[p.Label] = (Unknown.TryGetValue(p.Label, out int n) ? n : 0) + 1; continue; }
                    counts[i]++;
                    if (p.Aggregate == "SUM") sums[i] += value.Value<double>();
                }
            }
            internal JArray Values()
            {
                var values = new JArray();
                for (int i = 0; i < plan.Select.Count; i++)
                {
                    var p = plan.Select[i];
                    values.Add(p.Aggregate == null ? key : p.Aggregate == "COUNT" ? new JValue(counts[i])
                        : counts[i] == 0 || Unknown.ContainsKey(p.Label) ? JValue.CreateNull() : new JValue(sums[i]));
                }
                return values;
            }
        }
    }
}
