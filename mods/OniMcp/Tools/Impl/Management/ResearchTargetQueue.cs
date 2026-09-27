using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using KSerialization;

namespace OniMcp.Tools
{
    // Persist requested targets separately: vanilla saves only its current target's
    // prerequisite graph. Only the FIFO head is submitted to vanilla research.
    [SerializationConfig(MemberSerialization.OptIn)]
    public sealed class ResearchTargetQueue : KMonoBehaviour, ISim200ms
    {
        [Serialize] private List<string> targets = new List<string>();
        private bool restoring = true;
        private bool updating;
        private static int nativeDepth;
        internal static ResearchTargetQueue Current => SaveGame.Instance?.GetComponent<ResearchTargetQueue>();

        internal string[] Read()
        {
            Synchronize();
            return Snapshot();
        }

        internal string[] Snapshot() => targets.ToArray();

        internal void Select(Tech tech, bool replace)
        {
            Synchronize();
            if (replace) targets.Clear();
            else if (targets.Count == 0 && Research.Instance.GetTargetResearch() is TechInstance native)
                targets.Add(native.tech.Id);
            if (!targets.Contains(tech.Id)) targets.Add(tech.Id);
            if (replace || Research.Instance.GetActiveResearch() == null) StartHead();
        }

        internal void Clear()
        {
            targets.Clear();
            Research.Instance.SetActiveResearch(null, true);
        }

        public void Sim200ms(float dt) => Synchronize();

        internal void ExternalSelection()
        {
            if (!updating && !restoring && nativeDepth == 0) targets.Clear();
        }

        internal void Synchronize()
        {
            var research = Research.Instance;
            if (updating || research == null || Db.Get()?.Techs == null) return;
            bool removed = false;
            while (targets.Count > 0)
            {
                var tech = Db.Get().Techs.TryGet(targets[0]);
                if (tech != null && research.Get(tech)?.IsComplete() != true) break;
                targets.RemoveAt(0);
                removed = true;
            }
            if (restoring)
            {
                restoring = false;
                if (targets.Count > 0) StartHead();
                return;
            }
            if (removed)
            {
                if (targets.Count > 0) StartHead();
                return;
            }
            // Native UI selection/cancellation wins. Never reassert an old MCP
            // target after the player has changed the research screen.
            if (targets.Count > 0 && research.GetTargetResearch()?.tech?.Id != targets[0])
                targets.Clear();
        }

        private void StartHead()
        {
            if (targets.Count == 0) return;
            updating = true;
            try { Research.Instance.SetActiveResearch(Db.Get().Techs.TryGet(targets[0]), true); }
            finally { updating = false; }
        }

        [HarmonyPatch(typeof(SaveGame), "OnPrefabInit")]
        private static class Attach
        {
            private static void Postfix(SaveGame __instance) => __instance.gameObject.AddOrGet<ResearchTargetQueue>();
        }

        [HarmonyPatch(typeof(Research), "OnSpawn")]
        private static class Restore
        {
            private static void Postfix() => Current?.Synchronize();
        }

        [HarmonyPatch(typeof(Research), "GetNextTech")]
        private static class Advance
        {
            private static void Prefix() => nativeDepth++;
            private static void Postfix() => Current?.Synchronize();
            private static void Finalizer() => nativeDepth--;
        }

        [HarmonyPatch(typeof(Research), "OnDeserialized")]
        private static class Loading
        {
            private static void Prefix() => nativeDepth++;
            private static void Finalizer() => nativeDepth--;
        }

        [HarmonyPatch(typeof(Research), "SetActiveResearch")]
        private static class PlayerSelection
        {
            private static void Prefix() => Current?.ExternalSelection();
        }

        [HarmonyPatch(typeof(Research), "CancelResearch")]
        private static class PlayerCancellation
        {
            private static void Prefix() => Current?.ExternalSelection();
        }

        [HarmonyPatch(typeof(Research), "OnSerializing")]
        private static class BeforeSave
        {
            private static void Prefix() => Current?.Synchronize();
        }
    }
}
