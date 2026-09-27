using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static Dictionary<string, object> TryPlanOne(string prefabId, int x, int y, JObject args, HashSet<int> plannedSupportCells = null, AutoDigContext autoDigContext = null)
        {
            string resolvedPrefabId;
            string resolveError;
            var def = ResolveBuildingDef(prefabId, out resolvedPrefabId, out resolveError);
            if (def == null)
                return ErrorResult(prefabId, x, y, resolveError);
prefabId = resolvedPrefabId;

string availabilityError = BuildAvailabilityError(def, args);
if (availabilityError != null)
return ErrorResult(prefabId, x, y, availabilityError, new Dictionary<string, object>
{
["prefabId"] = prefabId,
["unlocked"] = IsTechUnlocked(def),
["availableNow"] = IsUnlockedAndAvailable(def)
});

int cell = Grid.XYToCell(x, y);
            if (!Grid.IsValidBuildingCell(cell) || !Grid.IsVisible(cell))
                return ErrorResult(prefabId, x, y, "Invalid or not visible cell");

            int worldId = ToolUtil.ResolveWorldId(args);
            if (!ToolUtil.CellMatchesWorld(cell, worldId))
                return ErrorResult(prefabId, x, y, $"Cell is not in worldId={worldId}");

            var orientation = ParseOrientation(args["orientation"]?.ToString());
            var earlyPlacement = BuildPlacementDetails(def, x, y, worldId, orientation);
            var earlyExistingBuild = ExistingMatchingBuildAtPlacement(def, earlyPlacement);
            if (earlyExistingBuild != null)
            {
                var instantRetry = TryCompleteExistingVirtualFileBlueprint(def, earlyPlacement, args, earlyExistingBuild);
                if (instantRetry != null)
                    return instantRetry;
                RegisterSupportBlueprint(prefabId, x, y, plannedSupportCells);
                return new Dictionary<string, object>
                {
                    ["planned"] = false,
                    ["blueprintPlaced"] = false,
                    ["alreadyPresent"] = true,
                    ["alreadyBlueprint"] = string.Equals(earlyExistingBuild["kind"]?.ToString(), "blueprint", StringComparison.OrdinalIgnoreCase),
                    ["alreadyBuilding"] = string.Equals(earlyExistingBuild["kind"]?.ToString(), "building", StringComparison.OrdinalIgnoreCase),
                    ["valid"] = true,
                    ["prefabId"] = prefabId,
                    ["name"] = ToolUtil.CleanName(def.Name),
                    ["x"] = x,
                    ["y"] = y,
                    ["anchor"] = AnchorDictionary(x, y, worldId),
                    ["worldId"] = worldId,
                    ["placement"] = earlyPlacement.ToDictionary(),
                    ["footprint"] = earlyPlacement.Footprint.Select(cellInfo => cellInfo.ToDictionary()).ToList(),
                    ["existing"] = earlyExistingBuild
                };
            }
            var materialResult = SelectElements(def, args["material"]?.ToString(), worldId);
            materialResult.RequiredKg = RequiredMaterialKg(def);
            if (!materialResult.Valid)
                return ErrorResult(prefabId, x, y, materialResult.Error, materialResult.ToDictionary());
            if (!IsFreeBuildContext() && materialResult.Elements.Count == 1 && materialResult.Selected != null
                && materialResult.RequiredKg > materialResult.Selected.AvailableKg)
                return ErrorResult(prefabId, x, y, "Insufficient selected material mass", new Dictionary<string, object>
                {
                    ["reasonCode"] = "insufficient_material",
                    ["materialSelection"] = materialResult.ToDictionary()
                });

            var facadeResult = ResolveFacade(def, args["facade"]?.ToString() ?? args["facadeId"]?.ToString());
            if (!facadeResult.Valid)
                return ErrorResult(prefabId, x, y, facadeResult.Error);

            var supportResult = ValidateSupport(def, x, y, ToolUtil.GetBool(args, "allowUnsupported", false), plannedSupportCells, orientation);
            if (!supportResult.Valid)
                return ErrorResult(prefabId, x, y, supportResult.Error, supportResult.ToDictionary());

            var placement = BuildPlacementDetails(def, x, y, worldId, orientation);
            var replacementTarget = NativeTileReplacement(def, placement);
            var replacement = TileReplacementInfo(replacementTarget);
            var workAccess = CurrentWorkAccess(placement);
            var footprintResult = ValidateFootprint(placement);
            var existingBuild = ExistingMatchingBuildAtPlacement(def, placement);
            if (existingBuild != null)
            {
                var instantRetry = TryCompleteExistingVirtualFileBlueprint(def, placement, args, existingBuild);
                if (instantRetry != null)
                    return instantRetry;
                RegisterSupportBlueprint(prefabId, x, y, plannedSupportCells);
                return new Dictionary<string, object>
                {
                    ["planned"] = false,
                    ["blueprintPlaced"] = false,
                    ["alreadyPresent"] = true,
                    ["alreadyBlueprint"] = string.Equals(existingBuild["kind"]?.ToString(), "blueprint", StringComparison.OrdinalIgnoreCase),
                    ["alreadyBuilding"] = string.Equals(existingBuild["kind"]?.ToString(), "building", StringComparison.OrdinalIgnoreCase),
                    ["valid"] = true,
                    ["prefabId"] = prefabId,
                    ["name"] = ToolUtil.CleanName(def.Name),
                    ["x"] = x,
                    ["y"] = y,
                    ["anchor"] = AnchorDictionary(x, y, worldId),
                    ["worldId"] = worldId,
                    ["placement"] = placement.ToDictionary(),
                    ["footprint"] = placement.Footprint.Select(cellInfo => cellInfo.ToDictionary()).ToList(),
                    ["existing"] = existingBuild,
                    ["support"] = supportResult.ToDictionary(),
                    ["material"] = materialResult.Elements.Select(tag => tag.Name).ToList(),
                    ["materialSelection"] = materialResult.ToDictionary(),
                    ["materials"] = materialResult.ToDictionary(),
                    ["facade"] = facadeResult.ResponseId
                };
            }
            Dictionary<string, object> autoDig = null;
            if (!footprintResult.Valid)
            {
                var details = footprintResult.ToDictionary(placement);
                autoDig = TryAutoDigObstructions(placement, footprintResult, args, autoDigContext);
                if (autoDig == null)
                    return ErrorResult(prefabId, x, y, footprintResult.Error, details);
                details["autoDig"] = autoDig;
                if (!GetBool(autoDig, "available"))
                    return ErrorResult(prefabId, x, y, footprintResult.Error, details);
            }

            var existingUtility = ExistingMatchingUtilityAtPlacement(def, placement);
            if (existingUtility != null)
            {
                RegisterSupportBlueprint(prefabId, x, y, plannedSupportCells);
                return ExistingPlacementResult(def, placement, existingUtility, utility: true);
            }

            if (IsDryRun(args))
            {
                RegisterSupportBlueprint(prefabId, x, y, plannedSupportCells);
                var powerAutoConnect = TryAutoConnectPower(def, x, y, orientation, args, plannedSupportCells, autoDigContext);
                return PowerConnectionReceipt(new Dictionary<string, object>
                {
                    ["planned"] = false,
                    ["blueprintPlaced"] = false,
                    ["actualAnchor"] = null,
                    ["valid"] = true,
                    ["dryRun"] = true,
                    ["prefabId"] = prefabId,
                    ["name"] = ToolUtil.CleanName(def.Name),
                    ["x"] = x,
                    ["y"] = y,
                    ["anchor"] = AnchorDictionary(x, y, worldId),
                    ["worldId"] = worldId,
                    ["placement"] = placement.ToDictionary(),
                    ["footprint"] = placement.Footprint.Select(cellInfo => cellInfo.ToDictionary()).ToList(),
                    ["support"] = supportResult.ToDictionary(),
                    ["material"] = materialResult.Elements.Select(tag => tag.Name).ToList(),
                    ["materialSelection"] = materialResult.ToDictionary(),
                    ["materials"] = materialResult.ToDictionary(),
                    ["facade"] = facadeResult.ResponseId,
                    ["powerAutoConnect"] = powerAutoConnect,
                    ["autoDig"] = autoDig,
                    ["replacement"] = replacement, ["workAccess"] = workAccess,
                    ["actionable"] = GetBool(workAccess, "allCellsHaveCurrentAccess")
                }, powerAutoConnect, false);
            }

            var executionExistingBuild = ExistingMatchingBuildAtPlacement(def, placement);
            if (executionExistingBuild != null)
            {
                var instantRetry = TryCompleteExistingVirtualFileBlueprint(def, placement, args, executionExistingBuild);
                if (instantRetry != null)
                    return instantRetry;
                return ExistingPlacementResult(def, placement, executionExistingBuild, utility: false);
            }

            var executionExistingUtility = ExistingMatchingUtilityAtPlacement(def, placement);
            if (executionExistingUtility != null)
                return ExistingPlacementResult(def, placement, executionExistingUtility, utility: true);

            var executionFootprintResult = ValidateFootprint(placement);
            if (HasUnsafeExecutionConflict(executionFootprintResult))
            {
                var executionDetails = executionFootprintResult.ToDictionary(placement);
                executionDetails["safety"] = "execution_pre_place_recheck";
                executionDetails["reasonCode"] = IsLinearUtilityPrefab(prefabId)
                    ? "utility_path_conflict"
                    : "placement_conflict";
                return ErrorResult(prefabId, x, y,
                    "Placement safety changed after preflight; refusing to stomp an existing building, endpoint, connector, or utility object",
                    executionDetails);
            }

            Dictionary<string, object> instantCompletion = null;
            bool completedImmediately = IsAuthorizedVirtualFileInstantBuild(args);
            int originCell = PlacementOriginCell(def, x, y, orientation);
            GameObject go;
            if (completedImmediately && replacementTarget != null)
                return ErrorResult(prefabId, x, y, "Tile replacement uses ordinary construction only.");
            if (completedImmediately)
            {
                if (!TryBuildVirtualFileInstantBuild(def, placement, args, originCell, orientation,
                    materialResult.Elements, facadeResult.ResponseId, out go, out instantCompletion))
                    return InstantCompletionFailureResult(def, placement, null, instantCompletion, placedByThisRequest: false);
            }
            else
            {
                var pos = BuildPlacementPosition(cell, def, orientation);
                go = replacementTarget != null
                    ? def.TryReplaceTile(null, pos, orientation, materialResult.Elements, facadeResult.TryPlaceId)
                    : def.TryPlace(null, pos, orientation, materialResult.Elements, facadeResult.TryPlaceId);
                if (go == null)
                {
                    var failureDetails = BuildPlacementFailureDetails(placement, materialResult);
                    if (autoDig != null)
                        failureDetails["autoDig"] = autoDig;
                    return ErrorResult(prefabId, x, y, "Placement failed", failureDetails);
                }
                SetPriority(go, ToolUtil.GetInt(args, "priority") ?? 5);
            }
            RegisterSupportBlueprint(prefabId, x, y, plannedSupportCells);
            var actualPlacement = ActualPlacementDetails(go, def, x, y);
            var placementCheck = ComparePlacement(placement, actualPlacement);
            if (!GetBool(placementCheck, "valid") && !GetBool(placementCheck, "pending"))
                return ErrorResult(prefabId, x, y, "Native placement does not match the requested footprint; inspect before retrying.",
                    new Dictionary<string, object> { ["id"] = go.GetComponent<KPrefabID>()?.InstanceID ?? -1, ["safeToRetry"] = false,
                        ["mutationAttempted"] = true, ["blueprintPlaced"] = !completedImmediately,
                        ["buildingCompleted"] = completedImmediately, ["actualPlacement"] = actualPlacement, ["placementCheck"] = placementCheck });
            var placedPowerAutoConnect = TryAutoConnectPower(def, x, y, orientation, args, plannedSupportCells, autoDigContext);
            return PowerConnectionReceipt(new Dictionary<string, object>
            {
                ["planned"] = true,
                ["blueprintPlaced"] = !completedImmediately,
                ["buildingCompleted"] = completedImmediately,
                ["valid"] = true,
                ["prefabId"] = prefabId,
                ["name"] = ToolUtil.CleanName(def.Name),
                ["x"] = x,
                ["y"] = y,
                ["anchor"] = AnchorDictionary(x, y, worldId),
                ["worldId"] = worldId,
                ["placement"] = placement.ToDictionary(),
                ["footprint"] = placement.Footprint.Select(cellInfo => cellInfo.ToDictionary()).ToList(),
                ["actualPlacement"] = actualPlacement,
                ["actualAnchor"] = ActualAnchorArray(actualPlacement),
                ["placementCheck"] = placementCheck,
                ["support"] = supportResult.ToDictionary(),
                ["material"] = materialResult.Elements.Select(tag => tag.Name).ToList(),
                ["materialSelection"] = materialResult.ToDictionary(),
                ["materials"] = materialResult.ToDictionary(),
                ["facade"] = facadeResult.ResponseId,
                ["powerAutoConnect"] = placedPowerAutoConnect,
                ["autoDig"] = autoDig,
                ["replacement"] = replacement, ["workAccess"] = workAccess,
                ["actionable"] = GetBool(workAccess, "allCellsHaveCurrentAccess"),
                ["instantCompletion"] = instantCompletion,
                ["id"] = go.GetComponent<KPrefabID>()?.InstanceID ?? -1
            }, placedPowerAutoConnect, true);
        }
    }
}
