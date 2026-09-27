using System;
using System.Diagnostics;
using System.Collections.Generic;
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
        private ContinueEvents events;
        private ContinueStopEvents stopSettings;
        private double requestedSeconds;
        private static readonly Dictionary<string, ContinueStopEvents> settingsBySession = new Dictionary<string, ContinueStopEvents>();
        private Dictionary<string, string> lastReported = new Dictionary<string, string>();
        private readonly Dictionary<string, Dictionary<string, object>> windowEvents = new Dictionary<string, Dictionary<string, object>>();
        private TaskCompletionSource<CallToolResult> completion;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private double started;
        private BoundedGameWindow window;
        private int activity, removed;
        private bool progressed, fullResponse, includeMetadata;
        private Dictionary<string, string> windowFindings;
        private readonly HashSet<string> changedInWindow = new HashSet<string>();
        internal static bool Active => instance != null && instance.completion != null;

        private static ContinueStopEvents Settings(JObject args)
        {
            string owner = McpHttpServer.CurrentSessionId ?? "local";
            if (!settingsBySession.TryGetValue(owner, out var settings))
                settingsBySession[owner] = settings = new ContinueStopEvents();
            settings.Update(EventKeys(args, "ignoreEvents"), EventKeys(args, "unignoreEvents"));
            return settings;
        }

        private static string[] EventKeys(JObject args, string key)
        {
            if (args[key] == null) return new string[0];
            if (!(args[key] is JArray values) || values.Any(value => value.Type != JTokenType.String))
                throw new ArgumentException(key + " must be an array of event codes or finding IDs.");
            return values.Values<string>().ToArray();
        }

        internal static CallToolResult Configure(JObject args)
        {
            if (Active) return CallToolResult.Error("Stop-event settings cannot change during a running window.");
            if (ToolUtil.GetBool(args, "dryRun", false))
                return CallToolResult.Error("stop_events does not support dryRun; omit ignoreEvents/unignoreEvents to read settings without changing them.");
            try
            {
                var settings = Settings(args);
                return CallToolResult.Text(JsonConvert.SerializeObject(new {
                    defaultEvents = ContinueStopEvents.Defaults, ignoredEvents = settings.Ignored,
                    scope = "MCP session; preserved across windows and save loads until explicitly changed"
                }));
            }
            catch (ArgumentException ex) { return CallToolResult.Error(ex.Message); }
        }

        internal static CallToolResult Begin(JObject args)
        {
            if (!DeferredToolCall.IsDirect(args))
                return CallToolResult.Error("continue requires a direct game_control tools/call; do not put it in a batch, program, resource, or protocol task.");
            if (args["resetMonitor"] != null)
                return CallToolResult.Error("resetMonitor was removed in schema 3; use explicit ignoreEvents/unignoreEvents or speed/stop_events.");
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
            try { return instance.StartWindow(args, world, seconds, speed); }
            catch (ArgumentException ex) { return CallToolResult.Error(ex.Message); }
        }

        private CallToolResult StartWindow(JObject args, int world, double seconds, int speed)
        {
            string owner = McpHttpServer.CurrentSessionId;
            bool reset = policy == null || game != Game.Instance || world != worldId || owner != session
                || generation != GameContextLifecycle.CaptureGeneration();
            string responseMode = args["responseMode"]?.ToString() ?? "summary";
            if (responseMode != "summary" && responseMode != "full")
                throw new ArgumentException("responseMode must be summary or full.");
            fullResponse = responseMode == "full";
            includeMetadata = reset || args["ignoreEvents"] != null || args["unignoreEvents"] != null;
            stopSettings = Settings(args);
            requestedSeconds = seconds;
            game = Game.Instance;
            session = owner;
            worldId = world;
            generation = GameContextLifecycle.CaptureGeneration();
            cancellation = DeferredToolCall.CancellationGeneration;
            if (reset)
            {
                monitor = ColonyObservationRuntime.Monitor(world, true);
                policy = new GameContinuePolicy();
                lastReported.Clear();
            }
            monitor.RefreshOrders();
            latest = monitor.Read(clock.Elapsed.TotalSeconds, true);
            if (reset) policy.Reset(latest);
            start = latest;
            activity = removed = 0;
            progressed = false;
            windowEvents.Clear();
            windowFindings = new Dictionary<string, string>(lastReported);
            changedInWindow.Clear();
            started = clock.Elapsed.TotalSeconds;
            events = policy.Observe(latest, stopSettings);
            CaptureEvents();
            if (events.Stops) return Result("event");
            completion = new TaskCompletionSource<CallToolResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            var deferred = new DeferredToolResult(completion.Task);
            window = new BoundedGameWindow(() => clock.Elapsed.TotalSeconds,
                () =>
                {
                    var control = SpeedControlScreen.Instance;
                    if (game != null && game == Game.Instance && control != null && !control.IsPaused) control.Pause();
                },
                () => { Observe(); return events.Stops ? "event" : null; }, Complete);
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
            if (!refreshFood)
                latest = ContinueObservationFreshness.BeforeStop(latest, stopSettings, () => monitor.Read(clock.Elapsed.TotalSeconds, true));
            events = policy.Observe(latest, stopSettings);
            CaptureEvents();
            activity += events.Activity;
            removed += events.Completed;
            progressed |= events.WorkProgress;
        }

        private void CaptureEvents()
        {
            var observed = policy.Findings.ToDictionary(item => item.Id, item => item.Signature);
            foreach (var pair in observed)
                if (!windowFindings.TryGetValue(pair.Key, out string signature) || signature != pair.Value)
                    changedInWindow.Add(pair.Key);
            foreach (string resolved in windowFindings.Keys.Except(observed.Keys)) changedInWindow.Add(resolved);
            windowFindings = observed;
            foreach (var item in events.Items)
            {
                string key = item["code"] + ":" + (item.TryGetValue("findingId", out var id) ? id
                    : item.TryGetValue("targetId", out var target) ? target : "");
                windowEvents[key] = item;
            }
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
                if (sameGame && (reason == "window_complete" || reason == "event")) Observe(refreshFood: true);
                // An enabled event in the final paused sample takes precedence over the deadline.
                reason = ContinueResponse.FinalReason(reason, events.Stops);
                if (!sameGame) latest.Available = false;
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
            if (!paused) reason = "pause_not_confirmed";
            var findings = ColonyObservation.Findings(latest);
            var current = findings.ToDictionary(item => item.Id, item => item.Signature);
            var delta = ColonyObservation.Delta(lastReported, current);
            var result = new JObject
            {
                ["schemaVersion"] = 3,
                ["stopReason"] = reason == "window_complete" ? "duration_elapsed" : reason,
                ["isPaused"] = paused, ["requestedSeconds"] = requestedSeconds,
                ["responseMode"] = fullResponse ? "full" : "summary",
                ["ignoredEventCount"] = stopSettings.Ignored.Length,
                ["elapsedSeconds"] = Math.Round(clock.Elapsed.TotalSeconds - started, 2),
                ["gameSecondsAdvanced"] = Math.Round(latest.GameSeconds - start.GameSeconds, 2),
                ["observation"] = ContinueResponse.Observation(latest, findings, fullResponse, includeMetadata),
                ["changes"] = new JObject
                {
                    ["added"] = JArray.FromObject(delta["added"]), ["resolved"] = JArray.FromObject(delta["resolved"]),
                    ["changed"] = JArray.FromObject(delta["changed"]),
                    ["foodDeltaKcal"] = Math.Round(latest.FoodKcal - start.FoodKcal),
                    ["ordersRemoved"] = removed, ["activityChanges"] = activity, ["workProgressObserved"] = progressed
                },
                ["events"] = ContinueResponse.Events(windowEvents.Values, fullResponse, lastReported, current, changedInWindow)
            };
            if (fullResponse || includeMetadata) result["ignoredEvents"] = JArray.FromObject(stopSettings.Ignored);
            lastReported = current;
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
