using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Server;
using OniMcp.Support;
using UnityEngine;

namespace OniMcp.Tools
{
    internal sealed class GameContinueRunner : MonoBehaviour
    {
        private static GameContinueRunner instance;
        private Game game;
        private string session;
        private int generation, cancellation, worldId;
        private GameContinueMonitor monitor;
        private GameContinuePolicy policy;
        private ContinueSample latest, start;
        private ContinueDecision decision;
        private TaskCompletionSource<CallToolResult> completion;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private double started;
        private BoundedGameWindow window;
        private int activity, removed;
        private bool progressed;
        internal static bool Active => instance != null && instance.completion != null;

        internal static CallToolResult Begin(JObject args)
        {
            if (!DeferredToolCall.IsDirect(args))
                return CallToolResult.Error("continue requires a direct game_control tools/call; do not put it in a batch, program, resource, or protocol task.");
            if (Active) return CallToolResult.Error("A continue window is already active; wait for its result or call speed/pause.");
            if (Game.Instance == null || Game.Instance.IsLoading() || SpeedControlScreen.Instance == null)
                return CallToolResult.Error("A loaded game and speed control are required.");
            if (!SpeedControlScreen.Instance.IsPaused)
                return CallToolResult.Error("Pause before starting continue so its baseline is consistent.");
            double seconds = args["seconds"] == null ? 15 : args["seconds"].Value<double>();
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 1 || seconds > GameContinuePolicy.MaxSeconds)
                return CallToolResult.Error("seconds must be between 1 and 20 real seconds.");
            int speed = ToolUtil.GetInt(args, "speed") ?? SpeedControlScreen.Instance.GetSpeed() + 1;
            if (speed < 1 || speed > 3) return CallToolResult.Error("continue speed must be 1, 2, or 3.");
            if (ToolUtil.GetBool(args, "dryRun", false))
                return CallToolResult.Error("continue does not support dryRun; it advances only already planned work.");
            int world = ToolUtil.GetInt(args, "worldId") ?? ClusterManager.Instance.activeWorldId;
            if (ClusterManager.Instance.GetWorld(world) == null) return CallToolResult.Error("Unknown worldId.");
            if (instance == null) instance = MainThreadBridge.Instance.gameObject.AddComponent<GameContinueRunner>();
            return instance.StartWindow(args, world, seconds, speed);
        }

        private CallToolResult StartWindow(JObject args, int world, double seconds, int speed)
        {
            string owner = McpHttpServer.CurrentSessionId;
            bool reset = policy == null || game != Game.Instance || world != worldId || owner != session
                || generation != GameContextLifecycle.CaptureGeneration() || ToolUtil.GetBool(args, "resetMonitor", false);
            game = Game.Instance;
            session = owner;
            worldId = world;
            generation = GameContextLifecycle.CaptureGeneration();
            cancellation = DeferredToolCall.CancellationGeneration;
            if (reset)
            {
                monitor = new GameContinueMonitor(world);
                policy = new GameContinuePolicy();
            }
            latest = monitor.Read(clock.Elapsed.TotalSeconds);
            if (reset) policy.Reset(latest);
            start = latest;
            activity = removed = 0;
            progressed = false;
            started = clock.Elapsed.TotalSeconds;
            decision = policy.Observe(latest);
            if (decision.Decision != "continue") return Result("preflight");
            completion = new TaskCompletionSource<CallToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var deferred = new DeferredToolResult(completion.Task);
            window = new BoundedGameWindow(() => clock.Elapsed.TotalSeconds,
                () =>
                {
                    var control = SpeedControlScreen.Instance;
                    if (game != null && game == Game.Instance && control != null && !control.IsPaused) control.Pause();
                },
                () => { Observe(); return decision.Decision == "continue" ? null : "watch_triggered"; }, Complete);
            window.Start(seconds, () =>
            {
                SpeedControlScreen.Instance.SetSpeed(speed - 1);
                // Respect modal/user pause layers: do not clear them in a monitoring loop.
                SpeedControlScreen.Instance.Unpause();
            });
            return deferred;
        }

        private void Update()
        {
            if (completion == null) return;
            try
            {
                if (game != Game.Instance || GameContextLifecycle.RejectionReason(generation) != null)
                { Finish("game_context_changed"); return; }
                if (cancellation != DeferredToolCall.CancellationGeneration)
                { Finish("server_stopped"); return; }
                if (SpeedControlScreen.Instance == null || SpeedControlScreen.Instance.IsPaused)
                { Finish("external_pause"); return; }
                window.Tick();
            }
            catch (Exception ex) { Finish("monitor_failed", ex.Message); }
        }

        private void Observe(bool refreshFood = false)
        {
            latest = monitor.Read(clock.Elapsed.TotalSeconds, refreshFood);
            decision = policy.Observe(latest);
            activity += decision.Activity;
            removed += decision.Completed;
            progressed |= decision.WorkProgress;
        }

        private void Finish(string reason, string error = null)
        {
            window?.Stop(reason, error);
        }

        private void Complete(string reason, string error)
        {
            var pending = completion;
            if (pending == null) return;
            // BoundedGameWindow has already paused before any fallible final reads.
            bool sameGame = game != null && game == Game.Instance;
            try
            {
                if (sameGame && reason == "window_complete") Observe(refreshFood: true);
                if (reason != "window_complete" && reason != "watch_triggered")
                    decision.Add("replan", reason);
                if (error != null) decision.Add("replan", "monitor_error");
                pending.TrySetResult(Result(reason, error));
            }
            catch (Exception ex)
            {
                pending.TrySetResult(CallToolResult.Error("continue stopped; final state unavailable: " + ex.Message));
            }
            finally { completion = null; }
        }

        private CallToolResult Result(string reason, string error = null)
        {
            bool paused = game == Game.Instance && SpeedControlScreen.Instance != null && SpeedControlScreen.Instance.IsPaused;
            if (!paused) decision.Add("urgent", "pause_not_confirmed");
            var result = new JObject
            {
                ["decision"] = decision.Decision, ["stopReason"] = reason, ["isPaused"] = paused,
                ["elapsedSeconds"] = Math.Round(clock.Elapsed.TotalSeconds - started, 2),
                ["gameSecondsAdvanced"] = Math.Round(latest.GameSeconds - start.GameSeconds, 2),
                ["worldId"] = worldId, ["cycle"] = Math.Round(latest.GameSeconds / 600.0, 3),
                ["safety"] = new JObject
                {
                    ["dupes"] = latest.Dupes.Count, ["foodKcal"] = Math.Round(latest.FoodKcal),
                    ["foodDeltaKcal"] = Math.Round(latest.FoodKcal - start.FoodKcal),
                    ["maxStress"] = latest.Dupes.Count == 0 ? 0 : Math.Round(latest.Dupes.Max(d => d.Stress), 1),
                    ["minBreath"] = Math.Round(latest.Dupes.Where(d => d.Breath >= 0).Select(d => d.Breath).DefaultIfEmpty(-1).Min(), 1),
                    ["minHealthPercent"] = Math.Round(latest.Dupes.Where(d => d.Health >= 0).Select(d => d.Health).DefaultIfEmpty(-1).Min(), 1),
                    ["redAlert"] = latest.RedAlert, ["alerts"] = JArray.FromObject(latest.Alerts.Take(5))
                },
                ["work"] = new JObject
                {
                    ["working"] = latest.Working, ["unexpectedIdle"] = latest.Idle,
                    ["restingOrTendingNeeds"] = latest.LocalDupeCount - latest.Working - latest.Idle,
                    ["pendingBuilds"] = latest.PendingBuilds, ["pendingDigs"] = latest.PendingDigs,
                    ["ordersRemoved"] = removed, ["activityChanges"] = activity, ["workProgressObserved"] = progressed,
                    ["researchId"] = latest.ResearchId, ["researchPercent"] = Math.Round(latest.ResearchProgress, 1)
                },
                ["reasons"] = JArray.FromObject(decision.Reasons),
                ["next"] = decision.Decision == "continue"
                    ? "Repeat continue directly; no separate snapshot or map read is needed."
                    : "Keep paused. Address the reasons, plan useful work, then continue with resetMonitor=true."
            };
            if (error != null) result["error"] = error;
            return CallToolResult.Text(result.ToString(Formatting.None));
        }

        internal static CallToolResult Guard(string name, JObject args)
        {
            if (!Active) return null;
            bool speed = name == "game_control" && string.Equals(args?["domain"]?.ToString(), "speed", StringComparison.OrdinalIgnoreCase);
            string action = args?["action"]?.ToString()?.ToLowerInvariant();
            if (speed && action == "pause") { instance.Finish("explicit_pause"); return null; }
            if (speed && action == "time") return null;
            return CallToolResult.Error("continue is running. Wait for its paused result, or call game_control domain=speed action=pause before other tools.");
        }

        private void OnDisable() { Finish("monitor_disabled"); }
        private void OnDestroy() { Finish("monitor_destroyed"); if (instance == this) instance = null; }
    }
}
