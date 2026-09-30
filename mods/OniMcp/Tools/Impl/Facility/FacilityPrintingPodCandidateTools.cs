using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class FacilitySideScreenTools
    {
        private sealed class PrintingCandidateToken
        {
            internal readonly string Value = Guid.NewGuid().ToString("N");
        }

        private static readonly ConditionalWeakTable<MinionStartingStats, PrintingCandidateToken> PrintingCandidateTokens
            = new ConditionalWeakTable<MinionStartingStats, PrintingCandidateToken>();

        private static List<MinionStartingStats> CurrentPrintingCandidates(Telepad telepad)
        {
            if (HeadlessPrintingChoices.HasPrepared(telepad))
                return HeadlessPrintingChoices.Candidates(telepad);
            if (HeadlessPrintingChoices.HasUiBatch)
                return new List<MinionStartingStats>();
            if (Immigration.Instance == null || !Immigration.Instance.ImmigrantsAvailable
                || ImmigrantScreen.instance == null || ImmigrantScreen.instance.IsStarterMinion
                || ImmigrantScreen.instance.Telepad != telepad)
                return new List<MinionStartingStats>();
            return PrintingPodNativeChoices.Containers.OfType<CharacterContainer>()
                .Where(container => HeadlessPrintingChoices.IsCurrentNativeCard(container) && container.Stats != null)
                .Select(container => container.Stats).ToList();
        }

        private static string PrintingCandidateId(Telepad telepad, MinionStartingStats stats)
        {
            return telepad.gameObject.GetInstanceID() + ":" + PrintingCandidateTokens.GetValue(stats, _ => new PrintingCandidateToken()).Value;
        }

        private static Dictionary<string, object> PrintingCandidateInfo(Telepad telepad, MinionStartingStats stats)
        {
            return new Dictionary<string, object>
            {
                ["candidateId"] = PrintingCandidateId(telepad, stats),
                ["name"] = ToolUtil.CleanName(stats.Name),
                ["model"] = stats.personality?.model.ToString(),
                ["attributes"] = stats.StartingLevels,
                ["interests"] = stats.skillAptitudes.Select(pair => new Dictionary<string, object>
                {
                    ["id"] = pair.Key.Id, ["name"] = ToolUtil.CleanName(pair.Key.Name), ["aptitude"] = pair.Value
                }).ToList(),
                ["traits"] = stats.Traits.Where(trait => trait != null).Select(trait => new Dictionary<string, object>
                {
                    ["id"] = trait.Id, ["name"] = ToolUtil.CleanName(trait.Name)
                }).ToList(),
                ["stressReaction"] = stats.stressTrait == null ? null : new Dictionary<string, object>
                {
                    ["id"] = stats.stressTrait.Id, ["name"] = ToolUtil.CleanName(stats.stressTrait.Name)
                },
                ["joyReaction"] = stats.joyTrait == null ? null : new Dictionary<string, object>
                {
                    ["id"] = stats.joyTrait.Id, ["name"] = ToolUtil.CleanName(stats.joyTrait.Name)
                }
            };
        }

        private static Dictionary<string, object> PrintingCandidateStatus(Telepad telepad)
        {
            var candidates = CurrentPrintingCandidates(telepad);
            return new Dictionary<string, object>
            {
                ["available"] = Immigration.Instance != null && Immigration.Instance.ImmigrantsAvailable,
                ["prepared"] = HeadlessPrintingChoices.HasPrepared(telepad)
                    || (ImmigrantScreen.instance != null && ImmigrantScreen.instance.Telepad == telepad
                        && PrintingPodNativeChoices.Containers.Any(HeadlessPrintingChoices.IsCurrentNativeCard)),
                ["candidates"] = candidates.Select(stats => PrintingCandidateInfo(telepad, stats)).ToList(),
                ["prepareAction"] = "prepare_choices",
                ["recruitAction"] = "recruit candidateId=<listed token>; dryRun or confirm=true"
            };
        }

        private static string PrintingPodActionError(Telepad telepad)
        {
            if (telepad == null || !PlayerVisibility.Object(telepad.gameObject))
                return "Target Printing Pod is not player-discovered.";
            if (Immigration.Instance == null || !Immigration.Instance.ImmigrantsAvailable)
                return "No Printing Pod choices are available right now.";
            var operational = telepad.GetComponent<Operational>();
            if (operational == null || !operational.IsOperational)
                return "Target Printing Pod is not operational.";
            if (SpeedControlScreen.Instance == null || !SpeedControlScreen.Instance.IsPaused)
                return "Pause the game before preparing or claiming Printing Pod choices.";
            return null;
        }

        private static CallToolResult PreparePrintingChoices(JObject args, Telepad telepad, Dictionary<string, object> before)
        {
            string error = PrintingPodActionError(telepad);
            if (error != null) return CallToolResult.Error(error);
            bool dryRun = ToolUtil.GetBool(args, "dryRun", false);
            if (!dryRun && !ToolUtil.GetBool(args, "confirm", false))
                return CallToolResult.Error("confirm=true required to prepare native Printing Pod choices.");
            bool materialized = HeadlessPrintingChoices.HasPrepared(telepad)
                || (ImmigrantScreen.instance != null && ImmigrantScreen.instance.Telepad == telepad
                    && PrintingPodNativeChoices.Containers.Count > 0);
            if (!dryRun)
            {
                try { PrintingPodNativeChoices.Initialize(telepad); }
                catch (Exception exception)
                {
                    return CallToolResult.Error("Native choice preparation failed: " + exception.GetBaseException().Message
                        + ". Read current choices before retrying.");
                }
            }
            return JsonResult(new Dictionary<string, object>
            {
                ["dryRun"] = dryRun,
                ["materialized"] = !dryRun && !materialized,
                ["wouldMaterialize"] = dryRun && !materialized,
                ["uiOpened"] = false,
                ["printingCandidates"] = PrintingCandidateStatus(telepad),
                ["printingRewards"] = PrintingRewardStatus(telepad)
            });
        }

        private static CallToolResult RecruitPrintingCandidate(JObject args, Telepad telepad, Dictionary<string, object> before)
        {
            string error = PrintingPodActionError(telepad);
            if (error != null) return CallToolResult.Error(error);
            string candidateId = args["candidateId"]?.ToString();
            if (string.IsNullOrWhiteSpace(candidateId))
                return CallToolResult.Error("An explicit candidateId from list_candidates is required; recruitment never selects a default candidate.");
            var candidate = CurrentPrintingCandidates(telepad)
                .SingleOrDefault(stats => PrintingCandidateId(telepad, stats) == candidateId);
            if (candidate == null)
                return CallToolResult.Error("Candidate is stale or belongs to another Printing Pod. Read list_candidates again; no choice was consumed.");

            int population = Components.LiveMinionIdentities.Items.Count(dupe => dupe != null);
            int? maximum = ToolUtil.GetInt(args, "maxPopulation");
            if (args.Property("maxPopulation") != null
                && (args["maxPopulation"].Type != JTokenType.Integer || !maximum.HasValue || maximum.Value < 1))
                return CallToolResult.Error("maxPopulation must be a positive integer within the supported range; no choice was consumed.");
            if (maximum.HasValue && (maximum.Value < 1 || population >= maximum.Value))
                return CallToolResult.Error("Recruitment would exceed maxPopulation; no choice was consumed.");
            try { PrintingPodNativeChoices.ValidateAcceptance(telepad, candidate); }
            catch (Exception exception) { return CallToolResult.Error(exception.GetBaseException().Message); }
            var info = PrintingCandidateInfo(telepad, candidate);
            if (ToolUtil.GetBool(args, "dryRun", false))
                return JsonResult(new Dictionary<string, object>
                {
                    ["dryRun"] = true, ["selectedCandidate"] = info,
                    ["populationBefore"] = population, ["expectedPopulation"] = population + 1,
                    ["worldId"] = telepad.gameObject.GetMyWorldId()
                });
            if (!ToolUtil.GetBool(args, "confirm", false))
                return CallToolResult.Error("confirm=true required to recruit a Printing Pod duplicant.");

            using (var capture = PrintingRecruitmentReceipt.Begin(telepad, candidate, candidateId, population))
            {
                try { PrintingPodNativeChoices.Accept(telepad, candidate); }
                catch (Exception exception)
                {
                    var failed = JsonResult(capture.Finish(false, "Native recruitment failed: "
                        + exception.GetBaseException().Message + ". An accepted or consumed delivery must not be replayed."));
                    failed.IsError = true;
                    return failed;
                }
                var receipt = capture.Finish(true);
                receipt["selectedCandidate"] = info;
                receipt["printingRewards"] = PrintingRewardStatus(telepad, includeRecruitment: false);
                return JsonResult(receipt);
            }
        }
    }
}
