# Handoff: third-person BodyWorld rig restore and remote rig probe

Date: 2026-07-23

## State

Deployed:

- Release `Y4NGZInteractions.dll` built and deployed to the configured Gale test profile.
- Every local and remote `BodyWorld` session now captures/restores third-person arm-target TRS,
  both leg-control group subtrees, and `shin.L`/`shin.R` local rotations.
- Each player can acquire a first-clean-sight third-person pristine baseline after the v81
  vanilla-rest plausibility gate accepts it; session-entry poses remain the per-transform
  fallback.
- Default-on, saved-config-safe keys independently gate third-person restore, pristine-primary
  restore, and the remote rig probe.
- `[RemoteRigDiff]` samples remote arm targets, leg targets, and shins at pre-controller session
  begin, two `LateUpdate`s after restore, and five seconds after restore.

Verified:

- Release build succeeds with zero warnings and zero errors.
- All `scripts/test-*-static-regressions.ps1` scripts pass.
- No public API signatures changed.

Unverified:

- Multiplayer runtime behavior and `[RemoteRigDiff]` deltas have not yet been playtested.

## Open questions

- Do first remote session entries occur close enough to the v81 prefab-rest tolerances to acquire
  a pristine baseline promptly, or do ordinary vanilla locomotion/equip states cause repeated
  clean-sight rejection?
- After remote mantle, slide, and weapon sessions, do the two-`LateUpdate` and five-second samples
  remain stable while vanilla locomotion resumes?
