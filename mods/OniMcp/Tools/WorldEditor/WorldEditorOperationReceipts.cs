using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
    private static string SummarizeOperationResult(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "ok";
        try
        {
            var token = JToken.Parse(text);
            var obj = token as JObject;
            if (obj == null)
                return "ok";
            var parts = new List<string>();
            AddSummaryPart(parts, obj, "planned");
            AddSummaryPart(parts, obj, "marked");
            foreach (string key in new[] { "changed", "applied", "succeeded", "count", "matched", "dryRun", "committed", "markedForHarvest", "harvestWhenReady", "canBeHarvested", "target", "id" })
                AddSummaryPart(parts, obj, key);
            AddSummaryPart(parts, obj, "executedCells");
            AddSummaryPart(parts, obj, "remainingCells");
            AddSummaryPart(parts, obj, "failed");
            AddSummaryPart(parts, obj, "pathCells");
            AddSummaryPart(parts, obj, "prefabId");
            AddSummaryPart(parts, obj, "action");
            return parts.Count == 0 ? "ok" : string.Join(", ", parts);
        }
        catch
        {
            return TrimOperationText(text, 500);
        }
    }

    private static void AddSummaryPart(List<string> parts, JObject obj, string key)
    {
        if (obj[key] != null)
            parts.Add(key + "=" + obj[key]);
    }

    private static string TrimOperationText(string text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max)
            return text ?? string.Empty;
        return text.Substring(0, max) + "...";
    }

        private static void AppendOperationReceiptFacts(JObject receipt, JObject result)
        {
            if (result?["areaId"] == null) return;
            foreach (string key in new[] { "areaId", "worldId", "label", "rect" })
                if (result[key] != null) receipt[key] = result[key].DeepClone();
        }
    }
}
