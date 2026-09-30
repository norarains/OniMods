using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    // Native Deliver returns the exact object before its identity is necessarily
    // registered in Components. Receipt observation never advances a Unity frame.
    internal static class PrintingRecruitmentReceipt
    {
        [ThreadStatic] private static Capture active;
        private static Receipt latest;
        private static int ownerGeneration;

        internal static Capture Begin(Telepad pod, MinionStartingStats candidate, string candidateId, int population)
        {
            return new Capture(pod, candidate, candidateId, population);
        }

        internal static void Delivered(MinionStartingStats candidate, GameObject result)
        {
            if (active != null && active.Generation == ownerGeneration && ReferenceEquals(active.Candidate, candidate))
                active.DeliveredObject = result;
        }

        internal static Dictionary<string, object> Status(Telepad pod)
        {
            var current = latest;
            if (current == null || !ReferenceEquals(current.Owner, Immigration.Instance)
                || current.Pod == null || (pod != null && current.Pod != pod)
                || !PlayerVisibility.Object(current.Pod.gameObject))
                return null;
            return current.Observe();
        }

        internal static void ClearOwner()
        {
            latest = null;
            active = null;
            ownerGeneration++;
        }

        internal sealed class Capture : IDisposable
        {
            internal readonly MinionStartingStats Candidate;
            internal readonly int Generation;
            internal GameObject DeliveredObject;
            private readonly Capture previous;
            private readonly Telepad pod;
            private readonly Immigration owner;
            private readonly int round;
            private readonly string candidateId;
            private readonly int population;

            internal Capture(Telepad pod, MinionStartingStats candidate, string candidateId, int population)
            {
                this.pod = pod;
                this.candidateId = candidateId;
                this.population = population;
                Candidate = candidate;
                owner = Immigration.Instance;
                round = (int)HeadlessPrintingChoices.NativeField(typeof(Immigration), "spawnIdx").GetValue(owner);
                Generation = ownerGeneration;
                previous = active;
                active = this;
            }

            internal Dictionary<string, object> Finish(bool accepted, string error = null)
            {
                bool consumed = ReferenceEquals(owner, Immigration.Instance)
                    && (int)HeadlessPrintingChoices.NativeField(typeof(Immigration), "spawnIdx").GetValue(owner) != round;
                var receipt = new Receipt(owner, pod, candidateId, population, DeliveredObject,
                    accepted || DeliveredObject != null ? (bool?)true : consumed ? null : (bool?)false, consumed, error);
                // A failed validation does not replace a previous delivery receipt.
                // EndImmigration must not clear this scope: it precedes Deliver.
                if (Generation == ownerGeneration && (accepted || consumed || DeliveredObject != null))
                    latest = receipt;
                return receipt.Observe();
            }

            public void Dispose()
            {
                if (ReferenceEquals(active, this))
                    active = Generation == ownerGeneration ? previous : null;
            }
        }

        private sealed class Receipt
        {
            internal readonly Immigration Owner;
            internal readonly Telepad Pod;
            private readonly string candidateId;
            private readonly int populationBefore;
            private readonly GameObject deliveredObject;
            private readonly bool? accepted;
            private readonly bool consumed;
            private readonly string error;
            private Dictionary<string, object> registeredDuplicant;

            internal Receipt(Immigration owner, Telepad pod, string candidateId, int population, GameObject result,
                bool? accepted, bool consumed, string error)
            {
                Owner = owner;
                Pod = pod;
                this.candidateId = candidateId;
                populationBefore = population;
                deliveredObject = result;
                this.accepted = accepted;
                this.consumed = consumed;
                this.error = error;
            }

            internal Dictionary<string, object> Observe()
            {
                var identity = deliveredObject == null ? null : deliveredObject.GetComponent<MinionIdentity>();
                int? id = deliveredObject == null ? null : deliveredObject.GetComponent<KPrefabID>()?.InstanceID;
                var duplicant = registeredDuplicant;
                if (duplicant == null && identity != null && id.HasValue && id.Value > 0)
                {
                    duplicant = new Dictionary<string, object>
                    {
                        ["id"] = id.Value,
                        ["name"] = ToolUtil.CleanName(deliveredObject.GetProperName()),
                        ["worldId"] = deliveredObject.GetMyWorldId()
                    };
                    if (Components.LiveMinionIdentities.Items.Any(item => ReferenceEquals(item, identity)))
                        registeredDuplicant = duplicant;
                }
                var result = new Dictionary<string, object>
                {
                    ["candidateId"] = candidateId,
                    ["deliveryAccepted"] = accepted,
                    ["roundConsumed"] = consumed,
                    ["arrivalStatus"] = registeredDuplicant != null ? "registered"
                        : deliveredObject != null ? "pending" : accepted == false ? "not_started" : "unconfirmed",
                    ["duplicant"] = duplicant,
                    ["populationBefore"] = populationBefore,
                    ["populationObserved"] = Components.LiveMinionIdentities.Items.Count(item => item != null),
                    ["worldId"] = Pod.gameObject.GetMyWorldId()
                };
                if (error != null) result["error"] = error;
                return result;
            }
        }
    }
}
