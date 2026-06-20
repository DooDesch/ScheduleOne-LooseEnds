// IL2CPP backend (net6.0) global usings.
//
// Import the most-used Il2Cpp* game namespaces globally so the rest of the source uses UNQUALIFIED game type
// names (NPC, NPCManager, Player, NetworkSingleton...). The law / vision / police / dragging / responses
// namespaces are imported file-locally where needed to avoid type-name collisions.
//
// NOTE: because UnityEngine is imported here and System is imported implicitly, the bare identifiers `Object`
// and `Random` are ambiguous - always write `UnityEngine.Object` / `UnityEngine.Random` (or `System.Random`).

global using UnityEngine;
global using Il2CppScheduleOne.DevUtilities;    // NetworkSingleton<T>, Singleton<T>, PlayerSingleton<T>, PersistentSingleton<T>
global using Il2CppScheduleOne.PlayerScripts;   // Player (Player.Local), PlayerCrimeData
global using Il2CppScheduleOne.NPCs;            // NPC, NPCHealth, NPCMovement, NPCAwareness, NPCManager
