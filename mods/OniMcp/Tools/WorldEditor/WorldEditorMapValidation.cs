using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static CallToolResult ValidateMapChangesInGame(JObject args, List<MapEditCell> changes)
        {
            // Invoke execution's game validators with mutation disabled, not just the parser.
            var preview = (JObject)args.DeepClone();
            preview["dryRun"] = true;
            preview["confirm"] = false;
            preview["allowPartial"] = false;
            var checks = new JArray();
            foreach (var group in changes.GroupBy(ChangeKind))
            {
                var result = group.Key == "build"
                    ? ApplyBuildMapEdit(preview, group)
                    : ApplyOrderMapEdit(preview, group.Key, group);
                checks.Add(new JObject { ["kind"] = group.Key,
                    ["result"] = WorldEditorResponsePolicy.Body(result) });
                if (WorldEditorResultFailed(result))
                    return CallToolResult.Error(JsonResultText(new JObject
                    {
                        ["ok"] = false, ["phase"] = "preflight", ["applied"] = 0,
                        ["checks"] = checks
                    }));
            }
            return JsonResult(new JObject
            {
                ["ok"] = true,
                ["phase"] = "preflight",
                ["checks"] = checks,
                ["sourcePath"] = args["sourcePath"]?.ToString(),
                ["changedCells"] = changes.Count,
                ["kinds"] = new JArray(changes.GroupBy(ChangeKind).Select(group => new JObject
                {
                    ["kind"] = group.Key,
                    ["cells"] = group.Count()
                }))
            });
        }
    }
}
