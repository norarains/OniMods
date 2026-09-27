using System.Collections.Generic;

namespace OniMcp.Tools
{
    internal sealed class ContinueResearchStation
    {
        internal int Id { get; set; }
        internal int WorldId { get; set; }
        internal string PrefabId { get; set; }
        internal string ResearchType { get; set; }
        internal bool Required { get; set; }
        internal double RemainingPoints { get; set; }
        internal bool? Operational { get; set; }
        internal bool? Powered { get; set; }
        internal int? CircuitId { get; set; }
        internal bool? HasPowerSource { get; set; }
        internal double StoredKg { get; set; }
        internal bool MissingMaterial { get; set; }
        internal string[] FailedFlags { get; set; } = new string[0];
        internal string DeliveryItem { get; set; }
        internal bool? DeliveryPaused { get; set; }
        internal bool? DeliveryMeetsRequirements { get; set; }
        internal bool? FetchPending { get; set; }
        internal bool? ResearchChorePresent { get; set; }

        internal Dictionary<string, object> ToDictionary() => new Dictionary<string, object>
        {
            ["id"] = Id, ["worldId"] = WorldId, ["prefabId"] = PrefabId,
            ["researchType"] = ResearchType, ["required"] = Required, ["remainingPoints"] = RemainingPoints,
            ["operational"] = Operational, ["powered"] = Powered, ["circuitId"] = CircuitId,
            ["hasPowerSource"] = HasPowerSource, ["storedKg"] = StoredKg, ["failedFlags"] = FailedFlags,
            ["deliveryItem"] = DeliveryItem, ["deliveryPaused"] = DeliveryPaused,
            ["deliveryMeetsRequirements"] = DeliveryMeetsRequirements, ["fetchPending"] = FetchPending,
            ["researchChorePresent"] = ResearchChorePresent
        };
    }
}
