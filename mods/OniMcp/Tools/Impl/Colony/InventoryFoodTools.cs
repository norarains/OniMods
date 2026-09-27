using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class InventoryTools
    {
        public static McpTool GetFoodInventory()
        {
            return new McpTool
            {
                Name = "resources_food",
                Hidden = true,
                Group = "resources",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "get_food_inventory" },
                Description = "弃用警告：旧工具将在 0.3.0 移除；请改用 read_control domain=resources action=food",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["worldId"] = new McpToolParameter
                    {
                        Type = "integer",
                        Description = "按世界 ID 过滤，留空返回全部世界",
                        Required = false
                    },
                    ["limit"] = new McpToolParameter
                    {
                        Type = "integer",
                        Description = "最多返回多少种食物，默认 100，最大 500",
                        Required = false
                    },
                    ["visibleOnly"] = new McpToolParameter
                    {
                        Type = "boolean",
                        Description = "是否只统计已揭示格子内食物，默认 true；调试可传 false",
                        Required = false
                    }
                },
                Handler = args =>
                {
                    if (Game.Instance == null)
                        return CallToolResult.Error("Game not initialized");

                    int? worldId = TryGetInt(args, "worldId");
                    bool visibleOnly = TryGetBool(args, "visibleOnly", true);
                    int limit = ClampLimit(args, 100, 500);
                    var groups = new Dictionary<string, FoodAggregate>();
                    float totalCaloriesKcal = 0f, storedCaloriesKcal = 0f;
                    var samples = new List<Dictionary<string, object>>();
                    bool details = ToolUtil.GetBool(args, "includeDetails", false) || args["id"] != null;
                    string query = args["query"]?.ToString();

                    foreach (var edible in Components.Edibles.Items)
                    {
                        if (edible == null || edible.gameObject == null) continue;

                        if (!ObjectReadFacts.MatchesId(edible.gameObject, args)) continue;
                        if (!string.IsNullOrWhiteSpace(query) && edible.GetProperName().IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0
                            && edible.name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        var pickupable = edible.GetComponent<Pickupable>();
                        var primary = edible.GetComponent<PrimaryElement>();
                        int cell = pickupable != null ? ToolUtil.PickupableCell(pickupable) : Grid.PosToCell(edible);
                        if (!ToolUtil.VisibleCellAllowed(cell, visibleOnly))
                            continue;
                        int itemWorldId = Grid.IsValidCell(cell) ? Grid.WorldIdx[cell] : edible.GetMyWorldId();
                        if (worldId.HasValue && itemWorldId != worldId.Value) continue;

                        string prefabId = edible.GetComponent<KPrefabID>()?.PrefabTag.Name ?? edible.name;
                        string name = ToolUtil.CleanName(edible.GetProperName());
                        string key = prefabId;

                        FoodAggregate aggregate;
                        if (!groups.TryGetValue(key, out aggregate))
                        {
                            aggregate = new FoodAggregate
                            {
                                Name = name,
                                PrefabId = prefabId,
                                Quality = edible.GetQuality(),
                                Morale = edible.GetMorale(),
                                WorldIds = new HashSet<int>()
                            };
                            groups[key] = aggregate;
                        }

                        float caloriesKcal = SafeFloat(edible.Calories) / 1000f;
                        totalCaloriesKcal += caloriesKcal;
                        bool stored = pickupable?.storage != null;
                        if (stored) storedCaloriesKcal += caloriesKcal;
                        if (details && samples.Count < limit)
                        {
                            var rot = edible.GetSMI<Rottable.Instance>();
                            samples.Add(new Dictionary<string, object> {
                                ["id"] = edible.GetComponent<KPrefabID>()?.InstanceID, ["prefabId"] = prefabId,
                                ["x"] = Grid.CellColumn(cell), ["y"] = Grid.CellRow(cell), ["worldId"] = itemWorldId,
                                ["stored"] = stored, ["kcal"] = Math.Round(caloriesKcal),
                                ["temperatureC"] = primary == null ? (object)null : Math.Round(primary.Temperature - 273.15, 1),
                                ["freshnessPercent"] = rot == null ? (object)null : Math.Round(rot.RotConstitutionPercentage * 100, 1),
                                ["refrigeration"] = rot == null ? null : Rottable.RefrigerationLevel(rot).ToString(),
                                ["atmosphere"] = rot == null ? null : Rottable.AtmosphereQuality(rot).ToString()
                            });
                        }
                        aggregate.Count++;
                        aggregate.TotalCaloriesKcal += caloriesKcal;
                        aggregate.TotalMassKg += primary != null ? SafeFloat(primary.Mass) : 0f;
                        aggregate.StoredCount += pickupable != null && pickupable.storage != null ? 1 : 0;
                        aggregate.WorldIds.Add(itemWorldId);
                    }

                    var foods = groups.Values
                        .OrderByDescending(food => food.TotalCaloriesKcal)
                        .Take(limit)
                        .Select(food => food.ToDictionary())
                        .ToList();

                    var result = new Dictionary<string, object>
                    {
                        ["totalCaloriesKcal"] = Math.Round(totalCaloriesKcal, 1),
                        ["storedCaloriesKcal"] = Math.Round(storedCaloriesKcal, 1),
                        ["looseCaloriesKcal"] = Math.Round(totalCaloriesKcal - storedCaloriesKcal, 1),
                        ["fetchabilityVerified"] = false,
                        ["visibleOnly"] = visibleOnly,
                        ["foodTypes"] = groups.Count,
                        ["returned"] = foods.Count,
                        ["foods"] = foods
                    };

                    if (details) result["items"] = samples;
                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

    }
}
