using System;
using UnityEngine;
using Il2CppScheduleOne.VoiceOver;       // EVOLineType
using Il2CppScheduleOne.Police;          // PoliceOfficer
using Il2CppScheduleOne.Employees;       // Employee

namespace LooseEnds.Reaction
{
    /// <summary>
    /// Makes the NPC that DISCOVERED a body visibly react, so a witness no longer just stands there. This is purely
    /// COSMETIC: the actual police dispatch is owned by <see cref="ReactionDispatcher"/> (which aims the game's
    /// investigation at the scene). A civilian plays an alerted voice line and turns to face the body; they do NOT
    /// flee (we want them to stop and look, not run) and do NOT call the police themselves (that would double-dispatch
    /// and aim officers at the player, breaking the scene flow). Police/employees just get a brief alerted VO.
    /// </summary>
    internal static class CitizenReaction
    {
        /// <summary>Cosmetic-only witness reaction. Does not dispatch.</summary>
        internal static void React(NPC discoverer, Vector3 bodyPos)
        {
            if (discoverer == null) return;

            // Alerted voice line for everyone who witnesses the body.
            Vo(discoverer, EVOLineType.Alerted);

            bool isPolice = false;
            bool isEmployee = false;
            try { isPolice = discoverer.TryCast<PoliceOfficer>() != null; } catch { }
            try { isEmployee = discoverer.TryCast<Employee>() != null; } catch { }
            bool isCivilian = !isPolice && !isEmployee;

            // A civilian stops and turns to face the body (no fleeing, no dispatch). Police/employees just alert.
            if (isCivilian)
            {
                try
                {
                    NPCMovement mv = discoverer.Movement;
                    if (mv != null)
                    {
                        Vector3 forward = (bodyPos - discoverer.transform.position);
                        forward.y = 0f;
                        if (forward.sqrMagnitude > 0.0001f)
                            mv.FaceDirection(forward.normalized);
                    }
                }
                catch (Exception e) { Core.Log?.Warning("[Reaction] face-body failed: " + e.Message); }
            }
        }

        /// <summary>Just the alerted gasp - used when the native call behaviour will drive the rest (phone + facing).</summary>
        internal static void Alert(NPC npc)
        {
            if (npc == null) return;
            Vo(npc, EVOLineType.Alerted);
        }

        private static void Vo(NPC npc, EVOLineType type)
        {
            try { npc.PlayVO(type, false); } catch { }
        }
    }
}
