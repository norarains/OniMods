using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KSerialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Tools;

internal static class ResearchQueueRegression
{
    private static int checks;
    private static ResearchTargetQueue queue;
    private static Tech a, b, prerequisite;

    internal static void Run()
    {
        Reset();
        queue.Select(a, true); queue.Select(b, false);
        Check(queue.Read().SequenceEqual(new[] { "A", "B" }), "append retains FIFO requested targets");
        Check(Research.Instance.GetActiveResearch().tech == prerequisite, "native prerequisite remains active");
        Check(Research.Instance.GetTargetResearch().tech == a, "lower-tier appended B cannot preempt A");
        Check(Research.Instance.GetResearchQueue().All(t => t.tech != b), "only head enters native queue");
        int writes = Research.Instance.Writes;
        queue.Select(a, false); queue.Select(b, false);
        Check(queue.Read().SequenceEqual(new[] { "A", "B" }) && Research.Instance.Writes == writes,
            "duplicate appends neither reorder targets nor restart native research");
        Complete();
        Check(queue.Read().SequenceEqual(new[] { "A", "B" }) && Research.Instance.GetActiveResearch().tech == a,
            "prerequisite completion does not pop FIFO head");
        Complete();
        Check(queue.Read().SequenceEqual(new[] { "B" }) && Research.Instance.GetActiveResearch().tech == b,
            "head completion immediately activates next requested target");
        Complete();
        Check(queue.Read().Length == 0 && Research.Instance.GetActiveResearch() == null,
            "completed program stays empty");

        Reset(); Research.Instance.SetActiveResearch(a, true);
        queue.Select(b, false);
        Check(queue.Read().SequenceEqual(new[] { "A", "B" }), "append adopts an existing native target");
        queue.Clear();
        Check(queue.Read().Length == 0 && Research.Instance.GetResearchQueue().Count == 0,
            "clear removes saved program and native graph");
        queue.Sim200ms(.2f);
        Check(Research.Instance.GetActiveResearch() == null, "periodic sync cannot revive explicitly cleared research");

        TestPersistence();
        TestPlayerOverridesAndReentrancy();
        TestSameTargetUiOverride();
        TestNativeScopeCleanup();
        TestClearHandlerPreview();
        Console.WriteLine("Persistent research component regression: " + checks + " checks passed");
    }

    private static void TestPersistence()
    {
        Reset(); queue.Select(a, true); queue.Select(b, false);
        Research.Instance.Get(prerequisite).Points = 7;
        var field = typeof(ResearchTargetQueue).GetField("targets", BindingFlags.Instance | BindingFlags.NonPublic);
        Check(field != null && field.GetCustomAttribute<SerializeAttribute>() != null,
            "FIFO identities are explicitly included in native opt-in serialization");
        Check(typeof(ResearchTargetQueue).GetCustomAttribute<SerializationConfig>() != null,
            "component declares serialization configuration");
        // Round-trip the production serialized field, not an inferred private
        // native research queue. This verifies saved intent, not Klei's binary codec.
        string saved = JsonConvert.SerializeObject(field.GetValue(queue));
        var restored = new ResearchTargetQueue();
        field.SetValue(restored, JsonConvert.DeserializeObject<List<string>>(saved));
        SaveGame.Instance.gameObject.Components[typeof(ResearchTargetQueue)] = restored;
        queue = restored;
        Research.Instance.OnDeserialized(a);
        InvokePatch("Restore", "Postfix");
        Check(queue.Read().SequenceEqual(new[] { "A", "B" }), "load retains unrelated queued targets");
        Check(Research.Instance.Get(prerequisite).Points == 7 && Research.Instance.GetActiveResearch().tech == prerequisite,
            "restoration neither resets progress nor skips prerequisites");
        int writes = Research.Instance.Writes;
        queue.Sim200ms(.2f); queue.Read();
        Check(Research.Instance.Writes == writes, "restoration is idempotent after first sync");

        field.SetValue(queue, new List<string> { "missing_dlc", "A", "B" });
        Research.Instance.Get(a).Completed = true;
        queue.Synchronize();
        Check(queue.Read().SequenceEqual(new[] { "B" }) && Research.Instance.GetActiveResearch().tech == b,
            "removed/completed heads do not block remaining valid targets");

        Reset(); Research.Instance.SetActiveResearch(b, true); writes = Research.Instance.Writes;
        InvokePatch("Restore", "Postfix");
        Check(queue.Read().Length == 0 && Research.Instance.Writes == writes && Research.Instance.GetActiveResearch().tech == b,
            "legacy save without managed targets retains native selection");
    }

    private static void TestPlayerOverridesAndReentrancy()
    {
        Reset(); queue.Select(a, true); queue.Select(b, false);
        Research.Instance.SetActiveResearch(null, true); queue.Sim200ms(.2f);
        Check(queue.Read().Length == 0 && Research.Instance.GetActiveResearch() == null,
            "player cancellation wins over queued tail");
        Reset(); queue.Select(a, true); queue.Select(b, false);
        Research.Instance.SetActiveResearch(b, true); queue.Synchronize();
        Check(queue.Read().Length == 0 && Research.Instance.GetActiveResearch().tech == b,
            "different player selection releases managed program");
        Reset();
        Research.Instance.OnSet = () => queue.Synchronize();
        queue.Select(a, true); queue.Select(b, false);
        Check(queue.Read().SequenceEqual(new[] { "A", "B" }) && Research.Instance.Writes == 1,
            "native callbacks during controller selection cannot recursively reset or clear the program");
        Complete(); Complete();
        Check(queue.Read().SequenceEqual(new[] { "B" }), "completion advancement also tolerates reentrant callbacks");
        Research.Instance.OnSet = null;
    }

    private static void TestSameTargetUiOverride()
    {
        Reset(); queue.Select(a, true); queue.Select(b, false);
        Research.Instance.SetActiveResearch(a, true);
        Check(queue.Read().Length == 0 && Research.Instance.GetTargetResearch().tech == a,
            "explicit player re-selection of head with clearQueue must discard old managed tail");

        Reset(); queue.Select(a, true); queue.Select(b, false);
        Research.Instance.CancelResearch(a);
        Research.Instance.SetActiveResearch(a, true);
        Check(queue.Read().Length == 0 && Research.Instance.GetTargetResearch().tech == a,
            "cancel and reselect between samples cannot silently retain the old tail");

        Reset(); queue.Select(a, true); queue.Select(b, false);
        Research.Instance.OnDeserialized(a);
        Check(queue.Read().SequenceEqual(new[] { "A", "B" }),
            "native load selection is not mistaken for a player override after initial restoration");
    }

    private static void TestNativeScopeCleanup()
    {
        foreach (bool loading in new[] { false, true })
        {
            Reset(); queue.Select(a, true); queue.Select(b, false);
            var expected = new InvalidOperationException("native fixture failure");
            if (loading) Research.Instance.OnNativeLoad = () => throw expected;
            else Research.Instance.OnNativeAdvance = () => throw expected;
            Exception observed = null;
            try
            {
                if (loading) Research.Instance.OnDeserialized(a);
                else Research.Instance.GetNextTech();
            }
            catch (Exception ex) { observed = ex; }
            Check(ReferenceEquals(observed, expected), "native exceptions propagate from guarded " + (loading ? "load" : "advance"));
            Research.Instance.SetActiveResearch(a, true);
            Check(queue.Read().Length == 0,
                "native scope finalizer restores player override behavior after " + (loading ? "load" : "advance") + " failure");
        }
    }

    private static void Complete() => Research.Instance.CompleteActive();

    private static void TestClearHandlerPreview()
    {
        foreach (bool confirmed in new[] { false, true })
        {
            Reset(); queue.Select(a, true); queue.Select(b, false);
            int writes = Research.Instance.Writes;
            var active = Research.Instance.GetActiveResearch();
            var result = ResearchTools.ControlResearch().Handler(new JObject {
                ["action"] = "clear", ["dryRun"] = true, ["confirm"] = confirmed
            });
            Check(!result.IsError, "research clear supports preview with confirm=" + confirmed);
            var body = JObject.Parse(result.Content[0].Text);
            Check(body["dryRun"].Value<bool>() && !body["committed"].Value<bool>(),
                "research clear preview explicitly reports no commit");
            Check(Research.Instance.Writes == writes && Research.Instance.GetActiveResearch() == active
                && queue.Read().SequenceEqual(new[] { "A", "B" }),
                "research clear preview cannot erase native research or persisted FIFO intent");
        }
        Reset(); Research.Instance.SetActiveResearch(b, true);
        typeof(ResearchTargetQueue).GetField("targets", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(queue, new List<string> { "A", "B" });
        int beforeRestore = Research.Instance.Writes;
        var restoringPreview = ResearchTools.ControlResearch().Handler(new JObject {
            ["action"] = "clear", ["dryRun"] = true, ["confirm"] = true
        });
        Check(!restoringPreview.IsError && Research.Instance.Writes == beforeRestore
            && Research.Instance.GetActiveResearch().tech == b
            && queue.Snapshot().SequenceEqual(new[] { "A", "B" }),
            "clear preview during pending restoration reads intent without synchronizing native selection");
        Reset(); queue.Select(a, true); queue.Select(b, false);
        var rejected = ResearchTools.ControlResearch().Handler(new JObject { ["action"] = "clear" });
        Check(rejected.IsError && queue.Read().Length == 2, "research clear commit requires confirmation");
        var committed = ResearchTools.ControlResearch().Handler(new JObject { ["action"] = "clear", ["confirm"] = true });
        Check(!committed.IsError && queue.Read().Length == 0 && Research.Instance.GetActiveResearch() == null,
            "research clear confirmed commit removes native and persisted queues");
    }
    private static void Reset()
    {
        Db.TestTechs.Items.Clear(); SaveGame.Instance = new SaveGame(); Research.Instance = new Research();
        prerequisite = Add("prerequisite", 0); a = Add("A", 3); b = Add("B", 1); a.Required.Add(prerequisite);
        InvokePatch("Attach", "Postfix", SaveGame.Instance);
        queue = ResearchTargetQueue.Current;
    }
    private static Tech Add(string id, int tier)
    {
        var tech = new Tech { Id = id, Tier = tier }; Db.TestTechs.Items.Add(id, tech);
        Research.Instance.Techs.Add(id, new TechInstance { tech = tech }); return tech;
    }
    private static void InvokePatch(string patch, string method, params object[] args)
        => typeof(ResearchTargetQueue).GetNestedType(patch, BindingFlags.NonPublic)
            .GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new InvalidOperationException("Research queue: " + message); }
}
