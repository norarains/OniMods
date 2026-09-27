using System.Linq;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static string LineConstructionState(int cell, ObjectLayer[] layers)
        {
            var objects = layers.Select(layer => Grid.Objects[cell, (int)layer]).Where(PlayerVisibility.Object).Distinct();
            var states = objects.Select(go => (go.GetComponent<Constructable>() != null ? "planned" : "built")
                + ":" + (go.GetComponent<KPrefabID>()?.InstanceID ?? go.GetInstanceID())).ToArray();
            return "objects=" + (states.Length == 0 ? "." : string.Join(",", states));
        }
    }
}
