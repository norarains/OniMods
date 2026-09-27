using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class PowerAndRoomTools
    {
        public static McpTool InfrastructureReadControl()
        {
            return new McpTool
            {
                Name = "infrastructure_read_control",
                Group = "infrastructure",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "infrastructure_read" },
                Tags = new List<string> { "power", "electricity", "rooms", "infrastructure", "电力", "房间", "基础设施" },
                Description = "电力/房间只读聚合入口：action=power_summary/rooms；ports 返回电力、液管、气管、信号、运输端口。",
                Parameters = InfrastructureReadParams(),
                Handler = args =>
                {
                    args = args ?? new JObject();
                    string action = (args["action"]?.ToString() ?? "").Trim().ToLowerInvariant();
                    var forwarded = new JObject(args);
                    forwarded.Remove("action");

                    switch (action)
                    {
                        case "power_summary":
                            return GetPowerSummary().Handler(forwarded);
                        case "rooms":
                            return ListRooms().Handler(forwarded);
                        default:
                            return CallToolResult.Error("Unsupported action. Use power_summary, or rooms.");
                    }
                }
            };
        }

        public static McpTool GetPowerSummaryCompat()
        {
            var tool = GetPowerSummary();
            tool.Hidden = true;
            tool.Description = "兼容入口：请使用 read_control domain=infrastructure action=power_summary。";
            return tool;
        }

        public static McpTool ListRoomsCompat()
        {
            var tool = ListRooms();
            tool.Hidden = true;
            tool.Description = "兼容入口：请使用 read_control domain=infrastructure action=rooms。";
            return tool;
        }


    }
}
