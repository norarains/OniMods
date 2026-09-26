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
4. Verify orders/settings from returned postconditions; use one targeted read only when the response lacks that evidence. Planned orders are not completed construction. Independent writes may be batched; dependent stages require updated evidence. Then start the fast loop with `resetMonitor=true`.

## Fast round

When cached capabilities advertise `boundedContinue`, call directly:

```text
game_control domain=speed action=continue seconds=15 task="Advance planned work and check safety"
```

Use `resetMonitor=true` only on the first call after a planning/review round. `speed=1..3` is optional; otherwise the selected game speed is preserved. Windows are 1–20 real seconds; use 10–20 for stable work and 3–6 for a carefully inspected delicate operation. The server samples safety during the window, stops early when needed, and returns paused. Do not wrap continue in batch, program, or protocol tasks.

For schemaVersion=2, read `recommendedAction`, `isPaused`, `endedBy`, `triggers`, `observation.findings` and `changes`. `endedBy` says what ended the run (`window_complete`, `attention_required`, `preflight`, or interruption); `recommendedAction` is the server’s advice for your next step. The call has finished and the game is paused; it is not waiting for another MCP action. Older schemaVersion=1 used `decision`/`reasons`/`stopReason`.

- **continue + isPaused=true:** immediately repeat continue within the user's play scope. This is a short sanity check, not a new planning round. No separate pause, sleep, snapshot, map, dupe scan, discovery, rewritten plan or per-window commentary. The response already contains the fresh observation.
- **review:** stay paused and address the returned reason. Refill exhausted work, investigate persistent work-time idleness/blockage, handle an exhausted research queue or newly actionable Printing Pod/skill choice, or review the objective at `review_due`. Expand only the implicated domain. Ordinary idle status alone does not justify a terrain scan.
- **urgent, isPaused=false, error, or missing monitoring evidence:** stop the fast loop. Confirm/pause if needed, inspect the specific risk and plan carefully. Never blindly repeat an uncertain advance; a transport error may mean the window already ran.

`activityChanges` records movement/chore transitions; `ordersRemoved` can include completed or cancelled orders. Neither proves a requested building finished. Use `workProgressObserved`, queue/research changes and targeted verification at milestones. A stable colony-wide food/stress reading does not replace individual safety checks. `resetMonitor=true` acknowledges only nonurgent findings already reported by this monitoring session. They remain in `observation.findings` until resolved; changed severity/actionability and newly arrived findings interrupt again. A skill blocker waiting for experience should remain visible while useful work continues. Urgent conditions cannot be waived.

The server requests a review after 300 simulation seconds and pauses at the deadline even if the client disappears. After a timeout, reconnect and read/pause once to establish state; never replay automatically. The monitor covers dupe vitals, threatening/new bad HUD alerts, local food, infrastructure counts, Printing Pod readiness, skill points, missing advanced-research skill, tracked build/dig work and research. It does not prove every route safe or inspect all utilities: retain preflight and targeted checks for hazardous work.

For an older server without `boundedContinue`, use the fallback: arrange a pause in a local try/finally before resuming for 8–15 real seconds, pause, then one compact snapshot. Do not assume continue exists or emulate waiting inside a synchronous server program.

Leave the game paused when the requested play scope ends. Report milestones, blockers and final pause state briefly. Do not add duplicants without explicit permission or use cheats. During an established livestream, service stream health/comments before continuing; unrelated credentials do not block gameplay.
