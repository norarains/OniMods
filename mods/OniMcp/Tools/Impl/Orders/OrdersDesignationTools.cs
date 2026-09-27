using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using OniMcp.Core;
using UnityEngine;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static partial class OrdersTools
{
        public static McpTool CancelArea()
        {
            return new McpTool
            {
                Name = "orders_cancel_area",
                Group = "orders",
                Mode = "execute",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "cancel_area", "orders_cancel" },
                Description = "兼容入口：请优先使用 orders_control domain=area action=cancel。取消矩形区域内玩家已下达的订单：挖掘、建造、拆除、清扫、收获、攻击、抓捕等可取消对象",
                Parameters = RectParams(new Dictionary<string, McpToolParameter>
                {
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "Preview cancellation without changing any order.", Required = false },
                    ["includeAttack"] = new McpToolParameter { Type = "boolean", Description = "是否取消区域内攻击标记，默认 true", Required = false },
                    ["includeCapture"] = new McpToolParameter { Type = "boolean", Description = "是否取消区域内抓捕标记，默认 true", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "提交必须为 true；dryRun=true 不要求确认", Required = false }
                }),
                Handler = args =>
                {
                    if (!HasRectInput(args))
                        return CallToolResult.Error("areaId or x1/y1/x2/y2 are required");

                    var rect = ToolUtil.GetRect(args);
                    int cells = RectCellCount(rect);
                    bool dryRun = ToolUtil.GetBool(args, "dryRun", false);
                    if (!dryRun && !ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required to cancel orders; dryRun=true previews only");

                    int worldId = ToolUtil.ResolveWorldId(args);
                    bool includeAttack = ToolUtil.GetBool(args, "includeAttack", true);
                    bool includeCapture = ToolUtil.GetBool(args, "includeCapture", true);
                    var seen = new HashSet<GameObject>();
                    int triggered = 0;

                    for (int y = rect["y1"]; y <= rect["y2"]; y++)
                    {
                        for (int x = rect["x1"]; x <= rect["x2"]; x++)
                        {
                            int cell = Grid.XYToCell(x, y);
                            if (!PlayerVisibility.Cell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                                continue;

                            for (int layer = 0; layer < 45; layer++)
                            {
                                var go = Grid.Objects[cell, layer];
                                if (go == null || seen.Contains(go))
                                    continue;
                                seen.Add(go);
                                if (!dryRun) go.Trigger(CancelEvent);
                                triggered++;
                            }
                        }
                    }

                    int attacksCancelled = includeAttack ? SetAttackMarks(rect, worldId, false, null, false, dryRun) : 0;
                    int capturesCancelled = includeCapture ? SetCaptureMarks(rect, worldId, false, new PrioritySetting(PriorityScreen.PriorityClass.basic, 5), false, false, dryRun) : 0;

                    return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                    {
                        ["dryRun"] = dryRun,
                        ["committed"] = !dryRun,
                        ["wouldTriggerObjects"] = triggered,
                        ["triggeredObjects"] = dryRun ? 0 : triggered,
                        ["wouldCancelAttacks"] = attacksCancelled,
                        ["attacksCancelled"] = dryRun ? 0 : attacksCancelled,
                        ["wouldCancelCaptures"] = capturesCancelled,
                        ["capturesCancelled"] = dryRun ? 0 : capturesCancelled,
                        ["worldId"] = worldId,
                        ["rect"] = rect
                    }, McpJsonUtil.Settings));
                }
            };
        }

        private static int SetAttackMarks(Dictionary<string, int> rect, int worldId, bool mark, Newtonsoft.Json.Linq.JObject args, bool applyPriority, bool dryRun = false)
        {
            int changed = 0;
            foreach (var target in Components.FactionAlignments.Items)
            {
                var go = target?.gameObject;
                if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                    continue;
                int cell = Grid.PosToCell(go);
                if (!CellInRect(cell, rect, worldId))
                    continue;
                if (mark && (!target.canBePlayerTargeted || !target.IsAlignmentActive()))
                    continue;

                if (!dryRun) target.SetPlayerTargeted(mark);
                if (!dryRun && mark && applyPriority && args != null)
                    ApplyPriority(go, args);
                changed++;
            }
            return changed;
        }

        private static int SetCaptureMarks(Dictionary<string, int> rect, int worldId, bool mark, PrioritySetting setting, bool updatePriority, bool requireCapturable, bool dryRun = false)
        {
            int changed = 0;
            foreach (var capturable in Components.Capturables.Items)
            {
                var go = capturable?.gameObject;
                if (go == null || !ToolUtil.GameObjectMatchesWorld(go, worldId))
                    continue;
                int cell = Grid.PosToCell(go);
                if (!CellInRect(cell, rect, worldId))
                    continue;
                if (mark && requireCapturable && !capturable.IsCapturable())
                    continue;

                if (!dryRun) capturable.MarkForCapture(mark, setting, updatePriority);
                changed++;
            }
            return changed;
        }

        private static void ApplyPriority(GameObject go, Newtonsoft.Json.Linq.JObject args)
        {
            var prioritizable = go.GetComponent<Prioritizable>();
            if (prioritizable == null)
                return;

            bool top = ToolUtil.GetBool(args, "topPriority", false);
            int priority = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "priority") ?? 5, 9));
            var setting = new PrioritySetting(top ? PriorityScreen.PriorityClass.topPriority : PriorityScreen.PriorityClass.basic, top ? 1 : priority);
            prioritizable.SetMasterPriority(setting);
        }
}
}
