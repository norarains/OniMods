using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    // Native pump/conduit statuses can be stale immediately after load or network refresh.
    // Report them immediately; require two simulated seconds of persistence to interrupt.
    internal sealed class SupplyStatusTiming
    {
        private readonly Dictionary<string, double> firstSeen = new Dictionary<string, double>();
        private double lastTime = -1;
        internal void Apply(IEnumerable<BuildingSupplyFinding> supplies, double seconds)
        {
            if (seconds < lastTime) firstSeen.Clear();
            lastTime = seconds;
            var seen = new HashSet<string>();
            foreach (var supply in supplies)
            {
                supply.ConfirmationPending = false;
                if (supply.Construction || supply.Statuses.Count == 0 || supply.Statuses.Any(id =>
                    id != "EmptyPumpingStation" && id != "LiquidPipeEmpty" && id != "GasPipeEmpty")) continue;
                string key = supply.WorldId + ":" + supply.Id + ":" + string.Join(",", supply.Statuses);
                seen.Add(key);
                if (!firstSeen.ContainsKey(key)) firstSeen[key] = seconds;
                supply.ConfirmationPending = seconds - firstSeen[key] < 2;
            }
            foreach (string key in firstSeen.Keys.Except(seen).ToList()) firstSeen.Remove(key);
        }
    }
}
