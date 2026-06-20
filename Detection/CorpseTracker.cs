using System.Collections.Generic;
using LooseEnds.Config;
using LooseEnds.Killer;

namespace LooseEnds.Detection
{
    /// <summary>
    /// Maintains the small live set of NPC corpses in the world. Populated two ways: event-driven (the NPCHealth.Die
    /// postfix calls <see cref="OnNpcDied"/> for instant, low-latency tracking) and a periodic <see cref="Reconcile"/>
    /// safety net over NPCManager.NPCRegistry (catches NPC-vs-NPC / environmental deaths, bodies already down on load,
    /// and prunes revived/despawned corpses). Keeping this set tiny is the main performance lever: when it is empty,
    /// the per-frame scan is a single count check.
    /// </summary>
    internal static class CorpseTracker
    {
        private static readonly Dictionary<int, CorpseRecord> _corpses = new Dictionary<int, CorpseRecord>();
        private static readonly List<int> _toRemove = new List<int>();

        internal static int Count => _corpses.Count;
        internal static Dictionary<int, CorpseRecord>.ValueCollection Records => _corpses.Values;

        internal static bool TryGet(NPC npc, out CorpseRecord rec)
        {
            rec = null;
            if (npc == null) return false;
            return _corpses.TryGetValue(npc.GetInstanceID(), out rec);
        }

        /// <summary>Event-driven registration from the NPCHealth.Die postfix.</summary>
        internal static void OnNpcDied(NPC npc, Player killer)
        {
            if (npc == null) return;
            int id = npc.GetInstanceID();
            if (!_corpses.TryGetValue(id, out CorpseRecord rec))
            {
                rec = new CorpseRecord { Npc = npc, Id = id };
                _corpses[id] = rec;
#if DEBUG
                Core.LogDebug($"[Corpse] tracked new corpse id={id} (now {_corpses.Count})");
#endif
            }
            if (killer != null) rec.Killer = killer;
        }

        /// <summary>
        /// Periodic safety-net reconcile (call on a ~2s cadence). Adds dead-but-untracked NPCs and removes corpses
        /// that revived or whose object is gone. Robust to destroyed Il2Cpp objects (per-record try/catch).
        /// </summary>
        internal static void Reconcile()
        {
            bool includeKnockedOut = Preferences.ReactToKnockedOut;

            // 1) add dead-but-untracked
            try
            {
                var reg = NPCManager.NPCRegistry;
                if (reg != null)
                {
                    int n = reg.Count;
                    for (int i = 0; i < n; i++)
                    {
                        NPC npc = reg[i];
                        if (npc == null) continue;
                        NPCHealth h;
                        try { h = npc.Health; } catch { continue; }
                        if (h == null) continue;
                        bool dead;
                        try { dead = h.IsDead || (includeKnockedOut && h.IsKnockedOut); } catch { continue; }
                        if (!dead) continue;
                        int id = npc.GetInstanceID();
                        if (!_corpses.ContainsKey(id))
                        {
                            _corpses[id] = new CorpseRecord { Npc = npc, Id = id, Killer = KillerRegistry.GetKiller(npc) };
#if DEBUG
                            Core.LogDebug($"[Corpse] reconcile added corpse id={id} (now {_corpses.Count})");
#endif
                        }
                    }
                }
            }
            catch { /* registry not ready */ }

            // 2) remove revived / destroyed
            _toRemove.Clear();
            foreach (KeyValuePair<int, CorpseRecord> kv in _corpses)
            {
                NPC npc = kv.Value.Npc;
                bool remove = false;
                try
                {
                    if (npc == null) remove = true;
                    else
                    {
                        NPCHealth h = npc.Health;
                        bool stillCorpse = h != null && (h.IsDead || (includeKnockedOut && h.IsKnockedOut));
                        if (!stillCorpse) remove = true;
                    }
                }
                catch { remove = true; }   // destroyed object
                if (remove) _toRemove.Add(kv.Key);
            }
            for (int i = 0; i < _toRemove.Count; i++)
            {
                int id = _toRemove[i];
                _corpses.Remove(id);
                KillerRegistry.ForgetId(id);
            }
        }

        internal static void Remove(int id)
        {
            _corpses.Remove(id);
            KillerRegistry.ForgetId(id);
        }

        internal static void Clear()
        {
            _corpses.Clear();
        }
    }
}
