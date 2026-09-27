using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    // Caller-owned interruption settings. Finding lifetimes never modify this set.
    internal sealed class ContinueStopEvents
    {
        internal static readonly string[] Defaults = {
            "monitor_unavailable", "no_dupes", "red_alert", "invalid_dupe_cell", "low_breath",
            "health", "starving", "stress", "body_temperature", "dupe_missing", "food_low",
            "printing_pod_ready", "skill_points_available", "advanced_research_skill_missing",
            "research_unpowered", "research_material_missing", "research_inoperable",
            "building_material_missing", "construction_material_missing",
            "worker_idle", "hud", "research_queue_empty", "orders_finished",
            "no_observed_progress", "orders_not_progressing"
        };
        private readonly HashSet<string> ignored = new HashSet<string>(StringComparer.Ordinal);
        internal string[] Ignored => ignored.OrderBy(value => value, StringComparer.Ordinal).ToArray();

        internal void Update(IEnumerable<string> ignore, IEnumerable<string> unignore)
        {
            var additions = (ignore ?? Enumerable.Empty<string>()).ToArray();
            var removals = (unignore ?? Enumerable.Empty<string>()).ToArray();
            foreach (string key in additions.Concat(removals))
                if (string.IsNullOrWhiteSpace(key) || !Defaults.Contains(key.Split(':')[0]))
                    throw new ArgumentException("Unknown stop event: " + key);
            if (additions.Intersect(removals).Any())
                throw new ArgumentException("An event cannot be ignored and unignored in the same request.");
            foreach (string key in removals) ignored.Remove(key);
            foreach (string key in additions) ignored.Add(key);
        }

        internal bool IsIgnored(string code, string findingId = null) => ignored.Contains(code)
            || (findingId != null && ignored.Contains(findingId));
        internal bool IsEnabled(string code, string findingId = null) => Defaults.Contains(code) && !IsIgnored(code, findingId);
    }
}
