---
name: oni-mcp-autonomous-iteration
description: Use when iterating on the ONI MCP server so an agent can build with onim, deploy with onim dev, automatically launch/load a save, run batched runtime tests with a tester agent, and improve tool ergonomics toward long autonomous play.
---

# ONI MCP Autonomous Iteration

## Capability gate

Read [capability discovery](../oni-gameplay/references/capabilities.md) before using tool examples. Only call names in `capabilities.publicTools` directly. Examples naming `colony_control`, `dupes_control`, `read_control`, or `search_control` are internal operations: use the documented batch route only when listed in `capabilities.batchOperations`. Skip edit-mark reads unless `capabilities.editMarks=true`. Cache discovery for the session.


Use this workflow for end-to-end ONI MCP server improvement, especially when the goal is to make test agents control Oxygen Not Included with minimal manual steering.

## Operating Loop

1. Inspect current state first.
   - Run `git status --short`.
   - Check whether ONI is already running and whether `http://localhost:8788/mcp/` responds.
   - Read the current MCP catalog with `server_control domain=catalog action=manifest` or a targeted catalog search before assuming schemas.

2. Batch implementation before restarting ONI.
   - Prefer one implementation batch, then one `onim build`, one `onim dev`, one ONI restart, and one runtime test batch.
   - Do not restart for every small edit unless a stale DLL blocks testing.
   - Keep new files under 500 lines.
   - Use meaningful file names such as `GameLaunchTools.cs`, not numbered split files.

3. Build and deploy exactly.
   - Run `onim build`.
   - Run `git diff --check`.
   - Run `onim dev`.
   - Treat the deployed Dev mod DLL as stale until ONI is restarted or the runtime catalog proves the new schema loaded.

4. Launch/load the game through MCP.
   - Prefer `game_control domain=launch action=status limit=5`.
   - First call `game_control domain=launch action=start dryRun=true confirm=true index=0 resume=false`.
   - If dryRun is clean, call `game_control domain=launch action=start confirm=true index=0 resume=false`.
   - Verify with `colony_control domain=snapshot action=get profile=minimal`.
   - Keep the game paused during tests unless the test explicitly requires time passing.
   - For a full process restart on Linux, call `game_control domain=launch action=restart_load dryRun=true resume=false`, then repeat with `confirm=true`. The accepted response contains `jobId` and the exact saved path; after Steam relaunch, query `game_control domain=launch action=restart_status jobId=<id>` until `stage=loaded` or `stage=failed`.
   - `restart_load` is intentionally asynchronous across processes. Its relay carries only the old PID and the locally resolved absolute Steam executable, then launches Steam AppID 457140; it never carries the save path or MCP token.

5. Optimize both planning and routine execution.
   - Follow [the two-speed play loop](../oni-play-loop/SKILL.md). Plan enough useful work for available workers, then use the advertised bounded continue operation.
   - A healthy fast round must take one direct continue call, with no extra pause/snapshot/map call or repeated planning. The agent decides whether the returned facts require planning; there is no server recommendation or implicit review horizon.
   - Global snapshot green/watch.alert=false alone is not an individual-health guarantee. Continue includes dupe vitals and work evidence; hazardous plans still need targeted preflight.
   - Test early stops, final pause, interrupted clients, failed monitoring, rest vs unexpected idle, and actual progress vs activity. Keep session evidence in /tmp; maintain reusable contracts in existing docs.

6. Run tester-agent feedback after deployment.
   - Give the tester only the task and current MCP endpoint assumptions, not your expected fixes.
   - Ask for a compact report: passed checks, blocking bugs, ergonomics problems, and highest-value next fixes.
   - Prefer read-only and `dryRun=true` tests first.
   - Include these areas when relevant: catalog schema, launch/status, snapshot, world search, sequence search, planning parse, build dryRun, orders dryRun metadata, power/wire auto-connect, and area handles.

7. Iterate from tester findings.
   - Fix issues that reduce one-call usability or agent clarity first.
   - Rebuild, redeploy, and rerun only the affected runtime checks.
   - Leave unrelated refactors for later unless they block autonomy.
   - Do not add lines to already oversized files unless the same batch also makes a meaningful semantic split.

## Runtime Checks

Prefer the bundled smoke script after MCP endpoint is online:

```bash
python .agents/skills/oni-mcp-autonomous-iteration/scripts/runtime_smoke.py
```

It verifies JSON-RPC initialize, the exact seven-tool default public surface (or the authenticated full surface), launch status, planning parse, and `world_editor` active-world lifecycle behavior. With a loaded colony it runs snapshot and world-sequence reads directly on the full surface or through public `server_control` batching on the default surface. At the main menu it requires a structured `game_not_loaded` active-file response and only preflights the hidden read calls. It never places orders.

For a prepared work batch, the helper uses the same bounded operation through the WSL-aware bridge:

```bash
python .agents/skills/oni-mcp-autonomous-iteration/scripts/survival_watch.py --target-cycles 1 --max-seconds 60 --poll-seconds 15 --speed 3
```

This transport test helper stops on any enabled event, error or time/cycle budget. It does not plan, modify ignore settings, or acknowledge findings. During gameplay use native MCP calls so the agent can inspect every returned observation. A 100-cycle goal is not permission for a blind 100-cycle resume.

Use these checks as a minimum smoke suite after a launch-related or planning-related change:

```text
server_control domain=diagnostics action=status detail=brief
game_control domain=launch action=status limit=5
game_control domain=launch action=start dryRun=true confirm=true index=0 resume=false
colony_control domain=snapshot action=get profile=minimal
building_control domain=planning action=parse_plan plan="粉砂岩砖@氧气" worldId=0
building_control domain=planning action=parse_plan plan="用粉砂岩建造砖块，锚点氧气" worldId=0
building_control domain=planning action=parse_plan plan="Build two sandstone tiles near the base" worldId=0
read_control domain=world action=search pattern="粉砂岩-泥土-氧气" direction=both matchMode=smart worldId=0 limit=3
building_control domain=planning action=build_area plan="粉砂岩砖@氧气" worldId=0 dryRun=true limit=3
server_control domain=batch action=call_many dryRun=true responseMode=summary calls=[...]
```

Expected planning behavior:

- `粉砂岩砖@氧气` resolves to `prefabId=Tile`, `material=SiltStone`, `query=氧气`.
- `Build two sandstone tiles near the base` resolves to `prefabId=Tile`, `material=SandStone`, `query=base`.
- Natural anchor forms such as `锚点氧气`, `靠近电池`, `目标厕所`, and `@氧气` should produce an anchor query.
- A dryRun failure is acceptable when it reports the true game reason, such as unavailable material, unreachable target, obstruction, or missing support.

## ONI Process Handling

Use the bundled Steam-only launcher helper:

```bash
bash .agents/skills/oni-mcp-autonomous-iteration/scripts/launch_oni_mcp.sh
```

If a running ONI PID already has a healthy MCP endpoint, it returns immediately without touching `unity.lock` or Steam. Otherwise it clears stale `unity.lock`, requires the Steam client, and launches fixed AppID 457140 through the Steam URI only; the AppID is not configurable. If MCP is offline while old ONI PIDs still exist, it waits for the full old PID set to exit before sending the URI; after the request, success requires a live PID not present before launch plus a healthy `http://localhost:8788/mcp/`. It prints compact diagnostics on failure and never falls back to launching the ONI binary directly. Useful overrides:

```bash
ONI_MCP_WAIT_SECONDS=360 bash .agents/skills/oni-mcp-autonomous-iteration/scripts/launch_oni_mcp.sh
ONI_OLD_PROCESS_EXIT_WAIT_SECONDS=60 bash .agents/skills/oni-mcp-autonomous-iteration/scripts/launch_oni_mcp.sh
```

Use Steam launch only:

```bash
pidof OxygenNotIncluded | xargs -r kill
rm -f ~/.local/share/Steam/steamapps/common/OxygenNotIncluded/unity.lock
steam steam://run/457140 >/tmp/oni-steam-launch.log 2>&1 &
```

Wait for both the process and MCP endpoint:

```bash
pidof OxygenNotIncluded
curl -fsS --max-time 2 http://localhost:8788/mcp/
```

Avoid `pgrep -f OxygenNotIncluded` in kill commands because it can match the shell command itself.

## Tester Report Format

Ask the tester to return:

```markdown
## Result
- Overall: pass/fail
- Loaded save: yes/no
- Paused after launch: yes/no

## Passed
- ...

## Bugs
- Severity, tool call, observed result, expected result

## Ergonomics
- Places where the agent still needed coordinates, repeated calls, or hidden knowledge

## Next Fixes
- Ordered list of fixes by autonomy impact
```

## Long-Run Goal

Optimize toward a tester agent surviving 100 cycles with low manual steering:

- Prefer semantic search and reusable area handles over raw coordinates.
- Every action dryRun should explain reachability, material availability, risk, and next action.
- Every write/action path that creates dupe work must include priority handling. Set priority directly when supported, or return compact `priorityAction`/`nextActions` using existing order/priority endpoints. Critical survival work such as access ladders, food, oxygen, toilets, reachable material digs, prerequisite sweeps, and rescue paths should default high priority, usually 7, unless user specified another value.
- Tool schemas should expose all supported parameters at the aggregate entrypoint.
- One-call search/action paths should return compact metadata that lets the next agent decide without reading large maps.
- Printing pod rewards must use existing `building_control domain=side_surface surface=facility kind=printing_pod`, never a new public tool. Use `action=list_rewards`, then `action=claim rewardIndex=N dryRun=true`, then `confirm=true` only when explicitly consuming the reward. After claiming survival-critical material/food/oxygen, immediately plan or return sweep/storage/build priority follow-up so the reward actually helps the colony.
