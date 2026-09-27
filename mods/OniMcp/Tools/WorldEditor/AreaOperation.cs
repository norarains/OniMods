using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    internal static class AreaOperation
    {
        internal static JObject Parse(string line)
        {
            var args = OperationArguments.Parse(line);
            var allowed = new[] { "x1", "y1", "x2", "y2", "worldId", "label", "task", "dryRun", "confirm" };
            foreach (var property in args.Properties())
                if (!allowed.Contains(property.Name)) throw new ArgumentException("Unknown area argument: " + property.Name);
            foreach (string key in new[] { "x1", "y1", "x2", "y2" })
                if (args[key]?.Type != JTokenType.Integer || args[key].Value<long>() < int.MinValue || args[key].Value<long>() > int.MaxValue)
                    throw new ArgumentException("area requires integer x1/y1/x2/y2");
            if (args["worldId"] != null && (args["worldId"].Type != JTokenType.Integer
                || args["worldId"].Value<long>() < 0 || args["worldId"].Value<long>() > int.MaxValue))
                throw new ArgumentException("worldId must be a nonnegative integer");
            foreach (string key in new[] { "label", "task" })
                if (args[key] != null && args[key].Type != JTokenType.String)
                    throw new ArgumentException(key + " must be text");
            foreach (string key in new[] { "dryRun", "confirm" })
                if (args[key] != null && args[key].Type != JTokenType.Boolean)
                    throw new ArgumentException(key + " must be boolean");
            args["domain"] = "area";
            args["action"] = "define";
            return args;
        }
    }
}
