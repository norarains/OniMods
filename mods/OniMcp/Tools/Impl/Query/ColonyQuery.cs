using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;

namespace OniMcp.Tools
{
    internal static partial class ColonyQuery
    {
        private static FactField F(string name, string type = "number", string unit = null, string cost = "cheap") => new FactField(name, type, unit, cost);
        private static IEnumerable<FactField> Identity => new[] {
            F("id"), F("prefabId", "string"), F("name", "string"), F("worldId"), F("x", unit:"cell"), F("y", unit:"cell"), F("position", "object")
        };
        internal static Dictionary<string, FactDataset> Datasets()
        {
            var building = Identity.Concat(new[] {
                F("blueprint", "boolean"), F("orientation", "string"), F("isOperational", "boolean"), F("isActive", "boolean"),
                F("priority"), F("statuses", "array", cost:"native_status"), F("work", "array", "seconds", "native_work"),
                F("config", "object", cost:"native_config"), F("ports", "array", cost:"native_ports")
                , F("capabilities", "string[]", cost:"native_config")
            }).ToArray();
            var result = new Dictionary<string, FactDataset>(StringComparer.OrdinalIgnoreCase) {
                ["buildings"] = new FactDataset("buildings", building, () => BuildingRows(false)),
                ["building_warnings"] = new FactDataset("building_warnings", building, () => BuildingRows(true)),
                ["ports"] = new FactDataset("ports", Identity.Concat(new[] { F("blueprint", "boolean"), F("ports", "array", cost:"native_ports") }), () => BuildingRows(false)),
                ["building_defs"] = new FactDataset("building_defs", new[] {
                    F("prefabId", "string"), F("name", "string"), F("width", unit:"cell"), F("height", unit:"cell"),
                    F("unlocked", "boolean"), F("availableNow", "boolean"), F("requiresPower", "boolean"), F("powerWatts", unit:"W"),
                    F("materialCategories", "string[]"), F("placement", "object", cost:"native_definition"), F("details", "object", cost:"materials_and_definition")
                    , F("categories", "string[]"), F("searchTerms", "string[]"), F("description", "string")
                }, DefinitionRows),
                ["items"] = new FactDataset("items", Identity.Concat(new[] {
                    F("elementId", "string"), F("massKg", unit:"kg"), F("units", unit:"count"), F("stored", "boolean"), F("storageId"),
                    F("temperatureK", unit:"K"), F("caloriesKcal", unit:"kcal"), F("freshnessPercent", unit:"%"),
                    F("refrigeration", "string"), F("atmosphere", "string"), F("cellReachable", "boolean", cost:"navigation")
                }), ItemRows),
                ["dupes"] = new FactDataset("dupes", Identity.Concat(new[] {
                    F("healthPercent", unit:"%"), F("breath", unit:"%"), F("stress", unit:"%"), F("stamina", unit:"%"),
                    F("caloriesKcal", unit:"kcal"), F("temperatureK", unit:"K"), F("skillPoints"), F("skills", "string[]", cost:"native_skills"),
                    F("chore", "string"), F("schedule", "string")
                }), DupeRows),
                ["orders"] = new FactDataset("orders", Identity.Concat(new[] {
                    F("kind", "string"), F("priority"), F("workSecondsRemaining", unit:"s"),
                    F("materials", "object", "tag:kg/units", "native_fetch"), F("statuses", "array", cost:"native_status"),
                    F("cellReachable", "boolean", cost:"navigation")
                }), OrderRows)
            };
            foreach (string name in new[] { "buildings", "building_warnings", "ports" })
                result[name].IndexedRows = predicate => IndexedBuildingRows(predicate, name == "building_warnings");
            result["building_warnings"].Required = row => (row.Get("statuses") as JArray)?.Count > 0;
            return result;
        }

        internal static CallToolResult Handle(JObject args)
        {
            try
            {
                QueryArguments.Validate(args);
                var datasets = Datasets();
                string action = args["action"]?.ToString();
                if (action == "schema") return CallToolResult.Text(Schema(datasets, args["dataset"]?.ToString()).ToString(Formatting.None));
                if (action != "select") throw new ArgumentException("query action must be schema or select");
                if (Game.Instance == null || Game.Instance.IsLoading()) return CallToolResult.Error("A loaded game is required");
                if (SpeedControlScreen.Instance == null || !SpeedControlScreen.Instance.IsPaused) return CallToolResult.Error("Pause before querying a consistent colony snapshot");
                var plan = FactQueryParser.Parse(args["query"]?.ToString(), AreaPredicate);
                if (!datasets.TryGetValue(plan.Dataset, out var dataset)) throw new ArgumentException("Unknown dataset: " + plan.Dataset + "; request query/schema");
                int frame = Time.frameCount;
                var result = FactQueryExecutor.Execute(plan, dataset);
                result["sample"] = new JObject { ["frame"] = frame,
                    ["cycle"] = Math.Round(GameUtil.GetCurrentCycle() + (GameClock.Instance?.GetCurrentCycleAsPercentage() ?? 0f), 4),
                    ["scope"] = plan.Dataset == "building_defs" ? "installed_definitions" : "player_discovered_objects_all_worlds_unless_filtered" };
                return CallToolResult.Text(result.ToString(Formatting.None));
            }
            catch (ArgumentException ex) { return CallToolResult.Error(ex.Message); }
        }

        private static JObject Schema(Dictionary<string, FactDataset> datasets, string name)
        {
            if (string.IsNullOrEmpty(name)) return new JObject {
                ["datasets"] = new JArray(datasets.Keys), ["grammar"] = "SELECT fields|COUNT(*)|SUM(field) [AS alias] FROM dataset [WHERE comparisons AND/OR ...] [GROUP BY field] [ORDER BY field ASC|DESC] [LIMIT 1..200] [OFFSET n]",
                ["predicates"] = "= != < <= > >= CONTAINS, IS [NOT] NULL, has_status('nativeId'), in_area('handle'), near(x,y,radiusCells)",
                ["semantics"] = "Exact comparisons are case-sensitive; CONTAINS is case-insensitive. All queries are read-only and require pause. null means unknown/not applicable. SUM is null if any input is unknown; COUNT(field) counts known values. No joins or mutations.",
                ["budgets"] = new JObject { ["scan"] = FactQueryExecutor.MaxScan, ["milliseconds"] = 250, ["outputChars"] = FactQueryExecutor.MaxOutputChars },
                ["discovery"] = "action=schema dataset=<name>"
            };
            if (!datasets.TryGetValue(name, out var dataset)) throw new ArgumentException("Unknown dataset: " + name);
            return new JObject {
                ["dataset"] = dataset.Name, ["columns"] = new JArray("field", "type", "unit", "cost"),
                ["fields"] = new JArray(dataset.Fields.Values.Select(f => new JArray(f.Name, f.Type, f.Unit, f.Cost))),
                ["notes"] = name == "orders" ? "Queued construction, dig and marked deconstruction only. materials=null means fetch evidence unavailable. cellReachable checks the exact cell, not work/fetch eligibility."
                    : name == "ports" ? "One row per owner; ports contains native port cells, roles and local line evidence. Blueprint connections are unknown."
                    : name == "items" ? "Visible pickupables, including stored objects. Mass does not establish fetchability. cellReachable is explicit navigation, not delivery eligibility."
                    : name == "building_warnings" ? "One row per building with native warning statuses. Includes intended interactions; no stop or repair is implied."
                    : name == "buildings" ? "Includes completed buildings, visible geysers and blueprints. work lists native Workables, not pending orders. Config thresholds use native units (temperature K)."
                    : "Exact canonical IDs and localized names; null is unknown/not applicable."
            };
        }

        private static FactPredicate AreaPredicate(string id)
        {
            if (!AreaHandleRegistry.TryGet(id, out var area)) throw new ArgumentException("Unknown area handle: " + id);
            FactPredicate result = null;
            foreach (var item in new[] {
                new { field = "worldId", op = "=", value = area.WorldId },
                new { field = "x", op = ">=", value = area.X1 }, new { field = "x", op = "<=", value = area.X2 },
                new { field = "y", op = ">=", value = area.Y1 }, new { field = "y", op = "<=", value = area.Y2 }
            })
            {
                var predicate = new FactPredicate { Field = item.field, Operator = item.op, Value = new JValue(item.value) };
                result = result == null ? predicate : new FactPredicate { Operator = "AND", Left = result, Right = predicate };
            }
            return result;
        }

        internal static CallToolResult ReadWarnings(JObject args)
        {
            // A named view uses exactly the same reader and budgets as SELECT.
            try
            {
                string sql = QueryArguments.WarningSql(args, ClusterManager.Instance?.activeWorldId ?? -1);
                if (!string.Equals(FactQueryParser.Parse(sql, AreaPredicate).Dataset, "building_warnings", StringComparison.OrdinalIgnoreCase))
                    return CallToolResult.Error("warnings.md queries must select FROM building_warnings");
                return Handle(new JObject { ["action"] = "select", ["query"] = sql });
            }
            catch (ArgumentException ex) { return CallToolResult.Error(ex.Message); }
        }
    }
}
