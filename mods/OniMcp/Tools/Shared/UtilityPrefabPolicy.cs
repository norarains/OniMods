using System;
using System.Collections.Generic;

namespace OniMcp.Tools
{
    internal static class UtilityPrefabPolicy
    {
        private static readonly Dictionary<string, string> Families = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Wire"] = "Wire", ["WireRefined"] = "Wire",
            ["HighWattageWire"] = "Wire", ["WireRefinedHighWattage"] = "Wire",
            ["GasConduit"] = "GasConduit", ["InsulatedGasConduit"] = "GasConduit", ["GasConduitRadiant"] = "GasConduit",
            ["LiquidConduit"] = "LiquidConduit", ["InsulatedLiquidConduit"] = "LiquidConduit", ["LiquidConduitRadiant"] = "LiquidConduit",
            ["SolidConduit"] = "SolidConduit", ["LogicWire"] = "LogicWire", ["LogicRibbon"] = "LogicWire"
        };

        internal static bool IsLinear(string prefabId) => prefabId != null && Families.ContainsKey(prefabId);

        internal static bool SameFamily(string first, string second) => first != null && second != null
            && Families.TryGetValue(first, out string a) && Families.TryGetValue(second, out string b) && a == b;

        internal static bool TrySelect(string layerDefault, string requested, out string selected)
        {
            selected = string.IsNullOrWhiteSpace(requested) ? layerDefault : requested.Trim();
            return selected != null && Families.TryGetValue(selected, out string family)
                && string.Equals(family, layerDefault, StringComparison.OrdinalIgnoreCase);
        }
    }
}
