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
assert plan.index('if (!GetBool(placementCheck, "valid"))') < plan.index('var placedPowerAutoConnect')
monitor = (ROOT / 'Impl/Core/GameContinueMonitor.cs').read_text()
assert monitor.count('(worldId < 0 || item.GetMyWorldId() == worldId)') == 2
assert 'wallSeconds >= nextInfrastructureRead' in monitor
snapshot = (ROOT / 'Impl/World/SnapshotTools.cs').read_text()
assert 'FoodSnapshot food = !compactObservation' in snapshot
assert 'BuildingSnapshot buildings = !compactObservation' in snapshot
print('PASS preview mutation guards, shared observation wiring, native footprint verification (source contracts)')
