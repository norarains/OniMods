using System;

namespace OniMcp.Tools
{
    internal static class ThresholdValuePolicy
    {
        internal static string NativeUnit(string component) => component.IndexOf("Temperature", StringComparison.OrdinalIgnoreCase) >= 0 ? "K" : "native";

        internal static float ToNative(float value, string unit, bool temperature, float min, float max, Func<float, float> displayToNative)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentException("threshold must be finite");
            switch ((unit ?? "native").ToLowerInvariant())
            {
                case "native": break;
                case "display": value = displayToNative(value); break;
                case "k": if (!temperature) throw new ArgumentException("K requires a temperature threshold"); break;
                case "c": if (!temperature) throw new ArgumentException("C requires a temperature threshold"); value += 273.15f; break;
                case "f": if (!temperature) throw new ArgumentException("F requires a temperature threshold"); value = (value - 32f) * 5f / 9f + 273.15f; break;
                default: throw new ArgumentException("unit must be native, K, C, F, or display");
            }
            if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentException("converted threshold must be finite");
            return Math.Max(min, Math.Min(max, value));
        }
    }
}
