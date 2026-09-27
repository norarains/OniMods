using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OniMcp.Tools
{
    internal static class ResearchStationObservation
    {
        private static readonly FieldInfo FetchListField = typeof(ManualDeliveryKG).GetField("fetchList", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo ChoreField = typeof(ResearchCenter).GetField("chore", BindingFlags.Instance | BindingFlags.NonPublic);

        internal static List<ContinueResearchStation> Read(int worldId)
        {
            var result = new List<ContinueResearchStation>();
            var active = Research.Instance?.GetActiveResearch();
            var sourceCircuits = new HashSet<ushort>(Components.Batteries.Items.Where(item => item != null).Select(item => item.CircuitID));
            sourceCircuits.UnionWith(Components.Generators.Items.Where(item => item != null).Select(item => item.CircuitID));
            sourceCircuits.Remove(ushort.MaxValue);
            foreach (var building in Components.BuildingCompletes.Items)
            {
                if (building == null || (worldId >= 0 && building.GetMyWorldId() != worldId)) continue;
                var station = building.GetComponent<ResearchCenter>();
                if (station == null) continue;
                string type = station.GetResearchType();
                float required = 0, earned = 0;
                active?.tech?.costsByResearchTypeID.TryGetValue(type, out required);
                active?.progressInventory?.PointsByTypeID.TryGetValue(type, out earned);
                var operational = building.GetComponent<Operational>();
                var consumer = building.GetComponent<EnergyConsumer>();
                var storage = building.GetComponent<Storage>();
                var delivery = building.GetComponent<ManualDeliveryKG>();
                var item = new ContinueResearchStation
                {
                    Id = building.GetComponent<KPrefabID>()?.InstanceID ?? building.GetInstanceID(),
                    WorldId = building.GetMyWorldId(), PrefabId = building.Def.PrefabID, ResearchType = type,
                    Required = required > earned, RemainingPoints = Math.Max(0, required - earned),
                    Operational = operational?.IsOperational, Powered = consumer?.IsPowered,
                    CircuitId = consumer == null ? (int?)null : consumer.CircuitID == ushort.MaxValue ? -1 : consumer.CircuitID,
                    HasPowerSource = consumer == null ? (bool?)null : sourceCircuits.Contains(consumer.CircuitID),
                    StoredKg = Math.Round(storage?.MassStored() ?? 0f, 2),
                    MissingMaterial = storage != null && storage.MassStored() <= 0,
                    FailedFlags = operational?.Flags.Where(pair => !pair.Value).Select(pair => pair.Key.Name).OrderBy(name => name).ToArray() ?? new string[0],
                    DeliveryItem = delivery?.RequestedItemTag.Name,
                    DeliveryPaused = delivery?.IsPaused,
                    DeliveryMeetsRequirements = delivery == null ? (bool?)null : operational == null || operational.MeetsRequirements(delivery.operationalRequirement),
                    FetchPending = delivery == null || FetchListField == null ? (bool?)null : FetchListField.GetValue(delivery) != null,
                    ResearchChorePresent = ChoreField == null ? (bool?)null : ChoreField.GetValue(station) != null
                };
                result.Add(item);
            }
            return result;
        }
    }
}
