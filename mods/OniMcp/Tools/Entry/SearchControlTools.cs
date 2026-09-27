using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;

namespace OniMcp.Tools
{
    internal static class SearchControlTools
    {
        internal static McpTool ControlSearch()
        {
            return new McpTool
            {
                Name = "search_control",
                Group = "search",
                Mode = "read",
                Risk = "none",
                Aliases = new List<string> { "find_control", "search_action_control" },
                Tags = new List<string> { "search", "find", "targeting", "read-only", "workflow" },
                Description = "Dedicated read-only search entrypoint. Searches tools, world objects, resources, buildings, dupes, or authoritative map glyph mappings.",
                Parameters = new Dictionary<string, McpToolParameter>
                {
                    ["domain"] = new McpToolParameter { Type = "string", Description = "Search domain: tools, world, resources, buildings, dupes, or glyphs (aliases symbols/codes).", Required = true, EnumValues = new List<string> { "tools", "world", "resources", "buildings", "dupes", "glyphs" } },
                    ["query"] = new McpToolParameter { Type = "string", Description = "Search text. Matches names, ids, tags, elements, buildings, tools, or dupes depending on domain.", Required = false },
                    ["queries"] = new McpToolParameter { Type = "array", Description = "domain=glyphs: batch glyph codes or names, at most 100 strings.", Required = false },
                    ["direction"] = new McpToolParameter { Type = "string", Description = "domain=glyphs: auto, code_to_meaning, or meaning_to_code.", Required = false, EnumValues = new List<string> { "auto", "code_to_meaning", "meaning_to_code" } },
                    ["matchMode"] = new McpToolParameter { Type = "string", Description = "domain=glyphs: auto, exact, or contains.", Required = false, EnumValues = new List<string> { "auto", "exact", "contains" } },
                    ["view"] = new McpToolParameter { Type = "string", Description = "domain=glyphs: optional overlay context used to filter contextual meanings.", Required = false },
                    ["perQueryLimit"] = new McpToolParameter { Type = "integer", Description = "domain=glyphs: matches per query, default 20, maximum 100.", Required = false },
                    ["target"] = new McpToolParameter { Type = "string", Description = "Alias for query when the caller is searching for a target to act on.", Required = false },
                    ["search"] = new McpToolParameter { Type = "string", Description = "Alias for query.", Required = false },
                    ["intent"] = new McpToolParameter { Type = "string", Description = "Optional intended follow-up, such as dig, mop, build, inspect, configure, move, prioritize, or explain.", Required = false },
                    ["kind"] = new McpToolParameter { Type = "string", Description = "Optional subtype, such as cells, buildings, items, resources, dupes, database, or guide.", Required = false },
                    ["kinds"] = new McpToolParameter { Type = "array", Description = "Optional subtype list for world searches.", Required = false },
                    ["category"] = new McpToolParameter { Type = "string", Description = "Building category filter for domain=buildings.", Required = false },
                    ["group"] = new McpToolParameter { Type = "string", Description = "Tool group filter for domain=tools.", Required = false },
                    ["mode"] = new McpToolParameter { Type = "string", Description = "Tool mode filter for domain=tools: read, write, execute, or any.", Required = false },
                    ["risk"] = new McpToolParameter { Type = "string", Description = "Tool risk filter for domain=tools: none, low, medium, dangerous, or any.", Required = false },
                    ["includeStored"] = new McpToolParameter { Type = "boolean", Description = "domain=resources: include stored items, default follows the resource search implementation.", Required = false },
                    ["looseOnly"] = new McpToolParameter { Type = "boolean", Description = "domain=resources: only return loose pickupable items.", Required = false },
                    ["includeUnavailable"] = new McpToolParameter { Type = "boolean", Description = "domain=buildings: include unavailable or locked building definitions.", Required = false },
                    ["areaId"] = new McpToolParameter { Type = "string", Description = "Optional reusable area handle to constrain searches that support area filtering.", Required = false },
                    ["worldId"] = new McpToolParameter { Type = "integer", Description = "Optional world id filter.", Required = false },
                    ["visibleOnly"] = new McpToolParameter { Type = "boolean", Description = "World search visibility filter when supported.", Required = false },
                    ["detail"] = new McpToolParameter { Type = "string", Description = "Result detail level: brief, compact, or full.", Required = false, EnumValues = new List<string> { "brief", "compact", "full" } },
                    ["limit"] = new McpToolParameter { Type = "integer", Description = "Maximum number of results.", Required = false }
                },
                Handler = args =>
                {
                    string domain = NormalizeDomain(args["domain"]?.ToString());
                    if (string.IsNullOrWhiteSpace(domain))
                        return CallToolResult.Error("domain is required");

                    var forwarded = BuildForwardArgs(args);
                    var searchResult = DispatchSearch(domain, forwarded);
                    if (searchResult.IsError)
                        return searchResult;
                    if (domain == "glyphs")
                        return searchResult;

                    var parsed = ParseResult(searchResult);
                    var response = new JObject
                    {
                        ["v"] = 1,
                        ["tool"] = "search_control",
                        ["domain"] = domain,
                        ["query"] = Query(args),
                        ["intent"] = args["intent"]?.DeepClone(),
                        ["searchResult"] = parsed,
                        ["readOnly"] = true
                    };

                    return CallToolResult.Text(JsonConvert.SerializeObject(response, McpJsonUtil.Settings));
                }
            };
        }

        private static CallToolResult DispatchSearch(string domain, JObject args)
        {
            switch (domain)
            {
                case "tools":
                case "catalog":
                    return ToolCatalogTools.SearchTools().Handler(args);
                case "world":
                case "map":
                    return WorldSearchTools.SearchWorld().Handler(args);
                case "resources":
                case "items":
                    return InventoryTools.SearchItems().Handler(args);
                case "buildings":
                case "build":
                    return BuildPlanningTools.SearchBuildables().Handler(args);
                case "dupes":
                case "duplicants":
                    args["kinds"] = new JArray("dupes");
                    return WorldSearchTools.SearchWorld().Handler(args);
                case "glyphs":
                    return WorldEditorTools.SearchGlyphs(args);
                case "knowledge":
                case "database":
                case "guide":
                    return CallToolResult.Error("search_control domain=knowledge/database/guide is disabled because in-game database queries are crash-prone on this runtime. Use external docs or static repo data instead.");
                default:
                    return CallToolResult.Error("domain must be tools, world, resources, buildings, dupes, or glyphs");
            }
        }

        private static JObject BuildForwardArgs(JObject args)
        {
            var forwarded = args == null ? new JObject() : (JObject)args.DeepClone();
            string query = Query(args);
            if (!string.IsNullOrWhiteSpace(query))
                forwarded["query"] = query;

            forwarded.Remove("domain");
            forwarded.Remove("target");
            forwarded.Remove("search");
            forwarded.Remove("intent");
            forwarded.Remove("actionTool");
            forwarded.Remove("actionDomain");
            forwarded.Remove("action");

            return forwarded;
        }

        private static JToken ParseResult(CallToolResult result)
        {
            string text = result.Content != null && result.Content.Count > 0 ? result.Content[0].Text : string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                return new JObject();

            try
            {
                return JToken.Parse(text);
            }
            catch
            {
                return new JObject { ["text"] = text };
            }
        }

        private static string Query(JObject args)
        {
            return (args?["query"] ?? args?["target"] ?? args?["search"])?.ToString();
        }

        private static string NormalizeDomain(string domain)
        {
            string normalized = (domain ?? string.Empty).Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "symbols":
                case "codes":
                    return "glyphs";
                default:
                    return normalized;
            }
        }
    }
}
