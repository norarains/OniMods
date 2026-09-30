using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Tools;

internal static partial class PrintingPodRegression
{
    private static void TestHeadlessPreparation()
    {
        TestHeadlessNoScreen();
        TestHeadlessNativeAdoption();
        TestHeadlessRoundInvalidation();
        TestHeadlessNativeGenerationRules();
        TestHeadlessFailureNoReroll();
        TestHeadlessPendingUiCleanup();
        TestGuiDeliveryFailureCleanup();
        TestPrintingUiBridge();
    }

    private static void TestHeadlessNoScreen()
    {
        Reset(false);
        var unusedScreen = Screen;
        ImmigrantScreen.instance = null;
        foreach (bool confirmed in new[] { false, true })
        {
            var preview = Body(FacilitySideScreenTools.TestPrintingPrepare(new JObject {
                ["dryRun"] = true, ["confirm"] = confirmed
            }, pod));
            Check((bool)preview["wouldMaterialize"] && !(bool)preview["uiOpened"]
                && MinionStartingStats.Generated == 0 && UnityEngine.Random.Calls == 0
                && Immigration.Instance.PackageCalls == 0, "headless prepare preview never rolls native choices");
        }
        Check(FacilitySideScreenTools.TestPrintingPrepare(new JObject(), pod).IsError
            && MinionStartingStats.Generated == 0, "headless preparation also requires confirmation");
        var result = Body(FacilitySideScreenTools.TestPrintingPrepare(new JObject { ["confirm"] = true }, pod));
        Check(!(bool)result["uiOpened"] && unusedScreen.InitializeCalls == 0
            && unusedScreen.ShowCalls == 0 && unusedScreen.GenerateCalls == 0,
            "headless preparation needs no native screen or hierarchy activation");
        Check(MinionStartingStats.Generated == 3 && Immigration.Instance.PackageCalls == 1
            && Candidates()["candidates"].Count() == 3 && !MinionStartingStats.LastWasStarter
            && MinionStartingStats.LastModels.SequenceEqual(new[] { GameTags.Minions.Models.Standard, GameTags.Minions.Models.Bionic }),
            "normal enabled-care-package round uses native three-candidate plus one-package factory inputs");
        string token = Token();
        var stats = HeadlessPrintingChoices.Candidates(pod)[0];
        Body(FacilitySideScreenTools.TestPrintingPrepare(new JObject { ["confirm"] = true }, pod));
        Check(MinionStartingStats.Generated == 3 && Immigration.Instance.PackageCalls == 1
            && Token() == token && ReferenceEquals(HeadlessPrintingChoices.Candidates(pod)[0], stats),
            "repeated preparation and lists reuse exact native offers without reroll");
        var copy = HeadlessPrintingChoices.Candidates(pod); copy.Clear();
        Check(HeadlessPrintingChoices.Candidates(pod).Count == 3, "consumer cannot clear cached choices through returned list");
        Check(FacilitySideScreenTools.TestPrintingPrepare(new JObject { ["confirm"] = true }, other).IsError
            && MinionStartingStats.Generated == 3, "other pod cannot prepare a competing random batch");
        Body(FacilitySideScreenTools.TestPrintingRecruit(new JObject {
            ["candidateId"] = token, ["dryRun"] = true, ["confirm"] = true
        }, pod));
        Check(pod.AcceptCalls == 0 && HeadlessPrintingChoices.HasPrepared(pod), "headless recruitment preview retains offers");
        Body(FacilitySideScreenTools.TestPrintingRecruit(new JObject {
            ["candidateId"] = token, ["confirm"] = true
        }, pod));
        Check(pod.AcceptCalls == 1 && ReferenceEquals(pod.LastAccepted, stats)
            && Immigration.Instance.EndCalls == 1 && !HeadlessPrintingChoices.HasPrepared(pod),
            "headless recruitment uses one native Telepad delivery and clears the consumed cache");
        Check(unusedScreen.ShowCalls == 0 && unusedScreen.ProceedCalls == 0 && ImmigrantScreen.instance == null,
            "headless delivery remains independent of immigrant screen");

        Reset(false);
        Body(FacilitySideScreenTools.TestPrintingPrepare(new JObject { ["confirm"] = true }, pod));
        var delivery = HeadlessPrintingChoices.Packages(pod).Single();
        var claim = Body(FacilitySideScreenTools.TestPrintingClaim(new JObject {
            ["itemId"] = "Algae", ["confirm"] = true
        }, pod));
        Check((bool)claim["claimed"] && ReferenceEquals(pod.LastAccepted, delivery)
            && Immigration.Instance.EndCalls == 1 && Screen.ShowCalls == 0 && Screen.ProceedCalls == 0
            && Components.LiveMinionIdentities.Items.Count == 1,
            "headless care package shares exact native wrapper lifecycle without screen or duplicant");
    }

    private static void TestHeadlessNativeAdoption()
    {
        Reset();
        string token = Token();
        Body(FacilitySideScreenTools.TestPrintingPrepare(new JObject { ["confirm"] = true }, pod));
        Check(HeadlessPrintingChoices.HasPrepared(pod) && Token() == token
            && ReferenceEquals(HeadlessPrintingChoices.Candidates(pod)[0], first.Stats)
            && ReferenceEquals(HeadlessPrintingChoices.Packages(pod).Single(), package.Delivery),
            "preparation adopts already-materialized player's offers with same identity and facade");
        NoNativeMutation("adopting native offers");
        Check(!HeadlessPrintingChoices.ShouldBridgeUi && HeadlessPrintingChoices.CandidateForCard(first) == null,
            "adoption retains normal native card rendering instead of enabling a headless GUI override");
        Body(FacilitySideScreenTools.TestPrintingRecruit(new JObject {
            ["candidateId"] = token, ["confirm"] = true
        }, pod));
        Check(Screen.ProceedCalls == 1 && UnityEngine.Object.PrintingDestroyCalls == 3 && Immigration.Instance.EndCalls == 1,
            "adopted GUI offers still use native OnProceed to destroy old cards");

        Reset();
        first.Stats = null;
        Check(FacilitySideScreenTools.TestPrintingPrepare(new JObject { ["confirm"] = true }, pod).IsError
            && !HeadlessPrintingChoices.HasPrepared(pod), "pending native card blocks headless replacement");
        NoNativeMutation("pending-card prepare");
        Reset(); Screen.Bind(other);
        Check(FacilitySideScreenTools.TestPrintingPrepare(new JObject { ["confirm"] = true }, pod).IsError,
            "existing offers bound to another pod cannot be adopted implicitly");
        NoNativeMutation("wrong-pod adoption");
    }

    private static void TestHeadlessRoundInvalidation()
    {
        foreach (string change in new[] { "cooldown", "round", "owner" })
        {
            Reset(false);
            Body(FacilitySideScreenTools.TestPrintingPrepare(new JObject { ["confirm"] = true }, pod));
            string token = Token();
            if (change == "cooldown") Immigration.Instance.ImmigrantsAvailable = false;
            if (change == "round") Immigration.Instance.SpawnIndex++;
            if (change == "owner") Immigration.Instance = new Immigration();
            Check(!HeadlessPrintingChoices.HasPrepared(pod) && Candidates()["candidates"].Count() == 0,
                change + " invalidates cached choices");
            Check(FacilitySideScreenTools.TestPrintingRecruit(new JObject {
                ["candidateId"] = token, ["confirm"] = true
            }, pod).IsError && pod.AcceptCalls == 0, change + " cannot consume stale candidate ID");
        }
    }

    private static void TestHeadlessNativeGenerationRules()
    {
        Reset(false); UnityEngine.Random.Next = 71;
        Body(FacilitySideScreenTools.TestPrintingPrepare(new JObject { ["confirm"] = true }, pod));
        Check(MinionStartingStats.Generated == 2 && Immigration.Instance.PackageCalls == 2,
            "native upper random branch generates two candidates and two packages");
        Reset(false); CustomGameSettings.Instance.CarePackageSetting.id = "Disabled";
        Body(FacilitySideScreenTools.TestPrintingPrepare(new JObject { ["confirm"] = true }, pod));
        Check(MinionStartingStats.Generated == 3 && Immigration.Instance.PackageCalls == 0
            && UnityEngine.Random.Calls == 0, "disabled care packages use native three-candidate count without package RNG");

        Reset(false);
        MinionStartingStats.GeneratedResults.Enqueue(NewStats("Existing"));
        Body(FacilitySideScreenTools.TestPrintingPrepare(new JObject { ["confirm"] = true }, pod));
        Check(MinionStartingStats.Generated == 4 && HeadlessPrintingChoices.Candidates(pod).All(stats => stats.personality.Id != "Existing"),
            "native retry rule avoids existing non-bionic personalities");
        Reset(false);
        var bionic = NewStats("Existing"); bionic.personality.model = GameTags.Minions.Models.Bionic;
        MinionStartingStats.GeneratedResults.Enqueue(bionic);
        Body(FacilitySideScreenTools.TestPrintingPrepare(new JObject { ["confirm"] = true }, pod));
        Check(MinionStartingStats.Generated == 3 && HeadlessPrintingChoices.Candidates(pod)[0].personality.model == GameTags.Minions.Models.Bionic,
            "native bionic personality exemption is retained");

        Reset(false);
        Immigration.Instance.PackageResults.Enqueue(new CarePackageInfo {
            id = "Algae", quantity = 100, facadeID = "SELECTRANDOM", requirement = () => true
        });
        Db.Facades.resources.Add(new Database.EquippableFacadeResource { Id = "chosen-native-facade", DefID = "Algae" });
        Body(FacilitySideScreenTools.TestPrintingPrepare(new JObject { ["confirm"] = true }, pod));
        Check(HeadlessPrintingChoices.Packages(pod).Single().facadeID == "chosen-native-facade",
            "native package facade randomization is retained in cached delivery wrapper");
    }

    private static void TestHeadlessFailureNoReroll()
    {
        Reset(false); MinionStartingStats.ThrowOnGeneration = true;
        Check(FacilitySideScreenTools.TestPrintingPrepare(new JObject { ["confirm"] = true }, pod).IsError
            && MinionStartingStats.Generated == 1, "native factory failure is surfaced");
        MinionStartingStats.ThrowOnGeneration = false;
        Check(FacilitySideScreenTools.TestPrintingPrepare(new JObject { ["confirm"] = true }, pod).IsError
            && MinionStartingStats.Generated == 1 && !HeadlessPrintingChoices.HasPrepared(pod),
            "failed generation cannot be retried into another random offer set");

        Reset(false);
        var patches = new HarmonyLib.Patches();
        patches.Prefixes.Add(new HarmonyLib.Patch { PatchMethod = typeof(string).GetMethod("ToString", Type.EmptyTypes) });
        HarmonyLib.Harmony.CurrentPatches = patches;
        Check(FacilitySideScreenTools.TestPrintingPrepare(new JObject { ["confirm"] = true }, pod).IsError,
            "foreign native-generation patches fail closed");
        NoNativeMutation("mod conflict");
    }

    private static void TestGuiDeliveryFailureCleanup()
    {
        Reset();
        string token = Token();
        pod.ThrowAfterEnd = true;
        var result = FacilitySideScreenTools.TestPrintingRecruit(new JObject {
            ["candidateId"] = token, ["confirm"] = true
        }, pod);
        Check(result.IsError && pod.AcceptCalls == 1 && Immigration.Instance.EndCalls == 1
            && Screen.ProceedCalls == 1, "GUI native delivery failure reports error after exactly one consumed round");
        Check(Screen.Offers.Count == 0 && Screen.Selected.Count == 0
            && first.StopCalls == 1 && second.StopCalls == 1 && package.StopCalls == 1
            && first.gameObject.PrintingOfferDestroyed && second.gameObject.PrintingOfferDestroyed
            && package.gameObject.PrintingOfferDestroyed,
            "native OnProceed failure still cancels pending generation and cleans old offers");
        Immigration.Instance.ImmigrantsAvailable = true;
        pod.ThrowAfterEnd = false;
        Body(FacilitySideScreenTools.TestPrintingPrepare(new JObject { ["confirm"] = true }, pod));
        Check(Token() != token && MinionStartingStats.Generated == 3,
            "next round gets fresh choices after native delivery failure");
        Check(FacilitySideScreenTools.TestPrintingRecruit(new JObject {
            ["candidateId"] = token, ["confirm"] = true
        }, pod).IsError && pod.AcceptCalls == 1, "old GUI candidate token cannot consume the new round");
    }

    private static void TestHeadlessPendingUiCleanup()
    {
        foreach (bool fails in new[] { false, true })
        {
            Reset(false);
            Body(FacilitySideScreenTools.TestPrintingPrepare(new JObject { ["confirm"] = true }, pod));
            string token = Token();
            // Native GUI creates cards before its delayed GenerateCharacter runs.
            // A headless claim in that window must stop every delayed generation.
            first.Stats = null;
            var pendingPackage = new CarePackageContainer(null);
            Screen.Offers.Add(first); Screen.Offers.Add(pendingPackage);
            Screen.Bind(pod); Screen.IsVisible = true;
            Screen.Selected.Add(second.Stats);
            pod.ThrowAfterEnd = fails;
            var result = FacilitySideScreenTools.TestPrintingRecruit(new JObject {
                ["candidateId"] = token, ["confirm"] = true
            }, pod);
            Check(result.IsError == fails && pod.AcceptCalls == 1 && Immigration.Instance.EndCalls == 1,
                "pending GUI cleanup retains the native delivery outcome, fails=" + fails);
            Check(first.StopCalls == 1 && pendingPackage.StopCalls == 1
                && first.gameObject.PrintingOfferDestroyed && pendingPackage.gameObject.PrintingOfferDestroyed
                && Screen.Offers.Count == 0 && Screen.Selected.Count == 0 && !Screen.IsVisible,
                "consumed headless round stops and clears pending UI cards, fails=" + fails);
            Check(!HeadlessPrintingChoices.HasPrepared(pod) && Screen.ProceedCalls == 0,
                "consumed headless round cannot leave cached offers or invoke another delivery");
            Check(FacilitySideScreenTools.TestPrintingRecruit(new JObject {
                ["candidateId"] = token, ["confirm"] = true
            }, pod).IsError && pod.AcceptCalls == 1, "partial delivery failure cannot be retried");
        }
    }
}
