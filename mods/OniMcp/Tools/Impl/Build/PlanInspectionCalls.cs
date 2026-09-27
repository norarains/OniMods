using System.Collections.Generic;

namespace OniMcp.Tools
{
    internal static class PlanInspectionCalls
    {
        internal static Dictionary<string, object> Preview(string plan) => new Dictionary<string, object> {
            ["tool"] = "world_editor", ["arguments"] = new Dictionary<string, object> {
                ["command"] = "edit", ["path"] = "/active/buildings/plans.oni",
                ["content"] = "<<<<<<< SEARCH\n=======\n" + plan + "\n>>>>>>> REPLACE",
                ["dryRun"] = true, ["confirm"] = false, ["task"] = "Preview the requested construction plan"
            }
        };

        internal static Dictionary<string, object> WorldSearch(string pattern) => new Dictionary<string, object> {
            ["tool"] = "server_control", ["arguments"] = new Dictionary<string, object> {
                ["domain"] = "batch", ["action"] = "call_many", ["responseMode"] = "full",
                ["task"] = "Inspect the requested world pattern",
                ["calls"] = new[] { new Dictionary<string, object> {
                    ["tool"] = "read_control", ["args"] = new Dictionary<string, object> {
                        ["domain"] = "world", ["action"] = "search", ["pattern"] = pattern,
                        ["matchMode"] = "smart", ["direction"] = "both", ["limit"] = 5
                    }
                } }
            }
        };
    }
}
