using System;
using System.Linq;

namespace OniMcp.Tools
{
    internal static class ContinueObservationFreshness
    {
        // HUD notifications update every sample, while supply facts may be cached for two seconds.
        // Resolve their coverage from one fresh sample before allowing that mismatch to stop time.
        internal static ContinueSample BeforeStop(ContinueSample sample, ContinueStopEvents settings, Func<ContinueSample> refresh)
        {
            return ColonyObservation.Findings(sample).Any(f => f.StopEligible && settings.IsEnabled(f.Code, f.Id))
                ? refresh() : sample;
        }
    }
}
