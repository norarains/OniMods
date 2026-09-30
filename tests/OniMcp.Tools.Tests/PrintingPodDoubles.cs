using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OniMcp.Core;
using UnityEngine;

// Native boundaries follow installed Telepad.OnAcceptDelivery and
// ImmigrantScreen.Initialize/OnProceed. Production owns all guards and selection.
internal interface ITelepadDeliverable { }
internal interface ITelepadDeliverableContainer
{
    void SelectDeliverable();
    void DeselectDeliverable();
    GameObject GetGameObject();
}

internal sealed partial class Immigration
{
    internal static Immigration Instance;
    internal bool ImmigrantsAvailable = true;
    internal int EndCalls;
    internal float Remaining;
    public float GetTimeRemaining() => Remaining;
    public int EndImmigration()
    {
        EndCalls++; ImmigrantsAvailable = false; Remaining = 1800; spawnIdx++;
        PrintingUiNativeHooks.Invoke(typeof(OniMcp.PrintingChoiceRoundEndedPatch), "Postfix");
        return spawnIdx;
    }
    internal void OnPrefabInit() => PrintingUiNativeHooks.Invoke(typeof(OniMcp.PrintingChoiceOwnerInitializedPatch), "Postfix");
}

internal sealed class Telepad : KMonoBehaviour
{
    internal int AcceptCalls;
    internal ITelepadDeliverable LastAccepted;
    internal bool ThrowAfterEnd { get; set; }
    public void OnAcceptDelivery(ITelepadDeliverable value)
    {
        AcceptCalls++;
        LastAccepted = value;
        Immigration.Instance.EndImmigration();
        if (ThrowAfterEnd) throw new InvalidOperationException("native delivery failed after consuming round");
        if (value is MinionStartingStats stats)
        {
            var dupe = new MinionIdentity();
            dupe.gameObject.Cell = gameObject.Cell;
            dupe.gameObject.name = stats.Name;
            dupe.gameObject.Components[typeof(KPrefabID)] = new KPrefabID { InstanceID = 901 };
            Components.LiveMinionIdentities.Items.Add(dupe);
        }
    }
}

internal class CharacterSelectionController
{
    private CharacterContainer containerPrefab = new CharacterContainer();
    private CarePackageContainer carePackageContainerPrefab = new CarePackageContainer();
    private GameObject containerParent = new GameObject();
    protected int numberOfDuplicantOptions = 3;
    protected int numberOfCarePackageOptions = 0;
    public Action<ITelepadDeliverable> OnReplacedEvent;
    internal int DisabledProceedCalls;
    internal bool HasPrefabs => containerPrefab != null && carePackageContainerPrefab != null && containerParent != null;
    internal int[] OptionCounts => new[] { numberOfDuplicantOptions, numberOfCarePackageOptions };
    private void DisableProceedButton() { DisabledProceedCalls++; }
    protected int selectableCount = 1;
    protected List<ITelepadDeliverable> selectedDeliverables = new List<ITelepadDeliverable>();
    protected List<ITelepadDeliverableContainer> containers = new List<ITelepadDeliverableContainer>();
    internal List<ITelepadDeliverableContainer> Offers => containers;
    internal List<ITelepadDeliverable> Selected => selectedDeliverables;
    internal int AddCalls, RemoveCalls;
    internal Action OnAdded { get; set; }
    internal int ChoiceCount { set { selectableCount = value; } }
    public bool IsStarterMinion { get; set; }
    public void AddDeliverable(ITelepadDeliverable value)
    {
        AddCalls++;
        if (!selectedDeliverables.Contains(value) && selectedDeliverables.Count < selectableCount)
            selectedDeliverables.Add(value);
        OnAdded?.Invoke();
    }
    public void RemoveDeliverable(ITelepadDeliverable value)
    { RemoveCalls++; selectedDeliverables.Remove(value); }
    protected virtual void OnProceed() { throw new NotSupportedException(); }
    protected virtual void InitializeContainers() { throw new NotSupportedException("Headless flow must not initialize UI containers"); }
}

internal sealed class ImmigrantScreen : CharacterSelectionController
{
    internal static ImmigrantScreen instance;
    private Telepad telepad;
    public Telepad Telepad => telepad;
    internal int InitializeCalls, GenerateCalls, ProceedCalls, ShowCalls, CleanupCalls;
    internal bool IsVisible;
    internal Func<IEnumerable<ITelepadDeliverableContainer>> Generate;
    internal void Bind(Telepad value) { telepad = value; }
    public void Show(bool show = true) { ShowCalls++; IsVisible = show; }
    public static void InitializeImmigrantScreen(Telepad value)
    { instance.Initialize(value); instance.Show(); }
    private void Initialize(Telepad value)
    {
        InitializeCalls++;
        if (containers.Count == 0)
        {
            GenerateCalls++;
            containers.AddRange(Generate?.Invoke() ?? Enumerable.Empty<ITelepadDeliverableContainer>());
            selectedDeliverables = new List<ITelepadDeliverable>();
        }
        foreach (var candidate in containers.OfType<CharacterContainer>())
            candidate.SetReshufflingState(false);
        telepad = value;
    }
    protected override void OnProceed()
    {
        ProceedCalls++;
        telepad.OnAcceptDelivery(selectedDeliverables[0]);
        Show(false);
        foreach (var offer in containers)
        {
            UnityEngine.Object.Destroy(offer.GetGameObject());
            CleanupCalls++;
        }
        containers.Clear();
    }
}

internal sealed class CharacterContainer : ITelepadDeliverableContainer
{
    private CharacterSelectionController controller;
    internal void SetController(CharacterSelectionController value) { controller = value; }
    internal CharacterSelectionController Controller => controller;
    internal readonly GameObject gameObject = new GameObject();
    internal MinionStartingStats Stats { get; set; }
    internal bool Reshuffling = true;
    internal int StopCalls;
    internal int RenderCalls;
    public void SetMinion(MinionStartingStats value) { Stats = value; RenderCalls++; }
    public void StopAllCoroutines() { StopCalls++; }
    public void SetReshufflingState(bool enable) { Reshuffling = enable; }
    public GameObject GetGameObject() => gameObject;
    public void SelectDeliverable() => ImmigrantScreen.instance.AddDeliverable(Stats);
    public void DeselectDeliverable() => ImmigrantScreen.instance.RemoveDeliverable(Stats);
    public void GenerateCharacter(bool starter, string aptitude = null)
    { throw new NotSupportedException("Headless flow must not generate through a UI card"); }
}

internal sealed class CarePackageContainer : ITelepadDeliverableContainer
{
    private CharacterSelectionController controller;
    private CarePackageInfo info;
    private KToggle selectButton = new KToggle();
    internal void SetController(CharacterSelectionController value) { controller = value; }
    internal CharacterSelectionController Controller => controller;
    internal KToggle SelectButton => selectButton;
    internal int AnimatorCalls, InfoTextCalls;
    private void SetAnimator() { AnimatorCalls++; }
    private void SetInfoText() { InfoTextCalls++; }
    internal bool Reshuffling = true;
    public void SetReshufflingState(bool enable) { Reshuffling = enable; }
    internal sealed class CarePackageInstanceData : ITelepadDeliverable
    {
        public CarePackageInfo info;
        public string facadeID;
    }
    internal readonly GameObject gameObject = new GameObject();
    internal int StopCalls;
    public void StopAllCoroutines() { StopCalls++; }
    public CarePackageInstanceData carePackageInstanceData;
    internal CarePackageInfo Info => info;
    internal CarePackageInstanceData Delivery => carePackageInstanceData;
    internal CarePackageContainer(CarePackageInfo info)
    { this.info = info; carePackageInstanceData = new CarePackageInstanceData { info = info, facadeID = info?.facadeID }; }
    public CarePackageContainer() : this(null) { }
    public GameObject GetGameObject() => gameObject;
    public void SelectDeliverable() => ImmigrantScreen.instance.AddDeliverable(carePackageInstanceData);
    public void DeselectDeliverable() => ImmigrantScreen.instance.RemoveDeliverable(carePackageInstanceData);
    private void GenerateCharacter(bool starter)
    { throw new NotSupportedException("Headless flow must not generate through a UI card"); }
}

internal sealed class CarePackageInfo : ITelepadDeliverable
{
    public string id;
    public float quantity;
    public string facadeID;
    public Func<bool> requirement;
}
internal sealed partial class MinionStartingStats : ITelepadDeliverable
{
    public string Name;
    public bool IsValid = true;
    public Personality personality;
    public Klei.AI.Trait stressTrait;
    public Klei.AI.Trait joyTrait;
    public readonly List<Klei.AI.Trait> Traits = new List<Klei.AI.Trait>();
    public readonly Dictionary<string, int> StartingLevels = new Dictionary<string, int>();
    public readonly Dictionary<Database.SkillGroup, float> skillAptitudes = new Dictionary<Database.SkillGroup, float>();
}
internal sealed partial class Personality
{
    public string Id;
    public Tag model;
}
namespace Klei.AI
{
    internal sealed class Trait { public string Id; public string Name; }
}
namespace Database
{
    internal sealed class SkillGroup { public string Id; public string Name; }
}
internal sealed class Operational { internal bool IsOperational { get; set; } = true; }
internal sealed class CreatureBrain { }
internal sealed class SpeedControlScreen
{
    internal static SpeedControlScreen Instance = new SpeedControlScreen();
    internal bool IsPaused = true;
}
internal sealed partial class ClusterManager { internal int activeWorldId; }
internal sealed partial class MinionIdentity { internal readonly GameObject gameObject = new GameObject(); }
internal static partial class Components
{
    internal static readonly ComponentCollection<Telepad> Telepads = new ComponentCollection<Telepad>();
    internal static readonly ComponentCollection<MinionIdentity> LiveMinionIdentities = new ComponentCollection<MinionIdentity>();
}
internal static partial class Assets
{
    internal static readonly Dictionary<string, GameObject> PrintingPrefabs = new Dictionary<string, GameObject>();
    internal static GameObject GetPrefab(string id) => PrintingPrefabs.TryGetValue(id, out var prefab) ? prefab : null;
}
namespace UnityEngine
{
    internal static class Time { internal static float timeScale = 0; }
    public sealed partial class GameObject { internal bool PrintingOfferDestroyed; }
    public static partial class Object
    {
        internal static int PrintingDestroyCalls;
        internal static void Destroy(GameObject target) { PrintingDestroyCalls++; target.PrintingOfferDestroyed = true; }
    }
}

namespace OniMcp.Tools
{
    public static partial class FacilitySideScreenTools
    {
        internal static CallToolResult TestPrintingClaim(JObject args, Telepad pod)
            => ClaimPrintingReward(args, pod, TelepadInfo(pod, false));
        internal static Dictionary<string, object> TestPrintingRewards(Telepad pod) => PrintingRewardStatus(pod);
        internal static Dictionary<string, object> TestPrintingCandidates(Telepad pod) => PrintingCandidateStatus(pod);
        internal static CallToolResult TestPrintingPrepare(JObject args, Telepad pod)
            => PreparePrintingChoices(args, pod, TelepadInfo(pod, false));
        internal static CallToolResult TestPrintingRecruit(JObject args, Telepad pod)
            => RecruitPrintingCandidate(args, pod, TelepadInfo(pod, false));
        internal static GameObject TestPrintingTarget(JObject args) => FindTelepadTarget(args);
        private static GameObject FindBuildingTarget(JObject args, Func<GameObject, bool> predicate)
        {
            int? id = ToolUtil.GetInt(args, "id");
            return id.HasValue ? Components.Telepads.Items.Select(pod => pod.gameObject)
                .FirstOrDefault(go => go.GetInstanceID() == id.Value && predicate(go)) : null;
        }
        private static bool HasRectInput(JObject args) => args["x1"] != null || args["y1"] != null;
        private static bool MatchesQuery(Dictionary<string, object> info, string query)
            => info.Values.Any(value => value != null && value.ToString().IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
        private static CallToolResult JsonResult(Dictionary<string, object> value)
            => CallToolResult.Text(JsonConvert.SerializeObject(value));
        private static Dictionary<string, object> TelepadInfo(Telepad pod, bool includeVictory)
            => new Dictionary<string, object> { ["id"] = pod.gameObject.GetInstanceID(), ["available"] = Immigration.Instance?.ImmigrantsAvailable };
        private static Dictionary<string, object> TargetInfo(GameObject target)
            => new Dictionary<string, object> { ["id"] = target.GetInstanceID(), ["worldId"] = target.GetMyWorldId() };
    }
}
