using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OniMcp.Tools
{
    internal sealed class FactQueryStatus
    {
        internal readonly string Id, NotificationType;
        internal readonly Func<string> ReadText;

        internal FactQueryStatus(string id, string notificationType, Func<string> readText)
        { Id = id; NotificationType = notificationType; ReadText = readText; }
    }

    // Share one native enumeration without resolving localized names for ID predicates.
    internal sealed class FactQueryStatusSnapshot
    {
        private readonly Func<IEnumerable<FactQueryStatus>> read;
        private FactQueryStatus[] entries;
        private JArray ids, details;

        internal FactQueryStatusSnapshot(Func<IEnumerable<FactQueryStatus>> read)
        { this.read = read; }

        internal static IEnumerable<FactField> Fields => new[] {
            new FactField("statusIds", "string[]", cost: "native_status"),
            new FactField("statuses", "array", cost: "native_status")
        };

        private FactQueryStatus[] Entries => entries ?? (entries = read().ToArray());

        internal JArray Ids => ids ?? (ids = new JArray(Entries
            .Select(entry => entry.Id).Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.Ordinal)));

        internal JArray Details => details ?? (details = new JArray(Entries.Select(entry =>
            new JObject { ["id"] = entry.Id, ["text"] = entry.ReadText(), ["type"] = entry.NotificationType })));
    }
}
