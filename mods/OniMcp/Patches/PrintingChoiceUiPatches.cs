using System;
using System.Collections.Generic;
using HarmonyLib;
using OniMcp.Tools;
using UnityEngine;

namespace OniMcp
{
    // These hooks only bind an already prepared headless round to the ordinary
    // player-opened GUI. They never open it or generate another set of offers.
    [HarmonyPatch(typeof(CharacterSelectionController), "InitializeContainers")]
    internal static class PrintingChoiceInitializeCardsPatch
    {
        private static bool Prefix(CharacterSelectionController __instance)
        {
            if (__instance != ImmigrantScreen.instance || __instance.IsStarterMinion || !HeadlessPrintingChoices.ShouldBridgeUi)
                return true;
            if (HeadlessPrintingChoices.NativeContainers().Count > 0)
                return true;

            try
            {
                InitializeCards(__instance);
            }
            catch (Exception ex)
            {
                // A native/mod UI failure must not roll another batch or escape
                // the hook and deactivate the mod. Headless choices stay usable.
                try { PrintingPodNativeChoices.ClearPendingNativeCards(); }
                catch (Exception) { }
                UnityEngine.Debug.LogWarning("[OniMcp] Cached Printing Pod cards could not be initialized: " + ex.Message);
            }
            return false;
        }

        private static void InitializeCards(CharacterSelectionController __instance)
        {
            var type = typeof(CharacterSelectionController);
            var candidatePrefab = HeadlessPrintingChoices.NativeField(type, "containerPrefab").GetValue(__instance) as CharacterContainer;
            var packagePrefab = HeadlessPrintingChoices.NativeField(type, "carePackageContainerPrefab").GetValue(__instance) as CarePackageContainer;
            var parent = HeadlessPrintingChoices.NativeField(type, "containerParent").GetValue(__instance) as GameObject;
            if (candidatePrefab == null || (packagePrefab == null && HeadlessPrintingChoices.UiPackageCount > 0) || parent == null)
                throw new InvalidOperationException("Native Printing Pod card prefabs are unavailable.");

            HeadlessPrintingChoices.NativeMethod(type, "DisableProceedButton").Invoke(__instance, null);
            __instance.OnReplacedEvent = null;
            var cards = new List<ITelepadDeliverableContainer>();
            HeadlessPrintingChoices.NativeField(type, "containers").SetValue(__instance, cards);
            HeadlessPrintingChoices.NativeField(type, "selectedDeliverables").SetValue(__instance, new List<ITelepadDeliverable>());
            HeadlessPrintingChoices.NativeField(type, "numberOfDuplicantOptions").SetValue(__instance, HeadlessPrintingChoices.UiCandidateCount);
            HeadlessPrintingChoices.NativeField(type, "numberOfCarePackageOptions").SetValue(__instance, HeadlessPrintingChoices.UiPackageCount);

            for (int i = 0; i < HeadlessPrintingChoices.UiCandidateCount; i++)
            {
                var card = Util.KInstantiateUI<CharacterContainer>(candidatePrefab.gameObject, parent);
                card.SetController(__instance);
                card.SetReshufflingState(false);
                cards.Add(card);
            }
            for (int i = 0; i < HeadlessPrintingChoices.UiPackageCount; i++)
            {
                var card = Util.KInstantiateUI<CarePackageContainer>(packagePrefab.gameObject, parent);
                card.SetController(__instance);
                card.SetReshufflingState(false);
                cards.Add(card);
            }
        }
    }

    [HarmonyPatch(typeof(CharacterContainer), "GenerateCharacter", new[] { typeof(bool), typeof(string) })]
    internal static class PrintingChoiceRenderCandidatePatch
    {
        private static bool Prefix(CharacterContainer __instance, bool is_starter)
        {
            if (is_starter) return true;
            var candidate = HeadlessPrintingChoices.CandidateForCard(__instance);
            if (candidate == null) return true;
            try { __instance.SetMinion(candidate); }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[OniMcp] Cached duplicant card could not be rendered: " + ex.Message);
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(CarePackageContainer), "GenerateCharacter")]
    internal static class PrintingChoiceRenderPackagePatch
    {
        private static bool Prefix(CarePackageContainer __instance, bool is_starter)
        {
            if (is_starter) return true;
            var package = HeadlessPrintingChoices.PackageForCard(__instance);
            if (package == null) return true;
            try { RenderPackage(__instance, package); }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[OniMcp] Cached care-package card could not be rendered: " + ex.Message);
            }
            return false;
        }

        private static void RenderPackage(CarePackageContainer __instance, CarePackageContainer.CarePackageInstanceData package)
        {
            var type = typeof(CarePackageContainer);
            HeadlessPrintingChoices.NativeField(type, "info").SetValue(__instance, package.info);
            __instance.carePackageInstanceData = package;
            HeadlessPrintingChoices.NativeMethod(type, "SetAnimator").Invoke(__instance, null);
            HeadlessPrintingChoices.NativeMethod(type, "SetInfoText").Invoke(__instance, null);
            var select = HeadlessPrintingChoices.NativeField(type, "selectButton").GetValue(__instance) as KToggle;
            if (select == null) throw new InvalidOperationException("Native care-package selection button is unavailable.");
            select.ClearOnClick();
            select.onClick += __instance.SelectDeliverable;
        }
    }

    [HarmonyPatch(typeof(Immigration), "EndImmigration")]
    internal static class PrintingChoiceRoundEndedPatch
    {
        private static void Postfix()
        {
            HeadlessPrintingChoices.Clear();
            try
            {
                // Also cover native GUI delivery failures after EndImmigration,
                // rejection and expiry. OnProceed already captured its choice.
                PrintingPodNativeChoices.ClearPendingNativeCards();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[OniMcp] Ended Printing Pod round cleanup failed: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(Immigration), "OnPrefabInit")]
    internal static class PrintingChoiceOwnerInitializedPatch
    {
        private static void Postfix()
        {
            HeadlessPrintingChoices.Clear();
            try { PrintingPodNativeChoices.ClearPendingNativeCards(); }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning("[OniMcp] Previous Printing Pod owner cleanup failed: " + ex.Message);
            }
        }
    }
}
