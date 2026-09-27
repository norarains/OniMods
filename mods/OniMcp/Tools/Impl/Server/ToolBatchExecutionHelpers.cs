using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    public static partial class ToolBatchTools
    {
        private static Dictionary<string, object> ExecuteSingleCall(int index, JToken callToken, JObject defaults, string responseMode, bool includeArguments, int maxTextChars)
        {
            var call = callToken as JObject;
            if (call == null)
                return ErrorResult(index, null, null, "call entry must be an object");

            string name = call["name"]?.ToString() ?? call["tool"]?.ToString() ?? call["t"]?.ToString();
            if (string.IsNullOrWhiteSpace(name))
                return ErrorResult(index, null, call, "tool name is required");

            var preflight = PreflightSingleCall(index, callToken, defaults, includeArguments);
            if (preflight.ContainsKey("isError") && (bool)preflight["isError"])
                return preflight;

            var argumentsToken = call["arguments"] ?? call["args"] ?? call["a"];
            JObject arguments;
            if (argumentsToken == null || argumentsToken.Type == JTokenType.Null)
            {
                arguments = new JObject();
            }
            else if (argumentsToken.Type == JTokenType.Object)
            {
                arguments = (JObject)argumentsToken.DeepClone();
            }
            else
            {
                return ErrorResult(index, name, call, "arguments must be an object");
            }

            MergeDefaults(arguments, defaults);

            if (IsBatchCall(name, arguments))
                return ErrorResult(index, name, call, $"{ToolName} cannot call itself");

            McpTool tool;
            if (TryGetBatchOperation(name, out tool)
                && RequiresBatchConfirm(tool, arguments))
            {
                return ErrorResult(index, name, call, $"dangerous tool '{tool.Name}' requires arguments.confirm=true");
            }

            var toolResult = OniToolRegistry.CallToolFromWorldEditor(name, arguments, false);
            string text = ExtractText(toolResult);
            var result = new Dictionary<string, object>
            {
                ["index"] = index,
                ["name"] = name,
                ["canonicalName"] = preflight.ContainsKey("canonicalName") ? preflight["canonicalName"] : name,
                ["group"] = preflight.ContainsKey("group") ? preflight["group"] : null,
                ["mode"] = preflight.ContainsKey("mode") ? preflight["mode"] : null,
                ["risk"] = preflight.ContainsKey("risk") ? preflight["risk"] : null,
                ["executed"] = true,
                ["isError"] = toolResult.IsError,
                ["text"] = responseMode == "summary" ? CompactSummary(text, maxTextChars) : Truncate(text, maxTextChars)
            };
            if (preflight.ContainsKey("id"))
                result["id"] = preflight["id"];
            if (includeArguments)
                result["arguments"] = arguments;
            if (responseMode == "summary")
            {
                result.Remove("text");
                result["summary"] = CompactSummaryObject(text, maxTextChars);
            }
            if (responseMode == "full")
            {
                // Keep one representation. JSON stays structured rather than being
                // escaped twice inside text and content copies of the same child.
                result.Remove("text");
                try { result["result"] = JToken.Parse(text); }
                catch (JsonException) { result["text"] = text; }
                if (toolResult.Content != null && toolResult.Content.Any(item => item.Type != "text"))
                    result["content"] = toolResult.Content.Where(item => item.Type != "text").ToList();
            }
            return result;
        }

        private static bool IsBatchCall(string name, JObject arguments)
        {
            McpTool tool;
            if (TryGetBatchOperation(name, out tool)
                && string.Equals(tool.Name, ToolName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return ServerTools.IsServerControlDomainCall(name, arguments, "batch", "call_many", "many");
        }

        private static void MergeDefaults(JObject target, JObject defaults)
        {
            if (target == null || defaults == null)
                return;

            foreach (var property in defaults.Properties())
            {
                if (target[property.Name] != null)
                    continue;

                target[property.Name] = property.Value.DeepClone();
            }
        }

        private static Dictionary<int, string> DuplicateCallWarnings(JArray calls, JObject defaults)
        {
            var warnings = new Dictionary<int, string>();
            var seen = new Dictionary<string, int>();

            for (int i = 0; i < calls.Count; i++)
            {
                var call = calls[i] as JObject;
                if (call == null)
                    continue;

                string name = call["name"]?.ToString() ?? call["tool"]?.ToString() ?? call["t"]?.ToString();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var argumentsToken = call["arguments"] ?? call["args"] ?? call["a"];
                JObject arguments = (argumentsToken as JObject) != null ? (JObject)argumentsToken.DeepClone() : new JObject();
                MergeDefaults(arguments, defaults);

                McpTool tool;
                string canonicalName = TryGetBatchOperation(name, out tool) ? tool.Name : name;
                if (!IsWriteOrExecute(tool))
                    continue;

                string key = canonicalName + ":" + CanonicalJson(arguments);
                int previous;
                if (seen.TryGetValue(key, out previous))
                {
                    string message = $"duplicate write/execute call matches index {previous}; repeated same-area calls usually indicate wrong routing or stale state. Read the previous result before retrying.";
                    warnings[i] = message;
                }
                else
                {
                    seen[key] = i;
                }
            }

            return warnings;
        }

        private static bool IsWriteOrExecute(McpTool tool)
        {
            if (tool == null)
                return true;
            return string.Equals(tool.Mode, "write", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tool.Mode, "execute", StringComparison.OrdinalIgnoreCase);
        }

        private static bool RequiresBatchConfirm(McpTool tool, JObject arguments)
        {
            if (tool == null || !string.Equals(tool.Risk, "dangerous", StringComparison.OrdinalIgnoreCase))
                return false;
            if (ToolUtil.GetBool(arguments, "confirm", false))
                return false;
            if (ToolUtil.GetBool(arguments, "dryRun", false))
                return false;
            if (HasPreviewTokenBypass(tool, arguments))
                return false;
            return !IsKnownReadOnlyAggregateCall(tool.Name, arguments);
        }

        private static bool IsKnownReadOnlyAggregateCall(string toolName, JObject arguments)
        {
            string name = (toolName ?? string.Empty).Trim();
            string domain = (arguments?["domain"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();
            string action = (arguments?["action"]?.ToString() ?? string.Empty).Trim().ToLowerInvariant();

            if (string.Equals(name, "game_control", StringComparison.OrdinalIgnoreCase))
                return (domain == "launch" && (action == "status" || action == "restart_status"))
                    || (domain == "save" && (action == "list" || action == "status"))
                    || (domain == "speed" && action == "time")
                    || (domain == "state" && (action == "status" || action == "time"));

            if (string.Equals(name, "world_editor", StringComparison.OrdinalIgnoreCase))
            {
                string command = arguments?["command"]?.ToString()?.Trim().ToLowerInvariant();
                // zoom/read can synchronize a camera unless explicitly disabled.
                return command == "symbols" || command == "pwd" || command == "ls"
                    || ((command == "read" || command == "zoom") && !ToolUtil.GetBool(arguments, "syncView", false));
            }

            if (string.Equals(name, "building_control", StringComparison.OrdinalIgnoreCase))
                return (new[] { "production", "queue", "fabricator", "crafting" }.Contains(domain)
                    && new[] { "list", "list_fabricators", "fabricators", "list_recipes", "recipes", "mutant_seed_list", "list_mutant_seeds" }.Contains(action))
                    || (new[] { "storage", "stores", "filter", "filters", "tile_selection", "receptacle" }.Contains(domain)
                        && new[] { "list", "detail" }.Contains(action))
                    || (new[] { "side_surface", "surface", "generic_surface" }.Contains(domain)
                        && (arguments?["surface"]?.ToString() ?? "").Equals("user_menu", StringComparison.OrdinalIgnoreCase) && action == "list")
                    || (new[] { "config", "configuration", "side_screen" }.Contains(domain)
                    && new[] { "list", "status", "list_automation", "automation", "get_access", "state_list" }.Contains(action))
                    || domain == "planning" && (
                    action == "parse_plan" || action == "parse_sequence" || action == "parse"
                    || action == "search_defs" || action == "search" || action == "defs"
                    || action == "materials" || action == "preview"
                    || action == "placement_candidates" || action == "candidates" || action == "anchors");

            return false;
        }

        private static bool HasPreviewTokenBypass(McpTool tool, JObject arguments)
        {
            return tool?.Parameters != null
                && tool.Parameters.ContainsKey("previewToken")
                && !string.IsNullOrWhiteSpace(arguments?["previewToken"]?.ToString());
        }

        private static string CanonicalJson(JObject arguments)
        {
            if (arguments == null)
                return "{}";

            var ordered = new JObject();
            foreach (var property in arguments.Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
                ordered[property.Name] = property.Value.DeepClone();
            return ordered.ToString(Formatting.None);
        }

        private static Dictionary<string, object> ErrorResult(int index, string name, JObject call, string message)
        {
            var result = new Dictionary<string, object>
            {
                ["index"] = index,
                ["isError"] = true,
                ["content"] = new List<Dictionary<string, object>>
                {
                    new Dictionary<string, object>
                    {
                        ["type"] = "text",
                        ["text"] = message
                    }
                },
                ["text"] = message
            };

            if (!string.IsNullOrWhiteSpace(name))
                result["name"] = name;
            if (call != null)
                result["call"] = call;

            return result;
        }

        private static string NormalizeResponseMode(string value)
        {
            string mode = string.IsNullOrWhiteSpace(value) ? "summary" : value.Trim().ToLowerInvariant();
            if (mode == "compact")
                return "summary";
            if (mode == "full" || mode == "summary" || mode == "errors")
                return mode;
            return "summary";
        }

    }
}
