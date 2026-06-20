using HarmonyLib;
using LooseEnds.Killer;

namespace LooseEnds.Patches
{
    /// <summary>
    /// Captures killer attribution from the NPC health system. NotifyAttackedByPlayer fires whenever a player damages
    /// an NPC (the uniform hook across civilians / police / employees and all attack types), so we remember the most
    /// recent attacker; Die() promotes that to the confirmed killer. These postfixes are read-only (they only record
    /// a Player reference) and therefore safe on every peer - only the host later acts on the data.
    /// </summary>
    [HarmonyPatch(typeof(NPCHealth), nameof(NPCHealth.NotifyAttackedByPlayer))]
    internal static class NPCHealth_NotifyAttackedByPlayer_Patch
    {
        private static void Postfix(NPCHealth __instance, Player player)
        {
            try { KillerRegistry.RecordAttacker(__instance.npc, player); }
            catch { /* attribution is best-effort */ }
        }
    }

    [HarmonyPatch(typeof(NPCHealth), nameof(NPCHealth.Die))]
    internal static class NPCHealth_Die_Patch
    {
        private static void Postfix(NPCHealth __instance)
        {
            try
            {
                NPC npc = __instance.npc;
                if (npc == null) return;
                Player killer = KillerRegistry.RecordKill(npc);
                // CorpseTracker.OnNpcDied(npc) is wired in Step 3 to register the corpse immediately.
                Detection.CorpseTracker.OnNpcDied(npc, killer);
#if DEBUG
                Core.LogDebug($"[Killer] NPC died id={npc.GetInstanceID()} killer={(killer != null ? killer.PlayerCode : "unknown")}");
#endif
            }
            catch { /* best-effort */ }
        }
    }
}
