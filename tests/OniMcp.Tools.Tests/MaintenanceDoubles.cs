using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        internal static JObject AreaReceiptFixture(JObject result)
        { var receipt = new JObject(); AppendOperationReceiptFacts(receipt, result); receipt["executed"] = ResultAppliedCount(JsonResult(result)); return receipt; }
        internal static JObject PreviewFixture(string tool, JObject args) => PreviewOperation("fixture", tool, args, false);
    }

    public static partial class BuildPlanningTools
    {
        private sealed partial class PlacementDetails
        {
            internal int AnchorX, AnchorY, WorldId;
            internal Orientation Orientation;
        }
        internal static List<Dictionary<string, object>> PortConflictsFixture(BuildingDef def, int x, int y,
            Orientation orientation = Orientation.Neutral, GameObject ignored = null)
            => FindBuildingPortConflicts(def, new PlacementDetails { AnchorX = x, AnchorY = y, WorldId = 0, Orientation = orientation }, ignored);
        private static int PlacementOriginCell(BuildingDef def, int x, int y, Orientation orientation) => Grid.XYToCell(x, y);
        private static string PlacementObjectPrefabId(GameObject go) => go.GetComponent<Building>()?.Def?.PrefabID;
    }
}
internal enum BuildLocationRule { Anywhere, HighWattBridgeTile, Tile, NotInTiles }
internal enum ConduitType { None, Liquid, Gas, Solid }
internal enum Orientation { Neutral, R90, R180, R270 }
internal struct CellOffset
{
    internal int x, y;
    internal CellOffset(int x, int y) { this.x = x; this.y = y; }
}
internal static class Rotatable
{
    internal static CellOffset GetRotatedCellOffset(CellOffset offset, Orientation orientation)
        => orientation == Orientation.R90 ? new CellOffset(-offset.y, offset.x) : offset;
}
internal interface ISecondaryInput
{
    bool HasSecondaryConduitType(ConduitType type);
    CellOffset GetSecondaryConduitOffset(ConduitType type);
}
internal interface ISecondaryOutput
{
    bool HasSecondaryConduitType(ConduitType type);
    CellOffset GetSecondaryConduitOffset(ConduitType type);
}
internal sealed partial class BuildingDef
{
    internal BuildLocationRule BuildLocationRule { get; set; }
    internal ConduitType InputConduitType { get; set; }
    internal ConduitType OutputConduitType { get; set; }
    internal CellOffset UtilityInputOffset { get; set; }
    internal CellOffset UtilityOutputOffset { get; set; }
    internal CellOffset PowerInputOffset { get; set; }
    internal CellOffset PowerOutputOffset { get; set; }
    internal bool RequiresPowerInput { get; set; }
    internal bool RequiresPowerOutput { get; set; }
}
internal static partial class Grid
{
    internal static readonly HashSet<int> Hidden = new HashSet<int>();
    internal static bool IsVisible(int cell) => !Hidden.Contains(cell);
    internal static int OffsetCell(int cell, CellOffset offset) => cell + offset.x + offset.y * WidthInCells;
    internal static ObjectLayer GetObjectLayerForConduitType(ConduitType type)
        => type == ConduitType.Liquid ? ObjectLayer.LiquidConnection : type == ConduitType.Gas ? ObjectLayer.GasConnection : ObjectLayer.SolidConnection;
}
namespace UnityEngine
{
    public sealed partial class GameObject
    {
        public T[] GetComponents<T>() => Components.Values.OfType<T>().ToArray();
    }
}

internal interface ICircuitConnected { }
internal sealed class TestCircuitConnection : ICircuitConnected { }
internal sealed class TestElectricalSystem { internal bool IsDirty { get; set; } }
internal sealed partial class Game
{
    internal static Game Instance { get; set; }
    internal CircuitManager circuitManager { get; set; }
    internal TestElectricalSystem electricalConduitSystem { get; set; }
}
internal sealed class CircuitManager
{
    private bool dirty;
    internal bool DirtyForFixture { get => dirty; set => dirty = value; }
    internal ushort Circuit { get; set; }
    internal ushort GetCircuitID(ICircuitConnected connection) => Circuit;
}
namespace OniMcp.Tools
{
    internal static class AreaHandleRegistry
    {
        internal static int Defines;
        internal static TestAreaHandle Define(Dictionary<string, int> rect, int worldId, string label)
        { Defines++; return new TestAreaHandle { Rect = rect }; }
    }
    internal sealed class TestAreaHandle
    {
        internal Dictionary<string, int> Rect;
        internal Dictionary<string, object> ToDictionary() => new Dictionary<string, object> { ["areaId"] = "a1", ["rect"] = Rect };
    }
}

namespace OniMcp.Tools
{
    public static partial class BuildingControlTools
    {
        internal static bool FileContext;
        internal static OniMcp.Core.CallToolResult WithVirtualFileContext(System.Func<OniMcp.Core.CallToolResult> action)
        { FileContext = true; try { return action(); } finally { FileContext = false; } }
    }
}

internal sealed class UtilityNetworkLink
{
    internal void GetCells(int origin, Orientation orientation, out int first, out int second)
    { first = Grid.OffsetCell(origin, new CellOffset(-1, 0)); second = Grid.OffsetCell(origin, new CellOffset(1, 0)); }
}
