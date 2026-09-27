using System;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Tools;
using UnityEngine;

internal static class OrdersDryRunRegression
{
    private static int checks;
    private static GameObject target;
    private static FactionAlignment faction;
    private static Capturable capture;
    private static Prioritizable priority;
    private static EmptyWorkable empty;
    private static Deconstructable deconstruct;

    internal static void Run()
    {
        foreach (bool confirmed in new[] { false, true })
        {
            Reset();
            var args = Area(confirmed);
            var result = OrdersTools.CancelArea().Handler(args);
            Check(!result.IsError, "cancel preview reaches native target enumeration");
            Check((int)Body(result)["wouldTriggerObjects"] == 1, "cancel preview deduplicates a multi-layer target");
            Check((int)Body(result)["triggeredObjects"] == 0, "cancel response never reports applied work in preview");
            Check((int)Body(result)["wouldCancelAttacks"] == 1 && (int)Body(result)["wouldCancelCaptures"] == 1,
                "cancel previews attack and capture targets");
            AssertUnchanged("cancel confirm=" + confirmed);

            foreach (string action in new[] { "mark", "cancel" })
            {
                Reset();
                args = Area(confirmed); args["id"] = 42; args["action"] = action;
                result = OrdersTools.Attack().Handler(args);
                Check(!result.IsError && (int)Body(result)["wouldChange"] == 1, "attack preview evaluates " + action);
                Check((int)Body(result)["changed"] == 0, "attack preview reports zero changes");
                AssertUnchanged("attack " + action + " confirm=" + confirmed);
            }

            foreach (bool debugInstant in new[] { false, true })
            {
                Reset(); DebugHandler.InstantBuildMode = debugInstant;
                args = Area(confirmed); args["type"] = "liquid";
                result = OrdersTools.EmptyConduits().Handler(args);
                Check(!result.IsError, "empty preview evaluates target in normal and instant modes");
                AssertUnchanged("empty confirm=" + confirmed + " instant=" + debugInstant);
                Check((int)Body(result)["marked"] == 0, "empty preview reports zero applied marks");

                Reset(); DebugHandler.InstantBuildMode = debugInstant;
                args = Area(confirmed); args["type"] = "liquid";
                result = OrdersTools.CutConduits().Handler(args);
                Check(!result.IsError, "cut preview does not require commit confirmation");
                AssertUnchanged("cut confirm=" + confirmed + " instant=" + debugInstant);
                Check((int)Body(result)["queued"] == 0, "cut preview reports zero queued deconstructions");

                // Utility sections lacking Deconstructable use Trigger fallback on commit.
                Reset(); target.Components.Remove(typeof(Deconstructable));
                args = Area(confirmed); args["type"] = "liquid";
                result = OrdersTools.CutConduits().Handler(args);
                Check(!result.IsError, "cut preview supports utility trigger fallback");
                AssertUnchanged("cut fallback confirm=" + confirmed);
            }
        }
        TestExactCutAndValidation();
        TestCommitSpies();
        TestQueuedDeconstructionIgnoresGlobalInstant();
        Reset();
        Console.WriteLine("Order dry-run handler regression: " + checks + " checks passed");
    }

    private static void TestExactCutAndValidation()
    {
        Reset();
        var other = new GameObject { Cell = target.Cell, name = "other wire" };
        var otherDeconstruct = new Deconstructable();
        other.Components[typeof(Deconstructable)] = otherDeconstruct;
        Grid.Objects[target.Cell, (int)ObjectLayer.Wire] = other;
        var args = JObject.Parse("{id:42,worldId:1,type:'all',dryRun:true,confirm:true}");
        var result = OrdersTools.CutConduits().Handler(args);
        Check(!result.IsError && (int)Body(result)["wouldQueue"] == 1,
            "exact ID cut previews only the requested object despite other utilities at the cell");
        AssertUnchanged("exact ID cut");
        Check(otherDeconstruct.Calls == 0, "exact ID preview cannot affect other layers");
        args["dryRun"] = false;
        Check(!OrdersTools.CutConduits().Handler(args).IsError && deconstruct.Calls == 1
            && otherDeconstruct.Calls == 0, "exact ID commit queues only the selected utility");

        Reset(); args = JObject.Parse("{id:999,worldId:1,type:'all',dryRun:true,confirm:true}");
        Check(OrdersTools.CutConduits().Handler(args).IsError, "unknown exact ID must not fall back to other utilities");
        AssertUnchanged("unknown ID cut");
        args["id"] = 42; args["worldId"] = 2;
        Check(OrdersTools.CutConduits().Handler(args).IsError, "exact ID respects selected world");
        AssertUnchanged("wrong-world cut");

        Reset(); deconstruct.allowDeconstruction = false;
        args = Area(true); args["type"] = "liquid";
        result = OrdersTools.CutConduits().Handler(args);
        Check((int)Body(result)["wouldQueue"] == 0 && (int)Body(result)["skipped"] == 1,
            "preview runs native deconstruction eligibility and reports rejected targets");
        AssertUnchanged("ineligible cut");

        foreach (var handler in new[] { OrdersTools.CancelArea(), OrdersTools.Attack(),
            OrdersTools.EmptyConduits(), OrdersTools.CutConduits() })
        {
            Reset(); args = Area(false); args["dryRun"] = false; args["type"] = "liquid";
            Check(handler.Handler(args).IsError, "unconfirmed commit is rejected: " + handler.Name);
            AssertUnchanged("unconfirmed " + handler.Name);
        }
    }

    private static void TestCommitSpies()
    {
        Reset(); var args = Area(true); args["dryRun"] = false;
        Check(!OrdersTools.CancelArea().Handler(args).IsError && target.NativeTriggers == 1
            && faction.Calls == 1 && capture.Calls == 1, "cancel spies detect real native writes");
        Reset(); args = Area(true); args["dryRun"] = false; args["id"] = 42;
        Check(!OrdersTools.Attack().Handler(args).IsError && faction.Calls == 1 && priority.Calls == 1,
            "attack spies detect real native writes");
        Reset(); args = Area(true); args["dryRun"] = false; args["type"] = "liquid";
        Check(!OrdersTools.EmptyConduits().Handler(args).IsError && empty.Marked == 1 && priority.Calls == 1,
            "empty spies detect real native writes");
        Reset(); args = Area(true); args["dryRun"] = false; args["type"] = "liquid";
        Check(!OrdersTools.CutConduits().Handler(args).IsError && deconstruct.Calls == 1 && priority.Calls == 1,
            "cut spies detect real native queue writes");
        Reset(); target.Components.Remove(typeof(Deconstructable));
        Check(!OrdersTools.CutConduits().Handler(args).IsError && target.NativeTriggers == 1 && priority.Calls == 1,
            "cut spies detect native fallback events");
    }

    private static void TestQueuedDeconstructionIgnoresGlobalInstant()
    {
        Reset(); DebugHandler.InstantBuildMode = true;
        // Positive control models native QueueDeconstruction's destructive branch.
        var control = new Deconstructable();
        control.QueueDeconstruction(userTriggered: true);
        Check(control.InstantCompletions == 1, "native double detects user-triggered instant completion");
        var args = Area(true); args["dryRun"] = false; args["type"] = "liquid";
        Check(!OrdersTools.CutConduits().Handler(args).IsError && deconstruct.Calls == 1
            && deconstruct.LastUserTriggered == false && deconstruct.InstantCompletions == 0,
            "ordinary cut queues dupe work even while global instant mode is enabled");
        Check(DebugHandler.InstantBuildMode, "direct deconstruction preserves the player's debug setting");

        foreach (bool preview in new[] { false, true })
        {
            Reset(); DebugHandler.InstantBuildMode = true; deconstruct.allowDeconstruction = false;
            args["dryRun"] = preview;
            var result = Body(OrdersTools.CutConduits().Handler(args));
            Check((int)result["queued"] == 0 && (int)result["skipped"] == 1 && deconstruct.Calls == 0,
                "global instant mode cannot bypass deconstruction eligibility; preview=" + preview);
        }

        Reset(); DebugHandler.InstantBuildMode = true;
        target.Components.Remove(typeof(Deconstructable)); args["dryRun"] = false;
        Check(!OrdersTools.CutConduits().Handler(args).IsError && target.NativeTriggers == 1
            && target.InstantDeconstructionTriggers == 0 && DebugHandler.InstantBuildMode,
            "utility fallback temporarily suppresses instant mode and restores it afterward");
        Reset(); DebugHandler.InstantBuildMode = true;
        target.Components.Remove(typeof(Deconstructable)); target.ThrowOnNativeTrigger = true;
        bool threw = false;
        try { OrdersTools.CutConduits().Handler(args); }
        catch (InvalidOperationException) { threw = true; }
        Check(threw && DebugHandler.InstantBuildMode && target.InstantDeconstructionTriggers == 0,
            "fallback restores global instant mode even if native designation throws");
    }

    private static JObject Area(bool confirmed) => new JObject {
        ["x1"] = 20, ["x2"] = 21, ["y1"] = 2, ["y2"] = 2,
        ["worldId"] = 1, ["dryRun"] = true, ["confirm"] = confirmed, ["priority"] = 8
    };
    private static JObject Body(CallToolResult result) => JObject.Parse(result.Content[0].Text);
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new InvalidOperationException("Order dry-run: " + message); }
    private static void AssertUnchanged(string context)
    {
        Check(target.NativeTriggers == 0 && faction.Calls == 0 && capture.Calls == 0
            && priority.Calls == 0 && empty.Marked == 0 && empty.Emptied == 0 && deconstruct.Calls == 0,
            context + " must not call any native mutation");
        Check(faction.Targeted && capture.Marked, context + " preserves existing designations");
    }
    private static void Reset()
    {
        Array.Clear(Grid.Objects, 0, Grid.Objects.Length);
        Array.Clear(Grid.WorldIdx, 0, Grid.WorldIdx.Length);
        Components.FactionAlignments.Items.Clear(); Components.Capturables.Items.Clear();
        UnityEngine.Object.Registered.Clear(); DebugHandler.InstantBuildMode = false;
        int cell = Grid.XYToCell(20, 2);
        Grid.WorldIdx[cell] = 1; Grid.WorldIdx[cell + 1] = 1;
        target = new GameObject { Cell = cell, name = "test pipe" };
        var id = new KPrefabID { InstanceID = 42, PrefabTag = new Tag("LiquidConduit"), gameObject = target };
        target.Components[typeof(KPrefabID)] = id; UnityEngine.Object.Registered.Add(id);
        faction = new FactionAlignment { gameObject = target, Targeted = true };
        capture = new Capturable { gameObject = target, Marked = true };
        priority = new Prioritizable(); empty = new EmptyWorkable(); deconstruct = new Deconstructable();
        target.Components[typeof(Prioritizable)] = priority;
        target.Components[typeof(EmptyWorkable)] = empty;
        target.Components[typeof(Deconstructable)] = deconstruct;
        Components.FactionAlignments.Items.Add(faction); Components.Capturables.Items.Add(capture);
        Grid.Objects[cell, (int)ObjectLayer.LiquidConduit] = target;
        Grid.Objects[cell, (int)ObjectLayer.LiquidConduitTile] = target;
    }
}
