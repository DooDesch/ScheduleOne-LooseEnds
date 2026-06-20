# Loose Ends

A MelonLoader (IL2CPP) mod for **Schedule I** that makes dead bodies actually matter.

In vanilla, police and citizens walk over a corpse like it isn't there, and a body drags around as if it weighs as
much as a plastic bag. Loose Ends fixes both:

1. **Witnesses react.** A living NPC (citizen or police) who actually *sees* a dead NPC reports it: citizens get the
   police called, police enter the game's real **"Investigating"** pursuit state. A body that is hidden from sight -
   in a dumpster, behind a wall, indoors, underwater, in bushes - is never seen, so nothing happens. That is the whole
   point: hide your bodies or face the heat.
2. **Corpses are heavier.** A carried corpse is made about 5x heavier by default (configurable), adding weight and
   stress to disposal.

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
- A throttled scan asks each nearby living NPC's own vision cone whether it can see the body
  (`VisionCone.IsPointWithinSight`), which honors field-of-view and the game's occlusion layers - so a hidden body is
  genuinely unseen.
- On the first sighting, after a short reaction delay, the configured police response fires.
- Killer attribution is captured at attack time (the game stores no "killer" on a corpse), so the heat goes to whoever
  actually did it. If the killer is unknown (an environmental or NPC-vs-NPC death), single-player blames the local
  player so there is still a response; co-op falls back to a scene response.
- It is host-authoritative: detection and all police/pursuit writes run only on the server/host and use the game's own
  networked calls, so they replicate to clients. In a real co-op lobby the system stays off until you opt in
  (`EnableInMultiplayer`) and test it with two players.

## Settings (category `LooseEnds_01_Main`)

**Master**
- `Enabled` (true) - master switch.
- `EnableInMultiplayer` (false) - keep off in real co-op until tested with 2 players.

**Detection**
- `ReactCitizens` (true), `ReactPolice` (true), `ReactEmployees` (false) - which NPC types can witness.
- `RequireLineOfSight` (true) - use the vision cone (hidden bodies unseen). Off = pure radius (notices through walls).
- `UseVisionConeRange` (true) - rely on the NPC's own sight range. Off = use `DetectionRange` as an explicit radius.
- `DetectionRange` (12 m), `ReactionDelaySeconds` (3 s), `ObserverCullRadius` (25 m), `ScanIntervalSeconds` (0.4 s),
  `MaxRaycastsPerScan` (64) - tuning + performance caps.
- `ReactToKnockedOut` (false) - also react to merely unconscious NPCs.

**Response (the "realism" dial)**
- `ResponseMode` (0 = Hunt, 1 = Scene, 2 = Escalating). **Hunt** applies heat to the killer (reliable default).
  **Scene** routes officers to investigate the body (experimental). **Escalating** does Scene then Hunt.
- `RequirePlayerAlsoSeen` (false) - require the witness to also see the killer before heat is applied (more lenient).
- `PursuitLevel` (1 = Investigating; 2 Arresting, 3 NonLethal, 4 Lethal) - how hard the police come.
- `UseSetPursuitLevel` (true) / `UsePoliceCalled` (true) - the two independent levers used to apply the response.
- `RaiseLawIntensity` (false) - also bump global police presence.
- `UseCitizenCallAnimation` (false) - immersive "pull out phone" animation on the discovering civilian (experimental).
- `OncePerCorpse` (true), `ResponseCooldownSeconds` (60 s) - anti-spam.
- `AttributeUnknownToLocalPlayer` (true) - blame the local player for an unattributed body in single-player.

**Weight**
- `CorpseWeightMultiplier` (5, range 1-20) - how heavy a carried corpse feels.
- `WeightMode` (0 = DragForce feel only, 1 = real Rigidbody mass, 2 = both). DragForce is safest (leaves throwing vanilla).

## Building

```
dotnet build -c Debug     # dev build: includes config probes + verbose logging, auto-copies to the game Mods folder
dotnet build -c Release   # release build: all dev surface compiled out
```

The post-build step copies the DLL to `Mods/` (path set by `ModsDirectory` in the csproj).

## Verifying in-game (Phase 0 checklist)

A few behaviours are best confirmed live in your own save. Build **Debug**, load a save, then:

1. **It loads cleanly.** Check the MelonLoader log for `Loose Ends v1.0.0` and `[Core] Witness system ACTIVE`
   (the latter appears once you are in a save on the host). No errors.
2. **Make a corpse.** Open the dev console and run `triggerlightning <npc_id>` (e.g. `triggerlightning cranky_frank`)
   to kill an NPC, or kill one yourself. The log should show `[Killer] NPC died ...` and `[Corpse] tracked new corpse`.
3. **Witnessing (open).** With the body in the open near other NPCs, the log should show
   `[Witness] corpse N SEEN by observer M`, then after the reaction delay `[Reaction] HUNT corpse=N ...`. The player's
   pursuit should show **Investigating** and police should respond.
4. **Hidden body.** Put a body in a dumpster / indoors / underwater near the same NPCs. With `RequireLineOfSight` on it
   should NOT be seen and NOT trigger - this is the core mechanic.
5. **Weight.** Drag the body. It should feel clearly heavier than vanilla but still movable, and return to normal when
   dropped (`[Weight] corpse drag start ...`).
6. **Deterministic dispatch test.** Toggle the Debug config button `ForceDiscoverNearest` to immediately run the
   response on the nearest corpse, bypassing line-of-sight and the delay.

Tuning knobs to confirm against your save: `ScanIntervalSeconds`, `DetectionRange`, `PursuitLevel`, and whether
`SetPursuitLevel` alone vs `PoliceCalled` produces the response you want (both are independently toggleable).

## Status / known limitations

- **Hunt mode** is the reliable, supported path. **Scene mode** is experimental: the game's police API is player-keyed,
  so "send officers to a bare position" is synthesized (nearest officer + body-search of a known suspect) and is clearly
  logged as experimental.
- **Multiplayer** is host-authoritative and off by default in co-op (`EnableInMultiplayer`) until a 2-player live test.
- Killer attribution covers player-inflicted deaths; environmental / NPC-vs-NPC deaths are still detected as bodies but
  have no player to blame (single-player can fall back to the local player; co-op uses a scene response).
