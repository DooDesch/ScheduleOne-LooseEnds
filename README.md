# Loose Ends

A MelonLoader (IL2CPP) mod for **Schedule I** that makes dead bodies actually matter.

> 🛟 **Need help or found a bug?** Get support at [support.doodesch.de](https://support.doodesch.de).

In vanilla, police and citizens walk over a corpse like it isn't there, and a body drags around as if it weighs as
much as a plastic bag. Loose Ends fixes both:

1. **Witnesses react.** A living NPC who actually *sees* a dead NPC reacts. A civilian witness pulls out their phone
   and **calls the police** over the game's own ~4 second call window - phone out, a progress icon over their head.
   That window is your chance: knock the witness out or kill them before the call connects and it stops, silencing the
   only witness. When the call connects, officers are sent to the **scene** (the body) and the game's own
   **"Investigating"** behaviour walks them there, searches, and only escalates to arresting you if an officer actually
   spots you - otherwise it times out and the case is dropped. A **police** officer who finds a body reports it
   instantly (you cannot phone-block a cop). A body hidden from sight - in a dumpster, behind a wall, indoors,
   underwater, behind cover - is never seen, so nothing happens. That is the whole point: hide your bodies or face the
   heat.
2. **Corpses are heavier.** While you drag a body it **resists being pulled** - it lags behind the carry point and
   drags sluggishly (and is heavier to throw), scaled by a configurable multiplier. It does **not** slow you down; the
   weight is in the body, not your legs.

Everything is configurable in the in-game **Mod Manager / Phone App** settings, including how punishing the police
response is.

## Requirements

- MelonLoader (IL2CPP build of Schedule I)
- [S1API](https://thunderstore.io) (Il2Cpp)
- Optional: **Mod Manager & Phone App** - if present, the settings appear in-game (phone + pause menu) and apply live.
  Without it the mod still works; settings live in `UserData/MelonPreferences.cfg` under `[LooseEnds_01_Main]`.

## How it works

- The mod keeps a small live set of NPC corpses (updated instantly on death and reconciled every ~2 s). When there are
  no corpses, it does effectively nothing, so there is no idle cost.
- A throttled scan asks each nearby living NPC whether it can see the body. There are two sight checks: the NPC's own
  vision cone (`VisionCone.IsPointWithinSight`, field-of-view + occlusion) within a believable `BodySightRange`, and a
  close-range notice within `NoticeRadius` (NPCs never look down at their feet, so a body lying flat is below their
  forward cone - this is what makes someone standing over a corpse actually react). Close-range line of sight is
  resolved by the game's own visibility solver (`EntityVisibility`), so a body in a dumpster / behind a wall / in
  bushes stays hidden, and partial cover counts as hidden.
- On the first sighting, after a short reaction delay, the witness reacts. A **civilian/employee** witness pulls out
  their phone via the game's native `CallPoliceBehaviour` (~4 s call, visible progress icon) - interruptible: down the
  witness before it connects and no police are called. A witness in active combat with you holds at "seen" and calls
  once the fight ends (a panicking witness still calls, exactly like vanilla). A **police** witness reports instantly.
- When the call connects (or for the instant police/`WitnessCallsPolice` off path), the suspect is raised to the
  configured pursuit level and officers are redirected to the **scene**: the suspect's `LastKnownPosition` is set to
  the body, so the game's Investigating behaviour sends officers to search there rather than chasing your live
  position. They escalate to arresting only if they actually spot you.
- Every body a witness sees gets its own response (`OncePerCorpse`); there is no global throttle, so a body left in
  front of someone is never silently ignored.
- Killer attribution is captured at attack time (the game stores no "killer" on a corpse), so the heat goes to whoever
  actually did it; a knockout falls back to the recent attacker. If the killer is unknown (an environmental or
  NPC-vs-NPC death), single-player blames the local player so there is still a response; co-op falls back sensibly.
- Getting arrested settles the whole spree: the killer registry is cleared and every tracked body is marked resolved,
  so you are not re-arrested for each corpse you left lying around.
- It is host-authoritative: detection and all police/pursuit writes run only on the server/host and use the game's own
  networked calls, so they replicate to clients. In a real co-op lobby the system stays off until you opt in
  (`EnableInMultiplayer`) and test it with two players.

## Settings (category `LooseEnds_01_Main`)

**Master**
- `Enabled` (true) - master switch.
- `EnableInMultiplayer` (false) - keep off in real co-op until tested with 2 players.

**Detection**
- `ReactCitizens` (true), `ReactPolice` (true), `ReactEmployees` (false) - which NPC types can witness (your crew
  ignores bodies by default).
- `RequireLineOfSight` (true) - use the vision cone (hidden bodies unseen). Off = pure radius (notices through walls).
- `UseVisionConeRange` (true) - rely on the NPC's own sight range. Off = use `DetectionRange` as an explicit radius.
- `DetectionRange` (12 m, range 3-40) - explicit notice radius used when `UseVisionConeRange` is off.
- `ReactionDelaySeconds` (3 s, range 0-30) - delay after the first sighting before the response fires.
- `ObserverCullRadius` (25 m, range 5-60) - performance: only NPCs within this distance of a corpse are considered.
- `BodySightRange` (12 m, range 4-30) - how far an NPC can notice a body through their vision cone. Capped below normal
  sight range, because a body lying flat is far less conspicuous than a standing person - otherwise a corpse in the
  open is spotted instantly by anyone nearby.
- `NoticeRadius` (6 m, range 2-15) - close-range notice radius. Any living NPC this close with a clear line of sight
  notices a body even though it is below their forward cone (line of sight still respected, so hidden stays hidden).
- `ScanIntervalSeconds` (0.4 s, range 0.1-2), `MaxRaycastsPerScan` (64, range 8-256) - scan cadence + per-tick cap.
- `ReactToKnockedOut` (false) - also react to merely unconscious NPCs (not just dead ones).

**Response (the "realism" dial)**
- `WitnessCallsPolice` (true) - a civilian witness pulls out their phone and calls over the game's own ~4 s window
  (visible progress icon); silence them before it connects to stop it. Off = police are alerted instantly. A police
  officer always reports instantly regardless.
- `RequirePlayerAlsoSeen` (false) - off: seeing the body is enough (hiding yourself is not enough, you must hide the
  body). On: the witness must also have you in sight before heat is applied (closer to vanilla crime-witnessing).
- `PursuitLevel` (1, range 1-4) - level to apply: 1 = Investigating, 2 = Arresting, 3 = NonLethal, 4 = Lethal.
- `OncePerCorpse` (true) - respond only once per corpse. Off = re-trigger allowed, subject to the cooldown below.
- `ResponseCooldownSeconds` (60 s, range 5-600) - per-corpse re-trigger gap, used only when `OncePerCorpse` is off. It
  does not throttle different bodies - every body a witness sees gets its own response.
- `AttributeUnknownToLocalPlayer` (true) - in single-player, blame the local player for an unattributed body so the
  investigation still has a suspect. Treated as off in co-op.

**Weight**
- `CorpseWeightMultiplier` (5, range 1-20) - how heavy a dragged body (dead or knocked-out) feels to pull. Higher = the
  body resists more: it lags behind the carry point, drags sluggishly, and is heavier to throw. 1 = vanilla. It does
  not slow the player.

## Building

```
dotnet build -c Debug     # dev build: includes config probes + verbose logging, auto-copies to the game Mods folder
dotnet build -c Release   # release build: all dev surface compiled out
```

The post-build step copies the DLL to `Mods/` (path set by `ModsDirectory` in the csproj).

## Verifying in-game

A few behaviours are best confirmed live in your own save. Build **Debug**, load a save, then:

1. **It loads cleanly.** Check the MelonLoader log for `Loose Ends v1.1.0` and `[Core] Witness system ACTIVE`
   (the latter appears once you are in a save on the host). No errors.
2. **Make a corpse.** Open the dev console and run `triggerlightning <npc_id>` (e.g. `triggerlightning cranky_frank`)
   to kill an NPC, or kill one yourself. The log should show `[Killer] NPC died ...` and `[Corpse] tracked new corpse`.
3. **Witnessing (open).** With the body in the open near other NPCs, the log should show
   `[Witness] corpse N SEEN by observer M`, then `[Reaction] witness ... CALLING police ...`. The civilian pulls out
   their phone; once the ~4 s call connects the log shows `[Reaction] call CONNECTED ...`, your pursuit shows
   **Investigating**, and officers head to the scene.
4. **Silence the witness.** Knock out or kill the calling witness before the ~4 s window elapses - the log should show
   `[Reaction] witness SILENCED before the call connected ...` and no police are called. (Build Debug and press F8 to
   get a weapon arsenal for this.)
5. **Hidden body.** Put a body in a dumpster / indoors / underwater / behind cover near the same NPCs. With
   `RequireLineOfSight` on it should NOT be seen and NOT trigger - this is the core mechanic.
6. **Weight.** Drag the body. It should clearly resist being pulled (lagging behind, heavy to throw) without slowing
   your own movement, and return to normal when dropped (`[Weight] drag start ...`).
7. **Deterministic dispatch test.** Toggle the Debug config button `ForceDiscoverNearest` to immediately run the
   response on the nearest corpse, bypassing line-of-sight and the delay.

Tuning knobs to confirm against your save: `ScanIntervalSeconds`, `BodySightRange`, `NoticeRadius`, and `PursuitLevel`.

## Status / known limitations

- **Multiplayer** is host-authoritative and off by default in co-op (`EnableInMultiplayer`) until a 2-player live test.
- Killer attribution covers player-inflicted deaths; environmental / NPC-vs-NPC deaths are still detected as bodies but
  have no player to blame (single-player can fall back to the local player; co-op falls back to no attribution).
