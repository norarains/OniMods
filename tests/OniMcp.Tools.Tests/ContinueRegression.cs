using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Tools;

internal static class ContinueRegression
{
    private static int checks;
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception("Continue regression: " + message); }

    internal static void Run()
    {
        TestDecisions();
        ObservationRegression.Run();
        TestWindows();
        TestDeferredCalls();
        Console.WriteLine("Bounded continue regression checks passed: " + checks);
    }

    private static ContinueSample Sample(double time = 0, bool working = true)
    {
        var s = new ContinueSample { WorldId = 1, GameSeconds = time, FoodKcal = 5000, PendingBuilds = 0, PendingDigs = 0, PrintingReady = false, InfrastructureKnown = false, AdvancedResearchBlocked = false, CanLearnAdvancedResearch = false, Beds = 1, Toilets = 1, OxygenProducers = 1, ResearchQueueCount = 0, AdvancedResearchBuildingId = null };
        s.Dupes.Add(new ContinueDupe { Id = 1, WorldId = 1, Cell = 50, SkillPoints = 0, AdvancedResearchSkill = false, Valid = true, Working = working,
            Chore = "Build", Breath = 100, Health = 100, Calories = 2000000, Stress = 0, BodyTemperature = 310 });
        return s;
    }

    private static bool Has(ContinueEvents d, string code) => d.Items.Any(r => (string)r["code"] == code);

    private static void TestDecisions()
    {
        var p = new GameContinuePolicy();
        var s = Sample();
        p.Reset(s);
        var next = Sample(15); next.Dupes[0].Cell++;
        var d = p.Observe(next);
        Check(d.Stops == false && d.Activity == 1, "healthy movement is a cheap continue signal");
        Check(!d.WorkProgress && d.Completed == 0, "movement must not claim completed work");
        next = Sample(16); next.Dupes[0].Breath = 20;
        Check(p.Observe(next).Stops, "local low breath stops despite healthy food and stress");
        next = Sample(17); next.Dupes[0].Health = 98;
        Check(Has(p.Observe(next), "health"), "damage stops early");
        next = Sample(18); next.Dupes.Clear();
        d = p.Observe(next);
        Check(d.Stops && Has(d, "dupe_missing"), "a vanished dupe is not a green snapshot");
        foreach (var field in new[] { "food", "stress", "temperature", "calories", "red", "invalid", "unknown" })
        {
            p.Reset(Sample()); next = Sample(1);
            switch (field)
            {
                case "food": next.FoodKcal = 0; break;
                case "stress": next.Dupes[0].Stress = 90; break;
                case "temperature": next.Dupes[0].BodyTemperature = 320; break;
                case "calories": next.Dupes[0].Calories = 1000; break;
                case "red": next.RedAlert = true; break;
                case "invalid": next.Dupes[0].Valid = false; break;
                case "unknown": next.Available = false; break;
            }
            Check(p.Observe(next).Stops, field + " must stop the fast path");
        }
        p.Reset(Sample(0, false));
        Check(p.Observe(Sample(200, false)).Stops == false, "scheduled rest is not stalled work");
        next = Sample(201, false); next.Dupes[0].UnexpectedIdle = true;
        Check(p.Observe(next).Stops == false, "brief chore transition gets a grace period");
        next = Sample(206, false); next.Dupes[0].UnexpectedIdle = true;
        Check(p.Observe(next).Stops == false, "idle timer survives short continue windows");
        next = Sample(212, false); next.Dupes[0].UnexpectedIdle = true;
        Check(Has(p.Observe(next), "worker_idle"), "persistent unexpected idle requests planning");
        p.Reset(Sample());
        Check(Has(p.Observe(Sample(61)), "no_observed_progress"), "stalled work requests diagnosis");
        s = Sample(); s.PendingIds.Add(10); p.Reset(s);
        next = Sample(121); next.PendingIds.Add(10); next.Dupes[0].Cell++;
        Check(Has(p.Observe(next), "orders_not_progressing"), "movement cannot hide an unproductive queue forever");
        s = Sample(); s.PendingIds.Add(10); p.Reset(s);
        next = Sample(1, false);
        d = p.Observe(next);
        Check(d.Completed == 1 && Has(d, "orders_finished"), "exhausted tracked queue triggers planning");
        p.Reset(Sample()); next = Sample(40); next.WorkProgress = 5;
        Check(p.Observe(next).WorkProgress, "actual work reduction is progress");
        s = Sample(); s.ResearchId = "Basic"; s.ResearchProgress = 20; p.Reset(s);
        next = Sample(30); next.ResearchId = "Basic"; next.ResearchProgress = 30;
        Check(p.Observe(next).WorkProgress, "research can keep a stable loop running");
        next = Sample(31);
        Check(Has(p.Observe(next), "research_queue_empty"), "completed/changed research needs a decision");
        p.Reset(Sample(0, false));
        Check(!p.Observe(Sample(3000, false)).Stops, "no hidden review horizon interrupts the requested window");
        p.Reset(Sample(300, false));
        Check(p.Observe(Sample(301, false)).Stops == false, "explicit review resets the horizon");
        next = Sample(302, false); next.Alerts.Add("Bad: Power failure");
        Check(p.Observe(next).Stops, "new negative HUD alert stops for diagnosis");
        next = Sample(303, false); next.Alerts.Add("DuplicantThreatening: Trapped"); p.Reset(next);
        Check(p.Observe(next).Stops, "review cannot waive a threatening alert");
    }

    private static void TestWindows()
    {
        double time = 0; bool paused = true; int samples = 0, finishes = 0; string reason = null;
        var events = new List<string>();
        var w = new BoundedGameWindow(() => time, () => { paused = true; events.Add("pause"); },
            () => { samples++; return null; }, (r, error) => { finishes++; reason = r; events.Add("result"); });
        w.Start(15, () => paused = false);
        Check(!paused && w.Running, "start resumes without blocking the caller");
        w.Tick(); time = .1; w.Tick();
        Check(samples == 1, "health sampling has a bounded rate");
        time = 15; w.Tick();
        Check(paused && !w.Running && reason == "window_complete", "deadline pauses without any client follow-up");
        Check(string.Join(",", events) == "pause,result", "pause precedes response generation");
        w.Tick(); w.Stop("again");
        Check(finishes == 1, "completion and pause are idempotent");
        foreach (string stop in new[] { "explicit_pause", "server_stopped", "game_context_changed", "monitor_disabled" })
        {
            w.Start(20, () => paused = false); w.Tick(stop);
            Check(paused && reason == stop, "interruption stops immediately: " + stop);
        }
        w.Start(10, () => { paused = false; throw new Exception("resume"); });
        Check(paused && reason == "resume_failed", "partial resume failure pauses");
        w = new BoundedGameWindow(() => time, () => paused = true,
            () => { throw new Exception("bad read"); }, (r, e) => reason = r);
        w.Start(10, () => paused = false); w.Tick();
        Check(paused && reason == "monitor_failed", "monitor exceptions pause before reporting failure");
        w = new BoundedGameWindow(() => time, () => paused = true,
            () => "event", (r, e) => reason = r);
        w.Start(10, () => paused = false); w.Tick();
        Check(paused && reason == "event", "danger ends the window early");
        foreach (double invalid in new[] { 0, -1, 21, double.NaN, double.PositiveInfinity })
        {
            bool rejected = false;
            try { w.Start(invalid, () => paused = false); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected && paused, "invalid duration never resumes");
        }
    }

    private static void TestDeferredCalls()
    {
        var args = new JObject { ["action"] = "continue" };
        Check(!DeferredToolCall.IsDirect(args), "direct gate closed by default");
        DeferredToolCall.Invoke(args, () =>
        {
            Check(DeferredToolCall.IsDirect(args), "HTTP root arguments accepted");
            Check(!DeferredToolCall.IsDirect((JObject)args.DeepClone()), "nested batch arguments rejected");
            return CallToolResult.Text("ok");
        });
        Check(!DeferredToolCall.IsDirect(args), "direct gate closes after dispatch");
        var source = new TaskCompletionSource<CallToolResult>();
        var deferred = new DeferredToolResult(source.Task);
        deferred.Content.Add(new ToolContent { Text = "notification" });
        var resolved = deferred.Resolve();
        Check(!resolved.IsCompleted, "dispatch waits asynchronously for final result");
        source.SetResult(CallToolResult.Text("paused"));
        var result = resolved.GetAwaiter().GetResult();
        Check(result.Content.Count == 2 && result.Content[0].Text == "paused", "final result retains middleware notifications");
        Check(JObject.FromObject(deferred)["Completion"] == null, "task internals never appear on the wire");
        int before = DeferredToolCall.CancellationGeneration;
        DeferredToolCall.CancelPending();
        Check(DeferredToolCall.CancellationGeneration != before, "server stop revokes active leases");
    }
}
