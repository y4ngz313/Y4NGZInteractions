# Changelog

Last updated: 2026-09-02

## 1.0.2

- Fixed picking up or dropping an item while crouched breaking the player
  animation, camera, and movement.
- Fixed equipping or dropping a weapon while crouched leaving the first-person
  arms below the camera while moving, and movement stalling and snapping on
  every walk start afterwards.
- Fixed the top edge of the visor clipping for one frame on every weapon
  pickup and drop.
- Fixed the camera jumping for one frame, and briefly jerking up and back down,
  on a crouched drop.
- Fixed a crouched drop leaving the next interaction's viewpoint below the
  floor.
- Fixed the first-person arms showing one stale frame at the start of an
  interaction.
- Fixed crouched walking with a live-body weapon detaching the arms from the
  camera after a jump or a fall.
- Fixed crouch-walking playing the sprint arms animation.
- Fixed crouching sometimes playing twice during live-body interactions.
- A second `Y4NGZInteractions.dll` anywhere in the plugins tree is now reported
  at startup with its path, and the startup log names the loaded version and
  location.
- New config keys `Own Arms Metarig Write-Back` and `Own Metarig Root
  Write-Back`, both on by default.

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
