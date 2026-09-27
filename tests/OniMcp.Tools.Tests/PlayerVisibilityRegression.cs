using System;
using System.Collections.Generic;
using System.Linq;
using OniMcp.Tools;
using UnityEngine;

internal static class PlayerVisibilityRegression
{
    internal static void Run()
    {
        int checks = 0;
        Action<bool, string> check = (ok, message) => {
            checks++;
            if (!ok) throw new Exception("Player discovery: " + message);
        };
        var known = new GameObject { Cell = 4000 };
        var fog = new GameObject { Cell = 4001 };
        var buried = new GameObject { Cell = 4002 };
        var discovery = new Uncoverable { IsUncovered = false };
        buried.Components[typeof(Uncoverable)] = discovery;
        Grid.Hidden.Add(fog.Cell);
        Grid.Solid[buried.Cell] = true;
        try
        {
            check(!PlayerVisibility.Cell(-1), "invalid cell is unknown");
            check(!PlayerVisibility.Cell(Grid.WorldIdx.Length), "outside grid is unknown");
            check(!PlayerVisibility.Object(null), "null object is unknown");
            check(PlayerVisibility.Cell(known.Cell), "explored terrain is readable");
            check(PlayerVisibility.Object(known), "known object needs no camera or UI");
            ClusterManager.Instance.GetWorld(0).IsDiscovered = false;
            check(!PlayerVisibility.Cell(known.Cell), "cell reveal cannot bypass world discovery");
            check(!PlayerVisibility.Object(known), "unknown world's objects remain hidden");
            ClusterManager.Instance.GetWorld(0).IsDiscovered = true;
            check(!PlayerVisibility.Object(fog), "fog hides object");
            check(!PlayerVisibility.Cell(fog.Cell), "fog hides terrain");
            check(PlayerVisibility.Cell(buried.Cell), "rock covering an object stays readable");
            check(!PlayerVisibility.Object(buried), "explored anchor does not uncover object");
            check(PlayerVisibility.Known(buried) == null, "map layer lookup hides buried identity");

            // Changing terrain alone cannot override ONI's native discovery bit.
            Grid.Solid[buried.Cell] = false;
            check(!PlayerVisibility.Object(buried), "partial excavation waits for native discovery");
            discovery.IsUncovered = true;
            check(PlayerVisibility.Object(buried), "native first-exposure discovery is honored");
            Grid.Solid[buried.Cell] = true;
            check(PlayerVisibility.Object(buried), "rediscovered/reburied object remains known");
            Grid.Hidden.Add(buried.Cell);
            check(!PlayerVisibility.Object(buried), "uncovered flag does not bypass fog");
            Grid.Hidden.Remove(buried.Cell);

            var loose = new GameObject { Cell = buried.Cell };
            var item = new Pickupable { gameObject = loose, cachedCell = loose.Cell };
            loose.Components[typeof(Pickupable)] = item;
            check(!PlayerVisibility.Object(loose), "loose buried resources are excluded");
            Grid.Foundation[loose.Cell] = true;
            check(PlayerVisibility.Object(loose), "constructed tiles do not hide inventory");
            Grid.Foundation[loose.Cell] = false;
            item.storage = new Storage { gameObject = known };
            check(PlayerVisibility.Object(loose), "visible storage exposes its contents");
            item.storage.gameObject = fog;
            check(!PlayerVisibility.Object(loose), "hidden storage contents stay hidden");
            item.storage.gameObject = loose;
            check(!PlayerVisibility.Object(loose), "storage cycles fail closed");
            item.storage = null;
            item.cachedCell = known.Cell;
            check(PlayerVisibility.Object(loose), "pickupable cached cell is authoritative");

            // A hidden object inserted ahead of visible results cannot consume a
            // result limit, change an aggregate, or satisfy an exact-ID-like match.
            discovery.IsUncovered = false;
            var candidates = new[] { fog, buried, known };
            var visible = candidates.Where(PlayerVisibility.Object);
            check(visible.Count() == 1, "hidden candidates cannot inflate counts");
            check(visible.Take(1).Single() == known, "visibility precedes pagination");
            check(visible.FirstOrDefault(go => go == buried) == null, "exact match cannot bypass visibility");
            Grid.Hidden.Remove(fog.Cell);
            check(PlayerVisibility.Object(fog), "fresh discovery works without cache invalidation");

            var portDef = new BuildingDef { InputConduitType = ConduitType.Liquid };
            Grid.Objects[buried.Cell, (int)ObjectLayer.LiquidConnection] = buried;
            try
            {
                var conflicts = BuildPlanningTools.PortConflictsFixture(portDef,
                    Grid.CellColumn(buried.Cell), Grid.CellRow(buried.Cell));
                check(conflicts.Count == 1, "unknown connector still blocks destructive placement");
                check(conflicts[0]["existingId"] == null && conflicts[0]["existingPrefabId"] == null,
                    "placement preview cannot disclose hidden connector identity");
            }
            finally { Grid.Objects[buried.Cell, (int)ObjectLayer.LiquidConnection] = null; }
        }
        finally
        {
            Grid.Hidden.Remove(fog.Cell);
            Grid.Hidden.Remove(buried.Cell);
            ClusterManager.Instance.GetWorld(0).IsDiscovered = true;
            Grid.Solid[buried.Cell] = Grid.Foundation[buried.Cell] = false;
        }
        Console.WriteLine("Player discovery regression: " + checks + " checks passed");
    }
}

internal sealed class Uncoverable { internal bool IsUncovered { get; set; } }
internal sealed class Pickupable
{
    internal GameObject gameObject { get; set; }
    internal int cachedCell { get; set; }
    internal Storage storage { get; set; }
}
internal sealed class Storage { internal GameObject gameObject { get; set; } }
internal sealed class WorldContainer { internal bool IsDiscovered { get; set; } = true; }
internal sealed class ClusterManager
{
    internal static ClusterManager Instance { get; } = new ClusterManager();
    private readonly Dictionary<int, WorldContainer> worlds = new Dictionary<int, WorldContainer> {
        [0] = new WorldContainer(), [1] = new WorldContainer()
    };
    internal WorldContainer GetWorld(int id) => worlds.TryGetValue(id, out var world) ? world : null;
}
internal static partial class Grid
{
    internal static readonly bool[] Solid = new bool[WidthInCells * HeightInCells];
    internal static readonly bool[] Foundation = new bool[WidthInCells * HeightInCells];
    internal static bool IsWorldValidCell(int cell) => IsValidCell(cell) && WorldIdx[cell] >= 0;
}
namespace OniMcp.Tools
{
    public static partial class ToolUtil
    {
        internal static int PickupableCell(Pickupable item)
            => Grid.IsValidCell(item.cachedCell) ? item.cachedCell : Grid.PosToCell(item.gameObject);
    }
}
