using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    public static partial class ToolCatalogTools
    {
        private static readonly string[] DiscoverableBatchOperations = {
            "colony_control", "dupes_control", "read_control"
        };

        private static List<Dictionary<string, object>> SearchBatchOperations(string query, string group,
            string mode, string risk, string detail, int limit)
        {
            var rows = new List<Dictionary<string, object>>();
            bool exactOperation = DiscoverableBatchOperations.Contains(query.Trim());
            foreach (string name in DiscoverableBatchOperations)
            {
                if (exactOperation && name != query.Trim()) continue;
                if (!ToolBatchTools.TryGetBatchOperation(name, out var tool)) continue;
                if (!string.IsNullOrEmpty(group) && !string.Equals(group, tool.Group, StringComparison.OrdinalIgnoreCase)) continue;
                if (mode != "any" && !string.IsNullOrEmpty(mode) && mode != tool.Mode) continue;
                if (risk != "any" && !string.IsNullOrEmpty(risk) && risk != tool.Risk) continue;
                int score = Score(tool, ExpandQuery(query), query);
                if (score == 0) continue;
                var row = ManifestByDetail(tool, score, NormalizeDetail(detail));
                row["score"] = score;
                row["directlyCallable"] = false;
                var args = new JObject();
                if (name == "colony_control" && (query.Contains("research") || query.Contains("研究") || query.Contains("科技")))
                {
                    args["domain"] = "management";
                    args["kind"] = "research";
                    args["action"] = query.Contains("list") || query.Contains("列表") ? "list" : "status";
                    if (args["action"].ToString() == "list") { args["includeComplete"] = false; args["limit"] = 10; }
                }
                if (name == "colony_control" && new[] { "plant", "farm", "seed", "uproot", "种植", "铲除" }.Any(query.Contains))
                {
                    args["domain"] = "bio"; args["bioDomain"] = "farming";
                    args["action"] = query.Contains("seed") ? "seed_catalog" : "list_planting";
                    args["limit"] = 10;
                }
                row["call"] = new JObject
                {
                    ["tool"] = "server_control",
                    ["args"] = new JObject
                    {
                        ["domain"] = "batch", ["action"] = "call_many", ["responseMode"] = "full",
                        ["calls"] = new JArray(new JObject { ["tool"] = name, ["args"] = args })
                    }
                };
                if (!args.HasValues) row["argumentsRequired"] = true;
                rows.Add(row);
            }
            bool printingQuery = new[] { "printing", "care", "reward", "telepad", "打印", "补给" }.Any(query.Contains);
            bool recruitmentQuery = new[] { "recruit", "candidate", "immigration", "duplicant", "dupe", "招募", "候选", "新人", "复制人" }.Any(query.Contains);
            if ((printingQuery || recruitmentQuery) && OniToolRegistry.TryGetTool("building_control", out var buildingTool))
            {
                if (printingQuery)
                {
                    rows.Add(new Dictionary<string, object> {
                        ["name"] = "printing_pod_rewards", ["score"] = 100, ["directlyCallable"] = true,
                        ["call"] = new JObject { ["tool"] = "building_control", ["args"] = new JObject {
                            ["domain"] = "side_surface", ["surface"] = "facility", ["kind"] = "printing_pod",
                            ["action"] = "list_rewards", ["task"] = "Inspect current Printing Pod choices"
                        } }
                    });
                }
                rows.Add(new Dictionary<string, object> {
                    ["name"] = "printing_pod_candidates", ["score"] = recruitmentQuery ? 110 : 90,
                    ["directlyCallable"] = true,
                    ["description"] = "Passive current-round candidates. If unmaterialized, preview/confirm headless prepare_choices; then preview/confirm recruit with exact candidateId and optional maxPopulation.",
                    ["call"] = new JObject { ["tool"] = "building_control", ["args"] = new JObject {
                        ["domain"] = "side_surface", ["surface"] = "facility", ["kind"] = "printing_pod",
                        ["action"] = "list_candidates"
                    } }
                });
            }
            return rows.OrderByDescending(row => Convert.ToInt32(row["score"]))
                .ThenBy(row => row["name"].ToString()).Take(limit).ToList();
        }
    }
}
