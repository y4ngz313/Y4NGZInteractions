# Architecture

Last updated: 2026-08-08

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

## Restoration

Live-body restoration captures controller, layers, parameters, transforms, camera/arms state, visor state, rig-builder state, prop state, and other scoped presentation state before mutation. Stop restores from those snapshots. Scoped transform restoration is automatic rather than manifest-controlled.

Verbose probes are operational diagnostics and default off. Essential restoration remains active.

## Schema

Schema 2 is strict and neutral. Bundle paths are confined to AssetRootPath, transform paths are canonical, defaults do not encode one consumer's offsets, and semantic options replace diagnostic exemptions.

Schema 1 is an internal compatibility input only. It normalizes to schema 2 for the 1.x line and emits a migration warning.

## Distribution

The production assembly has no consumer assembly or private tooling dependency. Restore/build/test use public packages. Release builds do not deploy unless explicitly opted in. The package contains only the runtime DLL and standard metadata.
