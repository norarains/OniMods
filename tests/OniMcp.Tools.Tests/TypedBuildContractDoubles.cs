using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        internal static CallToolResult BuildCommandFixture(JObject args, string file, string plan, bool preview)
            => preview ? PreflightBuildEdit(args, file, plan) : ApplyBuildEdit(args, file, plan);
        // The file-to-layer lookup is a boundary of the linked command handlers.
        private static string PrefabForConnectionMap(string path)
        {
            if (path.Contains("liquid_conduits")) return "LiquidConduit";
            if (path.Contains("gas_conduits")) return "GasConduit";
            if (path.Contains("logic")) return "LogicWire";
            if (path.Contains("solid_conveyor")) return "SolidConduit";
            return "Wire";
        }
    }

    public static partial class BuildingControlTools
    {
        internal static JObject LastBuildCommand;
        internal static int BuildCommandCalls;
        internal static CallToolResult ControlBuildingFromVirtualFile(JObject args)
        {
            return WithVirtualFileContext(() =>
            {
                BuildCommandCalls++;
                LastBuildCommand = (JObject)args.DeepClone();
                return CallToolResult.Text("{valid:true}");
            });
        }
    }

    public static partial class BuildPlanningTools
    {
        private sealed class ReplacementCell
        {
            internal int Cell;
            internal bool Valid = true, Visible = true, InWorld = true;
            internal int X => Grid.CellColumn(Cell);
            internal int Y => Grid.CellRow(Cell);
        }
        private sealed partial class PlacementDetails
        {
            internal readonly List<ReplacementCell> Footprint = new List<ReplacementCell>();
        }
        internal static GameObject ReplacementFixture(BuildingDef def, int cell, bool visible = true)
            => NativeTileReplacement(def, ReplacementPlacement(cell, visible));
        internal static bool MatchingReplacementFixture(BuildingDef def, int cell, GameObject existing)
            => HasMatchingUtilityReplacement(def, ReplacementPlacement(cell, true), existing);
        private static PlacementDetails ReplacementPlacement(int cell, bool visible)
        {
            var placement = new PlacementDetails();
            placement.Footprint.Add(new ReplacementCell { Cell = cell, Visible = visible });
            return placement;
        }
    }
}

internal sealed partial class BuildingDef
{
    internal ObjectLayer TileLayer { get; set; }
    internal GameObject ReplacementCandidate { get; set; }
    internal bool ReplacementOccupied { get; set; }
    internal bool NativeCanReplace { get; set; } = true;
    internal bool NativeLocationValid { get; set; } = true;
    internal bool IsReplacementLayerOccupied(int cell) => ReplacementOccupied;
    internal GameObject GetReplacementCandidate(int cell) => ReplacementCandidate;
    internal bool CanReplace(GameObject candidate) => NativeCanReplace;
    internal bool IsValidPlaceLocation(GameObject ignored, int cell, Orientation orientation, bool replacement, out string error)
    { error = null; return NativeLocationValid; }
}
internal sealed partial class BuildingComplete
{
    internal BuildingDef Def { get; set; }
}
