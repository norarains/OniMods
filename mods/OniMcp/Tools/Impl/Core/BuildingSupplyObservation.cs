using System;
using System.Collections.Generic;
using UnityEngine;

namespace OniMcp.Tools
{
    internal static class BuildingSupplyObservation
    {
        internal static void Read(GameObject go, bool construction, List<BuildingSupplyFinding> result)
        {
            int cell = Grid.PosToCell(go);
            if (!Grid.IsValidCell(cell) || !ToolUtil.VisibleCellAllowed(cell, true)) return;
            var group = go.GetComponent<KSelectable>()?.GetStatusItemGroup();
            if (group == null) return;
            BuildingSupplyFinding finding = null;
            foreach (var entry in group)
            {
                string status = entry.item?.Id;
                if (!BuildingSupplyFinding.IsShortage(status)) continue;
                if (finding == null)
                    finding = new BuildingSupplyFinding {
                        Id = go.GetComponent<KPrefabID>()?.InstanceID ?? go.GetInstanceID(),
                        WorldId = go.GetMyWorldId(), X = Grid.CellColumn(cell), Y = Grid.CellRow(cell),
                        PrefabId = go.GetComponent<Building>()?.Def?.PrefabID ?? PrefabIdentity.BaseId(go.name),
                        Construction = construction
                    };
                finding.Statuses.Add(status);
                finding.Messages.Add(ToolUtil.CleanName(entry.GetName()));
                // Preserve native units: countable tags are units; other ingredients are kg.
                var amounts = entry.data as Dictionary<Tag, float>;
                if (entry.data is IFetchList fetch)
                    amounts = status == "MaterialsUnavailableForRefill" ? fetch.GetRemaining() : fetch.GetRemainingMinimum();
                if (amounts != null)
                    foreach (var pair in amounts)
                        if (pair.Value > 0)
                            finding.Missing[pair.Key.Name + (Assets.IsTagCountable(pair.Key) ? ":units" : ":kg")] = Math.Round(pair.Value, 2);
            }
            if (finding != null) result.Add(finding);
        }
    }
}
