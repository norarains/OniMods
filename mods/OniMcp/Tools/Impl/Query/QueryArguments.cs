using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    internal static class QueryArguments
    {
        internal static void Validate(JObject args)
        {
            Only(args, "domain", "action", "query", "dataset", "task");
            string action = args["action"]?.ToString();
            if (action == "schema")
            {
                if (args["query"] != null) throw new ArgumentException("query is only for action=select");
            }
            else if (action == "select")
            {
                if (args["dataset"] != null) throw new ArgumentException("Use FROM in SELECT; dataset is only for schema");
                if (args["query"]?.Type != JTokenType.String) throw new ArgumentException("query must be a SELECT string");
            }
            else throw new ArgumentException("query action must be schema or select");
        }

        internal static string WarningSql(JObject args, int defaultWorld)
        {
            Only(args, "query", "worldId", "id", "areaId", "limit", "offset", "task", "syncView", "focusCamera");
            if (args["query"] != null)
            {
                if (args["query"].Type != JTokenType.String || string.IsNullOrWhiteSpace(args["query"].ToString()))
                    throw new ArgumentException("query must be a SELECT string");
                if (new[] { "worldId", "id", "areaId", "limit", "offset" }.Any(key => args[key] != null))
                    throw new ArgumentException("Do not combine query with warning selectors; put all filters and LIMIT/OFFSET in SELECT");
                return args["query"].ToString();
            }
            int world = Integer(args, "worldId", defaultWorld, -1, int.MaxValue);
            int limit = Integer(args, "limit", 20, 1, 200);
            int offset = Integer(args, "offset", 0, 0, 50000);
            string sql = "SELECT id, prefabId, position, statuses FROM building_warnings WHERE ";
            sql += args["areaId"] != null && args["worldId"] == null ? "id IS NOT NULL" : "worldId = " + world;
            if (args["id"] != null) sql += " AND id = " + Integer(args, "id", 0, int.MinValue, int.MaxValue);
            if (args["areaId"] != null)
            {
                if (args["areaId"].Type != JTokenType.String) throw new ArgumentException("areaId must be a string");
                sql += " AND in_area('" + args["areaId"].ToString().Replace("'", "''") + "')";
            }
            return sql + " LIMIT " + limit + " OFFSET " + offset;
        }

        private static void Only(JObject args, params string[] allowed)
        {
            foreach (var property in args.Properties())
                if (!allowed.Contains(property.Name) && !property.Name.StartsWith("_", StringComparison.Ordinal))
                    throw new ArgumentException("Unsupported query argument: " + property.Name + "; request the dataset schema");
        }

        private static int Integer(JObject args, string key, int fallback, int min, int max)
        {
            if (args[key] == null) return fallback;
            if (args[key].Type != JTokenType.Integer || !long.TryParse(args[key].ToString(), out long value) || value < min || value > max)
                throw new ArgumentException(key + " must be an integer in " + min + ".." + max);
            return (int)value;
        }
    }
}
