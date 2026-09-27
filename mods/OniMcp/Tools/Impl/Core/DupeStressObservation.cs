using System;
using System.Collections.Generic;
using Klei.AI;

namespace OniMcp.Tools
{
    internal static class DupeStressObservation
    {
        internal static Dictionary<string, object> Read(MinionIdentity dupe)
        {
            var attribute = dupe.GetAttributes()?.Get(Db.Get().Amounts.Stress.deltaAttribute.Id);
            if (attribute == null) return null;
            var modifiers = new List<Dictionary<string, object>>();
            for (int i = 0; i < attribute.Modifiers.Count; i++)
            {
                var modifier = attribute.Modifiers[i];
                if (modifier.Value == 0) continue;
                modifiers.Add(new Dictionary<string, object> {
                    ["source"] = ToolUtil.CleanName(modifier.GetDescription()),
                    ["value"] = Math.Round(modifier.Value, 5), ["multiplier"] = modifier.IsMultiplier
                });
            }
            return new Dictionary<string, object> {
                ["changePerCycle"] = Math.Round(attribute.GetTotalValue() * 600, 2),
                ["modifierUnits"] = "stress_per_second_or_multiplier", ["contributors"] = modifiers
            };
        }
    }
}
