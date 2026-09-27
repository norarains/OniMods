using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    internal static class AreaDefinition
    {
        internal static CallToolResult Define(JObject args)
        {
            foreach (string key in new[] { "x1", "y1", "x2", "y2" })
                if (args[key]?.Type != JTokenType.Integer || !ToolUtil.GetInt(args, key).HasValue)
                    return CallToolResult.Error("area define requires integer x1/y1/x2/y2");
            if (args["worldId"] != null && (args["worldId"].Type != JTokenType.Integer
                || !ToolUtil.GetInt(args, "worldId").HasValue || ToolUtil.GetInt(args, "worldId").Value < 0))
                return CallToolResult.Error("worldId must be a nonnegative integer");
            if (args["areaId"] != null || args["relative"] != null || args["rel"] != null)
                return CallToolResult.Error("Define an area with absolute rectangle coordinates only");
            int worldId = ToolUtil.ResolveWorldId(args);
            int x1 = ToolUtil.GetInt(args, "x1").Value, x2 = ToolUtil.GetInt(args, "x2").Value;
            int y1 = ToolUtil.GetInt(args, "y1").Value, y2 = ToolUtil.GetInt(args, "y2").Value;
            // ResolveRect clamps to the world bounds; a definition must instead
            // reject a misspecified rectangle without silently changing its scope.
            var rect = new Dictionary<string, int> {
                ["x1"] = Math.Min(x1, x2), ["x2"] = Math.Max(x1, x2),
                ["y1"] = Math.Min(y1, y2), ["y2"] = Math.Max(y1, y2)
            };
            for (int y = rect["y1"]; ; y++)
            {
                if (y < 0 || y >= Grid.HeightInCells || rect["x1"] < 0 || rect["x2"] >= Grid.WidthInCells)
                    return CallToolResult.Error("Area must be inside the selected world");
                for (int x = rect["x1"]; x <= rect["x2"]; x++)
                    if (!ToolUtil.CellMatchesWorld(Grid.XYToCell(x, y), worldId))
                        return CallToolResult.Error("Area must be inside the selected world");
                if (y == rect["y2"]) break;
            }
            bool preview = ToolUtil.GetBool(args, "dryRun", false);
            if (preview)
                return CallToolResult.Text(JsonConvert.SerializeObject(new {
                    dryRun = true, committed = false, worldId, rect, label = args["label"]?.ToString()
                }));
            var handle = AreaHandleRegistry.Define(rect, worldId, args["label"]?.ToString());
            var result = handle.ToDictionary();
            result["committed"] = true;
            return CallToolResult.Text(JsonConvert.SerializeObject(result));
        }
    }
}
