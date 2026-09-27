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
assert claim.index('!Immigration.Instance.ImmigrantsAvailable') < claim.index('CurrentCarePackages()') < claim.index('telepad.OnAcceptDelivery')
current = extract_block(reward, 'private static IEnumerable<CarePackageInfo> CurrentCarePackages')
assert current.index('!Immigration.Instance.ImmigrantsAvailable') < current.index('GetField("containers"')
assert '"priorityAction"' not in reward and '"priorityPlan"' not in reward
assert '"entityKind"' in reward and '"quantity"' in reward
runner = (ROOT / 'Impl/Core/GameContinueRunner.cs').read_text()
configure = extract_block(runner, 'internal static CallToolResult Configure')
assert configure.index('"dryRun"') < configure.index('Settings(args)')
monitor = (ROOT / 'Impl/Core/GameContinueMonitor.cs').read_text()
assert monitor.count('(worldId < 0 || item.GetMyWorldId() == worldId)') == 2
assert 'wallSeconds >= nextInfrastructureRead' in monitor
assert 'BuildingSupplyObservation.Read(building.gameObject, false, buildingSupplies)' in monitor
assert 'BuildingSupplyObservation.Read(building.gameObject, true, buildingSupplies)' in monitor
assert 'sample.BuildingSupplies.AddRange(buildingSupplies)' in monitor
supply = (ROOT / 'Impl/Core/BuildingSupplyObservation.cs').read_text()
assert 'GetStatusItemGroup()' in supply and 'BuildingSupplyFinding.IsShortage(status)' in supply
assert 'GetRemainingMinimum()' in supply and 'VisibleCellAllowed(cell, true)' in supply
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
detail = extract_block((ROOT / 'Impl/Dupes/DuplicantInfoTools.cs').read_text(), 'private static List<Dictionary<string, object>> CompactAttributes')
assert detail.index('EssentialAttributeIds.Contains(attr.Id)') < detail.index('.Take(24)')
print('PASS preview mutation guards, shared observation wiring, native footprint verification (source contracts)')
