# Capability discovery and compact reads

Call `server_control domain=catalog action=manifest detail=brief` once per connection/restart. Cache `capabilities`; rediscover after a missing-tool or schema error. Do not probe unsupported names repeatedly.

- `publicTools`: the callable MCP tools. The default surface is building_control, game_control, navigation_control, orders_control, benchmark, server_control, world_editor.
- `batchOperations`: internal aggregates supported through `server_control domain=batch action=call_many`. They are not direct tools. In older builds without this field, prefer virtual-file reads and do not assume a hidden operation is batch-callable.
- `boundedContinue`: prefer direct `game_control domain=speed action=continue seconds=15` for authorized autonomous play. Schema 3 returns paused with `stopReason`, `observation.findings`, net changes and detected events. The caller decides next actions. `ignoreEvents`/`unignoreEvents` change persistent per-session stop filters without hiding findings; `speed/stop_events` reads or updates them. Follow the [two-speed play loop](../../oni-play-loop/SKILL.md); a healthy round needs no extra read.
- `editMarks`: call `game_control domain=ui uiDomain=edit_mark action=list` only when true. Otherwise skip that step; do not retry it or require the user to create a mark.
- Use the current map legend first; see the shared glyph policy in `../SKILL.md`. Query only unknown or ambiguous symbols.
- Ordinary reads omit tutorials. Request `includeHelp=true` once for usage, or `responseMode=full` for complete edit diagnostics. Edit `result`/`error` payloads are JSON objects, not JSON-encoded strings.

For a supported internal operation, translate each example into a batch item:

```json
{"domain":"batch","action":"call_many","responseMode":"summary","calls":[{"tool":"colony_control","args":{"domain":"snapshot","action":"get","profile":"minimal"}},{"tool":"dupes_control","args":{"domain":"info","action":"status_check","radius":4,"includeReachableSamples":false}}]}
```

Supply the required task description on the outer call. Use `responseMode=full` for targeted details: structured child JSON is at `results[].result`, plain text at `text`. Compact snapshots retain safety metrics and watch results. Query catalog search and inspect `operations[].call` for internal operation recipes. Keep safety/confirmation fields on write items. Batch `dryRun=true` validates routing/schema only; to run game validators without mutation set `dryRun=true` on each child and execute the batch normally.

Fallback reads without internal operations:

```text
world_editor command=read path=/active/index.md includeState=true
world_editor command=read path=/active/dupes/reachability.md radius=4 sampleLimit=0
world_editor command=read path=/active/map/viewport.md
```

Map-edit preflight now invokes game placement/order validators with mutation disabled. It checks current state; it does not reserve materials or simulate earlier pending construction. Re-read after execution, especially for dependent patches.
