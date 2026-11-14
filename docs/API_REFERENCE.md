# API Reference

Last updated: 2026-11-14

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

TryStopInteraction ends the session, restores the animator and releases the body before it returns. With the `Smooth Blending` config entry on (the default) and a stop reason of Requested, NaturalEnd or PresenterFailure, the player's skin bones then keep the session's last pose and move to the live vanilla pose over the next 0.2 s. Bone reads in that window see the blended pose. A consumer that writes a bone every frame wins: the blend takes the written value as its target. InteractionEnded handlers run with the session's last pose already on the rig. A stop from inside a post-animation callback behaves the same way. A body animator that updates in the physics step never blends. With the entry off, the rig holds the restored vanilla pose when the call returns.

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

## Post-animation callback

~~~csharp
bool TrySetInteractionPostAnimationCallback(
    InteractionAnimationHandle handle,
    Action<InteractionAnimationFrame> callback);

readonly struct InteractionAnimationFrame
{
    InteractionAnimationHandle Handle { get; }
    PlayerControllerB Player { get; }
    InteractionAnimationViewer Viewer { get; }
    float Weight { get; }
    float DeltaTime { get; }
}

enum InteractionAnimationViewer { LocalFirstPerson = 0, LocalThirdPerson = 1, Remote = 2 }
~~~

The call returns false when the handle is not an active BodyWorld session. A null callback clears it. Weight is the session's eased base weight (0 to 1) before the viewer and yield factors. Viewer uses the viewer rule in the [Manifest Reference](MANIFEST_REFERENCE.md).

The callback runs once per active session per frame, local and remote alike, in session start order. It runs in LateUpdate at script execution order 31000: after vanilla `PlayerControllerB.LateUpdate` and its patches, and after the runtime's own tick, before the camera stabilizers and the visor glue. A session stopped earlier in the frame gets no call.

Inside the callback you may:

- write that player's bone and IK target transforms for this frame;
- read the animator;
- call TrySetInteraction* and TryFireInteractionTrigger;
- call TryStopInteraction or TryBeginInteractionExit on the session's own handle.

Inside the callback you may not:

- assign the controller, call Rebind or Animator.Update;
- write the weight of a layer the session owns;
- write the camera, visor, arms root or metarig root;
- expect a bone write to survive to the next frame, because the animator overwrites it.

A callback that throws is caught, logged once with the handle and the exception, and cleared for that session. The session and every other callback keep running.

## Completion event

~~~csharp
event EventHandler<InteractionAnimationEndedEventArgs> InteractionEnded;
~~~

Event data is immutable: Handle, Player, PackId, InteractionId, PresentationKind, and StopReason. It fires exactly once after restoration and lease release. One subscriber exception is isolated from other subscribers.

## External animator ownership

BodyWorld refuses to replace a controller it does not recognize as the expected vanilla controller or an incumbent session owned by this API. A consumer or another mod that directly owns the body animator must release it before starting BodyWorld.

## BodyWorld camera and playback semantics

preserveGameplayCamera preserves camera continuity at controller seams; it does not freeze player-local position. stopOnGameplayCameraDisplacement controls the stance-aware safety guard. stabilizeLocalCameraPosition is the explicit opt-in session position pin. localCameraOwnedExternally is the explicit camera/visor ownership declaration. None of preservation, guarding, or position pinning is treated as an ownership signal.

For BodyWorld manifests with a movementParameter, value 2 is the generic sprint state. While that state is active, the presenter emits low-noise live_body.playback_rate_sample and live_body.playback_rate_summary records. They compare Animator normalized-time progression with a monotonic wall clock and include state length, configured Animator/state speed, completed cycles, and effective clip-seconds per wall-clock second.
