using System;
using UnityEngine;
using Il2CppScheduleOne.Law;       // LawManager, LawController, DeadlyAssault, Crime
using Il2CppScheduleOne.Vision;    // VisionCone
using LooseEnds.Config;
using LooseEnds.Detection;
using LooseEnds.Networking;

namespace LooseEnds.Reaction
{
    /// <summary>
    /// Turns a discovered corpse into a police response, honoring the configured realism. Hunt mode applies heat to
    /// the killer-player (the in-game "Investigating" pursuit) and dispatches officers via the game's own networked
    /// LawManager call - so on the host it replicates to all clients with no custom RPC. Scene mode (Step 7) routes
    /// officers to the body. All writes assume the caller already gated on the network authority (server/host).
    /// </summary>
    internal static class ReactionDispatcher
    {
        private static float _lastDispatchTime = -9999f;

        /// <summary>
        /// Attempt the response for a corpse whose reaction delay has elapsed. Respects the global anti-spam cooldown
        /// (retries next tick if blocked) and the "killer must also be seen" gate. Sets Dispatched on success.
        /// </summary>
        internal static void TryDispatch(CorpseRecord rec)
        {
            if (rec == null || rec.Dispatched) return;

            float now = Time.time;
            if (now - _lastDispatchTime < Preferences.ResponseCooldownSeconds) return;   // anti-spam; retry later

            if (Preferences.RequirePlayerAlsoSeen && !DiscovererSeesAnyPlayer(rec.Discoverer))
            {
                return;   // body seen but the killer is not in view yet - wait (retries each tick)
            }

            Player killer = ResolveKiller(rec);
            ResponseMode mode = Preferences.Mode;

            try
            {
                switch (mode)
                {
                    case ResponseMode.Hunt:
                        DoHunt(rec, killer);
                        break;
                    case ResponseMode.Scene:
                        DoScene(rec, killer);
                        break;
                    case ResponseMode.Escalating:
                        // Scene now; the escalation-to-Hunt-on-arrival is handled in Step 7's scene controller.
                        DoScene(rec, killer);
                        break;
                }
            }
            catch (Exception e)
            {
                Core.Log?.Warning("[Reaction] dispatch failed: " + e.Message);
            }

            rec.Dispatched = true;
            rec.DispatchedTime = now;
            _lastDispatchTime = now;
        }

        private static Player ResolveKiller(CorpseRecord rec)
        {
            if (rec.Killer != null) return rec.Killer;
            // Unknown killer (environmental / NPC-vs-NPC). In single-player, optionally blame the local player so Hunt
            // still has a target; in co-op never blame a random player (the caller falls back to Scene).
            if (Preferences.AttributeUnknownToLocalPlayer && !Net.IsCoop())
            {
                try { return Player.Local; } catch { return null; }
            }
            return null;
        }

        /// <summary>Apply heat to the killer-player: the "Investigating" pursuit + a networked police dispatch.</summary>
        private static void DoHunt(CorpseRecord rec, Player killer)
        {
            if (killer == null)
            {
                // No one to hunt - degrade to a scene response so officers still investigate the body.
                DoScene(rec, null);
                return;
            }

            // Mirror the game's own citizen murder-report sequence (CallPoliceBehaviour.FinalizeCall): record the
            // suspect's position so dispatched officers know where to search and the search timer resets, raise the
            // pursuit level (never downgrading an already-higher one), and log the crime on the player's record.
            try
            {
                PlayerCrimeData crimeData = killer.CrimeData;
                if (crimeData != null)
                {
                    try { crimeData.RecordLastKnownPosition(true); } catch { }

                    if (Preferences.UseSetPursuitLevel)
                    {
                        PlayerCrimeData.EPursuitLevel level = (PlayerCrimeData.EPursuitLevel)Preferences.PursuitLevelInt;
                        if ((int)crimeData.CurrentPursuitLevel < (int)level)
                        {
                            crimeData.SetPursuitLevel(level);
                        }
                    }

                    try { crimeData.AddCrime(new DeadlyAssault(), 1); } catch { }
                }
            }
            catch (Exception e) { Core.Log?.Warning("[Reaction] Hunt crime-data update failed: " + e.Message); }

            if (Preferences.UsePoliceCalled)
            {
                try
                {
                    if (Singleton<LawManager>.InstanceExists)
                    {
                        Singleton<LawManager>.Instance.PoliceCalled(killer, new DeadlyAssault());
                    }
                }
                catch (Exception e) { Core.Log?.Warning("[Reaction] PoliceCalled failed: " + e.Message); }
            }

            if (Preferences.RaiseLawIntensity)
            {
                try
                {
                    if (Singleton<LawController>.InstanceExists)
                    {
                        Singleton<LawController>.Instance.ChangeInternalIntensity(1f);
                    }
                }
                catch { /* best-effort */ }
            }

            Core.Log?.Msg($"[Reaction] HUNT corpse={rec.Id} killer={SafeCode(killer)} pursuit={Preferences.PursuitLevelInt}");
        }

        /// <summary>
        /// Scene investigation - route officers to the body. Fully built in Step 7; until then it degrades to a Hunt
        /// on the nearest player (so a response still happens) and is clearly logged as experimental.
        /// </summary>
        private static void DoScene(CorpseRecord rec, Player killer)
        {
            Reaction.SceneInvestigation.Dispatch(rec, killer);
        }

        private static bool DiscovererSeesAnyPlayer(NPC discoverer)
        {
            if (discoverer == null) return false;
            try
            {
                NPCAwareness aw = discoverer.Awareness;
                VisionCone cone = aw != null ? aw.VisionCone : null;
                if (cone == null) return false;
                Player local = Player.Local;
                if (local != null && cone.IsPlayerVisible(local)) return true;
            }
            catch { /* ignore */ }
            return false;
        }

        internal static string SafeCode(Player p)
        {
            try { return p != null ? p.PlayerCode : "null"; } catch { return "?"; }
        }
    }
}
