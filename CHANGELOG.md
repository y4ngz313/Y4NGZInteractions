# Changelog

Last updated: 2026-08-08

## 1.0.0 - release candidate

- Finalized the standalone local presentation, ownership, and restoration contract.
- Kept BodyWorld and DedicatedLocalViewmodel; removed the misleading Hybrid option.
- Added RejectIfBusy and transactional InterruptExisting conflict policies.
- Added resource leases so different remote players can coexist while conflicting local/body resources cannot.
- Added immutable pack registration snapshots and strict, path-specific schema-2 validation reports.
- Retained schema-1 JSON migration for the 1.x line with an explicit warning.
- Added InteractionEnded after restoration, float parameter support, and presentation-specific active queries.
- Added deterministic stop handling for invalidation, death, round unload, presenter failure, interruption, requested stop, natural end, and shutdown.
- Removed the unused backend abstraction, production hotkey probe, and Input System dependency.
- Made profile deployment opt-in and centralized version 1.0.0 for assembly, plugin, and package staging.
- Added behavioral tests, public API analysis, Windows CI, clean-room examples, authoring validators, and community documentation.
- Restricted the release archive to the DLL, icon, README, license, changelog, and manifest.

Human multiplayer, clean-profile, viewpoint, downstream rebuild, and independent-doc-following gates remain required before publication.
