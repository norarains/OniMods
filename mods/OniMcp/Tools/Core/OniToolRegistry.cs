using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Core;

namespace OniMcp.Tools
{
    /// <summary>
    /// MCP Tool 注册表
    /// 集中管理所有暴露给 AI 的游戏操作工具
    /// </summary>
    public static class OniToolRegistry
    {
        private static readonly Dictionary<string, McpTool> _tools = new Dictionary<string, McpTool>();
        private static readonly Dictionary<string, McpTool> _internalOperations = new Dictionary<string, McpTool>();
        private static readonly Dictionary<string, string> _aliases = new Dictionary<string, string>();
        private static readonly HashSet<string> DefaultPublicToolNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "building_control",
            "game_control",
            "navigation_control",
            "orders_control",
            "benchmark",
            "server_control",
            "world_editor",
        };
        private static bool _initialized;
        private static readonly OniToolRegistryCache ToolCache = new OniToolRegistryCache();
        private static List<McpToolInfo> _cachedCoreToolInfos, _cachedAllToolInfos;

        /// <summary>
        /// 初始化所有工具
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;

            Register(CoreToolEnglishDescriptions.Apply(ServerControlEntryTools.ControlServer()));
            Register(CoreToolEnglishDescriptions.Apply(WorldEditorTools.ControlWorldEditor()));
            Register(CoreToolEnglishDescriptions.Apply(NavigationControlTools.ControlNavigation()));
            Register(CoreToolEnglishDescriptions.Apply(BuildingControlTools.ControlBuilding()));
            Register(CoreToolEnglishDescriptions.Apply(GameControlEntryTools.ControlGame()));
            Register(CoreToolEnglishDescriptions.Apply(OrdersControlEntryTools.ControlOrders()));
            Register(BenchmarkTools.Benchmark());
            RegisterInternal(ColonyTools.ControlColony());
            RegisterInternal(DuplicantTools.ControlDupes());
            RegisterInternal(ReadTools.ControlRead());
            RegisterInternal(CoordinateControlTools.ControlCoordinate());
            BuildToolInfoCache();
            _initialized = true;
        }

        private static void Register(McpTool tool)
        {
            ToolMetadata.ApplyDefaults(tool);
            _tools[tool.Name] = tool;
            foreach (var alias in tool.Aliases)
            {
                if (!string.IsNullOrEmpty(alias))
                    _aliases[alias] = tool.Name;
            }

            ToolCache.Clear();
            _cachedCoreToolInfos = null;
            _cachedAllToolInfos = null;
        }

        private static void RegisterInternal(McpTool operation)
        {
            ToolMetadata.ApplyDefaults(operation);
            _internalOperations[operation.Name] = operation;
        }

        internal static bool TryGetOperation(string name, out McpTool operation)
        {
            operation = null;
            if (string.IsNullOrWhiteSpace(name))
                return false;
            return TryGetTool(name, out operation) || _internalOperations.TryGetValue(name, out operation);
        }

        public static List<McpTool> GetTools()
        {
            return ToolCache.GetTools(_tools.Values);
        }

        public static List<McpTool> GetVisibleTools()
        {
            return ToolCache.GetVisibleTools(_tools.Values, _tools.Values.Where(t => !t.Hidden));
        }

        public static bool TryGetTool(string name, out McpTool tool)
        {
            tool = null;
            if (string.IsNullOrWhiteSpace(name))
                return false;

            if (_tools.TryGetValue(name, out tool))
                return true;

            string canonicalName;
            return _aliases.TryGetValue(name, out canonicalName) && _tools.TryGetValue(canonicalName, out tool);
        }

        /// <summary>
        /// 获取 Tool 元信息（默认供 tools/list 暴露低 token 公开入口）
        /// </summary>
        public static List<McpToolInfo> GetToolInfos(bool includeAll = false)
        {
            var cached = includeAll ? _cachedAllToolInfos : _cachedCoreToolInfos;
            if (cached != null)
                return new List<McpToolInfo>(cached);

            BuildToolInfoCache();
            cached = includeAll ? _cachedAllToolInfos : _cachedCoreToolInfos;
            if (cached != null)
                return new List<McpToolInfo>(cached);

            return BuildToolInfos(includeAll);
        }

        private static void BuildToolInfoCache()
        {
            ToolCache.Ensure(_tools.Values);
            _cachedCoreToolInfos = BuildToolInfos(includeAll: false);
            _cachedAllToolInfos = BuildToolInfos(includeAll: true);
        }

        private static List<McpToolInfo> BuildToolInfos(bool includeAll)
        {
            var infos = new List<McpToolInfo>();
            ToolCache.Ensure(_tools.Values);
            foreach (var tool in ToolCache.GetVisibleSnapshot()
                .Where(tool => !tool.Hidden && (includeAll || DefaultPublicToolNames.Contains(tool.Name))))
            {
                var properties = new Dictionary<string, SchemaProperty>();
                var required = new List<string>();

                if (tool.Parameters != null)
                {
                    foreach (var param in tool.Parameters)
                    {
                        if (param.Key == ToolCallMiddleware.TaskDescriptionParameter)
                            continue;

                        if (IsCoordinateParameter(param.Key) && !IsCoordinateTool(tool.Name))
                            continue;

                        properties[param.Key] = new SchemaProperty
                        {
                            Type = param.Value.Type,
                            Description = param.Value.Description,
                            Enum = param.Value.SchemaEnumValues,
                            Items = param.Value.Items,
                            McpHeader = param.Value.McpHeader
                        };
                        if (param.Value.Required)
                            required.Add(param.Key);
                    }
                }

                properties[ToolCallMiddleware.TaskDescriptionParameter] = new SchemaProperty
                {
                    Type = "string",
                    Description = "Required for every tool call: briefly describe what you are doing so it can be shown near the player's mouse in-game."
                };
                required.Add(ToolCallMiddleware.TaskDescriptionParameter);

                infos.Add(new McpToolInfo
                {
                    Name = tool.Name,
                    Description = ToolMetadata.FormatDescription(tool),
                    Execution = new ToolExecution { TaskSupport = "optional" },
                    InputSchema = new InputSchema
                    {
                        Properties = properties,
                        Required = required.Count > 0 ? required : null
                    }
                });
            }
            return infos;
        }

        public static int GetDefaultToolInfoCount()
        {
            return _cachedCoreToolInfos?.Count ?? _tools.Values.Count(tool => DefaultPublicToolNames.Contains(tool.Name));
        }

        /// <summary>
        /// 调用指定 Tool
        /// </summary>
        public static CallToolResult CallTool(string name, JObject arguments)
        {
            return CallToolCore(name, arguments, false);
        }

        internal static CallToolResult CallToolFromWorldEditor(string name, JObject arguments, bool allowValidatedCoordinates)
        {
            if (string.IsNullOrWhiteSpace(name))
                return CallToolResult.Error("Operation name is required.");
            if (_tools.ContainsKey(name) || _aliases.ContainsKey(name))
                return CallToolCore(name, arguments, allowValidatedCoordinates);
            if (!_internalOperations.TryGetValue(name, out var operation))
                return CallToolResult.Error($"Operation not found: {name}");
            return CallOperationCore(operation, arguments, allowValidatedCoordinates);
        }

        private static CallToolResult CallOperationCore(McpTool operation, JObject arguments, bool allowValidatedCoordinates)
        {
            var notifications = ToolCallMiddleware.DrainNotifications();
            try
            {
                if (!ToolCallMiddleware.TryGetTaskDescription(arguments, out var taskDescription))
                    return ToolCallMiddleware.MissingTaskDescription(operation.Name, notifications);
                ToolCallMiddleware.PresentTaskDescription(taskDescription);
                if (!allowValidatedCoordinates && operation.Name != "coordinate_control" && ContainsCoordinateArguments(arguments))
                    return ToolCallMiddleware.Inject(CallToolResult.Error("Raw coordinate arguments require a validated world-editor semantic command."), notifications);
                return ToolCallMiddleware.Inject(operation.Handler(arguments ?? new JObject()), notifications);
            }
            catch (Exception ex)
            {
                return ToolCallMiddleware.Inject(CallToolResult.Error($"Internal operation error: {ex.Message}"), notifications);
            }
        }

        internal static bool HasCoordinateArguments(JToken token)
        {
            return ContainsCoordinateArguments(token);
        }

        private static CallToolResult CallToolCore(string name, JObject arguments, bool allowValidatedCoordinates)
        {
            var middlewareNotifications = ToolCallMiddleware.DrainNotifications();
            if (string.IsNullOrWhiteSpace(name))
                return ToolCallMiddleware.Inject(CallToolResult.Error("Tool name is required."), middlewareNotifications);
            bool usedLegacyAlias = false;
            if (!_tools.TryGetValue(name, out var tool))
            {
                if (!_aliases.TryGetValue(name, out var canonicalName) || !_tools.TryGetValue(canonicalName, out tool))
                    return ToolCallMiddleware.Inject(CallToolResult.Error($"Tool not found: {name}"), middlewareNotifications);
                usedLegacyAlias = true;
            }

            try
            {
                if (!ToolCallMiddleware.TryGetTaskDescription(arguments, out var taskDescription))
                {
                    var missingTaskResult = ToolCallMiddleware.MissingTaskDescription(tool.Name, middlewareNotifications);
                    return usedLegacyAlias
                        ? ToolMetadata.AddLegacyAliasDeprecationWarning(missingTaskResult, name, tool.Name)
                        : missingTaskResult;
                }

                ToolCallMiddleware.PresentTaskDescription(taskDescription);

                if (!allowValidatedCoordinates && !IsCoordinateTool(tool.Name)
                    && !(tool.Name == "server_control" && arguments?["domain"]?.ToString() == "batch")
                    && ContainsCoordinateArguments(arguments))
                {
                    var coordinateResult = ToolCallMiddleware.Inject(CallToolResult.Error("Use semantic query/target/areaId inputs for this tool; exact cells require a world_editor map patch or supported semantic operation file."), middlewareNotifications);
                    return usedLegacyAlias
                        ? ToolMetadata.AddLegacyAliasDeprecationWarning(coordinateResult, name, tool.Name)
                        : coordinateResult;
                }

                var blocked = GameContinueRunner.Guard(tool.Name, arguments);
                var result = ToolCallMiddleware.Inject(blocked ?? tool.Handler(arguments ?? new JObject()), middlewareNotifications);
                return usedLegacyAlias
                    ? ToolMetadata.AddLegacyAliasDeprecationWarning(result, name, tool.Name)
                    : result;
            }
            catch (Exception ex)
            {
                var errorResult = ToolCallMiddleware.Inject(CallToolResult.Error($"Tool execution error: {ex.Message}"), middlewareNotifications);
                return usedLegacyAlias
                    ? ToolMetadata.AddLegacyAliasDeprecationWarning(errorResult, name, tool.Name)
                    : errorResult;
            }
        }

        internal static bool IsCoordinateTool(string name)
        {
            return string.Equals(name, "coordinate_control", StringComparison.Ordinal)
                || string.Equals(name, "world_editor", StringComparison.Ordinal);
        }

        internal static bool IsCoordinateParameter(string name)
        {
            switch (name)
            {
                case "x":
                case "y":
                case "x1":
                case "y1":
                case "x2":
                case "y2":
                case "dx":
                case "dy":
                case "cell":
                case "cells":
                case "points":
                case "anchors":
                    return true;
                default:
                    return false;
            }
        }

        private static bool ContainsCoordinateArguments(JToken token)
        {
            if (token == null)
                return false;

            if (token.Type == JTokenType.Object)
            {
                foreach (var property in ((JObject)token).Properties())
                {
                    if (IsCoordinateParameter(property.Name) || ContainsCoordinateArguments(property.Value))
                        return true;
                }
            }
            else if (token.Type == JTokenType.Array)
            {
                foreach (var item in (JArray)token)
                {
                    if (ContainsCoordinateArguments(item))
                        return true;
                }
            }

            return false;
        }
    }

}
