# Manifest Reference

Last updated: 2026-08-08

New content uses strict schema version 2. Field names are exact and case-sensitive; unknown or duplicate fields are errors. Every validation issue identifies a stable code and JSON path.

## Root

| Field | Required | Meaning |
| --- | --- | --- |
| schemaVersion | yes | Must be 2 for new content |
| interactionId | yes | Must match the registered interaction id |
| durationSeconds | yes | Natural lifetime; zero disables timed completion |
| bundleInternalName | no | Optional bundle cache identity |
| localViewmodel | for DedicatedLocalViewmodel | Camera-local payload |
| body | for BodyWorld | Player-body payload |

Vectors are JSON objects with x, y, and z numbers.

## DedicatedLocalViewmodel

~~~json
{
  "schemaVersion": 2,
  "interactionId": "inspect",
  "durationSeconds": 2.0,
  "localViewmodel": {
    "bundleFileName": "viewmodel/example_viewmodel",
    "prefabAssetName": "ExampleViewmodel",
    "controllerAssetName": "ExampleViewmodelController",
    "activeBool": "InteractionActive",
    "enterTrigger": "Enter",
    "exitTrigger": "Exit",
    "exitSeconds": 0.2,
    "cameraAnchorPath": "Rig/CameraAnchor",
    "cameraLocalPosition": { "x": 0, "y": 0, "z": 0 },
    "cameraLocalEuler": { "x": 0, "y": 0, "z": 0 },
    "localScale": { "x": 1, "y": 1, "z": 1 },
    "hideVanillaFirstPersonArms": true,
    "prefabRenderersToHide": [],
    "prefabRenderersToShow": ["Rig/Prop"]
  }
}
~~~

Camera position and rotation default to zero. Scale defaults to one. cameraAnchorPath and renderer paths are prefab-relative canonical transform paths.

## BodyWorld

~~~json
{
  "schemaVersion": 2,
  "interactionId": "wave",
  "durationSeconds": 2.0,
  "body": {
    "enabled": true,
    "bundleFileName": "body/example_body",
    "controllerAssetName": "ExampleBodyController",
    "activeBool": "InteractionActive",
    "enterTrigger": "Enter",
    "exitTrigger": "Exit",
    "fullBodyLayer": "FullBody",
    "firstPersonArmsLayer": "FirstPersonArms",
    "startLayerWeight": 1,
    "layerWeightRampSeconds": 0.1,
    "fullBodyLayerWeight": -1,
    "enterLayerFadeSeconds": 0.1,
    "naturalEndLayerFadeSeconds": 0.1,
    "rebuildRigBuilders": false,
    "exitSeconds": 0.2,
    "movementParameter": "MovementState",
    "preserveGameplayCamera": true,
    "stopOnVanillaSpecialAnimation": true,
    "clipPack": { "enabled": false, "overrides": [] },
    "prop": { "enabled": false }
  }
}
~~~

preserveGameplayCamera and stopOnVanillaSpecialAnimation default to true. Scoped transform restoration is automatic.

## Clip packs

An enabled clip pack requires a confined bundleFileName and at least one unique slot-to-clip mapping.

~~~json
"clipPack": {
  "enabled": true,
  "bundleFileName": "body/example_clips",
  "bundleInternalName": "example_clips",
  "overrides": [
    { "slot": "Interaction_Main", "clip": "Example_Wave" }
  ]
}
~~~

## Props

~~~json
"prop": {
  "enabled": true,
  "prefabAssetName": "ExampleProp",
  "attachBonePath": "spine.004/shoulder.R/arm.R/forearm.R/hand.R",
  "localPosition": { "x": 0, "y": 0, "z": 0 },
  "localEulerAngles": { "x": 0, "y": 0, "z": 0 },
  "localScale": 1,
  "releaseSeconds": 0
}
~~~

The attachment path is relative to the arms metarig used by the presenter.

## Path rules

Bundle paths must be trimmed, relative, slash-separated, and confined to AssetRootPath. Rooted paths, backslashes, empty segments, dot segments, and parent traversal are rejected where applicable. Transform paths must be prefab-relative, use forward slashes, and contain no leading slash, trailing slash, duplicate slash, dot, or parent segment.

## Schema-1 compatibility

Schema-1 JSON is parsed through an internal compatibility DTO, normalized to schema 2, and accepted for the 1.x line with warning manifest_schema_1_migrated. Legacy-only fields never appear on the public schema-2 DTO.

Schema 2 removes frameRate, root displayName, localViewmodel.root, runtimeMaterialMode, sockets, body.clip, diagnostic override fields, validation metadata, and low-level restoration toggles. See [1.0 Migration Guide](MIGRATION_1_0.md).
