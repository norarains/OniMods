using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static Dictionary<string, object> BuildExecutionPlan(string intent, string input, List<PlanSequenceItem> worldItems)
        {
            return new Dictionary<string, object> {
                ["intent"] = intent,
                ["oneCall"] = intent == "world_pattern" || intent == "world_cell"
                    ? PlanInspectionCalls.WorldSearch(string.Join("-", worldItems.Select(item => item.ElementId)))
                    : PlanInspectionCalls.Preview(input),
                ["validation"] = "parse_only; executable preview performs native placement validation"
            };
        }
    }
}
