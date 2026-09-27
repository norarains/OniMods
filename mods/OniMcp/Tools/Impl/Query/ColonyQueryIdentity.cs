using System.Collections.Generic;
using UnityEngine;

namespace OniMcp.Tools
{
    internal static partial class ColonyQuery
    {
        private static IEnumerable<FactRow> IndexedBuildingRows(FactPredicate predicate, bool warnings)
        {
            var ids = FactQueryIdentity.Candidates(predicate);
            if (ids == null) return null;
            var objects = new List<GameObject>();
            foreach (int id in ids)
            {
                var identity = KPrefabIDTracker.Get().GetInstance(id);
                // Retain the unindexed universe for objects without a KPrefabID,
                // unregistered/transitional objects, and older saves/mods.
                if (identity == null) return null;
                var go = identity.gameObject;
                if (go.GetComponent<BuildingComplete>() != null || go.GetComponent<Geyser>() != null
                    || go.GetComponent<Constructable>() != null) objects.Add(go);
            }
            return BuildingRows(warnings, objects);
        }
    }
}
