using System;

namespace OniMcp.Tools
{
    // A frame-driven lease. The client is never responsible for the final pause.
    internal sealed class BoundedGameWindow
    {
        private readonly Func<double> now;
        private readonly System.Action pause;
        private readonly Func<string> observe;
        private readonly Action<string, string> complete;
        private double deadline, nextSample;
        internal bool Running { get; private set; }

        internal BoundedGameWindow(Func<double> now, System.Action pause, Func<string> observe, Action<string, string> complete)
        {
            this.now = now; this.pause = pause; this.observe = observe; this.complete = complete;
        }

        internal void Start(double seconds, System.Action resume)
        {
            if (Running) throw new InvalidOperationException("Window already running");
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 1 || seconds > GameContinuePolicy.MaxSeconds)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            deadline = now() + seconds;
            nextSample = now();
            Running = true;
            try { resume(); }
            catch (Exception ex) { Stop("resume_failed", ex.Message); }
        }

        internal void Tick(string interruption = null)
        {
            if (!Running) return;
            try
            {
                if (interruption != null) { Stop(interruption); return; }
                double time = now();
                if (time >= deadline) { Stop("window_complete"); return; }
                if (time < nextSample) return;
                nextSample = time + 0.5;
                string reason = observe();
                if (reason != null) Stop(reason);
            }
            catch (Exception ex) { Stop("monitor_failed", ex.Message); }
        }

        internal void Stop(string reason, string error = null)
        {
            if (!Running) return;
            Running = false;
            try { pause(); }
            catch (Exception ex) { reason = "pause_failed"; error = ex.Message; }
            complete(reason, error);
        }
    }
}
