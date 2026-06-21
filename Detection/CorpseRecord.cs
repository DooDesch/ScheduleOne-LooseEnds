using UnityEngine;

namespace LooseEnds.Detection
{
    /// <summary>
    /// Per-corpse state tracked by <see cref="CorpseTracker"/>. Keyed by the NPC's Unity instance id. Holds the
    /// discovery latch + reaction timing (so a body fires a response at most once / under cooldown). The corpse
    /// weight cache (for restoring the original drag on drop) lives in <c>Weight/CorpseWeight.cs</c>.
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

        // ----- call state (witness phoning the police: native CallPoliceBehaviour, interruptible 4s window) -----
        /// <summary>The discoverer is on the phone calling the police; silence them before it connects to stop it.</summary>
        public bool Calling;
        public float CallStartTime;
        /// <summary>The call behaviour actually became active (phone genuinely out) at least once during this attempt.</summary>
        public bool CallWasActive;
        /// <summary>Earliest time a fresh call attempt may start, after one stalled (witness was occupied).</summary>
        public float CallRetryAfter;
        /// <summary>The suspect the in-progress call targets (captured at call start).</summary>
        public Player CallKiller;
        /// <summary>Where the body rested when the call started - officers are redirected here once the call connects.</summary>
        public Vector3 ScenePos;
    }
}
