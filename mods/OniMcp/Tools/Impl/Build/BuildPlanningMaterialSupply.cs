using System.Collections.Generic;

namespace OniMcp.Tools
{
    public static partial class BuildPlanningTools
    {
        private static int PickupableWorldId(Pickupable pickupable)
        {
            int cell = ToolUtil.PickupableCell(pickupable);
            if (Grid.IsValidCell(cell) && Grid.IsWorldValidCell(cell))
                return Grid.WorldIdx[cell];
            return pickupable.GetMyWorldId();
        }

        // Native inventory membership already includes loose and stored fetchables.
        // Read their current quantities once; never add loose mass to its cached total.
        private static float AvailableAmount(int worldId, Tag tag)
        {
            if (!tag.IsValid || ClusterManager.Instance == null)
                return 0f;
            var world = ClusterManager.Instance.GetWorld(worldId >= 0 ? worldId : ClusterManager.Instance.activeWorldId);
            var inventory = world?.worldInventory;
            var pickupables = inventory?.GetPickupables(tag, includeRelatedWorlds: false);
            if (pickupables == null)
                return 0f;

            float amount = 0f;
            var counted = new HashSet<Pickupable>();
            foreach (var pickupable in pickupables)
            {
                if (pickupable == null || !counted.Add(pickupable)
                    || !PlayerVisibility.Object(pickupable.gameObject)
                    || PickupableWorldId(pickupable) != world.id)
                    continue;
                var identity = pickupable.KPrefabID ?? pickupable.GetComponent<KPrefabID>();
                if (identity == null || identity.HasTag(GameTags.StoredPrivate)
                    || !inventory.IsReachable(pickupable))
                    continue;

                float quantity = ToolUtil.SafeFloat(pickupable.TotalAmount);
                if (quantity > 0f)
                    amount += quantity;
            }
            return ToolUtil.SafeFloat(amount);
        }
    }
}
