using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class InfrastructurePortReadTools
    {
        private static readonly ObjectLayer[] PowerLayers = { ObjectLayer.Wire, ObjectLayer.WireTile, ObjectLayer.ReplacementWire };
        private static readonly ObjectLayer[] LiquidLayers = { ObjectLayer.LiquidConduit, ObjectLayer.LiquidConduitTile, ObjectLayer.ReplacementLiquidConduit };
        private static readonly ObjectLayer[] GasLayers = { ObjectLayer.GasConduit, ObjectLayer.GasConduitTile, ObjectLayer.ReplacementGasConduit };
        private static readonly ObjectLayer[] LogicLayers = { ObjectLayer.LogicWire, ObjectLayer.LogicWireTile, ObjectLayer.ReplacementLogicWire };
        private static readonly ObjectLayer[] RailLayers = { ObjectLayer.SolidConduit, ObjectLayer.SolidConduitTile, ObjectLayer.ReplacementSolidConduit };

        private static IEnumerable<Dictionary<string, object>> BuildPorts(GameObject go, Building building, BuildingDef def, string kind)
        {
            if (Wants(kind, "power"))
            {
                foreach (var port in PowerPorts(go, building, def).Concat(PowerBridgePorts(go, def)))
                    yield return port;
            }
            if (Wants(kind, "liquid") || Wants(kind, "gas"))
            {
                foreach (var port in ConduitPorts(go, building, def, kind))
                    yield return port;
            }
            if (Wants(kind, "logic"))
            {
                foreach (var port in LogicPorts(go))
                    yield return port;
            }
            if (Wants(kind, "rail"))
            {
                foreach (var port in RailPorts(go, building, def))
                    yield return port;
            }
        }

        private static IEnumerable<Dictionary<string, object>> PowerPorts(GameObject go, Building building, BuildingDef def)
        {
            if (building == null)
                yield break;
            if (def.RequiresPowerInput)
                yield return Port("power", "input", "电力输入", building.GetPowerInputCell(), PowerLayers, PowerStatus(go, "input"));
            if (def.RequiresPowerOutput)
                yield return Port("power", "output", "电力输出", building.GetPowerOutputCell(), PowerLayers, PowerStatus(go, "output"));

            var consumer = go.GetComponent<EnergyConsumer>();
            if (consumer != null && !def.RequiresPowerInput)
                yield return Port("power", "consumer", "耗电端", building.GetPowerInputCell(), PowerLayers, PowerStatus(go, "consumer", consumer));
            var generator = go.GetComponent<Generator>();
            if (generator != null && !def.RequiresPowerOutput)
                yield return Port("power", "generator", "发电端", building.GetPowerOutputCell(), PowerLayers, PowerStatus(go, "generator", null, generator));
        }

        private static Dictionary<string, object> Port(string layer, string role, string label, int cell, ObjectLayer[] layers, Dictionary<string, object> extra)
        {
            var result = new Dictionary<string, object>
            {
                ["layer"] = layer,
                ["role"] = role,
                ["label"] = label,
                ["cell"] = CellObject(cell),
                ["hasLine"] = PlayerVisibility.Cell(cell) ? (object)HasLayer(cell, layers) : null,
                ["hasBuiltLine"] = PlayerVisibility.Cell(cell) ? (object)UtilityConnectionRead.IsBuilt(cell, layers) : null,
                ["line"] = PlayerVisibility.Cell(cell) ? LineObject(cell, layers) : null
            };
            if (!PlayerVisibility.Cell(cell)) { result["connected"] = null; return result; }
            foreach (var item in extra)
                result[item.Key] = item.Value;
            return result;
        }

        private static Dictionary<string, object> PowerStatus(GameObject go, string role, EnergyConsumer consumer = null, Generator generator = null)
        {
            consumer = consumer ?? go.GetComponent<EnergyConsumer>();
            generator = generator ?? go.GetComponent<Generator>();
            var battery = go.GetComponent<Battery>();
            ICircuitConnected connection = role == "output" || role == "generator"
                ? (ICircuitConnected)generator ?? battery ?? (ICircuitConnected)consumer
                : (ICircuitConnected)consumer ?? generator ?? (ICircuitConnected)battery;
            ushort circuit = connection is EnergyConsumer c ? c.CircuitID
                : connection is Generator g ? g.CircuitID : connection is Battery b ? b.CircuitID : ushort.MaxValue;
            bool pending = PowerConnectionReadiness.Pending(connection, circuit);
            return new Dictionary<string, object>
            {
                ["connected"] = pending ? (bool?)null : circuit != ushort.MaxValue,
                ["circuitId"] = pending ? (int?)null : circuit == ushort.MaxValue ? -1 : circuit,
                ["networkPending"] = pending,
                ["loadW"] = consumer != null ? (object)Math.Round(ToolUtil.SafeFloat(consumer.WattsNeededWhenActive), 1) : null,
                ["generatorW"] = generator != null ? (object)Math.Round(ToolUtil.SafeFloat(generator.WattageRating), 1) : null,
                ["batteryJ"] = battery != null ? (object)Math.Round(ToolUtil.SafeFloat(battery.JoulesAvailable), 1) : null,
                ["roleHint"] = role
            };
        }

        private static bool Wants(string kind, string layer)
        {
            return kind == "all" || kind == layer || (kind == "conduit" && (layer == "liquid" || layer == "gas"));
        }

        private static string NormalizeKind(string kind)
        {
            kind = (kind ?? "all").Trim().ToLowerInvariant();
            if (kind == "water" || kind == "liquid_conduit" || kind == "pipe") return "liquid";
            if (kind == "gas_conduit" || kind == "vent") return "gas";
            if (kind == "automation" || kind == "signal") return "logic";
            if (kind == "solid" || kind == "conveyor" || kind == "shipping") return "rail";
            if (kind == "electric" || kind == "wire") return "power";
            return string.IsNullOrEmpty(kind) ? "all" : kind;
        }

        private static ObjectLayer[] LayersFor(string layer)
        {
            return layer == "gas" ? GasLayers : LiquidLayers;
        }

        private static bool HasLayer(int cell, ObjectLayer[] layers)
        {
            return PlayerVisibility.Cell(cell) && layers.Any(layer => PlayerVisibility.Object(Grid.Objects[cell, (int)layer]));
        }

        private static Dictionary<string, object> LineObject(int cell, ObjectLayer[] layers)
        {
            var dirs = new List<string>();
            var to = new List<Dictionary<string, object>>();
            AddLineNeighbor(cell, layers, "U", 0, 1, dirs, to);
            AddLineNeighbor(cell, layers, "D", 0, -1, dirs, to);
            AddLineNeighbor(cell, layers, "L", -1, 0, dirs, to);
            AddLineNeighbor(cell, layers, "R", 1, 0, dirs, to);

            return new Dictionary<string, object>
            {
                ["glyph"] = LineGlyph(dirs),
                ["dirs"] = dirs.Count == 0 ? "." : string.Join("", dirs.ToArray()),
                ["to"] = to,
                ["bridge"] = BridgeId(cell)
            };
        }

        private static void AddLineNeighbor(
            int cell,
            ObjectLayer[] layers,
            string dir,
            int dx,
            int dy,
            List<string> dirs,
            List<Dictionary<string, object>> to)
        {
            int neighbor = Grid.XYToCell(Grid.CellColumn(cell) + dx, Grid.CellRow(cell) + dy);
            var bits = UtilityConnectionRead.Read(cell, layers);
            var flag = dir == "U" ? UtilityConnections.Up : dir == "D" ? UtilityConnections.Down
                : dir == "L" ? UtilityConnections.Left : UtilityConnections.Right;
            if ((bits & flag) == 0) return;
            dirs.Add(dir);
            to.Add(CellObject(neighbor));
        }

        private static string LineGlyph(List<string> dirs)
        {
            bool u = dirs.Contains("U");
            bool d = dirs.Contains("D");
            bool l = dirs.Contains("L");
            bool r = dirs.Contains("R");
            int count = (u ? 1 : 0) + (d ? 1 : 0) + (l ? 1 : 0) + (r ? 1 : 0);
            if (count == 0) return ".";
            if (count == 1) return "*";
            if (count == 4) return "十";
            if (u && d && !l && !r) return "|";
            if (l && r && !u && !d) return "一";
            if (u && r && !d && !l) return "└";
            if (u && l && !d && !r) return "┘";
            if (d && r && !u && !l) return "┌";
            if (d && l && !u && !r) return "┐";
            if (u && l && r && !d) return "┴";
            if (d && l && r && !u) return "┬";
            if (u && d && r && !l) return "├";
            if (u && d && l && !r) return "┤";
            return "?";
        }

        private static string BridgeId(int cell)
        {
            var go = PlayerVisibility.Cell(cell) ? PlayerVisibility.Known(Grid.Objects[cell, (int)ObjectLayer.Building]) : null;
            if (go == null)
                return null;
            string id = go.GetComponent<BuildingComplete>()?.name ?? go.name;
            return id.IndexOf("Bridge", StringComparison.OrdinalIgnoreCase) >= 0 ? id : null;
        }

        private static Dictionary<string, object> CellObject(int cell)
        {
            if (!Grid.IsValidCell(cell))
                return new Dictionary<string, object> { ["valid"] = false };
            return new Dictionary<string, object>
            {
                ["x"] = Grid.CellColumn(cell),
                ["y"] = Grid.CellRow(cell),
                ["cell"] = cell,
                ["valid"] = true
            };
        }

        private static BuildingDef ResolveBuildingDef(GameObject go)
        {
            var kpid = go.GetComponent<KPrefabID>();
            string id = kpid?.PrefabTag.Name ?? go.name;
            return string.IsNullOrWhiteSpace(id) ? null : Assets.GetBuildingDef(PrefabIdentity.BaseId(id));
        }

        private static bool HasRectInput(JObject args)
        {
            return !string.IsNullOrWhiteSpace(args["areaId"]?.ToString())
                || args["x1"] != null || args["y1"] != null || args["x2"] != null || args["y2"] != null;
        }


        private static bool HasPointInput(JObject args)
        {
            return ToolUtil.GetInt(args, "x").HasValue && ToolUtil.GetInt(args, "y").HasValue;
        }

        private static Dictionary<string, int> PointRect(JObject args)
        {
            int x = ToolUtil.GetInt(args, "x") ?? 0;
            int y = ToolUtil.GetInt(args, "y") ?? 0;
            int radius = Math.Max(0, Math.Min(ToolUtil.GetInt(args, "radius") ?? 8, 80));
            return new Dictionary<string, int>
            {
                ["x1"] = x - radius,
                ["y1"] = y - radius,
                ["x2"] = x + radius,
                ["y2"] = y + radius
            };
        }

        private static bool CellInRect(int cell, Dictionary<string, int> rect, int worldId)
        {
            if (!Grid.IsValidCell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId))
                return false;
            int x = Grid.CellColumn(cell);
            int y = Grid.CellRow(cell);
            return x >= rect["x1"] && x <= rect["x2"] && y >= rect["y1"] && y <= rect["y2"];
        }

        private static bool MatchesQuery(GameObject go, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;
            string q = query.Trim();
            var def = go.GetComponent<Building>()?.Def ?? ResolveBuildingDef(go);
            string name = ToolUtil.CleanName(go.GetProperName());
            return Contains(name, q) || Contains(def?.PrefabID, q) || Contains(go.name, q);
        }

        private static bool Contains(string value, string query)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static IEnumerable<Constructable> FindConstructables(int worldId)
        {
            Constructable[] constructables = UnityEngine.Object.FindObjectsByType<Constructable>(FindObjectsSortMode.None);
            return constructables.Where(item => item != null && ToolUtil.GameObjectMatchesWorld(item.gameObject, worldId));
        }

        private static Dictionary<string, object> Summarize(List<Dictionary<string, object>> items)
        {
            var ports = items.SelectMany(item => item["ports"] as IEnumerable<Dictionary<string, object>> ?? Enumerable.Empty<Dictionary<string, object>>()).ToList();
            return new Dictionary<string, object>
            {
                ["buildings"] = items.Count,
                ["ports"] = ports.Count,
                ["power"] = ports.Count(p => (string)p["layer"] == "power"),
                ["liquid"] = ports.Count(p => (string)p["layer"] == "liquid"),
                ["gas"] = ports.Count(p => (string)p["layer"] == "gas"),
                ["logic"] = ports.Count(p => (string)p["layer"] == "logic"),
                ["rail"] = ports.Count(p => (string)p["layer"] == "rail")
            };
        }

        private static string PortSummary(List<Dictionary<string, object>> ports)
        {
            return string.Join(", ", ports.Select(p => p["layer"] + ":" + p["role"]).ToArray());
        }
    }
}
