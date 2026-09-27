using System.Collections.Generic;
using System.Linq;

namespace OniMcp.Tools
{
    internal static class DigSkillObservation
    {
        internal static List<Dictionary<string, object>> Read(IEnumerable<int> cells, int worldId)
        {
            var requirements = cells.Where(Grid.IsValidCell).Distinct()
                .Select(cell => new { cell, perk = RequiredPerk(Grid.Element[cell].hardness) })
                .Where(item => item.perk != null).GroupBy(item => item.perk);
            return requirements.Select(group => new Dictionary<string, object> {
                ["perk"] = group.Key, ["cells"] = group.Count(),
                ["localSkillAvailable"] = MinionResume.AnyMinionHasPerk(group.Key, worldId),
                ["sample"] = group.Take(8).Select(item => new { x = Grid.CellColumn(item.cell), y = Grid.CellRow(item.cell) }).ToArray(),
                ["sameWorkerReachabilityVerified"] = false
            }).ToList();
        }

        private static string RequiredPerk(byte hardness)
        {
            var perks = Db.Get().SkillPerks;
            if (hardness == 255) return perks.CanDigUnobtanium.Id;
            if (hardness >= 251) return perks.CanDigRadioactiveMaterials.Id;
            if (hardness >= 200) return perks.CanDigSuperDuperHard.Id;
            if (hardness >= 150) return perks.CanDigNearlyImpenetrable.Id;
            if (hardness >= 50) return perks.CanDigVeryFirm.Id;
            return null;
        }
    }
}
