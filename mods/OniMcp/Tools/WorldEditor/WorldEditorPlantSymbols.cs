using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static void AddRuntimePlantSymbolRows(List<JObject> rows)
        {
            if (!RuntimeDatabaseReady || Game.Instance == null) return;
            var seen = new HashSet<string>();
            foreach (var plant in Components.HarvestDesignatables.Items)
            {
                if (plant == null) continue;
                var prefab = plant.GetComponent<KPrefabID>();
                if (prefab == null || !seen.Add(prefab.PrefabTag.Name)) continue;
                string name = ToolUtil.CleanName(plant.gameObject.GetProperName());
                rows.Add(GlyphRow("Plant", prefab.PrefabTag.Name, name, name,
                    "Plant anchor label; use the map's prefab/InstanceID for exact targeting."));
            }
        }
    }
}
