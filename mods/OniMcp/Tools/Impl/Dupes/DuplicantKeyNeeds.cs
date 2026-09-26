using System;
using System.Collections.Generic;

namespace OniMcp.Tools
{
    public static partial class DuplicantTools
    {
        private static KeyNeeds KeyNeedValues(MinionIdentity dupe)
        {
            // Amounts lives on the Modifiers object; it is not a Unity component.
            return new KeyNeeds
            {
                Stamina = DupeAmountUtil.AmountValueByName(dupe, "Stamina", -1f),
                Calories = DupeAmountUtil.AmountValueByName(dupe, "Calories", -1f),
                Stress = DupeAmountUtil.StressValue(dupe, -1f),
                Bladder = DupeAmountUtil.AmountValueByName(dupe, "Bladder", -1f),
                Breath = DupeAmountUtil.AmountValueByName(dupe, "Breath", -1f),
                BodyTemperature = DupeAmountUtil.AmountValueByName(dupe, "Temperature", -1f)
            };
        }

        private sealed class KeyNeeds
        {
            public float Stamina = -1f;
            public float Calories = -1f;
            public float Stress = -1f;
            public float Bladder = -1f;
            public float Breath = -1f;
            public float BodyTemperature = -1f;

            public bool HasData => Stamina >= 0f || Calories >= 0f || Stress >= 0f
                || Bladder >= 0f || Breath >= 0f || BodyTemperature >= 0f;
            // ONI stores calories, while the player-facing food unit is kcal.
            public bool LowCalories => Calories >= 0f && Calories < 1000000f;
            public bool LowBreath => Breath >= 0f && Breath < 35f;

            public Dictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object>
                {
                    ["stamina"] = RoundOrNull(Stamina),
                    ["calories"] = RoundOrNull(Calories),
                    ["caloriesKcal"] = Calories < 0f ? null : (object)Math.Round(Calories / 1000f, 2),
                    ["stress"] = RoundOrNull(Stress),
                    ["bladder"] = RoundOrNull(Bladder),
                    ["breath"] = RoundOrNull(Breath),
                    ["bodyTemperature"] = RoundOrNull(BodyTemperature)
                };
            }

            private static object RoundOrNull(float value)
            {
                return value < 0f ? null : (object)Math.Round(value, 2);
            }
        }
    }
}
