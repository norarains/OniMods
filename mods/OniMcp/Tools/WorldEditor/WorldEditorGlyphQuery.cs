using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    public static partial class WorldEditorTools
    {
        private static JObject LookupGlyphQuery(List<JObject> rows, string input, string direction,
            string matchMode, string view, int limit)
        {
            var candidates = FilterGlyphRowsByView(rows, view);
            string resolvedDirection = direction == "auto"
                ? candidates.Any(row => GlyphText(row, "symbol").Equals(input, StringComparison.Ordinal))
                    ? "code_to_meaning"
                    : "meaning_to_code"
                : direction;
            Func<JObject, IEnumerable<string>> values = resolvedDirection == "code_to_meaning"
                ? (Func<JObject, IEnumerable<string>>)(row => new[] { GlyphText(row, "symbol") })
                : row => new[]
                {
                    GlyphText(row, "id"), GlyphText(row, "name"), GlyphText(row, "kind"),
                    GlyphText(row, "meaning"), GlyphText(row, "dirs")
                };

            var comparison = resolvedDirection == "code_to_meaning"
                ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var exactMatches = candidates.Where(row => values(row).Any(value =>
                value.Equals(input, comparison))).ToList();
            bool useExact = matchMode == "exact" || (matchMode == "auto" && exactMatches.Count > 0);
            var matches = useExact
                ? exactMatches
                : candidates.Where(row => values(row).Any(value =>
                    value.IndexOf(input ?? string.Empty, comparison) >= 0)).ToList();

            return new JObject
            {
                ["input"] = input,
                ["resolvedDirection"] = resolvedDirection,
                ["exact"] = useExact && exactMatches.Count > 0,
                ["count"] = matches.Count,
                ["matches"] = new JArray(matches.Take(limit))
            };
        }

    }
}
