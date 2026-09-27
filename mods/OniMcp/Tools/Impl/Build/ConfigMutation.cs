using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    internal static class ConfigMutation
    {
        // Call after resolving/validating the target, before any setter or visual callback.
        internal static CallToolResult Preview(JObject args, object target, object intended)
        {
            if (ToolUtil.GetBool(args, "dryRun", false))
                return CallToolResult.Text(JsonConvert.SerializeObject(new {
                    dryRun = true, changed = false, target, intended
                }));
            return ToolUtil.GetBool(args, "confirm", false) ? null : CallToolResult.Error("confirm=true is required");
        }
    }
}
