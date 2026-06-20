namespace LooseEnds.Detection
{
    /// <summary>
    /// Per-corpse state tracked by <see cref="CorpseTracker"/>. Keyed by the NPC's Unity instance id. Holds the
    /// discovery latch + reaction timing (so a body fires a response at most once / under cooldown) and the cached
    /// original drag weights so the heavier-corpse change can be restored exactly on drop.
    /// </summary>
    internal sealed class CorpseRecord
    {
        public NPC Npc;
        public int Id;

        /// <summary>The player blamed for this body, or null if unknown (environmental / NPC-vs-NPC).</summary>
        public Player Killer;

        // ----- discovery / reaction state -----
        /// <summary>A witness has seen the body; the reaction-delay timer is running.</summary>
        public bool Discovered;
        public float FirstSeenTime;
        /// <summary>The NPC that first saw the body.</summary>
        public NPC Discoverer;
        /// <summary>The police response has fired for this body.</summary>
        public bool Dispatched;
        public float DispatchedTime;

        // ----- weight cache (Step 5) -----
        public bool WeightCached;
        public float OrigMass;
        public float OrigDragForceMult;
    }
}
