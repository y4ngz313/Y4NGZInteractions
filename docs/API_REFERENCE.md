# API Reference

Last updated: 2026-08-08

Namespace: Y4NGZInteractions.InteractionAnimationApi

Y4NGZInteractions is a local presentation, ownership, and restoration runtime. It does not replicate requests and does not retarget clips at runtime.

## Registration

InteractionAnimationPackDefinition requires:

- PackId: stable pack identifier and canonical session-owner identity.
- Version: consumer pack version.
- AssetRootPath: existing directory that confines every bundle path.
- Interactions: one or more definitions.

Each InteractionAnimationDefinition requires InteractionId, PresentationKind, and ManifestJson. Registration validates every manifest, parses schema 1 through migration when needed, and snapshots caller-owned input. Later mutations to the caller's pack, array, definitions, or JSON strings do not alter the registered pack.

~~~csharp
bool TryRegisterInteractionPack(
    InteractionAnimationPackDefinition pack,
    out string reason);
~~~

A pack id may be registered once per plugin lifetime. Failed validation returns the first error code in reason. Use ValidateInteractionPack to inspect the complete immutable report.

## Validation

~~~csharp
InteractionAnimationValidationReport ValidateInteractionPack(
    InteractionAnimationPackDefinition pack);

InteractionAnimationValidationReport ValidateInteractionManifest(
    string manifestJson,
    string expectedInteractionId,
    InteractionAnimationPresentationKind presentationKind);
~~~

Each issue has Code, JsonPath, Message, and Severity. IsValid is false when at least one issue has Error severity. Schema-1 migration is a Warning.

## Starting and conflicts

~~~csharp
bool TryStartInteraction(
    InteractionAnimationRequest request,
    out InteractionAnimationHandle handle,
    out string reason);
~~~

The request contains Player, PackId, InteractionId, and ConflictPolicy. RejectIfBusy is the default and leaves the incumbent untouched. InterruptExisting is transactional:

1. resolve and validate registration;
2. check target/player rules and external body-controller ownership;
3. preflight manifest, paths, bundle, prefab, controller, anchor, and presenter;
4. suspend and restore conflicting sessions;
5. acquire leases and start the replacement;
6. if start fails, reacquire and resume the incumbent;
7. if start succeeds, end incumbents with Interrupted.

DedicatedLocalViewmodel is local-player-only. BodyWorld on the local player claims both the body animator and camera/arms resource. BodyWorld on a remote player claims only that player's body animator, so sessions on different remote players may coexist.

## Preloading

~~~csharp
bool TryPreloadInteractionAssets(
    string packId,
    string interactionId,
    out string reason);
~~~

Preloading validates registered paths and begins or completes bundle loading without starting a session. Start still performs full preflight.

## Queries and stopping

~~~csharp
bool IsInteractionActive(InteractionAnimationHandle handle);

bool TryGetActiveInteraction(
    PlayerControllerB player,
    InteractionAnimationPresentationKind presentationKind,
    out InteractionAnimationHandle handle);

bool TryStopInteraction(
    InteractionAnimationHandle handle,
    InteractionAnimationStopReason stopReason);
~~~

Queries are presentation-specific. Stopping is always handle-scoped.

Stop reasons are Requested, NaturalEnd, Interrupted, PlayerInvalidated, PlayerDied, RoundUnloaded, PresenterFailure, and Shutdown.

## Graceful exit

~~~csharp
bool TryBeginInteractionExit(
    InteractionAnimationHandle handle,
    out string reason);
~~~

The presenter fires the manifest exit trigger and schedules restoration after exitSeconds. Zero seconds ends on the next lifecycle tick.

## Parameters

~~~csharp
bool TrySetInteractionBool(handle, parameterName, value);
bool TrySetInteractionInt(handle, parameterName, value);
bool TrySetInteractionFloat(handle, parameterName, value);
bool TryFireInteractionTrigger(handle, parameterName);
~~~

Parameter calls apply only to an active handle and return false when the presenter cannot find a compatible parameter.

## Completion event

~~~csharp
event EventHandler<InteractionAnimationEndedEventArgs> InteractionEnded;
~~~

Event data is immutable: Handle, Player, PackId, InteractionId, PresentationKind, and StopReason. It fires exactly once after restoration and lease release. One subscriber exception is isolated from other subscribers.

## External animator ownership

BodyWorld refuses to replace a controller it does not recognize as the expected vanilla controller or an incumbent session owned by this API. A consumer or another mod that directly owns the body animator must release it before starting BodyWorld.
