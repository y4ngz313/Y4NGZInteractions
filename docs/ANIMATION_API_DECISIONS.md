# Animation API Decisions

**Internal ADR log (maintainer reference).** External modders: see `docs/GETTING_STARTED.md`,
`docs/API_REFERENCE.md`, and `docs/MANIFEST_REFERENCE.md` for the supported surface. This log
records *why* the runtime behaves as it does, including reasoning that is now historical.

Last updated: 2026-08-05

Decision log for the interaction animation architecture. Read before proposing runtime changes.
Entries marked **SUPERSEDED** are kept only so old reasoning is not re-litigated.

## 2026-08-05: Publish as a Public MIT Library

Decision: the API ships publicly as `y4ngz313/Y4NGZInteractions` on GitHub under MIT, from a
fresh git history rather than the local development history. The static regression suite gained a
guard that fails the build when a consumer literal (a consumer interaction id, pack id, bundle or
manifest file name) appears anywhere under `src/`.

Reason: the local history carries consumer bundles and machine-specific paths that cannot be
published or rewritten cheaply, and a public API's ownership boundary has to be enforced by a
test rather than by a house rule — every consumer literal removed today entered the repository
under the same rule that was supposed to prevent it.

## 2026-08-05: Consumer Payloads and Authoring Tools Leave the API

Decision: consumer animation payloads and the Unity authoring tools move to the consuming mods
(Y4NGZUpgrades, Y4NGZCompany, Y4NGZMonsters). The packages ship as a pure library: a single
managed DLL and its docs, no bundles, no clips, no editor scripts. `runtime-assets/` is untracked
in its entirety and is now local dev-only scratch; the stale `tools/unity/` mirrors were deleted.

Reason: the API is consumer-agnostic by charter, and roughly 120 MB of consumer bundles in the
repository contradicted that at every level — release size, a per-consumer prop presenter
hardcoded in the runtime, and mirrored copies of authoring tools that drifted from the live ones
in the maintainer's Unity projects. Mirrors that cannot be verified from this repository are worse
than absent.

## 2026-08-05: A Viewmodel Manifest Must Author Its Bundle and Prefab

Decision: the manifest schema carries no drone-tablet defaults. `localViewmodel.bundleFileName`
and `localViewmodel.prefab` default to empty and are validated as required
(`manifest_viewmodel_bundle_file_empty`, `manifest_viewmodel_prefab_empty`), alongside the
existing controller and camera-anchor requirements. No value is silently inherited from another
pack.

Reason: a schema default naming one consumer's bundle made every other consumer's manifest
implicitly depend on that consumer shipping, and a typo in a bundle name resolved to the drone
tablet's assets instead of failing. Failing closed at validation names the fault at author time.

## 2026-08-05: Interactions Declare Their Own Guard Exemptions

Decision: `exemptFromCameraDisplacementGuard` and `exemptFromSpecialAnimationAutoStop` are root
manifest bools declared by the authoring consumer. The two config keys
(`Camera Displacement Guard Exempt Interactions`, `Special Animation Auto-Stop Exempt
Interactions`) now default to empty and are demoted to a user override: the effective exemption is
the manifest flag OR the config list. The built-in `y4ngz.cctv.operator` default is removed.

Reason: an exemption is a property of the interaction, which the consumer knows and the API does
not. Shipping one consumer's interaction id as an API default exempted it in every profile
including installs that did not have the mod, and left the consumer unable to declare a new
exemption without a config edit by the player.

## 2026-08-05: `sockets.prop` Supersedes `sockets.tablet`

Decision: the socket field naming the held prop renderer is `sockets.prop`. `sockets.tablet` is
retained as a deprecated alias so manifests authored against the original schema keep loading;
runtime code reads the resolved value, never the raw alias, and validation reports
`manifest_socket_prop_empty`.

Reason: a generic API cannot name a field after one consumer's item. The alias costs one
resolution branch and keeps existing consumer manifests loading unchanged.

## 2026-08-05: The Debug Probe Is Config-Driven and Off by Default

Decision: every probe payload identity — pack id, both interaction ids, both manifest file names,
and the three animator parameter names — is a config entry defaulting to empty. An unconfigured
probe self-disables and logs `probe.disabled: pack_id_not_configured`. Nothing is baked in.

Reason: the probe was the last place in `src/` holding a consumer's ids and manifest names, and it
registered them on every load. Config-driven and disabled by default makes it a diagnostic any
consumer can point at their own pack, and keeps the shipped library free of consumer identity.

## 2026-07-24: Anchor Live-Body Camera Y/Z and Sanitize Stop Rotation

Decision: every local live-body session default-enables a rotation stabilizer that treats the
session-entry gameplay-camera local yaw/roll as immutable. After ordinary `LateUpdate` writers and
again immediately before render, it reads the current vanilla-owned local pitch and writes
`Quaternion.Euler(currentPitch, entryYaw, entryRoll)`. It never derives Y/Z from the prior frame's
output. `Stabilize Camera Rotation During Session` is the new default-on global kill-switch.

At Stop, seam capture remains before stabilizer release so it measures raw stop-entry residue.
When `Restore Camera Rotation Snap To Rest` (new, default on) is enabled, restore preserves the
captured gameplay-camera pitch, snaps gameplay-camera local Y/Z to zero, and writes
`CameraContainer.localEulerAngles = (90, 359.8182, 0)`. The Info-level
`[RestoreSeam.camerarotation] stop_restore_gate` records discarded camera Y/Z and container
deviation from rest. Disabling the snap returns to exact stop-entry replay; animator-ownership
rejection still prevents either policy from writing over a new owner.

The position displacement guard's single session-start baseline line now also reports
gameplay-camera Y/Z deviation from zero, camera-container deviation from authored rest, and
`pre_existing_rotation_residue` using a `0.02`-degree threshold.

Reason: the 2026-07-24 verification log entered a heavy-shotgun session at
`(54.05132, 0, 0.025076)` and stopped ten seconds later at
`(1.834537, 359.3166, 1.064444)`. During that interval, consumer recoil logs recaptured baselines
at progressively contaminated values while its janitor skipped for `session_active`; vanilla
rewrites local X while preserving existing Y/Z. Interactions previously had no session rotation
invariant, and its exact Stop replay then preserved the contaminated quaternion after teardown.

## 2026-07-24: Capture Third-Person Pristine Controls Before Player Scripts Run

Decision: `Restore Pristine Third-Person Rig Control Pose` now sources each player's primary pose
once from a highest-priority `PlayerControllerB.Awake` prefix. At that point the serialized
third-person control transforms exist but no player script or movement clip has run. The
vanilla-rest plausibility calculation never rejects this movement-mod-aware authored baseline
outright, but since 2026-08-05 an implausible Awake capture is accepted only pending runtime
recapture: it is replaced at the first verified-clean idle session entry (local player only,
`Recapture Implausible Pristine Rig Baseline`, via the session-start camera-drift heal), because
a contaminated Awake capture (observed 2026-08-05: 1.005 m / 163 degrees off) otherwise becomes
the Stop-restore authority and drives cumulative viewpoint drift. If the spawn-time capture is
unavailable, the session-entry snapshot remains the explicit fallback; a moving live pose is
never promoted to pristine — the recapture accepts only a verified-clean idle pose.

Reason: on the current modpack, first-session sightings are already under continuous movement
animation and consistently differ from v81 vanilla rest by about `0.83 m` and `93.86` degrees.
Treating those values as a rejection gate made the pristine dictionary permanently empty for
every player.

## 2026-07-18: Attribute Camera Displacement From a Hygienic Session Reference

Decision: the local live-body displacement guard captures its player-local gameplay-camera
reference as the first operation after `TryStart` accepts the request, before diagnostics,
bundle work, or the authored controller. It compares that reference with the v81 vanilla-rest
expectation `(0, 2.35, 0.01)`. A difference greater than the existing `1.25 m` threshold marks
the reference `baseline_contaminated=True`, logs warning marker
`live_body.camera_displacement_guard.pre_existing_displacement`, and prevents movement back
toward vanilla rest from being attributed to the session. A non-exempt session stops only when
the displacement from the TryStart reference itself exceeds `1.25 m`, the current pose is also
more than `1.25 m` from the vanilla rest expectation (since 2026-08-05:
`action='continue_within_vanilla_rest_envelope'` otherwise — a crouched baseline ~1.17 m below
rest must not trip the guard on stand-up), and, for a contaminated
reference, the current pose is farther from vanilla rest than the entry pose. The proven restore
path remains unchanged after a genuine stop.

`Camera Displacement Guard Exempt Interactions` is a new comma-separated config key in section
`Interaction Animation API V2`, defaulting to `y4ngz.cctv.operator`. Matching is trimmed and
case-insensitive. An exempt interaction is never stopped by this guard; the first over-threshold
evaluation logs `live_body.camera_displacement_guard.guard_exempt` at info level. The ordinary
stop is now `live_body.camera_displacement_guard.stop` and reports measurements only: TryStart
baseline, current position, vanilla-rest expectation, both rest-relative distances, baseline
source, and the pre-existing/contaminated flags. It no longer claims unconditionally that an
authored controller moved the camera hierarchy. Baseline-unavailable and evaluation-unavailable
gates also log their continuation action.

Reason: playtest 9 showed the old guard stopping two `y4ngz.cctv.operator` sessions while the
station pose-lock and legitimate enter animation moved the camera, then running the complete
restore stack against that external owner. It also blamed a crossbow controller when its
equip-time reference was already displaced by external slide residue. A saved-config-safe
exemption handles the intentional CCTV owner, while per-session contamination metadata makes
later evidence distinguish pre-existing state from genuinely new displacement.

## 2026-07-17: Preserve Script-Driven Camera Rotation Across Animator Rebind

**SUPERSEDED FOR STOP Y/Z BY THE 2026-07-24 SANITATION DECISION.**

Historical decision: every local live-body controller seam captures the gameplay camera and
camera-container local rotations immediately before the controller change, then replays them after
the rig graph is rebuilt and before final evaluation. Exact replay remains the Start policy and
the Stop fallback when the 2026-07-24 snap is disabled. Stop writes only after an
ownership-guarded animator restore succeeds. `Restore Camera Rotation` is a default-on kill-switch;
capture, reapply, every skip gate, missing target, and exception are logged under
`[RestoreSeam.camerarotation]`.

Reason: playtest 6 proved that `AnimatorStateSnapshot.Restore`'s `Animator.Rebind()` zeroed the
script-driven `gameplayCamera.transform.localRotation` on the rendered Stop frame. Vanilla
`PlayerControllerB.PlayerLookInput()` restored the real pitch on the next frame, producing a
one-frame whole-screen jump whose size matched the current look angle. The existing camera pin
protected position only. Exact capture/reapply preserves the live mouse-look result without
re-deriving vanilla `cameraUp` semantics, and the symmetric Start guard protects the same
Rebind-era hierarchy behavior.

The existing `Enable Restore Seam Frame Logger` gate also enables one `[RestoreSeam.timing]` line
per successful Start and per Stop. Start separates setup, bundle load, controller swap, rig build,
camera reapply, final animator update, rig evaluation, diagnostics, prop instantiation, and
finalization. Stop separates setup, camera capture, prop destruction, animator restore, pose
restore, rig build, seam glue, final animator update, rig evaluation, and cleanup. These timings
measure the start/stop hitch before any optimization is selected.

## 2026-07-16: Restore Pristine Rig Controls and Reapply Vanilla Arm Glue Synchronously

Decision: the local live-body restore uses the scene-pristine `[RigDiff]` snapshot as the primary
source for every `RigArms` transform, with the equip-time capture retained only as a
per-transform fallback when no pristine baseline exists. The existing `Restore Rig Control Pose`
master switch remains; `Restore Pristine Rig Control Pose` is a new default-on kill-switch that
selects the pristine-primary behavior. Remote sessions retain the equip-time path because the
startup pristine probe is deliberately local-player-only.

After snapshot, scoped first-person, and rig-control restoration, and after the rig graph is
rebuilt, local `Stop()` reproduces vanilla's three-way first-person arm handling. A special
interaction sets the arms metarig local Euler to `(-90, 0, 0)` and skips the camera-Y pin. Normal
play with `localArmsMatchCamera == true` uses the camera-relative `LateUpdate` position path;
normal play with it false uses the `Update` path, positioning local arms from the metarig's
current position and forward before rotating the metarig. The head-bob-off camera-container Y pin
runs only in the two normal-play branches. This occurs before the existing same-frame
`Animator.Update(0f)` and `RigBuilder.Evaluate(0f)`. `Restore Vanilla Arms Glue` is a separate
default-on kill-switch, every invocation logs its selected branch, and all vanilla field access is
failure-contained. The false branch deliberately snaps metarig rotation to the target instead of
taking vanilla's `15f * deltaTime` lerp step: the restore needs vanilla's converged steady-state
pose in one frame, not a partial step from stale restored state.

Reason: playtest 3 measured both failure modes directly. The stop frame rendered an equip-time
arm pose and both hands moved about 0.34 m on the following vanilla frame; applying the matching
glue branch before rig evaluation makes the stop frame use vanilla's live steady-state arm basis.
Separately, one equip-time capture was already 0.308 m off the startup-pristine right-arm target,
so restoring that capture could preserve external or weapon-authored contamination. Scoped
first-person restoration now precedes rig-control restoration so it cannot overwrite the
pristine targets afterward.

## 2026-07-16: Restore Animator State Fresh By Default

Decision: `Restore State Mode` config (fresh | crossfade | replay) with `fresh` as default —
restore controller, parameters, speed, and layer weights but never `Play` the equip-time states
back. Playtest-2 probes showed `Play(fullPathHash, normalizedTime)` teleporting the camera
container (8.3 cm/frame walking) and stranding non-looping states past end (one knee-cam cause).
Fresh mode removed both failure modes in playtest 3.

Also established: the playtest-3 knee-cam pre-existed under the vanilla controller with no
session active (suspect: Upgrades movement presentation exit residue) — the API's displacement
guard + restore is what repaired it on the next pickup, so knee-cam is not (this time) an API
defect. The guard should stop attributing entry-time displacement to the authored controller.

## 2026-07-16: Default Restore-Seam Guards for Rig Controls and Camera Position

Decision: every live-body session captures the complete `RigArms` local-TRS subtree before its
controller swap, then reapplies it only after a successful ownership-guarded animator snapshot
restore and before any RigBuilder is restored or rebuilt. This remains the local/remote fallback
behind the global `Restore Rig Control Pose` kill-switch; the later pristine-primary decision
above supersedes equip-time locals as the primary source for local-player transforms. Local
restores also default-enable the existing restore-scoped camera guard: capture
the Stop-entry player-local camera position, pin position only through the restore plus two
`LateUpdate`s, and let player-root motion pass through without continuously constraining
rotation. The later exact seam-rotation decision above separately protects rotation from
`Animator.Rebind`. The existing
`scopedFirstPersonTransformRestore` manifest path remains separate and unchanged.

The restore sequence also evaluates each active rebuilt `RigBuilder` at zero delta immediately
after `Animator.Update(0f)`. `RigBuilder.Build()` creates a new playable graph, but that graph
otherwise waits until the next animation update to evaluate; the explicit same-frame evaluation
ensures the IK-only first-person arms are applied before the restore frame renders.

The camera guard is bound as the new `Restore Camera Pin` key, default `true`. BepInEx persists
bound config values, so changing a code default does not change profiles where that key was
already saved. Default flips that must reach existing profiles therefore require a key rename;
the prior camera-pin entry is intentionally no longer bound and becomes orphaned config state.

Reason: post-restore `[RigDiff]` probes repeatedly found the same persistent displacement on
`ArmsRightArm_target` across independent stops (`localPositionDelta.x = -0.02314496`, about
10 degrees), with other stops at roughly 0.25-0.28 position delta and 32-81 degrees. Lethal
Company's resting/empty-hand states do not write the Chain IK targets, while both arm
`ChainIKConstraint`s remain at weight 1, so the restored target residue bends vanilla arms
indefinitely. Restore-seam samples also measured the camera-container local z snapping on the
Stop frame from an authored value to vanilla rest z `2.096` (including `2.0665 -> 2.096`), with
the camera world position inheriting the jump. The already-probed player-local camera guard
directly masks that discontinuity while the restored layers settle.

Rejected: leaving either correction as a per-manifest opt-in, because both seams are generic
controller-restore behavior and affect every consumer; restoring the whole arms metarig while
bypassing `Animator.Rebind`, because that revives outgoing animated-transform residue and the
floating-arms failure Rebind was introduced to clear. Pose replay is also rejected when
`AnimatorStateSnapshot.Restore` reports `controller_changed_externally`, because a newer owner
must retain its animation state.

## 2026-07-11: Exclusive Live-Body Ownership and Bind-Pose Restoration

Decision: the coordinator permits one `BodyWorld` session per player. A new API session
interrupts and restores the old one before starting. Starts fail closed when a non-API custom
controller owns the animator; active presenters yield if their controller is replaced. Direct
controller owners must query `IsPlayerInteractionActive` before starting; they may defer or
interrupt through `TryStopPlayerInteractions` before taking ownership. Every successful
snapshot restore calls `Animator.Rebind()` before replaying the captured vanilla parameters,
layer weights, states, and normalized times.

Reason: controller references alone are not ownership. Concurrent sessions can snapshot each
other's controllers and restore out of order, producing floating bodies/arms. Even a clean
single-session controller restore can leave wrist or arm transforms written by the outgoing
clip when the resumed vanilla state does not curve those bones. Rebind clears that residue.

Rejected: consumer-specific cleanup delays (timing-dependent); allowing stacked BodyWorld
sessions (restore ordering is ambiguous); blindly restoring when another controller has taken
ownership (clobbers the newer animation); treating each newly authored clip as the fix site.

Follow-up evidence: lifecycle restoration can still be overwritten after Stop by a consumer's
`LateUpdate` or `Application.onBeforeRender` callback. Such callbacks must verify their handle
is still active on every write and target only the intended rendered skeleton. The live-body
presenter also stops if an authored controller moves the local gameplay camera more than 1.25 m
from its player-local start position; this is a last-resort guard, not approval for unsafe clips.

**SUPERSEDED for generic restore seams by the 2026-07-16 decision.** Scoped trial (2026-07-12):
`scopedFirstPersonTransformRestore` is an explicit manifest opt-in,
initially enabled only for Worklight. It captures descendants of the local arms metarig, restores
them before the RigBuilder rebuild, and bypasses whole-player `Animator.Rebind`. Existing weapon,
Tablet, and CCTV behavior remains unchanged while Worklight/Ghost parkour validate the approach.
Worklight also opts into `stabilizeLocalCameraPosition`: a final-LateUpdate guard pins only the
gameplay camera's player-local position while leaving mouse-look rotation untouched. This closes
small controller-driven viewpoint shifts that are visible but remain below the emergency 1.25 m
displacement threshold. The position guard remains for two LateUpdates after controller restore,
then releases itself; immediate destruction exposed a final-frame snap. The option is not enabled
for weapons, Tablet, or CCTV.

## 2026-07-02: Action Gestures Via Generic Animator Parameter Passthrough

Decision: consuming mods (Y4NGZUpgrades) drive gestures through
`TrySetInteractionBool/Int` + `TryFireInteractionTrigger` with controller-defined parameter
names; the interactions DLL exposes no tablet-specific action API.

Reason: the shell controller owns gesture semantics; a generic passthrough means new gestures
are authoring-only changes. The tablet stays a reference implementation, not a special case.

Rejected: per-action API methods; action config in the manifest (the caller knows what it wants
to play and when — the manifest only maps slots to clips).

## 2026-07-02: Presenter-Polled Auto-Interrupt

Decision: presenters expose `ShouldAutoStop`; the coordinator polls it each Tick and stops the
session with `Interrupted`. The live-body presenter sets it on `inSpecialInteractAnimation`,
`isClimbingLadder`, or `isPlayerDead`.

Reason: vanilla special animations own the body animator and camera; keeping the tablet layers
active fights them (same class of bug as the old FK-vs-IK fight). Immediate stop is correct —
the vanilla animation takes over the view, so no put-away animation is wanted.

Rejected: Harmony patches on vanilla interaction entry points (invasive, version-fragile);
graceful exit on interrupt (Hide playing during a ladder grab looks wrong).

## 2026-07-02: Finger Reference = Vanilla Hold + Curl-Axis Knobs

Decision: the finger transfer anchors LC fingers to the vanilla-hold pose plus per-hand extra
curl about each finger's natural curl axis (rest→vanilla delta axis), tuned via preview
close-ups rendered from the item side with the tablet prop attached.

Reason: the source rest hand is itself a half-curled grip, so rest-to-rest correspondence left
LC's splayed rest visible in game (measured: index/thumb grip deltas only 10–19°).

Rejected: pure rest-anchored transfer (splayed hands); per-clip manual finger keyframes
(unmaintainable across 10+ clips and future packs).

## 2026-07-02: Toggle Lifecycle Via Graceful Exit API

Decision: indefinite interactions (`durationSeconds: 0`) end through
`TryBeginInteractionExit(handle)` → presenter `BeginExit()` fires the controller exit trigger
(put-away animation plays), coordinator auto-stops after `body.exitSeconds`.

Reason: Page Down is now a toggle (tablet stays out until pressed again); a fixed duration or an
immediate `TryStopInteraction` would cut the `Tablet@Hide` animation.

Rejected: probe-side timers around `TryStopInteraction` (probe has no presenter access); fixed
long durations (not a toggle); restoring instantly and skipping Hide.

## 2026-07-02: Movement-Driven First-Person Loops Via Animator Int Param

Decision: the presenter drives `body.movementParameter` (`Y4NGZ_DroneTablet_Move`, 0/1/2) every
Tick from `player.isSprinting` + horizontal `thisController.velocity`; the shell controller's
first-person layer has Hold ⇄ Walk ⇄ Run equals-transitions.

Reason: Walk/Run are distinct authored loops in the source pack; the animator state machine is
the right place for blend logic, the runtime just reports movement facts.

Rejected: cross-fading clips from code; blend trees (harder to author via editor scripts, no
benefit for discrete states); patching vanilla movement params (fragile).

## 2026-07-02: Ship The Source Tablet As A Rigid hand.L Prop

Decision: the FPS pack tablet model ships in the clip-pack bundle and is attached at runtime to
LC's `hand.L` with a constant local pose baked by the retargeter
(`inv(B_left)`-mapped source pose; `tablet-prop-attachment.json` → manifest `body.prop`).

Reason: the source tablet (`Tablet_01`) is rigidly parented to `hand_l` — zero animation curves
needed; it follows Get/Idle/Walk/Run/Hide automatically and stays glued to the palm exactly like
the source. (This supersedes the older guidance to avoid attaching props to live LC hand bones —
that guidance predated the IK-target architecture; with hand rotation == target rotation the
hand bone is stable and authoritative.)

Rejected: baking prop trajectory curves on a rig path (needless indirection for a rigid prop);
`LocalItemHolder` socket (right hand, wrong side, filtered path); separate viewmodel prop.

## 2026-07-01: IK-Target Clips Are The Production First-Person Path

Decision (formalized 2026-07-02): first-person arm content = IK-target curves + finger FK curves
baked offline (v4 SourceAbsolute anatomical transfer), played through the shell controller +
clip-pack `AnimatorOverrideController` on `playerBodyAnimator`, with RigBuilder rebuilds around
controller swaps. See `ANIMATION_AUTHORING_PIPELINE.md`.

Reason: LC's own first-person arms are IK-target driven; feeding the targets is the native,
stable channel. Proven in game 2026-07-02 (correct hands, no twist).

This **SUPERSEDES**:
- *2026-07-01: Deprecate Live LC First-Person Arms As The Primary Target* — reversed; the arms
  ARE the target, the earlier failures were FK-vs-IK fighting and missing basis correction.
- *2026-07-01: Adopt Hybrid Body Plus Local Viewmodel Architecture* — the dedicated viewmodel is
  now diagnostics-only (`LocalViewmodelPresenter` retained behind config).
- *2026-07-01: Use FPS Rig LC Arms Mesh-Transfer POC For First Viewmodel Proof* — the POC prefab
  survives as the BAKER's sample rig, not a runtime asset.

## 2026-07-01: Keep V2 Inside Y4NGZInteractions.dll For Now

Decision: no separate public DLL until the architecture boundary is stable. Still current.

## 2026-07-01: Require Manifest-Driven Authoring

Decision: every interaction is manifest-driven; runtime never infers asset relationships or
stores per-animation offsets in code. Still current — the tablet toggle added `exitSeconds`,
`movementParameter`, and `prop` to the manifest rather than code constants.

## 2026-07-01: Retain Old Presenter Paths Only As Diagnostics

Still current (`LocalViewmodelPresenter`, `diagnosticVanillaOverrideClip`).

## 2026-07-01: Preload Large Viewmodel Bundles Before Interaction Start

Still current for the viewmodel path; the production clip-pack bundle is small (~2 MB incl. the
tablet model), loads synchronously without a hitch.
