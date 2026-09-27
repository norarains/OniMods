using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Tools;
using UnityEngine;

internal static class ConstructionAndFilterRegression
{
    internal static void Run()
    {
        var clear = new HashSet<int> { 10, 11, 12, 20, 21 };
        var reachable = new HashSet<int> { 10 };
        Func<int, bool> free = clear.Contains, nav = reachable.Contains;
        Check(ConstructionWorkCells.FindReachable(new[] { new[] { 10 }, new[] { 20 } }, free, nav) == 10,
            "one native work position is sufficient for a multi-cell building");
        Check(ConstructionWorkCells.FindReachable(new[] { new[] { 10, 99 } }, free, nav) == -1,
            "reachable worker cannot build through blocked native clearance cells");
        Check(ConstructionWorkCells.FindReachable(new[] { new[] { 10, 99 }, new[] { 10, 11 } }, free, nav) == 10,
            "alternate unobstructed route to the same work cell remains eligible");
        Check(ConstructionWorkCells.FindReachable(new[] { new[] { 20, 21 } }, free, nav) == -1,
            "clear but disconnected position is not construction access");
        Check(ConstructionWorkCells.FindReachable(new[] { new[] { -1 }, new int[0] }, free, nav) == -1,
            "invalid and empty native rows fail closed");
        clear.Remove(10);
        Check(ConstructionWorkCells.FindReachable(new[] { new[] { 10, 11 } }, free, nav) == 10,
            "native navigation decides work-cell validity, including traversable solid cells");

        var go = new GameObject(); FilterTools.Target = go;
        var single = new Filterable(); go.Components[typeof(Filterable)] = single;
        var preview = JObject.Parse("{tag:'Hydrogen',dryRun:true}");
        var body = Body(FilterTools.TestSingle(preview));
        Check(single.SelectedTag == GameTags.Void && single.Writes == 0 && !(bool)body["changed"],
            "single preview never sets the native filter");
        Check((string)body["proposed"]["tag"] == "Hydrogen" && (bool)body["wouldChange"], "preview exposes intended value");
        preview["confirm"] = true;
        Check(!FilterTools.TestSingle(preview).IsError && single.Writes == 0, "dryRun wins over confirm");
        preview["dryRun"] = false; body = Body(FilterTools.TestSingle(preview));
        Check(single.SelectedTag == new Tag("Hydrogen") && single.Writes == 1 && (bool)body["changed"], "commit returns fresh selection");
        Check(FilterTools.TestSingle(JObject.Parse("{tag:'Invalid'}")).IsError && single.Writes == 1, "invalid selection leaves state intact");
        FilterTools.TestSingle(JObject.Parse("{clear:true,dryRun:true}"));
        Check(single.SelectedTag == new Tag("Hydrogen") && single.Writes == 1, "clear preview is pure");

        var tree = new TreeFilterable(); go.Components[typeof(TreeFilterable)] = tree;
        tree.Tags.Add(new Tag("Dirt"));
        body = Body(FilterTools.TestTree(JObject.Parse("{tags:['Algae'],mode:'replace',dryRun:true}")));
        Check(tree.Writes == 0 && tree.Tags.SetEquals(new[] { new Tag("Dirt") }) && (string)body["selected"][0]["tag"] == "Dirt",
            "tree preview preserves selected tags and delivery state");
        FilterTools.TestTree(JObject.Parse("{tags:['Algae'],mode:'add'}"));
        Check(tree.Writes == 1 && tree.Tags.Count == 2, "tree add commits once");
        Check(FilterTools.TestTree(JObject.Parse("{tags:['Dirt'],mode:'typo'}")).IsError && tree.Writes == 1,
            "unknown mode cannot accidentally add tags");
        var flat = new FlatTagFilterable(); go.Components[typeof(FlatTagFilterable)] = flat;
        flat.tagOptions.Add(new Tag("Dirt")); flat.tagOptions.Add(new Tag("Algae"));
        FilterTools.TestTree(JObject.Parse("{mode:'clear',dryRun:true}"));
        Check(flat.Writes == 0 && tree.Writes == 1, "flat preview never invokes native selection callbacks");
        Check(FilterTools.TestTree(JObject.Parse("{tags:['Invalid']}" )).IsError && flat.Writes == 0, "flat invalid tag fails before clearing");
        FilterTools.TestTree(JObject.Parse("{tags:['Algae']}"));
        Check(flat.Writes == 1 && tree.Tags.SetEquals(new[] { new Tag("Algae") }), "flat commit updates native selection");
        foreach (string spelling in new[] { "mode:'clear'", "clear:true" })
        {
            var args = JObject.Parse("{" + spelling + ",tags:['Dirt'],dryRun:true}");
            body = Body(FilterTools.TestTree(args));
            Check(((JArray)body["proposed"]).Count == 0 && tree.Tags.Count == 1,
                "clear ignores retained tags without mutating during preview");
            args["dryRun"] = false;
            FilterTools.TestTree(args);
            Check(tree.Tags.Count == 0, "clear commit cannot re-add supplied tags");
            tree.Tags.Add(new Tag("Algae"));
        }
        Console.WriteLine("Construction access and filter mutation regressions passed");
    }
    private static JObject Body(CallToolResult result) => JObject.Parse(result.Content[0].Text);
    private static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
}

internal sealed class Filterable
{
    private Tag selected = GameTags.Void;
    internal int Writes;
    internal Tag SelectedTag { get => selected; set { selected = value; Writes++; } }
}
internal sealed class TreeFilterable
{
    internal HashSet<Tag> Tags = new HashSet<Tag>();
    internal int Writes;
    internal HashSet<Tag> GetTags() => Tags;
    internal void UpdateFilters(HashSet<Tag> next) { Tags = new HashSet<Tag>(next); Writes++; }
}
internal sealed class FlatTagFilterable
{
    internal readonly HashSet<Tag> tagOptions = new HashSet<Tag>();
    internal int Writes;
}
namespace OniMcp.Tools
{
    public static partial class FilterTools
    {
        internal static GameObject Target;
        internal static CallToolResult TestSingle(JObject args) => SetSingleFilter(args);
        internal static CallToolResult TestTree(JObject args) => SetTreeFilter(args);
        private static GameObject FindTarget(JObject args) => Target;
        private static object TargetInfo(GameObject go) => new { id = 6844 };
        private static List<Tag> SingleFilterOptions(Filterable filter) => new List<Tag> { new Tag("Hydrogen"), new Tag("Oxygen") };
        private static Dictionary<string, object> TagInfo(Tag tag) => new Dictionary<string, object> { ["tag"] = tag.Name };
        private static List<Tag> ParseTags(JToken tags) => tags == null ? new List<Tag>() : tags.Select(tag => new Tag((string)tag)).ToList();
        private static void ApplyFlatTags(FlatTagFilterable flat, HashSet<Tag> next)
        { flat.Writes++; Target.GetComponent<TreeFilterable>().UpdateFilters(next); }
    }
}
