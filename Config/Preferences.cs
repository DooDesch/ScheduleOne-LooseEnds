using MelonLoader;
using UnityEngine;

namespace LooseEnds.Config
{
    /// <summary>How the police respond when a corpse is discovered.</summary>
    internal enum ResponseMode
    {
        /// <summary>Apply heat to the killer-player (pursuit -> Investigating + PoliceCalled). Reliable default.</summary>
        Hunt = 0,
        /// <summary>Route officers to the body location only (experimental - the game API is player-keyed).</summary>
        Scene = 1,
        /// <summary>Scene first, escalate to Hunt once an officer reaches the body / the killer is also seen.</summary>
        Escalating = 2,
    }

    /// <summary>What to scale to make a carried corpse heavier.</summary>
    internal enum WeightMode
    {
        /// <summary>Divide Draggable.DragForceMultiplier - sluggish drag feel, leaves throw/NavMesh physics vanilla.</summary>
        DragForce = 0,
        /// <summary>Multiply Rigidbody.mass - physically correct, interacts with DragManager.MassInfluence.</summary>
        Mass = 1,
        /// <summary>Both of the above.</summary>
        Both = 2,
    }

    /// <summary>
    /// MelonPreferences wrapper. The category id is prefixed with the mod name ("LooseEnds_...") so it is
    /// auto-detected by the "Mod Manager &amp; Phone App" settings UI. Enum-like settings are stored int-backed
    /// (typo-proof, clamps trivially) and exposed as the strong enum via the accessors. The realism of the police
    /// response is fully user-configurable per the design (Hunt / Scene / Escalating, plus a "must also see the
    /// player" gate). DEBUG-only probe entries register only in DEBUG builds.
    /// </summary>
    internal static class Preferences
    {
        private const string CategoryId = "LooseEnds_01_Main";

        private static MelonPreferences_Category _category;

        // ----- master -----
        private static MelonPreferences_Entry<bool> _enabled;
        private static MelonPreferences_Entry<bool> _enableInMp;

        // ----- detection -----
        private static MelonPreferences_Entry<bool> _reactCitizens;
        private static MelonPreferences_Entry<bool> _reactPolice;
        private static MelonPreferences_Entry<bool> _reactEmployees;
        private static MelonPreferences_Entry<bool> _requireLos;
        private static MelonPreferences_Entry<bool> _useVisionRange;
        private static MelonPreferences_Entry<float> _detectionRange;
        private static MelonPreferences_Entry<float> _reactionDelay;
        private static MelonPreferences_Entry<float> _observerCullRadius;
        private static MelonPreferences_Entry<float> _scanInterval;
        private static MelonPreferences_Entry<int> _maxRaycastsPerScan;
        private static MelonPreferences_Entry<bool> _reactToKnockedOut;

        // ----- response -----
        private static MelonPreferences_Entry<int> _responseMode;
        private static MelonPreferences_Entry<bool> _requirePlayerSeen;
        private static MelonPreferences_Entry<int> _pursuitLevel;
        private static MelonPreferences_Entry<bool> _raiseLawIntensity;
        private static MelonPreferences_Entry<bool> _useSetPursuitLevel;
        private static MelonPreferences_Entry<bool> _usePoliceCalled;
        private static MelonPreferences_Entry<bool> _useCitizenCallAnim;
        private static MelonPreferences_Entry<bool> _oncePerCorpse;
        private static MelonPreferences_Entry<float> _responseCooldown;
        private static MelonPreferences_Entry<bool> _attributeUnknownToLocal;

        // ----- weight -----
        private static MelonPreferences_Entry<float> _weightMultiplier;
        private static MelonPreferences_Entry<int> _weightMode;

#if DEBUG
        // One-shot "buttons" (toggle on -> action fires -> auto-reset to off) + dev toggles.
        private static MelonPreferences_Entry<bool> _btnForceDiscover;
        private static MelonPreferences_Entry<bool> _btnSpawnTestCorpse;
        private static MelonPreferences_Entry<bool> _logWitnessScan;
        private static MelonPreferences_Entry<int> _forcePursuitLevel;
#endif

        internal static void Initialize()
        {
            if (_category != null)
            {
                return;
            }

#if DEBUG
            _category = MelonPreferences.CreateCategory(CategoryId, "Loose Ends (Witness + Dev)");
#else
            _category = MelonPreferences.CreateCategory(CategoryId, "Loose Ends");
#endif

            // ----- master -----
            _enabled = Create("Enabled", true, "Enabled",
                "Master switch. When ON, NPCs who SEE a dead body react and carried corpses are heavier. OFF = fully vanilla.");
            _enableInMp = Create("EnableInMultiplayer", false, "Enable in multiplayer (experimental)",
                "OFF (default): the witness system auto-disables in a real co-op lobby until it has been tested with 2 " +
                "players. ON: force it on - it is host-authoritative and uses the game's own networked police calls, but " +
                "verify pursuit syncs to all clients first.");

            // ----- detection -----
            _reactCitizens = Create("ReactCitizens", true, "Citizens react",
                "Civilian NPCs who see a corpse call the police.");
            _reactPolice = Create("ReactPolice", true, "Police react",
                "Police who see a corpse begin investigating.");
            _reactEmployees = Create("ReactEmployees", false, "Employees react",
                "Your hired employees (dealers/handlers) react to a corpse. OFF by default - they are your crew.");
            _requireLos = Create("RequireLineOfSight", true, "Require line of sight",
                "ON (default): use the NPC's vision cone, so a body behind a wall / in a dumpster / indoors / underwater is " +
                "NOT seen (this is the whole 'hide the body' mechanic). OFF: a pure radius check (notices through walls).");
            _useVisionRange = Create("UseVisionConeRange", true, "Use the NPC's own vision range",
                "ON (default): rely on the game's vision-cone distance. OFF: use 'Detection range' below as an explicit radius.");
            _detectionRange = Create("DetectionRange", 12f, "Detection range (m)",
                "Explicit notice radius used when 'Use the NPC's own vision range' is OFF. Clamped 3-40.",
                new MelonLoader.Preferences.ValueRange<float>(3f, 40f));
            _reactionDelay = Create("ReactionDelaySeconds", 3f, "Reaction delay (s)",
                "After the first sighting, wait this long before the response fires (the NPC 'processes' the scene; also " +
                "debounces a quick glance). Clamped 0-30.",
                new MelonLoader.Preferences.ValueRange<float>(0f, 30f));
            _observerCullRadius = Create("ObserverCullRadius", 25f, "Observer cull radius (m)",
                "Performance: only NPCs within this distance of a corpse are even considered as witnesses. Clamped 5-60.",
                new MelonLoader.Preferences.ValueRange<float>(5f, 60f));
            _scanInterval = Create("ScanIntervalSeconds", 0.4f, "Scan interval (s)",
                "How often the witness scan runs. Lower = faster notice, slightly more cost. Clamped 0.1-2.",
                new MelonLoader.Preferences.ValueRange<float>(0.1f, 2f));
            _maxRaycastsPerScan = Create("MaxRaycastsPerScan", 64, "Max sight checks per scan",
                "Performance cap: at most this many vision checks per scan tick (round-robin if exceeded). Clamped 8-256.",
                new MelonLoader.Preferences.ValueRange<int>(8, 256));
            _reactToKnockedOut = Create("ReactToKnockedOut", false, "React to unconscious NPCs",
                "OFF (default): only react to actually-dead NPCs. ON: also react to merely knocked-out NPCs.");

            // ----- response -----
            _responseMode = Create("ResponseMode", (int)ResponseMode.Hunt, "Response mode (0 Hunt, 1 Scene, 2 Escalating)",
                "0 = Hunt the killer (heat applied to the killer-player: pursuit -> Investigating + police dispatched; the " +
                "reliable default). 1 = Scene investigation only (officers routed to the body; experimental). 2 = Escalating " +
                "(Scene first, escalate to Hunt once an officer reaches the body / the killer is also seen).",
                new MelonLoader.Preferences.ValueRange<int>(0, 2));
            _requirePlayerSeen = Create("RequirePlayerAlsoSeen", false, "Killer must also be seen",
                "OFF (default): seeing the BODY is enough to start the response - hiding yourself is not enough, you must " +
                "hide the body. ON: the discovering NPC must ALSO have the killer in sight before heat is applied (closer " +
                "to vanilla crime-witnessing).");
            _pursuitLevel = Create("PursuitLevel", 1, "Pursuit level to apply (1-4)",
                "Maps to the game's pursuit levels: 1 = Investigating, 2 = Arresting, 3 = NonLethal, 4 = Lethal. Default 1 " +
                "(Investigating - the status named in the request).",
                new MelonLoader.Preferences.ValueRange<int>(1, 4));
            _raiseLawIntensity = Create("RaiseLawIntensity", false, "Also raise law intensity",
                "ON: in addition to the pursuit level, nudge the global law-enforcement intensity so more police respond. " +
                "OFF (default): only set the pursuit level. (Used as a fallback if a dispatch no-ops at zero heat.)");
            _useSetPursuitLevel = Create("UseSetPursuitLevel", true, "Use SetPursuitLevel",
                "Apply the pursuit level directly on the killer (the in-game 'Investigating' status). Leave ON.");
            _usePoliceCalled = Create("UsePoliceCalled", true, "Use PoliceCalled dispatch",
                "Route through the game's LawManager.PoliceCalled (spawns/redirects officers with a DeadlyAssault crime). " +
                "Leave ON. (Both this and SetPursuitLevel are independent so the effective behavior can be tuned.)");
            _useCitizenCallAnim = Create("UseCitizenCallAnimation", false, "Citizen plays 'call police' animation",
                "OFF (default): a discovering civilian just triggers the police response. ON (immersive, riskier): drive the " +
                "discoverer's call-police behaviour so they visibly pull out a phone first.");
            _oncePerCorpse = Create("OncePerCorpse", true, "Respond only once per corpse",
                "ON (default): a corpse that has already triggered a response will not trigger again. OFF: re-trigger is " +
                "allowed, subject to the response cooldown.");
            _responseCooldown = Create("ResponseCooldownSeconds", 60f, "Response cooldown (s)",
                "Minimum seconds between dispatches (anti-spam), and the per-corpse re-trigger gap when 'Respond only once " +
                "per corpse' is OFF. Clamped 5-600.",
                new MelonLoader.Preferences.ValueRange<float>(5f, 600f));
            _attributeUnknownToLocal = Create("AttributeUnknownToLocalPlayer", true, "Blame local player when killer unknown",
                "ON (default, single-player): if a discovered corpse has no recorded killer, attribute it to the local " +
                "player so 'Hunt' still has a target. In a co-op lobby this is treated as OFF (an unattributed body falls " +
                "back to a scene response instead of blaming a random player).");

            // ----- weight -----
            _weightMultiplier = Create("CorpseWeightMultiplier", 5f, "Carried corpse weight x",
                "How heavy a carried corpse feels (default 5). Primarily slows you down while carrying a body (x5 ~ half " +
                "speed, x10 ~ a third, floored so you can always crawl) and makes the body drag more sluggishly. " +
                "1 = vanilla. Clamped 1-20.",
                new MelonLoader.Preferences.ValueRange<float>(1f, 20f));
            _weightMode = Create("WeightMode", (int)WeightMode.DragForce, "Weight mode (0 DragForce, 1 Mass, 2 Both)",
                "Secondary drag-physics tweak on the body itself (the carry slowdown applies regardless). 0 = DragForce " +
                "(body lags behind, safest). 1 = Rigidbody mass (affects throw/collisions; the drag spring is " +
                "mass-independent). 2 = both.",
                new MelonLoader.Preferences.ValueRange<int>(0, 2));

#if DEBUG
            _btnForceDiscover = Create("ForceDiscoverNearest", false, "> Force-discover nearest corpse (one-shot)",
                "Toggle ON to immediately run the response on the nearest corpse, bypassing line-of-sight and delay. Auto-resets.");
            _btnSpawnTestCorpse = Create("SpawnTestCorpse", false, "> Spawn test corpse (one-shot)",
                "Toggle ON to produce a throwaway corpse near you to witness. Auto-resets.");
            _logWitnessScan = Create("LogWitnessScan", false, "Debug: log witness scans",
                "Verbose per-scan log of tracked corpses, culled observers, sightings and dispatches.");
            _forcePursuitLevel = Create("ForcePursuitLevel", 0, "Debug: force pursuit level (0 = off)",
                "If > 0, every dispatch uses this pursuit level (1-4) regardless of the configured value - for testing each level.",
                new MelonLoader.Preferences.ValueRange<int>(0, 4));
#endif
        }

        private static MelonPreferences_Entry<T> Create<T>(string id, T def, string name, string desc = null,
            MelonLoader.Preferences.ValueValidator validator = null)
        {
            return validator == null
                ? _category.CreateEntry(id, def, name, desc)
                : _category.CreateEntry(id, def, name, desc, false, false, validator);
        }

        // ----- accessors -----

        internal static bool Enabled => _enabled?.Value ?? true;
        internal static bool EnableInMultiplayer => _enableInMp?.Value ?? false;

        internal static bool ReactCitizens => _reactCitizens?.Value ?? true;
        internal static bool ReactPolice => _reactPolice?.Value ?? true;
        internal static bool ReactEmployees => _reactEmployees?.Value ?? false;
        internal static bool RequireLineOfSight => _requireLos?.Value ?? true;
        internal static bool UseVisionConeRange => _useVisionRange?.Value ?? true;
        internal static float DetectionRange => Mathf.Clamp(_detectionRange?.Value ?? 12f, 3f, 40f);
        internal static float ReactionDelaySeconds => Mathf.Clamp(_reactionDelay?.Value ?? 3f, 0f, 30f);
        internal static float ObserverCullRadius => Mathf.Clamp(_observerCullRadius?.Value ?? 25f, 5f, 60f);
        internal static float ScanIntervalSeconds => Mathf.Clamp(_scanInterval?.Value ?? 0.4f, 0.1f, 2f);
        internal static int MaxRaycastsPerScan => Mathf.Clamp(_maxRaycastsPerScan?.Value ?? 64, 8, 256);
        internal static bool ReactToKnockedOut => _reactToKnockedOut?.Value ?? false;

        internal static ResponseMode Mode => (ResponseMode)Mathf.Clamp(_responseMode?.Value ?? 0, 0, 2);
        internal static bool RequirePlayerAlsoSeen => _requirePlayerSeen?.Value ?? false;
        /// <summary>Configured pursuit level as a raw int (1-4). The dispatcher casts it to the game enum.</summary>
        internal static int PursuitLevelInt
        {
            get
            {
#if DEBUG
                int forced = _forcePursuitLevel?.Value ?? 0;
                if (forced > 0) return Mathf.Clamp(forced, 1, 4);
#endif
                return Mathf.Clamp(_pursuitLevel?.Value ?? 1, 1, 4);
            }
        }
        internal static bool RaiseLawIntensity => _raiseLawIntensity?.Value ?? false;
        internal static bool UseSetPursuitLevel => _useSetPursuitLevel?.Value ?? true;
        internal static bool UsePoliceCalled => _usePoliceCalled?.Value ?? true;
        internal static bool UseCitizenCallAnimation => _useCitizenCallAnim?.Value ?? false;
        internal static bool OncePerCorpse => _oncePerCorpse?.Value ?? true;
        internal static float ResponseCooldownSeconds => Mathf.Clamp(_responseCooldown?.Value ?? 60f, 5f, 600f);
        internal static bool AttributeUnknownToLocalPlayer => _attributeUnknownToLocal?.Value ?? true;

        internal static float CorpseWeightMultiplier => Mathf.Clamp(_weightMultiplier?.Value ?? 5f, 1f, 20f);
        internal static WeightMode Weight => (WeightMode)Mathf.Clamp(_weightMode?.Value ?? 0, 0, 2);

#if DEBUG
        internal static bool LogWitnessScan => _logWitnessScan?.Value ?? false;

        internal static bool ConsumeForceDiscover() => Consume(_btnForceDiscover);
        internal static bool ConsumeSpawnTestCorpse() => Consume(_btnSpawnTestCorpse);

        private static bool Consume(MelonPreferences_Entry<bool> entry)
        {
            if (entry != null && entry.Value)
            {
                entry.Value = false;   // in-memory reset; avoids a save -> OnPreferencesSaved loop
                return true;
            }
            return false;
        }
#endif
    }
}
