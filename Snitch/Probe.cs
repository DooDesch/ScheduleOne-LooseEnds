#if SNITCH
using Snitch.Api;                 // Profiler, StateSnapshot
using LooseEnds.Detection;        // CorpseTracker, SightingScanner, CorpseRecord
using LooseEnds.Weight;           // CorpseWeight

namespace LooseEnds.Profiling
{
    /// <summary>
    /// DEBUG-only Snitch instrumentation for LooseEnds. Exposes the witness/corpse pipeline's key counts/state
    /// to the Snitch profiler (no-op when the Snitch host is absent). Compiled only when SNITCH is defined
    /// (Debug + EnableSnitch); excluded from Release. See Workspace/build/Snitch.props.
    /// </summary>
    internal static class SnitchProbe
    {
        public static void Register()
        {
            Profiler.RegisterCounter("LooseEnds.Corpses", () => CorpseTracker.Count, "bodies");
            Profiler.RegisterCounter("LooseEnds.ScanChecks", () => SightingScanner.LastChecks, "checks");
            Profiler.RegisterCounter("LooseEnds.WeightedBodies", () => CorpseWeight.ActiveCount, "carried");

            Profiler.RegisterStateProvider("LooseEnds", () =>
            {
                int discovered = 0, calling = 0, dispatched = 0;
                foreach (CorpseRecord rec in CorpseTracker.Records)
                {
                    if (rec.Discovered) discovered++;
                    if (rec.Calling) calling++;
                    if (rec.Dispatched) dispatched++;
                }
                return new StateSnapshot { Title = "Corpses" }
                    .Add("tracked", CorpseTracker.Count)
                    .Add("discovered", discovered)
                    .Add("calling", calling)
                    .Add("dispatched", dispatched)
                    .Add("carried", CorpseWeight.ActiveCount);
            });

            // ----- ablation lever ('snitch ablate looseends.scan') -----
            // Causal cost of the whole sighting pipeline (the distance pre-cull loop + the engine's native vision-cone
            // / occlusion raycasts). Host-only by construction - the witness system already runs only on the server,
            // so this is a no-op on a connected client. Run it with at least one corpse present (the only costly state).
            Profiler.RegisterAblationLever("looseends.scan",
                apply: () => SightingScanner.ScanDisabled = true,
                restore: () => SightingScanner.ScanDisabled = false);
        }
    }
}
#endif
