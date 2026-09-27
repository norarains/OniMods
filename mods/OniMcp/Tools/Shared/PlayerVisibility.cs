using UnityEngine;

namespace OniMcp.Tools
{
    // Player discovery, independent of the camera, overlay, or selected UI object.
    // Apply before matching identities, reading properties, or counting candidates.
    internal static class PlayerVisibility
    {
        internal static bool Cell(int cell)
        {
            if (!Grid.IsValidCell(cell) || !Grid.IsWorldValidCell(cell) || !Grid.IsVisible(cell))
                return false;
            return ClusterManager.Instance?.GetWorld(Grid.WorldIdx[cell])?.IsDiscovered ?? false;
        }

        internal static bool Object(GameObject go)
        {
            return Object(go, 0);
        }

        private static bool Object(GameObject go, int depth)
        {
            if (go == null || depth > 8)
                return false;

            // ONI records discovery after the first exposed occupied cell. This
            // survives reburial; do not infer discovery from an explored anchor.
            var uncoverable = go.GetComponent<Uncoverable>();
            if (uncoverable != null && !uncoverable.IsUncovered)
                return false;

            var pickupable = go.GetComponent<Pickupable>();
            if (pickupable != null && pickupable.storage != null)
                return Object(pickupable.storage.gameObject, depth + 1);

            int cell = pickupable != null ? ToolUtil.PickupableCell(pickupable) : Grid.PosToCell(go);
            if (!Cell(cell))
                return false;

            // Loose resources trapped inside natural terrain are not inventory
            // the player can inspect. Stored items remain readable via their owner.
            return pickupable == null || !Grid.Solid[cell] || Grid.Foundation[cell];
        }

        internal static GameObject Known(GameObject go)
        {
            return Object(go) ? go : null;
        }
    }
}
