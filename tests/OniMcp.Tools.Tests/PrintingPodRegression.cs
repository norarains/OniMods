using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using OniMcp.Tools;
using UnityEngine;

internal static partial class PrintingPodRegression
{
    private static int checks;
    private static Telepad pod, other;
    private static CharacterContainer first, second;
    private static CarePackageContainer package;
    private static ImmigrantScreen Screen => ImmigrantScreen.instance;

    internal static void Run()
    {
        TestReadsAndPreparation();
        TestTargetSelection();
        TestRecruitPreviews();
        TestRecruitGuards();
        TestRecruitLifecycle();
        TestNativeCallbackGuard();
        TestCarePackageLifecycle();
        TestHeadlessPreparation();
        TestArrivalReceipts();
        Reset();
        Components.Telepads.Items.Clear();
        Components.LiveMinionIdentities.Items.Clear();
        Immigration.Instance = null;
        ImmigrantScreen.instance = null;
        Console.WriteLine("Printing Pod actual handler regression: " + checks + " checks passed");
    }

    private static void TestReadsAndPreparation()
    {
        Reset(false);
        Check(Candidates()["candidates"].Count() == 0, "empty list must not manufacture candidates");
        FacilitySideScreenTools.TestPrintingRewards(pod);
        NoNativeMutation("empty candidate/reward reads");
        foreach (bool confirm in new[] { false, true })
        {
            var preview = Body(FacilitySideScreenTools.TestPrintingPrepare(new JObject {
                ["dryRun"] = true, ["confirm"] = confirm
            }, pod));
            Check((bool)preview["dryRun"] && !(bool)preview["uiOpened"], "prepare preview never opens the modal");
            NoNativeMutation("prepare preview confirm=" + confirm);
        }
        Check(FacilitySideScreenTools.TestPrintingPrepare(new JObject(), pod).IsError,
            "offer generation is an explicit confirmed operation");
        NoNativeMutation("unconfirmed prepare");
        Reset();
        var candidate = Candidates()["candidates"][0];
        Check((string)candidate["name"] == "Ada" && (string)candidate["model"] == "Minion",
            "native candidate name and model are exposed");
        Check((int)candidate["attributes"]["Learning"] == 7
            && (string)candidate["interests"][0]["id"] == "Research"
            && (string)candidate["traits"][0]["id"] == "QuickLearner"
            && (string)candidate["stressReaction"]["id"] == "UglyCrier"
            && (string)candidate["joyReaction"]["id"] == "BalloonArtist",
            "list carries decision-relevant native attributes, interests and traits");
        Check(Token() == (string)candidate["candidateId"], "list token is stable for the same native stats");
        Check(Candidates(other)["candidates"].Count() == 0, "other Printing Pod cannot list bound choices");
        NoNativeMutation("materialized list");
    }

    private static void TestRecruitPreviews()
    {
        foreach (bool confirm in new[] { false, true })
        {
            Reset();
            Screen.Selected.Add(second.Stats);
            var preview = Body(FacilitySideScreenTools.TestPrintingRecruit(new JObject {
                ["candidateId"] = Token(), ["dryRun"] = true, ["confirm"] = confirm, ["maxPopulation"] = 2
            }, pod));
            Check((bool)preview["dryRun"] && (int)preview["populationBefore"] == 1
                && (int)preview["expectedPopulation"] == 2, "preview labels bounded expected population separately from observed arrival");
            Check(Screen.Selected.Single() == second.Stats, "preview leaves native player's selection untouched");
            NoNativeMutation("recruit preview confirm=" + confirm);
        }
    }

    private static void TestTargetSelection()
    {
        Reset();
        Check(FacilitySideScreenTools.TestPrintingTarget(new JObject { ["worldId"] = 1 }) == other.gameObject,
            "explicit world selection resolves that world's Printing Pod");
        Check(FacilitySideScreenTools.TestPrintingTarget(new JObject { ["worldId"] = 9 }) == null,
            "missing explicit world cannot silently fall back to a different Printing Pod");
        Check(FacilitySideScreenTools.TestPrintingTarget(new JObject { ["id"] = 9999 }) == null,
            "missing exact target cannot fall back to default Printing Pod");
        Grid.Hidden.Add(pod.gameObject.Cell);
        Check(FacilitySideScreenTools.TestPrintingTarget(new JObject { ["worldId"] = 0 }) == null,
            "default target lookup does not expose an undiscovered Printing Pod");
    }

    private static void TestRecruitGuards()
    {
        Reset();
        Rejected(new JObject { ["confirm"] = true }, pod, "missing explicit identity");
        Rejected(new JObject { ["candidateId"] = Token() }, pod, "missing confirmation");
        Rejected(new JObject { ["candidateId"] = "stale", ["confirm"] = true }, pod, "unknown token");
        Rejected(new JObject { ["candidateId"] = Token(), ["confirm"] = true }, other, "wrong Printing Pod");
        Rejected(new JObject { ["candidateId"] = Token(), ["confirm"] = true, ["maxPopulation"] = 1 }, pod, "at population cap");
        Rejected(new JObject { ["candidateId"] = Token(), ["confirm"] = true, ["maxPopulation"] = 0 }, pod, "invalid population cap");
        foreach (var cap in new JToken[] { JValue.CreateNull(), new JValue(2.5), new JValue(2.0),
            new JValue(long.MaxValue), new JValue("2"), new JValue(true), new JObject(), new JArray(2) })
        {
            Rejected(new JObject { ["candidateId"] = Token(), ["confirm"] = true, ["maxPopulation"] = cap },
                pod, "malformed population cap " + cap.Type);
        }
        string old = Token();
        first.Stats = NewStats("Ada");
        Rejected(new JObject { ["candidateId"] = old, ["confirm"] = true }, pod,
            "same-name replacement cannot revive stale native stats token");

        foreach (string guard in new[] { "cooldown", "running", "hidden", "notOperational", "starter", "multiChoice", "noScreen" })
        {
            Reset();
            var args = new JObject { ["candidateId"] = Token(), ["confirm"] = true };
            if (guard == "cooldown") Immigration.Instance.ImmigrantsAvailable = false;
            if (guard == "running") SpeedControlScreen.Instance.IsPaused = false;
            if (guard == "hidden") Grid.Hidden.Add(pod.gameObject.Cell);
            if (guard == "notOperational") pod.GetComponent<Operational>().IsOperational = false;
            if (guard == "starter") Screen.IsStarterMinion = true;
            if (guard == "multiChoice") Screen.ChoiceCount = 2;
            var screen = Screen;
            if (guard == "noScreen") ImmigrantScreen.instance = null;
            Check(FacilitySideScreenTools.TestPrintingRecruit(args, pod).IsError, guard + " fails closed");
            ImmigrantScreen.instance = screen;
            NoNativeMutation(guard);
        }
        Reset();
        first.Stats = null;
        Check(Candidates()["candidates"].Count() == 1, "pending native cards are absent until stats exist");
        NoNativeMutation("pending native card");
    }

    private static void TestRecruitLifecycle()
    {
        Reset();
        string token = Token();
        Screen.Selected.Add(second.Stats);
        var result = Body(FacilitySideScreenTools.TestPrintingRecruit(new JObject {
            ["candidateId"] = token, ["confirm"] = true, ["maxPopulation"] = 2
        }, pod));
        Check((bool)result["deliveryAccepted"] && pod.LastAccepted == first.Stats,
            "explicit token replaces player's previous native selection with requested candidate");
        Check(pod.AcceptCalls == 1 && Screen.ProceedCalls == 1 && Immigration.Instance.EndCalls == 1,
            "one native proceed delivers once and owns the one cooldown transition");
        Check(Screen.AddCalls == 1 && Screen.RemoveCalls == 1, "native selection API is used exactly once each");
        Check(Screen.Offers.Count == 0 && UnityEngine.Object.PrintingDestroyCalls == 3
            && first.gameObject.PrintingOfferDestroyed && second.gameObject.PrintingOfferDestroyed
            && package.gameObject.PrintingOfferDestroyed,
            "native completion cleans every old offer, including unselected choices");
        Check((int)result["populationObserved"] == 2 && (string)result["arrivalStatus"] == "registered"
            && (int)result["duplicant"]["id"] == 901,
            "commit receipt identifies the actual newly delivered duplicant");
        Check(!Screen.IsVisible && !Immigration.Instance.ImmigrantsAvailable,
            "native completion closes the selector and starts cooldown");
        Check(FacilitySideScreenTools.TestPrintingRecruit(new JObject {
            ["candidateId"] = token, ["confirm"] = true
        }, pod).IsError && pod.AcceptCalls == 1, "replay cannot recruit a second duplicant");
    }

    private static void TestCarePackageLifecycle()
    {
        Reset();
        var rewards = JObject.FromObject(FacilitySideScreenTools.TestPrintingRewards(pod));
        Check((int)rewards["rewardCount"] == 1, "care packages share current native controller choices");
        foreach (bool confirm in new[] { false, true })
        {
            var preview = Body(FacilitySideScreenTools.TestPrintingClaim(new JObject {
                ["itemId"] = "Algae", ["dryRun"] = true, ["confirm"] = confirm
            }, pod));
            Check((bool)preview["dryRun"], "care package dryRun returns preview");
            NoNativeMutation("care package preview confirm=" + confirm);
        }
        Check(FacilitySideScreenTools.TestPrintingClaim(new JObject { ["itemId"] = "Algae" }, pod).IsError,
            "care package commit still requires confirmation");
        var result = Body(FacilitySideScreenTools.TestPrintingClaim(new JObject {
            ["itemId"] = "Algae", ["confirm"] = true
        }, pod));
        Check((bool)result["claimed"] && pod.LastAccepted is CarePackageContainer.CarePackageInstanceData,
            "care package delivery preserves the native facade-bearing wrapper");
        Check(Immigration.Instance.EndCalls == 1 && pod.AcceptCalls == 1 && Screen.ProceedCalls == 1,
            "care package also uses exactly one native completion lifecycle");
        Check(Screen.Offers.Count == 0 && UnityEngine.Object.PrintingDestroyCalls == 3
            && Components.LiveMinionIdentities.Items.Count == 1,
            "care package clears all offers without silently recruiting");
    }

    private static void TestNativeCallbackGuard()
    {
        Reset();
        Screen.OnAdded = () => { Screen.Selected.Clear(); Screen.Selected.Add(second.Stats); };
        var result = FacilitySideScreenTools.TestPrintingRecruit(new JObject {
            ["candidateId"] = Token(), ["confirm"] = true
        }, pod);
        Check(result.IsError && pod.AcceptCalls == 0 && Immigration.Instance.EndCalls == 0
            && Screen.ProceedCalls == 0 && Screen.CleanupCalls == 0,
            "native selection callback replacing requested candidate fails before delivery");
        Check(Screen.Offers.Count == 3 && Immigration.Instance.ImmigrantsAvailable,
            "selection callback failure leaves the reward cycle unconsumed");
    }

    private static void Reset(bool materialized = true)
    {
        HeadlessPrintingChoices.Clear();
        PrintingRecruitmentReceipt.ClearOwner();
        MinionStartingStats.Generated = 0;
        MinionStartingStats.ThrowOnGeneration = false;
        MinionStartingStats.GeneratedResults.Clear();
        UnityEngine.Random.Calls = 0; UnityEngine.Random.Next = 0;
        UnityEngine.Object.PrintingDestroyCalls = 0;
        Util.InstantiateCalls = 0;
        Game.DisabledDlc.Clear();
        Game.Instance = new Game();
        HarmonyLib.Harmony.CurrentPatches = null;
        CustomGameSettings.Instance = new CustomGameSettings();
        Db.Facades.resources.Clear();
        Components.Telepads.Items.Clear(); Components.LiveMinionIdentities.Items.Clear();
        Grid.Hidden.Clear();
        SpeedControlScreen.Instance = new SpeedControlScreen();
        ClusterManager.Instance.activeWorldId = 0;
        Immigration.Instance = new Immigration();
        ImmigrantScreen.instance = new ImmigrantScreen();
        pod = NewPod(101, 40, 0); other = NewPod(202, 41, 1);
        var dupe = new MinionIdentity();
        dupe.gameObject.Components[typeof(KPrefabID)] = new KPrefabID { InstanceID = 900 };
        Components.LiveMinionIdentities.Items.Add(dupe);
        first = new CharacterContainer { Stats = NewStats("Ada") };
        second = new CharacterContainer { Stats = NewStats("Bert") };
        package = new CarePackageContainer(new CarePackageInfo {
            id = "Algae", quantity = 100, facadeID = "native-facade", requirement = () => true
        });
        Screen.Generate = () => new ITelepadDeliverableContainer[] { first, second, package };
        if (materialized) { Screen.Offers.AddRange(Screen.Generate()); Screen.Bind(pod); }
    }

    private static Telepad NewPod(int id, int cell, int world)
    {
        var value = new Telepad(); value.gameObject.Cell = cell;
        Grid.WorldIdx[cell] = world; Grid.Solid[cell] = false;
        value.gameObject.Components[typeof(Telepad)] = value;
        value.gameObject.Components[typeof(KPrefabID)] = new KPrefabID { InstanceID = id };
        value.gameObject.Components[typeof(Operational)] = new Operational();
        Components.Telepads.Items.Add(value); return value;
    }
    private static MinionStartingStats NewStats(string name)
    {
        var stats = new MinionStartingStats { Name = name, personality = new Personality { Id = name, model = new Tag("Minion") },
            stressTrait = new Klei.AI.Trait { Id = "UglyCrier", Name = "Ugly Crier" },
            joyTrait = new Klei.AI.Trait { Id = "BalloonArtist", Name = "Balloon Artist" } };
        stats.StartingLevels.Add("Learning", 7);
        stats.skillAptitudes.Add(new Database.SkillGroup { Id = "Research", Name = "Researching" }, 1);
        stats.Traits.Add(new Klei.AI.Trait { Id = "QuickLearner", Name = "Quick Learner" });
        return stats;
    }
    private static JObject Candidates(Telepad target = null)
        => JObject.FromObject(FacilitySideScreenTools.TestPrintingCandidates(target ?? pod));
    private static string Token() => (string)Candidates()["candidates"][0]["candidateId"];
    private static JObject Body(CallToolResult result)
    {
        Check(!result.IsError, "successful handler result: " + result.Content[0].Text);
        return JObject.Parse(result.Content[0].Text);
    }
    private static void Rejected(JObject args, Telepad target, string reason)
    {
        Check(FacilitySideScreenTools.TestPrintingRecruit(args, target).IsError, reason + " rejects");
        NoNativeMutation(reason);
    }
    private static void NoNativeMutation(string reason)
    {
        Check(Screen.InitializeCalls == 0 && Screen.GenerateCalls == 0 && Screen.ShowCalls == 0
            && Screen.ProceedCalls == 0 && Screen.AddCalls == 0 && Screen.RemoveCalls == 0
            && Screen.CleanupCalls == 0 && pod.AcceptCalls == 0 && other.AcceptCalls == 0
            && Immigration.Instance.EndCalls == 0 && Components.LiveMinionIdentities.Items.Count == 1
            && MinionStartingStats.Generated == 0 && Immigration.Instance.PackageCalls == 0 && UnityEngine.Random.Calls == 0,
            reason + " leaves native state untouched");
    }
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new InvalidOperationException("Printing Pod: " + message); }
}
