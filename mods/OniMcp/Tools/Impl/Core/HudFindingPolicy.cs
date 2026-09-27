using System;
using System.Collections.Generic;

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
        internal static ColonyFinding Diagnostic(int world, string id, string opinion, string message) => new ColonyFinding {
            Id = "hud:" + world + ":diagnostic:" + id, Code = "hud", WorldId = world,
            Severity = opinion == "DuplicantThreatening" ? "critical" : "warning",
            Message = message, Revision = opinion + ":" + message,
            Details = new Dictionary<string, object> { ["source"] = "diagnostic", ["diagnosticId"] = id, ["opinion"] = opinion }
        };
    }
}
