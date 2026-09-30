using System;
using System.Collections.Generic;
using OniMcp.Tools;
using UnityEngine;

internal static class MaterialSupplyRegression
{
    internal static void Run()
    {
        int checks = 0;
        Action<bool, string> check = (ok, message) => {
            checks++;
            if (!ok) throw new InvalidOperationException("Material supply: " + message);
        };
        var world = ClusterManager.Instance.GetWorld(0);
        var inventory = new WorldInventory();
        var previous = world.worldInventory;
        world.worldInventory = inventory;
        var aluminum = new Tag("Aluminum");
        var loose = Item(4050, 440f);
        var stored = Item(4051, 100f);
        stored.storage = new Storage { gameObject = new GameObject { Cell = 4051 } };
        stored.KPrefabID.Tags.Add(GameTags.Stored);
        inventory.Items[aluminum] = new List<Pickupable> { loose };
        try
        {
            check(BuildPlanningTools.MaterialSupplyFixture(0, aluminum) == 440f,
                "440kg native loose stock is counted once, not cached total plus loose mass");
            check(inventory.CachedReads == 0 && !inventory.LastIncludeRelatedWorlds,
                "supply uses fresh local fetchables, never cached or related-world totals");
            inventory.Items[aluminum].Add(stored);
            inventory.Items[aluminum].Add(loose);
            check(BuildPlanningTools.MaterialSupplyFixture(0, aluminum) == 540f,
                "stored and loose stock each count once, even duplicate native references");

            var privateItem = Item(4052, 200f);
            privateItem.KPrefabID.Tags.Add(GameTags.StoredPrivate);
            var unreachable = Item(4053, 300f);
            inventory.Unreachable.Add(unreachable);
            var hidden = Item(4054, 400f);
            Grid.Hidden.Add(hidden.cachedCell);
            var buried = Item(4055, 500f);
            Grid.Solid[buried.cachedCell] = true;
            var otherWorld = Item(4056, 600f);
            Grid.WorldIdx[otherWorld.cachedCell] = 1;
            var hiddenStorage = Item(4057, 700f);
            hiddenStorage.storage = new Storage { gameObject = hidden.gameObject };
            inventory.Items[aluminum].AddRange(new[] {
                privateItem, unreachable, hidden, buried, otherWorld, hiddenStorage,
                Item(4058, -1f), Item(4059, float.NaN), Item(4060, float.PositiveInfinity), null
            });
            check(BuildPlanningTools.MaterialSupplyFixture(0, aluminum) == 540f,
                "private, unreachable, hidden, buried, foreign and invalid quantities cannot inflate supply");
            loose.TotalAmount = 40f;
            check(BuildPlanningTools.MaterialSupplyFixture(0, aluminum) == 140f,
                "a quantity consumed since the native cached total is observed immediately");
            inventory.Unreachable.Add(loose);
            check(BuildPlanningTools.MaterialSupplyFixture(0, aluminum) == 100f,
                "native reachability changes are observed without refreshing cached totals");
            inventory.Items[aluminum].Remove(stored);
            check(BuildPlanningTools.MaterialSupplyFixture(0, aluminum) == 0f,
                "removed fetchable stock is not recreated by a stale aggregate");
            check(BuildPlanningTools.MaterialSupplyFixture(0, Tag.Invalid) == 0f
                && BuildPlanningTools.MaterialSupplyFixture(0, new Tag("Absent")) == 0f
                && BuildPlanningTools.MaterialSupplyFixture(100, aluminum) == 0f,
                "missing tag, inventory entry or world supplies nothing");
            ClusterManager.Instance.activeWorldId = 0;
            inventory.Unreachable.Remove(loose);
            check(BuildPlanningTools.MaterialSupplyFixture(-1, aluminum) == 40f,
                "unspecified world uses the active world with the same eligibility rules");
        }
        finally
        {
            world.worldInventory = previous;
            Grid.Hidden.Remove(4054);
            Grid.Solid[4055] = false;
            Grid.WorldIdx[4056] = 0;
        }
        Console.WriteLine("Material supply regression: " + checks + " checks passed");
    }

    private static Pickupable Item(int cell, float quantity)
    {
        var item = new Pickupable { gameObject = new GameObject { Cell = cell },
            cachedCell = cell, TotalAmount = quantity, KPrefabID = new KPrefabID() };
        item.gameObject.Components[typeof(Pickupable)] = item;
        item.gameObject.Components[typeof(KPrefabID)] = item.KPrefabID;
        return item;
    }
}

internal sealed class WorldInventory
{
    internal readonly Dictionary<Tag, List<Pickupable>> Items = new Dictionary<Tag, List<Pickupable>>();
    internal readonly HashSet<Pickupable> Unreachable = new HashSet<Pickupable>();
    internal int CachedReads;
    internal bool LastIncludeRelatedWorlds;
    internal ICollection<Pickupable> GetPickupables(Tag tag, bool includeRelatedWorlds = false)
    {
        LastIncludeRelatedWorlds = includeRelatedWorlds;
        return Items.TryGetValue(tag, out var items) ? items : null;
    }
    internal bool IsReachable(Pickupable item) => !Unreachable.Contains(item);
    internal float GetTotalAmount(Tag tag, bool includeRelatedWorlds)
    {
        CachedReads++;
        return 440f;
    }
}

internal sealed partial class WorldContainer
{
    internal int id { get; set; }
    internal WorldInventory worldInventory { get; set; }
}
internal sealed partial class Pickupable
{
    internal KPrefabID KPrefabID { get; set; }
    internal float TotalAmount { get; set; }
    internal T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
    internal int GetMyWorldId() => Grid.IsValidCell(cachedCell) ? Grid.WorldIdx[cachedCell] : -1;
}
internal static partial class GameTags
{
    internal static readonly Tag StoredPrivate = new Tag("StoredPrivate");
}
namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        internal static float MaterialSupplyFixture(int worldId, Tag tag) => AvailableAmount(worldId, tag);
    }
}
