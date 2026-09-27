using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    internal static class ObjectReadFacts
    {
        internal static bool MatchesId(GameObject go, JObject args)
        {
            int? id = ToolUtil.GetInt(args, "id");
            return go != null && (!id.HasValue || (go.GetComponent<KPrefabID>()?.InstanceID ?? go.GetInstanceID()) == id.Value);
        }

        internal static Dictionary<string, object> Read(GameObject go)
        {
            int cell = Grid.PosToCell(go);
            var def = go.GetComponent<Building>()?.Def;
            var operational = go.GetComponent<Operational>();
            var status = new List<object>();
            var group = go.GetComponent<KSelectable>()?.GetStatusItemGroup();
            if (group != null)
                foreach (var entry in group)
                    status.Add(new { id = entry.item?.Id, text = ToolUtil.CleanName(entry.GetName()) });
            var priority = go.GetComponent<Prioritizable>();
            return new Dictionary<string, object> {
                ["id"] = go.GetComponent<KPrefabID>()?.InstanceID ?? go.GetInstanceID(),
                ["name"] = ToolUtil.CleanName(go.GetProperName()),
                ["prefabId"] = def?.PrefabID ?? go.GetComponent<KPrefabID>()?.PrefabTag.Name ?? go.name,
                ["position"] = new { x = Grid.CellColumn(cell), y = Grid.CellRow(cell) },
                ["worldId"] = go.GetMyWorldId(),
                ["blueprint"] = go.GetComponent<Constructable>() != null,
                ["orientation"] = go.GetComponent<Rotatable>()?.GetOrientation().ToString() ?? "Neutral",
                ["isOperational"] = operational?.IsOperational,
                ["isActive"] = operational?.IsActive,
                ["statuses"] = status,
                ["prioritizable"] = priority != null && priority.IsPrioritizable(),
                ["work"] = go.GetComponents<Workable>().Select(work => new {
                    type = work.GetType().Name, secondsRemaining = Math.Round(ToolUtil.SafeFloat(work.WorkTimeRemaining), 2)
                }).ToArray()
            };
        }
    }

    public static partial class GameControlTools
    {
        private static IEnumerable<GameObject> BuildingReadCandidates(bool includePlanned)
        {
            var seen = new HashSet<int>();
            foreach (var building in Components.BuildingCompletes.Items)
                if (building != null && seen.Add(building.gameObject.GetInstanceID())) yield return building.gameObject;
            foreach (var geyser in UnityEngine.Object.FindObjectsByType<Geyser>(FindObjectsSortMode.None))
                if (geyser != null && seen.Add(geyser.gameObject.GetInstanceID())) yield return geyser.gameObject;
            if (includePlanned)
                foreach (var blueprint in UnityEngine.Object.FindObjectsByType<Constructable>(FindObjectsSortMode.None))
                    if (blueprint != null && seen.Add(blueprint.gameObject.GetInstanceID())) yield return blueprint.gameObject;
        }
    }
}
