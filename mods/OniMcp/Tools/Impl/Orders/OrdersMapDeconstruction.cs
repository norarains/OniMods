using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class OrdersTools
    {
        // Map order tokens address the building layer. Utility layers require their own explicit operation.
        internal static CallToolResult DeconstructMapArea(JObject args)
        {
            int world = ToolUtil.ResolveWorldId(args);
            int x1 = args["x1"].Value<int>(), x2 = args["x2"].Value<int>();
            int y1 = args["y1"].Value<int>(), y2 = args["y2"].Value<int>();
            var targets = new HashSet<GameObject>();
            int cells = 0;
            for (int y = y1; y <= y2; y++)
                for (int x = x1; x <= x2; x++)
                {
                    int cell = Grid.XYToCell(x, y);
                    if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, world)
                        || !ToolUtil.VisibleCellAllowed(cell, true))
                        return CallToolResult.Error("Deconstruction cell outside visible selected world");
                    var candidates = DeconstructCandidatesAtCell(cell, "building").ToList();
                    if (candidates.Count != 1 || !candidates[0].GetComponent<Deconstructable>().allowDeconstruction)
                        return CallToolResult.Error("No single deconstructable building at (" + x + "," + y + "); no orders queued");
                    targets.Add(candidates[0]);
                    cells++;
                }
            bool dryRun = ToolUtil.GetBool(args, "dryRun", false);
            if (!dryRun && !ToolUtil.GetBool(args, "confirm", false))
                return CallToolResult.Error("confirm=true is required for deconstruction");
            if (!dryRun)
                foreach (var target in targets)
                    if (!TryQueueObjectDeconstruction(target, args, out string error)) return CallToolResult.Error(error);
            var receipts = targets.Select(target => {
                var row = DeconstructionTargetInfo(target);
                var priority = target.GetComponent<Prioritizable>();
                row["prioritySupported"] = priority != null;
                if (priority != null) row["actualPriority"] = priority.GetMasterPriority().priority_value;
                return row;
            }).ToList();
            return CallToolResult.Text(JsonConvert.SerializeObject(new {
                ok = true, dryRun, applied = dryRun ? 0 : cells, wouldApply = cells,
                targetCount = targets.Count, targets = receipts, requestedPriority = ToolUtil.GetInt(args, "priority") ?? 5
            }));
        }
    }
}
