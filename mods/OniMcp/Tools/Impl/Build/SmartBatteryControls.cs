using System;
using OniMcp.Core;
using System.Collections.Generic;
using OniMcp.Support;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    public static partial class BuildingConfigTools
    {
        private static Dictionary<string, object> SmartBatteryInfo(BatterySmart battery) => new Dictionary<string, object> {
            // ONI's IActivationRangeTarget property names are reversed relative to green/red output.
            ["lowThreshold"] = battery.DeactivateValue, ["highThreshold"] = battery.ActivateValue,
            ["storedJ"] = Math.Round(battery.JoulesAvailable, 1), ["capacityJ"] = battery.Capacity,
            ["chargePercent"] = battery.Capacity > 0 ? Math.Round(100 * battery.JoulesAvailable / battery.Capacity, 1) : 0
        };

        internal static CallToolResult PreviewBatteryThresholds(JObject args)
        {
            var preview = (JObject)args.DeepClone();
            preview["dryRun"] = true;
            return SetBatteryThresholds(preview);
        }

        private static CallToolResult SetBatteryThresholds(JObject args)
        {
            var go = FindTarget(args);
            var battery = go?.GetComponent<BatterySmart>();
            if (battery == null) return CallToolResult.Error("Target has no smart battery thresholds");
            double low = args["lowThreshold"]?.Value<double>() ?? battery.DeactivateValue;
            double high = args["highThreshold"]?.Value<double>() ?? battery.ActivateValue;
            if (!BatteryThresholdPolicy.Valid(low, high))
                return CallToolResult.Error("Thresholds must be whole percentages with 0 <= lowThreshold <= highThreshold <= 100");
            bool dryRun = ToolUtil.GetBool(args, "dryRun", false);
            if (!dryRun && !ToolUtil.GetBool(args, "confirm", false)) return CallToolResult.Error("confirm=true is required");
            var before = SmartBatteryInfo(battery);
            if (!dryRun)
            {
                battery.ActivateValue = (float)high;
                battery.DeactivateValue = (float)low;
            }
            return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object> {
                ["target"] = TargetInfo(go), ["dryRun"] = dryRun, ["committed"] = !dryRun,
                ["before"] = before, ["after"] = SmartBatteryInfo(battery),
                ["projected"] = new { lowThreshold = low, highThreshold = high }
            }, McpJsonUtil.Settings));
        }
    }
}
