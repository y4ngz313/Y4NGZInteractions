---
status: resolved
trigger: "Eliminate remaining in-session gameplay-camera yaw/roll accumulation; snap contaminated stop-seam rotation to rest; add rotation residue telemetry; make TPRig pristine capture movement-mod-aware; close initialization and logger gaps."
created: 2026-07-24
updated: 2026-07-24
---

# Camera Yaw/Roll Accumulation

## Symptoms

- expected: Live-body sessions preserve vanilla-owned gameplay-camera pitch while gameplay-camera local yaw/roll remain at absolute rest; stop restores camera/container Y/Z rest and retains pitch continuity.
- actual: Local live-body session `e67ed66e` entered at gameplay-camera local Euler `(54.05132,0,0.025076)` and stopped ten seconds later at `(1.834537,359.3166,1.064444)`; the stop seam then reapplied the contaminated rotation.
- errors: No exception; gate telemetry proves persistent in-session yaw/roll growth and TPRig pristine-capture rejection.
- timeline: Reproduced in two 2026-07-24 playtests after the sibling recoil baseline/residue fix made session-entry rotation nearly clean.
- reproduction: Start a local heavy-shotgun live-body session, fire during the session, then unequip/stop and inspect `[RestoreSeam.camerarotation]` capture/reapply lines.

## Current Focus

- hypothesis: Confirmed: live-body sessions had no Y/Z ownership invariant, so a consumer full-Euler camera writer could retain a tiny entry residue, recapture it as the next kick baseline, and grow it while vanilla preserved Y/Z; exact Stop replay then persisted the contaminated quaternion.
- test: Static writer trace, log chronology, direct sibling read-only driver inspection, build, and API regression checks.
- expecting: Session anchor uses only current X with immutable entry Y/Z; Stop snap uses captured X with rest Y/Z/container.
- next_action: Live playtest remains an acceptance question, not an implementation blocker.
- reasoning_checkpoint: Position stabilizer is position-only; hard visor glue writes visor transforms only; seam replay was the persistence defect but not the repeating session writer.
- tdd_checkpoint: API V2 static regression coverage asserts absolute Y/Z anchoring, Stop sanitation, spawn-time TPRig capture, initialized gates, and logger fallback.

## Evidence

- timestamp: 2026-07-24
  observation: `LogOutput-tilt-fix-verify-2026-07-24.log` shows start `(54.05132,0,0.025076)` and stop `(1.834537,359.3166,1.064444)` for handle `e67ed66e` over ten seconds.
  implication: Rotation contamination is introduced during an Interactions live-body session, not inherited from pre-session recoil residue.
- timestamp: 2026-07-24
  observation: The same stop seam immediately logs reapplication of the contaminated stop-entry camera quaternion.
  implication: Restore behavior preserves the newly introduced residue after controller teardown.
- timestamp: 2026-07-24
  observation: TPRig pristine capture repeatedly rejects with about `0.83` position and `93.86` degree rotation deltas.
  implication: Vanilla-rest comparison is invalid in the movement-mod-active live hierarchy and cannot establish a pristine baseline.
- timestamp: 2026-07-24
  observation: Sibling recoil logs during handle `e67ed66e` recapture baselines at `Z=0.1552`, then `Y=-0.6264/Z=1.0477`, while the residue janitor logs `skip=session_active`.
  implication: The active full-Euler writer is external to the presenter, but Interactions must enforce the generic local live-body Y/Z invariant while it owns the session.
- timestamp: 2026-07-24
  observation: Current presenter source contains no session camera rotation assignment outside the new absolute rotation stabilizer; existing seam writes are bounded to Start/Stop.
  implication: The position stabilizer and visor glue are eliminated as rotation sources.

## Eliminated

- hypothesis: Local camera position stabilization rotates the camera.
  reason: Its only transform assignment is world `position`; it never assigns local/world rotation.
- hypothesis: Hard visor glue writes gameplay-camera or camera-container rotation.
  reason: It writes the visor transform and visor target only.
- hypothesis: TPRig restore causes in-session camera accumulation.
  reason: TPRig assignments occur in the Stop restore path and target arm/leg controls, not camera transforms.

## Resolution

- root_cause: No local live-body session component owned gameplay-camera Y/Z. Vanilla repeatedly preserved whatever Y/Z a consumer writer left; consumer kick baselines were recaptured from that residue, and Interactions' exact Stop seam replay restored the contaminated values across teardown. Separately, TPRig pristine capture sampled already animated live transforms and used vanilla-rest plausibility as a hard rejection gate.
- fix: Added immutable session-entry Y/Z anchoring with current-pitch passthrough, default-on Stop sanitation to gameplay Y/Z zero and exact container rest, rotation residue telemetry, spawn-time authored-default TPRig capture with sanity-only plausibility, initialized gate logs, and static logger fallback.
- verification: Release build 0 warnings/0 errors; three current static regression scripts pass; build/deploy SHA-256 `9BB8D790C52933E1191F0CC638C271D172277380B241663805A08B39204302D3`; exactly one profile DLL. Live playtest pending.
- files_changed: `Plugin.cs`, `RestoreDiagnostics.cs`, `LiveBodyAnimatorPresenter.cs`, API V2 regression script, living API/decision/TPRig docs, and dated handoff.
