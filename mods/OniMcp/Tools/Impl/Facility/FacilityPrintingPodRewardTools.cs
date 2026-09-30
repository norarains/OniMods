using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class FacilitySideScreenTools
    {
        private static GameObject FindTelepadTarget(JObject args)
        {
            var explicitTarget = FindBuildingTarget(args, target => target.GetComponent<Telepad>() != null);
            if (explicitTarget != null)
                return explicitTarget;

            bool constrained = ToolUtil.GetInt(args, "id").HasValue
                || ToolUtil.GetInt(args, "x").HasValue
                || ToolUtil.GetInt(args, "y").HasValue
                || !string.IsNullOrWhiteSpace(args["query"]?.ToString())
                || !string.IsNullOrWhiteSpace(args["target"]?.ToString())
                || !string.IsNullOrWhiteSpace(args["name"]?.ToString())
                || HasRectInput(args);
            if (constrained)
                return null;

            int worldId = ToolUtil.GetInt(args, "worldId") ?? (ClusterManager.Instance?.activeWorldId ?? -1);
            var telepad = Components.Telepads.Items
                .FirstOrDefault(item => item != null && PlayerVisibility.Object(item.gameObject)
                    && (worldId < 0 || item.gameObject.GetMyWorldId() == worldId));
            return telepad == null ? null : telepad.gameObject;
        }

        private static CallToolResult ClaimPrintingReward(JObject args, Telepad telepad, Dictionary<string, object> before)
        {
            string error = PrintingPodActionError(telepad);
            if (error != null) return CallToolResult.Error(error);
            var rewards = CurrentCarePackages(telepad).ToList();
            if (rewards.Count == 0)
            {
                return CallToolResult.Error("No current care-package choice is prepared. Use prepare_choices with confirm=true; reads and dryRun never generate choices.");
            }

            var selected = ResolvePrintingReward(args, rewards);
            if (selected == null)
                return CallToolResult.Error("No current care-package choice matched. Duplicants use recruit with an explicit candidateId from list_candidates.");

            try { PrintingPodNativeChoices.ValidateAcceptance(telepad, selected); }
            catch (Exception exception) { return CallToolResult.Error(exception.GetBaseException().Message); }

            var reward = CarePackageInfoDictionary(selected, rewards.IndexOf(selected));
            if (ToolUtil.GetBool(args, "dryRun", false))
            {
                return JsonResult(new Dictionary<string, object>
                {
                    ["dryRun"] = true,
                    ["before"] = before,
                    ["selectedReward"] = reward,
                    ["printingRewards"] = PrintingRewardStatus(telepad)
                });
            }

            if (!ToolUtil.GetBool(args, "confirm", false))
                return CallToolResult.Error("confirm=true required to claim printing pod care package");

            try { PrintingPodNativeChoices.Accept(telepad, selected); }
            catch (Exception exception)
            {
                return CallToolResult.Error("Native package claim failed: " + exception.GetBaseException().Message
                    + ". Delivery may have started; inspect current choices and inventory before retrying.");
            }
            reward["claimable"] = false;
            return JsonResult(new Dictionary<string, object>
            {
                ["claimed"] = true,
                ["before"] = before,
                ["after"] = TelepadInfo(telepad, includeVictory: false),
                ["selectedReward"] = reward,
                ["printingRewards"] = PrintingRewardStatus(telepad)
            });
        }

        private static Dictionary<string, object> PrintingRewardStatus(Telepad telepad, bool includeRecruitment = true)
        {
            var immigration = Immigration.Instance;
            var rewards = CurrentCarePackages(telepad)
                .Select((item, index) => CarePackageInfoDictionary(item, index))
                .ToList();
            bool available = immigration != null && immigration.ImmigrantsAvailable;

            var result = new Dictionary<string, object>
            {
                ["available"] = available,
                ["timeRemainingSeconds"] = immigration == null ? (object)null : Math.Round(ToolUtil.SafeFloat(immigration.GetTimeRemaining()), 1),
                ["timeRemainingCycles"] = immigration == null ? (object)null : Math.Round(ToolUtil.SafeFloat(immigration.GetTimeRemaining() / 600f), 3),
                ["rewardCount"] = rewards.Count,
                ["rewards"] = rewards,
                ["claimSupport"] = "claim",
                ["dupeClaimSupport"] = "list_candidates/recruit",
                ["prepared"] = rewards.Count > 0 || CurrentPrintingCandidates(telepad).Count > 0,
                ["prepareAction"] = "prepare_choices; dryRun or confirm=true"
            };
            var recruitment = includeRecruitment ? PrintingRecruitmentReceipt.Status(telepad) : null;
            if (recruitment != null) result["recruitment"] = recruitment;
            return result;
        }

        private static CarePackageContainer.CarePackageInstanceData ResolvePrintingReward(JObject args, List<CarePackageContainer.CarePackageInstanceData> rewards)
        {
            if (rewards == null || rewards.Count == 0)
                return null;

            int? rewardIndex = ToolUtil.GetInt(args, "rewardIndex") ?? ToolUtil.GetInt(args, "itemIndex");
            if (rewardIndex.HasValue)
                return rewardIndex.Value >= 0 && rewardIndex.Value < rewards.Count ? rewards[rewardIndex.Value] : null;

            string query = FirstNonEmpty(args["itemId"], args["query"], args["target"], args["name"]);
            if (!string.IsNullOrWhiteSpace(query))
            {
                foreach (var reward in rewards)
                {
                    var info = CarePackageInfoDictionary(reward, rewards.IndexOf(reward));
                    if (MatchesQuery(info, query))
                        return reward;
                }

                return null;
            }

            return rewards[0];
        }

        private static IEnumerable<CarePackageContainer.CarePackageInstanceData> CurrentCarePackages(Telepad telepad)
        {
            if (Immigration.Instance == null || !Immigration.Instance.ImmigrantsAvailable)
                return Enumerable.Empty<CarePackageContainer.CarePackageInstanceData>();
            if (HeadlessPrintingChoices.HasPrepared(telepad))
                return HeadlessPrintingChoices.Packages(telepad);
            if (HeadlessPrintingChoices.HasUiBatch)
                return Enumerable.Empty<CarePackageContainer.CarePackageInstanceData>();
            var screen = ImmigrantScreen.instance;
            if (screen == null || screen.IsStarterMinion || (telepad != null && screen.Telepad != telepad))
                return Enumerable.Empty<CarePackageContainer.CarePackageInstanceData>();
            return PrintingPodNativeChoices.Containers
                .OfType<CarePackageContainer>()
                .Where(container => HeadlessPrintingChoices.IsCurrentNativeCard(container) && container.carePackageInstanceData?.info != null)
                .Select(container => container.carePackageInstanceData)
                .ToList();
        }
        private static Dictionary<string, object> CarePackageInfoDictionary(CarePackageContainer.CarePackageInstanceData choice, int index)
        {
            var info = choice?.info;
            var prefab = info == null || string.IsNullOrWhiteSpace(info.id) ? null : Assets.GetPrefab(info.id);
            return new Dictionary<string, object>
            {
                ["index"] = index,
                ["kind"] = "care_package",
                ["id"] = info?.id,
                ["prefabId"] = info?.id,
                ["name"] = prefab == null ? info?.id : ToolUtil.CleanName(prefab.GetProperName()),
                ["entityKind"] = prefab?.GetComponent<CreatureBrain>() != null ? "critter" : "item",
                ["quantity"] = info == null ? (object)null : Math.Round(ToolUtil.SafeFloat(info.quantity), 3),
                ["facadeId"] = choice?.facadeID,
                ["requirementMet"] = info?.requirement == null ? (object)null : SafeRequirement(info.requirement),
                ["claimable"] = true
            };
        }

        private static string FirstNonEmpty(params JToken[] values)
        {
            foreach (var value in values)
            {
                string text = value?.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }

            return null;
        }

        private static bool SafeRequirement(Func<bool> requirement)
        {
            try
            {
                return requirement == null || requirement();
            }
            catch
            {
                return false;
            }
        }

    }
}
