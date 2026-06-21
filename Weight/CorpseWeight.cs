using System.Collections.Generic;
using UnityEngine;
using Il2CppScheduleOne.Dragging;   // Draggable
using LooseEnds.Config;
using LooseEnds.Detection;

namespace LooseEnds.Weight
{
    /// <summary>
    /// Makes a dragged body (dead OR knocked-out) feel heavy to PULL - the body resists, instead of slowing the
    /// player (which felt bad). While a body is carried we weaken its follow spring (lower DragForceMultiplier so it
    /// lags behind the carry point), crank its linear/angular drag (it moves sluggishly and stops fast), and raise its
    /// mass a little (heavier to throw/shove). All cached per-Draggable and restored exactly on drop, so the effect is
    /// scoped to carrying and never leaks. Independent of the witness system; applies to any draggable body.
    /// </summary>
    internal static class CorpseWeight
    {
        private struct Saved
        {
            public Draggable D;     // the draggable we modified (to restore DragForceMultiplier without being handed it)
            public int NpcId;       // owning NPC instance id (to restore proactively when that NPC is un-tracked)
            public float DragMult;
            public Rigidbody Rb;
            public float Drag;
            public float AngularDrag;
            public float Mass;
            public bool HasRb;
        }

        private static readonly Dictionary<int, Saved> _active = new Dictionary<int, Saved>();

        /// <summary>How many bodies are currently weighted (i.e. being carried). For the debug HUD.</summary>
        internal static int ActiveCount => _active.Count;

        internal static void OnStartDragging(Draggable d)
        {
            if (!Preferences.Enabled || d == null) return;

            float mult = Preferences.CorpseWeightMultiplier;
            if (mult <= 1.0001f) return;   // vanilla weight

            int id;
            try { id = d.GetInstanceID(); } catch { return; }
            if (_active.ContainsKey(id)) return;   // already weighted (idempotent)

            bool isBody = IsBodyDraggable(d);
#if DEBUG
            Core.LogDebug($"[Weight] drag start id={id} body={isBody} mult={mult}");
#endif
            if (!isBody) return;

            Saved s = default;
            try
            {
                s.D = d;
                try { NPC owner = d.GetComponentInParent<NPC>(); s.NpcId = owner != null ? owner.GetInstanceID() : 0; }
                catch { s.NpcId = 0; }

                // The drag spring uses ForceMode.Acceleration (mass-independent), so the felt weight comes from a
                // weaker follow (DragForceMultiplier) plus real resistance (Rigidbody.drag). Mass only adds throw/
                // collision heft.
                s.DragMult = d.DragForceMultiplier;
                d.DragForceMultiplier = Mathf.Max(0.05f, s.DragMult / mult);

                Rigidbody rb = d.Rigidbody;
                if (rb != null)
                {
                    s.Rb = rb;
                    s.HasRb = true;
                    s.Drag = rb.drag;
                    s.AngularDrag = rb.angularDrag;
                    s.Mass = rb.mass;
                    rb.drag = s.Drag + (mult - 1f) * 2f;             // strong linear resistance (the "heavy to pull" feel)
                    rb.angularDrag = s.AngularDrag + (mult - 1f) * 2f;
                    rb.mass = s.Mass * Mathf.Sqrt(mult);            // gentle - heavier to throw, not immovable
                }

                _active[id] = s;
#if DEBUG
                Core.LogDebug($"[Weight] applied id={id} dragMult->{d.DragForceMultiplier:F2} rbDrag->{(s.HasRb ? s.Rb.drag.ToString("F1") : "n/a")}");
#endif
            }
            catch { /* leave it vanilla on any failure */ }
        }

        internal static void OnStopDragging(Draggable d)
        {
            if (d == null) return;
            int id;
            try { id = d.GetInstanceID(); } catch { return; }
            if (!_active.TryGetValue(id, out Saved s)) return;
            RestoreEntry(s);
            _active.Remove(id);
#if DEBUG
            Core.LogDebug($"[Weight] drag stop id={id} restored");
#endif
        }

        /// <summary>Put one body's physics back exactly as it was (idempotent, guarded against destroyed objects).</summary>
        private static void RestoreEntry(Saved s)
        {
            try { if (s.D != null) s.D.DragForceMultiplier = s.DragMult; } catch { /* draggable gone */ }
            try
            {
                if (s.HasRb && s.Rb != null)
                {
                    s.Rb.drag = s.Drag;
                    s.Rb.angularDrag = s.AngularDrag;
                    s.Rb.mass = s.Mass;
                }
            }
            catch { /* rigidbody gone */ }
        }

        /// <summary>
        /// Proactively restore a carried body's physics when its NPC stops being a corpse (e.g. revived in place on a new
        /// day). The game disables the ragdoll's Draggable WITHOUT firing StopDragging, so OnStopDragging never runs and
        /// the heavy-physics modification would otherwise leak onto the now-living NPC. Called from the corpse-prune path.
        /// </summary>
        internal static void RestoreForNpcId(int npcId)
        {
            if (npcId == 0 || _active.Count == 0) return;
            List<int> hits = null;
            foreach (KeyValuePair<int, Saved> kv in _active)
            {
                if (kv.Value.NpcId != npcId) continue;
                RestoreEntry(kv.Value);
                (hits ??= new List<int>()).Add(kv.Key);
            }
            if (hits != null) for (int i = 0; i < hits.Count; i++) _active.Remove(hits[i]);
        }

        /// <summary>Restore every carried body's physics, then drop the cache. Use on save/scene reset so the originals
        /// are never lost before they are applied (the cache is wiped at the sleep autosave AFTER NPCs revive).</summary>
        internal static void RestoreAll()
        {
            foreach (KeyValuePair<int, Saved> kv in _active) RestoreEntry(kv.Value);
            _active.Clear();
        }

        /// <summary>True if this draggable is a dead OR knocked-out NPC's ragdoll (both are draggable bodies).</summary>
        private static bool IsBodyDraggable(Draggable d)
        {
            // Primary: walk up to the owning NPC and check it's down (dead or unconscious).
            try
            {
                NPC npc = d.GetComponentInParent<NPC>();
                if (npc != null)
                {
                    NPCHealth h = npc.Health;
                    if (h != null && (h.IsDead || h.IsKnockedOut)) return true;
                }
            }
            catch { /* fall through */ }

            // Fallback: match a tracked corpse's ragdoll draggable (covers a detached ragdoll object).
            try
            {
                foreach (CorpseRecord rec in CorpseTracker.Records)
                {
                    NPC npc = rec.Npc;
                    if (npc == null) continue;
                    NPCMovement mv = npc.Movement;
                    if (mv != null && mv.RagdollDraggable == d) return true;
                }
            }
            catch { /* ignore */ }
            return false;
        }

        internal static void Clear() => _active.Clear();
    }
}
