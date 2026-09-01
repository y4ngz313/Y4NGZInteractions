# Architecture

Last updated: 2026-08-31

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

Verbose probes are operational diagnostics and default off. Essential restoration remains active.

## Schema

Schema 2 is strict and neutral. Bundle paths are confined to AssetRootPath, transform paths are canonical, defaults do not encode one consumer's offsets, and semantic options replace diagnostic exemptions.

Schema 1 is an internal compatibility input only. It normalizes to schema 2 for the 1.x line and emits a migration warning.

## Distribution

The production assembly has no consumer assembly or private tooling dependency. Restore/build/test use public packages. Release builds do not deploy unless explicitly opted in. The package contains only the runtime DLL and standard metadata.
