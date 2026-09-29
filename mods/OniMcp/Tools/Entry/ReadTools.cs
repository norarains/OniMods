using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    internal static class ReadTools
    {
        internal static McpTool ControlRead()
        {
            return new McpTool
            {
                Name = "read_control",
                Group = "read",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "state_read_control" },
Tags = new List<string> { "read", "state", "world", "area", "resources", "infrastructure" },
Description = "Specialized world, area, inventory, food, room and circuit diagnostics. For object facts use server_control domain=query.",
                Parameters = Parameters(),
                Handler = HandleRead
            };
        }

        private static Dictionary<string, McpToolParameter> Parameters()
        {
            return new Dictionary<string, McpToolParameter>
            {
                ["domain"] = new McpToolParameter
                {
                    Type = "string",
Description = "state, world, area, resources, or infrastructure.",
                    Required = true,
EnumValues = new List<string> { "state", "world", "area", "resources", "infrastructure" }
                },
                ["action"] = new McpToolParameter
                {
                    Type = "string",
Description = "state=current/current_state/overview; world=text_map/search/layout_candidates/reachable_area (exact cells: world_editor command=read path=/active/map/cell_X_Y.md); area=define/get/list/blocks/merge/forget; resources=inventory/food/pins/set_pin; infrastructure=power_summary/rooms.",
                    Required = true
                },
                ["type"] = new McpToolParameter { Type = "string", Description = "Alias subtype filter used by some child actions.", Required = false },
                ["id"] = new McpToolParameter { Type = "string", Description = "Object/resource/area id depending on domain.", Required = false },
                ["query"] = new McpToolParameter { Type = "string", Description = "Search or filter query.", Required = false },
                ["target"] = new McpToolParameter { Type = "string", Description = "Alias for query.", Required = false },
                ["search"] = new McpToolParameter { Type = "string", Description = "Alias for query.", Required = false },
                ["pattern"] = new McpToolParameter { Type = "string", Description = "world search sequence pattern, e.g. Dirt-Oxygen or 粉砂岩-泥土-氧气.", Required = false },
                ["sequence"] = new McpToolParameter { Type = "string", Description = "Alias sequence/pattern text for search-like actions.", Required = false },
                ["category"] = new McpToolParameter { Type = "string", Description = "Optional category filter for resource child actions.", Required = false },
                ["detail"] = new McpToolParameter { Type = "string", Description = "brief/full/compact detail level depending on child action.", Required = false },
                ["profile"] = new McpToolParameter { Type = "string", Description = "Map/snapshot output profile such as scan, minimal, or standard.", Required = false },
                ["format"] = new McpToolParameter { Type = "string", Description = "Requested output format, e.g. json, markdown, edit, raw.", Required = false },
                ["view"] = new McpToolParameter { Type = "string", Description = "Requested map/infrastructure view such as power, temperature, oxygen, liquid_conduits.", Required = false },
                ["compact"] = new McpToolParameter { Type = "boolean", Description = "Use compact token-saving map/table output when supported.", Required = false },
                ["syncView"] = new McpToolParameter { Type = "boolean", Description = "Opt in to changing the player overlay during map reads; default false.", Required = false },
                ["includeItems"] = new McpToolParameter { Type = "boolean", Description = "Include pickupables/dropped items when supported.", Required = false },
                ["includeBuildings"] = new McpToolParameter { Type = "boolean", Description = "Include building anchors/details when supported.", Required = false },
                ["includeDupes"] = new McpToolParameter { Type = "boolean", Description = "Include duplicants when supported.", Required = false },
                ["includeDetails"] = new McpToolParameter { Type = "boolean", Description = "Include detailed records for summary actions.", Required = false },
                ["includeReachability"] = new McpToolParameter { Type = "boolean", Description = "For world cell_info/cell_detail, include duplicant reachability to the target cell.", Required = false },

 ["includeLogs"] = new McpToolParameter { Type = "boolean", Description = "For state/current, include compact Player.log error tail. Default false to save tokens.", Required = false },
 ["includeLogErrors"] = new McpToolParameter { Type = "boolean", Description = "Alias for includeLogs; returns only suspicious recent log lines.", Required = false },
 ["logLimit"] = new McpToolParameter { Type = "integer", Description = "For includeLogs: tail lines scanned from Player.log. Default 160, max 500.", Required = false },
                ["worldId"] = new McpToolParameter { Type = "integer", Description = "World id. Defaults to active world or area-bound world.", Required = false },
                ["x"] = new McpToolParameter { Type = "integer", Description = "Cell X for point queries or alias for x1 in area actions.", Required = false },
                ["y"] = new McpToolParameter { Type = "integer", Description = "Cell Y for point queries or alias for y1 in area actions.", Required = false },
                ["x1"] = new McpToolParameter { Type = "integer", Description = "Area lower/left X.", Required = false },
                ["y1"] = new McpToolParameter { Type = "integer", Description = "Area lower Y.", Required = false },
                ["x2"] = new McpToolParameter { Type = "integer", Description = "Area upper/right X.", Required = false },
                ["y2"] = new McpToolParameter { Type = "integer", Description = "Area upper Y.", Required = false },
                ["areaId"] = new McpToolParameter { Type = "string", Description = "Reusable area handle.", Required = false },
                ["areaIds"] = new McpToolParameter { Type = "array", Description = "Area handle list for merge-like actions.", Required = false },
                ["label"] = new McpToolParameter { Type = "string", Description = "Optional area label.", Required = false },
                ["blockWidth"] = new McpToolParameter { Type = "integer", Description = "area blocks target width.", Required = false },
                ["blockHeight"] = new McpToolParameter { Type = "integer", Description = "area blocks target height.", Required = false },
                ["maxCells"] = new McpToolParameter { Type = "integer", Description = "Maximum map/area cells to return.", Required = false },
                ["limit"] = new McpToolParameter { Type = "integer", Description = "Maximum returned items.", Required = false },

                ["direction"] = new McpToolParameter { Type = "string", Description = "Search direction when supported.", Required = false },
                ["matchMode"] = new McpToolParameter { Type = "string", Description = "Search matching mode when supported.", Required = false },
                ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "Preview-only flag for child actions that support it.", Required = false },
                ["confirm"] = new McpToolParameter { Type = "boolean", Description = "Required for read_control resource write actions such as set_pin.", Required = false }
            };
        }

        private static CallToolResult HandleRead(JObject args)
        {
            args = args ?? new JObject();
            string domain = (args["domain"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            string action = (args["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            OniMcp.Support.OniMcpLog.Debug($"[OniMcp] read_control: domain={domain}, action={action}");

switch (domain)
{
case "state":
case "current":
case "overview":
return CurrentStateReadTools.ReadCurrent(args);
case "world":
if (action == "current" || action == "current_state" || action == "overview")
return CurrentStateReadTools.ReadCurrent(args);
return WorldAnalysisTools.ReadWorldControl().Handler(args);
                case "area":
                    return AreaTools.ControlArea().Handler(args);
                case "resources":
                    return InventoryTools.ControlResources().Handler(args);
                case "infrastructure":
                    return PowerAndRoomTools.InfrastructureReadControl().Handler(args);
                case "knowledge":
                case "database":
                case "guide":
                case "mechanics":
                case "mechanic":
                    return ForwardKnowledge(args);
                default:
                    return CallToolResult.Error("domain must be state, world, area, resources, or infrastructure");
            }
        }

        private static CallToolResult ForwardKnowledge(JObject args)
        {
            return CallToolResult.Error("read_control domain=knowledge/database/guide is disabled because in-game database queries are crash-prone at runtime. Use external docs or static repo data instead.");
        }
    }
}
