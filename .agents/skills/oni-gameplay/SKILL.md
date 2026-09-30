---
name: oni-mcp-control
description: Control a live ONI colony through MCP with paused planning, safe edits and targeted verification. Not for advice-only questions.
---

# ONI control

Read [capabilities](references/capabilities.md) once per connection. For autonomous play use [the play loop](../oni-play-loop/SKILL.md). Load [world-editor protocol](references/world-editor.md) before virtual-file edits or off-screen framing; load [tool reference](references/tool-reference.md) only for unfamiliar operations.

## Control contract

Keep paused while observing, planning and issuing orders. Use fresh targeted evidence, plan a useful bounded batch, execute, then verify independently. A returned continue observation is the fresh post-simulation read; do not duplicate it. Respect user scope, reserved areas and manual control. No cheats or debug spawning; keep instantBuild=false for construction. Never set allowSandbox=true without explicit user authorization. Printing Pod rewards default to care packages; adding duplicants requires explicit permission. During an established livestream, stream health and comments precede gameplay.

Printing Pod uses `building_control domain=side_surface surface=facility kind=printing_pod`. Lists are passive. If offers are unmaterialized, preview then confirm headless `prepare_choices` to generate the normal native round without rerolling existing offers; no popup, screenshot or mouse input is needed. With recruitment authorized and food/oxygen/housing ready, use `list_candidates`, select the exact returned `candidateId`, preview `recruit candidateId=… dryRun=true`, then confirm the same candidate. Apply the user's population limit via optional `maxPopulation`; the API has no default cap. Stale/unready offers need a fresh list and preview. Verify exact `duplicant` and `arrivalStatus`; `pending` resolves passively in Printing Pod `status`. Acceptance never implies roster registration. Care packages retain `list_rewards/claim`.

## Read facts

Use `server_control domain=query action=select query=...` for selective colony facts. Discover datasets with `action=schema`, then request only the needed `dataset` schema. Example: `SELECT id, position, statusIds FROM buildings WHERE id = 123 LIMIT 1`.

Select only needed fields; filter native IDs with `statusIds CONTAINS 'Flooded'`, requesting `statuses` only for localized detail. Routine individual-building warnings belong to the on-demand `building_warnings` dataset or `/active/buildings/warnings.md`; intended interactions need no repair. Continue retains colony alerts and essential safety facts. Use specialized diagnostics for navigation, full power circuits, terrain or research rather than assuming an ordinary object query proves them. Unknown/null is not zero or false; loose/stored mass does not prove fetchability. Queries return facts, never recommended actions. Reads obey player discovery: fog is unknown, buried objects stay hidden until native discovery, and IDs or visibleOnly=false cannot bypass this; explored off-screen areas remain readable. Static definitions do not prove an object exists in the save.

## Colony architecture

Use modular design throughout the colony. Give each system a responsibility, boundary and explicit interfaces. Account for dependencies, inputs, outputs, capacity and operating conditions; contain unintended effects and failures. Support inspection, maintenance, replacement and expansion. Stage retrofits so essential services remain available and verify both each module and sustained integration. This applies to every colony system; examples do not limit its scope.

## Build and order discipline

Before liquid-adjacent digs, trapped-dupe work or irreversible edits, inspect exact cells, falling material and access. Budget the complete batch, including unique utility cells and prerequisites. Resolve native prefab IDs through queries; use planning/materials and placement previews for actual validation. Read actual ports and circuit capacity before routing; automatic power connection is opt-in. Pending ladders are not current access, and valid placement does not guarantee work completion.

Use virtual map patches or typed operation files for exact coordinates; ordinary aggregates use IDs, semantic queries or area handles. Preview with `dryRun=true`, inspect errors/materials/side effects, then commit in a fresh call with `confirm=true`. Batch independent operations only. Outer batch dryRun validates routing; child dryRun invokes game validators. Inspect partial/failed counts and independently verify intended effects. Dupes use job preferences 0–5; building/order priorities use 1–9. Sweep pickupables; mop liquids. Cut only the intended utility layer. Configuration thresholds use native units (temperature K); explicit unit=C/F/K/display converts input.

## Authoritative glyphs

Use the current map legend and cached runtime mappings. Do not guess. Resolve only unknown symbols with `world_editor command=symbols queries=[...] direction=auto`; explicit directions are `code_to_meaning` and `meaning_to_code`. `count=0` remains unknown. Invalidate on restart/reconnect, schema change, or overlay change.

## Shared player UI

Ordinary construction, orders and configuration are passive. Use `syncView=false focusCamera=false` and explicit bounds/worldId. The player can inspect buildings, pan and switch overlays. Hand over UI for screenshots and pending capture frames, explicit camera/panel/hotkey actions, optional Printing Pod `open_immigrants` and buttons reporting UI effects. Typed Printing Pod preparation/selection needs no UI; synchronize shared choice mutations with the player. Delivery needs destinationId, never a mouse picker. Coordinate world/save changes, manual edits and speed. On external_pause, stay paused until the user hands control back. Structured maps are authoritative for exact placement.

## Recovery

Never replay an uncertain advance or write. Re-read the smallest affected state, resolve stale IDs/coordinates and regenerate the preview. After schema errors rediscover only the relevant operation. Keep paused if monitoring or pause confirmation fails. Record session bugs in /tmp, not repository history. Leave paused when authorized play ends.
