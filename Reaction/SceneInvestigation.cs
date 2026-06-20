using System;
using UnityEngine;
using Il2CppScheduleOne.Law;       // LawController
using Il2CppScheduleOne.Police;    // PoliceOfficer
using LooseEnds.Config;
using LooseEnds.Detection;

namespace LooseEnds.Reaction
{
    /// <summary>
    /// Scene investigation - investigate the body itself rather than purely hunting a player. The game's police API
    /// is fundamentally player-keyed (there is no "go to this position and look around" primitive), so this is a
    /// best-effort, clearly experimental synthesis: nudge law presence, find the officer nearest the body, and - when
    /// a killer is known - send that officer to body-search them (the closest in-engine "go investigate at the scene"
    /// behavior). Escalating mode reuses this and additionally applies the configured pursuit. Officer-routing exactness
    /// is the one item that needs a live Phase 0 probe; everything here is guarded so a wrong assumption is harmless.
    /// </summary>
    internal static class SceneInvestigation
    {
        internal static void Dispatch(CorpseRecord rec, Player killer)
        {
            // 1) raise law presence so officers are around to respond
            if (Preferences.RaiseLawIntensity)
            {
                try
                {
                    if (Singleton<LawController>.InstanceExists)
                        Singleton<LawController>.Instance.ChangeInternalIntensity(1f);
                }
                catch { /* best-effort */ }
            }

            // 2) route the nearest officer to investigate the scene (body-search the suspect, if known)
            bool routed = false;
            try
            {
                Vector3 pos = CorpsePosition(rec);
                float dist;
                PoliceOfficer nearest = PoliceOfficer.GetNearestOfficer(pos, out dist, true);
                if (nearest != null && killer != null)
                {
                    nearest.BeginBodySearch_Networked(killer.PlayerCode);
                    routed = true;
                }
            }
            catch (Exception e) { Core.Log?.Warning("[Reaction] scene officer routing failed: " + e.Message); }

            // 3) apply the configured pursuit on a known killer so a body is never simply ignored
            if (killer != null && Preferences.UseSetPursuitLevel)
            {
                try
                {
                    PlayerCrimeData crimeData = killer.CrimeData;
                    if (crimeData != null)
                    {
                        try { crimeData.RecordLastKnownPosition(true); } catch { }
                        PlayerCrimeData.EPursuitLevel level = (PlayerCrimeData.EPursuitLevel)Preferences.PursuitLevelInt;
                        if ((int)crimeData.CurrentPursuitLevel < (int)level)
                            crimeData.SetPursuitLevel(level);
                    }
                }
                catch (Exception e) { Core.Log?.Warning("[Reaction] scene SetPursuitLevel failed: " + e.Message); }
            }

            Core.Log?.Msg($"[Reaction] SCENE (experimental) corpse={rec.Id} killer={ReactionDispatcher.SafeCode(killer)} officerRouted={routed}");
        }

        private static Vector3 CorpsePosition(CorpseRecord rec)
        {
            try
            {
                NPC npc = rec.Npc;
                if (npc != null)
                {
                    NPCMovement mv = npc.Movement;
                    if (mv != null)
                    {
                        var drag = mv.RagdollDraggable;
                        if (drag != null && drag.transform != null) return drag.transform.position;
                    }
                    return npc.transform.position;
                }
            }
            catch { /* ignore */ }
            return Vector3.zero;
        }
    }
}
