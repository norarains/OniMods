using System;
using System.Collections.Generic;

namespace OniMcp.Tools
{
    // A preview budget, not a game inventory reservation. Shared within one batch only.
    internal sealed class BuildMaterialBudget
    {
        private readonly Dictionary<string, double> reserved = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        internal bool TryReserve(string material, double requiredKg, double availableKg, out double shortageKg)
        {
            shortageKg = 0;
            if (string.IsNullOrEmpty(material) || double.IsNaN(availableKg) || double.IsInfinity(availableKg) || availableKg < 0)
                return false;
            reserved.TryGetValue(material, out double previous);
            double total = previous + requiredKg;
            shortageKg = Math.Max(0, total - availableKg);
            if (double.IsNaN(total) || double.IsInfinity(total) || requiredKg < 0 || shortageKg > 0.001)
                return false;
            reserved[material] = total;
            return true;
        }
    }
}
