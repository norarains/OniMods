using System;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static CallToolResult ApplyBuildEdit(JObject args, string relative, string replacement)
        {
            args["domain"] = "planning";
            args["plan"] = replacement.Trim();
            bool connectionFile = relative.StartsWith("infrastructure/", StringComparison.Ordinal);
            if (connectionFile && !TryApplyInfrastructurePlan(args, relative, replacement, out string error))
                return CallToolResult.Error(error);
            // The typed file selects the operation. Bridge and joint-plate prefab
            // names can contain "Wire" without describing a linear connection path.
            if (connectionFile)
                args["action"] = "auto_connect";
            else
                args["action"] = ToolUtil.GetBool(args, "confirm", false) ? "build_area" : "parse_plan";
            return BuildingControlTools.ControlBuildingFromVirtualFile(args);
        }

        private static CallToolResult PreflightBuildEdit(JObject args, string relative, string replacement)
        {
            var preview = (JObject)args.DeepClone();
            preview["domain"] = "planning";
            preview["plan"] = replacement.Trim();
            preview["dryRun"] = true;
            preview["confirm"] = false;
            bool connectionFile = relative.StartsWith("infrastructure/", StringComparison.Ordinal);
            if (connectionFile
                && !TryApplyInfrastructurePlan(preview, relative, replacement, out string error))
                return CallToolResult.Error(error);
            preview["action"] = connectionFile ? "auto_connect" : "build_area";
            return PromoteWorldEditorFailure(BuildingControlTools.ControlBuildingFromVirtualFile(preview));
        }

        private static bool IsEditableBuildCommandFile(string relative)
        {
            return relative == "buildings/plans.oni"
                || relative == "infrastructure/power.oni"
                || relative == "infrastructure/liquid_conduits.oni"
                || relative == "infrastructure/gas_conduits.oni"
                || relative == "infrastructure/logic.oni"
                || relative == "infrastructure/solid_conveyor.oni";
        }

        private static bool TryApplyInfrastructurePlan(JObject args, string relative, string replacement, out string error)
        {
            error = null;
            string plan = (replacement ?? string.Empty).Trim();
            const string fullPattern = @"^connect\s+\(\s*-?\d+\s*,\s*-?\d+\s*\)(?:\s*(?:->|→)\s*\(\s*-?\d+\s*,\s*-?\d+\s*\)){1,}$";
            if (!Regex.IsMatch(plan, fullPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                error = "Infrastructure plans must use exactly: connect (x1,y1) -> (x2,y2) [-> (x3,y3) ...]";
                return false;
            }

            var points = new JArray();
            foreach (Match match in Regex.Matches(plan, @"\(\s*(-?\d+)\s*,\s*(-?\d+)\s*\)"))
            {
                if (!int.TryParse(match.Groups[1].Value, out int x)
                    || !int.TryParse(match.Groups[2].Value, out int y))
                {
                    error = "Infrastructure plan coordinates must be 32-bit integers";
                    return false;
                }
                points.Add(new JArray(x, y));
            }
            if (points.Count < 2)
            {
                error = "Infrastructure plans require at least two explicit points";
                return false;
            }

            string requested = args["prefabId"]?.ToString();
            string fallback = PrefabForConnectionMap(relative);
            if (!UtilityPrefabPolicy.TrySelect(fallback, requested, out string selected))
            { error = "Requested prefabId is not a supported linear utility for this infrastructure layer."; return false; }
            args["prefabId"] = selected;
            args["points"] = points;
            args["material"] = args["material"] ?? "auto";
            return true;
        }

    }
}
