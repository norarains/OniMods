---
name: oni-mcp-testing
description: 当用户要求测试、验证或审计 ONI MCP 服务器功能、连接状态、工具可用性，或评估 MCP 操作体验和易用性时使用。指导 agent 执行系统化的功能测试流程，覆盖连接、查询、地图、建造、相机、订单等核心能力，并生成测试报告。
---

# ONI MCP testing

Use the shared [control contract](../oni-gameplay/SKILL.md) and [capability discovery](../oni-gameplay/references/capabilities.md). Cache discovery for the running server.

For gameplay audits, select probes from actual work rather than running every feature. Start paused and favor reads and supported dry-runs. Use the [full checklist](references/full-checklist.md) only for broad conformance requests.

1. Verify one initialize/tools-list exchange and one compact safety snapshot. On WSL with Windows ONI, use the repository `scripts/oni_mcp_bridge.py --check`; see `docs/wsl-mcp.md`. Do not repeat failing localhost requests or widen the listener.
2. Test the path needed for the next gameplay action: semantic discovery, one local map, material/placement preflight, then a small authorized action and an independent read. Default zoom has one view; request extra overlays only when relevant.
3. Distinguish outer batch schema validation from child game validation. Set `dryRun=true` on each supported child and execute the outer batch normally. Pause/resume lack dry-run and are direct controls. Report management `validationLevel=syntax_only` honestly.
4. Inspect summary safety fields before relying on them. Full batch JSON is in `results[].result`; plain text uses `text`. Do not parse duplicate content or assume `isError=false` means the requested game work completed.
5. Log a minimal reproducer, expected/actual result, severity, runtime/build identity, elapsed time and response size. Separate observed failures from source-based suspicions and untested proposals. Keep raw evidence local and redact secrets.

For pacing changes, test the advertised direct continue path: healthy windows use one call, include fresh safety/work evidence, and return paused; danger and interruptions stop early; rest is not mistaken for worker starvation; timeout does not require a client pause. Compare response size and calls per window. Test guards with host fixtures rather than creating dangerous live scenarios.

For token/iteration audits, check repeated reads, unnecessary overlays, POI-heavy indexes, repeated material reports, discovery detours, and stale symbol rules. Trust authoritative current map legends; look up only unknown or ambiguous glyphs.

Leave the colony paused. Report passed checks, confirmed bugs, ergonomics costs, and tests still requiring a game restart. Do not restart or mutate merely to complete a checklist outside the user's scope.
