# 1.0 Migration Guide

Last updated: 2026-08-08

1.0 is a deliberate pre-1.0 contract cleanup. Downstream mods must rebuild against the final assembly.

## C# changes

- Remove Hybrid and choose BodyWorld or DedicatedLocalViewmodel.
- Remove OwnerModId; PackId is the session-owner identity.
- Set ConflictPolicy only when InterruptExisting is intended. The default is RejectIfBusy.
- Replace player-wide active/stop calls with TryGetActiveInteraction(player, presentationKind, out handle) and handle-scoped stopping.
- Use InteractionEnded for exactly-once post-restoration completion.
- Use TrySetInteractionFloat for Float parameters.
- Remove pack DisplayName and Author.
- Remove interaction DisplayName and ExpectedDurationSeconds.
- Supply PackId, Version, AssetRootPath, and at least one interaction.
- Treat validation reports as immutable and display Code, JsonPath, Message, and Severity.

## Manifest changes

Use schemaVersion 2 for new content.

Rename:

- localViewmodel.prefab to prefabAssetName
- localViewmodel.controller to controllerAssetName
- localViewmodel.cameraAnchor to cameraAnchorPath
- body controller fields to controllerAssetName
- prop.prefabName to prefabAssetName
- prop.attachBone to attachBonePath
- suppressRigBuilders to rebuildRigBuilders with inverted meaning
- diagnostic camera/special exemptions to preserveGameplayCamera and stopOnVanillaSpecialAnimation

Replace renderer hint arrays with hideVanillaFirstPersonArms, prefabRenderersToHide, and prefabRenderersToShow.

Remove frameRate, root displayName, localViewmodel.root, runtimeMaterialMode, sockets, body.clip, diagnostic override fields, validation metadata, and low-level restore toggles.

durationSeconds in the manifest is the sole natural-duration source. Camera position/rotation defaults are zero and scale defaults to one.

## Compatibility window

Schema-1 JSON remains accepted through an internal migration DTO for the 1.x line. A successful migration includes warning manifest_schema_1_migrated. The new public DTO exposes only schema 2.

## Networking

No networking API was added. Every observing client must invoke the local API after resolving the target player through consumer-owned networking. Never send a local handle across the network.

## Verification

After rebuilding a consumer:

1. validate and register every pack;
2. run both presentation kinds used by the consumer;
3. test rejection and interruption;
4. test every parameter type used;
5. test explicit stop, natural end, exit, death, ladder/special animation, and round unload;
6. test one and two players;
7. repeat cycles and inspect camera, arms, visor, rig, animator, and prop restoration.
