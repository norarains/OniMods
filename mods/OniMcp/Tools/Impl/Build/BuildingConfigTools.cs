using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class BuildingConfigTools
    {
        public static McpTool ControlBuildingConfig()
        {
            return new McpTool
            {
                Name = "building_config_control",
                Group = "buildings",
                Mode = "write",
                Risk = "dangerous",
                Aliases = new List<string> { "buildings_config_control", "building_side_screen_control" },
                Tags = new List<string> { "buildings", "config", "automation", "side-screen", "door", "access", "visual", "colors" },
                Description = "建筑配置组合工具：action=set_enabled/set_toggle/set_threshold/set_slider/set_valve_flow/set_limit_valve/set_logic_timer/set_logic_ribbon_bit/set_door_state/get_access/set_access/copy_settings/visual",
                Parameters = BuildingConfigControlParams(),
                Handler = args =>
                {
                    string operation = (args["action"]?.ToString() ?? args["operation"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    switch (operation)
                    {
                        case "set_enabled":
                        case "enabled":
                            return OrdersTools.SetBuildingEnabled().Handler(args);
                        case "set_toggle":
                        case "toggle":
                            return OrdersTools.SetBuildingToggle().Handler(args);
                        case "set_battery_thresholds":
                            return SetBatteryThresholds(args);
                        case "set_threshold":
                        case "threshold":
                            return SetThreshold().Handler(args);
                        case "set_slider":
                        case "slider":
                            return SetSlider().Handler(args);
                        case "set_valve_flow":
                        case "valve_flow":
                            return SetValveFlow().Handler(args);
                        case "set_limit_valve":
                        case "limit_valve":
                            return SetLimitValve().Handler(args);
                        case "set_logic_timer":
                        case "logic_timer":
                            return SetLogicTimer().Handler(args);
                        case "set_logic_ribbon_bit":
                        case "logic_ribbon_bit":
                            return SetLogicRibbonBit().Handler(args);
                        case "set_door_state":
                        case "door_state":
                            return SetDoorState().Handler(args);
                        case "get_access":
                        case "access_get":
                            return GetAccessControl().Handler(args);
                        case "set_access":
                        case "access_set":
                            return SetAccessControl().Handler(args);
                        case "copy_settings":
                        case "copy":
                            return CopySettings().Handler(args);
                        case "state_list":
                        case "list_state":
                            return ForwardStateControl(args, "list");
                        case "state_set":
                        case "set_state_control":
                            return ForwardStateControl(args, "set");
                        case "visual":
                        case "visual_control":
                            return ForwardVisualControl(args);
                        default:
                            return CallToolResult.Error("action must be set_enabled, set_toggle, set_threshold, set_slider, set_valve_flow, set_limit_valve, set_logic_timer, set_logic_ribbon_bit, set_door_state, get_access, set_access, copy_settings, state_list, state_set, or visual");
                    }
                }
            };
        }

        private static CallToolResult ForwardVisualControl(JObject args)
        {
            var forwarded = args == null ? new JObject() : (JObject)args.DeepClone();
            var visualAction = forwarded["visualAction"] ?? forwarded["visual_action"] ?? forwarded["visualOperation"] ?? forwarded["visual_operation"];
            forwarded["action"] = visualAction ?? "";
            forwarded.Remove("visualAction");
            forwarded.Remove("visual_action");
            forwarded.Remove("visualOperation");
            forwarded.Remove("visual_operation");
            return VisualControlTools.ControlVisual().Handler(forwarded);
        }

        private static CallToolResult ForwardStateControl(JObject args, string action)
        {
            var forwarded = args == null ? new JObject() : (JObject)args.DeepClone();
            forwarded["action"] = action;
            return StateControlTools.ControlState().Handler(forwarded);
        }

        public static McpTool SetThreshold()
        {
            return new McpTool
            {
                Name = "buildings_threshold_set",
                Group = "buildings",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "automation_threshold_set", "sensor_threshold_set" },
                Tags = new List<string> { "buildings", "automation", "sensor", "threshold", "slider" },
                Description = "兼容入口：请使用 building_control domain=config action=set_threshold",
                Hidden = true,
                Parameters = LookupParams(new Dictionary<string, McpToolParameter>
                {
                    ["threshold"] = new McpToolParameter { Type = "number", Description = "Native threshold (temperature: Kelvin); unit=C/F/K/display explicitly converts input.", Required = true },
                    ["unit"] = new McpToolParameter { Type = "string", Description = "Threshold input unit: native (default), K, C, F, or display.", Required = false },
                    ["activateAbove"] = new McpToolParameter { Type = "boolean", Description = "true 表示高于阈值激活，false 表示低于阈值激活", Required = true },
                    ["component"] = new McpToolParameter { Type = "string", Description = "同一对象有多个阈值组件时按组件类型名筛选", Required = false }
                }),
                Handler = args =>
                {
                    var go = FindTarget(args);
                    if (go == null)
                        return CallToolResult.Error("Target not found");

                    var threshold = FindThresholdSwitch(go, args["component"]?.ToString());
                    if (threshold == null)
                        return CallToolResult.Error("Target does not expose an IThresholdSwitch");

                    float? requested = ToolUtil.GetFloat(args, "threshold");
                    if (!requested.HasValue)
                        return CallToolResult.Error("threshold is required");

                    float processed;
                    try { processed = ThresholdValuePolicy.ToNative(requested.Value, args["unit"]?.ToString(),
                        threshold.GetType().Name.IndexOf("Temperature", StringComparison.OrdinalIgnoreCase) >= 0,
                        threshold.RangeMin, threshold.RangeMax, threshold.ProcessedInputValue); }
                    catch (ArgumentException ex) { return CallToolResult.Error(ex.Message); }

                    bool activateAbove = ToolUtil.GetBool(args, "activateAbove", true);
                    var preview = ConfigMutation.Preview(args, TargetInfo(go), new {
                        threshold = processed, unit = ThresholdValuePolicy.NativeUnit(threshold.GetType().Name), activateAbove });
                    if (preview != null) return preview;
                    threshold.Threshold = processed;
                    threshold.ActivateAboveThreshold = activateAbove;

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["target"] = TargetInfo(go),
                        ["threshold"] = ThresholdInfo(threshold),
                        ["changed"] = true
                    }, McpJsonUtil.Settings));
                }
            };
        }


    }
}
