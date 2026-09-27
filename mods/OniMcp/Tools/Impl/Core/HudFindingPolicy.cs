using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace OniMcp.Tools
{
    internal static class HudFindingPolicy
    {
        // Tutorial is also used by ONI for persistent yellow colony warnings.
        internal static bool IsNotificationWarning(string type) => type == "Bad" || type == "BadMinor"
            || type == "Tutorial" || type == "DuplicantThreatening";
        internal static bool IsDiagnosticWarning(string opinion) => opinion == "DuplicantThreatening"
            || opinion == "Bad" || opinion == "Warning" || opinion == "Concern";
        internal static ColonyFinding Notification(string type, string title) => new ColonyFinding {
            Id = "hud:-1:" + type + ": " + title, Code = "hud", WorldId = -1,
            Severity = type == "DuplicantThreatening" ? "critical" : "warning",
            Message = title, Details = new Dictionary<string, object> { ["source"] = "notification", ["type"] = type }
        };
        internal static ColonyFinding Diagnostic(int world, string id, string opinion, string message, bool informational = false) => new ColonyFinding {
            Id = "hud:" + world + ":diagnostic:" + id, Code = "hud", WorldId = world,
            Severity = informational ? "info" : opinion == "DuplicantThreatening" ? "critical" : "warning",
            StopEligible = opinion == "DuplicantThreatening" || (!informational && id != "IdleDiagnostic"),
            Message = message, Revision = opinion + ":" + message,
            Details = new Dictionary<string, object> { ["source"] = "diagnostic", ["diagnosticId"] = id, ["opinion"] = opinion,
                ["cached"] = true, ["ageGameSeconds"] = null, ["nativeUpdateIntervalGameSeconds"] = 4 }
        };
        internal static bool MatchesTemplate(string value, string template)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(template)) return false;
            string pattern = Regex.Escape(template).Replace("\\{0}", ".+").Replace("\\{1}", ".+");
            return Regex.IsMatch(value, "^" + pattern + "$");
        }

        internal static IEnumerable<ColonyFinding> CoalesceNotifications(IEnumerable<ColonyFinding> notifications)
        {
            foreach (var group in notifications.GroupBy(item => item.Id))
            {
                var finding = group.First();
                bool stops = group.Any(item => item.StopEligible);
                var covered = group.Where(item => item.Details.ContainsKey("coveredBy"))
                    .Select(item => item.Details["coveredBy"]).Distinct().ToArray();
                finding.StopEligible = stops;
                if (covered.Length > 0) finding.Details["coveredBy"] = covered;
                yield return finding;
            }
        }
    }
}
