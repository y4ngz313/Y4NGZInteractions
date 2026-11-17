# Architecture

Last updated: 2026-11-16

This document records only current 1.0 invariants.

## Scope

Y4NGZInteractions is a local presentation, resource ownership, lifecycle, and restoration runtime. Consumer mods own gameplay, input, payloads, networking, and any authoring/retargeting pipeline.

## Registration boundary

Caller-owned pack objects are inputs, never live configuration. Registration validates every manifest and snapshots identities, presentation kinds, JSON, normalized schema-2 data, and the normalized asset root. Registered state is immutable from the consumer's perspective.

## Resource model

A lease is a pair of resource kind and target player identity.

- BodyWorld remote: body animator.
- BodyWorld local: body animator plus local camera/arms presentation.
- DedicatedLocalViewmodel local: local camera/arms presentation.

Different remote players can coexist. Claims that resolve to the same pair conflict.

RejectIfBusy is non-mutating. InterruptExisting completes request resolution, external ownership validation, asset/controller/presenter preflight, and lease planning before suspending an incumbent. A failed replacement start reacquires and resumes the incumbent without resetting its natural-end or graceful-exit deadline.

## Session lifecycle

A successful start owns a unique local handle. The coordinator ticks sessions and stops them for natural duration, scheduled graceful exit, player invalidation, player death, round unload, presenter request/failure, explicit request, interruption, or shutdown.

Before one immutable InteractionEnded event is raised, presenter-owned state has been restored, the session is ended, its leases are released, and active lookup state no longer contains the handle. Subscriber exceptions do not block other subscribers.

## External ownership

BodyWorld never blindly replaces an animator controller. The presenter may take ownership only from the expected vanilla controller or from a conflicting API session that currently proves resource ownership.

Before vanilla multiplayer animation sync samples the live animator, the runtime ensures the game's current and previous state-hash lists contain one slot per active animator layer. It preserves existing hashes and only appends missing slots, so a BodyWorld controller with additional layers cannot interrupt vanilla locomotion replication.

## Restoration

Live-body restoration captures controller, layers, parameters, transforms, camera/arms state, visor state, rig-builder state, prop state, and other scoped presentation state before mutation. Stop restores from those snapshots. Scoped transform restoration is automatic rather than manifest-controlled.

Stance is a mid-session invariant, not only a stop-time repair. The swapped live-body controller carries a faithful copy of vanilla's Base Layer, so it inherits vanilla's one-way crouch cluster: the `crouching` Bool gates only the exits out of `CrouchDown`/`CrouchIdle`/`CrouchWalk`, and the sole edge back in is the `startCrouching` trigger, which vanilla fires from a crouch input edge. An AnyState edge taken mid-session therefore strands a still-crouched session in the standing cluster, where the standing locomotion clip keys the camera chain to standing height while the first-person arms stay crouched. Every tick a live-body session compares the player's stance against the Base Layer's settled state and re-fires `startCrouching` when a crouched player is observed outside the crouch cluster. That rule fires on observed state desync only, never on input, so it cannot double up with vanilla's own edge.

The camera displacement guard has two halves with disjoint ownership. The rest envelope is instantaneous — it sees one frame with no history — so it is telemetry plus pure magnitude only: it measures the viewpoint against both stance rest heights and stops only for a reading beyond the displacement threshold from both. It never stops for a stance mismatch, because that reading is true at t=0 of every legitimate crouch or stand transition, when the camera is by definition still at the opposite stance's rest. Stance damage is owned solely by the debounced stance viewpoint invariant, which requires the mismatch to persist past vanilla's crouch-glide window before it stops the session.

Stop-time camera-chain repair remains the teardown backstop, and every stop-time correction is applied at the camera-container level, never on the gameplay camera. The camera is a child of the container; vanilla derives the chain from the container every frame rather than rewriting the camera's own local position, so a container write is transient and self-healing while a camera write is permanent. `gameplayCamera.localPosition` must stay at its pristine captured value, the stop seam restores it when it has drifted, and a `teardown_residue_check` log asserts at warn level if any residue survives teardown. For a local player the correction combines the current crouch/stand rest height with clean session-entry horizontal placement, and falls back to vanilla horizontal rest when the captured entry already contains persistent camera residue. After the pristine chain restore the chain is authored-correct, so a further stance correction runs only when the measured chain still deviates from the stance target beyond tolerance. Controller-authored end-frame position is never adopted as the next session's baseline.

The restore-scoped camera pin obeys the same container-only rule. It survives teardown for two LateUpdates so the viewpoint cannot pop while the vanilla camera parent settles, and it holds the pose by translating the camera container, never by writing the gameplay camera's world position: vanilla re-authors the container from its clips during exactly that window, so a world write on the child baked the difference between the pin target and the animating container pose into `gameplayCamera.localPosition` — permanently, and after the teardown tripwire had already passed. A crouched drop pinned the 1.17 crouch rest against a container still animating down from 2.35 and left a ~-0.99 camera-local offset that put the next session's viewpoint through the floor. The pin's final release restores `gameplayCamera.localPosition` to pristine and emits a second `teardown_residue_check` with `phase='post_pin_release'`, so the tripwire now covers the deferred window as well as the camera-chain snap (`phase='camera_chain_snap'`).

One mod is known to own `gameplayCamera.localPosition` on purpose: First Person View pushes the camera away from the body by an amount that follows the look pitch, and rewrites it every LateUpdate. While it is loaded (`ExternalCameraLocalOwner`, resolved once from the chainloader) the seam never writes that transform: the pristine chain restore and the rest-baseline refine skip the camera, the stop seam and the pin release do not clear it, and the `teardown_residue_check` does not count it as residue. Every viewpoint measurement (session baseline, displacement guard, start-side drift heal, stance snap, pin target) then reads the point the camera occupies at its pristine local position instead of the camera itself, so the other mod's offset is neither healed nor pinned. Clearing the offset after First Person View had written it, and pinning the container against the pushed camera, rendered the two frames after a look-down stop from the wrong place. The opt-in session pin (`stabilizeLocalCameraPosition`) holds its pose by writing the camera's world position and is not adapted.

Two bindings are owned per frame rather than repaired at seams, because Unity re-captures their write-defaults value at the first natural evaluation after any controller swap, in whatever stance the player holds, and no seam-time steering can reach that capture (the assignment, `RigBuilder.Build`, a manual evaluation, and a pristine-held window were each proven too early). The first-person arms metarig position is keyed only by Idle1 and the crouch cluster; the metarig root position is keyed only by the walk family, JumpLand, FallNoJump and the crouch cluster, so Idle1, Sprint and Jump write back whatever was captured — after a crouched swap, the crouch pose, which every idle-to-walk crossfade then drags across the whole rig (camera included). `ArmsMetarigWriteBackOwner` and `MetarigRootWriteBackOwner` classify the Base Layer state, model the authored value per state (unkeyed standing states get the authored standing rest, transitions with an unkeyed endpoint blend the authored endpoints by transition time, the CrouchDown ramp evaluates at its own normalized time), and write only when the reading deviates beyond a 2 cm gate, so a healthy capture is a no-op every frame. Keyed and unknown states are the animator's. The root owner ticks at execution order -32000 as well as from the host, because the root is an ancestor of the camera chain and vanilla places the visor against that chain in `PlayerControllerB.LateUpdate`; a correction after that read moved the camera out from under an already-placed visor. Both owners also tick inside the start and stop seams after the final evaluation, followed by the vanilla arms glue, so the rendered seam frame matches the next vanilla frame.

Rest references are runtime-settled values, never prefab-authored or poisoned ones. The healthy standing camera rest in the player-local frame is (0, 2.351, +0.0135); an earlier value of -0.3545 in z was measured under the poisoned root and made the start-side drift heal fire on every session and the crouched-exit fallback push the camera 0.37 m back. The pristine camera-chain snapshot carries the prefab's pre-animation container pose, 9 mm above the runtime rest; vanilla's idle clips re-key the container every evaluation, so after the stop seam's final evaluation the deferred camera pin is retargeted to the evaluated pose rather than the snapped one.

Verbose probes are operational diagnostics and default off. Essential restoration remains active.

## Fades and the exit pose blend

Layer fades follow the smoothstep curve 3t² − 2t³ (0, 0.15625, 0.5, 0.84375, 1 at 0, 25, 50, 75 and 100 %). The exit has two stages.

**Stage A, the consumer controller is still on the animator.** This covers the graceful exit (`exitSeconds`) and the natural-end fade. The custom layer weights fall on the eased curve; a graceful exit fades each layer from its last base weight (before the viewer and yield factors) to 0. Under them the shell's vanilla-copy layers keep vanilla's per-frame layer weights by name. On the local body they also get vanilla's parameters; on a remote body they get the library's own locomotion parameters.

**Stage B, the exit pose blend, after Stop.** Stop restores exactly as without blending: the controller, parameters, rig, camera and visor return inside the call. With blending armed, Stop also:

1. captures P0, the local position and rotation of every blend bone, as the first statement after the `stop_begin` sample and before the locomotion parameters sync (P0 is the pose the session last rendered);
2. captures R, the restored pose, after the visor seam (the last transform write of Stop), and registers the blend;
3. writes P0 back once, only when this frame's animation pass already ran (a stop from LateUpdate, a post-animation callback, or a coroutine resumed by `WaitForEndOfFrame`). A stop from Update, FixedUpdate or a coroutine resumed by `yield return null` runs before that pass, so the bones keep R. The runner's first pass then writes P0 after the animation pass.

From then on two steps run every frame.

Before every animation pass, a system at the head of `PreLateUpdate` (ahead of `DirectorUpdateAnimationBegin`) puts R back on every channel that still holds the blend's last write. A channel another writer changed since is left alone. So no animation pass, rig rebuild or controller swap ever reads a value the blend wrote.

After the animation pass, the blend runner (order -31000, `LateUpdate`) computes and writes the blend. Each bone's position and rotation are handled separately. Per channel it reads the current value. If the value differs from the blend's last write (an exact compare), the animator or another writer drove that channel and the target is that value; otherwise the target is R. The blend's last write is the value the transform stored, read back after every write. The runner writes nlerp (rotation) or lerp (position) from P0 to the target at ease(t), with t = elapsed / 0.2 s. At t ≥ 1 it writes the target and ends. The target is re-read every frame, so a vanilla change during the blend is followed. The blend never touches the animator, its controller, states, parameters or layer weights, and it never writes scale.

After Stop returns, the bones hold P0 when the stop ran after the frame's animation pass, and R when it ran before it. The stop frame renders P0 either way. The reason for keeping R before the pass: a consumer that swaps the animator controller in the same call as an Update stop binds the restored pose, so the swap must not read P0. While a blend is live it costs about 4 transform reads and 2 transform writes per bone per frame; while idle the pre-pass system is one count check and the runner is disabled.

**Timing.**

| Rule | Value |
| --- | --- |
| Clock | `Time.deltaTime`, the scaled time the animator uses |
| First pass | The stop frame renders P0, written at Stop (stop after the animation pass) or by the runner's first pass (stop before it). The next frame renders ease(dt / 0.2) |
| Hitch | t is clamped to 1; a long frame writes the target and ends the blend |
| Switch latch | `Smooth Blending` is read once per blend, when Stop begins, and once per session for fades, when the session starts |

**Arming rules.** All must hold: the switch is on; the stop reason is Requested, NaturalEnd, PresenterFailure, or Interrupted while `player.isPlayerControlled` is true; the stop is not the library's own conflict suspend (policy InterruptExisting), which arms nothing although its public stop reason stays Interrupted; the animator restore succeeded; the body animator is not null and is active and enabled; the animator does not update in the physics step (an animator in physics update mode never arms); the animator is not culled (its culling mode is AlwaysAnimate or a source renderer is visible); the player is alive; the bone set is not empty; the pre-pass system is installed. PlayerDied, PlayerInvalidated, RoundUnloaded and Shutdown never arm. A Stop followed by a Start in the same call looks as without blending: the new session cancels the blend before any animation pass.

**Bone set.** The union of the skinned bones of the player model, both LOD models and the arms model, without null entries, the animator transform, the arms metarig, the local arms transform, the gameplay camera and the camera's ancestors. The local player blends the visible first-person arms and the shadow body; a remote body blends the whole body. The set is cached per player and rebuilt when a renderer reference or its shared mesh changes. A bone with a non-finite or near-zero-length value is skipped for that pass.

**Cancel.**

| Event | What happens |
| --- | --- |
| A live-body session starts on the player | Before the new session captures anything: with no pass run yet, R goes on every bone; otherwise driven channels get the vanilla value of the last pass and undriven channels get R. The new session sees a clean post-stop pose, never P0 |
| Player dead, player no longer controlled, round changed, animator null, disabled or culled, controller changed (vanilla death swap, another mod's swap) | Released: channels that still hold the blend's last write get R; channels another writer changed are left alone |
| Player or bone destroyed | Dropped; nothing written |
| Plugin shutdown | Every blend cancelled as for a new session; the registry is cleared and the runner disabled |

A DedicatedLocalViewmodel session that starts after a body stop does not cancel the blend, and the viewmodel presenter does not blend.

The runner is enabled when the first blend arms and disabled when the last one ends.

## Smooth Blending switch

`[Blending] Smooth Blending` (default true) turns on eased fades and the exit pose blend. With it off, nothing blending-related runs: Stop does its restore only, with no bone-set access, no P0 or R capture, no registry entry and a disabled runner. Fades, viewer changes and sprint yield use straight lines. Off does not change the schema-1 defaults, body.layers, the viewer rule, the yields or the post-animation callback. The former `Restore State Mode` entry is no longer bound and is removed from existing config files on first launch; the file is saved only when something was removed.

## Execution order

| Order | Component | Pass | Work |
| --- | --- | --- | --- |
| head | Exit pose blend pre-pass system | `PreLateUpdate`, ahead of `DirectorUpdateAnimationBegin` | Puts R back on channels still holding the blend's last write; the host installs it at attach and removes it at teardown |
| -32000 | Metarig root write-back runner | LateUpdate | Early metarig root write-back |
| -31000 | Exit pose blend runner | LateUpdate | Computes and writes every live blend after the animation pass; enabled only while a blend is live |
| 0 | `PlayerControllerB.LateUpdate` and its patches | LateUpdate | Vanilla layer weights, ground-follow, mantle, arms shift |
| 0 | Runtime host | LateUpdate | Coordinator tick (layer weights, auto-stops, Stop and arm), arms write-back, late metarig root write-back |
| 31000 | Post-animation callback runner | LateUpdate | Consumer callbacks; enabled only while a callback is set |
| 32000 | Camera stabilizers, restore diagnostics runner | LateUpdate | Camera and diagnostics |
| none | Visor glue | onBeforeRender | Visor follows the final camera pose |

The blend runs before every order-0 LateUpdate, so vanilla, held items and model replacements read the blended pose. The callbacks run after them, so a callback's bone writes are the last pose of the frame.

## Networking

Blending adds no network message. Each client blends its own copy of the animator at its own stop time, so observers start their blends at their own stops. On a remote body the blend moves from P0 into whatever vanilla's next state sync shows and ends on it.

## Schema

Schema 2 is strict and neutral. Bundle paths are confined to AssetRootPath, transform paths are canonical, defaults do not encode one consumer's offsets, and semantic options replace diagnostic exemptions.

Schema 1 is an internal compatibility input only. It normalizes to schema 2 for the 1.x line and emits a migration warning.

## Distribution

The production assembly has no consumer assembly or private tooling dependency. Restore/build/test use public packages. Release builds do not deploy unless explicitly opted in. The package contains only the runtime DLL and standard metadata.
