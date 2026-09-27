using System;
using System.Collections.Generic;
using Klei.AI;

namespace OniMcp.Tools
{
    public static partial class DuplicantTools
    {
        private static Dictionary<string, object> MoraleInfo(MinionIdentity dupe, Database.Skill learning = null)
        {
            if (dupe == null) return null;
            double? morale = Db.Get().Attributes.QualityOfLife.Lookup(dupe)?.GetTotalValue();
            double? expectation = Db.Get().Attributes.QualityOfLifeExpectation.Lookup(dupe)?.GetTotalValue();
            var result = new Dictionary<string, object> { ["current"] = morale, ["expectation"] = expectation };
            if (learning != null)
            {
                bool interested = dupe.GetComponent<MinionResume>()?.HasSkillAptitude(learning) == true;
                result["projectedExpectation"] = expectation + learning.GetMoraleExpectation();
                result["projectedMorale"] = morale + (interested ? 1 : 0);
            }
            return result;
        }
    }
}
