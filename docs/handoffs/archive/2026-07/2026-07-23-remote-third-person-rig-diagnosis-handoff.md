# Handoff: remote/third-person body was never in the hardened restore path â€” first multiplayer evidence

Date: 2026-07-23. Roles: Fable orchestrates/researches, Codex implements (rescue tasks, xhigh,
model gpt-5.6-sol, launched from the target repo dir â€” sandbox/job registry are cwd-keyed).
Continues from `2026-07-18-slide-animated-camera-pose-residue-handoff.md` (Fix G/H implemented in
`SlideAnimationBridge.cs`; letters continue from there).
Repos: Interactions `C:\Lethal Company Modding\Interactions\Y4NGZInteractions` and Upgrades
`C:\Lethal Company Modding\Upgrades\Y4NGZUpgrades`. The maintainer must approve any commit.

## 1. Context: what produced the new evidence

First-ever multiplayer test of the mod (2026-07-23, desktop `#1 'OFF THE MARKET'` observing
`#0 'rickdesantis96'`). Videos: `Mantle Only.mp4` (remote mantle,
required viewing) and `Full multiplayer.mp4` (contains remote slide). Log:
Gale profile `terrible` `BepInEx\LogOutput.log` (desktop, 21:59â€“22:07 UTC window).

User-reported symptoms, now seen from another player's perspective for the first time:

- Remote mantle: player floats above the ledge in a glitchy pose instead of grabbing it; mesh
  warps during the clip.
- Remote slide: player appears flying in the air instead of sliding on the ground.
- PERMANENT post-session corruption: one leg bent backwards persisting into vanilla animations;
  hands/arms bent out of shape making certain vanilla hand/arm animations janky. Long-running
  local symptom (walk drift) is the already-root-caused camera-yaw residue (Fix G family).

## 2. Root diagnosis (STATE â€” verified against code, clips, and the playtest log)

**Every restore-seam hardening from rounds 1â€“9 is local-player-scoped.** Pristine rig restore,
camera rotation/pin, visor pose, arms glue â€” all skip remote sessions. Confirmed in the log:
every remote session emits `capture_skipped â€¦ reason='not_local_player'` for every guard
(`LogOutput.log:3412-3557`). Remote sessions DID start and restore (`auto-finished` /
`exit-finished`, fresh mode, no warnings) â€” the restores ran, but at unhardened fidelity.
The remote third-person body has been running the exact seams the local campaign fixed, unfixed.

### During-animation mechanisms (mantle video, all confirmed in code)

1. **Ledge-grab IK is local-only by construction.** `ApplyMantleBodyGrabIK` runs inside
   `LedgeMantlePatch.PostPlayerLateUpdate`, gated `IsLocalPlayer` (`LedgeMantlePatch.cs:143-146,191`).
   Its own comment: the raw clip's hands "always float above the surface". Observers see the raw clip.
2. **Duration desync â†’ airborne vanilla pose above the ledge.** Remote auto-end is fixed
   0.72 s / 1.36 s (`SlideAnimationBridge.cs:19-20`); local tall mantle runs
   `1.3 Ã— heightScale(0.9â€“1.4) Ã— MANTLE_FINISH_T(0.9)` â‰ˆ up to 1.64 s. Remote animation ends
   early â†’ restored vanilla animator sees `isGrounded == false` â†’ jump/fall pose while the
   capsule is still lerping up the lift path (+0.28 m overshoot). Matches video frames.
   **Mantle has a start message only** (`MSG_MANTLE_START`) â€” no exit/abort/duration sync.
   Slide has start+stop.
3. **Look constraints stay weighted during sessions.** The bridge sets nothing equivalent to
   `inSpecialInteractAnimation`, so `LookHead`/`LookHead2` (spine.003/004, weights 0.45/1) keep
   compositing the remote player's synced look direction on top of the clip â†’ upper-body warp.
   Vanilla's own climbs zero these during special animations (`PlayerControllerB.cs:5197-5205`).
4. The movement body clips themselves are correctly vanilla-shaped (verified curve paths in
   `LethalModAssets_v2/Assets/Y4NGZ/MovementPlayerAnimations/Y4NGZ_Body_*.anim`: FK chains +
   `Rig 1/*Leg_target` + `spine.003/*Arm_target`). Clip authoring is NOT the root cause.

### Permanent-corruption mechanisms

- **A. Third-person IK-target residue has no restore scope anywhere.** Established fact
  (2026-07-16 decision): vanilla empty-hand states never write the arm ChainIK targets and the
  constraints stay at weight 1 â†’ residue bends arms indefinitely. The pristine fix covers only
  the first-person `RigArms` subtree and is deliberately local-only
  (`LiveBodyAnimatorPresenter.cs:1334-1403`). But `WeaponThirdPersonAnimation.cs` starts full
  `BodyWorld` API sessions on REMOTE bodies posing the third-person `spine.003/*Arm_target` â€”
  outside every existing restore scope. Locally invisible (own body is shadows-only); multiplayer
  exposed it.
- **B. Knee pole flip is self-perpetuating.** Leg TwoBoneIK hint weights are 0; vanilla clips
  animate thigh FK + leg targets but not shin FK (audit Â§2), so the bend plane is seeded from the
  previous frame's solved shin. One hyperextension event during a deep-fold clip persists through
  vanilla locomotion indefinitely â€” "leg bent backwards continuing into vanilla animations".
- **C. Begin-time snapshots are sticky-contaminating.** The bridge's `TransformPoseSnapshot`
  restores begin-time values with no pristine baseline â€” a session started on contaminated state
  faithfully re-applies it (same garbage-in/garbage-out pattern as the slide camera bug).

### Secondary findings

- Restore-critical bridge evidence lines are `LogDebug` (scoped-restore count, RigBuilder rebuild
  count, `EnsureControllerStillApplied` re-fight) â€” dropped by the disk logger. Violates the
  "every gate logs" lesson; we are blind exactly where it matters remotely.
- Bridge restore never calls `RigBuilder.Evaluate(0f)` after `Build()` (the API does, on purpose)
  â†’ one FK-only frame per remote restore.
- The bridge is a direct controller owner duplicating the API restore stack at lower fidelity;
  two systems (bridge = movement, API = weapons) share remote animators with different restore
  semantics.

## 3. Fix plan (dispatch to Codex, xhigh, gpt-5.6-sol; one rescue task per repo)

**Fix J (Interactions) â€” third-person + remote pristine restore scope.**
Extend the live-body restore so the third-person rig controls are captured/restored around every
`BodyWorld` session, for BOTH local and remote players: `spine.003/LeftArm_target`,
`spine.003/RightArm_target`, `Rig 1/LeftLeg/*`, `Rig 1/RightLeg/*` (targets AND their group
parents), plus shin.L/shin.R local rotations as the pole re-seed. Follow the existing
pristine-primary/equip-fallback pattern; pristine capture must extend to remote players (capture
at first sight of each player's clean state, e.g. spawn/first-session-begin with a
vanilla-rest plausibility check, mirroring the displacement guard's contamination flagging).
New default-on kill-switch keys (BepInEx persists saved values â€” new keys, not default flips).
Every gate logs at Info under `[RestoreSeam.rigpose]`/a new `[RestoreSeam.tprig]` marker.

**Fix K (Upgrades) â€” mantle/slide network sync.**
Extend `MSG_MANTLE_START` payload with duration (float seconds, computed by the initiator via
`ResolveMantleDuration Ã— MANTLE_FINISH_T`) and add `MSG_MANTLE_STOP` (mirroring the slide's
start/stop pair) sent from `FinishMantle`/`AbortMantle`. Remote sessions use the received
duration instead of the fixed 0.72/1.36 constants (keep constants as fallback for old peers).
Handle host relay like the existing messages.

**Fix L (Upgrades) â€” replicate the ledge grab.**
Add `LedgeGripCenter` and `WallNormal` to the mantle start payload; on observer clients run the
existing `ApplyMantleBodyGrabIK` (body FABRIK blend, camera-envelope weight replaced by a
duration-based envelope) for the remote player's session each LateUpdate while the session is
active. FP-arm and occluder/camera work stays local-only.

**Fix M (Upgrades) â€” suppress look constraints during movement sessions.**
While a bridge session is active on any player, drive `cameraLookRig1/2` weights to 0 (the
vanilla special-animation behavior) and restore prior weights at end. Prefer setting the weights
directly each Tick (vanilla re-asserts them, so a one-shot write is insufficient); log the gate
once per session.

**Fix N (both repos) â€” evidence + parity.**
(a) Promote the Debug-level seam logs above to Info. (b) Add `RigBuilder.Evaluate(0f)` after the
bridge's restore `Build()` loop, mirroring the API. (c) Add a remote rig probe: on every remote
session begin/restore(+2 LateUpdates)/+5 s, log local TRS of `spine.003/*Arm_target`,
`Rig 1/*Leg_target`, shin.L/shin.R â€” marker `[RemoteRigDiff]`, gated by a default-on
diagnostics key. This is the acceptance lens for J/K/L/M.

Constraints for implementers: public API signatures frozen; no consumer content into
Interactions; Release builds only (`-c Release`, Debug silently skips deploy); run the
Interactions static regression scripts after Interactions changes; NEVER `git commit`
(the maintainer approves commits); new BepInEx keys for any default change; every gate logs.

## 4. Verification protocol (next multiplayer playtest)

1. Two machines per `MULTIPLAYER-SYNC-DIAGNOSIS-2026-07-23.md` Â§8 (profile alignment first).
2. Remote mantle on a tall ledge: body reaches the top before the animation ends (Fix K), hands
   plant on the lip (Fix L), no head/spine twist toward the observer (Fix M).
3. After remote mantle/slide/weapon equip-unequip Ã—3 each: `[RemoteRigDiff]` deltas vs pristine
   within noise; visually, vanilla walk/idle on the remote body shows straight knees and clean
   hands (Fix J).
4. Local regressions: weapons/CCTV/tablet unchanged; slide camera baseline (Fix G) still clean.

## 5. Open questions

- Which entry path plants the FIRST contamination in a fresh lobby â€” weapon TP session residue
  (mechanism A) or a mid-clip movement restore (B)? `[RemoteRigDiff]` decides.
- Should the bridge become an API consumer outright (one restore stack) instead of carrying its
  own snapshot? Architecturally yes, but it is a larger refactor than Jâ€“N and touches frozen
  surfaces â€” dedicated session decision, not part of this round.
- Mantle body-lowering (`MANTLE_BODY_LOWER_METERS`) is applied to the local capsule only; after
  Fix K the remote pose may sit slightly high during the lift â€” acceptable or needs the same
  offset replicated?
