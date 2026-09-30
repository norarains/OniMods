using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using UnityEngine;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class HarmonyPatch : Attribute
    {
        internal Type TargetType { get; }
        internal string MethodName { get; }
        public HarmonyPatch(Type type, string method) { TargetType = type; MethodName = method; }
        public HarmonyPatch(Type type, string method, Type[] parameters) : this(type, method) { }
    }
}
namespace KSerialization
{
    public enum MemberSerialization { OptIn }
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class SerializationConfig : Attribute { public SerializationConfig(MemberSerialization value) { } }
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeAttribute : Attribute { }
}
public interface ISim200ms { void Sim200ms(float dt); }
public class KMonoBehaviour
{
    internal GameObject gameObject = new GameObject();
    public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
    internal void Subscribe(int id, Action<object> handler)
    {
        gameObject.EventHandlers.TryGetValue(id, out var current);
        gameObject.EventHandlers[id] = current + handler;
    }
}
namespace UnityEngine
{
    public sealed partial class GameObject
    {
        internal T AddOrGet<T>() where T : class, new()
        {
            var component = GetComponent<T>();
            if (component != null) return component;
            component = new T(); Components[typeof(T)] = component;
            if (component is KMonoBehaviour behaviour) behaviour.gameObject = this;
            return component;
        }
    }
}
internal sealed partial class KPrefabID
{
    internal readonly HashSet<Tag> SerializableTags = new HashSet<Tag>();
    internal void AddTag(Tag tag, bool save)
    { Tags.Add(tag); if (save) SerializableTags.Add(tag); }
}
internal sealed partial class Constructable : KMonoBehaviour { }
internal sealed class SaveGame : KMonoBehaviour { internal static SaveGame Instance { get; set; } }
internal sealed class Tech
{
    internal string Id { get; set; }
    internal string Name => Id;
    internal string category => "fixture";
    internal string desc => "Fixture technology";
    internal int Tier { get; set; }
    internal int tier => Tier;
    internal readonly List<Tech> Required = new List<Tech>();
    internal List<Tech> requiredTech => Required;
    internal readonly List<string> searchTerms = new List<string>();
    internal readonly List<string> unlockedItemIDs = new List<string>();
    internal readonly List<Tech> unlockedItems = new List<Tech>();
    internal readonly Dictionary<string, float> costsByResearchTypeID = new Dictionary<string, float>();
}
internal sealed class TechInstance
{
    internal Tech tech { get; set; }
    internal bool Completed { get; set; }
    internal double Points { get; set; }
    internal bool IsComplete() => Completed;
    internal float GetTotalPercentageComplete() => (float)(Points / 100.0);
}
internal sealed class TestTechDatabase
{
    internal readonly Dictionary<string, Tech> Items = new Dictionary<string, Tech>();
    internal int Count => Items.Count;
    internal object GetResource(int index) => Items.Values.ElementAt(index);
    internal Tech TryGet(string id) => id != null && Items.TryGetValue(id, out var tech) ? tech : null;
}
internal sealed partial class Db
{
    internal static readonly TestTechDatabase TestTechs = new TestTechDatabase();
    internal TestTechDatabase Techs => TestTechs;
}
internal sealed class Research
{
    internal static Research Instance { get; set; }
    internal readonly Dictionary<string, TechInstance> Techs = new Dictionary<string, TechInstance>();
    private readonly List<TechInstance> queue = new List<TechInstance>();
    private TechInstance active;
    internal int Writes;
    internal Action OnSet;
    internal Action OnNativeAdvance;
    internal Action OnNativeLoad;
    internal TechInstance Get(Tech tech) => Techs.TryGetValue(tech.Id, out var item) ? item : null;
    internal TechInstance GetActiveResearch() => active;
    internal bool IsBeingResearched(Tech tech) => active?.tech == tech;
    internal TechInstance GetTargetResearch() => queue.LastOrDefault();
    internal List<TechInstance> GetResearchQueue() => queue.ToList();
    // This fixture models the installed native boundary: head's prerequisite
    // graph, tier ordering, and separate immutable-by-selection point balances.
    internal void SetActiveResearch(Tech tech, bool clearQueue)
        => ResearchNativePatches.Run(this, nameof(SetActiveResearch), () => SetActiveBody(tech, clearQueue));

    private void SetActiveBody(Tech tech, bool clearQueue)
    {
        Writes++;
        if (clearQueue) queue.Clear();
        active = null;
        if (tech == null) queue.Clear();
        else
        {
            if (queue.Count == 0) Add(tech);
            queue.Sort((a, b) => a.tech.Tier.CompareTo(b.tech.Tier));
            active = queue.FirstOrDefault();
        }
        OnSet?.Invoke();
    }
    private void Add(Tech tech)
    {
        var item = Get(tech);
        if (!item.IsComplete() && !queue.Contains(item)) queue.Add(item);
        foreach (var required in tech.Required) Add(required);
    }
    internal void CompleteActive()
    {
        active.Completed = true;
        GetNextTech();
    }

    internal void GetNextTech() => ResearchNativePatches.Run(this, nameof(GetNextTech), () =>
    {
        OnNativeAdvance?.Invoke();
        if (queue.Count > 0) queue.RemoveAt(0);
        SetActiveResearch(queue.LastOrDefault()?.tech, false);
    });

    internal void CancelResearch(Tech tech, bool clickedEntry = true)
        => ResearchNativePatches.Run(this, nameof(CancelResearch), () =>
        {
            if (queue.Any(t => t.tech == tech)) SetActiveResearch(null, false);
        });

    internal void OnDeserialized(Tech savedTarget)
        => ResearchNativePatches.Run(this, nameof(OnDeserialized), () =>
        {
            OnNativeLoad?.Invoke();
            SetActiveResearch(savedTarget, false);
        });
}

// Execute the production patch methods at the corresponding native boundaries.
// This proves queue behavior through hooks, while making no claim about Harmony
// installation or Unity's save codec (those still require an ONI runtime test).
internal static class ResearchNativePatches
{
    internal static void Run(Research instance, string method, Action body)
    {
        var patches = typeof(OniMcp.Tools.ResearchTargetQueue).GetNestedTypes(BindingFlags.NonPublic)
            .Where(type => type.GetCustomAttributes<HarmonyLib.HarmonyPatch>()
                .Any(patch => patch.TargetType == typeof(Research) && patch.MethodName == method)).ToArray();
        Exception failure = null;
        try
        {
            Invoke(patches, "Prefix", instance, null);
            body();
            Invoke(patches, "Postfix", instance, null);
        }
        catch (Exception ex) { failure = ex; }
        finally { Invoke(patches, "Finalizer", instance, failure); }
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Invoke(Type[] patches, string phase, Research instance, Exception failure)
    {
        foreach (var patch in patches)
        {
            var method = patch.GetMethod(phase, BindingFlags.Static | BindingFlags.NonPublic);
            if (method == null) continue;
            object[] arguments = method.GetParameters().Select(parameter =>
            {
                if (parameter.Name == "__instance") return (object)instance;
                if (parameter.Name == "__exception") return failure;
                throw new InvalidOperationException("Unmodeled native patch argument: " + parameter.Name);
            }).ToArray();
            try { method.Invoke(null, arguments); }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); }
        }
    }
}
