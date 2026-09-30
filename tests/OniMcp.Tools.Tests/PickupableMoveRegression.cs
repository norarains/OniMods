using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using OniMcp.Tools;

internal static class PickupableMoveRegression
{
    internal static void Run()
    {
        var item = new GameObject();
        var movable = new Movable();
        var identity = new KPrefabID();
        item.Components[typeof(Movable)] = movable;
        item.Components[typeof(KPrefabID)] = identity;
        var args = new JObject { ["destinationId"] = 42 };
        UserMenuActionTools.Destination = new GameObject { Cell = 18 };
        Check(UserMenuActionTools.TestMove(item, new JObject(), false) != null, "destination required");
        Check(UserMenuActionTools.TestMove(item, args, true) == null && movable.Calls == 0, "preview does not place delivery");
        movable.CanMove = false;
        Check(UserMenuActionTools.TestMove(item, args, false) != null && movable.Calls == 0, "invalid destination rejected");
        movable.CanMove = true; identity.Tags.Add(GameTags.Stored);
        Check(UserMenuActionTools.TestMove(item, args, false) != null && movable.Calls == 0, "stored pickupable rejected");
        identity.Tags.Clear(); movable.tagRequiredForMove = 5;
        Check(UserMenuActionTools.TestMove(item, args, false) != null, "native movement prerequisite enforced");
        identity.Tags.Add(5);
        Check(UserMenuActionTools.TestMove(item, args, false) == null && movable.Calls == 1 && movable.LastCell == 18,
            "direct native order at the exact destination");
        Check(UserMenuActionTools.TestMove(item, args, false) == null && movable.Calls == 1, "same destination replay is idempotent");
        UserMenuActionTools.Destination.Cell = 19;
        Check(UserMenuActionTools.TestMove(item, args, false) != null && movable.Calls == 1, "different pending destination must not silently retarget");
        UserMenuActionTools.Destination = null;
        Check(UserMenuActionTools.TestMove(item, args, false) != null, "missing destination rejected");
        Console.WriteLine("Pickupable delivery regression checks passed");
    }
    private static void Check(bool ok, string message)
    { if (!ok) throw new InvalidOperationException("Pickupable move: " + message); }
}

namespace OniMcp.Tools
{
    public static partial class UserMenuActionTools
    {
        internal static GameObject Destination;
        private sealed class ActionSpec { internal string ActionKey; }
        private static GameObject FindTarget(JObject args) => (int?)args["id"] == 42 ? Destination : null;
        private static string InvokeSpec(GameObject go, ActionSpec spec)
            => throw new InvalidOperationException("Movement must never invoke the interactive user-menu callback");
        internal static string TestMove(GameObject go, JObject args, bool dryRun)
            => ExecuteMenuAction(go, new ActionSpec { ActionKey = "toggle_move_pickupable" }, args, dryRun);
    }
}
internal readonly struct Tag : IEquatable<Tag>
{
    internal static readonly Tag Invalid = default(Tag);
    internal readonly string Name;
    internal Tag(string name) { Name = name; }
    public bool Equals(Tag other) => Name == other.Name;
    public override bool Equals(object other) => other is Tag tag && Equals(tag);
    public override int GetHashCode() => Name?.GetHashCode() ?? 0;
    public override string ToString() => Name;
    public static bool operator ==(Tag a, Tag b) => a.Equals(b);
    public static bool operator !=(Tag a, Tag b) => !a.Equals(b);
    public static implicit operator Tag(int value) => value == 0 ? Invalid : new Tag("#" + value);
}
internal static partial class GameTags
{
    internal static readonly Tag Stored = 1;
    internal static readonly Tag Void = new Tag("Void");
}
internal sealed partial class KPrefabID
{
    internal readonly HashSet<Tag> Tags = new HashSet<Tag>();
    internal bool HasTag(Tag tag) => Tags.Contains(tag);
}
internal sealed class Movable
{
    internal Tag tagRequiredForMove;
    internal int Calls, LastCell;
    internal bool CanMove = true, IsMarkedForMove;
    internal GameObject StorageProxy;
    internal bool CanMoveTo(int cell) => CanMove;
    internal void MoveToLocation(int cell)
    {
        Calls++; LastCell = cell; IsMarkedForMove = true;
        StorageProxy = new GameObject { Cell = cell };
    }
}
