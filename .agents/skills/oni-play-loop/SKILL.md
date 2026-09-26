---
name: oni-mcp-play-loop
description: 当用户要求 agent 通过 MCP 循环游玩 Oxygen Not Included、自动玩一段时间、继续殖民地，或运行暂停-规划-恢复循环时使用。强制执行严格的 pause → observe → plan → execute → resume briefly → pause → verify 循环，仅在能力声明支持时读取玩家规划标记，限制运行窗口，并在风险或歧义决策前停下等待用户确认。
---

# ONI play loop

Use the shared [control contract](../oni-gameplay/SKILL.md) and cached [capabilities](../oni-gameplay/references/capabilities.md). Read the world-editor reference only when editing. This file adds loop timing and stop conditions.

1. Pause (`game_control domain=speed action=pause`). Read, plan, and issue commands only while paused.
2. Read one safety snapshot: `colony_control domain=snapshot action=get profile=minimal` through the advertised batch route, or `world_editor command=read path=/active/index.md includeState=true syncView=false`. Do not routinely fetch both. Inspect edit marks only when `capabilities.editMarks=true`.
3. Choose a small, authorized action. Inspect exact cells for liquid-adjacent work, navigation hazards, or irreversible edits. Expand only the flagged domain. Use `dupes_control domain=info action=status_check radius=4 includeReachableSamples=false` for suspected health/navigation issues; ordinary idle status alone does not require a map scan.
4. Run the child's actual game validator when supported. Outer batch `dryRun=true` checks routing/schema only. To validate placement/order state, execute a normal batch whose child arguments contain `dryRun=true`; never preview and commit the same operation in one dependent batch.
5. Review `valid`, `actionable`, `reasonCode`, material requirements, hazards, and `validationLevel`. A management `syntax_only` preview is not semantic validation. Research target previews are semantic. Commit with a fresh `dryRun=false confirm=true` call only after a successful relevant preview. Pause/resume and other controls without dry-run support are invoked directly within the user's authorized scope.
6. Resume only to advance verified work: 8–15 real seconds normally, 3–6 for emergencies, at most 20 for long hauling. Do not add commands during the window. Arrange the pause before resuming (a local try/finally client is useful); do not assume a server-side advance action exists.
7. Pause immediately, read one compact safety snapshot, and inspect the changed work area only when needed. Verify actual work progress independently of write success. Repeat only within the requested play scope, and leave the game paused at the end.

For independent validator calls:

```json
{"domain":"batch","action":"call_many","responseMode":"summary","requireAllValid":true,"stopOnError":true,"calls":[{"tool":"building_control","args":{"domain":"planning","action":"build_area","areaId":"<observed area>","prefabId":"Ladder","dryRun":true}}]}
```

For exact map/order edits, follow [the virtual-file protocol](../oni-gameplay/references/world-editor.md). Preview with the outer world-editor `dryRun=true`; commit in a fresh edit with `dryRun=false confirm=true`. Ordinary aggregates do not accept raw coordinates. Never use attack for terrain work.

Stop and diagnose on critical health, food, oxygen, heat or power alerts; repeated blocked work; or uncertainty that affects safety. Seek a decision only for consequential choices outside the user's authorization. Never add duplicants without explicit permission or use sandbox/debug resources. During an established livestream, service stream health and comments first; missing unrelated credentials do not block ordinary gameplay.

Report progress, any blocker, and whether the game is paused. Use the user's language and keep routine loop updates brief.
