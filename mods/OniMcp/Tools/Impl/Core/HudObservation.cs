using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    internal static class HudObservation
    {
        internal static void Read(ContinueSample sample)
        {
            var notifications = new List<ColonyFinding>();
            foreach (var notification in NotificationTools.GetNotifications(includePending: false))
            {
                string type = notification.Type.ToString();
                if (!notification.IsReady() || !HudFindingPolicy.IsNotificationWarning(type)) continue;
                var finding = HudFindingPolicy.Notification(type, ToolUtil.CleanName(notification.titleText));
                // Match the native notification, not its translated title. Read the same cached
                // daily report as the UI tooltip; never scan atmosphere or invoke tooltip callbacks.
                if (notification.titleText == (string)global::STRINGS.MISC.NOTIFICATIONS.INSUFFICIENTOXYGENLASTCYCLE.NAME)
                {
                    var report = ReportManager.Instance?.YesterdaysReport;
                    if (report != null)
                    {
                        var entry = report.GetEntry(ReportManager.ReportType.OxygenCreated);
                        finding.Details["period"] = "previous_cycle";
                        finding.Details["producedKg"] = Math.Round(entry.Positive, 2);
                        finding.Details["consumedKg"] = Math.Round(Math.Abs(entry.Negative), 2);
                        finding.Details["netKg"] = Math.Round(entry.Net, 2);
                    }
                }
                string covered = CoveredNotification(notification, sample);
                if (covered != null)
                {
                    finding.StopEligible = false;
                    finding.Details[covered == "building_warnings" ? "detailView" : "coveredBy"] = covered;
                }
                notifications.Add(finding);
            }
            sample.HudFindings.AddRange(HudFindingPolicy.CoalesceNotifications(notifications));
            var utility = ColonyDiagnosticUtility.Instance;
            if (utility == null) return;
            foreach (var world in utility.diagnosticDisplaySettings)
            {
                if (sample.WorldId >= 0 && sample.WorldId != world.Key) continue;
                foreach (var setting in world.Value)
                {
                    var diagnostic = utility.GetDiagnostic(setting.Key, world.Key);
                    if (diagnostic == null) continue;
                    var result = diagnostic.LatestResult; // Cached by the game, no evaluation here.
                    string opinion = result.opinion.ToString();
                    if (HudFindingPolicy.IsDiagnosticWarning(opinion))
                    {
                        bool powerTrend = diagnostic.id == "PowerUseDiagnostic" && opinion == "Concern"
                            && HudFindingPolicy.MatchesTemplate(result.Message, global::STRINGS.UI.COLONY_DIAGNOSTICS.POWERUSEDIAGNOSTIC.SIGNIFICANT_POWER_CHANGE_DETECTED);
                        var finding = HudFindingPolicy.Diagnostic(world.Key, diagnostic.id,
                            opinion, ToolUtil.CleanName(result.Message), powerTrend);
                        if (diagnostic.id == "IdleDiagnostic") finding.Details["stopEvent"] = "worker_idle";
                        NativeConditionDetails.DiagnosticTargets(finding, result);
                        sample.HudFindings.Add(finding);
                    }
                }
            }
        }

        private static string CoveredNotification(Notification notification, ContinueSample sample)
        {
            if (notification.Type.ToString() == "DuplicantThreatening") return null;
            var statusItems = Db.Get().BuildingStatusItems;
            if (sample.PrintingReady && notification.titleText == statusItems.NewDuplicantsAvailable.notificationText)
                return "printing_pod_ready:-1";
            var go = notification.Notifier != null ? notification.Notifier.gameObject : null;
            if (go == null) return null;
            int id = go.GetComponent<KPrefabID>()?.InstanceID ?? go.GetInstanceID();
            var supply = sample.BuildingSupplies.FirstOrDefault(item => item.Id == id && item.WorldId == go.GetMyWorldId());
            if (supply == null) return null;
            var statuses = go.GetComponent<KSelectable>()?.GetStatusItemGroup();
            if (statuses == null) return null;
            foreach (var entry in statuses)
                if (entry.item != null && supply.Statuses.Contains(entry.item.Id)
                    && notification.titleText == entry.item.notificationText)
                {
                    bool research = sample.ResearchStations.Any(item => item.Id == id && item.Required);
                    if (!research && !supply.Essential) return "building_warnings";
                    string code = research ? "research_material_missing"
                        : supply.Construction ? "construction_material_missing" : "building_material_missing";
                    return code + ":" + supply.WorldId + ":" + id;
                }
            return null;
        }
    }
}
