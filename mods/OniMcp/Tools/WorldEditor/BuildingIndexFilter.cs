using System;

namespace OniMcp.Tools
{
    internal static class BuildingIndexFilter
    {
        internal static string Category(string prefab)
        {
            prefab = prefab ?? "";
            if (prefab.StartsWith("Prop", StringComparison.OrdinalIgnoreCase)
                || prefab.StartsWith("POI", StringComparison.OrdinalIgnoreCase)
                || prefab.StartsWith("Gravitas", StringComparison.OrdinalIgnoreCase)
                || prefab.IndexOf("TilePOI", StringComparison.OrdinalIgnoreCase) >= 0)
                return "poi";
            if (prefab.IndexOf("Conduit", StringComparison.OrdinalIgnoreCase) >= 0
                || prefab.IndexOf("Wire", StringComparison.OrdinalIgnoreCase) >= 0)
                return "utility";
            if (prefab.IndexOf("Tile", StringComparison.OrdinalIgnoreCase) >= 0
                || prefab.IndexOf("Ladder", StringComparison.OrdinalIgnoreCase) >= 0
                || prefab.IndexOf("Door", StringComparison.OrdinalIgnoreCase) >= 0)
                return "structure";
            return "facility";
        }

        internal static bool Matches(string prefab, string name, string query, string category, bool includePoi)
        {
            string actual = Category(prefab);
            if (!includePoi && actual == "poi") return false;
            if (!string.IsNullOrWhiteSpace(category) && !actual.Equals(category, StringComparison.OrdinalIgnoreCase))
                return false;
            return string.IsNullOrWhiteSpace(query)
                || (prefab ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                || (name ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
