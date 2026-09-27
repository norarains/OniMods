using System;
using System.Collections.Generic;
using System.Linq;
using Klei.AI;

namespace OniMcp.Tools
{
    public static partial class DuplicantTools
    {
        private static readonly HashSet<string> EssentialAttributeIds = new HashSet<string> {
            "Athletics", "Strength", "Learning", "Digging", "Construction", "Machinery",
            "Caring", "Cooking", "Botanist", "Ranching", "Art", "SpaceNavigation"
        };

        internal static Dictionary<string, object> GetDupeDetail(MinionIdentity dupe, bool full = false)
        {
            var position = dupe.transform.GetPosition();
            var schedule = dupe.GetComponent<Schedulable>()?.GetSchedule();
            var result = full ? GetAttributeSummary(dupe, true) : GetCompactDupeDiagnosticDetails(dupe);
            result["id"] = dupe.GetComponent<KPrefabID>()?.InstanceID ?? -1;
            result["name"] = dupe.GetProperName();
            result["position"] = new { x = Math.Round(position.x, 2), y = Math.Round(position.y, 2) };
            result["worldId"] = dupe.GetMyWorldId();
            result["schedule"] = schedule?.name;
            result["currentScheduleBlock"] = schedule?.GetCurrentScheduleBlock()?.GroupId;
            var chore = dupe.GetComponent<ChoreConsumer>()?.choreDriver?.GetCurrentChore();
            result["currentChore"] = chore?.choreType?.Id ?? chore?.GetType().Name;
            result["needs"] = full ? GetNeedsSummary(dupe)["amounts"] : KeyNeedValues(dupe).ToDictionary();
            var attributes = dupe.GetAttributes();
            result["morale"] = attributes?.Get("QualityOfLife")?.GetTotalValue();
            result["moraleExpectation"] = attributes?.Get("QualityOfLifeExpectation")?.GetTotalValue();
            var resume = dupe.GetComponent<MinionResume>();
            result["advancedResearch"] = new {
                mastered = resume?.HasMasteredSkill("Researching1") ?? false,
                canLearn = resume != null && resume.CanMasterSkill(resume.GetSkillMasteryConditions("Researching1"))
            };
            if (!full) result.Remove("nonZeroAmounts");
            return result;
        }
    }
}
