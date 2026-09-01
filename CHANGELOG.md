# Changelog

Last updated: 2026-08-31

## 1.0.2

- Fixed dropping or picking up an item while crouched breaking the player
  animation, camera, and movement.
- Fixed equipping OR dropping a weapon while crouched leaving the first-person
  arms about a meter below the camera in every walking, sprinting, strafing,
  jumping, and falling state (only the idle and crouch states author the arms
  height; the rest write back a default pose Unity captures at each controller
  swap, in whatever stance the player held). Both swap seams — equip and the
  teardown restore — now normalize the arms chain and evaluate the controller's
  default standing state once before replaying the live stance, so the captured
  default is always the standing rest. A stance safety net additionally lifts
  the arms if a capture is ever poisoned by a path outside these seams.
- Fixed the camera briefly jerking up and back down on a crouched drop: the
  restored base layer now re-enters the crouch states instantly instead of
  crossfading from the standing idle.
- Detect duplicate installs at startup: a second `Y4NGZInteractions.dll`
  anywhere in the plugins tree now logs a `duplicate_install_detected` error
  naming the stale path, because consumers can bind to the skipped copy and see
  `interaction_animation_api_not_initialized` for the whole session. The
  `api.initialized` log line now also carries the loaded assembly's version and
  location.
- Fixed the teardown camera pin baking a permanent camera offset: it now holds
  the viewpoint by moving the camera container instead of the gameplay camera's
  world position, and restores the camera's pristine local position when it
  releases, so a crouched drop can no longer leave the next interaction's
  viewpoint below the floor.
- Fixed crouched walking with a live-body weapon detaching the arms from the
  camera after a jump or a fall.
- Fixed crouch-walking playing the sprint arms animation.
- Fixed crouching sometimes playing twice during live-body interactions.

## 1.0.1

- Survive BepInEx `HideManagerGameObject=false`: the runtime host owns its
  lifecycle on a `DontDestroyOnLoad` object, and teardown only runs on a real
  application quit. Fixes the API dying immediately after chainloader in some
  mod-manager profiles.
- Added IK bake telemetry and a degenerate-input warning to `IkBakeProbe`.

## 1.0.0

- Finalized the standalone local presentation, ownership, and restoration
  contract for BodyWorld and DedicatedLocalViewmodel interactions.
- Added reject-if-busy and transactional interrupt-existing conflict policies,
  per-player resource leases, immutable registration snapshots, and exactly-once
  completion events after restoration.
- Added strict, path-specific schema-2 validation while retaining schema-1 JSON
  migration for the 1.x line, including legacy prop-bone lookup compatibility.
- Added deterministic stop handling for invalidation, death, round unload,
  presenter failure, interruption, requested stop, natural end, and shutdown.
- Decoupled live-body camera semantics, preserved crouch and stance continuity
  across swaps, and drove locomotion parameters for remote-player sessions.
- Made first-person camera pinning stance-relative and reduced routine interaction
  log noise.
- Removed the unused backend abstraction, production hotkey probe, and Input
  System dependency.
- Made profile deployment opt-in and centralized version 1.0.0 for the assembly,
  plugin metadata, and package stager.
- Added behavioral tests, public API analysis, clean-room examples, authoring
  validators, Markdown checks, Windows CI, and deterministic package verification.
- Restricted the release archive to the DLL, icon, README, license, changelog,
  and manifest.

Clean-profile gameplay, multiplayer ownership/restoration, crouch/viewpoint
behavior, downstream consumer rebuilds, and final package inspection remain
required before publication.
