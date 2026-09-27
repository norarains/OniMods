using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static Dictionary<string, object> TryAutoConnectPower(
            BuildingDef def,
            int anchorX,
            int anchorY,
            Orientation orientation,
            JObject args,
            HashSet<int> plannedSupportCells,
            AutoDigContext autoDigContext)
        {
            bool enabled = def != null
                && def.RequiresPowerInput
                && ToolUtil.GetBool(args, "autoConnectPower", true);
            if (!enabled)
            {
                return new Dictionary<string, object>
                {
                    ["enabled"] = false,
                    ["reason"] = def == null || !def.RequiresPowerInput ? "building_has_no_power_input" : "disabled_by_argument"
                };
            }

            int worldId = ToolUtil.ResolveWorldId(args);
            int inputCell = PowerInputCell(def, anchorX, anchorY, orientation);
            if (!Grid.IsValidCell(inputCell) || !ToolUtil.CellMatchesWorld(inputCell, worldId))
            {
                return new Dictionary<string, object>
                {
                    ["enabled"] = true,
                    ["status"] = "no_valid_power_input_cell",
                    ["planned"] = 0,
                    ["failed"] = 0
                };
            }

            int sourceCell;
            int radius = ToolUtil.GetInt(args, "maxAutoConnectRadius") ?? 80;
            if (!TryFindNearestWireOrPowerOutput(inputCell, worldId, radius, out sourceCell))
            {
                return new Dictionary<string, object>
                {
                    ["enabled"] = true,
                    ["status"] = "no_connected_power_source",
                    ["input"] = CellCoordDictionary(inputCell),
                    ["planned"] = 0,
                    ["failed"] = 0
                };
            }

            int maxCells = Math.Max(1, Math.Min(ToolUtil.GetInt(args, "maxCells") ?? 200, 500));
            if (sourceCell == inputCell && UtilityConnectionRead.HasLine(inputCell, UtilityLayersForPrefab("Wire").ToArray()))
                return new Dictionary<string, object> {
                    ["enabled"] = true, ["status"] = "line_already_present",
                    ["input"] = CellCoordDictionary(inputCell), ["source"] = CellCoordDictionary(sourceCell),
                    ["planned"] = 0, ["reused"] = 1, ["failed"] = 0, ["autoDigQueued"] = 0
                };
            var path = FindReachablePowerRoute(sourceCell, inputCell, worldId, maxCells);
            if (path == null)
                return new Dictionary<string, object> {
                    ["enabled"] = true, ["status"] = "no_reachable_wire_route", ["failed"] = 1, ["planned"] = 0,
                    ["input"] = CellCoordDictionary(inputCell), ["source"] = CellCoordDictionary(sourceCell),
                    ["reachability"] = "no_visible_currently_reachable_route_within_search_budget",
                    ["searchCellLimit"] = 4096, ["maxCells"] = maxCells
                };

            var wireArgs = new JObject
            {
                ["prefabId"] = "Wire",
                ["material"] = args["wireMaterial"] ?? args["material"] ?? new JValue("auto"),
                ["autoConnectPower"] = false,
                ["confirm"] = true,
                ["dryRun"] = IsDryRun(args),
                ["worldId"] = worldId,
                ["maxCells"] = maxCells,
                // Utility routing must never silently excavate a wall chosen by Manhattan routing.
                ["autoDigObstructions"] = false,
                ["autoUprootObstructions"] = false,
                ["priority"] = ToolUtil.GetInt(args, "priority") ?? 5,
                ["points"] = new JArray(path.Select(point => new JArray(point.x, point.y)))
            };

            var connectResult = AutoConnectUtility().Handler(wireArgs);
            var connectPayload = ParseToolJsonPayload(connectResult);
            if (connectPayload == null)
            {
                return new Dictionary<string, object>
                {
                    ["enabled"] = true,
                    ["status"] = "connect_result_unparseable",
                    ["dryRun"] = IsDryRun(args),
                    ["input"] = CellCoordDictionary(inputCell),
                    ["source"] = CellCoordDictionary(sourceCell),
                    ["pathCells"] = path.Count,
                    ["planned"] = 0,
                    ["failed"] = 1,
                    ["path"] = path.Select(p => new { x = p.x, y = p.y }).ToList(),
                    ["error"] = connectResult.Content.FirstOrDefault()?.Text
                };
            }

            return new Dictionary<string, object>
            {
                ["enabled"] = true,
                ["status"] = connectPayload.ContainsKey("failed") && Convert.ToInt32(connectPayload["failed"]) > 0
                    ? "partially_connected"
                    : "connected_or_planned",
                ["dryRun"] = IsDryRun(args),
                ["input"] = CellCoordDictionary(inputCell),
                ["source"] = CellCoordDictionary(sourceCell),
                ["pathMode"] = "visible_reachable_route",
                ["reachability"] = "existing_wire_or_currently_reachable_work_cell",
                ["pathCells"] = path.Count,
                ["planned"] = GetInt(connectPayload, "planned"),
                ["reused"] = GetInt(connectPayload, "reusedExisting"),
                ["failed"] = Math.Max(connectResult.IsError ? 1 : 0, GetInt(connectPayload, "failed")),
                ["autoDigQueued"] = GetInt(connectPayload, "autoMarkedObstructions"),
                ["path"] = path.Select(p => new { x = p.x, y = p.y }).ToList(),
                ["segments"] = BuildPathSegments(path),
                ["connectResult"] = connectPayload
            };
        }

        private static Dictionary<string, object> PowerConnectionReceipt(Dictionary<string, object> result,
            Dictionary<string, object> connection, bool placed)
        {
            if (GetInt(connection, "failed") <= 0) return result;
            result["valid"] = false;
            result["success"] = false;
            result["partial"] = placed;
            result["safeToRetry"] = !placed;
            result["reasonCode"] = "power_connection_failed";
            result["error"] = placed ? "Building placed; power connection failed." : "Power connection preview failed.";
            return result;
        }

        private static int PowerInputCell(BuildingDef def, int anchorX, int anchorY, Orientation orientation)
        {
            int anchorCell = def == null ? Grid.InvalidCell : PlacementOriginCell(def, anchorX, anchorY, orientation);
            if (!Grid.IsValidCell(anchorCell) || def == null)
                return Grid.InvalidCell;
            CellOffset rotated = Rotatable.GetRotatedCellOffset(def.PowerInputOffset, orientation);
            return Grid.OffsetCell(anchorCell, rotated);
        }

        private static Dictionary<string, object> CellCoordDictionary(int cell)
        {
            return new Dictionary<string, object>
            {
                ["cell"] = cell,
                ["x"] = Grid.IsValidCell(cell) ? Grid.CellColumn(cell) : -1,
                ["y"] = Grid.IsValidCell(cell) ? Grid.CellRow(cell) : -1
            };
        }

        private static IEnumerable<CellCoord> LineCells(int x1, int y1, int x2, int y2, HashSet<string> seen)
        {
            int dx = Math.Sign(x2 - x1);
            int dy = Math.Sign(y2 - y1);
            int x = x1;
            int y = y1;
            while (true)
            {
                string key = x + "," + y;
                if (seen.Add(key))
                    yield return new CellCoord(x, y);
                if (x == x2 && y == y2)
                    yield break;
                if (x != x2)
                    x += dx;
                else if (y != y2)
                    y += dy;
            }
        }

        private static IEnumerable<CellCoord> StraightLineCells(int x1, int y1, int x2, int y2)
        {
            var seen = new HashSet<string>();
            foreach (var cell in LineCells(x1, y1, x2, y2, seen))
                yield return cell;
        }

        private static bool TryDirection(string direction, out int dx, out int dy)
        {
            dx = 0;
            dy = 0;
            switch ((direction ?? "").Trim().ToLowerInvariant())
            {
                case "right":
                case "east":
                case "e":
                    dx = 1;
                    return true;
                case "left":
                case "west":
                case "w":
                    dx = -1;
                    return true;
                case "up":
                case "north":
                case "n":
                    dy = 1;
                    return true;
                case "down":
                case "south":
                case "s":
                    dy = -1;
                    return true;
                default:
                    return false;
            }
        }

    }
}
