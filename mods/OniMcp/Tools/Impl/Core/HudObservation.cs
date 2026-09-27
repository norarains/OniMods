using System;
using System.Collections.Generic;

namespace OniMcp.Tools
{
    internal static class HudObservation
    {
        internal static void Read(ContinueSample sample)
        {
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
                sample.HudFindings.Add(finding);
            }
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
                        sample.HudFindings.Add(HudFindingPolicy.Diagnostic(world.Key, diagnostic.id,
                            opinion, ToolUtil.CleanName(result.Message)));
                }
            }
        }
    }
}
