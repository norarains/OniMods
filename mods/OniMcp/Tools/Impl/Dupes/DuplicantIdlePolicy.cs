using System;

namespace OniMcp.Tools
{
    internal static class DuplicantIdlePolicy
    {
        internal static string Reason(bool canReceiveMove, int reachableCells, float stamina,
            bool lowCalories, string scheduleBlock)
        {
            if (!canReceiveMove) return "cannot_receive_move_command";
            if (reachableCells == 0) return "no_reachable_nearby_cells";
            if (stamina >= 0f && stamina < 15f) return "low_stamina";
            if (lowCalories) return "low_calories";
            // ONI's actual work group is Worktime. Retain Work for older callers.
            if (!string.IsNullOrWhiteSpace(scheduleBlock)
                && !string.Equals(scheduleBlock, "Worktime", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(scheduleBlock, "Work", StringComparison.OrdinalIgnoreCase))
                return "schedule_block_" + scheduleBlock;
            return "no_current_chore";
        }

        internal static bool NeedsAttention(string reason)
        {
            return !string.IsNullOrWhiteSpace(reason)
                && !reason.StartsWith("schedule_block_", StringComparison.Ordinal);
        }

        internal static string Next(string reason)
        {
            if (!NeedsAttention(reason))
                return "Scheduled non-work time; no rescue or priority change is indicated by idleness alone.";
            return reason == "no_current_chore"
                ? "Check personal priorities and available errands; use dupes_control domain=priority action=list or inspect nearby build/dig/supply errands."
                : "Inspect the returned reasonCode before issuing rescue or priority changes.";
        }
    }
}
