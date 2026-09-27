using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    internal static class NativeConditionDetails
    {
        internal static Dictionary<string, object> Read(GameObject go, bool includeStatuses = true)
        {
            if (go == null) return null;
            int cell = Grid.PosToCell(go);
            if (!Grid.IsValidCell(cell) || !ToolUtil.VisibleCellAllowed(cell, true)) return null;
            var statuses = new List<Dictionary<string, object>>();
            var group = go.GetComponent<KSelectable>()?.GetStatusItemGroup();
            if (includeStatuses && group != null)
                foreach (var entry in group)
                    if (entry.item != null)
                        statuses.Add(new Dictionary<string, object> {
                            ["id"] = entry.item.Id, ["message"] = ToolUtil.CleanName(entry.GetName()) });
            var result = new Dictionary<string, object> {
                ["id"] = go.GetComponent<KPrefabID>()?.InstanceID ?? go.GetInstanceID(),
                ["prefabId"] = go.GetComponent<KPrefabID>()?.PrefabTag.Name ?? go.name,
                ["worldId"] = go.GetMyWorldId(), ["x"] = Grid.CellColumn(cell), ["y"] = Grid.CellRow(cell),
                ["statuses"] = statuses
            };
            if (go.GetComponent<HarvestDesignatable>() != null || go.GetComponent<StandardCropPlant>() != null)
                result["wilting"] = go.HasTag(GameTags.Wilting);
            var plot = go.GetComponent<PlantablePlot>();
            if (plot?.Occupant != null) result["plant"] = Read(plot.Occupant);
            return result;
        }

        internal static void DiagnosticTargets(ColonyFinding finding, ColonyDiagnostic.DiagnosticResult result)
        {
            var targets = new List<GameObject>();
            if (result.clickThroughTarget != null && result.clickThroughTarget.second != null)
                targets.Add(result.clickThroughTarget.second);
            if (result.clickThroughObjects != null) targets.AddRange(result.clickThroughObjects.Where(go => go != null));
            bool farming = Equals(finding.Details["diagnosticId"], "FarmDiagnostic");
            var details = targets.Distinct().Select(go => Read(go, farming)).Where(item => item != null).ToList();
            if (details.Count == 0) return;
            finding.Details["targetCount"] = details.Count;
            finding.Details["targets"] = details.Take(8).ToList();
            finding.Details["targetsTruncated"] = details.Count > 8;
            finding.Details["targetStatusSource"] = "current_native_statuses";
        }
    }
}
