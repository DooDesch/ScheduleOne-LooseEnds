using System.Collections.Generic;
using UnityEngine;
using Il2CppScheduleOne.Dragging;   // Draggable
using Il2CppScheduleOne.Tools;      // FloatStack
using LooseEnds.Config;
using LooseEnds.Detection;

namespace LooseEnds.Weight
{
    /// <summary>
    /// Makes a carried corpse feel heavy. The drag itself uses ForceMode.Acceleration (mass-independent), so the only
    /// thing that really sells the weight is slowing the CARRIER down while they haul a body - that is the stress the
    /// design wants. So the primary effect is a labelled multiplicative entry pushed onto the local player's
    /// MoveSpeedMultiplierStack while a corpse is held (removed on drop). As a secondary touch the corpse's own
    /// Draggable is made laggier (lower DragForceMultiplier). Scoped to carrying; restored exactly on release. Keeps its
    /// own cache keyed by Draggable instance id so it is independent of the witness system.
    /// </summary>
    internal static class CorpseWeight
    {
        private const string SlowLabel = "LooseEnds_CorpseWeight";

        private struct Saved
        {
            public float DragMult;
            public Rigidbody Rb;
            public float Mass;
            public bool HasMass;
            public bool Slowed;
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

            bool isCorpse = IsCorpseDraggable(d);
            bool localDragger = IsLocalDragger(d);
#if DEBUG
            Core.LogDebug($"[Weight] drag start id={id} corpse={isCorpse} localDragger={localDragger} mult={mult}");
#endif
            if (!isCorpse) return;

            WeightMode mode = Preferences.Weight;
            Saved saved = default;
            try
            {
                // Secondary: make the body itself lag behind the carry point.
                saved.DragMult = d.DragForceMultiplier;
                if (mode == WeightMode.DragForce || mode == WeightMode.Both)
                {
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
                        rb.mass = rb.mass * mult;   // affects throw weight + collisions (drag itself is mass-independent)
                    }
                }

                // Primary, actually-noticeable effect: slow the local carrier while hauling the body.
                if (localDragger && ApplyCarrySlowdown(mult)) saved.Slowed = true;

                _active[id] = saved;
#if DEBUG
                Core.LogDebug($"[Weight] applied id={id} dragMult->{d.DragForceMultiplier} slowedCarrier={saved.Slowed}");
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
                if (s.Slowed) RemoveCarrySlowdown();
            }
            catch { /* object may be gone */ }
            _active.Remove(id);
        }

        /// <summary>Push a labelled multiplicative slowdown onto the local player's move-speed stack.</summary>
        private static bool ApplyCarrySlowdown(float mult)
        {
            try
            {
                PlayerMovement pm = PlayerSingleton<PlayerMovement>.Instance;
                if (pm == null) return false;
                FloatStack stack = pm.MoveSpeedMultiplierStack;
                if (stack == null) return false;
                // mult 1 -> 1.0 (no slow), 5 -> 0.50, 10 -> 0.31, 20 -> 0.17. Floored so you can always crawl.
                float factor = Mathf.Clamp(1f / (1f + (mult - 1f) * 0.25f), 0.15f, 1f);
                stack.Add(new FloatStack.StackEntry(SlowLabel, factor, FloatStack.EStackMode.Multiplicative, 0));
                return true;
            }
            catch { return false; }
        }

        private static void RemoveCarrySlowdown()
        {
            try
            {
                PlayerMovement pm = PlayerSingleton<PlayerMovement>.Instance;
                if (pm != null && pm.MoveSpeedMultiplierStack != null)
                    pm.MoveSpeedMultiplierStack.Remove(SlowLabel);
            }
            catch { /* ignore */ }
        }

        private static bool IsLocalDragger(Draggable d)
        {
            try
            {
                Player local = Player.Local;
                Player cur = d.CurrentDragger;
                return local != null && cur != null && cur.GetInstanceID() == local.GetInstanceID();
            }
            catch { return false; }
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

        internal static void Clear()
        {
            RemoveCarrySlowdown();   // safety: never leave the player stuck slow after a scene change
            _active.Clear();
        }
    }
}
