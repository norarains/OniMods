using System;

namespace OniMcp.Tools
{
    internal static class BatteryThresholdPolicy
    {
        internal static bool Valid(double low, double high) => !double.IsNaN(low) && !double.IsNaN(high)
            && low >= 0 && high <= 100 && low <= high && low == Math.Truncate(low) && high == Math.Truncate(high);
    }
}
