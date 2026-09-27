using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;
namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        public static McpTool AutoConnectUtility()
        {
            return new McpTool
            {
                Name = "utility_auto_connect",
                Group = "buildings",
                Mode = "execute",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "build_auto_connect", "wire_auto_connect", "pipe_auto_connect", "logic_auto_connect" },
                Tags = new List<string> { "buildings", "utility", "wire", "pipe", "logic", "connect", "电线", "水管", "气管", "信号线" },
                Description = "兼容入口：请使用 building_control domain=planning action=auto_connect",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["type"] = new McpToolParameter { Type = "string", Description = "utility 类型：wire、liquid、gas、solid、logic；prefabId 留空时用它选择默认建筑", Required = false, EnumValues = new List<string> { "wire", "liquid", "gas", "solid", "logic" } },
                    ["prefabId"] = new McpToolParameter { Type = "string", Description = "可选建筑 prefabId，默认按 type 选择 Wire/LiquidConduit/GasConduit/SolidConduit/LogicWire", Required = false },
                    ["fromX"] = new McpToolParameter { Type = "integer", Description = "起点 X", Required = false },
                    ["fromY"] = new McpToolParameter { Type = "integer", Description = "起点 Y", Required = false },
                    ["toX"] = new McpToolParameter { Type = "integer", Description = "终点 X", Required = false },
                    ["toY"] = new McpToolParameter { Type = "integer", Description = "终点 Y", Required = false },
                    ["fromQuery"] = new McpToolParameter { Type = "string", Description = "可选起点搜索词；wire 自动连接时可搜索已有电源/电线/建筑", Required = false },
                    ["toQuery"] = new McpToolParameter { Type = "string", Description = "可选终点搜索词；wire 自动连接时搜索要接电的设备", Required = false },
                    ["maxAutoConnectRadius"] = new McpToolParameter { Type = "integer", Description = "wire 缺省起点时，围绕目标电口搜索已有电线/输出端口的半径，默认 80，最大 200", Required = false },
                    ["points"] = new McpToolParameter { Type = "array", Description = "可选折线路径点数组，支持 [[x,y],...] 或 [{x,y},...]；提供后优先使用", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "目标世界 ID，默认当前激活世界", Required = false },
                    ["material"] = new McpToolParameter { Type = "string", Description = "建造材料 tag；auto/default 自动选择", Required = false },
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "建造优先级 1..9，默认 5", Required = false },
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "仅预检，不生成蓝图", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "执行修改必须为 true；dryRun=true 时可省略", Required = false },
                    ["maxCells"] = new McpToolParameter { Type = "integer", Description = "最多处理路径格数，默认 200，最大 500", Required = false },
                    ["autoDigObstructions"] = new McpToolParameter { Type = "boolean", Description = "默认 true，遇到自然固体自动标记挖掘", Required = false },
                    ["autoUprootObstructions"] = new McpToolParameter { Type = "boolean", Description = "默认 true，遇到可铲植物自动标记铲除", Required = false },
                    ["maxAutoDigCells"] = new McpToolParameter { Type = "integer", Description = "最多自动标记挖掘/铲除格，默认 100，最大 500", Required = false }
                },
                Handler = args =>
                {
                    bool dryRun = IsDryRun(args);
                    if (!dryRun && !ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required unless dryRun=true");

                    string prefabId = args["prefabId"]?.ToString();
                    if (string.IsNullOrWhiteSpace(prefabId))
                        prefabId = DefaultUtilityPrefab(args["type"]?.ToString());
                    string resolvedPrefabId;
                    string resolveError;
                    var def = ResolveBuildingDef(prefabId, out resolvedPrefabId, out resolveError);
                    if (def == null)
                        return CallToolResult.Error(resolveError);
prefabId = resolvedPrefabId;
args["prefabId"] = prefabId;

string availabilityError = BuildAvailabilityError(def, args);
if (availabilityError != null)
return CallToolResult.Error(availabilityError);

if (!IsLinearUtilityPrefab(def.PrefabID))
return CallToolResult.Error("utility_auto_connect only supports linear utility prefabs such as Wire, LiquidConduit, GasConduit, SolidConduit and LogicWire");

                    int maxCells = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "maxCells") ?? 200, 500));
                    string error;
                    var path = ResolveUtilityPath(args, maxCells, out error);
                    if (error != null)
                        return CallToolResult.Error(error);

                    if (args["worldId"] == null)
                        args["worldId"] = ToolUtil.ResolveWorldId(args);
                    if (args["material"] == null)
                        args["material"] = "auto";
                    if (args["autoDigObstructions"] == null)
                        args["autoDigObstructions"] = true;
                    if (args["autoUprootObstructions"] == null)
                        args["autoUprootObstructions"] = true;
                    int worldId = ToolUtil.ResolveWorldId(args);
                    var pathSafety = ValidateUtilityPathSafety(def, path, worldId);
                    if (!pathSafety.Valid)
                        return CallToolResult.Error(JsonConvert.SerializeObject(
                            UtilityPathConflictResult(def, path, pathSafety, "path_preflight"), McpJsonUtil.Settings));
                    var pathMaterial = SelectElements(def, args["material"]?.ToString(), worldId);
                    pathMaterial.RequiredKg = RequiredMaterialKg(def) * Math.Max(0, path.Count - CountUtilityPathCells(def, path, worldId));
                    if (!pathMaterial.Valid || (!IsFreeBuildContext() && pathMaterial.Elements.Count == 1 && pathMaterial.Selected != null
                        && pathMaterial.RequiredKg > pathMaterial.Selected.AvailableKg))
                        return CallToolResult.Error(JsonConvert.SerializeObject(new Dictionary<string, object> {
                            ["valid"] = false, ["planned"] = 0, ["failed"] = 1, ["reasonCode"] = "insufficient_path_material",
                            ["materialSelection"] = pathMaterial.ToDictionary()
                        }, McpJsonUtil.Settings));
                    var results = new List<Dictionary<string, object>>();
                    var errors = new List<Dictionary<string, object>>();
                    var plannedSupportCells = new HashSet<int>();
                    var autoDigContext = AutoDigContext.FromArgs(args);
                    int planned = 0;
                    int reused = 0;
                    int valid = 0;
                    int autoMarked = 0;
                    foreach (var point in path)
                    {
                        var result = TryPlanOne(def.PrefabID, point.x, point.y, args, plannedSupportCells, autoDigContext);
                        bool ok = result.ContainsKey("planned") && (bool)result["planned"];
                        bool validPlacement = result.ContainsKey("valid") && (bool)result["valid"];
                    bool alreadyPresent = result.ContainsKey("alreadyPresent") && (bool)result["alreadyPresent"];
                    bool alreadyConnected = result.ContainsKey("alreadyConnected") && (bool)result["alreadyConnected"];
                        autoMarked += GetAutoDigInt(result, "marked") + GetAutoDigInt(result, "uprootMarked") + GetAutoDigInt(result, "alreadyMarked") + GetAutoDigInt(result, "alreadyUprootMarked");
                    if (ok || alreadyPresent || alreadyConnected || (dryRun && validPlacement) || IsAutoDigResult(result))
                    {
                        valid++;
                        if (ok)
                            planned++;
                        if (alreadyPresent || alreadyConnected)
                            reused++;
                    }
                        else
                        {
                            errors.Add(result);
                        }
                        results.Add(result);
                    }
                    bool placementConflict = errors.Any(item =>
                        EqualsIgnoreCase(item.TryGetValue("reasonCode", out object itemReason) ? itemReason?.ToString() : null, "utility_path_conflict")
                        || EqualsIgnoreCase(item.TryGetValue("reasonCode", out itemReason) ? itemReason?.ToString() : null, "placement_conflict"));
                    string networkError = null;
                    bool networkConnected = dryRun || (errors.Count == 0 && PersistUtilityPathConnections(def, path, out networkError));
                    if (!networkConnected && networkError != null) errors.Add(new Dictionary<string, object> { ["reasonCode"] = "utility_network_incomplete", ["error"] = networkError });
                    int connectedCells = dryRun ? valid : CountUtilityPathCells(def, path, worldId);
                    bool complete = dryRun ? errors.Count == 0 && valid == path.Count : connectedCells >= path.Count && networkConnected;
                    var response = new Dictionary<string, object>
                    {
                        ["prefabId"] = def.PrefabID,
                        ["dryRun"] = dryRun,
                        ["committed"] = !dryRun && (planned > 0 || reused > 0 || autoMarked > 0),
                        ["placementMode"] = dryRun ? "dry_run_validation" : "cell_by_cell",
                        ["materialSelection"] = pathMaterial.ToDictionary(),
                        ["pathMode"] = "continuous_manhattan_path",
                ["pathCells"] = path.Count,
                ["planned"] = planned,
                ["reusedExisting"] = reused,
                ["valid"] = valid,
                ["autoMarkedObstructions"] = autoMarked,
                ["failed"] = errors.Count,
                ["connectedCells"] = connectedCells,
                ["connectionsPersisted"] = !dryRun && networkConnected,
                ["networkConnected"] = !dryRun && networkConnected && IsCompletedUtilityPath(def, path),
                ["complete"] = complete,
                ["success"] = complete,
                ["autoDigLimitReached"] = autoDigContext.LimitReached,
                ["path"] = path.Select(p => new { x = p.x, y = p.y }).ToList(),
                ["segments"] = BuildPathSegments(path),
                ["errors"] = errors.Take(50).ToList(),
                ["results"] = results
            };
                    if (placementConflict)
                    {
                        response["reasonCode"] = "utility_path_conflict";
                        response["success"] = false;
                        return CallToolResult.Error(JsonConvert.SerializeObject(response, McpJsonUtil.Settings));
                    }
                    if (!complete)
                    {
                        response["reasonCode"] = "utility_path_incomplete";
                        return CallToolResult.Error(JsonConvert.SerializeObject(response, McpJsonUtil.Settings));
                    }
                    return CallToolResult.Text(JsonConvert.SerializeObject(response, McpJsonUtil.Settings));
        }
            };
        }
    }
}
