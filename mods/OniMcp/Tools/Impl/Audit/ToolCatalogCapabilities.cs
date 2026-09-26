using System.Linq;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    internal static class ToolCatalogCapabilities
    {
        internal static JObject Read()
        {
            var batch = new JArray();
            foreach (string name in new[] { "colony_control", "dupes_control", "read_control", "search_control" })
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
                ["stateRead"] = "world_editor command=read path=/active/index.md includeState=true",
                ["symbolsRead"] = "world_editor command=symbols queries=[...]",
                ["help"] = "includeHelp=true; responseMode=full preserves edit diagnostics"
            };
        }
    }
}
