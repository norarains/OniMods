#!/usr/bin/env python3
"""Source wiring checks supplement host behavior tests; these do not execute Unity."""
from pathlib import Path
from verify_building_blueprint_safety import extract_block

ROOT = Path(__file__).resolve().parents[1] / "mods/OniMcp/Tools"
priority = (ROOT / "Impl/Orders/OrdersPriorityTools.cs").read_text()
for method in ("SetBuildingPriority()", "SetPriorityArea()"):
    body = extract_block(priority, "public static McpTool " + method)
    assert 'bool dryRun = ToolUtil.GetBool(args, "dryRun", false)' in body
    assert 'if (!dryRun) prioritizable.SetMasterPriority(setting);' in body
    assert body.count('SetMasterPriority(') == 1
    assert 'if (!dryRun && !ToolUtil.GetBool(args, "confirm", false))' in body
planting = (ROOT / "Impl/Bio/FarmingPlantingTools.cs").read_text()
body = extract_block(planting, "public static McpTool SetPlanting()")
assert 'if (!dryRun) plot.CancelActiveRequest();' in body
preview = body.index('if (dryRun)\n')
assert body.index('if (!hasDeposit || !validEntity)') < preview < body.index('plot.OrderRemoveOccupant();')
assert 'return CallToolResult.Text' in body[preview:body.index('plot.OrderRemoveOccupant();')]
assert body.index('plot.OrderRemoveOccupant();') < body.index('plot.CreateOrder(seedTag, mutationTag);')
harvest = extract_block((ROOT / "Impl/Bio/FarmingTools.cs").read_text(), "public static McpTool SetHarvestable()")
preview = harvest.index('if (ToolUtil.GetBool(args, "dryRun", false))')
assert harvest.index('HarvestMarkPolicy.ShouldMarkNow') < preview < harvest.index('harvestable.gameObject.Trigger')
assert 'return CallToolResult.Text' in harvest[preview:harvest.index('harvestable.gameObject.Trigger')]
uproot = extract_block((ROOT / "Impl/Bio/FarmingAreaOperations.cs").read_text(), "public static McpTool UprootArea()")
assert 'if (!dryRun && !ToolUtil.GetBool(args, "confirm", false))' in uproot
assert 'if (!dryRun)\n                            {\n                                uprootable.MarkForUproot();' in uproot
assert 'if (!dryRun)\n                            {\n                                uprootable.ForceCancelUproot();' in uproot
for filename in ("Impl/Core/GameContinuePolicy.cs", "Impl/World/SnapshotResearchAlertTools.cs"):
    assert 'ColonyObservation.Findings(sample)' in (ROOT / filename).read_text()
for filename in ("Impl/World/SnapshotTools.cs", "Impl/Colony/DiagnosticsTools.cs"):
    source = (ROOT / filename).read_text()
    assert 'ColonyObservationRuntime.Read(worldId)' in source
    assert 'ColonyObservation.Serialize(' in source
assert 'AddAlert(' not in (ROOT / 'Impl/Colony/DiagnosticsTools.cs').read_text()
assert 'AddAlert(' not in (ROOT / 'Impl/World/SnapshotResearchAlertTools.cs').read_text()
geometry = (ROOT / 'Impl/Build/BuildPlanningPlacementGeometry.cs').read_text()
assert 'def.PlacementOffsets' in geometry and 'GetRotatedCellOffset' in geometry
assert 'RegisteredBuildingOccupancy.Cells' in geometry and '.SetEquals(' in geometry
plan = (ROOT / 'Impl/Build/BuildPlanningPlanOne.cs').read_text()
assert plan.index('if (!GetBool(placementCheck, "valid") && !GetBool(placementCheck, "pending"))') < plan.index('var placedPowerAutoConnect')
assert '"registrationPending"' in geometry and '!go.GetComponent<Constructable>().isSpawned' in geometry
assert '"safeToRetry"] = false' in plan and '"id"] = go.GetComponent<KPrefabID>()' in plan
manual = extract_block((ROOT / 'Impl/Orders/OrdersBuildingTools.cs').read_text(), "public static McpTool ConfigureManualDelivery()")
mutations = extract_block(manual, 'if (!dryRun)')
for mutation in ('delivery.Pause(', 'delivery.capacity =', 'delivery.refillMass =', 'delivery.MinimumMass =', 'delivery.RequestDelivery()', 'delivery.UpdateDeliveryState()'):
    assert manual.count(mutation) == mutations.count(mutation) == 1
reward = (ROOT / 'Impl/Facility/FacilityPrintingPodRewardTools.cs').read_text()
claim = extract_block(reward, 'private static CallToolResult ClaimPrintingReward')
assert claim.index('PrintingPodActionError(telepad)') < claim.index('CurrentCarePackages(telepad)') < claim.index('PrintingPodNativeChoices.Accept(')
current = extract_block(reward, 'private static IEnumerable<CarePackageContainer.CarePackageInstanceData> CurrentCarePackages')
assert current.index('!Immigration.Instance.ImmigrantsAvailable') < current.index('PrintingPodNativeChoices.Containers')
assert 'CarePackageContainer).GetField' not in reward
assert 'EndImmigration()' not in claim
assert claim.index('if (ToolUtil.GetBool(args, "dryRun", false))') < claim.index('PrintingPodNativeChoices.Accept(')
assert '"priorityAction"' not in reward and '"priorityPlan"' not in reward
assert '"entityKind"' in reward and '"quantity"' in reward
runner = (ROOT / 'Impl/Core/GameContinueRunner.cs').read_text()
configure = extract_block(runner, 'internal static CallToolResult Configure')
assert configure.index('"dryRun"') < configure.index('Settings(args)')
monitor = (ROOT / 'Impl/Core/GameContinueMonitor.cs').read_text()
for order_array in ("builds", "digs", "deconstructions"):
    loop = extract_block(monitor, "foreach (var item in " + order_array + ")")
    # The condition lies before the loop body in single-statement foreach syntax.
    start = monitor.index("foreach (var item in " + order_array + ")")
    assert 'ToolUtil.GameObjectMatchesWorld(item.gameObject, worldId)' in monitor[start:start + 250]
assert "!Grid.Solid[dig.Value]" in monitor
assert "sample.PendingDeconstructions++" in monitor
assert 'wallSeconds >= nextInfrastructureRead' in monitor
assert 'BuildingSupplyObservation.Read(building.gameObject, false, buildingSupplies)' in monitor
assert 'BuildingSupplyObservation.Read(building.gameObject, true, buildingSupplies)' in monitor
assert 'sample.BuildingSupplies.AddRange(buildingSupplies)' in monitor
supply = (ROOT / 'Impl/Core/BuildingSupplyObservation.cs').read_text()
assert 'GetStatusItemGroup()' in supply and 'BuildingSupplyFinding.IsShortage(status)' in supply
assert 'GetRemainingMinimum()' in supply and 'PlayerVisibility.Object(go)' in supply
power = (ROOT / 'Impl/Build/BuildPlanningPowerConnect.cs').read_text()
assert 'sourceCell == inputCell && UtilityConnectionRead.HasLine' in power
for field in ('autoDigObstructions', 'autoUprootObstructions'):
    assert f'["{field}"] = false' in power
assert 'result["partial"] = placed' in power and 'result["safeToRetry"] = !placed' in power
ports = (ROOT / 'Impl/Build/InfrastructureConduitPorts.cs').read_text()
assert 'def.InputConduitType == type' in ports and 'def.OutputConduitType == type' in ports
assert 'building.GetUtilityInputCell()' in ports and 'building.GetUtilityOutputCell()' in ports
snapshot = (ROOT / 'Impl/World/SnapshotTools.cs').read_text()
assert 'FoodSnapshot food = !compactObservation' in snapshot
assert 'BuildingSnapshot buildings = !compactObservation' in snapshot
# These are wiring guards, complementary to the executable PlayerVisibility tests.
# Filtering must happen before lazy native fields, matching, counts and pagination.
visibility = (ROOT / 'Shared/PlayerVisibility.cs').read_text()
assert '!uncoverable.IsUncovered' in visibility
assert '!Grid.IsWorldValidCell(cell) || !Grid.IsVisible(cell)' in visibility
assert 'GetWorld(Grid.WorldIdx[cell])?.IsDiscovered ?? false' in visibility
assert 'Camera' not in visibility and 'SelectTool' not in visibility
util = (ROOT / 'Shared/ToolUtil.cs').read_text()
selector = extract_block(util, 'public static bool GameObjectMatchesWorld')
assert selector.index('PlayerVisibility.Object(go)') < selector.index('worldId < 0')
assert 'return PlayerVisibility.Cell(cell);' in extract_block(util, 'public static bool VisibleCellAllowed')
rows = (ROOT / 'Impl/Query/ColonyQueryObjects.cs').read_text()
for marker, guard in [('private static IEnumerable<FactRow> BuildingRows', 'PlayerVisibility.Object(go)'),
                      ('private static IEnumerable<FactRow> ItemRows', 'PlayerVisibility.Object(item.gameObject)')]:
    body = extract_block(rows, marker)
    assert body.index(guard) < body.index('yield return new FactRow')
indexed = (ROOT / 'Impl/Query/ColonyQueryIdentity.cs').read_text()
assert 'PlayerVisibility.Object(identity.gameObject)' in indexed and 'BuildingRows(warnings, objects)' in indexed
cell = (ROOT / 'Impl/World/WorldCellInfoReadTools.cs').read_text()
assert cell.index('!PlayerVisibility.Cell(cell)') < cell.index('Grid.Element[cell]')
cell_md = (ROOT / 'WorldEditor/WorldEditorCellSnapshot.cs').read_text()
assert cell_md.index('!PlayerVisibility.Cell(cell)') < cell_md.index('AppendCellBaseSnapshot(sb, cell)')
layer = (ROOT / 'WorldEditor/WorldEditorLogicGateRead.cs').read_text()
assert layer.count('PlayerVisibility.Known(Grid.Objects') == 3
html_routes = (ROOT / 'WorldEditor/WorldEditorVirtualFileReader.cs').read_text()
assert html_routes.count('RenderDiscoveredHtmlCells(xMin, xMax, yMin, yMax, activeMode)') == 2
assert 'Grid.Element[cell]' not in html_routes and 'Grid.Temperature[cell]' not in html_routes
html_cells = (ROOT / 'WorldEditor/WorldEditorHtmlMap.cs').read_text()
assert html_cells.index('PlayerVisibility.Cell(cell)') < html_cells.index('Grid.Element[cell]')
assert 'CellBuildingObject(cell)' in html_cells and 'Unknown (unrevealed)' in html_cells
navigation = (ROOT / 'Impl/Navigation/CameraTools.cs').read_text()
assert 'GetBool(args, "requireDiscovered"' not in navigation
assert navigation.index('world == null || !world.IsDiscovered') < navigation.index('world.LookAtSurface()')
for relative in ('Impl/World/WorldSearchRequest.cs', 'Impl/World/WorldTextMapReadTools.cs',
                 'Impl/World/WorldAreaSnapshotReadTools.cs', 'Impl/Colony/InventoryTools.cs',
                 'Impl/World/WorldElementSummaryTools.cs', 'Impl/Rocket/RocketTools.cs'):
    source = (ROOT / relative).read_text()
    assert 'GetBool(args, "visibleOnly"' not in source, relative
geysers = (ROOT / 'Impl/Bio/GeoTunerTools.cs').read_text()
assert 'PlayerVisibility.Object(geyser.gameObject)' in geysers
for path in ROOT.rglob('*.cs'):
    if path.name not in ('PlayerVisibility.cs', 'SandboxEntityAndEnvironmentTools.cs'):
        assert 'Grid.IsVisible(' not in path.read_text(), path
risks = (ROOT / 'Impl/Orders/OrdersCellObservations.cs').read_text()
assert risks.index('!PlayerVisibility.Cell(neighbor)') < risks.index('Grid.Element[neighbor]')
assert '"unexplored_neighbor"' in risks
liquids = (ROOT / 'Impl/Orders/OrdersLiquidTools.cs').read_text()
assert liquids.count('!PlayerVisibility.Cell(cell) || !ToolUtil.CellMatchesWorld(cell, worldId)') == 3
assert '!PlayerVisibility.Object(go) || seen.Contains(go)' in liquids
assert 'PlayerVisibility.Cell(Grid.CellBelow(cell))' in liquids
assert plan.index('earlyPlacement.Footprint.Any') < plan.index('ExistingMatchingBuildAtPlacement')
detail = extract_block((ROOT / 'Impl/Dupes/DuplicantInfoTools.cs').read_text(), 'private static List<Dictionary<string, object>> CompactAttributes')
assert detail.index('EssentialAttributeIds.Contains(attr.Id)') < detail.index('.Take(24)')
print('PASS preview mutation guards, observation/discovery wiring, native footprint verification (source contracts)')
