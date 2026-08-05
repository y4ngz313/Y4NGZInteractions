# Restore seam internals

Last updated: 2026-08-05

> **Maintainer documentation.** This describes how the live-body presenter takes and gives back
> ownership of the player's animator, rig, camera, and visor, plus the kill-switches, log markers,
> and probes that exist to debug it. None of it is part of the public API contract — config keys
> and log markers change with the diagnostics. Consumers should read
> [../API_REFERENCE.md](../API_REFERENCE.md) instead.

Decision history for these behaviours lives in `../ANIMATION_API_DECISIONS.md`. Bugs and
follow-ups are tracked in the repository's
[GitHub Issues](https://github.com/y4ngz313/Y4NGZInteractions/issues).

## Presenters

### `LiveBodyAnimatorPresenter` (`PresentationKind.BodyWorld`)

The production path. Swaps `playerBodyAnimator.runtimeAnimatorController` to the authored shell
controller, wrapped in an `AnimatorOverrideController` whose slots are overridden by clip-pack
clips. Rebuilds all `RigBuilder`s after every controller change (mandatory when
`suppressRigBuilders` is false).

The controller swap resets every animator parameter to its default, and vanilla only rewrites
`Walking` / `crouching` / `Jumping` on state transitions, so the presenter reapplies the
equip-time parameter values from the snapshot immediately after the swap
(`[RestoreSeam.locomotion] parameters_reapplied`). Without this, equipping mid-walk kills
locomotion — sprint animation and camera bob — until the player happens to stop and start moving
again.

Restore goes through `AnimatorStateSnapshot`: switch back to the original controller, call
`Animator.Rebind()` to clear outgoing bone transforms, restore parameters, layer weights and
states, restore local `RigArms` controls from the startup-pristine snapshot with per-transform
equip-time fallback, restore the targeted third-person rig controls from the per-player pristine
snapshot with session-entry fallback, then rebuild the rig graph. For local sessions it then
reapplies the captured Start rotations or the sanitised Stop targets, applies vanilla's local
first-person arm glue, evaluates the animator at zero delta to write FK and IK targets, and
immediately calls each active `RigBuilder.Evaluate(0f)` so IK is applied before the same frame
renders. Rig-control and camera-rotation restore are skipped when controller ownership changed
externally.

Manifest-driven extras: `body.movementParameter` (animator Int written every Tick from the
session player's movement: 0 idle, 1 walking, 2 sprinting, from `isSprinting` and horizontal
`thisController.velocity` above 0.2 m/s); `body.prop` (prefab from the clip-pack bundle parented
to a bone under the local arms metarig or a remote player's body metarig at a fixed baked local
pose, destroyed on Stop or at `releaseSeconds`); `body.scopedFirstPersonTransformRestore`
(arms-metarig pose restoration without a whole-player rebind);
`body.stabilizeLocalCameraPosition` (final-`LateUpdate` position pinning for short local
interactions, mouse-look rotation stays live, the guard spans two `LateUpdate`s after restore);
`body.localCameraOwnedExternally` (stand down every local camera and visor behaviour for the
session). All default false and are independent of the default-on rig-control restore and
restore-scoped camera pin.

### `LocalViewmodelPresenter` (`DedicatedLocalViewmodel`, `Hybrid`)

Camera-local, consumer-owned presentation for interactions that need geometry or animation
isolation from the live player rig. It hides only the renderers named by the manifest, drives
optional animator lifecycle parameters, rebuilds and evaluates the viewmodel's own `RigBuilder`
after `Rebind` (the authored clips drive IK targets that only move arm bones through that graph),
and restores the live first-person renderers when the interaction stops. Bundles over 16 MB are
loaded asynchronously through a preload coroutine; a synchronous start on one is refused.

## Session lifecycle

- A player has at most one active `BodyWorld` session. Starting another first interrupts and
  fully restores the existing API session (`interaction.preempted`), then takes ownership.
  Dedicated local viewmodels do not participate in this player-body lock.
- A `BodyWorld` start fails with `player_animator_owned_externally` when a non-API system has
  replaced the expected vanilla local/remote controller. An active presenter also auto-stops if
  its applied controller is replaced (`live_body.ownership_lost`). It never restores over the new
  owner.
- Code that swaps `playerBodyAnimator.runtimeAnimatorController` directly must either defer while
  `IsPlayerInteractionActive(player)` is true or call `TryStopPlayerInteractions`, verify the
  query is false, and only then take ownership.
- `durationSeconds > 0` in the manifest (or a positive `ExpectedDurationSeconds`, which wins) →
  the coordinator auto-stops after that time (`NaturalEnd`). Zero → indefinite; end via
  `TryBeginInteractionExit` (plays the exit animation, waits `body.exitSeconds`, then stops) or
  `TryStopInteraction` (immediate).
- Presenters implement the internal `IInteractionPresenter`: `TryStart`, `Tick`, `Stop`,
  `float BeginExit()` (fire exit trigger, return seconds to wait; 0 if not applicable),
  `bool ShouldAutoStop` (polled by the coordinator; the live-body presenter sets it when the
  player enters a vanilla special animation, starts climbing a ladder, dies, or loses controller
  ownership → stop reason `Interrupted`), and
  `bool TrySetAnimatorParameter(name, type, value)`.

## Restore scopes

### First-person rig controls

Before every local or remote `BodyWorld` controller swap, the presenter captures local TRS for the
`RigArms` control subtree as a fallback. For the local player, the startup-pristine `[RigDiff]`
snapshot is the primary restore source; equip-time locals are used only for transforms without a
pristine baseline. The scoped first-person snapshot restores first, then rig controls restore
before `RigBuilder` rebuild, so neither authored curves nor a contaminated equip pose can remain
on empty-hand IK targets. This first-person `RigArms` pristine source remains local-only; remote
first-person controls retain the equip-time fallback.

### Third-person rig controls

A separate third-person restore scope runs for every local and remote `BodyWorld` session. It
captures and restores full local TRS for `spine.003/LeftArm_target`, `spine.003/RightArm_target`,
and both `Rig 1/*Leg` control groups (group parents, hints, and targets), plus local rotation only
for `shin.L` / `shin.R` to re-seed each leg's Two Bone IK bend plane.

A highest-priority `PlayerControllerB.Awake` prefix captures each player's serialized authored
local TRS before player scripts or movement clips can run; that per-player pose is the pristine
primary source, and session-entry values restore only transforms without it.

The v81 prefab-rest comparison (0.05 m position, 8 degrees rotation, 0.01 scale) never rejects a
movement-mod-aware authored baseline outright, but an `Awake` capture it flags implausible is not
trusted unconditionally: it is accepted with
`action='accept_authored_default_pending_runtime_recapture'` and replaced at the first
verified-clean idle session entry (local player, standing, no special animation, near-zero
horizontal speed, camera at vanilla rest) via
`TryRecapturePristineThirdPersonRigPoseIfImplausible`, logged as
`[RestoreSeam.tprig] rest_baseline_recaptured`. Without the recapture, a contaminated `Awake`
capture (observed 2026-08-05: maxPositionDelta 1.005 m, maxRotationDelta 163 degrees) becomes the
Stop-restore authority and drives cumulative viewpoint drift. The recapture runs only through the
session-entry camera-drift heal, so it depends on `Heal Camera Drift At Session Start`; remote
players' baselines are never repaired — their Stops keep restoring the `Awake`-time capture.

`Restore Third-Person Rig Control Pose` and `Restore Pristine Third-Person Rig Control Pose` are
independent default-on kill-switches. Every capture, restore, missing-data, contamination,
disabled, and animator-ownership gate logs at Info under `[RestoreSeam.tprig]`.

### Vanilla arms glue

After a successful local restore and rig rebuild, the presenter synchronously reproduces vanilla's
three-way arm handling: special interactions set arms-metarig local Euler to `(-90, 0, 0)` without
a camera-Y pin; `localArmsMatchCamera == true` uses the camera-relative `LateUpdate` position;
false uses the `Update` position from the metarig's current position and forward. Both normal
branches apply the head-bob-off camera-Y pin and then rotate the metarig to
`localArmsRotationTarget`. The false branch snaps to vanilla's converged rotation target instead of
taking one `15f * deltaTime` lerp step from stale restore state. This all runs before the
same-frame `Animator.Update(0f)` and `RigBuilder.Evaluate(0f)`.

### Camera rotation

Every local `BodyWorld` controller seam captures the gameplay camera and camera-container local
rotations before the controller change. Start reapplies the exact entry rotations after rig
rebuild. Stop preserves the captured gameplay-camera pitch but, by default, discards local yaw and
roll to zero and restores `CameraContainer` exactly to authored rest `(90, 359.8182, 0)`. Stop only
writes after the animator ownership guard permits snapshot restore. `Restore Camera Rotation`
controls seam capture/reapply, while `Restore Camera Rotation Snap To Rest` independently controls
the default-on Stop sanitation. `[RestoreSeam.camerarotation] stop_restore_gate` logs the discarded
gameplay-camera Y/Z and container rest deviation before applying the selected policy.

During every local `BodyWorld` session, the default-on camera-rotation stabilizer reads only the
current vanilla-owned local pitch and writes gameplay-camera local yaw/roll as absolute values from
the immutable session-entry baseline. It runs after ordinary `LateUpdate` writers and immediately
before rendering, preventing recoil or other consumer effects from feeding their previous output
into later frames. `Stabilize Camera Rotation During Session` is the global kill-switch; this
protection is independent of the manifest's opt-in position stabilizer.

### Camera position

Every local `BodyWorld` stop default-pins the gameplay camera at its Stop-entry player-local
position through the restore frame and two `LateUpdate`s. The position-only pin is reconstructed
from the moving player root, so root motion passes through; rotation continuity and sanitation are
handled by the rotation guards above. Remote sessions have no camera work. The pin uses the
default-on `Restore Camera Pin` config key.

Local Stop also snaps the camera-chain local positions — `CameraContainer`, gameplay camera,
first-person arms metarig root, and local-arms transform — back to the authored defaults captured
before `PlayerControllerB.Awake` (`ApplyCameraChainPositionSnapToRest`). Authored clips can write
positions on these transforms that vanilla only ever derives from each other, so stop-entry residue
would otherwise persist and stack across sessions (observed camera Y climbing ~2.35 → 2.47 with
weapons rendering progressively lower). When the snap applies, the restore-scoped
`LocalCameraPositionStabilizer` — whose memorised Stop-entry position is contaminated by the
authored clip's final pose — is retargeted to the snapped position (`RetargetToCurrentPosition`,
logged in `snap_applied ... stabilizerRetargeted=True`), so the restore-frame pin and the two
deferred `LateUpdate` applies hold the healed pose instead of undoing the snap. Skip paths
(crouching, special animation, external camera owner, kill-switch) intentionally leave the
stop-entry pin in force. `Restore Camera Chain Position Snap To Rest` is the default-on
kill-switch; `[RestoreSeam.camerachain]` logs every applied, skipped, and failed gate.

The session-start twin, `Heal Camera Drift At Session Start` (default on), measures the gameplay
camera against vanilla rest `(0, 2.35, 0.01)` and, when it deviates by more than 2 cm but less than
the displacement-guard threshold, restores the chain to authored defaults before the guard baseline
is captured, repairing drift accumulated by earlier sessions or other mods instead of adopting it
as the restore target.

### Visor

The same seam block preserves the local helmet visor's local and world pose plus its target point's
local pose when those transforms are under the body animator. It restores them after camera
rotation, snaps visor position to the restored target exactly as vanilla does, and keeps the
visor's captured world rotation so vanilla's `LateUpdate` rotation lerp does not jump ahead.
`Restore Visor Pose` is the independent default-on kill-switch; `[RestoreSeam.visor]` logs the
one-time runtime hierarchy finding and every capture, reapply, and skipped gate. If neither
transform is under the animator, the seam fix performs no visor writes.

During a first-person local session, `Hard Visor Glue During Session` (default on) re-glues the
visor to its camera target point — position *and* rotation, no lerp — just before each render.
Vanilla's own glue smooths rotation at 53 deg/s, which lags fast scripted or animated camera moves
and sweeps the mask edge into frame. A visor a consumer has parked away from the camera is left
alone, and `localCameraOwnedExternally` sessions always stand down.

### Camera displacement guard

The live-body presenter captures its guard reference at `TryStart` before diagnostics, bundle work,
or controller application. It compares that player-local reference with the v81 vanilla rest
expectation `(0, 2.35, 0.01)` and logs `pre_existing_displacement` plus `baseline_contaminated=True`
when the entry pose is already more than `1.25 m` away. Later evaluations measure only current
versus that TryStart reference.

An over-threshold movement that leaves the camera within `1.25 m` of the vanilla rest expectation
never stops the session: it continues with
`live_body.camera_displacement_guard.continue ... action='continue_within_vanilla_rest_envelope'`.
This kills the false positive where a baseline captured while crouched (~1.17 m below rest) tripped
`stop_genuinely_new_displacement` on stand-up (observed twice 2026-08-05). A contaminated session
likewise does not stop for movement that leaves it no farther from vanilla rest; a genuinely new
displacement that also exits the vanilla-rest envelope still stops through the unchanged restore
path.

Exempt sessions never stop through this guard and log `guard_exempt` at info level instead.
Exemption is the manifest's `exemptFromCameraDisplacementGuard` **or** membership of the
`Camera Displacement Guard Exempt Interactions` config list; the config default is empty. The same
one-per-start baseline line reports gameplay camera local Y/Z deviation from zero, camera-container
deviation from authored rest, the `0.02`-degree rotation threshold, `pre_existing_rotation_residue`,
and `baseline_crouching=`.

The special-animation auto-stop has the same two-source exemption:
`exemptFromSpecialAnimationAutoStop` in the manifest or the
`Special Animation Auto-Stop Exempt Interactions` config list (also empty by default). Death and
ladder climbing still stop an exempt session; only `inSpecialInteractAnimation` on its own is
ignored, logged once as `live_body.auto_stop_exempt ... action='continue_exempt'`.

## Config catalogue

Config file `BepInEx/config/com.y4ngz.interactions.cfg`.

### Section `Interaction Animation API V2`

| Key | Default | Effect |
|---|---|---|
| `Camera Displacement Guard Exempt Interactions` | *(empty)* | Comma-separated, trimmed, case-insensitive interaction ids. Operator override on top of the manifest flag. |
| `Special Animation Auto-Stop Exempt Interactions` | *(empty)* | Same, for the special-animation auto-stop. |

Both defaults were emptied when the exemptions moved into the manifest; a read failure falls back
to the empty default and logs `api.camera_displacement_guard_config_read_failed` /
`api.special_animation_auto_stop_config_read_failed` once.

### Section `Interaction Animation API V2 Restore Diagnostics`

| Key | Default | Effect |
|---|---|---|
| `Enable Restore Seam Frame Logger` | `false` | A preallocated sampler at execution order `32000` records the local camera, camera container, arms metarig, controller, and animator layer states. Every live-body `Stop` emits the three preceding and next six samples, tagged `coordinator_late_update` or `consumer_call`. The same gate emits one `[RestoreSeam.timing]` line per successful Start and per Stop with controller swap/restore, `RigBuilder.Build()`, prop instantiate/destroy, final `Animator.Update(0f)`, and `RigBuilder.Evaluate(0f)` phase milliseconds. |
| `Enable Restore Rig State Logger` | `true` | Emits every animator layer immediately before and after the restore `RigBuilder.Build()` loop and after the final `Animator.Update(0)`. |
| `Enable Pristine Rig Diff Probe` | `true` | Captures the pristine metarig-root local TRS, every `RigArms` transform, Two Bone IK and Chain IK constraint settings, and Rig/RigLayer weights, then diffs them two `LateUpdate`s after restore. |
| `Rig Diff Hotkey` | `NumpadMultiply` | `UnityEngine.InputSystem.Key` enum name; dumps the current rig diff on press. |
| `Restore Rig Control Pose` | `true` | Kill-switch for symmetric capture/restore of the local or remote player's `RigArms` subtree around every live-body session. |
| `Restore Pristine Rig Control Pose` | `true` | Local restores use the startup-pristine `RigArms` locals as the primary source; the equip-time snapshot covers only transforms with no pristine baseline. |
| `Restore Third-Person Rig Control Pose` | `true` | Kill-switch for local/remote capture and restore of the third-person arm targets, both leg-control subtrees, and shin pole-seed rotations. |
| `Restore Pristine Third-Person Rig Control Pose` | `true` | Use each player's serialized authored-default pose (captured in the `PlayerControllerB.Awake` prefix) as the primary restore source; session-entry stays the per-transform fallback. Vanilla-rest plausibility never rejects the authored baseline outright. |
| `Recapture Implausible Pristine Rig Baseline` | `true` | Replace a sanity-flagged implausible `Awake`-time baseline with a fresh capture at the first verified-clean idle session entry. Local player only; depends on `Heal Camera Drift At Session Start`. |
| `Restore Vanilla Arms Glue` | `true` | Apply vanilla's special-animation / `Update` / `LateUpdate` first-person arm branch before the same-frame animator and rig evaluation. `[RestoreSeam.armsglue] gates` logs the gate values and selected branch. |
| `Restore Camera Pin` | `true` | Pin every local live-body restore at its Stop-entry player-local camera position through the restore and two `LateUpdate`s, regardless of the manifest stabilisation opt-in. |
| `Restore Camera Rotation` | `true` | Exact local gameplay-camera and camera-container rotation capture/reapply across controller changes. |
| `Stabilize Camera Rotation During Session` | `true` | Preserve live pitch while anchoring local yaw/roll to immutable session-entry values, after other `LateUpdate` writers and before render. |
| `Restore Camera Rotation Snap To Rest` | `true` | At local Stop, preserve pitch, snap local Y/Z to zero, restore `CameraContainer` to `(90, 359.8182, 0)`. |
| `Restore Camera Chain Position Snap To Rest` | `true` | At local Stop, restore the local positions of `CameraContainer`, gameplay camera, first-person arms metarig root, and local-arms transform to authored defaults, and retarget the restore-scoped position stabilizer. Positions only. |
| `Heal Camera Drift At Session Start` | `true` | At local session start, restore the camera chain to authored defaults when the gameplay camera deviates from vanilla rest by more than 2 cm but less than the displacement-guard threshold, before the guard baseline is captured. Also the carrier for the implausible-baseline recapture. |
| `Restore Visor Pose` | `true` | Local helmet-visor pose preservation across Start and Stop controller changes; the captured pre-seam world rotation preserves vanilla's in-progress lerp state. |
| `Hard Visor Glue During Session` | `true` | Re-glue the visor to its camera target point (position and rotation, no lerp) just before each render during first-person local sessions. Stands down for a parked visor and for `localCameraOwnedExternally`. |
| `Enable External Camera Presentation Logger` | `true` | For `localCameraOwnedExternally` sessions, logs final pre-render body, first-person-arms, and local-visor renderer state once at entry and again only when render eligibility changes. Low-noise alternative to the full frame logger for finding local presentation leaks. |
| `Enable Remote Rig Diff Probe` | `true` | On every remote session, logs local TRS for `spine.003/*Arm_target`, `Rig 1/*Leg_target`, and `shin.L`/`shin.R` at session begin, two `LateUpdate`s after restore, and five seconds after restore. |
| `Restore State Mode` | `fresh` | How vanilla animator layer states resume at Stop. Accepted values `fresh`, `crossfade`, `replay`; an unrecognised value warns once under `[RestoreSeam.mode]` and falls back to `fresh`. |

### Section `Interaction Animation API V2 Debug`

The Page Down probe. Every payload is config-supplied and empty by default, so a released package
registers nothing and the probe reports `probe.disabled: pack_id_not_configured` once.

| Key | Default | Effect |
|---|---|---|
| `Enable Page Down Viewmodel Probe` | `true` | Master switch for the Page Down toggle. |
| `Prefer Live Body Animator For Page Down` | `true` | When the live-body half is registered, Page Down starts it instead of the viewmodel half. |
| `Probe Pack Id` | *(empty)* | Pack id the probe registers under. Empty disables the probe entirely. |
| `Probe Viewmodel Interaction Id` | *(empty)* | Interaction id for the viewmodel half. Empty disables that half. |
| `Probe Live Body Interaction Id` | *(empty)* | Interaction id for the live-body half. Empty disables that half. |
| `Probe Viewmodel Manifest File Name` | *(empty)* | Manifest file name for the viewmodel half, resolved against the default asset roots. |
| `Probe Body Manifest File Name` | *(empty)* | Manifest file name for the live-body half, same resolution. |
| `Probe Action Ready Bool Parameter` | *(empty)* | Animator `Bool` the Home key toggles on the running probe interaction. Empty disables that key. |
| `Probe Action Trigger Parameter` | *(empty)* | Animator `Trigger` the End key fires. Empty disables that key. |
| `Probe Action Index Parameter` | *(empty)* | Animator `Int` the End key sets, cycling 1–4, before firing the trigger. |

Usage: put a manifest and its bundles in a folder the default asset roots cover (the API's own
plugin folder is simplest for local work), fill in the pack id, one interaction id, and the
matching manifest file name, then press Page Down in game. The first press starts the interaction;
the second calls `TryBeginInteractionExit`, so the put-away animation plays and the session stops
itself. Home and End drive the action parameters while it runs. Registration is attempted once per
session — a failure logs `probe.pack_registration_failed` and goes quiet rather than retrying on
every key press.

## Log marker glossary

Coordinator and presenter events use the `[LCInteractionAnimationAPI]` prefix:

`api.initialized` / `api.shutdown` · `pack.registered` · `interaction.started` /
`interaction.stopped` / `interaction.preempted` / `interaction.exit_begun` /
`interaction.start_rejected` · `probe.started` / `probe.toggle_off` / `probe.disabled` ·
`live_body.bundle_loaded` / `live_body.controller_applied` / `live_body.clip_pack_applied
overriddenSlots=N` · `live_body.prop_attached` / `live_body.prop_released` ·
`live_body.exit_begun` · `live_body.rig_rebuilt phase='start'/'restore'` ·
`live_body.frame` / `live_body.calibration` (0.25 s cadence) · `live_body.ownership_lost` ·
`live_body.auto_stop_requested` / `live_body.auto_stop_exempt` ·
`live_body.restored controllerRestore='restored'` · `local_viewmodel.instantiated` /
`local_viewmodel.anchor_aligned` / `local_viewmodel.stopped`.

Restore instrumentation markers:

| Marker | Reports |
|---|---|
| `[RestoreSeam]` | The frame window and Stop invocation phase. |
| `[RestoreSeam.rig]` | The three animator-state checkpoints. |
| `[RigDiff]` | Pristine capture, hotkey and automatic diffs, reflection degradation. |
| `[RestoreSeam.rigpose]` | Session rig-control capture/restore counts, separated into pristine-primary and equip-fallback. |
| `[RestoreSeam.tprig]` | Authored-default capture, vanilla-rest sanity (including `action='accept_authored_default_pending_runtime_recapture'`), the `rest_baseline_recaptured` runtime replacement, initialisation gates, and pristine/equip-fallback restore counts for local and remote players. |
| `[RemoteRigDiff]` | Remote session-begin, restore-plus-two-`LateUpdate`, and restore-plus-five-second local-TRS samples. |
| `[RestoreSeam.pin]` | The default-on local restore camera pin. |
| `[RestoreSeam.rigeval]` | Same-frame rig evaluation counts and failures. |
| `[RestoreSeam.render]` | Start/Stop render-time camera, hand, metarig, visor/target pose, visor renderer state, and visor-camera enabled state. |
| `[RestoreSeam.armsglue]` | The synchronous vanilla glue result and its gates. |
| `[RestoreSeam.camerarotation]` | Local rotation capture/reapply values, Stop snap selection, discarded residue, every gate. |
| `[RestoreSeam.camerachain]` | The Stop position snap and session-start drift heal (`snap_applied` with before/after player-local positions and `stabilizerRetargeted=`, plus every `snap_skipped` / `snap_failed`). |
| `[RestoreSeam.visor]` | Visor hierarchy, local/world pose capture, synchronous glue, skipped gates. |
| `[RestoreSeam.timing]` | One per-phase millisecond summary for each Start and Stop. |
| `[RestoreSeam.locomotion]` | Parameter reapplication count at the start seam. |
| `[RestoreSeam.mode]` | An invalid `Restore State Mode` value. |

Camera-displacement markers all include the interaction id, threshold, baseline source, and
contamination facts:

- `live_body.camera_displacement_guard.baseline_captured` — every local `TryStart` reference, plus
  `pre_existing_rotation_residue`, `baseline_crouching=`, and gameplay-camera and container
  rotation deviations, exactly once per session start.
- `.pre_existing_displacement` — a warning at `TryStart` and, if needed, a one-time warning when an
  over-threshold change is ignored because the contaminated state did not worsen.
- `.guard_exempt` — one-time info continuation for an exempt session.
- `.continue` with `action='continue_within_vanilla_rest_envelope'` — info continuation for
  over-threshold movement that stays within `1.25 m` of vanilla rest.
- `.stop` — error-level, fact-only stop for genuinely new displacement.
- `.baseline_unavailable` / `.evaluation_unavailable` — the guard's degraded continuation gates.

## Internal reason strings

These never reach a consumer; they appear in `[RestoreSeam.tprig]` and related lines as the
`reason=` field of a skipped or failed internal operation:
`restore_diagnostics_not_initialized`, `kill_switch_disabled`, `player_missing`,
`pristine_baseline_unavailable`, `already_recaptured`, `baseline_already_plausible`,
`candidate_still_implausible`, `recapture_failed:<message>`, `no_transforms_restored`,
`already_refined`, `no_transforms_refined`.
