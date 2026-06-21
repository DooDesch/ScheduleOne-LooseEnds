# Changelog

All notable changes to Loose Ends are documented here. This project adheres to
[Semantic Versioning](https://semver.org/).

## [1.1.0] - 2026-06-21

A reworked, more believable witness-to-police flow.

### Changed
- **Civilians now phone the police for real.** Instead of an instant, invisible dispatch, a civilian witness pulls out
  their phone and calls over the game's own ~4 second call window (native `CallPoliceBehaviour`, with a progress icon
  over their head). The call is interruptible: knock the witness out or kill them before it connects and no police are
  called - silence the only witness. A police officer who finds a body still reports it instantly.
- **Officers are redirected to the scene.** When the call connects, the suspect's last-known position is set to the
  body, so the game's Investigating behaviour sends officers to search the scene rather than chasing your live
  position. They escalate to arresting only if they actually spot you; otherwise the case times out and is dropped.
- **Tighter, more believable detection.** A body lying flat is now only noticed within a new `BodySightRange` (capped
  well below an NPC's normal sight range, since a corpse on the ground is far less conspicuous than a standing person),
  and close-range line of sight is resolved by the game's own visibility solver (`EntityVisibility`) - so partial cover
  (bushes, props) and dumpsters genuinely hide a body.
- **Corpse weight is in the body, not your legs.** Dragging a body now makes the body resist being pulled (it lags
  behind the carry point, drags sluggishly, and is heavier to throw) instead of slowing the player down.

### Added
- New settings `WitnessCallsPolice`, `BodySightRange`, and `NoticeRadius`.

### Fixed
- The reaction is no longer globally throttled - every body a witness sees gets its own response, so a body left in
  front of someone is never silently ignored.
- A witness in active combat with the player is held at "seen" and calls once the fight ends (a panicking witness still
  calls, exactly like vanilla); killing the witness mid-call now genuinely stops the call.
- The corpse-weight physics are restored when an NPC is un-tracked (e.g. respawns on a new day), so the heavy-body
  modifier never leaks onto a living NPC.
- Getting arrested now clears killer attribution for all outstanding bodies, so a single arrest settles the whole
  spree instead of re-arresting you for each corpse.

### Removed
- The old `ResponseMode` (Hunt / Scene / Escalating), `WeightMode`, and the `UseSetPursuitLevel`, `UsePoliceCalled`,
  `RaiseLawIntensity`, and `UseCitizenCallAnimation` levers - folded into the single native call-and-scene flow.

## [1.0.0] - 2026-06-20

Initial release.

### Added
- **Witnesses react.** Living NPCs (citizens and police) who actually see a dead NPC report it: citizens
  get the police called, police enter the game's real "Investigating" pursuit. Bodies hidden from sight
  (dumpster, indoors, underwater, behind cover) are not seen, so the gameplay is "hide your bodies".
- **Real line of sight** via each NPC's own vision cone (`VisionCone.IsPointWithinSight`), honoring field
  of view and the game's occlusion layers.
- **Killer attribution** captured at attack time, so the heat goes to whoever did it; unknown
  (environmental / NPC-vs-NPC) deaths fall back sensibly (single-player blames the local player, co-op uses
  a scene response).
- **Heavier corpses.** A carried corpse is made heavier (configurable, default ~5x) so disposal has weight
  and stress - it slows the carrier down and the body drags more sluggishly.
- **Configurable realism.** Hunt the killer / Scene investigation / Escalating, line-of-sight on/off, which
  NPC types react, pursuit level, reaction delay, cooldowns, weight, and more - in the in-game Mod Manager /
  Phone App or `MelonPreferences.cfg`.
- **Cheap when idle** - the throttled witness scan only runs while a corpse exists.
- **Host-authoritative;** multiplayer is off by default (`EnableInMultiplayer`) until tested with two players.
