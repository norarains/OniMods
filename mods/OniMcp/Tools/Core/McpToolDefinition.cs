using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    /// <summary>
    /// Tool 定义
    /// </summary>
    public class McpTool
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string Group { get; set; }
        public string Mode { get; set; }
        public string Risk { get; set; }
        public bool Hidden { get; set; }
        public List<string> Aliases { get; set; }
        public List<string> Tags { get; set; }
        public Dictionary<string, McpToolParameter> Parameters { get; set; }
        public Func<JObject, CallToolResult> Handler { get; set; }
    }

    public class McpToolParameter
    {
        public string Type { get; set; }
        public string Description { get; set; }
        public bool Required { get; set; }
        public List<string> EnumValues { get; set; }
        public string McpHeader { get; set; }
        public SchemaProperty Items { get; set; }

        public List<object> SchemaEnumValues
        {
            get
            {
                if (EnumValues == null)
                    return null;

                var values = new List<object>();
                foreach (var value in EnumValues)
                {
                    if (Type == "integer")
                    {
                        int intValue;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out intValue))
                        {
                            values.Add(intValue);
                            continue;
                        }
                    }
                    else if (Type == "number")
                    {
                        double numberValue;
                        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out numberValue))
                        {
                            values.Add(numberValue);
                            continue;
                        }
                    }

                    values.Add(value);
                }

                return values;
            }
        }
    }

    internal static class ToolMetadata
    {
        private const string LegacyAliasRemovalVersion = "0.3.0";

        public static void ApplyDefaults(McpTool tool)
        {
            if (string.IsNullOrEmpty(tool.Group))
                tool.Group = InferGroup(tool.Name);
            if (string.IsNullOrEmpty(tool.Mode))
                tool.Mode = InferMode(tool.Name);
            if (string.IsNullOrEmpty(tool.Risk))
                tool.Risk = InferRisk(tool.Name);
            if (tool.Parameters == null)
                tool.Parameters = new Dictionary<string, McpToolParameter>();
            if (tool.Aliases == null)
                tool.Aliases = new List<string>();
            if (tool.Tags == null)
                tool.Tags = new List<string>();
        }

        public static string FormatDescription(McpTool tool)
        {
            return $"[{tool.Group}/{tool.Mode}/{tool.Risk}] {tool.Description}";
        }

        public static bool HasLegacyAliases(McpTool tool)
        {
            return tool?.Aliases != null && tool.Aliases.Count > 0;
        }

        public static string LegacyAliasesDeprecationWarning(McpTool tool)
        {
            return $"DEPRECATED: legacy aliases for '{tool.Name}' will be removed in {LegacyAliasRemovalVersion}; use '{tool.Name}' directly.";
        }

        public static CallToolResult AddLegacyAliasDeprecationWarning(CallToolResult result, string alias, string canonicalName)
        {
            if (result == null)
                result = CallToolResult.Text(string.Empty);
            if (result.Content == null)
                result.Content = new List<ToolContent>();

            result.Content.Insert(0, new ToolContent
            {
                Text = $"DEPRECATED: legacy tool name/alias '{alias}' will be removed in {LegacyAliasRemovalVersion}; use '{canonicalName}' instead."
            });
            return result;
        }
        private static string InferGroup(string name)
        {
            name = (name ?? "").ToLowerInvariant();

            if (name.StartsWith("tools_")) return "tools";
            if (name.StartsWith("server_") || name.StartsWith("logs_") || name.StartsWith("mcp_") || name.Contains("mcp")) return "server";
            if (name.StartsWith("database_")) return "database";
            if (name.StartsWith("research_")) return "research";
            if (name.StartsWith("ui_")) return "ui";
            if (name.StartsWith("map_")) return "map";
            if (name.StartsWith("sandbox_") || name.StartsWith("debug_")) return "sandbox";
            if (name.StartsWith("rocket") || name.StartsWith("launch_") || name.StartsWith("assignment_group_") || name.Contains("spacecraft")) return "rockets";
            if (name.StartsWith("space_") || name.StartsWith("starmap_") || name.StartsWith("temporal_") || name.StartsWith("warp_")) return "space";
            if (name.StartsWith("story_") || name.StartsWith("lore_") || name.StartsWith("printerceptor") || name.StartsWith("remote_work_") || name.StartsWith("artifact_")) return "story";
            if (name.StartsWith("diet_")) return "diet";
            if (name.StartsWith("game_") || name.Contains("speed") || name.Contains("pause")) return "game";
            if (name.StartsWith("camera_")) return "camera";
            if (name.StartsWith("dupe") || name.StartsWith("assignable") || name.StartsWith("minion_") || name.StartsWith("bionic_") || name.Contains("duplicant")) return "dupes";
            if (name.StartsWith("schedule_")) return "schedules";
            if (name.StartsWith("resources_") || name.StartsWith("storage_") || name.StartsWith("receptacle") || name.Contains("inventory") || name.Contains("food") || name.Contains("resources")) return "resources";
            if (name.StartsWith("filters_")) return "filters";
            if (name.StartsWith("automation_") || name.StartsWith("automatable_") || name.StartsWith("logic_") || name.StartsWith("critter_sensor") || name.StartsWith("comet_detector") || name.StartsWith("cluster_location_sensor")) return "automation";
            if (name.StartsWith("side_") || name.StartsWith("state_") || name.StartsWith("direction_") || name.StartsWith("few_option_") || name.StartsWith("capacity_") || name.StartsWith("checkbox_") || name.StartsWith("time_range_") || name.StartsWith("activation_") || name.StartsWith("progress_") || name.StartsWith("user_menu_") || name.StartsWith("maintenance_") || name.StartsWith("related_") || name.StartsWith("n_toggle")) return "controls";
            if (name.StartsWith("building") || name.StartsWith("buildings_") || name.StartsWith("doors_") || name.StartsWith("access_control_") || name.StartsWith("lights_") || name.StartsWith("pixel_") || name.StartsWith("geo_") || name.StartsWith("dispenser") || name.StartsWith("suit_locker") || name.StartsWith("telepad") || name.Contains("building")) return "buildings";
            if (name.StartsWith("production_") || name.StartsWith("configurable_consumer") || name.StartsWith("mutant_seed")) return "production";
            if (name.StartsWith("orders_") || name.StartsWith("priorities_") || name.StartsWith("conduits_") || name.StartsWith("plants_uproot") || name.Contains("dig") || name.Contains("sweep") || name.Contains("deconstruct")) return "orders";
            if (name.StartsWith("critters_") || name.StartsWith("incubator") || name.StartsWith("creature_lure")) return "ranching";
            if (name.StartsWith("farming_")) return "farming";
            if (name.StartsWith("medical_") || name.StartsWith("doctor_")) return "medical";
            if (name.StartsWith("power_")) return "power";
            if (name.StartsWith("rooms_")) return "rooms";
            if (name.StartsWith("world_") || name.StartsWith("area_") || name.StartsWith("layout_") || name.StartsWith("thermal_") || name.Contains("cell")) return "world";
            if (name.StartsWith("notification") || name.StartsWith("colony_") || name.Contains("colony") || name.Contains("alerts")) return "colony";
            return "misc";
        }

        private static string InferMode(string name)
        {
            if (name.Contains("set_") || name.Contains("rename") || name.Contains("assign") || name.Contains("deconstruct") || name.Contains("sweep") || name.Contains("dig"))
                return "write";
            if (name.Contains("pause") || name.Contains("resume") || name.Contains("speed") || name.Contains("screenshot") || name.Contains("focus"))
                return "execute";
            return "read";
        }

        private static string InferRisk(string name)
        {
            if (name.Contains("deconstruct") || name.Contains("dig"))
                return "dangerous";
            if (name.Contains("rename") || name.Contains("assign") || name.Contains("set_") || name.Contains("sweep") || name.Contains("launch") || name.Contains("cancel"))
                return "medium";
            if (name.Contains("pause") || name.Contains("resume") || name.Contains("speed") || name.Contains("focus") || name.Contains("screenshot"))
                return "low";
            return "none";
        }
    }
}
