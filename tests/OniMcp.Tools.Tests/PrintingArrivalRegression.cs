using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OniMcp.Tools;
using UnityEngine;

internal static partial class PrintingPodRegression
{
    private static void TestArrivalReceipts()
    {
        foreach (bool headless in new[] { false, true })
        {
            Reset(!headless);
            if (headless) Body(FacilitySideScreenTools.TestPrintingPrepare(new JObject { ["confirm"] = true }, pod));
            string token = Token();
            pod.DeferArrivalRegistration = true;
            var response = Body(FacilitySideScreenTools.TestPrintingRecruit(new JObject {
                ["candidateId"] = token, ["confirm"] = true
            }, pod));
            Check((bool)response["deliveryAccepted"] && (bool)response["roundConsumed"]
                && (string)response["arrivalStatus"] == "pending" && (int)response["populationObserved"] == 1,
                "accepted " + (headless ? "headless" : "native GUI") + " delivery does not invent registered population");
            Check((int)response["duplicant"]["id"] == 901 && (string)response["candidateId"] == token
                && response["recruited"] == null && response["populationAfter"] == null && response["duplicants"] == null,
                "pending receipt identifies exact captured object without ambiguous completed-arrival fields");
            Check(pod.AcceptCalls == 1 && Immigration.Instance.EndCalls == 1
                && Components.LiveMinionIdentities.Items.Count == 1, "receipt capture does not force registration or another native lifecycle");
            var pending = RecruitmentStatus();
            Check((string)pending["arrivalStatus"] == "pending" && (int)pending["duplicant"]["id"] == 901,
                "passive Printing Pod status retains exact pending identity");
            pod.RegisterArrival(); // Script a later Unity frame while simulation remains paused.
            var registered = RecruitmentStatus();
            Check((string)registered["arrivalStatus"] == "registered" && (int)registered["populationObserved"] == 2
                && (int)registered["duplicant"]["id"] == 901 && (string)registered["candidateId"] == token,
                "passive status resolves registration of the same native object");
            Check(pod.AcceptCalls == 1 && Immigration.Instance.EndCalls == 1 && SpeedControlScreen.Instance.IsPaused,
                "passive resolution leaves native delivery, cooldown and pause untouched");
            Check(FacilitySideScreenTools.TestPrintingRecruit(new JObject {
                ["candidateId"] = token, ["confirm"] = true
            }, pod).IsError && pod.AcceptCalls == 1, "accepted pending or registered delivery cannot be replayed");
            Check(JObject.FromObject(FacilitySideScreenTools.TestPrintingRewards(other))["recruitment"] == null,
                "receipt on another pod does not borrow the accepted arrival");
        }
        TestArrivalObjectCorrelation();
        TestArrivalRegistrationWithoutId();
        TestArrivalExceptionReceipt();
        TestArrivalReceiptReadOnly();
    }

    private static void TestArrivalObjectCorrelation()
    {
        Reset(); pod.DeferArrivalRegistration = true;
        pod.BeforeDelivery = () => AddUnrelatedArrival(first.Stats.Name, 902);
        pod.AfterDelivery = () => AddUnrelatedArrival(first.Stats.Name, 903);
        var response = Body(FacilitySideScreenTools.TestPrintingRecruit(new JObject {
            ["candidateId"] = Token(), ["confirm"] = true
        }, pod));
        Check((int)response["populationObserved"] == 3 && (string)response["arrivalStatus"] == "pending"
            && (int)response["duplicant"]["id"] == 901,
            "unrelated same-name arrivals do not replace the exact selected delivery or prove its registration");
        var unrelatedStats = NewStats(first.Stats.Name);
        unrelatedStats.Deliver(default(Vector3));
        Check((int)RecruitmentStatus()["duplicant"]["id"] == 901 && unrelatedStats.DeliveryCalls == 1,
            "native Deliver outside the capture scope cannot overwrite a receipt");
        pod.RegisterArrival();
        Check((string)RecruitmentStatus()["arrivalStatus"] == "registered" && (int)RecruitmentStatus()["populationObserved"] == 4,
            "only exact object registration completes the selected arrival");
        var identity = pod.LastDeliveredObject.GetComponent<MinionIdentity>();
        Components.LiveMinionIdentities.Items.Remove(identity);
        Check((string)RecruitmentStatus()["arrivalStatus"] == "registered",
            "historical arrival registration does not become pending when a minion later leaves live population");
    }

    private static void TestArrivalRegistrationWithoutId()
    {
        Reset(); pod.DeferArrivalRegistration = true; pod.SuppressArrivalId = true;
        var result = Body(FacilitySideScreenTools.TestPrintingRecruit(new JObject {
            ["candidateId"] = Token(), ["confirm"] = true
        }, pod));
        Check((string)result["arrivalStatus"] == "pending" && result["duplicant"].Type == JTokenType.Null,
            "an uninitialized native identifier is not fabricated from population or names");
        pod.RegisterArrival();
        Check((string)RecruitmentStatus()["arrivalStatus"] == "pending",
            "registered list membership without usable native ID remains pending");
        pod.LastDeliveredObject.Components[typeof(KPrefabID)] = new KPrefabID { InstanceID = 904 };
        Check((string)RecruitmentStatus()["arrivalStatus"] == "registered" && (int)RecruitmentStatus()["duplicant"]["id"] == 904,
            "passive observation resolves initialized ID on the original captured object");
    }

    private static void TestArrivalExceptionReceipt()
    {
        foreach (bool afterDelivery in new[] { false, true })
        {
            Reset(); pod.DeferArrivalRegistration = true;
            pod.ThrowAfterEnd = !afterDelivery; pod.ThrowAfterDelivery = afterDelivery;
            string token = Token();
            var result = FacilitySideScreenTools.TestPrintingRecruit(new JObject {
                ["candidateId"] = token, ["confirm"] = true
            }, pod);
            var receipt = JObject.Parse(result.Content[0].Text);
            Check(result.IsError && (bool)receipt["roundConsumed"] && receipt["error"] != null
                && Immigration.Instance.EndCalls == 1 && pod.AcceptCalls == 1,
                "failed native acceptance exposes consumed round rather than inviting a replay");
            Check(afterDelivery ? (bool)receipt["deliveryAccepted"] && (string)receipt["arrivalStatus"] == "pending"
                    && (int)receipt["duplicant"]["id"] == 901
                : receipt["deliveryAccepted"].Type == JTokenType.Null && (string)receipt["arrivalStatus"] == "unconfirmed"
                    && receipt["duplicant"].Type == JTokenType.Null,
                "failure receipt distinguishes captured delivery from unconfirmed post-cooldown failure");
            Check(JToken.DeepEquals(receipt, RecruitmentStatus()), "existing passive status preserves the failed-delivery facts");
            Check(FacilitySideScreenTools.TestPrintingRecruit(new JObject {
                ["candidateId"] = token, ["confirm"] = true
            }, pod).IsError && pod.AcceptCalls == 1 && Immigration.Instance.EndCalls == 1,
                "consumed exception cannot duplicate recruitment");
            if (afterDelivery)
            {
                pod.RegisterArrival();
                Check((string)RecruitmentStatus()["arrivalStatus"] == "registered" && RecruitmentStatus()["error"] != null,
                    "later registration resolves the exact arrival while preserving native callback error");
            }
        }
    }

    private static void TestArrivalReceiptReadOnly()
    {
        Reset();
        string token = Token();
        Check(JObject.FromObject(FacilitySideScreenTools.TestPrintingRewards(pod))["recruitment"] == null,
            "passive status before acceptance does not manufacture a receipt");
        foreach (bool confirm in new[] { false, true })
        {
            Body(FacilitySideScreenTools.TestPrintingRecruit(new JObject {
                ["candidateId"] = token, ["dryRun"] = true, ["confirm"] = confirm
            }, pod));
            Check(JObject.FromObject(FacilitySideScreenTools.TestPrintingRewards(pod))["recruitment"] == null
                && first.Stats.DeliveryCalls == 0, "recruit preview neither invokes capture nor creates a receipt");
            NoNativeMutation("recruit receipt preview");
        }
        pod.DeferArrivalRegistration = true;
        Body(FacilitySideScreenTools.TestPrintingRecruit(new JObject { ["candidateId"] = token, ["confirm"] = true }, pod));
        int rng = UnityEngine.Random.Calls, generated = MinionStartingStats.Generated;
        for (int i = 0; i < 3; i++)
        {
            RecruitmentStatus(); Candidates(); FacilitySideScreenTools.TestPrintingRewards(pod);
        }
        Check(pod.AcceptCalls == 1 && Immigration.Instance.EndCalls == 1 && first.Stats.DeliveryCalls == 1
            && UnityEngine.Random.Calls == rng && MinionStartingStats.Generated == generated
            && Components.LiveMinionIdentities.Items.Count == 1 && SpeedControlScreen.Instance.IsPaused,
            "passive pending reads leave RNG, delivery, registration and pause unchanged");
        Immigration.Instance.OnPrefabInit();
        Check(JObject.FromObject(FacilitySideScreenTools.TestPrintingRewards(pod))["recruitment"] == null,
            "owner/save-load initialization discards pending receipt correlation");
        pod.RegisterArrival();
        Check(JObject.FromObject(FacilitySideScreenTools.TestPrintingRewards(pod))["recruitment"] == null,
            "late old-session registration cannot revive a cleared receipt");
    }

    private static JObject RecruitmentStatus()
        => (JObject)JObject.FromObject(FacilitySideScreenTools.TestPrintingRewards(pod))["recruitment"];

    private static void AddUnrelatedArrival(string name, int id)
    {
        var identity = new MinionIdentity();
        identity.gameObject.name = name;
        identity.gameObject.Cell = pod.gameObject.Cell;
        identity.gameObject.Components[typeof(MinionIdentity)] = identity;
        identity.gameObject.Components[typeof(KPrefabID)] = new KPrefabID { InstanceID = id };
        Components.LiveMinionIdentities.Items.Add(identity);
    }
}
