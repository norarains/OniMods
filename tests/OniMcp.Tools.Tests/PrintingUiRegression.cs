using System;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using OniMcp;
using OniMcp.Tools;
using UnityEngine;

internal static partial class PrintingPodRegression
{
    private static void TestPrintingUiBridge()
    {
        foreach (int random in new[] { 0, 71 })
        {
            Reset(false); UnityEngine.Random.Next = random;
            Body(FacilitySideScreenTools.TestPrintingPrepare(new JObject { ["confirm"] = true }, pod));
            var candidates = HeadlessPrintingChoices.Candidates(pod);
            var packages = HeadlessPrintingChoices.Packages(pod);
            int rng = UnityEngine.Random.Calls, generations = MinionStartingStats.Generated;
            Check(!(bool)PrintingUiNativeHooks.Invoke(typeof(PrintingChoiceInitializeCardsPatch), "Prefix", Screen),
                "player opening GUI uses the actual production headless card initialization prefix");
            Screen.Bind(pod);
            var characterCards = Screen.Offers.OfType<CharacterContainer>().ToList();
            var packageCards = Screen.Offers.OfType<CarePackageContainer>().ToList();
            Check(characterCards.Count == candidates.Count && packageCards.Count == packages.Count
                && Screen.OptionCounts.SequenceEqual(new[] { candidates.Count, packages.Count })
                && Screen.DisabledProceedCalls == 1 && Screen.Selected.Count == 0,
                "GUI creates exactly the already-rolled headless card counts");
            Check(characterCards.All(card => !card.Reshuffling) && packageCards.All(card => !card.Reshuffling),
                "native GUI cannot reroll a prepared headless round");
            for (int i = 0; i < characterCards.Count; i++)
            {
                Check(!(bool)PrintingUiNativeHooks.Invoke(typeof(PrintingChoiceRenderCandidatePatch), "Prefix", characterCards[i], false)
                    && ReferenceEquals(characterCards[i].Stats, candidates[i]) && characterCards[i].RenderCalls == 1,
                    "candidate card renders exact cached native stats instead of generating again");
            }
            for (int i = 0; i < packageCards.Count; i++)
            {
                Check(!(bool)PrintingUiNativeHooks.Invoke(typeof(PrintingChoiceRenderPackagePatch), "Prefix", packageCards[i], false)
                    && ReferenceEquals(packageCards[i].Delivery, packages[i]) && ReferenceEquals(packageCards[i].Info, packages[i].info)
                    && packageCards[i].Delivery.facadeID == packages[i].facadeID
                    && packageCards[i].AnimatorCalls == 1 && packageCards[i].InfoTextCalls == 1,
                    "package card renders exact cached wrapper, facade and native info");
                packageCards[i].SelectButton.Click();
                Check(Screen.Selected.Single() == packages[i], "package UI button selects exact native cached deliverable");
                Screen.Selected.Clear();
            }
            int created = Util.InstantiateCalls;
            Check((bool)PrintingUiNativeHooks.Invoke(typeof(PrintingChoiceInitializeCardsPatch), "Prefix", Screen)
                && Util.InstantiateCalls == created && Screen.Offers.Count == created,
                "reopening existing cards does not instantiate duplicate choices");
            Check(UnityEngine.Random.Calls == rng && MinionStartingStats.Generated == generations,
                "initialization and rendering prefixes never roll new native offers");
        }

        Reset(false);
        CustomGameSettings.Instance.CarePackageSetting.id = "Disabled";
        HeadlessPrintingChoices.NativeField(typeof(CharacterSelectionController), "carePackageContainerPrefab").SetValue(Screen, null);
        Body(FacilitySideScreenTools.TestPrintingPrepare(new JObject { ["confirm"] = true }, pod));
        Check(!(bool)PrintingUiNativeHooks.Invoke(typeof(PrintingChoiceInitializeCardsPatch), "Prefix", Screen)
            && Screen.Offers.OfType<CharacterContainer>().Count() == 3
            && !Screen.Offers.OfType<CarePackageContainer>().Any() && UnityEngine.Random.Calls == 0,
            "disabled care packages need no package prefab or count reroll when displaying three native candidates");

        Reset(false);
        Body(FacilitySideScreenTools.TestPrintingPrepare(new JObject { ["confirm"] = true }, pod));
        var starter = new CharacterSelectionController { IsStarterMinion = true };
        Check((bool)PrintingUiNativeHooks.Invoke(typeof(PrintingChoiceInitializeCardsPatch), "Prefix", starter)
            && starter.Offers.Count == 0 && Util.InstantiateCalls == 0,
            "new-colony setup controller is untouched by Printing Pod GUI bridge");
        Check((bool)PrintingUiNativeHooks.Invoke(typeof(PrintingChoiceRenderCandidatePatch), "Prefix", first, true)
            && (bool)PrintingUiNativeHooks.Invoke(typeof(PrintingChoiceRenderPackagePatch), "Prefix", package, true),
            "starter character and package generation remain native");
        Check((bool)PrintingUiNativeHooks.Invoke(typeof(PrintingChoiceRenderCandidatePatch), "Prefix", first, false),
            "unrelated card without current immigrant controller remains native");

        TestCrossPodGuiBinding();
        TestExternalRoundEndHooks();
    }

    private static void TestCrossPodGuiBinding()
    {
        Reset(false);
        Body(FacilitySideScreenTools.TestPrintingPrepare(new JObject { ["confirm"] = true }, pod));
        string token = Token();
        var candidates = HeadlessPrintingChoices.Candidates(pod);
        Check(!(bool)PrintingUiNativeHooks.Invoke(typeof(PrintingChoiceInitializeCardsPatch), "Prefix", Screen),
            "native GUI can initialize the global prepared round before rebinding its pod");
        Screen.Bind(other);
        foreach (var card in Screen.Offers.OfType<CharacterContainer>())
            PrintingUiNativeHooks.Invoke(typeof(PrintingChoiceRenderCandidatePatch), "Prefix", card, false);
        foreach (var card in Screen.Offers.OfType<CarePackageContainer>())
            PrintingUiNativeHooks.Invoke(typeof(PrintingChoiceRenderPackagePatch), "Prefix", card, false);
        Check(Screen.Offers.OfType<CharacterContainer>().Select(card => card.Stats).SequenceEqual(candidates),
            "player GUI on another pod shows the same global offers without reroll");
        Check(Candidates(other)["candidates"].Count() == 0
            && (int)JObject.FromObject(FacilitySideScreenTools.TestPrintingRewards(other))["rewardCount"] == 0,
            "MCP candidate and reward reads retain explicit prepared-pod identity despite GUI rebinding");
        Check(FacilitySideScreenTools.TestPrintingRecruit(new JObject {
            ["candidateId"] = token, ["confirm"] = true
        }, other).IsError && FacilitySideScreenTools.TestPrintingClaim(new JObject {
            ["itemId"] = "Algae", ["confirm"] = true
        }, other).IsError && pod.AcceptCalls == 0 && other.AcceptCalls == 0,
            "MCP cannot bypass prepared-pod binding using materialized GUI offers");
    }

    private static void TestExternalRoundEndHooks()
    {
        Reset(false);
        Body(FacilitySideScreenTools.TestPrintingPrepare(new JObject { ["confirm"] = true }, pod));
        PrintingUiNativeHooks.Invoke(typeof(PrintingChoiceInitializeCardsPatch), "Prefix", Screen);
        var cards = Screen.Offers.ToArray();
        Screen.Bind(pod); Screen.IsVisible = true;
        Immigration.Instance.EndImmigration();
        Check(!HeadlessPrintingChoices.HasUiBatch && Screen.Offers.Count == 0 && !Screen.IsVisible
            && cards.All(card => card.GetGameObject().PrintingOfferDestroyed)
            && cards.OfType<CharacterContainer>().All(card => card.StopCalls == 1)
            && cards.OfType<CarePackageContainer>().All(card => card.StopCalls == 1),
            "actual native EndImmigration postfix cancels pending cards even without MCP acceptance");
        Reset(false);
        Body(FacilitySideScreenTools.TestPrintingPrepare(new JObject { ["confirm"] = true }, pod));
        Immigration.Instance.OnPrefabInit();
        Check(!HeadlessPrintingChoices.HasPrepared(pod), "actual owner initialization hook clears transient saved-session choices");

        Reset();
        string oldToken = Token();
        var oldPackage = package.Delivery;
        Immigration.Instance.OnPrefabInit();
        Check(Candidates()["candidates"].Count() == 0
            && (int)JObject.FromObject(FacilitySideScreenTools.TestPrintingRewards(pod))["rewardCount"] == 0,
            "owner initialization removes surviving native GUI offers from passive reads");
        Check(FacilitySideScreenTools.TestPrintingRecruit(new JObject {
            ["candidateId"] = oldToken, ["confirm"] = true
        }, pod).IsError && pod.AcceptCalls == 0 && Immigration.Instance.EndCalls == 0,
            "direct recruitment cannot bypass prepare using a previous owner's GUI token");
        bool rejected = false;
        try { PrintingPodNativeChoices.ValidateAcceptance(pod, oldPackage); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "direct native acceptance rejects previous-owner package references");
    }
}

internal static class PrintingUiNativeHooks
{
    internal static object Invoke(Type type, string method, params object[] args)
        => type.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
}
internal sealed class KToggle
{
    public Action onClick;
    internal void ClearOnClick() { onClick = null; }
    internal void Click() { onClick?.Invoke(); }
}
internal static class Util
{
    internal static int InstantiateCalls;
    internal static T KInstantiateUI<T>(GameObject prefab, GameObject parent) where T : new()
    { InstantiateCalls++; return new T(); }
}
namespace UnityEngine
{
    internal static class Debug { internal static void LogWarning(string message) { } }
}
