using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    internal static class ContinueResponse
    {
        internal static JObject Observation(ContinueSample sample, List<ColonyFinding> findings,
            bool full, bool includeMetadata)
        {
            var result = JObject.FromObject(ColonyObservation.Serialize(sample, findings));
            if (full) return result;
            if (!includeMetadata)
                result["coverage"] = new JObject { ["profile"] = "colony_v1", ["available"] = sample.Available };
            var targets = new HashSet<int>(findings.Where(item => item.TargetId.HasValue).Select(item => item.TargetId.Value));
            result["researchStations"] = new JArray(((JArray)result["researchStations"])
                .Where(item => targets.Contains(item["id"].Value<int>())).Select(item => item.DeepClone()));
            if (!result["researchStations"].HasValues) result.Remove("researchStations");
            if (!result["dupeConcerns"].HasValues) result.Remove("dupeConcerns");
            return result;
        }

        internal static JArray Events(IEnumerable<Dictionary<string, object>> events, bool full,
            Dictionary<string, string> previous, Dictionary<string, string> current, ISet<string> changedInWindow = null)
        {
            return JArray.FromObject(events.Where(item => full
                || (changedInWindow != null && item.TryGetValue("findingId", out var id) && changedInWindow.Contains(id.ToString()))
                || !IsUnchangedIgnoredFinding(item, previous, current)));
        }

        private static bool IsUnchangedIgnoredFinding(Dictionary<string, object> item,
            Dictionary<string, string> previous, Dictionary<string, string> current)
        {
            if (!item.TryGetValue("ignored", out var ignored) || !Equals(ignored, true)
                || !item.TryGetValue("findingId", out var findingId)) return false;
            string id = findingId.ToString();
            return previous.TryGetValue(id, out string before) && current.TryGetValue(id, out string after) && before == after;
        }

        internal static string FinalReason(string reason, bool enabledEvent) =>
            reason == "window_complete" && enabledEvent ? "event" : reason;
    }
}
