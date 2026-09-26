using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    internal static class StorageListFilter
    {
        internal static IEnumerable<T> Apply<T>(IEnumerable<T> items, JObject args,
            Func<T, int> getId, Func<T, int> getWorld, Func<T, string, bool> matchesIdentity,
            Func<T, string, bool> matchesResource, int limit)
        {
            int? id = ToolUtil.GetInt(args, "id");
            int? worldId = ToolUtil.GetInt(args, "worldId");
            string query = (args["query"] ?? args["name"])?.ToString()?.Trim();
            string resource = args["resource"]?.ToString()?.ToLowerInvariant();
            return items.Where(item => !worldId.HasValue || getWorld(item) == worldId.Value)
                .Where(item => !id.HasValue || getId(item) == id.Value)
                .Where(item => string.IsNullOrEmpty(query) || matchesIdentity(item, query))
                .Where(item => string.IsNullOrEmpty(resource) || matchesResource(item, resource))
                .Take(limit);
        }
    }
}
