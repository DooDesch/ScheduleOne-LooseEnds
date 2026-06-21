using HarmonyLib;
using LooseEnds.Config;
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

    /// <summary>
    /// Knockouts never go through Die(), so without this they were only picked up by the ~2s reconcile poll. When
    /// "react to knocked-out NPCs" is enabled, register the body immediately (the periodic reconcile still handles the
    /// pref being toggled on later, despawns, and revives). Attribution uses the recent-attacker fallback since a KO
    /// never promotes a confirmed kill.
    /// </summary>
    [HarmonyPatch(typeof(NPCHealth), nameof(NPCHealth.KnockOut))]
    internal static class NPCHealth_KnockOut_Patch
    {
        private static void Postfix(NPCHealth __instance)
        {
            try
            {
                if (!Preferences.ReactToKnockedOut) return;
                NPC npc = __instance.npc;
                if (npc == null) return;
                Detection.CorpseTracker.OnNpcDied(npc, KillerRegistry.GetKiller(npc));
#if DEBUG
                Player k = KillerRegistry.GetKiller(npc);
                Core.LogDebug($"[Killer] NPC knocked out id={npc.GetInstanceID()} killer={(k != null ? k.PlayerCode : "unknown")}");
#endif
            }
            catch { /* best-effort */ }
        }
    }
}
