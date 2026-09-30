using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Klei.CustomSettings;

namespace OniMcp.Tools
{
    // Offers belong to one native immigration round. Both MCP and the ordinary
    // GUI consume these same objects; neither surface can reroll a prepared batch.
    internal static class HeadlessPrintingChoices
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static Batch batch;
        private static readonly HashSet<ITelepadDeliverableContainer> endedCards = new HashSet<ITelepadDeliverableContainer>();

        private sealed class Batch
        {
            internal Immigration Owner;
            internal int Round;
            internal Telepad Pod;
            internal bool Ready;
            internal bool Headless;
            internal string Failure;
            internal readonly List<MinionStartingStats> Candidates = new List<MinionStartingStats>();
            internal readonly List<CarePackageContainer.CarePackageInstanceData> Packages = new List<CarePackageContainer.CarePackageInstanceData>();
        }

        internal static bool HasPrepared(Telepad pod)
        {
            var current = Current();
            return current != null && current.Ready && current.Pod == pod;
        }

        internal static void Prepare(Telepad pod)
        {
            if (pod == null || Immigration.Instance == null || !Immigration.Instance.ImmigrantsAvailable)
                throw new InvalidOperationException("printing_choices_unavailable: No native Printing Pod round is available.");

            var current = Current();
            if (current != null)
            {
                if (current.Pod != pod)
                    throw new InvalidOperationException("printing_choices_wrong_pod: Prepared choices belong to another Printing Pod.");
                if (!current.Ready)
                    throw new InvalidOperationException(current.Failure ?? "printing_choices_pending: Native choices are not ready.");
                return;
            }

            var offers = NativeContainers();
            var screen = ImmigrantScreen.instance;
            if (offers.Count > 0 && (screen == null || screen.Telepad != pod))
                throw new InvalidOperationException("printing_choices_wrong_pod: Existing native choices belong to another Printing Pod.");

            var next = new Batch { Owner = Immigration.Instance, Round = Round(Immigration.Instance), Pod = pod };
            if (offers.Count > 0)
            {
                if (offers.Any(offer => endedCards.Contains(offer)))
                    throw new InvalidOperationException("printing_choices_stale: Native cards from an ended round remain; they cannot become new offers.");
                foreach (var offer in offers)
                {
                    var candidate = offer as CharacterContainer;
                    var package = offer as CarePackageContainer;
                    if (candidate != null && candidate.Stats != null)
                        next.Candidates.Add(candidate.Stats);
                    else if (package != null && package.Info != null && package.carePackageInstanceData != null)
                        next.Packages.Add(package.carePackageInstanceData);
                    else
                        throw new InvalidOperationException("printing_choices_pending: Existing native cards are still generating; no choices were replaced.");
                }
                next.Ready = true;
                batch = next;
                return;
            }

            ValidateGenerationCompatibility();
            ValidateUiBridge();
            next.Headless = true;
            // Keep a failed generation associated with its round as well. Retrying
            // after a constructor/package failure must not become another random roll.
            batch = next;
            try
            {
                int packageCount = CustomGameSettings.Instance.GetCurrentQualitySetting(CustomGameSettingConfigs.CarePackages).id == "Enabled"
                    ? (UnityEngine.Random.Range(0, 101) <= 70 ? 1 : 2) : 0;
                int candidateCount = packageCount == 0 ? 3 : 4 - packageCount;
                for (int i = 0; i < candidateCount; i++)
                    next.Candidates.Add(GenerateCandidate(next.Candidates));
                for (int i = 0; i < packageCount; i++)
                    next.Packages.Add(GeneratePackage(next.Packages));
                next.Ready = true;
            }
            catch (Exception ex)
            {
                next.Failure = "printing_choices_generation_failed: Native generation failed for this round; it will not reroll. " + ex.Message;
                throw new InvalidOperationException(next.Failure, ex);
            }
        }

        internal static List<MinionStartingStats> Candidates(Telepad pod)
        {
            return HasPrepared(pod) ? new List<MinionStartingStats>(batch.Candidates) : new List<MinionStartingStats>();
        }

        internal static List<CarePackageContainer.CarePackageInstanceData> Packages(Telepad pod)
        {
            return HasPrepared(pod) ? new List<CarePackageContainer.CarePackageInstanceData>(batch.Packages)
                : new List<CarePackageContainer.CarePackageInstanceData>();
        }

        internal static void Clear()
        {
            batch = null;
            // EndImmigration precedes Deliver. Even a failing delivery must not
            // let surviving old GUI objects be adopted as next round's offers.
            endedCards.Clear();
            try
            {
                foreach (var card in NativeContainers()) endedCards.Add(card);
            }
            catch (InvalidOperationException)
            {
                // Lifecycle hooks must not break native play on unsupported UI
                // metadata. Prepare will report the same missing field safely.
            }
        }

        internal static bool HasUiBatch => Current()?.Ready == true;
        internal static bool IsCurrentNativeCard(ITelepadDeliverableContainer card)
        {
            return card != null && !endedCards.Contains(card);
        }
        internal static bool ShouldBridgeUi => HasUiBatch && batch.Headless;
        internal static int UiCandidateCount => HasUiBatch ? batch.Candidates.Count : 0;
        internal static int UiPackageCount => HasUiBatch ? batch.Packages.Count : 0;

        internal static MinionStartingStats CandidateForCard(CharacterContainer card)
        {
            if (!ShouldBridgeUi || !IsImmigrantCard(card, typeof(CharacterContainer))) return null;
            int index = NativeContainers().OfType<CharacterContainer>().ToList().IndexOf(card);
            return index >= 0 && index < batch.Candidates.Count ? batch.Candidates[index] : null;
        }

        internal static CarePackageContainer.CarePackageInstanceData PackageForCard(CarePackageContainer card)
        {
            if (!ShouldBridgeUi || !IsImmigrantCard(card, typeof(CarePackageContainer))) return null;
            int index = NativeContainers().OfType<CarePackageContainer>().ToList().IndexOf(card);
            return index >= 0 && index < batch.Packages.Count ? batch.Packages[index] : null;
        }

        internal static List<ITelepadDeliverableContainer> NativeContainers()
        {
            var screen = ImmigrantScreen.instance;
            if (screen == null) return new List<ITelepadDeliverableContainer>();
            return NativeField(typeof(CharacterSelectionController), "containers").GetValue(screen) as List<ITelepadDeliverableContainer>
                ?? new List<ITelepadDeliverableContainer>();
        }

        internal static FieldInfo NativeField(Type type, string name)
        {
            var field = type.GetField(name, Members);
            if (field == null) throw new InvalidOperationException("Unsupported native Printing Pod field: " + type.Name + "." + name);
            return field;
        }

        internal static MethodInfo NativeMethod(Type type, string name, params Type[] arguments)
        {
            var method = type.GetMethod(name, Members, null, arguments, null);
            if (method == null) throw new InvalidOperationException("Unsupported native Printing Pod method: " + type.Name + "." + name);
            return method;
        }

        private static void ValidateUiBridge()
        {
            var type = typeof(CharacterSelectionController);
            foreach (string field in new[] { "containerPrefab", "carePackageContainerPrefab", "containerParent", "containers",
                "selectedDeliverables", "numberOfDuplicantOptions", "numberOfCarePackageOptions" })
                NativeField(type, field);
            NativeMethod(type, "DisableProceedButton");
            NativeField(typeof(CharacterContainer), "controller");
            NativeMethod(typeof(CharacterContainer), "SetMinion", typeof(MinionStartingStats));
            NativeField(typeof(CarePackageContainer), "controller");
            NativeField(typeof(CarePackageContainer), "info");
            NativeField(typeof(CarePackageContainer), "selectButton");
            NativeMethod(typeof(CarePackageContainer), "SetAnimator");
            NativeMethod(typeof(CarePackageContainer), "SetInfoText");

            var screen = ImmigrantScreen.instance;
            if (screen == null) return;
            bool packagesEnabled = CustomGameSettings.Instance.GetCurrentQualitySetting(CustomGameSettingConfigs.CarePackages).id == "Enabled";
            if (NativeField(type, "containerPrefab").GetValue(screen) as CharacterContainer == null
                || NativeField(type, "containerParent").GetValue(screen) as UnityEngine.GameObject == null
                || (packagesEnabled && NativeField(type, "carePackageContainerPrefab").GetValue(screen) as CarePackageContainer == null))
                throw new InvalidOperationException("printing_choices_ui_unavailable: Native card prefabs are missing; headless generation was not started.");
        }

        private static Batch Current()
        {
            if (batch != null && (Immigration.Instance == null || !Immigration.Instance.ImmigrantsAvailable
                || !ReferenceEquals(batch.Owner, Immigration.Instance) || batch.Round != Round(Immigration.Instance) || batch.Pod == null))
                batch = null;
            if (batch != null && batch.Ready && !batch.Headless)
            {
                var offers = NativeContainers();
                if (offers.OfType<CharacterContainer>().Count() != batch.Candidates.Count
                    || offers.OfType<CarePackageContainer>().Count() != batch.Packages.Count
                    || batch.Candidates.Any(candidate => !offers.OfType<CharacterContainer>().Any(card => ReferenceEquals(card.Stats, candidate)))
                    || batch.Packages.Any(package => !offers.OfType<CarePackageContainer>().Any(card => ReferenceEquals(card.carePackageInstanceData, package))))
                    batch = null;
            }
            return batch;
        }

        private static int Round(Immigration immigration)
        {
            return (int)NativeField(typeof(Immigration), "spawnIdx").GetValue(immigration);
        }

        private static bool IsImmigrantCard(object card, Type type)
        {
            var owner = NativeField(type, "controller").GetValue(card) as CharacterSelectionController;
            return owner != null && owner == ImmigrantScreen.instance && !owner.IsStarterMinion;
        }

        private static MinionStartingStats GenerateCandidate(List<MinionStartingStats> earlier)
        {
            MinionStartingStats candidate;
            int attempts = 0;
            do
            {
                // Exactly the native initial offer constructor: no guaranteed
                // aptitude/trait, starter-only restriction or debug generation.
                candidate = new MinionStartingStats(new List<Tag> {
                    GameTags.Minions.Models.Standard, GameTags.Minions.Models.Bionic
                }, false);
                attempts++;
            }
            while (InvalidCandidate(candidate, earlier) && attempts < 20);
            return candidate;
        }

        private static bool InvalidCandidate(MinionStartingStats candidate, List<MinionStartingStats> earlier)
        {
            if (earlier.Any(item => item.IsValid && item.personality.Id == candidate.personality.Id)) return true;
            if (Game.Instance != null && !Game.IsDlcActiveForCurrentSave(candidate.personality.requiredDlcId)) return true;
            return candidate.personality.model != GameTags.Minions.Models.Bionic
                && Components.LiveMinionIdentities.Items.Any(item => item.personalityResourceId == candidate.personality.Id);
        }

        private static CarePackageContainer.CarePackageInstanceData GeneratePackage(List<CarePackageContainer.CarePackageInstanceData> earlier)
        {
            CarePackageInfo info;
            int attempts = 0;
            do
            {
                info = Immigration.Instance.RandomCarePackage();
                attempts++;
            }
            while (earlier.Any(item => ReferenceEquals(item.info, info)) && attempts < 20);

            return new CarePackageContainer.CarePackageInstanceData {
                info = info,
                facadeID = info.facadeID == "SELECTRANDOM"
                    ? Db.GetEquippableFacades().resources.FindAll(item => item.DefID == info.id).GetRandom().Id : info.facadeID
            };
        }

        private static void ValidateGenerationCompatibility()
        {
            var methods = new[] {
                NativeMethod(typeof(CharacterSelectionController), "InitializeContainers"),
                NativeMethod(typeof(CharacterContainer), "GenerateCharacter", typeof(bool), typeof(string)),
                NativeMethod(typeof(CarePackageContainer), "GenerateCharacter", typeof(bool))
            };
            foreach (var method in methods)
            {
                var patches = Harmony.GetPatchInfo(method);
                if (patches == null) continue;
                if (patches.Prefixes.Concat(patches.Postfixes).Concat(patches.Transpilers).Concat(patches.Finalizers)
                    .Any(patch => patch.PatchMethod.DeclaringType?.Assembly != typeof(HeadlessPrintingChoices).Assembly))
                    throw new InvalidOperationException("printing_choices_mod_conflict: Another mod patches native offer generation; headless preparation cannot safely replace it (" + method.DeclaringType.Name + "." + method.Name + ").");
            }
        }
    }
}
