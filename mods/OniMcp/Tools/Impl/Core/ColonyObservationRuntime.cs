using System;
using System.Collections.Generic;
using OniMcp.Server;

namespace OniMcp.Tools
{
    internal static class ColonyObservationRuntime
    {
        private static int generation = -1;
        private static readonly Dictionary<int, GameContinueMonitor> monitors = new Dictionary<int, GameContinueMonitor>();
        internal static GameContinueMonitor Monitor(int worldId, bool refreshOrders = false)
        {
            int current = GameContextLifecycle.CaptureGeneration();
            if (current != generation) { monitors.Clear(); generation = current; }
            if (refreshOrders || !monitors.TryGetValue(worldId, out var monitor))
                monitors[worldId] = new GameContinueMonitor(worldId);
            return monitors[worldId];
        }
        internal static ContinueSample Read(int worldId) => Monitor(worldId, true).Read(0, true);
    }
}
