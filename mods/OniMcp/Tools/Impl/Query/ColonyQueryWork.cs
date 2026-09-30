using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace OniMcp.Tools
{
    internal static partial class ColonyQuery
    {
        private static IEnumerable<FactRow> DupeRows()
        {
            foreach (var dupe in Components.LiveMinionIdentities.Items)
            {
                if (dupe == null) continue;
                int cell = Grid.PosToCell(dupe);
                yield return new FactRow(field => {
                    var go = dupe.gameObject;
                    switch (field.ToLowerInvariant())
                    {
                        case "healthpercent": return dupe.GetComponent<Health>() is Health health ? Finite(health.hitPoints / Math.Max(1f, health.maxHitPoints) * 100.0) : null;
                        case "breath": return Amount(dupe, "Breath");
                        case "stress": return Amount(dupe, "Stress");
                        case "stamina": return Amount(dupe, "Stamina");
                        case "calorieskcal": return Amount(dupe, "Calories", 0.001);
                        case "temperaturek": return Amount(dupe, "Temperature");
                        case "skillpoints": return dupe.GetComponent<MinionResume>()?.AvailableSkillpoints;
                        case "skills": return dupe.GetComponent<MinionResume>()?.MasteryBySkillID.Where(p => p.Value).Select(p => p.Key).OrderBy(x => x).ToArray();
                        case "chore": return dupe.GetComponent<ChoreConsumer>()?.choreDriver?.GetCurrentChore()?.choreType?.Id;
                        case "schedule": return dupe.GetComponent<Schedulable>()?.GetSchedule()?.GetCurrentScheduleBlock()?.GroupId;
                        default: return IdentityValue(go, cell, field);
                    }
                });
            }
        }

        private static object Amount(MinionIdentity dupe, string name, double scale = 1)
        {
            double value = DupeAmountUtil.AmountValueByName(dupe, name, -1);
            return value < 0 ? null : Finite(value * scale);
        }

        private static IEnumerable<FactRow> OrderRows()
        {
            foreach (var item in UnityEngine.Object.FindObjectsByType<Constructable>(FindObjectsSortMode.None))
                if (item != null && PlayerVisibility.Object(item.gameObject)) yield return OrderRow(item.gameObject, "construction", item);
            foreach (var item in Components.Diggables.Items)
                if (item != null && PlayerVisibility.Object(item.gameObject)) yield return OrderRow(item.gameObject, "dig", item);
            foreach (var item in UnityEngine.Object.FindObjectsByType<Deconstructable>(FindObjectsSortMode.None))
                if (item != null && item.IsMarkedForDeconstruction() && PlayerVisibility.Object(item.gameObject)) yield return OrderRow(item.gameObject, "deconstruction", item);
        }

        private static FactRow OrderRow(GameObject go, string kind, Component component)
        {
            int cell = Grid.PosToCell(go);
            FactQueryStatusSnapshot statuses = null;
            return new FactRow(field => {
                switch (field.ToLowerInvariant())
                {
                    case "kind": return kind;
                    case "worksecondsremaining": return component is Workable work ? Finite(work.WorkTimeRemaining) : null;
                    case "materials": return component is Constructable constructable ? RemainingMaterials(constructable) : null;
                    case "statusids": return (statuses ?? (statuses = NativeStatusSnapshot(go))).Ids;
                    case "statuses": return (statuses ?? (statuses = NativeStatusSnapshot(go))).Details;
                    case "cellreachable": return CellReachable(cell);
                    default: return IdentityValue(go, cell, field);
                }
            });
        }

        private static object RemainingMaterials(Constructable constructable)
        {
            // Native remaining fetch amounts, not inventory mass or estimated deliverability.
            var field = typeof(Constructable).GetField("fetchList", BindingFlags.Instance | BindingFlags.NonPublic);
            var fetch = field?.GetValue(constructable) as IFetchList;
            var remaining = fetch?.GetRemaining();
            return remaining?.ToDictionary(p => p.Key.Name + (Assets.IsTagCountable(p.Key) ? ":units" : ":kg"), p => Finite(p.Value));
        }
    }
}
