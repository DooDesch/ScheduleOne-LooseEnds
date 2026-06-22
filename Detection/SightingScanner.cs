using System.Collections.Generic;
using UnityEngine;
using Il2CppScheduleOne.Dragging;     // Draggable
using Il2CppScheduleOne.Employees;    // Employee
using Il2CppScheduleOne.Police;       // PoliceOfficer
using Il2CppScheduleOne.Vision;       // VisionCone
using LooseEnds.Config;
#if SNITCH
using Snitch.Api;                 // Profiler sub-section timing + ablation flag (Debug + EnableSnitch only)
#endif

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

        // Sight-test count of the last scan pass. Always tracked (one int assignment) so the optional Snitch
        // profiler can surface it as a counter without coupling to DEBUG.
        internal static int LastChecks;
#if SNITCH
        // Snitch ablation lever 'looseends.scan': when set, Scan() returns immediately so the profiler can measure
        // the causal cost of the whole sighting pipeline (incl. the engine's native vision raycasts). Debug only.
        internal static bool ScanDisabled;
#endif
#if DEBUG
        // Extra live stats surfaced by the debug HUD (last scan pass).
        internal static int LastFound;
        internal static float LastScanTime;
#endif

        /// <summary>
        /// Runs one throttled scan. Any corpse seen this pass is latched (Discovered) and appended to
        /// <paramref name="newlyDiscovered"/> for the dispatcher. Caller clears the list ownership.
        /// </summary>
        internal static void Scan(List<CorpseRecord> newlyDiscovered)
        {
            newlyDiscovered.Clear();
#if SNITCH
            if (ScanDisabled) return;   // ablation lever 'looseends.scan': skip the sighting pipeline
#endif

            var reg = NPCManager.NPCRegistry;
            if (reg == null) return;
            int regCount;
            try { regCount = reg.Count; } catch { return; }
            if (regCount == 0) return;

            bool requireLos = Preferences.RequireLineOfSight;
            bool useVisionRange = Preferences.UseVisionConeRange;
            float explicitRange = Preferences.DetectionRange;
            // Cull observers beyond the relevant radius, but never tighter than the close-range notice radius.
            float cull = Mathf.Max(useVisionRange ? Preferences.ObserverCullRadius : explicitRange, Preferences.NoticeRadius);
            float cullSqr = cull * cull;
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
                    // Snitch (Debug): time ONLY the vision-cone / occlusion-ray test, so the distance pre-cull cost
                    // is recoverable as (LooseEnds.Scan - LooseEnds.Scan.Sight).
#if SNITCH
                    bool seen;
                    using (Profiler.Sample("LooseEnds.Scan.Sight"))
                        seen = CanSee(obs, corpse, corpsePos, requireLos);
#else
                    bool seen = CanSee(obs, corpse, corpsePos, requireLos);
#endif
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
            LastChecks = checksUsed;
#if DEBUG
            LastFound = newlyDiscovered.Count;
            LastScanTime = Time.time;
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

        private const float EyeHeight = 1.6f;

        private static bool CanSee(NPC observer, NPC corpse, Vector3 bodyPos, bool requireLos)
        {
            if (!requireLos)
            {
                return true;   // radius-only mode (pre-cull already passed)
            }

            NPCAwareness aw;
            try { aw = observer.Awareness; } catch { return false; }
            VisionCone cone = aw != null ? aw.VisionCone : null;

            Vector3 eye = EyeOf(observer, cone);
            float dist = (eye - bodyPos).magnitude;

            // 1) The NPC's own vision cone (FOV + occlusion), but only within a believable body-notice range. A body
            //    lying flat on the ground is far less conspicuous than a standing person, so we cap the cone test well
            //    below the NPC's full standing sight range - otherwise a corpse in the open is spotted by anyone within
            //    ~25m and the response feels instant with "nobody around".
            if (dist <= Preferences.BodySightRange)
            {
                try { if (cone != null && cone.IsPointWithinSight(bodyPos, false, null)) return true; }
                catch { /* fall through to the close-range notice */ }
            }

            // 2) Close-range notice. NPCs never look down at their own feet, so a body lying flat right next to them is
            //    below the forward vision cone - which is why cops walk straight over a corpse. If a living NPC is within
            //    the notice radius with a clear line of sight, they notice it. Occlusion is delegated to the game's own
            //    visibility solver (EntityVisibility), so a body in a dumpster / behind a wall stays hidden.
            try
            {
                float r = Preferences.NoticeRadius;
                if (dist <= r && BodyExposedTo(corpse, observer, eye, r, bodyPos, cone))
                    return true;
            }
            catch { /* ignore */ }

            return false;
        }

        // Minimum fraction of the body's visibility points that must be clear for a close-range notice. The game's
        // EntityVisibility treats "VisionObscurer" cover (bushes / props) as PARTIAL - it still returns a small nonzero
        // exposure - so a bare > 0 would notice a body fully tucked behind a dumpster. A threshold keeps "hide the body"
        // intact: a body must be meaningfully exposed, not just a sliver through cover.
        private const float NoticeExposureThreshold = 0.4f;

        /// <summary>
        /// True if the corpse has a clear enough line of sight to the observer's eye at close range. Prefers the game's
        /// own <see cref="EntityVisibility.CalculateExposureToPoint"/> (correct self + observer exclusion, partial-cover
        /// handling) and falls back to a manual occlusion ray if the body has no visibility component.
        /// </summary>
        private static bool BodyExposedTo(NPC corpse, NPC observer, Vector3 eye, float range, Vector3 bodyPos, VisionCone cone)
        {
            try
            {
                EntityVisibility vis = corpse != null ? corpse.Visibility : null;
                if (vis != null)
                {
                    // checkRange is measured from the body's transform to the eye; pad it so the ragdoll-vs-root offset
                    // never trips the helper's distance early-out for a body that is genuinely within the notice radius.
                    float exposure = vis.CalculateExposureToPoint(eye, range + 3f, observer);
                    return exposure >= NoticeExposureThreshold;
                }
            }
            catch { /* fall through to the manual ray */ }
            return HasClearPath(eye, bodyPos, cone);
        }

        private static Vector3 EyeOf(NPC observer, VisionCone cone)
        {
            try { if (cone != null && cone.VisionOrigin != null) return cone.VisionOrigin.position; } catch { }
            return observer.transform.position + Vector3.up * EyeHeight;
        }

        /// <summary>
        /// True if no occluder blocks the segment eye-&gt;body. The body's own collider at the far end is excluded by
        /// stopping the cast a little short, so a body is never its own blocker. If no occluder layer mask is available
        /// this returns true (proximity-only) - better to notice a body at your feet than to miss it.
        /// </summary>
        private static bool HasClearPath(Vector3 eye, Vector3 bodyPos, VisionCone cone)
        {
            Vector3 dir = bodyPos - eye;
            float dist = dir.magnitude;
            if (dist <= 0.6f) return true;   // basically on top of it
            dir /= dist;
            float castDist = dist - 0.5f;    // stop short so the body itself is not counted as the blocker
            try
            {
                if (cone != null)
                {
                    LayerMask mask = cone.VisibilityBlockingLayers;
                    if (mask.value != 0)
                        return !Physics.Raycast(eye, dir, castDist, mask.value);
                }
            }
            catch { /* fall through */ }
            return true;
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
