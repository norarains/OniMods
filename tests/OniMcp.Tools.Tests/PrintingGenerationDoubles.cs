using System;
using System.Collections.Generic;
using UnityEngine;
using System.Reflection;

// Script only native random/stat factories. The production cache decides when
// generation is allowed and retains/reuses the actual generated object identities.
internal sealed partial class Immigration
{
    private int spawnIdx;
    internal int SpawnIndex { get => spawnIdx; set => spawnIdx = value; }
    internal int PackageCalls;
    internal readonly Queue<CarePackageInfo> PackageResults = new Queue<CarePackageInfo>();
    public CarePackageInfo RandomCarePackage()
    {
        PackageCalls++;
        return PackageResults.Count > 0 ? PackageResults.Dequeue() : new CarePackageInfo {
            id = "Algae", quantity = 100, facadeID = "native-facade", requirement = () => true
        };
    }
}
internal sealed partial class MinionStartingStats
{
    internal static int Generated;
    internal static bool LastWasStarter;
    internal static bool ThrowOnGeneration;
    internal static List<Tag> LastModels;
    internal static readonly Queue<MinionStartingStats> GeneratedResults = new Queue<MinionStartingStats>();
    internal MinionStartingStats() { }
    public MinionStartingStats(List<Tag> models, bool isStarter, string guaranteedAptitudeID = null)
    {
        Generated++;
        if (ThrowOnGeneration) throw new InvalidOperationException("scripted native generation failure");
        LastModels = new List<Tag>(models);
        LastWasStarter = isStarter;
        var scripted = GeneratedResults.Count > 0 ? GeneratedResults.Dequeue() : null;
        Name = scripted?.Name ?? "Generated " + Generated;
        personality = scripted?.personality ?? new Personality { Id = Name, model = GameTags.Minions.Models.Standard };
        IsValid = scripted?.IsValid ?? true;
        if (scripted != null)
        {
            foreach (var attribute in scripted.StartingLevels) StartingLevels.Add(attribute.Key, attribute.Value);
            Traits.AddRange(scripted.Traits);
        }
    }
}
internal sealed partial class Personality { public string requiredDlcId { get; set; } = ""; }
internal sealed partial class MinionIdentity { public string personalityResourceId { get; set; } = "Existing"; }
internal sealed partial class Game
{
    internal static readonly HashSet<string> DisabledDlc = new HashSet<string>();
    internal static bool IsDlcActiveForCurrentSave(string id) => !DisabledDlc.Contains(id ?? "");
}
internal static partial class GameTags
{
    internal static class Minions
    {
        internal static class Models
        {
            internal static readonly Tag Standard = new Tag("Minion");
            internal static readonly Tag Bionic = new Tag("BionicMinion");
        }
    }
}
namespace UnityEngine
{
    internal static class Random
    {
        internal static int Calls;
        internal static int Next;
        internal static int Range(int min, int max) { Calls++; return Math.Max(min, Math.Min(max - 1, Next)); }
    }
}
namespace Klei.CustomSettings
{
    internal sealed class SettingConfig { }
    internal sealed class SettingLevel { public string id { get; set; } = "Enabled"; }
}
internal static class CustomGameSettingConfigs
{
    internal static readonly Klei.CustomSettings.SettingConfig CarePackages = new Klei.CustomSettings.SettingConfig();
}
internal sealed class CustomGameSettings
{
    internal static CustomGameSettings Instance = new CustomGameSettings();
    internal readonly Klei.CustomSettings.SettingLevel CarePackageSetting = new Klei.CustomSettings.SettingLevel();
    internal Klei.CustomSettings.SettingLevel GetCurrentQualitySetting(Klei.CustomSettings.SettingConfig config) => CarePackageSetting;
}
namespace Database
{
    internal sealed class EquippableFacadeResource { public string Id { get; set; } public string DefID { get; set; } }
    internal sealed class EquippableFacades
    {
        public readonly List<EquippableFacadeResource> resources = new List<EquippableFacadeResource>();
    }
}
internal sealed partial class Db
{
    internal static readonly Database.EquippableFacades Facades = new Database.EquippableFacades();
    public static Database.EquippableFacades GetEquippableFacades() => Facades;
}
internal static class PrintingNativeRandomExtensions
{
    public static T GetRandom<T>(this List<T> values) => values[UnityEngine.Random.Range(0, values.Count)];
}
namespace HarmonyLib
{
    internal static class Harmony
    {
        internal static Patches CurrentPatches { get; set; }
        internal static Patches GetPatchInfo(MethodBase method) => CurrentPatches;
    }
    internal sealed class Patches
    {
        internal readonly List<Patch> Prefixes = new List<Patch>();
        internal readonly List<Patch> Postfixes = new List<Patch>();
        internal readonly List<Patch> Transpilers = new List<Patch>();
        internal readonly List<Patch> Finalizers = new List<Patch>();
    }
    internal sealed class Patch { internal MethodInfo PatchMethod { get; set; } }
}
