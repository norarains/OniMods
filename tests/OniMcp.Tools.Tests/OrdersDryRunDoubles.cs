using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;

// Native boundaries are mutation spies. Production order handlers and their
// deconstruction helper are linked into this host executable unchanged.
namespace UnityEngine
{
    public sealed partial class GameObject
    {
        internal string name = "fixture";
        internal int NativeTriggers;
        internal int InstantDeconstructionTriggers;
        internal bool ThrowOnNativeTrigger;
        internal readonly Dictionary<int, Action<object>> EventHandlers = new Dictionary<int, Action<object>>();
        internal void Trigger(int hash)
        {
            NativeTriggers++;
            if (hash == (int)GameHashes.MarkForDeconstruct && DebugHandler.InstantBuildMode)
                InstantDeconstructionTriggers++;
            if (ThrowOnNativeTrigger) throw new InvalidOperationException("Native event fixture failure");
        }
        internal void Trigger(int hash, object data)
        { NativeTriggers++; if (EventHandlers.TryGetValue(hash, out var callback)) callback(data); }
        internal int GetInstanceID() => GetComponent<KPrefabID>()?.InstanceID ?? Cell;
        internal string GetProperName() => name;
        internal int GetMyWorldId() => Grid.WorldIdx[Cell];
    }
    public enum FindObjectsSortMode { None }
    public static class Object
    {
        internal static readonly List<object> Registered = new List<object>();
        internal static T[] FindObjectsByType<T>(FindObjectsSortMode order) => Registered.OfType<T>().ToArray();
    }
}

internal sealed partial class KPrefabID
{
    internal int InstanceID { get; set; }
    internal Tag PrefabTag { get; set; } = new Tag("fixture");
    internal GameObject gameObject { get; set; }
}
internal static class DebugHandler { internal static bool InstantBuildMode { get; set; } }
internal enum GameHashes { MarkForDeconstruct, NewConstruction }
internal sealed class TravelTube { }
internal sealed class Deconstructable
{
    internal bool allowDeconstruction = true;
    internal int Calls;
    internal int InstantCompletions;
    internal bool? LastUserTriggered;
    internal void QueueDeconstruction(bool userTriggered)
    {
        Calls++;
        LastUserTriggered = userTriggered;
        if (userTriggered && DebugHandler.InstantBuildMode) InstantCompletions++;
    }
}
internal interface IEmptyConduitWorkable { void EmptyContents(); void MarkForEmptying(); }
internal sealed class EmptyWorkable : IEmptyConduitWorkable
{
    internal int Emptied, Marked;
    public void EmptyContents() { Emptied++; }
    public void MarkForEmptying() { Marked++; }
}
internal static class PriorityScreen { internal enum PriorityClass { basic, topPriority } }
internal struct PrioritySetting
{
    internal PriorityScreen.PriorityClass priority_class;
    internal int priority_value;
    internal PrioritySetting(PriorityScreen.PriorityClass kind, int value)
    { priority_class = kind; priority_value = value; }
}
internal sealed class Prioritizable
{
    internal int Calls;
    internal bool Supported = true;
    internal PrioritySetting Master = new PrioritySetting(PriorityScreen.PriorityClass.basic, 5);
    internal void SetMasterPriority(PrioritySetting setting) { Calls++; Master = setting; }
    internal PrioritySetting GetMasterPriority() => Master;
    internal bool IsPrioritizable() => Supported;
}
internal sealed class FactionManager
{
    internal static FactionManager Instance { get; } = new FactionManager();
    internal enum FactionID { Duplicant, Pest }
    internal enum Disposition { Assist, Hostile }
    internal Disposition GetDisposition(FactionID source, FactionID target)
        => target == FactionID.Duplicant ? Disposition.Assist : Disposition.Hostile;
}
internal sealed class FactionAlignment
{
    internal GameObject gameObject { get; set; }
    internal bool canBePlayerTargeted = true;
    internal FactionManager.FactionID Alignment = FactionManager.FactionID.Pest;
    internal int Calls;
    internal bool Targeted;
    internal bool IsAlignmentActive() => true;
    internal void SetPlayerTargeted(bool value) { Calls++; Targeted = value; }
}
internal sealed class Capturable
{
    internal GameObject gameObject { get; set; }
    internal int Calls;
    internal bool Marked;
    internal bool IsCapturable() => true;
    internal void MarkForCapture(bool value, PrioritySetting setting, bool updatePriority)
    { Calls++; Marked = value; }
}
internal sealed class ComponentCollection<T> { internal readonly List<T> Items = new List<T>(); }
internal static class Components
{
    internal static readonly ComponentCollection<FactionAlignment> FactionAlignments = new ComponentCollection<FactionAlignment>();
    internal static readonly ComponentCollection<Capturable> Capturables = new ComponentCollection<Capturable>();
}

namespace OniMcp.Tools
{
    public static partial class ToolUtil
    {
        internal static string CleanName(string value) => value;
        internal static bool CellMatchesWorld(int cell, int worldId) => worldId < 0 || Grid.WorldIdx[cell] == worldId;
        internal static bool GameObjectMatchesWorld(GameObject go, int worldId) => CellMatchesWorld(go.Cell, worldId);
        internal static Dictionary<string, int> GetRect(JObject args)
        {
            int x = GetInt(args, "x1") ?? GetInt(args, "x") ?? 0;
            int y = GetInt(args, "y1") ?? GetInt(args, "y") ?? 0;
            return new Dictionary<string, int> { ["x1"] = x, ["y1"] = y,
                ["x2"] = GetInt(args, "x2") ?? x, ["y2"] = GetInt(args, "y2") ?? y };
        }
    }
    public static partial class OrdersTools
    {
        private const int CancelEvent = 99;
        private static Dictionary<string, McpToolParameter> RectParams(Dictionary<string, McpToolParameter> extra) => extra;
        private static bool HasRectInput(JObject args) => args["x1"] != null;
        private static int RectCellCount(Dictionary<string, int> rect)
            => (rect["x2"] - rect["x1"] + 1) * (rect["y2"] - rect["y1"] + 1);
        private static bool CellInRect(int cell, Dictionary<string, int> rect, int worldId)
            => Grid.IsValidCell(cell) && ToolUtil.CellMatchesWorld(cell, worldId)
                && Grid.CellColumn(cell) >= rect["x1"] && Grid.CellColumn(cell) <= rect["x2"]
                && Grid.CellRow(cell) >= rect["y1"] && Grid.CellRow(cell) <= rect["y2"];
        private static List<FactionAlignment> FindAttackTargets(JObject args)
            => Components.FactionAlignments.Items.Where(t =>
                (args["id"] == null || t.gameObject.GetInstanceID() == (int)args["id"])
                && (!HasRectInput(args) || CellInRect(t.gameObject.Cell, ToolUtil.GetRect(args), ToolUtil.ResolveWorldId(args)))).ToList();
        private static Dictionary<string, object> TargetResult(GameObject go, FactionAlignment target, string status)
            => ObjectResult(go, status);
        private static Dictionary<string, object> ObjectResult(GameObject go, string status)
            => new Dictionary<string, object> { ["id"] = go.GetInstanceID(), ["status"] = status };
        private static GameObject FindTarget(JObject args)
            => args["id"] == null ? null : UnityEngine.Object.Registered.OfType<KPrefabID>()
                .FirstOrDefault(id => id.InstanceID == (int)args["id"])?.gameObject;
    }
}
