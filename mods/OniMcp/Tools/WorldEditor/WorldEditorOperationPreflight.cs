using System;
using OniMcp.Core;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static JObject PreviewOperation(string line, string tool, JObject args, bool coordinates)
        {
            var row = new JObject { ["line"] = line, ["tool"] = tool, ["ok"] = true,
                ["preview"] = true, ["validationLevel"] = "routing_and_syntax_only", ["arguments"] = args };
            if (!HasNativeOperationPreview(tool, args)) return row;
            var previewArgs = (JObject)args.DeepClone();
            previewArgs["dryRun"] = true; previewArgs["confirm"] = false;
            var result = OniToolRegistry.CallToolFromWorldEditor(tool, previewArgs, coordinates);
            row["validationLevel"] = "native_preflight";
            row["ok"] = !WorldEditorResultFailed(result, previewArgs);
            row["result"] = WorldEditorResponsePolicy.Body(result);
            return row;
        }

        // Explicitly audited dry-run handlers. An arbitrary schema flag is not proof of purity.
        private static bool HasNativeOperationPreview(string tool, JObject args)
        {
            string domain = args["domain"]?.ToString(), action = args["action"]?.ToString();
            if (tool == "building_control")
                return domain == "production" && new[] { "set", "batch" }.Contains(action)
                    || domain == "storage" && action == "set_filter"
                    || domain == "config" && action == "set_battery_thresholds"
                    || domain == "side_surface" && args["surface"]?.ToString() == "user_menu" && new[] { "press", "batch" }.Contains(action);
            if (tool == "colony_control")
                return domain == "management" && args["kind"]?.ToString() == "research" && action == "set"
                    || domain == "bio" && args["bioDomain"]?.ToString() == "farming"
                        && new[] { "set_planting", "batch_set_planting", "uproot", "set_harvestable" }.Contains(action);
            return tool == "dupes_control" && domain == "skill" && action == "learn";
        }

        private static CallToolResult PreviewOperations(string relative, List<Tuple<string, string, JObject, bool>> compiled)
        {
            var rows = new JArray(compiled.Select(item => PreviewOperation(item.Item1, item.Item2, item.Item3, item.Item4)));
            var body = new JObject { ["ok"] = rows.All(row => row["ok"].Value<bool>()), ["dryRun"] = true,
                ["committed"] = false, ["executed"] = 0, ["path"] = "/active/" + relative,
                ["validationLevel"] = rows.All(row => row["validationLevel"].ToString() == "native_preflight") ? "native_preflight" : "per_command",
                ["commands"] = rows };
            return body["ok"].Value<bool>() ? JsonResult(body) : CallToolResult.Error(JsonResultText(body));
        }
    }
}
