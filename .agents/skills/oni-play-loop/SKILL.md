---
name: oni-mcp-play-loop
description: Autonomous ONI play through MCP. Batch useful work while paused, then repeat bounded continue calls with brief safety checks.
---

# ONI play loop

Use the shared [control contract](../oni-gameplay/SKILL.md) and cached capabilities. Read the world-editor reference only when editing.

## Plan while paused

Use the previous continue observation or one compact snapshot. Inspect only facts needed for the objective. Apply modular design across the colony and plan enough useful, reachable work for available workers over several windows. Account for material budgets, access, prerequisites, research and life support together. Respect meals/rest; do not invent chores to raise the busy count.

Preview hazardous or exact edits, review material/access/side-effect evidence, execute in a fresh call and verify. Planned orders are not completed construction. Pending access infrastructure is not currently usable. Re-read between dependent stages; batch independent actions.

## Fast rounds

Call directly: `game_control domain=speed action=continue seconds=15`. Use 10–20 real seconds for stable batches, 3–6 for an inspected delicate operation. Optional speed=1..3; otherwise preserve speed. No batch/program/task wrapper.

The call advances until its deadline or an enabled event, then returns paused with stopReason, observation, changes and events. duration_elapsed means the full window finished; event may occur before any time advances. The caller decides the next action. A healthy round needs only another continue: briefly check vitals, findings and useful progress, without another pause/snapshot/map/discovery call or rewritten plan. Stay paused and investigate only concrete concerns, depleted work or a planning milestone.

Schema3 summary retains current findings/vitals/progress; full adds coverage and repeated metadata. An enabled event in the final paused sample takes precedence over the deadline. events describes conditions detected during the window, including transient ones; observation.findings describes current conditions; changes is the net added/resolved/changed difference since the previous returned result. These are not competing issue lists or server recommendations. No call waits for a subsequent planning decision.

## Stop settings and evidence

Read settings with speed/stop_events. Explicit ignoreEvents/unignoreEvents accept default event codes or exact finding IDs; they persist for this MCP session across windows, resolution and save loads. Ignoring only changes interruption: findings remain visible. Summary omits unchanged ignored event repetitions. Never ignore urgent safety to maintain pace or silently acknowledge a condition after inspecting it.

Keep routine building warnings on demand through the shared query interface. Colony HUD alerts, native diagnostic warnings and essential safety remain in continue. Native cached diagnostics have unknown age; do not invent freshness. Covered HUD notifications use their specific finding for stopping. Informational power trends differ from overloads.

Activity changes and removed orders do not prove completed work; use workProgressObserved, research progress and independent checks at milestones. registrationPending blueprints need a later paused verification before retries. Food totals include potentially remote loose food; storage does not prove fetchability. Rest/personal needs do not consume productive-work stall timers. No routine atmosphere scans: inspect local gases only for dupe symptoms or a concrete plan.

Vitally important coverage is all live duplicants; food/work is selected-world. Ordinary continue does not prove navigation, fetchability or full utility networks. powerNetworkPending is unknown connection state, not a proven outage. Capability discovery advertises the actual default stop list and coverage.

On external_pause the player has control: wait for an explicit handoff. On transport errors or unconfirmed pause, establish state with one targeted pause/read; never blindly replay time advancement. Leave paused when scope ends. Stream health/comments take priority during an established livestream.
