using System.Collections.Generic;
using UnityEngine;

namespace LooseEnds.Killer
{
    /// <summary>
    /// Tracks which Player killed each NPC. The game keeps no "killer" field on a corpse, so we capture it at attack
    /// time: NPCHealth.NotifyAttackedByPlayer(Player) records the most recent attacker per NPC, and NPCHealth.Die()
    /// resolves that into a confirmed killer. Keyed by the NPC's Unity instance id (stable for the object's life).
    /// In-memory only - cleared on save / scene change so attribution never leaks across saves. The player's own
    /// crime/pursuit state is persisted by the game, not by us.
    /// </summary>
    internal static class KillerRegistry
    {
        // How long before death an attack still counts as "the kill" (covers beat-down-then-finish and delayed death).
        private const float AttributionWindowSeconds = 120f;

        private struct Attack { public Player Player; public float Time; }

        private static readonly Dictionary<int, Attack> _lastAttacker = new Dictionary<int, Attack>();
        private static readonly Dictionary<int, Player> _killer = new Dictionary<int, Player>();

        /// <summary>Called from the NotifyAttackedByPlayer postfix: remember the most recent player attacker.</summary>
        internal static void RecordAttacker(NPC npc, Player player)
        {
            if (npc == null || player == null) return;
            _lastAttacker[npc.GetInstanceID()] = new Attack { Player = player, Time = Time.time };
        }

        /// <summary>
        /// Called when an NPC dies: promote the most recent (recent-enough) attacker to the confirmed killer.
        /// Returns the resolved killer (or null if the NPC was not recently attacked by a player).
        /// </summary>
        internal static Player RecordKill(NPC npc)
        {
            if (npc == null) return null;
            int id = npc.GetInstanceID();
            if (_lastAttacker.TryGetValue(id, out Attack a) && a.Player != null
                && Time.time - a.Time <= AttributionWindowSeconds)
            {
                _killer[id] = a.Player;
                return a.Player;
            }
            return null;
        }

        /// <summary>
        /// The player responsible for a downed NPC, or null if unknown (environmental / NPC-vs-NPC). Prefers the
        /// confirmed killer (promoted on death), but falls back to a recent player attacker - so a body the player only
        /// knocked out (which never goes through Die/RecordKill) is still attributed to whoever assaulted it.
        /// </summary>
        internal static Player GetKiller(NPC npc)
        {
            if (npc == null) return null;
            int id = npc.GetInstanceID();
            if (_killer.TryGetValue(id, out Player p) && p != null) return p;
            if (_lastAttacker.TryGetValue(id, out Attack a) && a.Player != null
                && Time.time - a.Time <= AttributionWindowSeconds)
            {
                return a.Player;
            }
            return null;
        }

        /// <summary>Drop all attribution for an NPC (revived / despawned).</summary>
        internal static void Forget(NPC npc)
        {
            if (npc == null) return;
            ForgetId(npc.GetInstanceID());
        }

        /// <summary>Drop attribution by instance id (used when the NPC object is already gone).</summary>
        internal static void ForgetId(int id)
        {
            _lastAttacker.Remove(id);
            _killer.Remove(id);
        }

        /// <summary>Wipe everything (save / scene change).</summary>
        internal static void Clear()
        {
            _lastAttacker.Clear();
            _killer.Clear();
        }
    }
}
