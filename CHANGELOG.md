# Changelog

Last updated: 2026-08-19

## 1.0.0 - unreleased

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
