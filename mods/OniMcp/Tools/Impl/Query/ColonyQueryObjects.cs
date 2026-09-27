using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    internal static partial class ColonyQuery
    {
        private static object IdentityValue(GameObject go, int cell, string field)
        {
            var kpid = go.GetComponent<KPrefabID>();
            switch (field.ToLowerInvariant())
            {
                case "id": return kpid?.InstanceID ?? go.GetInstanceID();
                case "name": return ToolUtil.CleanName(go.GetProperName());
                case "prefabid": return go.GetComponent<Building>()?.Def?.PrefabID ?? kpid?.PrefabTag.Name ?? go.name;
                case "worldid": return Grid.IsValidCell(cell) ? (object)Grid.WorldIdx[cell] : null;
                case "x": return Grid.IsValidCell(cell) ? (object)Grid.CellColumn(cell) : null;
                case "y": return Grid.IsValidCell(cell) ? (object)Grid.CellRow(cell) : null;
                case "position": return Grid.IsValidCell(cell) ? new { x = Grid.CellColumn(cell), y = Grid.CellRow(cell) } : null;
                case "priority": return go.GetComponent<Prioritizable>() is Prioritizable p && p.IsPrioritizable() ? (object)p.GetMasterPriority().priority_value : null;
                default: throw new ArgumentException("Unsupported native field: " + field);
            }
        }

        private static IEnumerable<FactRow> BuildingRows(bool warnings, IEnumerable<GameObject> candidates = null)
        {
            foreach (var go in candidates ?? GameControlTools.BuildingReadCandidates(true))
            {
                int cell = Grid.PosToCell(go);
                if (!ToolUtil.VisibleCellAllowed(cell, true)) continue;
                yield return new FactRow(field => {
                    switch (field.ToLowerInvariant())
                    {
                        case "blueprint": return go.GetComponent<Constructable>() != null;
                        case "orientation": return go.GetComponent<Rotatable>()?.GetOrientation().ToString() ?? "Neutral";
                        case "isoperational": return go.GetComponent<Operational>()?.IsOperational;
                        case "isactive": return go.GetComponent<Operational>()?.IsActive;
                        case "statuses": return Statuses(go, warnings);
                        case "work": return go.GetComponents<Workable>().Select(w => new { type = w.GetType().Name, secondsRemaining = Finite(w.WorkTimeRemaining) }).ToArray();
                        case "config": return BuildingConfigTools.SnapshotConfig(go);
                        case "capabilities": return BuildingConfigTools.SnapshotConfig(go)["capabilities"];
                        case "ports": return InfrastructurePortReadTools.QueryPorts(go);
                        default: return IdentityValue(go, cell, field);
                    }
                });
            }
        }

        internal static JArray Statuses(GameObject go, bool warnings = false)
        {
            var result = new JArray();
            var group = go.GetComponent<KSelectable>()?.GetStatusItemGroup();
            if (group == null) return result;
            foreach (var entry in group)
            {
                if (entry.item == null) continue;
                string type = entry.item.notificationType.ToString();
                if (warnings && !HudFindingPolicy.IsNotificationWarning(type)) continue;
                result.Add(new JObject { ["id"] = entry.item.Id, ["text"] = ToolUtil.CleanName(entry.GetName()), ["type"] = type });
            }
            return result;
        }

        private static IEnumerable<FactRow> DefinitionRows()
        {
            foreach (var def in Assets.BuildingDefs)
            {
                if (def == null) continue;
                yield return new FactRow(field => {
                    switch (field.ToLowerInvariant())
                    {
                        case "prefabid": return def.PrefabID;
                        case "name": return ToolUtil.CleanName(def.Name);
                        case "width": return def.WidthInCells;
                        case "height": return def.HeightInCells;
                        case "unlocked": return DefinitionUnlocked(def);
                        case "availablenow": return DefinitionAvailable(def);
                        case "requirespower": return def.RequiresPowerInput;
                        case "powerwatts": return def.EnergyConsumptionWhenActive;
                        case "materialcategories": return def.MaterialCategory;
                        case "categories": return BuildPlanningTools.BuildingCategories(def);
                        case "searchterms": return def.SearchTerms;
                        case "description": return ToolUtil.CleanName(def.Desc);
                        case "placement": return BuildPlanningTools.BuildDefPlacementToDictionary(def);
                        case "details": return BuildPlanningTools.BuildingDefToDictionary(def);
                        default: throw new ArgumentException("Unsupported definition field: " + field);
                    }
                });
            }
        }

        private static bool? DefinitionUnlocked(BuildingDef def)
        {
            try
            {
                if (DebugHandler.InstantBuildMode || (Game.Instance != null && Game.Instance.SandboxModeActive)) return true;
                return Db.Get()?.Techs?.IsTechItemComplete(def.PrefabID);
            }
            catch { return null; }
        }

        private static bool? DefinitionAvailable(BuildingDef def)
        {
            try { return def.IsAvailable() ? DefinitionUnlocked(def) : false; }
            catch { return null; }
        }

        private static IEnumerable<FactRow> ItemRows()
        {
            foreach (var item in Components.Pickupables.Items)
            {
                if (item == null) continue;
                int cell = ToolUtil.PickupableCell(item);
                if (!ToolUtil.VisibleCellAllowed(cell, true)) continue;
                yield return new FactRow(field => {
                    var go = item.gameObject;
                    var primary = item.PrimaryElement ?? go.GetComponent<PrimaryElement>();
                    switch (field.ToLowerInvariant())
                    {
                        case "elementid": return primary?.ElementID.ToString();
                        case "masskg": return primary == null ? null : Finite(primary.Mass);
                        case "units": return primary == null ? null : Finite(primary.Units);
                        case "stored": return item.storage != null || (item.KPrefabID?.HasTag(GameTags.Stored) ?? false);
                        case "storageid": return item.storage?.GetComponent<KPrefabID>()?.InstanceID;
                        case "temperaturek": return primary == null ? null : Finite(primary.Temperature);
                        case "calorieskcal": return go.GetComponent<Edible>() is Edible edible ? Finite(edible.Calories / 1000.0) : null;
                        case "freshnesspercent": return go.GetSMI<Rottable.Instance>() is Rottable.Instance rot ? Finite(rot.RotConstitutionPercentage * 100) : null;
                        case "refrigeration": return go.GetSMI<Rottable.Instance>() is Rottable.Instance r ? Rottable.RefrigerationLevel(r).ToString() : null;
                        case "atmosphere": return go.GetSMI<Rottable.Instance>() is Rottable.Instance a ? Rottable.AtmosphereQuality(a).ToString() : null;
                        case "cellreachable": return CellReachable(cell);
                        default: return IdentityValue(go, cell, field);
                    }
                });
            }
        }

        private static object Finite(double value) => double.IsNaN(value) || double.IsInfinity(value) ? null : (object)value;
        private static object CellReachable(int cell)
        {
            if (!Grid.IsValidCell(cell)) return null;
            bool known = false;
            foreach (var dupe in Components.LiveMinionIdentities.Items)
            {
                if (dupe == null || dupe.GetMyWorldId() != Grid.WorldIdx[cell]) continue;
                var navigator = dupe.GetComponent<Navigator>();
                if (navigator == null) continue;
                known = true;
                if (navigator.CanReach(cell)) return true;
            }
            return known ? (object)false : null;
        }
    }

    public static partial class InfrastructurePortReadTools
    {
        internal static object QueryPorts(GameObject go)
        {
            var building = go.GetComponent<Building>();
            var def = building?.Def ?? ResolveBuildingDef(go);
            if (def == null) return null;
            var ports = BuildPorts(go, building, def, "all").ToArray();
            if (go.GetComponent<Constructable>() != null)
                foreach (var port in ports) { port["connected"] = null; port["source"] = "blueprint_definition"; }
            return ports;
        }
    }
}
