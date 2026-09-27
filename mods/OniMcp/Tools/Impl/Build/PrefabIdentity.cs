using System;

namespace OniMcp.Tools
{
    internal static class PrefabIdentity
    {
        internal static string BaseId(string id)
        {
            id = (id ?? "").Trim();
            if (id.EndsWith("(Clone)", StringComparison.Ordinal))
                id = id.Substring(0, id.Length - 7).TrimEnd();
            foreach (string suffix in new[] { "UnderConstruction", "Complete", "Preview" })
                if (id.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return id.Substring(0, id.Length - suffix.Length);
            return id;
        }
    }
}
