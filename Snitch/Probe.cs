#if SNITCH
using System.Text;
using UnityEngine;
using Il2CppScheduleOne.PlayerScripts;   // Player, PlayerCrimeData
using Snitch.Api;                 // Profiler, Panel, StateSnapshot
using LooseEnds.Detection;        // CorpseTracker, SightingScanner, CorpseRecord
using LooseEnds.Weight;           // CorpseWeight

namespace LooseEnds.Profiling
{
    /// <summary>
    /// DEBUG-only Snitch instrumentation for LooseEnds. Wraps the witness/corpse pipeline's counts + live state in a
    /// Snitch panel ("Loose Ends") - the in-game replacement for the old on-screen HUD and the F7/F8 debug hotkeys.
    /// Everything is a no-op when the Snitch host is absent. Compiled only when SNITCH is defined (Debug + EnableSnitch);
    /// excluded from Release. See Workspace/build/Snitch.props.
    ///
    /// Note: the panel id "LooseEnds" matches the existing "LooseEnds." counter prefix so the counters group under it.
    /// The "Give Arsenal" action calls <see cref="LooseEnds.Debugging.DebugArsenal.Give"/> (DEBUG-only; SNITCH is only
    /// defined in Debug, so both symbols are present together).
    /// </summary>
    internal static class SnitchProbe
    {
        // Reused per-poll so the main-thread Text provider does not allocate a fresh builder each frame.
        private static readonly StringBuilder _sb = new StringBuilder(256);

        public static void Register()
        {
            Panel p = Profiler.RegisterPanel("LooseEnds", "Loose Ends");

            // ----- counters (grouped under the panel via the "LooseEnds." prefix) -----
            p.Counter("Corpses", () => CorpseTracker.Count, "bodies");
            p.Counter("ScanChecks", () => SightingScanner.LastChecks, "checks");
            p.Counter("WeightedBodies", () => CorpseWeight.ActiveCount, "carried");

            // ----- state distribution -----
            p.State(() =>
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

            // ----- free-text readout (the same live state the old WitnessHud showed) -----
            p.Text(BuildStateText);

            // ----- action (replaces the old F8 hotkey) -----
            p.Action("Give Arsenal", () => LooseEnds.Debugging.DebugArsenal.Give());

            // ----- this panel's own log channel (Profiler.Log("LooseEnds", ...) lines show here) -----
            p.Log();

            // ----- ablation lever ('snitch ablate looseends.scan') -----
            // Causal cost of the whole sighting pipeline (the distance pre-cull loop + the engine's native vision-cone
            // / occlusion raycasts). Host-only by construction - the witness system already runs only on the server,
            // so this is a no-op on a connected client. Run it with at least one corpse present (the only costly state).
            Profiler.RegisterAblationLever("looseends.scan",
                apply: () => SightingScanner.ScanDisabled = true,
                restore: () => SightingScanner.ScanDisabled = false);
        }

        /// <summary>
        /// Multi-line readout of the witness system's live state - activation, tracked corpses (count / nearest
        /// distance / discovered / calling / dispatched / killer), your pursuit level, NPC count and whether you are
        /// carrying a body. Mirrors what the old on-screen HUD displayed. Polled on the main thread.
        /// </summary>
        private static string BuildStateText()
        {
            _sb.Clear();
            _sb.Append("active: ").Append(Core.WitnessStatus).Append('\n');

            int npcCount = -1;
            try { var reg = NPCManager.NPCRegistry; if (reg != null) npcCount = reg.Count; } catch { }

            // Player position + pursuit level.
            Vector3 ppos = Vector3.zero;
            bool havePlayer = false;
            string pursuit = "?";
            try
            {
                Player local = Player.Local;
                if (local != null)
                {
                    ppos = local.transform.position;
                    havePlayer = true;
                    PlayerCrimeData cd = local.CrimeData;
                    if (cd != null) pursuit = PursuitName((int)cd.CurrentPursuitLevel);
                }
            }
            catch { }

            // Nearest tracked corpse: distance + its killer.
            int corpseCount = CorpseTracker.Count;
            float nearestDist = -1f;
            string nearestKiller = "-";
            foreach (CorpseRecord rec in CorpseTracker.Records)
            {
                if (rec.Npc == null) continue;
                float d;
                try { d = Vector3.Distance(rec.Npc.transform.position, ppos); } catch { continue; }
                if (!havePlayer) d = -1f;
                if (nearestDist < 0f || (d >= 0f && d < nearestDist))
                {
                    nearestDist = d;
                    try { nearestKiller = KillerLabel(rec.Killer); } catch { nearestKiller = "?"; }
                }
            }

            _sb.Append("corpses: ").Append(corpseCount)
               .Append("  NPCs: ").Append(npcCount)
               .Append("  carrying: ").Append(CorpseWeight.ActiveCount > 0 ? "YES" : "no").Append('\n');
            _sb.Append("nearest corpse: ")
               .Append(nearestDist >= 0f ? nearestDist.ToString("F0") + "m" : "-")
               .Append("  killer: ").Append(nearestKiller).Append('\n');
            _sb.Append("pursuit: ").Append(pursuit);

            return _sb.ToString();
        }

        /// <summary>
        /// Human-readable killer label. Shows "you" for the local player (its raw PlayerCode is the host SteamID, or "0"
        /// when the game was launched without Steam - both opaque), the player name otherwise, or "unknown" if null.
        /// </summary>
        private static string KillerLabel(Player killer)
        {
            if (killer == null) return "unknown";
            try
            {
                Player local = Player.Local;
                if (local != null && local.GetInstanceID() == killer.GetInstanceID()) return "you";
            }
            catch { }
            try { string n = killer.PlayerName; if (!string.IsNullOrEmpty(n)) return n; } catch { }
            try { return killer.PlayerCode; } catch { return "?"; }
        }

        private static string PursuitName(int level)
        {
            switch (level)
            {
                case 0: return "None";
                case 1: return "Investigating";
                case 2: return "Arresting";
                case 3: return "NonLethal";
                case 4: return "Lethal";
                default: return level.ToString();
            }
        }
    }
}
#endif
