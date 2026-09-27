using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    public static class ResearchTools
    {
        public static McpTool GetResearchStatus()
        {
            return new McpTool
            {
                Name = "research_status",
                Group = "research",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Description = "兼容入口：请优先使用 colony_control domain=management kind=research action=status。查看当前研究目标、队列和进度",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["includeDetails"] = new McpToolParameter { Type = "boolean", Description = "Include active technology unlocks and prerequisites; default false.", Required = false }
                },
                Handler = args =>
                {
                    if (Research.Instance == null || Db.Get()?.Techs == null)
                        return CallToolResult.Error("Research not initialized");

                    var queuedTargets = ResearchTargetQueue.Current?.Read();
                    var active = Research.Instance.GetActiveResearch();
                    var target = Research.Instance.GetTargetResearch();
                    var queue = Research.Instance.GetResearchQueue();
                    var result = new Dictionary<string, object>
                    {
                        ["active"] = active != null ? TechToDictionary(active.tech, includeDetails: ToolUtil.GetBool(args, "includeDetails", false)) : null,
                        ["target"] = target != null ? (object)target.tech.Id : null,
                        ["queuedTargets"] = queuedTargets, ["queueOrder"] = "native_prerequisites_for_fifo_head",
                        ["queue"] = queue.Select(item => item.tech.Id).ToList()
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ListResearch()
        {
            return new McpTool
            {
                Name = "research_list",
                Group = "research",
                Mode = "read",
                Risk = "none",
                Hidden = true,
                Aliases = new List<string> { "list_research" },
                Description = "兼容入口：请优先使用 colony_control domain=management kind=research action=list。列出或搜索可研究科技。query 可匹配科技 ID、名称、解锁建筑 ID/名称或搜索词",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["query"] = new McpToolParameter { Type = "string", Description = "可选搜索词", Required = false },
                    ["includeComplete"] = new McpToolParameter { Type = "boolean", Description = "是否包含已完成科技，默认 true", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "最多返回数量，默认 30，最大 100", Required = false }
                },
                Handler = args =>
                {
                    if (Research.Instance == null || Db.Get()?.Techs == null)
                        return CallToolResult.Error("Research not initialized");

                    string query = args["query"]?.ToString();
                    bool includeComplete = ToolUtil.GetBool(args, "includeComplete", true);
                    int limit = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "limit") ?? 30, 100));

                    var matches = AllTechs()
                        .Where(tech => includeComplete || !IsComplete(tech))
                        .Where(tech => string.IsNullOrWhiteSpace(query) || Matches(tech, query))
                        .OrderBy(tech => string.Equals(tech.Id, query, StringComparison.OrdinalIgnoreCase) || string.Equals(ToolUtil.CleanName(tech.Name), query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                        .ThenBy(tech => IsComplete(tech))
                        .ThenBy(tech => tech.tier)
                        .ThenBy(tech => tech.Id)
                        .Take(limit)
                        .Select(tech => TechToDictionary(tech, includeDetails: true))
                        .ToList();

                    var result = new Dictionary<string, object>
                    {
                        ["query"] = string.IsNullOrWhiteSpace(query) ? null : query,
                        ["returned"] = matches.Count,
                        ["research"] = matches
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool SetResearch()
        {
            return new McpTool
            {
                Name = "research_set",
                Group = "research",
                Mode = "write",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "set_research" },
                Description = "兼容入口：请优先使用 colony_control domain=management kind=research action=set。选择当前研究目标。优先用 id 精确指定，也可用 query 搜索科技或解锁建筑",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["id"] = new McpToolParameter { Type = "string", Description = "科技 ID，例如 FarmingTech、SanitationSciences、ImprovedOxygen", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "搜索词，可匹配科技名称、ID、解锁项", Required = false },
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "Validate the technology without changing research.", Required = false },
                    ["clearQueue"] = new McpToolParameter { Type = "boolean", Description = "是否清空旧队列，默认 true", Required = false }
                },
                Handler = args =>
                {
                    if (Research.Instance == null || Db.Get()?.Techs == null)
                        return CallToolResult.Error("Research not initialized");

                    string id = args["id"]?.ToString();
                    string query = args["query"]?.ToString();
                    bool clearQueue = ToolUtil.GetBool(args, "clearQueue", true);
                    Tech tech = null;

                    if (!string.IsNullOrWhiteSpace(id))
                        tech = Db.Get().Techs.TryGet(id.Trim());

                    if (string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(query))
                    {
                        var matches = FindMatches(query).Where(candidate => !IsComplete(candidate)).ToList();
                        var exact = matches.FirstOrDefault(candidate => IsExactMatch(candidate, query));
                        if (exact != null)
                            tech = exact;
                        else if (matches.Count == 1)
                            tech = matches[0];
                        else if (matches.Count > 1)
                        {
                            var result = new Dictionary<string, object>
                            {
                                ["error"] = "Multiple research matches; pass id to select one",
                                ["matches"] = matches.Take(10).Select(candidate => TechToDictionary(candidate, includeDetails: true)).ToList()
                            };
                            return CallToolResult.Error(JsonConvert.SerializeObject(result, McpJsonUtil.Settings));
                        }
                    }

                    if (tech == null)
                        return CallToolResult.Error("Research tech not found");

                    if (IsComplete(tech))
                        return CallToolResult.Error($"Research already complete: {tech.Id}");

                    if (ToolUtil.GetBool(args, "dryRun", false))
                        return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            ["valid"] = true, ["dryRun"] = true, ["committed"] = false,
                            ["selected"] = TechToDictionary(tech, includeDetails: false),
                            ["clearQueue"] = clearQueue
                        }, McpJsonUtil.Settings));

                    var targetQueue = ResearchTargetQueue.Current;
                    if (targetQueue == null) return CallToolResult.Error("Persistent research queue unavailable; reload the colony before setting research.");
                    targetQueue.Select(tech, clearQueue);

                    var requestedTargets = targetQueue.Read();
                    var active = Research.Instance.GetActiveResearch();
                    var queue = Research.Instance.GetResearchQueue();
                    var response = new Dictionary<string, object>
                    {
                        ["selected"] = TechToDictionary(tech, includeDetails: true),
                        ["queuedTargets"] = requestedTargets, ["queueOrder"] = "native_prerequisites_for_fifo_head",
                        ["active"] = active != null ? TechToDictionary(active.tech, includeDetails: false) : null,
                        ["queue"] = queue.Select(item => item.tech.Id).ToList()
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(response, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ClearResearch()
        {
            return new McpTool
            {
                Name = "research_clear",
                Group = "research",
                Mode = "write",
                Risk = "medium",
                Hidden = true,
                Aliases = new List<string> { "clear_research", "research_cancel", "research_queue_clear" },
                Tags = new List<string> { "research", "queue", "cancel", "clear", "management", "researchscreen" },
                Description = "兼容入口：请优先使用 colony_control domain=management kind=research action=clear。取消当前研究队列，等价于 ResearchScreen 的取消研究按钮；需 confirm=true",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "Preview clearing research without changing active or queued targets.", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "提交必须为 true；dryRun=true 不要求确认", Required = false }
                },
                Handler = args =>
                {
                    bool dryRun = ToolUtil.GetBool(args, "dryRun", false);
                    if (!dryRun && !ToolUtil.GetBool(args, "confirm", false))
                        return CallToolResult.Error("confirm=true is required to clear the research queue");

                    if (Research.Instance == null || Db.Get()?.Techs == null)
                        return CallToolResult.Error("Research not initialized");

                    var activeBefore = Research.Instance.GetActiveResearch();
                    var targetBefore = Research.Instance.GetTargetResearch();
                    var queue = Research.Instance.GetResearchQueue();
                    var queueBefore = queue.Select(item => item.tech.Id).ToList();
                    var before = new Dictionary<string, object>
                    {
                        ["active"] = activeBefore != null ? TechToDictionary(activeBefore.tech, includeDetails: false) : null,
                        ["target"] = targetBefore != null ? TechToDictionary(targetBefore.tech, includeDetails: false) : null,
                        ["queue"] = queueBefore,
                        ["queuedTargets"] = ResearchTargetQueue.Current?.Snapshot()
                    };
                    if (dryRun)
                        return CallToolResult.Text(JsonConvert.SerializeObject(new Dictionary<string, object>
                        {
                            ["dryRun"] = true, ["committed"] = false, ["cleared"] = 0,
                            ["wouldClear"] = queueBefore.Count, ["before"] = before
                        }, McpJsonUtil.Settings));

                    ResearchTargetQueue.Current?.Clear();
                    if (ResearchTargetQueue.Current == null) Research.Instance.SetActiveResearch(null, true);

                    var activeAfter = Research.Instance.GetActiveResearch();
                    var targetAfter = Research.Instance.GetTargetResearch();
                    var queueAfter = Research.Instance.GetResearchQueue()
                        .Select(item => TechToDictionary(item.tech, includeDetails: false))
                        .ToList();

                    var response = new Dictionary<string, object>
                    {
                        ["dryRun"] = false, ["committed"] = true,
                        ["cleared"] = queueBefore.Count,
                        ["before"] = before,
                        ["after"] = new Dictionary<string, object>
                        {
                            ["active"] = activeAfter != null ? TechToDictionary(activeAfter.tech, includeDetails: false) : null,
                            ["target"] = targetAfter != null ? TechToDictionary(targetAfter.tech, includeDetails: false) : null,
                            ["queue"] = queueAfter
                        }
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(response, McpJsonUtil.Settings));
                }
            };
        }

        public static McpTool ControlResearch()
        {
            return new McpTool
            {
                Name = "research_control",
                Group = "research",
                Mode = "write",
                Risk = "medium",
                Aliases = new List<string> { "research_queue_control", "research_management" },
                Tags = new List<string> { "research", "queue", "tech", "management", "researchscreen" },
                Description = "统一查看、搜索、设置和清空研究队列。action=status/list/set/clear；status 查看当前队列，list 搜索科技，set 选择当前研究，clear 需 confirm=true。",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["action"] = new McpToolParameter { Type = "string", Description = "操作：status、list、set、clear", Required = true },
                    ["id"] = new McpToolParameter { Type = "string", Description = "action=set 时科技 ID，例如 FarmingTech、SanitationSciences、ImprovedOxygen", Required = false },
                    ["query"] = new McpToolParameter { Type = "string", Description = "action=list/set 时搜索词，可匹配科技名称、ID、解锁项", Required = false },
                    ["includeComplete"] = new McpToolParameter { Type = "boolean", Description = "action=list 时是否包含已完成科技，默认 true", Required = false },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "action=list 时最多返回数量，默认 30，最大 100", Required = false },
                    ["clearQueue"] = new McpToolParameter { Type = "boolean", Description = "action=set 时是否清空旧队列，默认 true", Required = false },
                    ["dryRun"] = new McpToolParameter { Type = "boolean", Description = "Preview set/clear without changing research.", Required = false },
                    ["confirm"] = new McpToolParameter { Type = "boolean", Description = "action=clear 时必须为 true，确认清空当前研究队列", Required = false }
                },
                Handler = args =>
                {
                    string action = (args["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
                    if (action == "status")
                        return GetResearchStatus().Handler(args);
                    if (action == "list")
                        return ListResearch().Handler(args);
                    if (action == "set")
                        return SetResearch().Handler(args);
                    if (action == "clear")
                        return ClearResearch().Handler(args);
                    return CallToolResult.Error("action must be one of status, list, set, clear");
                }
            };
        }

        private static IEnumerable<Tech> AllTechs()
        {
            var techs = Db.Get().Techs;
            for (int i = 0; i < techs.Count; i++)
            {
                var tech = techs.GetResource(i) as Tech;
                if (tech != null)
                    yield return tech;
            }
        }

        private static List<Tech> FindMatches(string query)
        {
            return AllTechs()
                .Where(tech => Matches(tech, query))
                .OrderByDescending(tech => IsExactMatch(tech, query))
                .ThenBy(tech => tech.tier)
                .ThenBy(tech => tech.Id)
                .ToList();
        }

        private static bool Matches(Tech tech, string query)
        {
            if (tech == null || string.IsNullOrWhiteSpace(query))
                return false;

            string q = query.Trim();
            return Contains(tech.Id, q)
                || Contains(tech.Name, q)
                || Contains(tech.category, q)
                || tech.searchTerms.Any(term => Contains(term, q))
                || tech.unlockedItemIDs.Any(item => Contains(item, q))
                || tech.unlockedItems.Any(item => item != null && (Contains(item.Id, q) || Contains(item.Name, q)));
        }

        private static bool IsExactMatch(Tech tech, string query)
        {
            if (tech == null || string.IsNullOrWhiteSpace(query))
                return false;

            string q = query.Trim();
            return EqualsIgnoreCase(tech.Id, q)
                || EqualsIgnoreCase(tech.Name, q)
                || tech.unlockedItemIDs.Any(item => EqualsIgnoreCase(item, q))
                || tech.unlockedItems.Any(item => item != null && (EqualsIgnoreCase(item.Id, q) || EqualsIgnoreCase(item.Name, q)));
        }

        private static Dictionary<string, object> TechToDictionary(Tech tech, bool includeDetails)
        {
            var instance = Research.Instance?.Get(tech);
            var result = new Dictionary<string, object>
            {
                ["id"] = tech.Id,
                ["name"] = ToolUtil.CleanName(tech.Name),
                ["tier"] = tech.tier,
                ["category"] = tech.category,
                ["complete"] = instance?.IsComplete() ?? false,
                ["active"] = Research.Instance?.IsBeingResearched(tech) ?? false,
                ["costs"] = tech.costsByResearchTypeID.ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value, 2))
            };

            if (instance != null)
                result["progress"] = instance.IsComplete() ? 100.0
                    : Math.Round(instance.GetTotalPercentageComplete() * 100.0, 1);

            if (includeDetails)
            {
                result["description"] = ToolUtil.CleanName(tech.desc);
                result["requires"] = tech.requiredTech.Select(required => new Dictionary<string, object>
                {
                    ["id"] = required.Id,
                    ["name"] = ToolUtil.CleanName(required.Name),
                    ["complete"] = IsComplete(required)
                }).ToList();
                result["unlocks"] = tech.unlockedItems.Select(item => new Dictionary<string, object>
                {
                    ["id"] = item.Id,
                    ["name"] = ToolUtil.CleanName(item.Name)
                }).ToList();
            }

            return result;
        }

        private static bool IsComplete(Tech tech)
        {
            return Research.Instance?.Get(tech)?.IsComplete() ?? false;
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool EqualsIgnoreCase(string value, string query)
        {
            return string.Equals(value, query, StringComparison.OrdinalIgnoreCase);
        }
    }
}
