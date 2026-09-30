using System;
using System.Collections.Generic;
using System.Reflection;

namespace OniMcp.Tools
{
    // Use the game's current offer objects and normal selection/delivery lifecycle.
    // Reading never initializes offers; preparation is an explicit write.
    internal static class PrintingPodNativeChoices
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        internal static List<ITelepadDeliverableContainer> Containers
        {
            get
            {
                var screen = ImmigrantScreen.instance;
                if (screen == null) return new List<ITelepadDeliverableContainer>();
                var field = RequiredField("containers");
                return field.GetValue(screen) as List<ITelepadDeliverableContainer>
                    ?? new List<ITelepadDeliverableContainer>();
            }
        }

        internal static void ValidateAccess()
        {
            if (ImmigrantScreen.instance == null)
                throw new InvalidOperationException("Native immigrant screen is not available.");
            RequiredField("containers");
            RequiredField("selectedDeliverables");
            var count = RequiredField("selectableCount").GetValue(ImmigrantScreen.instance);
            if (!(count is int) || (int)count != 1)
                throw new InvalidOperationException("Only native single-choice Printing Pod rounds are supported.");
            RequiredMethod("OnProceed", Type.EmptyTypes);
        }

        internal static void Initialize(Telepad telepad)
        {
            HeadlessPrintingChoices.Prepare(telepad);
        }

        internal static void ValidateAcceptance(Telepad telepad, ITelepadDeliverable deliverable)
        {
            if (HeadlessPrintingChoices.HasUiBatch && !HeadlessPrintingChoices.HasPrepared(telepad))
                throw new InvalidOperationException("Current MCP choices belong to a different Printing Pod.");
            bool cached = HeadlessPrintingChoices.HasPrepared(telepad)
                && (HeadlessPrintingChoices.Candidates(telepad).Exists(stats => ReferenceEquals(stats, deliverable))
                    || HeadlessPrintingChoices.Packages(telepad).Exists(package => ReferenceEquals(package, deliverable)));
            var screen = ImmigrantScreen.instance;
            bool native = screen != null && !screen.IsStarterMinion && screen.Telepad == telepad
                && Containers.Exists(container => HeadlessPrintingChoices.IsCurrentNativeCard(container)
                    && ((container is CharacterContainer character && ReferenceEquals(character.Stats, deliverable))
                        || (container is CarePackageContainer package && ReferenceEquals(package.carePackageInstanceData, deliverable))));
            if (!cached && !native)
                throw new InvalidOperationException("The requested deliverable is not a current choice for this Printing Pod.");
            if (native) ValidateAccess();
        }

        internal static void Accept(Telepad telepad, ITelepadDeliverable deliverable)
        {
            ValidateAcceptance(telepad, deliverable);
            var screen = ImmigrantScreen.instance;
            bool native = screen != null && screen.Telepad == telepad
                && Containers.Exists(container => (container is CharacterContainer character && ReferenceEquals(character.Stats, deliverable))
                    || (container is CarePackageContainer package && ReferenceEquals(package.carePackageInstanceData, deliverable)));
            if (!native)
            {
                // A headless offer is delivered by the same native telepad lifecycle.
                // No selection screen, render hierarchy or mouse interaction is needed.
                try { telepad.OnAcceptDelivery(deliverable); }
                finally
                {
                    // EndImmigration runs before Deliver. Also clean up if Deliver
                    // throws after consuming the round; never retry that delivery.
                    if (Immigration.Instance != null && !Immigration.Instance.ImmigrantsAvailable)
                    {
                        HeadlessPrintingChoices.Clear();
                        ClearPendingNativeCards();
                    }
                }
                return;
            }
            var selected = RequiredField("selectedDeliverables").GetValue(screen) as List<ITelepadDeliverable>;
            if (selected == null)
                throw new InvalidOperationException("Native selection state is not initialized.");
            foreach (var previous in selected.ToArray()) screen.RemoveDeliverable(previous);
            screen.AddDeliverable(deliverable);
            if (selected.Count != 1 || !ReferenceEquals(selected[0], deliverable))
                throw new InvalidOperationException("Native selection callbacks changed the requested choice; delivery was not started.");
            // OnProceed owns delivery, cooldown, container destruction and UI cleanup.
            // Do not call EndImmigration again: OnAcceptDelivery already does so.
            try { RequiredMethod("OnProceed", Type.EmptyTypes).Invoke(screen, null); }
            finally
            {
                if (Immigration.Instance != null && !Immigration.Instance.ImmigrantsAvailable)
                {
                    HeadlessPrintingChoices.Clear();
                    ClearPendingNativeCards();
                }
            }
        }

        internal static void ClearPendingNativeCards()
        {
            var screen = ImmigrantScreen.instance;
            if (screen == null || screen.IsStarterMinion) return;
            var cards = Containers;
            if (cards.Count == 0) return;
            foreach (var card in cards)
            {
                // A player may have opened the GUI immediately before a headless
                // claim. Cancel delayed generation before clearing the shared round.
                if (card is CharacterContainer character) character.StopAllCoroutines();
                if (card is CarePackageContainer package) package.StopAllCoroutines();
                UnityEngine.Object.Destroy(card.GetGameObject());
            }
            cards.Clear();
            var selected = RequiredField("selectedDeliverables").GetValue(screen) as List<ITelepadDeliverable>;
            selected?.Clear();
            screen.Show(false);
        }

        private static FieldInfo RequiredField(string name)
        {
            var field = typeof(CharacterSelectionController).GetField(name, Members);
            if (field == null) throw new InvalidOperationException("Unsupported native Printing Pod field: " + name);
            return field;
        }

        private static MethodInfo RequiredMethod(string name, Type[] parameters)
        {
            var method = typeof(ImmigrantScreen).GetMethod(name, Members, null, parameters, null);
            if (method == null) throw new InvalidOperationException("Unsupported native Printing Pod method: " + name);
            return method;
        }
    }
}
