using System;
using MelonLoader;
using S1API.Lifecycle;
using LooseEnds.Config;
using LooseEnds.Detection;
using LooseEnds.Killer;
using LooseEnds.Networking;
using LooseEnds.Reaction;
#if SNITCH
using Snitch.Api;                 // Profiler section timing (Debug + EnableSnitch only; no-op when host absent)
#endif

[assembly: MelonInfo(typeof(LooseEnds.Core), "Loose Ends", "1.1.0", "DooDesch", null)]
[assembly: MelonGame("TVGS", "Schedule I")]
[assembly: MelonOptionalDependencies("ModManager&PhoneApp")]

namespace LooseEnds
{
    /// <summary>
    /// MelonLoader entry point for LooseEnds. Living NPCs who SEE a dead body react (citizens call the police,
    /// police begin the "Investigating" pursuit), and carried corpses are made heavier. The witness system is
    /// host-authoritative (runs only on the FishNet server, true even in single-player) and auto-disables in a real
    /// co-op lobby until a tester opts in. The throttled scan only does work while at least one corpse exists.
    /// </summary>
    public sealed class Core : MelonMod
    {
        public static Core Instance { get; private set; }
        public static MelonLogger.Instance Log { get; private set; }

        private bool _inWorld;
        private float _scanAccum;
        private float _reconcileAccum;
        // Tracks Player.Local.CrimeData.MinsSinceLastArrested to detect an arrest (it resets toward 0 on arrest).
        // -1 = not yet read.
        private int _prevMinsSinceArrested = -1;
        private readonly System.Collections.Generic.List<Detection.CorpseRecord> _discovered = new System.Collections.Generic.List<Detection.CorpseRecord>();
        // Last logged activation state (-1 unknown, 0 off-pref, 1 off-MP, 2 not-authority, 3 active) - dedups re-arm logs.
        private int _activeLogState = -1;

        [System.Diagnostics.Conditional("DEBUG")]
        public static void LogDebug(string msg) { Log?.Msg(msg); }

        public override void OnInitializeMelon()
        {
            Instance = this;
            Log = LoggerInstance;

            Preferences.Initialize();

            try { HarmonyInstance.PatchAll(); }
            catch (Exception e) { Log.Warning("[Core] Harmony patch failed: " + e.Message); }

            // Discovered-corpse + killer-attribution maps are in-memory only; wipe them on save/scene change so state
            // never leaks across saves (the player's own crime/pursuit state is persisted by the game, not by us).
            GameLifecycle.OnSaveStart += ResetState;
            GameLifecycle.OnPreSceneChange += ResetState;

            HookModManager();

#if DEBUG
            Log.Msg("Loose Ends v1.1.0 (DEBUG) - witness system + corpse weight. Dev probes in the config.");
#else
            Log.Msg("Loose Ends v1.1.0 - witness system + corpse weight.");
#endif
        }

        /// <summary>Clears all in-memory mod state (corpse tracking, killer attribution) on save / scene change.</summary>
        private static void ResetState()
        {
            CorpseTracker.Clear();
            KillerRegistry.Clear();
            Weight.CorpseWeight.RestoreAll();   // restore (not just drop) carried-body physics before wiping the cache
            // Drop the arrest-watcher edge state so a stale sample from a previous save/scene can't mis-compare.
            if (Instance != null) Instance._prevMinsSinceArrested = -1;
        }

        private void HookModManager()
        {
            // Optional dependency - isolated + guarded so a missing ModManager never breaks load.
            try
            {
                ModManagerPhoneApp.ModSettingsEvents.OnPhonePreferencesSaved += OnSettingsSaved;
                ModManagerPhoneApp.ModSettingsEvents.OnMenuPreferencesSaved += OnSettingsSaved;
                Log.Msg("[Core] Mod Manager & Phone App hooked (settings apply live).");
            }
            catch (Exception)
            {
                Log.Msg("[Core] Mod Manager & Phone App not present - settings via the MelonPreferences config file (apply live on save).");
            }
        }

        private void OnSettingsSaved()
        {
#if DEBUG
            HandleDebugCommands();
#endif
        }

        public override void OnPreferencesSaved()
        {
            OnSettingsSaved();
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            _inWorld = sceneName == "Main";
        }

        public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
        {
            _inWorld = false;
            WitnessStatus = "[not in world]";
            ResetState();
        }

#if DEBUG
        public override void OnGUI()
        {
            if (!_inWorld) return;
            Debugging.WitnessHud.Draw();
        }
#endif

        /// <summary>
        /// Decides whether the witness system should be doing work this frame, honoring the master switch, the
        /// host-authority requirement (server only) and the co-op safety posture. Logs each state transition once.
        /// </summary>
        /// <summary>Short human-readable activation state, surfaced by the debug HUD.</summary>
        internal static string WitnessStatus = "init";
#if DEBUG
        /// <summary>Debug HUD on/off (toggle with F7).</summary>
        internal static bool HudVisible = true;
#endif

        private bool WitnessActive()
        {
            if (!Preferences.Enabled)
            {
                WitnessStatus = "[OFF: disabled in settings]";
                if (_activeLogState != 0) { Log.Msg("[Core] Disabled via preferences - running vanilla."); _activeLogState = 0; }
                return false;
            }
            if (Net.IsCoop() && !Preferences.EnableInMultiplayer)
            {
                WitnessStatus = "[OFF: co-op - set EnableInMultiplayer]";
                if (_activeLogState != 1) { Log.Msg("[Core] Co-op session detected - witness system auto-DISABLED (set EnableInMultiplayer to opt in after a 2-player test)."); _activeLogState = 1; }
                return false;
            }
            if (!Net.IsServer)
            {
                // Not the authority (a connected client): the host runs detection + dispatch for everyone.
                WitnessStatus = "[client - host runs detection]";
                if (_activeLogState != 2) { Log.Msg("[Core] Not the network authority - witness detection runs on the host."); _activeLogState = 2; }
                return false;
            }
            WitnessStatus = "[ACTIVE]";
            if (_activeLogState != 3) { Log.Msg("[Core] Witness system ACTIVE."); _activeLogState = 3; }
            return true;
        }

        public override void OnUpdate()
        {
            if (!_inWorld)
            {
                return;
            }

#if DEBUG
            HandleDebugCommands();
            try { if (Input.GetKeyDown(KeyCode.F7)) HudVisible = !HudVisible; } catch { }
            try { if (Input.GetKeyDown(KeyCode.F8)) Debugging.DebugArsenal.Give(); } catch { }
#endif

            // Arrest watcher (server only): one arrest settles the whole spree - clear killer attribution for all bodies.
            CheckArrest();

            if (!WitnessActive())
            {
                return;
            }

            float dt = Time.deltaTime;

            // Safety-net reconcile (~2s): keep the live corpse set in sync with the world.
            _reconcileAccum += dt;
            if (_reconcileAccum >= 2f)
            {
                _reconcileAccum = 0f;
#if SNITCH
                using (Profiler.Sample("LooseEnds.Reconcile")) CorpseTracker.Reconcile();
#else
                CorpseTracker.Reconcile();
#endif
            }

            // The big perf lever: with no corpses in the world, do nothing else.
            if (CorpseTracker.Count == 0)
            {
                _scanAccum = 0f;
                return;
            }

            // Throttled witness scan.
            _scanAccum += dt;
            if (_scanAccum < Preferences.ScanIntervalSeconds)
            {
                return;
            }
            _scanAccum = 0f;

#if SNITCH
            using (Profiler.Sample("LooseEnds.WitnessTick")) WitnessTick();
#else
            WitnessTick();
#endif
        }

        /// <summary>
        /// Detects the local player being arrested and settles the whole spree: when MinsSinceLastArrested resets
        /// toward 0 (it otherwise counts up one per in-game minute), the player was just arrested, so we clear the
        /// killer registry and mark every tracked body resolved - a single arrest stops the player being re-arrested
        /// for each NPC they downed. Server-only and fully guarded (Player.Local / CrimeData may be null early).
        /// </summary>
        private void CheckArrest()
        {
            try
            {
                if (!Net.IsServer) return;

                Player local = Player.Local;
                if (local == null) return;
                PlayerCrimeData crimeData = local.CrimeData;
                if (crimeData == null) return;

                int mins = crimeData.MinsSinceLastArrested;

                if (_prevMinsSinceArrested >= 0 && mins < _prevMinsSinceArrested)
                {
                    KillerRegistry.Clear();
                    CorpseTracker.ResolveAll();
                    Log?.Msg("[Core] Player arrested - cleared killer attribution for all bodies.");
                }

                _prevMinsSinceArrested = mins;
            }
            catch (Exception e) { Log?.Warning("[Core] arrest watcher failed: " + e.Message); }
        }

        /// <summary>The throttled detection + reaction pass: scan for sightings, then run pending reactions.</summary>
        private void WitnessTick()
        {
            // 1) find new sightings (latches Discovered on the corpse records).
#if SNITCH
            using (Profiler.Sample("LooseEnds.Scan")) SightingScanner.Scan(_discovered);
#else
            SightingScanner.Scan(_discovered);
#endif

            // 2) fire reactions for corpses whose reaction delay has elapsed; re-arm re-triggers when allowed.
            float now = Time.time;
            float delay = Preferences.ReactionDelaySeconds;
            bool oncePerCorpse = Preferences.OncePerCorpse;
            float cooldown = Preferences.ResponseCooldownSeconds;

            foreach (Detection.CorpseRecord rec in CorpseTracker.Records)
            {
                // A call already in progress: advance it (connect -> scene, or witness silenced -> re-arm).
                if (rec.Calling)
                {
                    ReactionDispatcher.UpdateCall(rec, now);
                    continue;
                }

                if (!rec.Discovered) continue;
                if (rec.Dispatched)
                {
                    if (!oncePerCorpse && now - rec.DispatchedTime >= cooldown)
                    {
                        rec.Discovered = false;
                        rec.Dispatched = false;
                        rec.Discoverer = null;   // re-arm: the scanner can re-discover it
                    }
                    continue;
                }
                if (now - rec.FirstSeenTime < delay) continue;
                ReactionDispatcher.TryStartResponse(rec);
            }
        }

#if DEBUG
        private void HandleDebugCommands()
        {
            try
            {
                // These dev actions write authority state - only run them on the server, and wait until the local
                // player has actually spawned (Player.Local set), so they act relative to the player rather than the
                // map origin. The toggle stays pending until then.
                if (!Net.IsServer) return;
                try { if (Player.Local == null) return; } catch { return; }
                // Spawn a corpse first so a same-frame force-discover can act on it.
                if (Preferences.ConsumeSpawnTestCorpse()) KillNearestNpc();
                if (Preferences.ConsumeForceDiscover()) ForceDiscoverNearest();
                if (Preferences.ConsumeGiveArsenal()) Debugging.DebugArsenal.Give();
            }
            catch (Exception e) { Log.Warning("[Core] debug command failed: " + e.Message); }
        }

        /// <summary>Dev helper: force the response on the corpse nearest the player, bypassing line-of-sight + delay.</summary>
        private void ForceDiscoverNearest()
        {
            if (!Net.IsServer) { Log.Msg("[Debug] force-discover ignored - not the network authority."); return; }
            try
            {
                Vector3 ppos = Vector3.zero;
                try { Player local = Player.Local; if (local != null) ppos = local.transform.position; } catch { }

                Detection.CorpseRecord best = null;
                float bestD = float.MaxValue;
                foreach (Detection.CorpseRecord rec in CorpseTracker.Records)
                {
                    if (rec.Npc == null) continue;
                    float d;
                    try { d = (rec.Npc.transform.position - ppos).sqrMagnitude; } catch { continue; }
                    if (d < bestD) { bestD = d; best = rec; }
                }
                if (best == null) { Log.Msg("[Debug] force-discover: no tracked corpse. Create one with 'triggerlightning <npc>'."); return; }

                best.Discovered = true;
                best.FirstSeenTime = Time.time - 1000f;   // bypass the reaction delay
                best.Dispatched = false;
                best.Calling = false;
                best.Discoverer = null;
                ReactionDispatcher.TryStartResponse(best);
                Log.Msg($"[Debug] force-discovered + dispatched corpse {best.Id}.");
            }
            catch (Exception e) { Log.Warning("[Core] ForceDiscoverNearest failed: " + e.Message); }
        }

        /// <summary>Dev helper: kill the living NPC nearest the player via the game's own death path (makes a test corpse).</summary>
        private void KillNearestNpc()
        {
            if (!Net.IsServer) { Log.Msg("[Debug] spawn-test-corpse ignored - not the network authority."); return; }
            try
            {
                Vector3 ppos = Vector3.zero;
                try { Player local = Player.Local; if (local != null) ppos = local.transform.position; } catch { }

                var reg = NPCManager.NPCRegistry;
                if (reg == null) { Log.Msg("[Debug] NPC registry not ready."); return; }
                NPC best = null;
                float bestD = float.MaxValue;
                int n = reg.Count;
                for (int i = 0; i < n; i++)
                {
                    NPC npc = reg[i];
                    if (npc == null) continue;
                    NPCHealth h;
                    try { h = npc.Health; } catch { continue; }
                    if (h == null) continue;
                    try { if (h.IsDead || h.IsKnockedOut) continue; } catch { continue; }
                    float d;
                    try { d = (npc.transform.position - ppos).sqrMagnitude; } catch { continue; }
                    if (d < bestD) { bestD = d; best = npc; }
                }
                if (best == null) { Log.Msg("[Debug] no living NPC found to kill."); return; }

                Log.Msg($"[Debug] killing nearest NPC id={best.GetInstanceID()} (~{Mathf.Sqrt(bestD):F1}m) to make a test corpse.");
                best.Health.Die();
            }
            catch (Exception e) { Log.Warning("[Core] KillNearestNpc failed: " + e.Message); }
        }
#endif
    }
}
