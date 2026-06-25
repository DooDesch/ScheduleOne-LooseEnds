#if DEBUG
using System;
using System.Collections.Generic;
using Il2CppScheduleOne.ItemFramework;     // ItemDefinition, ItemInstance
using Reg = Il2CppScheduleOne.Registry;    // Registry.GetItem (qualified to avoid namespace-wide import)
// PlayerInventory + PlayerSingleton<T> come from the global usings.

namespace LooseEnds.Debugging
{
    /// <summary>
    /// DEBUG-only test helper (the one-shot config button, or the Snitch panel "Give Arsenal" action): drops a weapon
    /// arsenal straight into the player's inventory. It exists to test the witness "silence the caller" flow - you need a weapon to knock out or kill a
    /// witness during their ~4s phone call. The real weapon item IDs live in Unity assets (not in code), so this is
    /// self-discovering: it walks a broad candidate ID list and adds whichever the game's item Registry actually
    /// resolves, logging the rest so the list can be refined. Compiled out of Release entirely.
    /// </summary>
    internal static class DebugArsenal
    {
        // Confirmed-valid Schedule I weapon item IDs (verified live via the game's item Registry). Registry.GetItem
        // returns null for an unknown id, so any future-invalid entry is simply skipped and logged under "unknown" -
        // the valid ones still get added. m1911mag is spare ammo; the guns spawn pre-loaded.
        private static readonly string[] Candidates =
        {
            "machete", "baseballbat",   // melee - best for KNOCKING OUT a witness mid-call
            "m1911", "revolver", "m1911mag"   // firearms (lethal) + spare magazine
        };

        internal static void Give()
        {
            try
            {
                PlayerInventory inv = PlayerSingleton<PlayerInventory>.Instance;
                if (inv == null) { Core.Log?.Warning("[Arsenal] PlayerInventory not ready."); return; }

                var added = new List<string>();
                var unknown = new List<string>();
                int full = 0;

                foreach (string id in Candidates)
                {
                    ItemDefinition def = null;
                    try { def = Reg.GetItem(id); } catch { }
                    if (def == null) { unknown.Add(id); continue; }
                    try
                    {
                        ItemInstance inst = def.GetDefaultInstance(1);
                        if (inst == null) { unknown.Add(id); continue; }
                        if (!inv.CanItemFitInInventory(inst, 1)) { full++; continue; }
                        inv.AddItemToInventory(inst);
                        added.Add(id);
                    }
                    catch (Exception e) { Core.Log?.Warning($"[Arsenal] give '{id}' failed: {e.Message}"); }
                }

                Core.Log?.Msg($"[Arsenal] added [{string.Join(", ", added)}]"
                    + (full > 0 ? $"; {full} skipped (inventory full - clearinventory first)" : "")
                    + (unknown.Count > 0 ? $"; unknown ids [{string.Join(", ", unknown)}]" : ""));
            }
            catch (Exception e) { Core.Log?.Warning("[Arsenal] failed: " + e.Message); }
        }
    }
}
#endif
