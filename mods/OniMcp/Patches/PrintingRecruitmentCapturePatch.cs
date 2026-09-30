using HarmonyLib;
using OniMcp.Tools;
using UnityEngine;

namespace OniMcp
{
    [HarmonyPatch(typeof(MinionStartingStats), "Deliver", new[] { typeof(Vector3) })]
    internal static class PrintingRecruitmentCapturePatch
    {
        private static void Postfix(MinionStartingStats __instance, GameObject __result)
        {
            PrintingRecruitmentReceipt.Delivered(__instance, __result);
        }
    }

    [HarmonyPatch(typeof(Immigration), "OnPrefabInit")]
    internal static class PrintingRecruitmentOwnerPatch
    {
        private static void Postfix()
        {
            PrintingRecruitmentReceipt.ClearOwner();
        }
    }
}
