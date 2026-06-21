using MelonLoader;
using UnityEngine;

namespace LooseEnds.Config
{
    /// <summary>
    /// MelonPreferences wrapper. The category id is prefixed with the mod name ("LooseEnds_...") so it is
    /// auto-detected by the "Mod Manager &amp; Phone App" settings UI. Enum-like settings are stored int-backed
    /// (typo-proof, clamps trivially). The police response is a single behaviour: officers are sent to investigate the
    /// SCENE (the body), and the game's own Investigating behaviour drives the search/escalate/timeout from there. A
    /// "killer must also be seen" gate is available for a more vanilla feel. DEBUG-only probe entries register only in
    /// DEBUG builds.
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
        private static MelonPreferences_Entry<float> _bodySightRange;
        private static MelonPreferences_Entry<float> _noticeRadius;
        private static MelonPreferences_Entry<float> _scanInterval;
        private static MelonPreferences_Entry<int> _maxRaycastsPerScan;
        private static MelonPreferences_Entry<bool> _reactToKnockedOut;

        // ----- response -----
        private static MelonPreferences_Entry<bool> _witnessCallsPolice;
        private static MelonPreferences_Entry<bool> _requirePlayerSeen;
        private static MelonPreferences_Entry<int> _pursuitLevel;
        private static MelonPreferences_Entry<bool> _oncePerCorpse;
        private static MelonPreferences_Entry<float> _responseCooldown;
        private static MelonPreferences_Entry<bool> _attributeUnknownToLocal;

        // ----- weight -----
        private static MelonPreferences_Entry<float> _weightMultiplier;

#if DEBUG
        // One-shot "buttons" (toggle on -> action fires -> auto-reset to off) + dev toggles.
        private static MelonPreferences_Entry<bool> _btnForceDiscover;
        private static MelonPreferences_Entry<bool> _btnSpawnTestCorpse;
        private static MelonPreferences_Entry<bool> _btnGiveArsenal;
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
            _bodySightRange = Create("BodySightRange", 12f, "Body sight range (m)",
                "How far an NPC can NOTICE a body through their vision cone. A body lying flat on the ground is far less " +
                "conspicuous than a standing person, so this is capped well below the NPC's normal sight range - otherwise " +
                "a corpse in the open is spotted by anyone within ~25m and the response feels instant. Clamped 4-30.",
                new MelonLoader.Preferences.ValueRange<float>(4f, 30f));
            _noticeRadius = Create("NoticeRadius", 6f, "Close-range notice radius (m)",
                "NPCs never look down at their feet, so a body lying flat is below their forward vision cone. Any living " +
                "NPC within this radius with a clear line of sight (occlusion still respected) notices the body even if " +
                "it is not in their cone - this is what makes someone standing over a corpse actually react. Clamped 2-15.",
                new MelonLoader.Preferences.ValueRange<float>(2f, 15f));
            _scanInterval = Create("ScanIntervalSeconds", 0.4f, "Scan interval (s)",
                "How often the witness scan runs. Lower = faster notice, slightly more cost. Clamped 0.1-2.",
                new MelonLoader.Preferences.ValueRange<float>(0.1f, 2f));
            _maxRaycastsPerScan = Create("MaxRaycastsPerScan", 64, "Max sight checks per scan",
                "Performance cap: at most this many vision checks per scan tick (round-robin if exceeded). Clamped 8-256.",
                new MelonLoader.Preferences.ValueRange<int>(8, 256));
            _reactToKnockedOut = Create("ReactToKnockedOut", false, "React to unconscious NPCs",
                "OFF (default): only react to actually-dead NPCs. ON: also react to merely knocked-out NPCs.");

            // ----- response -----
            _witnessCallsPolice = Create("WitnessCallsPolice", true, "Witness phones the police (call window)",
                "ON (default): a civilian witness pulls out their phone and calls the police over ~4 seconds (the game's own " +
                "call animation, with a progress icon over their head). Knock them out or kill them before the call connects " +
                "to stop it - that is your chance to silence the only witness. OFF: the police are alerted instantly. " +
                "(A police officer who finds a body always reports it instantly - you cannot phone-block a cop.)");
            _requirePlayerSeen = Create("RequirePlayerAlsoSeen", false, "Killer must also be seen",
                "OFF (default): seeing the BODY is enough to start the response - hiding yourself is not enough, you must " +
                "hide the body. ON: the discovering NPC must ALSO have the killer in sight before heat is applied (closer " +
                "to vanilla crime-witnessing).");
            _pursuitLevel = Create("PursuitLevel", 1, "Pursuit level to apply (1-4)",
                "Maps to the game's pursuit levels: 1 = Investigating, 2 = Arresting, 3 = NonLethal, 4 = Lethal. Default 1 " +
                "(Investigating - the status named in the request).",
                new MelonLoader.Preferences.ValueRange<int>(1, 4));
            _oncePerCorpse = Create("OncePerCorpse", true, "Respond only once per corpse",
                "ON (default): a corpse that has already triggered a response will not trigger again. OFF: re-trigger is " +
                "allowed, subject to the response cooldown.");
            _responseCooldown = Create("ResponseCooldownSeconds", 60f, "Re-trigger cooldown (s)",
                "Per-corpse re-trigger gap used ONLY when 'Respond only once per corpse' is OFF (how long before the same " +
                "body can raise a fresh response). It does NOT throttle different bodies - every body a witness sees gets " +
                "its own response. Clamped 5-600.",
                new MelonLoader.Preferences.ValueRange<float>(5f, 600f));
            _attributeUnknownToLocal = Create("AttributeUnknownToLocalPlayer", true, "Blame local player when killer unknown",
                "ON (default, single-player): if a discovered corpse has no recorded killer, attribute it to the local " +
                "player so 'Hunt' still has a target. In a co-op lobby this is treated as OFF (an unattributed body falls " +
                "back to a scene response instead of blaming a random player).");

            // ----- weight -----
            _weightMultiplier = Create("CorpseWeightMultiplier", 5f, "Dragged body weight x",
                "How heavy a dragged body (dead OR knocked-out) feels to pull. Higher = the body resists more - it lags " +
                "behind the carry point and drags sluggishly (and is heavier to throw). Does NOT slow the player. " +
                "1 = vanilla. Clamped 1-20.",
                new MelonLoader.Preferences.ValueRange<float>(1f, 20f));

#if DEBUG
            _btnForceDiscover = Create("ForceDiscoverNearest", false, "> Force-discover nearest corpse (one-shot)",
                "Toggle ON to immediately run the response on the nearest corpse, bypassing line-of-sight and delay. Auto-resets.");
            _btnSpawnTestCorpse = Create("SpawnTestCorpse", false, "> Spawn test corpse (one-shot)",
                "Toggle ON to produce a throwaway corpse near you to witness. Auto-resets.");
            _btnGiveArsenal = Create("GiveWeaponArsenal", false, "> Give weapon arsenal (one-shot)",
                "Toggle ON (or press F8) to drop a set of weapons into your inventory - for testing the 'silence the " +
                "witness mid-call' flow (you need a weapon to KO/kill the caller during their ~4s phone call). Auto-resets.");
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
        internal static float BodySightRange => Mathf.Clamp(_bodySightRange?.Value ?? 12f, 4f, 30f);
        internal static float NoticeRadius => Mathf.Clamp(_noticeRadius?.Value ?? 6f, 2f, 15f);
        internal static float ScanIntervalSeconds => Mathf.Clamp(_scanInterval?.Value ?? 0.4f, 0.1f, 2f);
        internal static int MaxRaycastsPerScan => Mathf.Clamp(_maxRaycastsPerScan?.Value ?? 64, 8, 256);
        internal static bool ReactToKnockedOut => _reactToKnockedOut?.Value ?? false;

        internal static bool WitnessCallsPolice => _witnessCallsPolice?.Value ?? true;
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
        internal static bool OncePerCorpse => _oncePerCorpse?.Value ?? true;
        internal static float ResponseCooldownSeconds => Mathf.Clamp(_responseCooldown?.Value ?? 60f, 5f, 600f);
        internal static bool AttributeUnknownToLocalPlayer => _attributeUnknownToLocal?.Value ?? true;

        internal static float CorpseWeightMultiplier => Mathf.Clamp(_weightMultiplier?.Value ?? 5f, 1f, 20f);

#if DEBUG
        internal static bool LogWitnessScan => _logWitnessScan?.Value ?? false;

        internal static bool ConsumeForceDiscover() => Consume(_btnForceDiscover);
        internal static bool ConsumeSpawnTestCorpse() => Consume(_btnSpawnTestCorpse);
        internal static bool ConsumeGiveArsenal() => Consume(_btnGiveArsenal);

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
