using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using OniMcp.Core;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class FarmingTools
    {
        public static McpTool BatchSetPlanting()
        {
            return new McpTool
            {
                Name = "farming_planting_batch_set",
                Group = "farming",
                Mode = "write",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "planters_batch_set_seed", "plant_seed_area_set" },
                Tags = new List<string> { "farming", "plants", "planters", "seeds", "batch" },
                Description = "兼容入口：请优先使用 colony_control domain=bio bioDomain=farming action=batch。按区域批量设置或取消种植请求；适合一次给多块农砖/种植箱安排同一种种子",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "integer", Description = "Exact plant/plot instance ID", Required = false },
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "Validate selected plots without mutation.", Required = false },
                    ["seedTag"] = new McpToolParameter { Type = "string", Description = "种子 prefab/tag，例如 BasicPlantSeed；action=set 时必填", Required = false },
                    ["mutationTag"] = new McpToolParameter { Type = "string", Description = "植物突变/亚种 tag；未使用突变时留空", Required = false },
                    ["action"] = new McpToolParameter { Type = "string", Description = "set 或 cancel，默认 set", Required = false, EnumValues = new List<string> { "set", "cancel" } },
                    ["emptyOnly"] = new McpToolParameter { Type = "boolean", Description = "只处理没有当前植物的种植槽，默认 true", Required = false },
                    ["removeOccupant"] = new McpToolParameter { Type = "boolean", Description = "若已有植物，是否先调用 OrderRemoveOccupant 铲除，默认 false", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "按种植槽名称、prefabId、当前植物或请求种子筛选", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多处理数量，默认 100，最大 500", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "Required for committed changes; dryRun needs no confirmation", Required = false }
                }),
                Handler = args =>
                {
                    if (!HasRectInput(args) && !ToolUtil.GetInt(args, "id").HasValue && string.IsNullOrWhiteSpace(args["query"]?.ToString()))
                        return CallToolResult.Error("Select targets with id, query, or areaId");
                    bool dryRun = ToolUtil.GetBool(args, "dryRun", false);
                    if (!dryRun && !ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required to batch change planting requests");

                    var rect = HasRectInput(args) ? ToolUtil.GetRect(args) : null;
                    int cells = rect == null ? 0 : RectCellCount(rect);
                    if (!dryRun && cells > 100 && !ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required when changing planting requests in more than 100 cells");

                    string action = (args["action"]?.ToString() ?? "set").Trim().ToLowerInvariant();
                    bool isSet = action == "set";
                    if (!isSet && action != "cancel")
                        return CallToolResult.Error("action must be set or cancel");

                    Tag seedTag = Tag.Invalid;
                    GameObject seedPrefab = null;
                    if (isSet)
                    {
                        string seedName = args["seedTag"]?.ToString();
                        if (string.IsNullOrWhiteSpace(seedName))
                            return CallToolResult.Error("seedTag is required for action=set");
                        seedTag = TagManager.Create(seedName.Trim());
                        seedPrefab = Assets.GetPrefab(seedTag);
                        if (seedPrefab == null || seedPrefab.GetComponent<PlantableSeed>() == null)
                            return CallToolResult.Error("seedTag is not a PlantableSeed prefab");
                    }

                    var mutationTag = string.IsNullOrWhiteSpace(args["mutationTag"]?.ToString())
                        ? Tag.Invalid
                        : TagManager.Create(args["mutationTag"].ToString().Trim());
                    int worldId = ToolUtil.ResolveWorldId(args);
                    string query = args["query"]?.ToString();
                    bool emptyOnly = ToolUtil.GetBool(args, "emptyOnly", true);
                    bool removeOccupant = ToolUtil.GetBool(args, "removeOccupant", false);
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 100, 500));
                    var seedInfo = isSet ? SeedInfo(seedPrefab.GetComponent<PlantableSeed>()) : null;
                    int changedCount = 0;
                    int failedCount = 0;
                    int unchangedCount = 0;
                    var changed = new List<Dictionary<string, object>>();
                    var failures = new List<Dictionary<string, object>>();
                    var unchanged = new List<Dictionary<string, object>>();
                    foreach (var plot in Components.BuildingCompletes.Items
                                 .Select(building => building?.GetComponent<PlantablePlot>())
                                 .Where(plot => plot != null && ToolUtil.GameObjectMatchesWorld(plot.gameObject, worldId))
                                 .Where(plot => rect == null || CellInRect(Grid.PosToCell(plot.gameObject), rect, worldId))
                                 .Where(plot => !ToolUtil.GetInt(args, "id").HasValue || (plot.GetComponent<KPrefabID>()?.InstanceID ?? plot.GetInstanceID()) == ToolUtil.GetInt(args, "id").Value)
                                 .Where(plot => PlantingMatches(plot, query))
                                 .Take(limit))
                    {
                        if (!isSet)
                        {
                            bool hadRequestedTag = plot.requestedEntityTag.IsValid;
                            bool hadActiveRequest = plot.GetActiveRequest != null;
                            if (!hadRequestedTag && !hadActiveRequest)
                            {
                                unchangedCount++;
                                AddBatchDetail(unchanged, BatchPlotOutcome("no_active_request", plot));
                                continue;
                            }

                            var before = PlotInfo(plot);
                            if (dryRun) { changedCount++; AddBatchDetail(changed, PlotInfo(plot)); continue; }
                            plot.CancelActiveRequest();
                            bool cleared = !plot.requestedEntityTag.IsValid && plot.GetActiveRequest == null;
                            if (cleared)
                            {
                                changedCount++;
                                AddBatchDetail(changed, PlotInfo(plot));
                            }
                            else
                            {
                                failedCount++;
                                var failure = BatchPlotOutcome("cancel_did_not_clear_request", plot);
                                failure["before"] = before;
                                AddBatchDetail(failures, failure);
                            }
                            continue;
                        }

                        bool hasDeposit = SeedMatchesPlotDepositTags(plot, seedPrefab, seedTag);
                        bool validEntity = plot.IsValidEntity(seedPrefab);
                        if (!hasDeposit || !validEntity)
                        {
                            failedCount++;
                            var failure = BatchPlotOutcome("seed_not_valid_for_plot", plot);
                            failure["hasDepositTag"] = hasDeposit;
                            failure["isValidEntity"] = validEntity;
                            failure["seed"] = seedInfo;
                            AddBatchDetail(failures, failure);
                            continue;
                        }
                        if (RequestedPlantingMatches(plot, seedTag, mutationTag))
                        {
                            unchangedCount++;
                            AddBatchDetail(unchanged, BatchPlotOutcome("request_already_active", plot));
                            continue;
                        }
                        if (plot.Occupant != null)
                        {
                            if (emptyOnly)
                            {
                                unchangedCount++;
                                AddBatchDetail(unchanged, BatchPlotOutcome("occupant_present_empty_only", plot));
                                continue;
                            }
                            if (!removeOccupant)
                            {
                                unchangedCount++;
                                AddBatchDetail(unchanged, BatchPlotOutcome("occupant_present_remove_disabled", plot));
                                continue;
                            }
                            if (!dryRun) plot.OrderRemoveOccupant();
                        }
                        if (dryRun) { changedCount++; AddBatchDetail(changed, PlotInfo(plot)); continue; }
                        plot.CancelActiveRequest();
                        plot.CreateOrder(seedTag, mutationTag);
                        bool requestedPlantingMatches = RequestedPlantingMatches(plot, seedTag, mutationTag);
                        bool requestActive = plot.GetActiveRequest != null;
                        if (requestedPlantingMatches)
                        {
                            changedCount++;
                            AddBatchDetail(changed, PlotInfo(plot));
                        }
                        else
                        {
                            failedCount++;
                            var failure = BatchPlotOutcome("create_order_postcondition_failed", plot);
                            failure["requestedPlantingMatches"] = requestedPlantingMatches;
                            failure["hasActiveRequest"] = requestActive;
                            failure["hasDepositTag"] = hasDeposit;
                            failure["isValidEntity"] = validEntity;
                            failure["worldInventoryAmount"] = Math.Round(AvailableSeedAmount(plot.gameObject, seedTag), 3);
                            failure["inventoryNote"] = "World inventory amount is informational only; it does not prove fetchability or explain CreateOrder outcome.";
                            failure["seed"] = seedInfo;
                            AddBatchDetail(failures, failure);
                        }
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["action"] = action,
                        ["seedTag"] = seedTag.IsValid ? seedTag.Name : null,
                        ["dryRun"] = dryRun,
                        ["committed"] = !dryRun,
                        ["wouldChange"] = changedCount,
                        ["changed"] = dryRun ? 0 : changedCount,
                        ["failed"] = failedCount,
                        ["unchanged"] = unchangedCount,
                        ["worldId"] = worldId,
                        ["rect"] = rect,
                        ["detailLimit"] = 20,
                        ["plots"] = changed,
                        ["failures"] = failures,
                        ["unchangedPlots"] = unchanged
                    }, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool UprootArea()
        {
            return new McpTool
            {
                Name = "plants_uproot_area",
                Group = "farming",
                Mode = "execute",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "farming_uproot_area", "plants_cancel_uproot" },
                Tags = new List<string> { "farming", "plants", "uproot", "harvest" },
                Description = "兼容入口：请优先使用 colony_control domain=bio bioDomain=farming action=uproot。按区域标记或取消铲除植物",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "Plant name or prefab filter in the selected world", Required = false },
                    ["id"] = new McpToolParameter { Type = "integer", Description = "Exact plant/plot instance ID", Required = false },
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "Preview uproot markers and priority changes without mutation.", Required = false },
                    ["action"] = new McpToolParameter { Type = "string", Description = "mark 或 cancel，默认 mark", Required = false, EnumValues = new List<string> { "mark", "cancel" } },
                    ["priority"] = new McpToolParameter { Type = "integer", Description = "铲除差事优先级 1-9，默认 5", Required = false },
                    ["topPriority"] = new McpToolParameter { Type = "boolean", Description = "是否设为红色最高优先级，默认 false", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "Required for committed changes; dryRun needs no confirmation", Required = false }
                }),
                Handler = args =>
                {
                    if (!HasRectInput(args) && !ToolUtil.GetInt(args, "id").HasValue && string.IsNullOrWhiteSpace(args["query"]?.ToString()))
                        return CallToolResult.Error("Select targets with id, query, or areaId");

                    var rect = HasRectInput(args) ? ToolUtil.GetRect(args) : null;
                    bool dryRun = ToolUtil.GetBool(args, "dryRun", false);
                    if (!dryRun && !ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required to change uproot orders");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    string action = (args["action"]?.ToString() ?? "mark").Trim().ToLowerInvariant();
                    if (action != "mark" && action != "cancel") return CallToolResult.Error("action must be mark or cancel");
                    bool mark = action == "mark";
                    int changed = 0;
                    var results = new List<Dictionary<string, object>>();
                    foreach (var uprootable in Components.Uprootables.Items)
                    {
                        var go = uprootable?.gameObject;
                        if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                            continue;
                        int cell = Grid.PosToCell(go);
                        if (rect != null && !CellInRect(cell, rect, worldId))
                            continue;

                        if (ToolUtil.GetInt(args, "id").HasValue && (go.GetComponent<KPrefabID>()?.InstanceID ?? go.GetInstanceID()) != ToolUtil.GetInt(args, "id").Value) continue;
                        string query = args["query"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(query) && ToolUtil.CleanName(go.GetProperName()).IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0
                            && go.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        if (!ToolUtil.VisibleCellAllowed(cell, true)) continue;
                        if (mark)
                        {
                            if (!uprootable.CanUproot())
                                continue;
                            if (!dryRun)
                            {
                                uprootable.MarkForUproot();
                                ApplyPriority(go, args);
                            }
                            results.Add(TargetInfo(go, dryRun ? "would_mark" : "marked"));
                        }
                        else
                        {
                            if (!uprootable.IsMarkedForUproot)
                                continue;
                            if (!dryRun)
                            {
                                uprootable.ForceCancelUproot();
                                go.Trigger(CancelEvent);
                            }
                            results.Add(TargetInfo(go, dryRun ? "would_cancel" : "cancelled"));
                        }
                        changed++;
                    }

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["action"] = mark ? "mark" : "cancel",
                        ["dryRun"] = dryRun,
                        ["committed"] = !dryRun,
                        ["wouldChange"] = changed,
                        ["changed"] = dryRun ? 0 : changed,
                        ["worldId"] = worldId,
                        ["rect"] = rect,
                        ["targets"] = results.Take(200).ToList(),
                        ["truncatedTargets"] = Math.Max(0, results.Count - 200)
                    }, McpJsonUtil.Settings));
                }
            };
        }

    }
}
