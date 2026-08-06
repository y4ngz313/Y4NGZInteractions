# Handoff: slide-exit residue ROOT-CAUSED â€” restore preserves the slide clip's ANIMATED camera pose

Date: 2026-07-18. Roles: Fable orchestrates/researches, Codex implements (rescue tasks, xhigh, launched
from the target repo dir â€” sandbox/job registry are cwd-keyed; no `--wait`, poll the job JSON under
`~/.claude/plugins/data/codex-openai-codex/state/<Repo>-<hash>/jobs/`).
Supersedes the open items of `2026-07-17-restore-seam-root-cause-camera-pitch-and-start-hitch-handoff.md`
(that doc's Â§Â§1â€“3 remain the campaign history) and continues Upgrades issue #63.
Repos: Interactions `C:\Lethal Company Modding\Interactions\Y4NGZInteractions` (branch
`codex/real-first-person-presenter`, rounds 4â€“9 UNCOMMITTED atop `27e7aea`) and Upgrades
`C:\Lethal Company Modding\Upgrades\Y4NGZUpgrades` (seam work uncommitted atop `117333e`, tree also
carries unrelated weapon/Lucky-8/progression work â€” do not touch it). The maintainer must approve any commit.

## 1. What is CONFIRMED FIXED (do not re-litigate)

| Fix | Where | Verdict |
|---|---|---|
| Full-screen end-of-animation flash (camera pitch zeroed by Rebind) | Interactions presenter round 7 ("Restore Camera Rotation") + Upgrades bridge port | user-confirmed dead, playtests 8â€“10 |
| 1â€“3-frame visor artifact (visor target under camera; rotation lerped 53Â·dt) | visor capture/reapply both repos round 8; `localVisor` itself is NOT under the animator (`Systems/Rendering/PlayerHUDHelmetModel`) â€” visor-side no-op is CORRECT | user-confirmed "mostly solved", playtest 10 |
| Bent vanilla arms after custom anims | round 1 + round 4 pristine rig restore (`pristineBaselineUsed=True` every stop) | holds |
| Guard killing CCTV operator sessions | Fix C round 9B: `Camera Displacement Guard Exempt Interactions` (default `y4ngz.cctv.operator`), TryStart pre-controller reference, `pre_existing_displacement`/`baseline_contaminated` flags | playtest 10: 7Ã— `guard_exempt`, sessions survive, baselines clean (2.348 vs rest expectation 2.35) |
| Slide restore state-machine stranding | round 9A fresh mode (`[Panic Slide] Slide Restore State Mode = fresh`) | playtest 10 sampler: all 6 layers re-enter at nt=0 and advance; container local at rest. Fresh mode WORKS â€” it just wasn't this bug |

## 2. THE REMAINING BUG (playtest-10 evidence, log `research/logs/2026-07-18-playtest10-LogOutput.log`)

Symptom (screenshot 2026-07-18 13:57 local): after a Panic Slide exit â€” camera at knee height,
walking direction skewed from view, "half glitched into the floor". Reproduced across playtests 9
and 10.

`[Panic Slide.postrestore]` sampler on the breaking slide (17:57:24 UTC, frames 44953â€“55):

- `cameraContainerLocalPosition=(0,-0.012,2.096)` = REST, all 3 frames. Container is innocent.
- Animator layers: fresh entries at normalizedTime 0, advancing. No stranding.
- `gameplayCameraLocalEuler=(5.38, 316.4693, 0.000002)` â€” **persistent local YAW âˆ’43.53Â° on the
  gameplay camera**, stable across frames. The IDENTICAL value 316.4693 appeared in playtest 9's
  slide capture (16:23:52). An exact repeating constant = an AUTHORED value, not drift.
- Playtest 9 also measured the height half of the residue: camera player-local position
  `(-0.01, 0.99, 0.01)` vs vanilla rest `(0, 2.35, 0.01)` â€” 1.36 m low, persisting â‰¥5 s.

**Mechanism (high confidence): the slide's authored animation ANIMATES THE CAMERA TRANSFORM**
(view sweep/yaw + drop toward the ground â€” `MainCamera` sits under `metarig/CameraContainer`, inside
the animated hierarchy). The bridge's exit-restore then *faithfully preserves that animated pose*:

1. Rotation: the round-9A camera-rotation port captures the LIVE camera localRotation at restore
   entry â€” which, at slide exit, is the slide clip's animated pose (deterministic capture time via
   `ExitHoldSeconds` â†’ identical 316.4693 every run) â€” and reapplies it after the restore.
   Garbage in, faithfully garbage out.
2. Position: `ParkourCameraPositionReleaseGuard` (`SlideAnimationBridge.cs` ~357â€“379) pins the
   camera at its restore-ENTRY player-local position (mid-slide, near ground) through 2 LateUpdates.
3. Nothing ever corrects either afterward: vanilla `PlayerLookInput` writes camera PITCH (and body
   yaw); it does not rewrite camera local yaw/roll/position. The vanilla controller does not animate
   the camera transform in normal states. So the animated yaw and height persist indefinitely.

Historical irony, understand it before "fixing the fix": pre-round-9A, the controller-swap Rebind
ZEROED the camera transform at exit â€” that zeroing WAS the one-frame flash we killed, but it was
also accidentally cleaning up the slide clip's camera pose. Weapons/CCTV differ from the slide:
their clips never animate the camera, so live-capture/reapply is correct there. The slide needs a
different restore TARGET, not a revert.

Why "walk at an angle": âˆ’43.5Â° stale camera local yaw offsets view from body heading. Why the guard
saw `current=(-0.01,0.99,0.01)` at the playtest-9 crossbow equip: that's the preserved slide-clip
camera height.

## 3. Fix plan (Upgrades `SlideAnimationBridge.cs` â€” dispatch to Codex, xhigh)

**Fix G â€” restore the camera to a VANILLA-CLEAN pose at slide/mantle exit, not the live animated one.**

- At `Begin()` (BEFORE `_animator.runtimeAnimatorController = controller` â€” the pre-swap state is
  guaranteed vanilla-clean because slides start from normal gameplay), capture gameplayCamera local
  TRS (+ cameraContainer local TRS for completeness) as the CLEAN baseline.
- At restore (in `RestoreVanillaAnimatorController`, same slot where the current live-capture
  reapply runs): restore camera local POSITION + local yaw/roll from the Begin-time clean baseline;
  take PITCH from the live `player.cameraUp` at exit (Begin-time pitch is stale if the player looked
  around mid-slide; vanilla rewrites pitch next frame anyway â€” write `localEulerAngles=(cameraUp,
  cleanYaw, cleanRoll)` so the exit frame is already correct). Keep the existing live-capture path
  for the container (container proved clean) or restore both from Begin â€” implementer's call, log
  which.
- Change the position guard's pin target from restore-entry player-local position to the Begin-time
  clean player-local position (same reasoning; pinning the mid-slide height is what holds the
  knee-cam through the hand-back).
- Config: NEW key (BepInEx persists saved values â€” key RENAME required for any default change),
  e.g. `[Panic Slide] Slide Restore Camera Baseline = begin` (begin|live kill-switch), default
  begin. Every gate logs. Per-target try/catch.
- Regression script: extend the escape-artist assertions (Begin-time capture before controller
  swap; baseline-mode branch; pitch-from-cameraUp).

**Fix H â€” extend the `[Panic Slide.postrestore]` sampler's coverage** (the knee-cam hid in an
unsampled transform): add gameplayCamera localPosition, player root world pos, metarig local TRS,
and `player.thisPlayerBody` yaw vs camera world yaw. Cheap, and it's the proof lens for Fix G's
acceptance.

**Fix I (investigate, likely same family) â€” "vanilla running arms too high" after MANTLE.** Mantle
runs through this same bridge. Suspect: the mantle clip animates arm/metarig transforms whose
exit pose is similarly preserved (scoped snapshot restores Begin-time values â€” check whether
`TransformPoseSnapshot.CapturePlayerHierarchy` includes the RigArms/IK-target subtree and whether
anything re-asserts an animated pose post-restore). The Fix H sampler extension plus one mantle
repro log should decide it. If it is the same mechanism, Fix G's begin-time-baseline pattern applies.

## 4. Open items carried (unchanged priority order after Fix G/H/I)

- **Fix F step 2**: start-seam hitch optimization â€” per-phase `[RestoreSeam.timing]` ms lines are
  already in every playtest log since round 7; analyze playtest-10's lines to pick the dominant cost
  (suspects: prop Instantiate first-shader-upload, double rig Build).
- **Fix D**: Upgrades movement-presentation exit-residue watchdog (rate-limited log when no session
  active and container deviates >5 cm from rest). Fix G may moot it; keep as diagnostic.
- **Commits**: Interactions rounds 4â€“9 (suggested split: fixes / instrumentation / docs+logs â€” logs
  in-repo vs gitignore still open) and the Upgrades seam work. maintainer approval required (AGENTS.md).
- **Issue #63**: open; progress comments could NOT be posted (GitHub unreachable from Codex sandbox)
  â€” post manually or at close-out.
- CCTV repo (`Y4NGZCompany`): deployed build is broken round 23 ("runaway arms"); interim revert =
  `Lever Phase Left Shoulder Offset = 0` in profile cfg. CCTV FP-arms port to Interactions
  LocalViewmodelPresenter is its own track (see CCTV handoff 2026-07-17). NOTE: with Fix C exempting
  `y4ngz.cctv.operator`, the operator live-body session now RUNS for the first time â€” CCTV behavior
  under a surviving session is lightly tested; watch it.

## 5. Hard-won gotchas (do not relearn)

- BepInEx persists saved config values: changing a code default does nothing for existing keys â€”
  RENAME the key.
- Silent early-return gates in seam-critical code hide no-ops for entire playtest rounds â€” every
  gate logs, keep it that way.
- `Stop()`/bridge-restore order vs `PlayerControllerB.LateUpdate` is undefined on the exit frame â€”
  re-apply vanilla per-frame writes synchronously if the rendered frame needs them.
- Vanilla FP arm glue in normal play is the UPDATE branch (`localArmsMatchCamera` is false); visor
  glue lerps rotation at 53Â·dt (artifacts decay over 2â€“3 frames, not 1).
- "Capture live & reapply" is only correct when the authored clip does NOT animate the transform;
  clips that animate the camera (slide) need a clean-baseline restore target instead (Â§2).
- BepInEx log timestamps are UTC. Build with `-c Release` (Debug silently skips deploy in this repo
  family). Playtest logs preserved under Interactions `research/logs/`.

## 6. Verification protocol for Fix G (next playtest)

1. Build Upgrades Release (auto-deploys, verify DLL timestamp/SHA). Slide repeatedly: mid-slide look
   sweeps, exit while turning, slideâ†’immediate second slide, slide right after CCTV use.
2. Acceptance in `[Panic Slide.postrestore]` (with Fix H fields): gameplayCamera localEuler y/z
   within Â±0.5Â° of the Begin-time clean values on every sampled frame; camera local position within
   1 cm of clean; player-local camera height back at ~(0, 2.35, 0.01) by sample+2.
3. User-visible: no knee-cam, no skewed walking, no visor/flash regressions on weapons+mantle.
4. Mantleâ†’vanilla-run arm check for Fix I; grab the log either way â€” sampler + `[RestoreSeam.timing]`
   lines feed Fix F/I next.
