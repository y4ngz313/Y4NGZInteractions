# Manifest Reference

Last updated: 2026-11-10

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
    "stopOnGameplayCameraDisplacement": true,
    "stabilizeLocalCameraPosition": false,
    "localCameraOwnedExternally": false,
    "stopOnVanillaSpecialAnimation": true,
    "clipPack": { "enabled": false, "overrides": [] },
    "prop": { "enabled": false }
  }
}
~~~

preserveGameplayCamera, stopOnGameplayCameraDisplacement, and stopOnVanillaSpecialAnimation default to true. stabilizeLocalCameraPosition and localCameraOwnedExternally default to false. Scoped transform restoration is automatic.

The camera fields have separate meanings:

- preserveGameplayCamera keeps camera transform continuity across controller swaps, rebinds, and restoration. It does not pin the camera during normal play.
- stopOnGameplayCameraDisplacement enables the stance-aware safety guard and stops a session after sustained authored displacement.
- stabilizeLocalCameraPosition explicitly pins only the gameplay camera's player-local position for the session while leaving mouse-look rotation live. Use it only when the authored controller temporarily moves a camera parent.
- localCameraOwnedExternally declares that another system owns the local camera and visor. That explicit ownership declaration makes the presenter's first-person camera and visor behavior stand down; the other three field values are not treated as ownership signals.

## Layer fades and the exit pose blend

layerWeightRampSeconds (startLayerWeight to 1), enterLayerFadeSeconds (0 to the layer's weight), naturalEndLayerFadeSeconds and exitSeconds (each layer's last base weight, before the viewer and yield factors, to 0) default to 0, which is instant. With the `Smooth Blending` config entry on (the default), each fade follows a smoothstep curve, 3t² − 2t³. With it off, each fade is a straight line. The entry is read once per session, when the session starts.

With `Smooth Blending` on, a BodyWorld interaction that ends with stop reason Requested, NaturalEnd or PresenterFailure, or Interrupted while the player is controlled, also blends the player's skin bones from the session's last pose into the live vanilla pose over 0.2 s. The duration is fixed; there is no manifest field. The library's own conflict suspend, death, invalidation, round unload and shutdown stops do not blend, and neither does a body animator that updates in the physics step. A DedicatedLocalViewmodel does not blend. See [Architecture](ARCHITECTURE.md).

A shell controller should keep vanilla's six layers under its custom layers, with vanilla's names: Base Layer, EmotesNoArms, HoldingItemsRightHand, UpperBodyEmotes, HoldingItemsBothHands and SpecialAnimations. `body.layers` cannot name them, so entries there never write their weights; vanilla does. The yields and the live exit then show vanilla.

## Layers

body.layers (schema 2 only, optional) lists extra named controller layers beyond fullBodyLayer and firstPersonArmsLayer. Absent means those two layers only. Each entry:

~~~json
"layers": [
  { "name": "Hands", "viewers": ["localFirstPerson"], "weight": -1, "yieldTo": ["sprint", "handsOnWall"] }
]
~~~

| Field | Type | Default | Rule |
| --- | --- | --- | --- |
| name | string | required | Controller layer name; the first layer with that name is used. An entry whose name resolves to layer 0, or to a layer an earlier entry already drives, is skipped with one warning per session |
| viewers | string[] | all three | Any of localFirstPerson, localThirdPerson, remote. Present and empty, or null, is an error |
| weight | float | -1 | Exactly -1 follows the session ramp; 0 to 1 pins the base weight. Fades apply either way |
| yieldTo | string[] | none | Any of sprint, handsOnWall |

At most 8 entries. Names are unique across fullBodyLayer, firstPersonArmsLayer and layers. Those two fields keep their own rules and count as the first two layers, for all viewers and with no yield; they are always kept when the controller has them.

| Code | Rule |
| --- | --- |
| manifest_body_layer_count_invalid | More than 8 entries |
| manifest_body_layer_name_missing | Empty or whitespace name |
| manifest_body_layer_reserved | A vanilla layer name (the six above) |
| manifest_body_layer_duplicate | A name used twice across fullBodyLayer, firstPersonArmsLayer and layers |
| manifest_body_layer_viewers_empty | viewers is present and empty, or null |
| manifest_body_layer_viewer_invalid | Unknown viewer |
| manifest_body_layer_yield_invalid | Unknown yield |
| manifest_body_layer_weight_invalid | Not -1 and outside 0 to 1, or not finite |
| manifest_unknown_field | Unknown field in an entry, at `$.body.layers[i].<field>` |
| manifest_field_requires_schema_2 | body.layers in a schema-1 manifest |

**Viewer rule.** Evaluated once per session per frame. remote: the session's player is not the local player. localThirdPerson: the local player whose body model is drawn (enabled and not shadows-only). localFirstPerson: the local player otherwise. One skeleton holds one pose per frame, so the rule is per frame, not per camera. When the viewer changes, each layer's viewer factor moves to 0 or 1 over 0.1 s. At session start it begins at its target.

**Yield rule.** A layer's effective weight is base weight × viewer factor × (1 − max(sprint yield, wall yield)). The sprint yield rises to 1 over 0.1 s while the player sprints, and falls back when the sprint ends. On a remote body, sprinting comes from the replicated position change. The wall yield is vanilla's hands-on-wall weight (0 to 1). Lowering a layer shows what is under it: sprint shows the base layer's arms; hands-on-wall shows the shell's UpperBodyEmotes layer (a shell without it shows its base layer). A layer without yieldTo never yields. With `Smooth Blending` off, the factor changes are straight lines.

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
  "attachBonePath": "spine.003/shoulder.R/arm.R_upper/arm.R_lower/hand.R",
  "localPosition": { "x": 0, "y": 0, "z": 0 },
  "localEulerAngles": { "x": 0, "y": 0, "z": 0 },
  "localScale": 1,
  "releaseSeconds": 0,
  "localBodyCopy": false
}
~~~

The attachment path is relative to the arms metarig used by the presenter.

localBodyCopy (default false) also attaches, for the local player only, a copy named `Y4NGZ_<prefabAssetName>_BodyInstance` to the same bone on the full body; cameras that render the local body draw the copy instead of the first-person prop, except the local player's own gameplay camera, which always keeps the first-person prop, even when a mod renders the body there (for example a mod that shows the body in first person or moves the gameplay camera for a third-person view). On the copy, joints, rigidbodies and colliders are destroyed and lights and audio sources are disabled; everything else on the prefab (MonoBehaviour scripts, Animators, particle systems, other components) stays active and runs on the copy, so keep scripts with side effects (network calls, sounds, gameplay state, singletons) off a prefab that opts in. See [Advanced Prop Recipe](ADVANCED_PROP_RECIPE.md).

## Path rules

Bundle paths must be trimmed, relative, slash-separated, and confined to AssetRootPath. Rooted paths, backslashes, empty segments, dot segments, and parent traversal are rejected where applicable. Transform paths must be prefab-relative, use forward slashes, and contain no leading slash, trailing slash, duplicate slash, dot, or parent segment.

## Schema-1 compatibility

Schema-1 JSON is parsed through an internal compatibility DTO, normalized to schema 2, and accepted for the 1.x line with warning manifest_schema_1_migrated. Legacy-only fields never appear on the public schema-2 DTO.

Migration maps legacy stabilizeLocalCameraPosition and localCameraOwnedExternally directly, maps exemptFromCameraDisplacementGuard to the inverse of stopOnGameplayCameraDisplacement, and preserves camera seams unless the legacy manifest explicitly declared external camera ownership. A schema-1 consumer that omitted stabilization does not acquire a position pin.

When a schema-1 manifest omits a key, the key takes its documented default (for example schemaVersion, cameraAnchor, cameraLocalPosition, localScale, startLayerWeight, fullBodyLayerWeight, suppressRigBuilders and prop localScale), not the zero value of its type. A schema-1 manifest that omits suppressRigBuilders therefore has suppressRigBuilders true, which sets rebuildRigBuilders to false. body.layers is schema 2 only; a schema-1 manifest with body.layers is rejected with manifest_field_requires_schema_2.

Schema 2 removes frameRate, root displayName, localViewmodel.root, runtimeMaterialMode, sockets, body.clip, diagnostic override fields, validation metadata, and low-level restoration toggles. See [1.0 Migration Guide](MIGRATION_1_0.md).
