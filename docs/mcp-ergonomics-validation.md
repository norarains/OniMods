# MCP ergonomics fixes — validation

Changes address repeated read tutorials, escaped nested edit results, map preflight that previously stopped at parsing, phantom building overlap near the Printing Pod, stale tool discovery, ignored storage selectors, and priorities following coordinate annotations.

## Implemented behavior

- State/map/cell reads omit repeated help by default. `includeHelp=true` requests usage. `responseMode=full` preserves edit diagnostic detail; nested `result` and `error` values become JSON objects in both response modes. Compact results retain failures, obstructions, material evidence and partial mutation counts.
- Map edits call the same placement/order validators as execution with `dryRun=true`, `confirm=false`, and partial validation disabled before applying a patch. Preview does not reserve materials or simulate pending construction.
- Building overlap checks use native grid registration instead of reconstructing an even-width footprint from a shifted anchor. Existing layer and bridge-endpoint guards remain.
- Storage lists apply `id`, `query`/`name`, world and resource selectors before limiting results. Natural orders preserve `:priority` after `@(x,y)`.
- The manifest reports public tools, available internal batch operations, and edit-mark support. Internal colony/dupe/read/search operations use validated server batches; the coordinate gateway remains excluded. Batch children inherit the outer task when none is supplied in defaults or child arguments.
- Gameplay skills use cached capability discovery and skip unavailable edit marks.

## Offline evidence

- `dotnet run --project tests/OniMcp.Tools.Tests`: 44 ergonomics assertions and 97 existing assertions passed. Tests compile production response, parsing, storage filter, occupancy, batch and preflight-dispatch code with substituted game/registry boundaries.
- A synthetic nested error shrank from 50,680 to 159 characters while retaining the error bit, obstruction, material and partial-write evidence. This is a fixture measurement, not a live-game token benchmark.
- Source contracts passed: build placement stomp safety, bridge endpoint stomp safety, world-editor safety, sandbox policy, logic-gate reads, instant-build/infrastructure plans, editable building files, and the seven-tool public documentation contract.
- All ten ONI skill metadata validations passed.
- `onim -m OniMcp build` passed against installed game assemblies with zero warnings/errors. `onim dev` rebuilds and installs the same working-tree changes.
- Changed-file whitespace validation passed. Whole-tree `git diff --check` reports existing trailing whitespace in root README and scratch scripts exposed by pre-existing line-ending changes; those files were left alone.

## Runtime test scope

The colony was loaded through Steam and kept paused. The live checks below exercised read-only calls and dry-runs; actual blueprint placement and order execution were not tested.

1. Run the bundled runtime smoke suite and verify the seven public tools plus the manifest batch routes. Confirm internal dupe/status reads work through a batch and `editMarks=false` prevents obsolete calls.
2. Compare compact index, map and cell reads to `includeHelp=true`; ensure compact reads remain usable and full guidance is reachable.
3. List a storage by exact ID, prefab query and localized name; verify conflicting selectors return no unrelated buildings.
4. Read the Printing Pod's occupied cells and `(286,71)`; dry-run a single-cell build beside its registered edge. A truly occupied cell must still be rejected; an empty adjacent cell must not report a phantom Pod overlap.
5. Submit map dry-runs for an occupied footprint, unavailable material and missing support. Check actual reasons, native JSON and zero mutations. Compare valid preview with execution only after reviewing the target cells.
6. Preview `挖 @(297,73):6` through the orders operation file and inspect compiled priority. A live designation/priority readback test needs an inspected safe cell and cleanup afterward.
7. Re-read state and orders to verify dry-run tests changed no game state and the game remains paused.

## First live verification

The installed build was reachable through `http://127.0.0.1:8788/mcp/`; `localhost` returned HTTP 400 (Invalid host) in this environment. The seven-tool smoke suite, modern protocol smoke, and internal state batch calls passed. Save 流星 was loaded at cycle 2, 0.8%, active world 1, paused throughout.

Independent tester checks passed for exact-ID and English/Chinese storage filters, truly occupied versus adjacent empty Printing Pod cells, missing floor support, invalid material and valid material with zero stock. Map previews returned `applied=0` and `committed=false`. The natural order preview preserved priority 6; the existing designation remained priority 5 because no order was executed.

The live review found additional metadata/help issues. Follow-up changes derive cell bounds from native registrations, recover world IDs in early placement errors, include entity/order glyphs, replace obsolete coordinate-gateway guidance, and remove identical diagnostic copies while retaining unique evidence. These changes were checked after a second ONI restart, as recorded below.

Host regression now passes 44 ergonomics checks plus 97 existing checks. Replaying four captured live failure payloads adds 28 evidence-preservation checks, for 169 total. Captured response sizes after further compaction:

| Case | Before | After |
|---|---:|---:|
| Occupied cell | 7003 chars | 1723 chars |
| Missing support | 4801 chars | 1395 chars |
| Invalid material | 3997 chars | 1865 chars |
| Zero-stock material | 4162 chars | 1891 chars |

These are character counts using compact JSON serialization, not tokenizer estimates. Distinct reason codes, blocker lists, support cells, commitment/applied counts and resource amounts were preserved by assertions.

## Final live verification

The follow-up build installed with zero build warnings/errors; the deployed DLL matches SHA256 `7102554e5cc91e87f6022991317a5c226b6df17c1be66e3762fb478784e423d8`. After restarting ONI, the runtime smoke suite passed again: seven public tools, loaded colony, modern protocol checks and internal state reads through the server batch route.

- Printing Pod cell metadata now reports `bottomLeft=(282,71)` and `topRight=(285,74)`, matching registered map occupancy.
- Authoritative symbol lookup resolves `人`, `物` and `挖` to duplicant, critter and dig-command meanings.
- The occupied-cell preview fails with the actual Headquarters blocker; the adjacent `(286,71)` preview passes. Both retain zero applied changes and `committed=false`.
- Missing support, invalid material and valid material with zero stock fail with their specific reasons and `anchor.worldId=1`.
- The final occupied-cell summary is 1,759 characters, including the updated next-step guidance. Its nested payloads are JSON objects and duplicate preview details are omitted.
- Public building/order descriptions and order-file help point to supported world-editor routes instead of recommending the hidden coordinate gateway.

The colony remained paused at cycle 2, 0.8%. No live orders were issued. A broad order-priority listing attempted through the batch endpoint was rejected by its aggregate confirmation guard before execution; verification of the priority fix is limited to production parser tests and the earlier natural-command preview.
