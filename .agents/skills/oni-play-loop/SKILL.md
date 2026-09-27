---
name: oni-mcp-play-loop
description: Use for autonomous ONI play or continuing a colony through MCP. Plan useful batches while paused, then repeat bounded continue calls with compact safety/progress checks; return to careful planning only for concrete triggers.
---

# ONI play loop

Use the shared [control contract](../oni-gameplay/SKILL.md) and cached [capabilities](../oni-gameplay/references/capabilities.md). Read the world-editor reference only when editing.

## Planning round

1. Pause. Use one compact safety snapshot or the previous `continue` result; do not fetch both routinely. Inspect only the domains needed for the next objective. Read edit marks only when `capabilities.editMarks=true`.
2. Plan a coherent batch that supplies available working duplicants with useful, reachable work for several run windows. Consider material budgets, access, prerequisites, priorities, research and recurring life-support work together. Respect meals, rest and personal needs; do not create pointless errands to increase a busy count. Avoid repeated one-tile planning when the whole safe section is already understood.
3. Inspect exact cells for liquid-adjacent work, navigation hazards or irreversible edits. Run supported semantic validators, review material/hazard/partial-failure evidence, and commit in a fresh call. Outer batch `dryRun=true` validates schema/routing only; run normal batches with child `dryRun=true` for actual game validation. Do not preview and commit dependent edits in the same batch.
4. Verify orders/settings from returned postconditions; use one targeted read only when the response lacks that evidence. Planned orders are not completed construction. Independent writes may be batched; dependent stages require updated evidence. Then start the fast loop. Every window refreshes tracked orders automatically.

## Fast round

When cached capabilities advertise `boundedContinue`, call directly:

```text
game_control domain=speed action=continue seconds=15 task="Advance planned work and check safety"
```

`speed=1..3` is optional; otherwise the selected speed is preserved. Windows are 1–20 real seconds; use 10–20 for stable work and 3–6 for an inspected delicate operation. Call directly, without batch/program/task wrappers.

Schema version 3 returns facts: `stopReason`, `isPaused`, elapsed time, `observation`, `changes`, `events`, and `ignoredEvents`. There is no recommended action. `duration_elapsed` means the requested window finished; `event` means an enabled event stopped it, including a condition already present before time advanced. Other reasons identify interruptions or failures. Each call completes with the game paused; no protocol operation waits for a follow-up decision.

- If the returned facts show healthy, useful work, immediately repeat continue within the user's scope. Make a short sanity check of findings, vitals and progress. No extra pause, snapshot, map, dupe scan, discovery or rewritten plan for a routine window.
- If facts require attention, stay paused and plan carefully. Investigate only the implicated domain, refill useful work, or handle research/Printing Pod/skill choices. Ordinary idle status does not justify a terrain scan.
- If pause is unconfirmed, monitoring is unavailable, or a transport error occurs, establish state with one targeted read/pause. Never replay an uncertain advance automatically.

The default stop-event list is advertised in `capabilities.boundedContinue.defaultStopEvents`. To inspect or change the caller's persistent settings without advancing:

```json
{"domain":"speed","action":"stop_events","ignoreEvents":["printing_pod_ready"],"task":"Keep the pending Printing Pod choice visible without interrupting this work"}
```

Use `unignoreEvents:["printing_pod_ready"]` to restore its stop. Both arrays also work on `continue`, accept event codes or exact finding IDs, and persist for the current MCP session across windows, finding resolution/reappearance, and save loads. A new MCP session starts with defaults. Ignoring affects interruption only: the condition remains in `observation.findings` and detected ignored events carry `ignored:true`. Do not ignore urgent safety conditions merely to maintain pace. Do not implicitly acknowledge findings after a read or planning round.

`observation.findings` is the shared current-condition list used by snapshots and diagnostics. `changes.added/resolved/changed` is the net difference from the previous returned continue observation; these sets are disjoint. The first result compares with an empty baseline. `events` records detected event types during this window, including transient conditions absent at the end. Neither is a second current-issues list.

`activityChanges` records movement/chore transitions; `ordersRemoved` includes completed or cancelled orders. Neither proves construction finished. Use `workProgressObserved`, research changes and targeted verification at meaningful milestones. A blueprint with `registrationPending=true` exists but its footprint has not yet been verified; inspect again on a subsequent paused read after native OnSpawn registration before retrying placement.

The server samples vitals every 0.5 real seconds, food/research infrastructure at most two seconds old, and pauses at the deadline even if the client disappears. It has no implicit 300-second review stop. The agent owns planning pace. Coverage includes all-dupe vitals, selected-world food/work, HUD alerts, infrastructure counts, Printing Pod, skill points and active research-station state. Read `coverage.notChecked`; station storage/connection facts do not establish material fetchability or safe routes.

For an older server without `boundedContinue`, use the fallback: arrange a pause in a local try/finally before resuming for 8–15 real seconds, pause, then one compact snapshot. Do not assume continue exists or emulate waiting inside a synchronous server program.

Leave the game paused when the requested play scope ends. Report milestones, blockers and final pause state briefly. Do not add duplicants without explicit permission or use cheats. During an established livestream, service stream health/comments before continuing; unrelated credentials do not block gameplay.
