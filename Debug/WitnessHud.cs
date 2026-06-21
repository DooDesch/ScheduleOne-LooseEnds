#if DEBUG
using System.Text;
using UnityEngine;
using Il2CppScheduleOne.PlayerScripts;   // Player, PlayerCrimeData
using LooseEnds.Detection;
using LooseEnds.Weight;

namespace LooseEnds.Debugging
{
    /// <summary>
    /// On-screen debug overlay (DEBUG builds only). Shows the witness system's live state - activation, tracked
    /// corpses (distance / discovered / dispatched / killer), last scan stats, whether you are carrying a body, and
    /// your current pursuit level. Toggle with F7. Compiled out of Release entirely.
    /// </summary>
    internal static class WitnessHud
    {
        private static GUIStyle _style;
        private static readonly StringBuilder _sb = new StringBuilder(640);

        internal static void Draw()
        {
            if (!Core.HudVisible) return;
            try { DrawInner(); } catch { }
        }

        private static void DrawInner()
        {
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.UpperLeft,
                    fontSize = 12,
                    richText = true,
                    wordWrap = false
                };
                _style.normal.textColor = Color.white;
                _style.padding = new RectOffset(8, 8, 6, 6);
            }

            _sb.Clear();
            _sb.Append("<b>Loose Ends</b>  ").Append(Core.WitnessStatus).Append("   <size=10>(F7 hide)</size>\n");

            int npcCount = -1;
            try { var reg = NPCManager.NPCRegistry; if (reg != null) npcCount = reg.Count; } catch { }

            _sb.Append("corpses tracked: <b>").Append(CorpseTracker.Count).Append("</b>")
               .Append("   NPCs: ").Append(npcCount)
               .Append("   carrying: ").Append(CorpseWeight.ActiveCount > 0 ? "<b>YES</b>" : "no").Append('\n');
            _sb.Append("last scan: checks=").Append(SightingScanner.LastChecks)
               .Append("  found=").Append(SightingScanner.LastFound).Append('\n');

            Vector3 ppos = Vector3.zero;
            string pursuit = "?";
            try
            {
                Player local = Player.Local;
                if (local != null)
                {
                    ppos = local.transform.position;
                    PlayerCrimeData cd = local.CrimeData;
                    if (cd != null) pursuit = PursuitName((int)cd.CurrentPursuitLevel);
                }
            }
            catch { }
            _sb.Append("your pursuit: <b>").Append(pursuit).Append("</b>\n");

            if (CorpseTracker.Count == 0)
            {
                _sb.Append("<size=11>no DEAD bodies tracked.\nKO'd NPCs don't count unless ReactToKnockedOut=true.\nkill an NPC (lethal) or toggle SpawnTestCorpse.</size>\n");
            }
            else
            {
                int shown = 0;
                foreach (CorpseRecord rec in CorpseTracker.Records)
                {
                    if (shown >= 8) { _sb.Append("  ...\n"); break; }
                    shown++;
                    float dist = -1f;
                    try { if (rec.Npc != null) dist = Vector3.Distance(rec.Npc.transform.position, ppos); } catch { }
                    string killer;
                    try { killer = KillerLabel(rec.Killer); } catch { killer = "?"; }
                    _sb.Append("  #").Append(rec.Id)
                       .Append("  ").Append(dist >= 0f ? dist.ToString("F0") + "m" : "?")
                       .Append(rec.Discovered ? "  <b>SEEN</b>" : "  unseen")
                       .Append(rec.Calling ? " <b>CALLING</b>" : "")
                       .Append(rec.Dispatched ? " <b>DISPATCHED</b>" : "")
                       .Append("  killer=").Append(killer)
                       .Append('\n');
                }
            }

            GUIContent content = new GUIContent(_sb.ToString());
            Vector2 size = _style.CalcSize(content);
            GUI.Box(new Rect(10f, 10f, Mathf.Max(380f, size.x + 6f), size.y + 6f), content, _style);
        }

        /// <summary>
        /// Human-readable killer label. Shows "you" for the local player (its raw PlayerCode is the host SteamID, or "0"
        /// when the game was launched without Steam - both opaque), the player name otherwise, or "unknown" if null.
        /// </summary>
        private static string KillerLabel(Player killer)
        {
            if (killer == null) return "unknown";
            try
            {
                Player local = Player.Local;
                if (local != null && local.GetInstanceID() == killer.GetInstanceID()) return "you";
            }
            catch { }
            try { string n = killer.PlayerName; if (!string.IsNullOrEmpty(n)) return n; } catch { }
            try { return killer.PlayerCode; } catch { return "?"; }
        }

        private static string PursuitName(int level)
        {
            switch (level)
            {
                case 0: return "None";
                case 1: return "Investigating";
                case 2: return "Arresting";
                case 3: return "NonLethal";
                case 4: return "Lethal";
                default: return level.ToString();
            }
        }
    }
}
#endif
