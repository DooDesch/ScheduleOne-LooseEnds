using HarmonyLib;
using Il2CppScheduleOne.Dragging;   // Draggable
using LooseEnds.Weight;

namespace LooseEnds.Patches
{
    /// <summary>
    /// Hooks corpse carrying to apply / restore the heavier-corpse weight. The drag events are parameterless, so we
    /// postfix the methods (which receive the Draggable as __instance). Restore runs on drop so the weight is scoped
    /// to carrying only. Non-corpse draggables are ignored by CorpseWeight.
    /// </summary>
    [HarmonyPatch(typeof(Draggable), nameof(Draggable.StartDragging))]
    internal static class Draggable_StartDragging_Patch
    {
        private static void Postfix(Draggable __instance)
        {
            try { CorpseWeight.OnStartDragging(__instance); } catch { }
        }
    }

    [HarmonyPatch(typeof(Draggable), nameof(Draggable.StopDragging))]
    internal static class Draggable_StopDragging_Patch
    {
        private static void Postfix(Draggable __instance)
        {
            try { CorpseWeight.OnStopDragging(__instance); } catch { }
        }
    }
}
