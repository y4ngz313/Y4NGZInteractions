# Handoff: live-body camera rotation residue and TPRig authored baseline

Date: 2026-07-24

## State

- **DEPLOYED / STATIC VERIFIED:** Release build completed with `0` warnings and `0` errors and
  deployed to Gale profile `terrible`.
- Build and deployed `Y4NGZInteractions.dll` SHA-256 are both
  `9BB8D790C52933E1191F0CC638C271D172277380B241663805A08B39204302D3`.
- The profile contains exactly one file named `Y4NGZInteractions.dll`, under
  `BepInEx/plugins/y4ngz-Y4NGZInteractions`. Renamed quarantine files were not touched.
- `test-interaction-animation-api-v2-static-regressions.ps1`,
  `test-interaction-animation-api-v2-viewmodel-presenter-static-regressions.ps1`, and
  `test-interaction-animation-consumer-root-resolution.ps1` pass.
- The root `scripts/test-player-animation-api-static-regressions.ps1` named by `AGENTS.md` is not
  present; the only copy is the retired V1 script under `_archive/player-animation-api-v1/`.
- **LIVE UNVERIFIED:** no post-build playtest has run against this DLL.
- Camera rotation now has two default-on gates: session-time Y/Z anchoring to the immutable entry
  baseline with live pitch passthrough, and Stop sanitation that preserves pitch, snaps gameplay
  camera Y/Z to zero, and restores `CameraContainer` to `(90, 359.8182, 0)`.
- Stop capture occurs before the session stabilizer is released. The Info
  `[RestoreSeam.camerarotation] stop_restore_gate` retains raw discarded-residue measurements.
- The displacement guard's single session-start line now includes gameplay-camera Y/Z residue,
  container deviation from authored rest, and `pre_existing_rotation_residue`.
- Each player's third-person pristine control pose is now captured from serialized local TRS in a
  highest-priority `PlayerControllerB.Awake` prefix. Vanilla-rest plausibility is retained as
  sanity telemetry and no longer rejects movement-mod-aware authored defaults.
- The `PrepareForLiveBodyStart` and remote session-begin initialization gates log once at Info.
  Presenter TPRig logs fall back to the RestoreDiagnostics static logger when a consumer context
  has no logger.

## Open questions

- Does the next shotgun playtest keep rendered gameplay-camera local Y/Z fixed at the session
  entry values through repeated recoil while `stop_restore_gate` reports the raw same-frame
  residue attempt?
- Does every spawned local and remote player emit one `pristine_capture_sanity` followed by
  `pristine_captured` with source `player_awake_prefix_authored_default`?
- Is the root-level Player Animation API regression-script reference in `AGENTS.md` intentionally
  stale now that V1 is archived?
