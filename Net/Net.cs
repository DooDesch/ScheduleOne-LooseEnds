using System;
using Il2CppFishNet;
using Il2CppFishNet.Managing;
using Il2CppScheduleOne.Networking;   // Lobby (PersistentSingleton<Lobby>)

namespace LooseEnds.Networking
{
    /// <summary>
    /// Two DISTINCT network signals, used for two different purposes (do not conflate them):
    ///
    ///   IsServer  - "am I the write authority?" via FishNet InstanceFinder.NetworkManager.IsServer. This is TRUE
    ///               even in single-player (the host runs a local FishNet server), which is exactly what we want:
    ///               the detection loop and all police/pursuit writes run only on the authority.
    ///
    ///   IsCoop    - "is this a real multi-human co-op session?" via the game's Lobby singleton
    ///               (Il2CppScheduleOne.Networking.Lobby : PersistentSingleton&lt;Lobby&gt;), IsInLobby && PlayerCount > 1.
    ///               This gates the EnableInMultiplayer safety posture. KNOWN GAP (same as Trashville): Lobby is
    ///               Steam-matchmaking-driven, so a direct-UDP dedicated server (no Steam lobby) reads as not-coop.
    ///
    /// All accessors are conservative: any failure returns the safe value (false).
    /// </summary>
    internal static class Net
    {
        private static NetworkManager Nm
        {
            get { try { return InstanceFinder.NetworkManager; } catch { return null; } }
        }

        /// <summary>True if a FishNet server/client is active (covers both SP-host and MP).</summary>
        internal static bool Online
        {
            get { var nm = Nm; try { return nm != null && (nm.IsServer || nm.IsClient); } catch { return false; } }
        }

        /// <summary>True only on the write authority (the host/server). True in single-player.</summary>
        internal static bool IsServer
        {
            get { var nm = Nm; try { return nm != null && nm.IsServer; } catch { return false; } }
        }

        /// <summary>True only in a real Steam co-op lobby with more than the local player.</summary>
        internal static bool IsCoop()
        {
            try
            {
                Lobby lobby = PersistentSingleton<Lobby>.Instance;
                if (lobby == null)
                {
                    return false;   // no lobby manager yet -> treat as single-player
                }
                return lobby.IsInLobby && lobby.PlayerCount > 1;
            }
            catch
            {
                return false;   // conservative: any failure -> assume single-player
            }
        }
    }
}
