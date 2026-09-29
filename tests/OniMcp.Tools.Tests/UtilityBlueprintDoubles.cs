using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static BuildingDef PlacementDef(string prefab)
        {
            ObjectLayer layer = UtilityPrefabPolicy.SameFamily(prefab, "Wire") ? ObjectLayer.Wire
                : UtilityPrefabPolicy.SameFamily(prefab, "GasConduit") ? ObjectLayer.GasConduit
                : UtilityPrefabPolicy.SameFamily(prefab, "LogicWire") ? ObjectLayer.LogicWire
                : UtilityPrefabPolicy.SameFamily(prefab, "SolidConduit") ? ObjectLayer.SolidConduit : ObjectLayer.LiquidConduit;
            return new BuildingDef { PrefabID = prefab, ObjectLayer = (int)layer, TileLayer = layer,
                ReplacementLayer = (ObjectLayer)((int)layer + 2), WidthInCells = 1, HeightInCells = 1 };
        }
        internal static GameObject CreateUtilityBlueprintFixture(BuildingDef def, int x, int y, bool replacement = false)
        {
            int cell = Grid.XYToCell(x, y);
            var go = new GameObject { Cell = cell };
            go.Components[typeof(Building)] = new Building { Def = def };
            go.Components[typeof(Constructable)] = new Constructable { gameObject = go, IsReplacementTile = replacement };
            go.Components[typeof(KAnimGraphTileVisualizer)] = new KAnimGraphTileVisualizer();
            var underConstruction = new BuildingUnderConstruction { gameObject = go, Def = def };
            go.Components[typeof(BuildingUnderConstruction)] = underConstruction;
            UnityEngine.Object.Registered.Add(underConstruction);
            int layer = replacement ? (int)def.ReplacementLayer : def.ObjectLayer;
            Grid.Objects[cell, layer] = go;
            PlacementGridCells.Add(System.Tuple.Create(cell, layer));
            return go;
        }
        internal static UtilityConnections BlueprintConnectionsFixture(int x, int y)
            => UnityEngine.Object.Registered.OfType<BuildingUnderConstruction>()
                .Single(building => building.gameObject.Cell == Grid.XYToCell(x, y))
                .GetComponent<KAnimGraphTileVisualizer>().Connections;
        internal static bool PersistConnectionsFixture(BuildingDef def, int[] cells, out string error)
            => PersistUtilityPathConnections(def, cells.Select(cell => new CellCoord
            { x = Grid.CellColumn(cell), y = Grid.CellRow(cell) }).ToList(), out error);
        internal static bool PreserveReplacementFixture(BuildingDef def, GameObject blueprint, GameObject source, out string error)
            => PreserveUtilityReplacementConnections(def, blueprint, source, out error);
        internal static GameObject NativeBlueprintFixture(BuildingDef def, GameObject source, out string error)
            => TryPlaceNativeBlueprint(def, new Vector3(), Orientation.Neutral, new List<Tag>(), null, source, out error);
        private static bool IsExactConnectionUtilityPrefab(string id) => UtilityPrefabPolicy.IsLinear(id);
        private static bool RefreshAndValidateUtilityPathNetwork(BuildingDef def, List<CellCoord> path, out string error)
        { error = null; return true; }
        // Native grid direction lookup is the boundary; the production method
        // under test owns path traversal, mask merging and serialized updates.
        private static bool TryExpectedConnectionBits(int from, int to, out UtilityConnections a, out UtilityConnections b)
        {
            int dx = Grid.CellColumn(to) - Grid.CellColumn(from), dy = Grid.CellRow(to) - Grid.CellRow(from);
            a = b = 0;
            if (dx == 1 && dy == 0) { a = UtilityConnections.Right; b = UtilityConnections.Left; }
            if (dx == -1 && dy == 0) { a = UtilityConnections.Left; b = UtilityConnections.Right; }
            if (dx == 0 && dy == 1) { a = UtilityConnections.Up; b = UtilityConnections.Down; }
            if (dx == 0 && dy == -1) { a = UtilityConnections.Down; b = UtilityConnections.Up; }
            return a != 0;
        }
    }
}

internal sealed partial class Constructable
{
    internal bool IsReplacementTile;
}
internal sealed class BuildingUnderConstruction : KMonoBehaviour
{
    internal BuildingDef Def { get; set; }
}
internal sealed class KAnimGraphTileVisualizer
{
    internal UtilityConnections Connections;
    internal bool RejectUpdates;
    internal void UpdateConnections(UtilityConnections connections)
    { if (!RejectUpdates && !OniMcp.Tools.BuildPlanningTools.NetworkFails) Connections = connections; }
}
internal static partial class Grid
{
    internal static int PosToCell(KMonoBehaviour component) => PosToCell(component.gameObject);
}
internal sealed partial class BuildingDef
{
    internal GameObject NativePlacementResult;
    internal int NativePlaceCalls, NativeReplaceCalls;
    internal GameObject TryPlace(GameObject ignored, Vector3 position, Orientation orientation, IList<Tag> elements, string facade)
    { NativePlaceCalls++; return NativePlacementResult; }
    internal GameObject TryReplaceTile(GameObject ignored, Vector3 position, Orientation orientation, IList<Tag> elements, string facade)
    { NativeReplaceCalls++; return NativePlacementResult; }
}
namespace UnityEngine
{
    public struct Vector3 { }
}
