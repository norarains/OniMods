using HarmonyLib;

namespace OniMcp.Tools
{
    // Only MCP-created blueprints opt in. Copy the current priority, including
    // any later player edit, when native construction emits NewConstruction.
    internal static class ConstructionPriorityIntent
    {
        private static readonly Tag Intent = new Tag("OniMcpPreserveConstructionPriority");

        internal static void Mark(UnityEngine.GameObject blueprint)
        {
            if (blueprint?.GetComponent<Constructable>() != null)
                blueprint.GetComponent<KPrefabID>()?.AddTag(Intent, true);
        }

        [HarmonyPatch(typeof(BuildingComplete), "OnPrefabInit")]
        private static class Install
        {
            private static void Postfix(BuildingComplete __instance)
            {
                __instance.Subscribe((int)GameHashes.NewConstruction, data => {
                    var source = data as Constructable;
                    if (source?.GetComponent<KPrefabID>()?.HasTag(Intent) != true) return;
                    var from = source.GetComponent<Prioritizable>();
                    var to = __instance.GetComponent<Prioritizable>();
                    if (from != null && to != null && to.IsPrioritizable())
                        to.SetMasterPriority(from.GetMasterPriority());
                });
            }
        }
    }
}
