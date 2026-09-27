using Newtonsoft.Json.Linq;
using UnityEngine;

namespace OniMcp.Tools
{
    public static partial class UserMenuActionTools
    {
        private static string ExecuteMenuAction(GameObject go, ActionSpec spec, JObject args, bool dryRun)
        {
            if (spec.ActionKey != "toggle_move_pickupable")
                return dryRun ? null : InvokeSpec(go, spec);

            int? destinationId = ToolUtil.GetInt(args, "destinationId");
            if (!destinationId.HasValue)
                return "destinationId is required for pickupable delivery; no interactive move tool is opened";
            var destination = FindTarget(new JObject { ["id"] = destinationId.Value });
            if (destination == null)
                return "Delivery destination not found";
            var movable = go.GetComponent<Movable>();
            int cell = Grid.PosToCell(destination);
            if (movable == null || !Grid.IsValidCell(cell) || !movable.CanMoveTo(cell)
                || !ToolUtil.VisibleCellAllowed(cell, true))
                return "Delivery destination must be a visible, non-solid cell in the target's world";
            var identity = go.GetComponent<KPrefabID>();
            if (identity == null || identity.HasTag(GameTags.Stored)
                || (movable.tagRequiredForMove != Tag.Invalid && !identity.HasTag(movable.tagRequiredForMove)))
                return "Pickupable is stored or lacks the native prerequisite for moving";
            if (movable.IsMarkedForMove)
                return movable.StorageProxy != null && Grid.PosToCell(movable.StorageProxy) == cell
                    ? null : "Pickupable already has a different delivery order; cancel_move_pickupable before retargeting";
            // The native order API creates normal delivery work without activating
            // MoveToLocationTool or depending on the current player selection.
            if (!dryRun)
                movable.MoveToLocation(cell);
            return null;
        }
    }
}
