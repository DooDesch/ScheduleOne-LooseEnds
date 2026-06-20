using System.Collections.Generic;
using UnityEngine;
using Il2CppScheduleOne.Dragging;     // Draggable
using Il2CppScheduleOne.Employees;    // Employee
using Il2CppScheduleOne.Police;       // PoliceOfficer
using Il2CppScheduleOne.Vision;       // VisionCone
using LooseEnds.Config;

namespace LooseEnds.Detection
{
    /// <summary>
    /// The witness test: for each undiscovered corpse, find a living NPC that can SEE it. The default path reuses the
    /// game's own per-NPC vision cone (VisionCone.IsPointWithinSight), which honors field-of-view and the occlusion
    /// layers - so a body behind a wall / in a dumpster / indoors / underwater is not seen. Cost is bounded by a
    /// distance pre-cull and a per-scan check cap (round-robin across observers), so it never spikes a frame.
    /// </summary>
    internal static class SightingScanner
    {
        private static int _observerCursor;

        /// <summary>
        /// Runs one throttled scan. Any corpse seen this pass is latched (Discovered) and appended to
        /// <paramref name="newlyDiscovered"/> for the dispatcher. Caller clears the list ownership.
        /// </summary>
        internal static void Scan(List<CorpseRecord> newlyDiscovered)
        {
            newlyDiscovered.Clear();

            var reg = NPCManager.NPCRegistry;
            if (reg == null) return;
            int regCount;
            try { regCount = reg.Count; } catch { return; }
            if (regCount == 0) return;

            bool requireLos = Preferences.RequireLineOfSight;
            bool useVisionRange = Preferences.UseVisionConeRange;
            float explicitRange = Preferences.DetectionRange;
            float cullSqr = useVisionRange
                ? Preferences.ObserverCullRadius * Preferences.ObserverCullRadius
                : explicitRange * explicitRange;
            int maxChecks = Preferences.MaxRaycastsPerScan;
            int checksUsed = 0;
#if DEBUG
            int corpsesScanned = 0;
#endif

            foreach (CorpseRecord rec in CorpseTracker.Records)
            {
                if (rec.Discovered) continue;
                NPC corpse = rec.Npc;
                if (corpse == null) continue;

                Vector3 corpsePos;
                try { corpsePos = CorpsePosition(rec); } catch { continue; }
#if DEBUG
                corpsesScanned++;
                float diagMinSqr = float.MaxValue;
                bool diagMinSight = false;
#endif

                for (int k = 0; k < regCount; k++)
                {
                    if (checksUsed >= maxChecks)
                    {
                        AdvanceCursor(checksUsed, regCount);
                        return;   // cap reached - resume next scan from where we left off
                    }

                    int idx = (_observerCursor + k) % regCount;
                    NPC obs = reg[idx];
                    if (obs == null) continue;
                    if (obs.GetInstanceID() == rec.Id) continue;

                    // observer must be alive and an enabled role
                    NPCHealth oh;
                    try { oh = obs.Health; } catch { continue; }
                    if (oh == null) continue;
                    try { if (oh.IsDead || oh.IsKnockedOut) continue; } catch { continue; }
                    if (!RoleEnabled(obs)) continue;

                    // cheap distance pre-cull (squared, no sqrt) before the expensive sight test
                    Vector3 obsPos;
                    try { obsPos = obs.transform.position; } catch { continue; }
                    float dsq = (obsPos - corpsePos).sqrMagnitude;
                    if (dsq > cullSqr) continue;

                    checksUsed++;
                    bool seen = CanSee(obs, corpsePos, requireLos);
#if DEBUG
                    if (dsq < diagMinSqr) { diagMinSqr = dsq; diagMinSight = seen; }
#endif
                    if (!seen) continue;

                    // discovered
                    rec.Discovered = true;
                    rec.FirstSeenTime = Time.time;
                    rec.Discoverer = obs;
                    newlyDiscovered.Add(rec);
#if DEBUG
                    Core.LogDebug($"[Witness] corpse {rec.Id} SEEN by observer {obs.GetInstanceID()} (killer={(rec.Killer != null ? rec.Killer.PlayerCode : "unknown")})");
#endif
                    break;   // stop scanning observers for this corpse
                }
#if DEBUG
                if (Preferences.LogWitnessScan && !rec.Discovered)
                    Core.LogDebug($"[Witness] corpse {rec.Id} pos=({corpsePos.x:F1},{corpsePos.y:F1},{corpsePos.z:F1}) nearestTestedObs={(diagMinSqr < float.MaxValue ? Mathf.Sqrt(diagMinSqr).ToString("F1") : "none")}m sight={diagMinSight}");
#endif
            }

            AdvanceCursor(checksUsed, regCount);
#if DEBUG
            if (Preferences.LogWitnessScan)
                Core.LogDebug($"[Witness] scan: corpses={CorpseTracker.Count} undiscovered-scanned={corpsesScanned} checks={checksUsed} found={newlyDiscovered.Count}");
#endif
        }

        private static void AdvanceCursor(int checksUsed, int regCount)
        {
            if (regCount <= 0) return;
            _observerCursor = (_observerCursor + Mathf.Max(1, checksUsed)) % regCount;
        }

        /// <summary>Where the body actually rests - prefer the ragdoll's draggable transform over the NPC root.</summary>
        private static Vector3 CorpsePosition(CorpseRecord rec)
        {
            NPC npc = rec.Npc;
            try
            {
                NPCMovement mv = npc.Movement;
                if (mv != null)
                {
                    Draggable drag = mv.RagdollDraggable;
                    if (drag != null)
                    {
                        Transform t = drag.transform;
                        if (t != null) return t.position + Vector3.up * 0.5f;
                    }
                }
            }
            catch { /* fall through */ }
            return npc.transform.position + Vector3.up * 0.5f;
        }

        private static bool CanSee(NPC observer, Vector3 corpsePos, bool requireLos)
        {
            if (!requireLos)
            {
                return true;   // already within the configured radius (pre-cull passed)
            }

            // Reuse the NPC's own vision cone: FOV + range + occlusion (the layers that make a hidden body unseen).
            try
            {
                NPCAwareness aw = observer.Awareness;
                VisionCone cone = aw != null ? aw.VisionCone : null;
                if (cone != null)
                {
                    return cone.IsPointWithinSight(corpsePos, false, null);
                }
            }
            catch { /* no usable cone -> cannot confirm sight */ }
            return false;
        }

        private static bool RoleEnabled(NPC obs)
        {
            try
            {
                if (obs.TryCast<PoliceOfficer>() != null) return Preferences.ReactPolice;
                if (obs.TryCast<Employee>() != null) return Preferences.ReactEmployees;
            }
            catch { /* treat as civilian */ }
            return Preferences.ReactCitizens;
        }
    }
}
