using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    // World/material boundaries only: tests execute the production auto-connect handler.
    public static partial class BuildPlanningTools
    {
        internal static readonly HashSet<string> Placed = new HashSet<string>();
        internal static readonly List<string> Requested = new List<string>();
        internal static readonly List<string> PersistedPath = new List<string>();
        internal static string SelectedMaterial, SelectedPrefab;
        internal static int UiCalls, PersistCalls, FailAt;
        internal static bool Conflict, NetworkFails;
        internal static float Available;
        internal static void ResetPlacement()
        {
            Placed.Clear(); Requested.Clear(); PersistedPath.Clear(); UiCalls = PersistCalls = 0;
            SelectedMaterial = SelectedPrefab = null;
            FailAt = -1; Conflict = NetworkFails = false; Available = 1000;
        }
        private sealed class CellCoord
        {
            internal int x, y;
            internal string Key => x + "," + y;
        }
        private sealed class PathSafety { internal bool Valid; }
        private sealed class MaterialStock { internal float AvailableKg; }
        private sealed class MaterialChoice
        {
            internal bool Valid = true;
            internal float RequiredKg;
            internal List<string> Elements = new List<string> { "Copper" };
            internal MaterialStock Selected = new MaterialStock();
            internal object ToDictionary() => new { RequiredKg, Selected.AvailableKg };
        }
        private sealed class AutoDigContext
        {
            internal bool LimitReached => false;
            internal static AutoDigContext FromArgs(JObject args) => new AutoDigContext();
        }
        private static bool IsDryRun(JObject args) => ToolUtil.GetBool(args, "dryRun", false);
        private static string DefaultUtilityPrefab(string type) => "GasConduit";
        private static BuildingDef ResolveBuildingDef(string id, out string resolved, out string error)
        { resolved = id; error = null; return new BuildingDef { PrefabID = id }; }
        private static string BuildAvailabilityError(BuildingDef def, JObject args) => null;
        private static bool IsLinearUtilityPrefab(string id) => UtilityPrefabPolicy.IsLinear(id);
        private static List<CellCoord> ResolveUtilityPath(JObject args, int max, out string error)
        {
            error = null;
            return ((JArray)args["points"]).Select(p => new CellCoord { x = (int)p[0], y = (int)p[1] }).ToList();
        }
        private static PathSafety ValidateUtilityPathSafety(BuildingDef def, List<CellCoord> path, int worldId)
            => new PathSafety { Valid = !Conflict };
        private static object UtilityPathConflictResult(BuildingDef def, List<CellCoord> path, PathSafety safety, string phase)
            => new { reasonCode = "utility_path_conflict", planned = 0 };
        private static MaterialChoice SelectElements(BuildingDef def, string material, int worldId)
        {
            SelectedPrefab = def.PrefabID; SelectedMaterial = material;
            return new MaterialChoice { Selected = new MaterialStock { AvailableKg = Available } };
        }
        private static float RequiredMaterialKg(BuildingDef def) => 25;
        private static int CountUtilityPathCells(BuildingDef def, List<CellCoord> path, int worldId)
            => path.Count(p => Placed.Contains(p.Key));
        private static bool IsFreeBuildContext() => false;
        // Trap for the previous UI implementation. Legacy nativePathPlacement=true
        // must never dispatch here, even if the PlanScreen would be available.
        private static Dictionary<string, object> TryPlaceUtilityPathNative(BuildingDef def, List<CellCoord> path, JObject args)
        {
            UiCalls++;
            return new Dictionary<string, object> { ["success"] = false, ["shouldFallback"] = true };
        }
        private static Dictionary<string, object> TryPlanOne(string prefab, int x, int y, JObject args, HashSet<int> support, AutoDigContext digs)
        {
            string key = x + "," + y;
            Requested.Add(key);
            if (Requested.Count == FailAt)
                return new Dictionary<string, object> { ["valid"] = false, ["planned"] = false, ["error"] = "placement unavailable" };
            bool present = Placed.Contains(key);
            bool planned = !IsDryRun(args) && !present;
            if (planned) Placed.Add(key);
            return new Dictionary<string, object> { ["valid"] = true, ["planned"] = planned, ["alreadyPresent"] = present };
        }
        private static int GetAutoDigInt(Dictionary<string, object> result, string key) => 0;
        private static bool IsAutoDigResult(Dictionary<string, object> result) => false;
        private static bool GetBool(Dictionary<string, object> result, string key)
            => result.TryGetValue(key, out var value) && value is bool flag && flag;
        private static bool EqualsIgnoreCase(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        private static bool PersistUtilityPathConnections(BuildingDef def, List<CellCoord> path, out string error)
        {
            PersistCalls++; PersistedPath.AddRange(path.Select(point => point.Key));
            error = NetworkFails ? "network incomplete" : null; return !NetworkFails;
        }
        private static bool IsCompletedUtilityPath(BuildingDef def, List<CellCoord> path) => false;
        private static object BuildPathSegments(List<CellCoord> path) => path;
    }
}
