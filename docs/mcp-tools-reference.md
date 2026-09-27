# ONI MCP 工具参考

本文档描述 `OniMcp` 当前推荐的工具面。运行时清单永远以 `server_control domain=catalog action=manifest` 和 `oni://tools/manifest` 为准。

## 快速开始

- MCP 地址: `http://localhost:8788/mcp/`
- 协议兼容: `2026-07-28` 无会话兼容路径 + `2025-11-25` / `2025-06-18` legacy initialize/session 路径
- Legacy/default public tools: 7 entrypoints: `benchmark`, `world_editor`, `game_control`, `navigation_control`, `building_control`, `orders_control`, `server_control`
- Modern `2026-07-28` `tools/list`: 当前只广告只读 `benchmark`
- 旧聚合入口: 作为虚拟文件和 server_control batch 工作流的内部操作，不直接注册为 MCP 工具
- `coordinate_control` 不属于当前公开运行时；普通聚合工具拒绝 raw coordinates
- Tool descriptions: default-public tool descriptions and parameter descriptions are in English

现代 `2026-07-28` 请求不使用 `initialize`，也不要求或返回 `Mcp-Session-Id`。每个请求携带 `_meta`、`MCP-Protocol-Version` 和 `Mcp-Method`；`resources/read` / `tools/call` 等有具体资源或工具名的请求还需匹配的 `Mcp-Name`。推荐先调用 `server/discover` 并以实际 capability 为准。

Legacy `2025-11-25` / `2025-06-18` 客户端继续先调用 `initialize`，随后请求携带协商得到的 `Mcp-Session-Id` 和 `Mcp-Protocol-Version`。

## 时间推进与返回信息

`game_control domain=speed action=continue seconds=15` 推进指定的实际时间窗口，遇到启用的停止事件则提前暂停。返回 `stopReason`、`events` 和统一的 `observation.findings`；是否继续、规划或操作由调用方决定。正常工作窗口只需下一次 `continue`，无需重复读取快照。

- 常驻 findings 包括复制人生命体征、食物、研究、建筑/建造缺料、打印舱、技能点，以及原生 HUD 的 Bad、BadMinor、Tutorial、DuplicantThreatening 通知和缓存的诊断警告。ONI 的黄色警告也会使用 Tutorial 类型。
- 氧气生成不足附带原生日报的上一周期产量、消耗量和净值（kg）。这不代表局部空气可呼吸度；常驻监测不扫描房间气体，复制人状态异常时再定向查询。
- `ignoreEvents` / `unignoreEvents` 接受事件类别或具体 finding ID。忽略只改变是否提前停止，不隐藏当前问题。
- 仅补货不足的状态标记为 `refill_only`，仍返回但不触发停机；实际空研究站仍会停止，并保留同一个研究 finding ID。新出现的空管道/空手压泵状态需由相隔至少两秒模拟时间的两次新鲜原生读取确认才触发停止（重复读取同一缓存不能确认），确认期间返回 `pending_status_confirmation` 与 `stopEligible=false`，避免加载后的暂态造成零时间循环。

`continue` 默认 `responseMode=summary`，保留全部当前 findings、生命体征、工作进展及有关复制人的士气/压力变化来源。首个窗口和设置变更返回完整监测范围与忽略列表；后续使用 `coverage.profile=colony_v1`、`coverage.available` 和 `ignoredEventCount`，不重复健康研究站或未变化的已忽略 finding 事件。`responseMode=full` 保留完整元数据及所有事件；瞬态、变化及未忽略事件在 summary 中仍返回。`colony_v1` 范围是所有存活复制人的生命体征、当前世界食物/工作/原生缺料状态和原生 HUD；不验证局部气体、导航、资源可取性或完整管网。结束采样同时发现启用事件时，`stopReason=event` 优先于 `duration_elapsed`；故障原因优先于事件。

HUD 缺料通知的每个原生目标都已被具体 finding 覆盖时，仅具体 finding 负责停止，HUD 仍显示 `coveredBy`；未知或未覆盖目标继续停止。打印舱通知同样使用 `printing_pod_ready`。缓存的 `IdleDiagnostic` 不绕过 `worker_idle` 的十秒模拟时间宽限。功耗变化提示是非停止的信息；电路过载仍停止。诊断返回 `cached=true`、原生更新间隔四秒模拟时间，实际缓存年龄不可得时为 null。农作诊断附带原生目标与当前植物状态；种植/可收获列表也提供状态，不扫描区域空气。

建造预检区分 `valid`（放置条件）和 `workAccess`（当前复制人导航到邻近工作格的证据）。`actionable=false` 表示有尚未显示可达的格子；不能把 `supportValidated` 当作可完工证明。待建梯子不会被假定已经提供通路，技能、配送及未来完工可达性未验证。单格地砖可走原生替换蓝图流程，保留旧地砖直到建成，不先拆除。地图拆除明确只针对建筑层并按原生对象去重。

生产站 `id` 在排序/截断前精确过滤，`includeRecipes` 只返回配方摘要；配方材料细节使用 `list_recipes`。用户菜单批次的优先级按子项 > defaults > 外层 priority 继承。目录中按内部聚合操作的完整名称查询时，仅返回该操作的 schema 与 batch 调用路径。解析计划的 `executionPlan.oneCall` 使用 `world_editor` 对 `/active/buildings/plans.oni` 做无副作用预检；世界图案查询走 batch 内部读取。搜索只返回匹配结果，不再生成不完整的动作、未声明的 targetRef 或被禁用的直接建造调用。

内部聚合操作可通过 `server_control domain=batch action=call_many` 的 `calls` 使用。`responseMode=summary` 保留种植、收获、过滤器和队列等写入后状态，不重复输出同一份 text；完整读取使用 `responseMode=full`。

生产队列、储存过滤器、用户菜单按钮、学习技能和智能电池阈值支持无副作用的 `dryRun`。生产批次先校验所有配方再修改队列。虚拟操作文件的预检返回 `validationLevel`：`native_preflight` 表示已调用支持预检的原生处理路径，`routing_and_syntax_only` 仅说明路由与语法通过。

智能电池使用 `building_control domain=config action=set_battery_thresholds id=… lowThreshold=20 highThreshold=80 dryRun=true`；提交使用 `confirm=true`。配置读取及建筑实例文件均显示阈值、电量；实例文件可编辑 `Battery.LowThreshold` 和 `Battery.HighThreshold`。电力摘要中的 `generatorStoredEnergyEmpty` 指发电机内部储能为空，不能据此判断燃料是否耗尽。

## 定位与执行原则

Authoritative model:

- Saves are directories. `latest/` is the fixed alias for the current/latest save.
- `cd latest` enters a save; `cd` or `cd ~` exits back to `/`, representing the main menu/root.
- Save contents are structured world files such as `map/terrain.oni`, `buildings/plans.oni`, `infrastructure/power.oni`, and `views/power.png`.
- There are no action patch files. World changes use `world_editor command=edit`; prefer one SEARCH/REPLACE block. Multiple blocks require outer `allowPartial=true` and cannot be transactionally rolled back.
- Reading the same file again is the observation step after an edit.

Example edit:

```text
`<<<<<<< SEARCH`
# observed or empty planning text
`=======`
用铜矿连接电池到制氧机
`>>>>>>> REPLACE`
```

`world_editor` is the default world interaction tool. It treats the loaded save as
a virtual folder and exposes map views as files:

- `world_editor command=ls path=/`
- `world_editor command=read path=/world/map/text.txt`
- `world_editor command=read path=/world/views/power.png`
- `world_editor command=search domain=buildings query="wire"`
- `world_editor command=read path=/active/buildings/plans.oni`
- `world_editor command=read path=/active/infrastructure/power.oni`

Search, planning, actions, building, orders, navigation, game actions, dupe
actions, and coordinate fallback are routed through `world_editor`. The default
public surface also keeps `benchmark`, `game_control`, `navigation_control`,
`building_control`, `orders_control`, and `server_control` available for direct
focused calls. Other former aggregate entrypoints are internal-only virtual-file
operations and are not callable as MCP tools.

新工具面按搜索/动作优先设计:

- 优先使用 `query`、`target`、`search`、`name`、`id`、`areaId`。
- For dedicated search, use `world_editor command=search` or the typed `/active/ops/search.md` workflow. The underlying `search_control` operation is internal and is not a direct MCP tool.
- Public aggregate tools do not accept raw `x/y`, `x1/y1/x2/y2`, `dx/dy`, `points`, or `anchors`. For exact orders, read `/active/ops/tools.md` and edit `/active/ops/orders.md`; use only currently public typed files/tools and ignore hidden `coordinate_control` and `/active/ops/coordinate.md` compatibility entries.
- 面向任务的返回应尽量包含 `reachable`、`executable`、失败原因、缺失条件和建议下一步。
- 需要定义区域并取得 `areaId` 时，通过 `world_editor` 的 typed `/active/ops/read.md` 工作流完成；底层 `read_control` 是 internal operation，不应作为 MCP 工具直接调用。
- 写入、执行和危险动作应支持 `dryRun` 或 `confirm`，并在执行后重新读取状态验证。
- 危险或大范围精确操作必须保持 pause -> read/plan -> dry-run -> confirm -> verify。

## 核心工具

| 工具 | 主要 domain/action | 风险 | 用途 |
|------|--------------------|------|------|
| `benchmark` | `cases`, `iterations`, `tool`, `includeDetails` | read | 固定工具链路基准与诊断，不修改游戏状态 |
| `world_editor` | `cd`, `ls`, `read`, `search`, `edit`, `plan`, `connect` | read/write/execute | 虚拟文件化世界访问、搜索、规划与受控编辑 |
| `server_control` | `catalog`, `batch`, `program` | read/execute | 健康检查、工具清单、工具搜索、目标指南、批量调用、agent program |
| `game_control` | `speed`, `state`, `save`, `sandbox`, `ui` | read/execute/dangerous | 暂停、恢复、调速、存档、沙盒、UI 编辑标记 |
| `navigation_control` | `camera` 或按已知相机 `action` 推断 | execute | 相机移动、世界切换、覆盖层、聚焦和截图 |
| `building_control` | `planning`, `config`, `storage`, `filter`, `production`, `side_surface`, `rocket` | read/write/execute | 建造规划、材料检查、蓝图、建筑侧屏配置、储存过滤、生产队列、火箭 |
| `orders_control` | `area`, `priority`, `designation`, `conduit` | execute/dangerous | 挖掘、清扫、拖地、拆除、优先级、区域订单、线路/管线剪断 |

## 建造规划

`building_control domain=planning` 是新的建造入口。

| action | 用途 |
|--------|------|
| `search_defs` | 搜索可建建筑定义 |
| `materials` | 查询建筑可用材料和库存 |
| `preview` | 预检一个蓝图锚点，返回可执行性和材料需求 |
| `placement_candidates` | 在区域或目标附近搜索可放置位置 |
| `build_area` | 放置蓝图，线性 utility 支持自动连接 |
| `auto_connect` | 兼容旧流程的显式连接入口，新流程通常不需要单独调用 |

### 材料返回

蓝图预检和放置结果应返回材料可行性。旧字段 `materialSelection` 仍保留兼容，新字段 `materials` 用于任务级判断:

```json
{
  "materials": {
    "requirementKnown": true,
    "requiredKg": 200.0,
    "selectedAvailableKg": 1240.0,
    "satisfied": true,
    "shortageKg": 0.0,
    "availableMaterials": [
      { "tag": "SandStone", "availableKg": 1240.0 }
    ]
  }
}
```

调用方应优先检查 `materials.satisfied`。如果为 `false`，向用户返回需要材料、可用材料和缺口，而不是继续下达蓝图。

### 线路和管路

`build_area` 对线性设施支持自动连接:

- `Wire`
- `LogicWire`
- `GasConduit`
- `LiquidConduit`
- `SolidConduit`

For semantic building, prefer `plan`, `blueprint`, `areaId`, search results, or semantic calls in `/active/ops/build.md`. For exact placement, read `/active/map/viewport.md` (zoom or read `symbols/glyphs.md` when needed), then edit the map markdown by replacing target empty-cell tokens with `建筑名:优先级` and optional `#材料字`. The map route translates these tokens to underlying `building_control build_area` anchors; `/active/ops/build.md` does not accept raw coordinates.

示例:

Prefer one SEARCH/REPLACE block. Multiple blocks require outer `allowPartial=true` and cannot be transactionally rolled back. Each operation-file replacement must contain exactly one executable command. Preview with outer `world_editor edit` `dryRun=true` and `confirm=false` (or omitted); execute with a new edit using outer `dryRun=false`, `confirm=true`, and non-conflicting command flags, then re-read the map or state.

This directly creates a continuous line, with no separate follow-up connection step required.

## 订单与剪断

`orders_control` supports semantic targets and area handles. For an exact rectangle, read `/active/ops/tools.md`, ignore hidden coordinate compatibility entries, then edit `/active/ops/orders.md`, for example `挖 x1=10 y1=20 x2=18 y2=20 priority=7 dryRun=true`. Preview with outer `dryRun=true` and no confirmation; execute only with a new edit using outer `dryRun=false`, `confirm=true`, and non-conflicting command flags.

```json
{
  "domain": "area",
  "action": "dig",
  "areaId": "a1",
  "confirm": true
}
```

剪断管线和线路时:

- `cut_conduits type=auto` 默认包含气管、液管、固体轨道、电线和逻辑线。
- `type=wire` 只剪电线。
- `type=logic` 只剪逻辑线。
- `type=all` 适合明确需要更大范围时使用。

## 相机与视图

`navigation_control` 仅用于相机、覆盖层和截图。支持的动作包括：

- `get_view`：读取当前相机位置、缩放和激活世界。
- `set_active_world`：切换激活世界并移动相机。
- `set_view` / `move`：设置或平移相机。
- `switch_view`：切换氧气、电力、管线、温度等覆盖层，可选截图。
- `focus_cell` / `focus_dupe`：聚焦格子或复制人。
- `screenshot` / `coordinate_screenshot`：保存普通截图或带坐标网格的区域截图。

建造和任务操作应直接使用 `building_control` 与 `orders_control`。每次工具调用必填的 `task` 文本会自动显示在玩家鼠标附近，无需额外定位流程。

## 常用资源

| URI | 说明 |
|-----|------|
| `oni://colony/status` | 周期、复制人数、速度、暂停状态 |
| `oni://colony/diagnostics` | 缺氧、断粮、过热等诊断 |
| `oni://colony/alerts` | 当前警报和通知 |
| `oni://colony/summary` | 面向行动规划的殖民地摘要 |
| `oni://resources/inventory` | 资源库存 |
| `oni://resources/food` | 食物库存和保质信息 |
| `oni://dupes` | 复制人列表 |
| `oni://dupes/status-check` | 复制人位置、差事、需求和疑似被困风险 |
| `oni://power/summary` | 电网摘要和电池状态 |
| `oni://power/ports` | 电力接口格、锚点、接线点和端口是否已有电线 |
| `oni://rooms/list` | 房间系统状态 |
| `oni://thermal/overheat-risk` | 建筑过热风险 |
| `oni://world/text-map` | 文本地图 |
| `oni://buildings/defs` | 可建造建筑定义 |
| `oni://tools/manifest` | 工具清单 |
| `oni://tools/guide` | 按目标推荐工具链 |
| `oni://guide/mechanics` | 机制、公式、边界条件速查 |

## 代码目录

```text
mods/OniMcp/
├── ModInfo.cs           # KMod 入口
├── Config/              # 选项
├── Core/                # MCP 协议类型
├── Localization/        # STRINGS
├── Patches/             # Harmony 补丁与游戏策略
├── UI/                  # 运行时 Overlay（坐标网格、对话气泡）
├── Server/              # HTTP/MCP 服务
├── Support/             # 日志、路径、反射
└── Tools/
    ├── Core/            # 工具与资源注册
    ├── Entry/           # 聚合入口（*Control* / Read / 英文描述）
    ├── WorldEditor/     # 虚拟世界文件系统
    ├── Shared/          # 共享辅助
    └── Impl/            # 各域实现（Build/Dupes/World/...）
```

Prefer extending aggregate entrypoints in `Tools/Entry/` for new public capabilities. Domain implementations live under `Tools/Impl/<Domain>/`. Shared search, reachability, and material checks belong in `Tools/Shared/`; exact spatial operations are routed through typed files under `/active/ops/`.

## 兼容性说明

新客户端建议按双协议处理：

1. 优先以 `2026-07-28` 调用 `server/discover`，使用返回的 capability；现代请求不发送 `Mcp-Session-Id`。
2. 若目标服务端只支持旧协议，再走 `initialize` + `Mcp-Session-Id` 的 `2025-11-25` / `2025-06-18` 路径。
3. 不要硬编码旧版细粒度工具列表；按当前协议调用 `tools/list` 或读取运行时 manifest。
4. 调用 `server_control domain=catalog action=search` 或读取 `oni://tools/guide` 查找目标流程。
5. 优先传语义定位参数。
6. 对危险动作传 `confirm: true`。
7. 执行后读取资源或区域快照验证状态。
