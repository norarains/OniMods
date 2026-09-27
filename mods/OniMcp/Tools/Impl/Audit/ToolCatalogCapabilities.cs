using System.Linq;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    internal static class ToolCatalogCapabilities
    {
        internal static JObject Read()
        {
            var batch = new JArray();
            foreach (string name in new[] { "colony_control", "dupes_control", "read_control" })
                if (ToolBatchTools.TryGetBatchOperation(name, out _))
                    batch.Add(name);
            bool editMarks = OniToolRegistry.TryGetTool("game_control", out var game)
                && game.Parameters != null && game.Parameters.TryGetValue("uiDomain", out var ui)
                && ui.EnumValues != null && ui.EnumValues.Contains("edit_mark");
            return new JObject
            {
                ["publicTools"] = new JArray(OniToolRegistry.GetVisibleTools().Select(tool => tool.Name).OrderBy(name => name)),
                ["batchOperations"] = batch,
                ["batchRoute"] = "server_control domain=batch action=call_many calls=[{tool:<operation>,args:{...}}]",
                ["operationDiscovery"] = "server_control domain=catalog action=search query=<intent> detail=full; inspect operations[].call",
                ["editMarks"] = editMarks,
                ["boundedContinue"] = new JObject
                {
                    ["call"] = "game_control domain=speed action=continue seconds=15",
                    ["maxRealSeconds"] = 20, ["returnsPaused"] = true,
                    ["schemaVersion"] = 3, ["defaultResponseMode"] = "summary",
                    ["fullResponseMode"] = "full", ["coverageProfile"] = "colony_v1",
                    ["defaultStopEvents"] = new JArray(ContinueStopEvents.Defaults),
                    ["settingsCall"] = "game_control domain=speed action=stop_events",
                    ["directOnly"] = true,
                    ["policy"] = "Advances until duration_elapsed or an enabled event; returns facts, no recommendation. ignoreEvents/unignoreEvents persist per session, including across finding resolution and save loads. Ignored findings remain visible; summary omits unchanged ignored finding events. Enabled events in the final sample take precedence over a simultaneous deadline. No implicit acknowledgement or review horizon."
                },
                ["query"] = "server_control domain=query action=select query=SELECT...; schema [dataset=name] for discovery",
                ["stateRead"] = "world_editor command=read path=/active/index.md includeState=true",
                ["symbolsRead"] = "world_editor command=symbols queries=[...]",
                ["help"] = "includeHelp=true; responseMode=full preserves edit diagnostics"
            };
        }
    }
}
