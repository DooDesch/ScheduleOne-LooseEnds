using System.Collections.Generic;
using UnityEngine;
using Il2CppScheduleOne.Dragging;   // Draggable
using LooseEnds.Config;
using LooseEnds.Detection;

namespace LooseEnds.Weight
{
    /// <summary>
    /// Makes a carried corpse heavier than the vanilla "plastic bag". Applied while a corpse draggable is held and
    /// restored exactly on drop, so the change is scoped to carrying and never permanently mutates the object. Keeps
    /// its own small cache keyed by the Draggable instance id so it is independent of the witness system (weight is a
    /// local drag-feel for the carrier, who may be a client). Only touches the per-object draggable - never the global
    /// DragManager - so other draggables (trash, items) are unaffected.
    /// </summary>
    internal static class CorpseWeight
    {
        private struct Saved
        {
            public float DragMult;
            public Rigidbody Rb;
            public float Mass;
            public bool HasMass;
        }

        private static readonly Dictionary<int, Saved> _active = new Dictionary<int, Saved>();

        internal static void OnStartDragging(Draggable d)
        {
            if (!Preferences.Enabled || d == null) return;

            float mult = Preferences.CorpseWeightMultiplier;
            if (mult <= 1.0001f) return;   // vanilla weight

            int id;
            try { id = d.GetInstanceID(); } catch { return; }
            if (_active.ContainsKey(id)) return;   // already scaled (idempotent)

            if (!IsCorpseDraggable(d)) return;

            WeightMode mode = Preferences.Weight;
            Saved saved = default;
            try
            {
                saved.DragMult = d.DragForceMultiplier;
                if (mode == WeightMode.DragForce || mode == WeightMode.Both)
                {
                    // Lower drag force -> the body lags behind the carry point -> feels heavy. Clamp so it stays movable.
                    d.DragForceMultiplier = Mathf.Max(0.05f, saved.DragMult / mult);
                }
                if (mode == WeightMode.Mass || mode == WeightMode.Both)
                {
                    Rigidbody rb = d.Rigidbody;
                    if (rb != null)
                    {
                        saved.Rb = rb;
                        saved.Mass = rb.mass;
                        saved.HasMass = true;
                        rb.mass = rb.mass * mult;
                    }
                }
                _active[id] = saved;
#if DEBUG
                Core.LogDebug($"[Weight] corpse drag start id={id} mode={mode} x{mult} (dragMult {saved.DragMult} -> {d.DragForceMultiplier}, mass {(saved.HasMass ? saved.Mass.ToString() : "n/a")})");
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
            try
            {
                d.DragForceMultiplier = s.DragMult;
                if (s.HasMass && s.Rb != null) s.Rb.mass = s.Mass;
            }
            catch { /* object may be gone */ }
            _active.Remove(id);
        }

        /// <summary>True if this draggable is a dead NPC's ragdoll (not a normal pickup-able item).</summary>
        private static bool IsCorpseDraggable(Draggable d)
        {
            // Primary: walk up to the owning NPC and check it's dead.
            try
            {
                NPC npc = d.GetComponentInParent<NPC>();
                if (npc != null)
                {
                    NPCHealth h = npc.Health;
                    if (h != null && h.IsDead) return true;
                }
            }
            catch { /* fall through to the tracked-corpse match */ }

            // Fallback: match against a tracked corpse's RagdollDraggable (covers a detached ragdoll object).
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
