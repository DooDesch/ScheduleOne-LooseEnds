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
        }
    }
}
#endif
