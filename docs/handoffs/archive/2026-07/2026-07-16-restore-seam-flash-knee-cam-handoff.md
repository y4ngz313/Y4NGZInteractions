# Handoff: restore-seam flash + knee-cam â€” playtest-3 analysis and next fixes

Date: 2026-07-16 (late session wrap-up). Roles: Fable orchestrates/researches, Codex 5.6 Sol
xhigh implements. Read `docs/Handoffs/2026-07-16-api-solidification-research-plan.md` first for
the pass context (WP0â€“WP6, decisions in Â§7), then this doc for the live bug state.

## TL;DR for the next session

- **Bug 2 (bent vanilla arms after custom anims): FIXED** (round-1 RigArms pose restore),
  user-confirmed, keep the kill-switch on.
- **Bug 1 (end-of-animation flash): NOT fixed after 3 rounds â€” but now precisely measured.**
  Camera, camera container, arms metarig, and animator states are all continuous through the
  stop frame; the **first-person hands teleport ~0.34 m between the stop frame and the next
  frame** (render-time sampler, both hands, rigid offset). The flash is one rendered frame of
  stale arm pose. Fix direction A below is the next move.
- **Knee-cam: exculpates the API this run.** The corruption existed under the *vanilla*
  controller minutes before any session; the crossbow pickup's camera-displacement guard +
  restore is what *repaired* it. Genesis suspect: Y4NGZUpgrades "Panic Slide" first-person
  movement presentation (slides at 03:15:08/03:15:11, corrupted pickup 03:15:24). Needs its own
  track in the Upgrades repo.
- Everything (WP0 research, WP1 probes, round 1â€“3 fixes, v1 archival, this handoff) is
  committed as the 2026-07-16 session-end commit ("Solidification pass: v1 removal, restore-seam diagnostics, and fix rounds 1-3") on `codex/real-first-person-presenter` (the maintainer approved a single
  commit at session end). Working tree clean.

## Current shipped state (deployed to the Gale test profile)

Round 1 (WP2+WP3): always-on `RigArms` local-TRS subtree capture at TryStart (before controller
swap) â†’ reapplied after animator snapshot restore, before rig rebuild. Kill-switch `Restore Rig
Control Pose`. Camera pin default ON.

Round 2: same-frame `RigBuilder.Evaluate(0f)` (reflection) after `Animator.Update(0f)` at start
and restore; camera pin re-keyed to `Restore Camera Pin` (BepInEx persists saved values â€” default
flips require a key rename); `[RestoreSeam.render]` onBeforeRender sampler.

Round 3: `Restore State Mode` config = `fresh` (default; controller/params/weights restored, no
`Play` â€” layers restart at their default states) | `crossfade` (`CrossFadeInFixedTime` 0.12 s,
normalizedTime wrapped) | `replay` (original behavior). Motivation: playtest-2 data showed
`Play(hash, normalizedTime)` snapping the camera container and stranding non-looping states past
end (that run's knee-cam).

Build: `dotnet build ./src/Y4NGZInteractions/Y4NGZInteractions.csproj -c Release` (auto-deploys).
Both regression scripts pass. Config: `BepInEx/config/com.y4ngz.interactions.cfg`, section
"Interaction Animation API V2 Restore Diagnostics".

## Round 4 implementation (2026-07-16; build verified, live playtest pending)

Fix A and Fix B are implemented in the shipped live-body presenter path. Release build succeeds
with zero warnings and auto-deployed to the Gale test profile; both V2 static regression scripts
pass. The `< 2 cm` hand-seam acceptance and post-stop `[RigDiff]` target check still require a
live playtest.

- Fix A: local-player `Stop()` now reproduces vanilla's head-bob-off camera-container Y pin and
  `localArmsMatchCamera` arm glue after controller/snapshot/pose restoration and rig rebuild, but
  before the existing `Animator.Update(0f)` and `RigBuilder.Evaluate(0f)`. It preserves vanilla's
  `!inSpecialInteractAnimation` and `localArmsMatchCamera` gates. New default-on key: `Restore
  Vanilla Arms Glue`.
- Fix B: the startup-pristine `[RigDiff]` transform baselines are now the primary local-player
  source for the `RigArms` subtree. Equip-time locals restore only transforms absent from the
  pristine set; remote sessions and unavailable-pristine cases retain the equip-time fallback.
  Scoped first-person restoration runs before this rig-control restore so it cannot overwrite the
  pristine targets. The pristine capture remains active when its restore is enabled even if the
  diff logger is disabled. New default-on key: `Restore Pristine Rig Control Pose`; the existing
  `Restore Rig Control Pose` remains the master switch.
- Both new operations are locally gated and failure-contained around vanilla/reflection access.
  The keys are new names so persisted BepInEx values cannot suppress the default-on fixes.

## Round 5 correction (2026-07-17; build verified, live playtest pending)

The round-4 live playtest **failed Fix A**. Diagnostics proved `restoreVanillaArmsGlue=True`,
and Fix B succeeded on every stop (`[RestoreSeam.rigpose] pristineBaselineUsed=True`, all 10
transforms restored), but 15 crossbow/flamethrower/revolver sessions emitted zero
`[RestoreSeam.armsglue]` lines. `IsLocalPlayer` had already passed for the pristine restore in the
same `Stop()`, so the glue method was silently returning at either `inSpecialInteractAnimation`
or `!localArmsMatchCamera`. The remaining `[RestoreSeam.render]` samples still showed 10-25 cm
hand jumps across the seam.

Decompiled `PlayerControllerB` establishes that `localArmsMatchCamera` is a serialized prefab
field and is never assigned by vanilla. Its only uses are the mutually exclusive arm-glue paths:
when false, `Update` positions local arms from the arms metarig's current position/forward and
then lerps metarig rotation; when true and not in a special interaction, `LateUpdate` uses the
camera-relative position and snaps rotation. The evidence therefore identifies the round-4
rejecting gate as `!localArmsMatchCamera`: normal play uses the false-field `Update` path, while
round 4 reproduced only the true-field `LateUpdate` path.

Round 5 replaces the silent gates with an info line on every enabled local invocation:
`[RestoreSeam.armsglue] gates` includes frame, handle, both gate values, and
`branch=specialanim|update|lateupdate`. It mirrors all three vanilla branches. `specialanim` sets
arms-metarig local Euler to `(-90, 0, 0)` and skips the camera-Y pin; `lateupdate` retains the
round-4 camera-relative glue; `update` computes position first from the metarig's pre-assignment
orientation, then rotates it. The normal branches retain the head-bob-off camera-Y pin. The
`update` rotation intentionally snaps directly to `localArmsRotationTarget` instead of taking
vanilla's `15f * Time.deltaTime` lerp step, because restore needs the converged steady-state pose
in this one seam frame and a partial step from stale state would preserve the discontinuity.

Release build succeeds with zero warnings and auto-deployed to the Gale test profile. Both V2
static regression scripts pass, including new checks for the diagnostic line, all three branch
shapes, camera-pin ordering, Update position-before-rotation ordering, and removal of the two old
silent gate forms. The `< 2 cm` rendered-hand seam acceptance still requires a round-5 playtest.

## Round 6 instrumentation (2026-07-17; build verified, live playtest pending)

Round 5 confirmed `branch=update`, `localArmsMatchCamera=False`, and rendered stop-seam hand
positions continuous to under 2 cm on four of five stops, but the perceived weapon flash was
unchanged; some animations also showed one black frame at start. Round 6 is instrumentation only:
no restore, glue, prop, or item behavior/order changed. `[RestoreSeam.render]` now records a
buffered pre-start frame plus `startFrame..startFrame+3`, and `stopFrame..stopFrame+5`, with world
position/Euler rotation for camera, both hands, arms metarig, and local arms; prop/held-item
visibility; frame timing; FOV; and near clip.

For the next playtest, grep `\[RestoreSeam\.render\]` and compare consecutive `phase=start` and
`phase=stop` lines, especially `animatedProp` versus `heldItem`, all `WorldEuler` fields,
`deltaTime`/`unscaledDeltaTime`, and `cameraFov`/`cameraNearClip` on the visible bad frame.

## Playtest-3 evidence (log preserved at `research/logs/2026-07-16-playtest3-LogOutput.log`)

Two live-body sessions total, both `y4ngz.crossbow.livebody`; `restoreStateMode='fresh'`
confirmed active on both restores.

### Stop 1 â€” the flash, isolated (frame 12946, `NaturalEnd`, player walking)

`[RestoreSeam]` frames before-3 â†’ after+6: gameplay camera world, container local (constant
`(0,-0.012,2.096)`), and arms-metarig world are all smooth to sub-millimeter through the stop.
Animator states restart at `@0` (fresh mode working as designed). **The camera/container/state
layers of the seam are all clean.**

`[RestoreSeam.render]` (log lines 2397/2402), the smoking gun:

| frame | hand.R world | hand.L world | camera world |
|---|---|---|---|
| 12946 (stop) | (5.195, 1.931, -13.551) | (4.896, 1.964, -14.320) | (4.2418, 2.6449, -13.6497) |
| 12947 | (4.957, 1.704, -13.482) | (4.642, 1.737, -14.245) | (4.2420, 2.6449, -13.6492) |

Both hands jump **~0.34 m in one frame** (â‰ˆ rigid parent offset, not per-target scatter) while
the camera moves 0.4 mm. The stop frame renders the arms at the pose our restore produced
(round-1 equip-time `RigArms` capture + fresh states at t=0 + same-frame rig eval); vanilla
re-glues the arms only on the *next* `PlayerControllerB.LateUpdate`
(`playerModelArmsMetarig.rotation = localArmsRotationTarget.rotation`,
`localArmsTransform.position = cameraContainer.position + camera.up * -0.5` â€” decompiled
~line 7704). FP arms fill much of the screen, so a 34 cm one-frame arm snap reads as a
flash; when a sleeve sweeps the near plane it reads as the "fully black frame" variant.

Conclusion: rounds 1â€“3 fixed camera continuity, state continuity, and rig-graph timing, but
**arm-pose continuity at the seam was never enforced**. Our Stop() runs from the coordinator
LateUpdate with undefined order vs `PlayerControllerB.LateUpdate`, so nothing corrects the
restored (stale) arm pose before that frame renders.

### Stop 2 â€” knee-cam autopsy (frame 43376, `Interrupted`)

Timeline (all same log):

1. 03:10:08 â€” stop 1 completes clean (container local back to rest `(0,-0.012,2.096)`).
2. 03:11â€“03:15 â€” normal play: CCTV monitor exits, **Panic Slide mantles + slides**
   (`Y4NGZ_Movement_PlayerMetarig`, 9 layers incl. `Y4NGZMovementLocalBody`), an emote.
   Last slides at **03:15:08 and 03:15:11**, emote 03:15:17.
3. Frames 43373â€“43375 (pre-pickup, **vanilla `metarig` controller, no session active**):
   gameplay camera world **Y = 0.156 (floor level)** while container world Y = 1.48;
   container local = **`(0,-0.358,1.009)`** vs rest `(0,-0.012,2.096)` â€” a low-and-forward
   pose consistent with a slide posture. This is the knee-cam, already present under vanilla.
4. 03:15:25.000 â€” crossbow pickup starts a live-body session. **7 ms later** the
   `camera_displacement_guard` fires (`displacement=2.485`, baseline `(0,-0.19,0.05)` vs
   current `(0,2.29,0.01)`) and auto-stops the session (`Interrupted`).
5. The restore repairs the hierarchy: after+1 shows container local back at `(0,-0.012,2.096)`,
   camera world Y = 2.64 (normal head height). **"Picking up the crossbow reset the view" was
   our own guard + restore path, not a vanilla behavior.**

Implications:

- The guard's log message ("The authored controller moved the local camera/body hierarchy") is
  **misattributed** in this case â€” the displacement pre-existed session start. The guard should
  distinguish "displaced at session entry" (external corruption; restore should target vanilla
  rest, and the event should not be blamed on the authored controller) from "displaced during
  session".
- Knee-cam genesis this run is **outside Y4NGZInteractions** â€” prime suspect is the Upgrades
  movement presentation (Panic Slide local-body layer animating the container, exit residue).
  Playtest-2's knee-cam had a *different* cause (replay-mode stranding a non-looping state past
  end) which fresh mode genuinely eliminated. Two causes, same symptom.
- `[RigDiff]` after stop 2: `ArmsRightArm_target` local pos **0.308 m off pristine** two
  LateUpdates post-restore. The round-1 capture for this session was taken 7 ms into the
  *corrupted* state, so the restore reinstated a contaminated pose. Equip-time capture is a
  fragile baseline: it also inherits whatever weapon FP systems have written to the targets.
  (The metarig-root 20.5Â° rotation delta in the same dump is likely look-direction noise â€”
  vanilla re-aims the metarig root every frame â€” but the target delta is real residue.)

## Fix plan status after round 4

**Fix A â€” IMPLEMENTED; LIVE ACCEPTANCE PENDING.** Enforce arm-pose continuity at the seam (the
flash). At the end of Stop(), after
snapshot restore + rig-control pose restore, synchronously apply vanilla's own glue for the
local player *before* `Animator.Update(0f)` + `RigBuilder.Evaluate(0f)`:
`playerModelArmsMetarig.rotation = localArmsRotationTarget.rotation`;
`localArmsTransform.position = cameraContainerTransform.position + gameplayCamera.transform.up * -0.5f`;
plus the camera-Y pin equation when head-bob is off (decompiled `PlayerControllerB.LateUpdate`
~7580/~7704 â€” copies in `research/decompiled/`). Then the same-frame rig eval computes IK
against the *live* glue pose and the rendered stop frame matches vanilla steady-state.
Acceptance: `[RestoreSeam.render]` hand delta across the seam < ~2 cm while walking.

**Fix B â€” IMPLEMENTED; LIVE ACCEPTANCE PENDING.** Restore the IK-target locals from a
scene-pristine snapshot (the `[RigDiff]` pristine capture already exists at startup) instead of
the equip-time capture; keep equip-time capture only as fallback for transforms without a
pristine baseline. Kills both the contaminated-capture failure (stop 2) and weapon-pose
inheritance. Vanilla never writes the targets in empty-hand states, so pristine locals are the
correct rest values by construction.

**Fix C â€” guard/pin entry-displacement handling.** When Stop-entry pose is already displaced
beyond threshold (guard fired at start, or pin capture far from baseline), the pin must NOT
re-assert the stop-entry pose on release and the restore should target vanilla rest. Reword the
guard log to say displacement was detected, without asserting the authored controller caused it,
when it fires on the first session tick.

**Fix D â€” separate track, Upgrades repo.** Panic Slide / movement-presentation exit residue on
`cameraContainerTransform` (and possibly `localArmsTransform`). Suggested: a cheap watchdog â€”
when no session is active and `!inSpecialInteractAnimation`, log (rate-limited) if container
local deviates > 5 cm from `(0,-0.012,2.096)` â€” to catch the genesis moment with a timestamped
culprit, then fix the movement presentation's exit path. Do not bake an auto-corrector into
Interactions; the corruption is not ours.

Fresh mode stays the default â€” playtest 3 surfaced no regression attributable to it, and it
provably removed the replay-teleport and state-stranding failure modes.

## Verification protocol

1. Build Release (auto-deploys), start a run, use crossbow/shotgun/tablet repeatedly while
   standing, walking, and turning; do slides/mantles between uses.
2. Flash check: `[RestoreSeam.render]` hand.R/hand.L deltas between `frame == stopFrame` and
   the next line â€” target < 2 cm (was 34 cm).
3. Residue check: `[RigDiff]` auto dump after each stop â€” `transformChanges` should list no
   `ArmsRightArm_target`/`ArmsLeftArm_target` entries (metarig-root rotation noise is expected).
4. Knee-cam: watch for `camera_displacement_guard` with large baseline deltas at session start â€”
   that indicates external corruption arrived before the pickup (Fix D territory).
5. Numpad* dumps a manual RigDiff any time something looks wrong in-game.

## Commit state

All session work was committed 2026-07-16 as that single commit on
`codex/real-first-person-presenter` (the maintainer's choice): WP0 `research/decompiled/`, WP1
diagnostics, WP5 v1 archival (git detected the moves as 100% renames), fix rounds 1â€“3, docs,
and the playtest-3 log. Remember the AGENTS.md rule for future work: never commit without
the maintainer's explicit yes.

## Open questions for the maintainer

- Whether knee-cam ever occurs in a run with *no* slides/mantles/emotes (would weaken Fix D's
  suspect list).
- crossfade mode is untested in-game (fresh was default); fine to leave untested unless fresh
  shows end-pose artifacts after Fix A.
