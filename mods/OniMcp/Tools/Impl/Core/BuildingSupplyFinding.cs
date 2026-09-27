using System;
using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    // Native shortage statuses only: ordinary delivery waits and unused recipes are not shortages.
    internal sealed class BuildingSupplyFinding
    {
        internal int Id, WorldId, X, Y;
        internal string PrefabId;
        internal bool Construction;
        internal bool ConfirmationPending;
        internal readonly SortedSet<string> Statuses = new SortedSet<string>(StringComparer.Ordinal);
        internal readonly SortedSet<string> Messages = new SortedSet<string>(StringComparer.Ordinal);
        internal readonly SortedDictionary<string, double> Missing = new SortedDictionary<string, double>(StringComparer.Ordinal);

        private static readonly HashSet<string> ShortageStatuses = new HashSet<string>(StringComparer.Ordinal) {
            "MaterialsUnavailable", "MaterialsUnavailableForRefill", "NeedResourceMass",
            "NeedLiquidIn", "NeedGasIn", "NeedSolidIn", "LiquidPipeEmpty", "GasPipeEmpty",
            "NoCoolant", "EmptyPumpingStation", "KettleInsuficientSolids", "KettleInsuficientFuel",
            "NoAvailableSeed", "NoAvailableEgg"
        };
        internal static bool IsShortage(string id) => id != null && ShortageStatuses.Contains(id);

        // Keep essential service interruptions; ordinary machine/construction status is queried on demand.
        internal bool Essential => !Construction && new[] {
            "Electrolyzer", "OxygenDiffuser", "MineralDeoxidizer", "RustDeoxidizer", "AlgaeHabitat",
            "Outhouse", "FlushToilet", "WashBasin", "HandSanitizer"
        }.Contains(PrefabId);

        internal ColonyFinding ToFinding()
        {
            string code = Construction ? "construction_material_missing" : "building_material_missing";
            bool refill = Statuses.Count == 1 && Statuses.Contains("MaterialsUnavailableForRefill");
            return new ColonyFinding {
                Id = code + ":" + WorldId + ":" + Id, Code = code, WorldId = WorldId, TargetId = Id,
                Severity = refill || ConfirmationPending ? "info" : "warning", StopEligible = !refill && !ConfirmationPending, Message = string.Join("; ", Messages),
                // Changes in the kind of shortage matter; small delivery mass changes should not churn deltas.
                Revision = string.Join(",", Statuses) + ":" + string.Join(",", Missing.Keys),
                Details = new Dictionary<string, object> {
                    ["prefabId"] = PrefabId, ["x"] = X, ["y"] = Y,
                    ["supplyState"] = ConfirmationPending ? "pending_status_confirmation" : refill ? "refill_only" : "shortage",
                    ["statusIds"] = Statuses.ToArray(), ["missingAmounts"] = Missing
                }
            };
        }
    }
}
