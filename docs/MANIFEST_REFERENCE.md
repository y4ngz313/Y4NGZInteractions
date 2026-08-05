# Manifest reference

Last updated: 2026-08-05

Complete schema for the interaction manifest JSON that a consumer passes as
`InteractionAnimationDefinition.ManifestJson`. Generated from
`src/Y4NGZInteractions/InteractionAnimationApi/Authoring/InteractionAnimationManifest.cs` and the
two presenters.

## How the manifest is parsed

The manifest is deserialised with Unity's `JsonUtility`, which imposes hard constraints:

- **Field names are matched exactly**, case-sensitive, in the camelCase spelling shown below.
  A misspelled field is not an error — it is silently ignored, and the field keeps its default.
- **No comments and no trailing commas.** JSON only. (`jsonc` examples in this document are for
  reading; strip the comments before shipping.)
- **Unknown fields are ignored.** You can keep authoring metadata in the file safely.
- **`Vector3` is an object** with `x`, `y`, `z` float members: `{"x": 0, "y": -0.42, "z": 0.95}`.
  A missing component defaults to `0`, not to the field's C# default.
- **Absent objects and fields keep their C# defaults.** `JsonUtility` constructs the object
  first, so field initialisers apply. This matters most for `body.suppressRigBuilders`, which
  defaults to **`true`** — a live-body manifest that omits it suppresses rig builders.

Validation runs twice against the same rules: once at `TryPreloadInteractionAssets` (if you
preload) and always at `TryStartInteraction`. Failures come back as snake_case `reason` strings,
listed per field below.

## Root object

| Field | Type | Required | Default | Runtime effect | Invalid → reason |
|---|---|---|---|---|---|
| `schemaVersion` | int | yes | `1` | Must be greater than zero. No behaviour is keyed off the value yet; it exists so future formats can be told apart. | `manifest_schema_version_invalid` |
| `interactionId` | string | yes | `""` | Must be non-empty and must equal the `InteractionId` of the registered definition (case-insensitive). Appears in every log line and is the key the config exemption lists match against. | `manifest_interaction_id_empty`, `manifest_interaction_id_mismatch` |
| `displayName` | string | no | `""` | Informational only. Neither presenter reads it; the debug probe copies it into the definition it builds. | — |
| `durationSeconds` | float | no | `0` | `0` = indefinite (end it yourself). Greater than zero = the coordinator auto-stops the session with `NaturalEnd` after that many seconds of wall-clock time. Must be finite and non-negative. Overridden by a non-zero `ExpectedDurationSeconds` on the definition — see [Duration precedence](#duration-precedence). | `manifest_duration_invalid` |
| `frameRate` | float | no | `0` | Authoring metadata. Unused at runtime. | — |
| `bundleInternalName` | string | no | `""` | The *internal* name of the AssetBundle (the name Unity baked into it), not a file name. When set, the presenter first scans already-loaded bundles for one with this name and reuses it instead of loading from disk. Leave empty and the bundle is always loaded from `bundleFileName`. Applies to the `body` bundle and to the `localViewmodel` bundle. | — |
| `localViewmodel` | object | for viewmodel kinds | see below | [Viewmodel section](#localviewmodel). | `manifest_viewmodel_missing` |
| `sockets` | object | for viewmodel kinds | see below | [Sockets section](#sockets). | `manifest_sockets_missing` |
| `liveRenderersToHide` | string[] | no | `[]` | Viewmodel only. Recognised entries are `thisPlayerModelArms` and `lc_first_person_hands` (either one, matched case-insensitively); if present, the player's live first-person arms renderer is disabled while the viewmodel is up and restored on stop. Any other value is ignored. | — |
| `body` | object | for `BodyWorld` | see below | [Body section](#body). | `manifest_body_disabled` |
| `validation` | object | no | empty | Authoring provenance (`generatedAt`, `previewPixelCoverage`, `meshTransfer`, `socketNames`, `cameraBounds`). All strings, all unused at runtime — they exist so a baked manifest records how it was verified. | — |
| `exemptFromCameraDisplacementGuard` | bool | no | `false` | See [Guard exemptions](#guard-exemptions). | — |
| `exemptFromSpecialAnimationAutoStop` | bool | no | `false` | See [Guard exemptions](#guard-exemptions). | — |

### Duration precedence

1. `InteractionAnimationDefinition.ExpectedDurationSeconds` if it is greater than zero.
2. Otherwise the manifest's `durationSeconds`.
3. If both are zero, the session runs until you end it (`TryBeginInteractionExit` or
   `TryStopInteraction`) or until it is interrupted.

A graceful exit is a separate timer: `TryBeginInteractionExit` schedules the stop for
`exitSeconds` from now regardless of the duration above.

## `body`

Required (and `enabled` must be `true`) for `PresentationKind.BodyWorld`. This is the path that
swaps the controller on `playerBodyAnimator`, so it is visible in first person *and* to other
clients in third person.

### Controller and bundle

| Field | Type | Required | Default | Runtime effect | Invalid → reason |
|---|---|---|---|---|---|
| `enabled` | bool | yes | `false` | Must be `true`, otherwise the presentation kind has nothing to play. | `manifest_body_disabled` |
| `bundleFileName` | string | yes | `""` | File name of the AssetBundle holding the shell controller, resolved relative to the pack's `AssetRootPath`. Skipped entirely if `bundleInternalName` matched an already-loaded bundle. | `manifest_body_bundle_file_empty`, then at start `live_body.bundle_missing`, `live_body.bundle_load_failed`, `asset_bundle_path_escapes_root` |
| `controller` | string | yes | `""` | Asset name of the `RuntimeAnimatorController` inside the bundle. | `manifest_body_controller_empty`, then `live_body.controller_missing` |
| `controllerAssetName` | string | no | `""` | Alternative asset name, **tried first**. If it loads nothing, `controller` is tried. Unity bundles sometimes address a controller as `Name.controller` and sometimes as `Name`; put the suffixed form here and the bare form in `controller` and either layout works. | — |
| `clip` | string | no | `""` | Legacy field. Not read by the runtime. | — |

### Lifecycle parameters

All parameter names are optional and are applied only if the applied controller actually has a
parameter of that exact name **and** the matching type; otherwise they are silently skipped.

| Field | Type | Default | Runtime effect |
|---|---|---|---|
| `activeBool` | string | `""` | Animator `Bool`. Set `true` immediately after the controller is applied, and `false` at exit and again at restore. |
| `enterTrigger` | string | `""` | Animator `Trigger`. Reset then fired right after the controller is applied. |
| `exitTrigger` | string | `""` | Animator `Trigger`. Fired by `TryBeginInteractionExit` and again at restore. |
| `exitSeconds` | float | `0` | Seconds the put-away animation needs after `exitTrigger` before the session stops and the vanilla controller is restored. `0` restores immediately. Also the window over which the custom layers fade to zero. |
| `movementParameter` | string | `""` | Animator `Int` written whenever the value changes: `0` idle, `1` walking, `2` sprinting. Derived from the session player's `isSprinting` and the horizontal magnitude of `thisController.velocity` (threshold 0.2 m/s). Not driven once an exit has begun. Empty disables it. |

### Layers and weights

| Field | Type | Default | Runtime effect |
|---|---|---|---|
| `fullBodyLayer` | string | `""` | Layer name (exact, case-sensitive) of the full-body layer in your controller. Resolved to an index at start; an unknown name simply leaves that layer undriven. |
| `firstPersonArmsLayer` | string | `""` | Layer name of the first-person arms layer, same resolution rules. |
| `startLayerWeight` | float | `1` | Weight both layers start at. Values `<= 0` are treated as `1`; the result is clamped to 0–1. |
| `layerWeightRampSeconds` | float | `0` | Seconds to lerp from `startLayerWeight` to `1`. `0` means "weight 1 immediately". |
| `fullBodyLayerWeight` | float | `-1` | Fixed weight for the full-body layer, overriding the ramp. Negative (the default, and the state of older manifests) keeps the legacy behaviour of ramping both layers together. `0` is a meaningful value: it lets vanilla's own body animation continue while only the first-person arms layer is authored, which avoids the walk-skew and frozen-torso artefacts a high-weight constant body pose causes. |
| `enterLayerFadeSeconds` | float | `0` | Multiplies both layer weights by a 0→1 ramp over this many seconds from session start. Use for one-shots whose first frames would otherwise pop through the camera near plane. `0` disables. |
| `naturalEndLayerFadeSeconds` | float | `0` | Multiplies both layer weights by a 1→0 ramp over the last seconds before a fixed-duration stop. Requires `durationSeconds > 0`, and is skipped once a graceful exit has begun. `0` disables. |

During a graceful exit both weights lerp from their value at `BeginExit` down to zero over
`exitSeconds`, which replaces all of the above.

### Rig and restore behaviour

| Field | Type | Default | Runtime effect |
|---|---|---|---|
| `suppressRigBuilders` | bool | **`true`** | `true` disables the player's `RigBuilder` components for the session instead of rebuilding them. `false` rebuilds every `RigBuilder` after the controller swap and evaluates them in the same frame — required if your clips drive IK targets through the Animation Rigging graph. **Set this explicitly.** An omitted field means `true`. |
| `scopedFirstPersonTransformRestore` | bool | `false` | Opt-in teardown for first-person-only interactions: captures the local arms-metarig descendants before the swap, restores them before the rig rebuild, and skips the broad whole-player `Animator.Rebind()`. |
| `stabilizeLocalCameraPosition` | bool | `false` | Opt-in for short local interactions whose temporary controller animates a parent of the gameplay camera: preserves mouse-look rotation while pinning the camera's player-local position. |
| `localCameraOwnedExternally` | bool | `false` | Declares that this session *is* the third-person presentation and some other system (a camera mod) owns the local camera. Every camera behaviour in the presenter — the displacement guard, both stabilizers, the session-entry drift heal, the stop-time snaps, visor restore and visor glue — stands down for this session, because all of them exist to hold a first-person camera at rest and would otherwise fight the external owner. |
| `diagnosticVanillaOverrideClip` | string | `""` | Diagnostic only. Replaces the shell controller's slot clips with a clip borrowed from the vanilla controller, to prove the runtime path independently of retarget quality. Requires `overrideSlotPrefix`. Applied only when no clip pack is active. |
| `overrideSlotPrefix` | string | `""` | The prefix used to pick which shell clips `diagnosticVanillaOverrideClip` replaces. **It is only read by that diagnostic** — it does not validate or filter `clipPack.overrides`. Conventionally it is the common prefix of your slot names (for example `Lantern_FirstPersonArms_`), which keeps the diagnostic and the clip pack in agreement. |

### `body.clipPack`

The generic playback path: the shell controller stays fixed and the clips come from a separate
bundle through an `AnimatorOverrideController`. New animation means a new clip pack, never a new
controller.

| Field | Type | Required | Default | Runtime effect | Invalid → reason |
|---|---|---|---|---|---|
| `enabled` | bool | no | `false` | `false` plays the shell controller's own clips unchanged. | — |
| `bundleFileName` | string | when enabled | `""` | Clip-pack bundle, resolved like the body bundle. | `live_body.clip_pack_invalid_manifest`, `live_body.clip_pack_bundle_missing`, `live_body.clip_pack_bundle_load_failed` |
| `bundleInternalName` | string | no | `""` | Internal bundle name used to reuse an already-loaded clip pack, exactly like the root `bundleInternalName`. Note this is the clip pack's own field — the root one does not cover it. | — |
| `overrides` | array | when enabled | `[]` | At least one entry is required. | `live_body.clip_pack_invalid_manifest` (empty array), `live_body.clip_pack_no_overrides_applied` (every entry blank) |
| `overrides[].slot` | string | yes | `""` | **The asset name of a clip inside the shell controller** — the placeholder the state machine references. This is an `AnimatorOverrideController` key, so it must match a clip the controller actually uses; a name that matches nothing is accepted by Unity and simply does nothing. Entries with an empty `slot` or `clip` are skipped. | — |
| `overrides[].clip` | string | yes | `""` | Asset name of the replacement `AnimationClip` in the clip-pack bundle. A name that is not in the bundle fails the start closed. | `live_body.clip_pack_clip_missing:<clip>` |

Naming convention that keeps this readable: give every slot in one controller a shared prefix
(`Lantern_FirstPersonArms_Hold`, `..._Raise`, `..._Lower`) and give the clips a distinct prefix
of their own (`IKT_Lantern_Hold`). Then a slot name is never mistaken for a clip name in a log.

### `body.prop`

A rigid prop parented to a hand bone for the duration of the interaction.

| Field | Type | Required | Default | Runtime effect |
|---|---|---|---|---|
| `enabled` | bool | no | `false` | Off by default. |
| `prefabName` | string | when enabled | `""` | Asset name of a `GameObject` prefab. Loaded from the **clip-pack bundle** when one is active, otherwise from the body bundle. A missing prefab logs `live_body.prop_prefab_missing` and the session continues without the prop — it does not fail the start. |
| `attachBone` | string | when enabled | `""` | Bone name, searched **recursively by bare name** (not by path) under the local player's arms metarig, or under the body animator's transform for a remote player. Lethal Company's hand bones are `hand.L` and `hand.R`. A name that matches nothing logs `live_body.prop_attach_bone_missing` and the session continues without the prop. |
| `localPosition` | Vector3 | no | `(0,0,0)` | Local position under the attach bone. |
| `localEulerAngles` | Vector3 | no | `(0,0,0)` | Local rotation under the attach bone. |
| `localScale` | float | no | `1` | Uniform scale. Values `<= 0` are treated as `1`. |
| `releaseSeconds` | float | no | `0` | `<= 0` keeps the prop for the whole session (destroyed at stop). A positive value destroys the instance once the session has run that long — for throw or hand-off animations. |

The instance is renamed `Y4NGZ_<prefabName>_Instance` and its whole hierarchy is moved to the
attach bone's layer, so a first-person prop renders on the first-person layer automatically.

## `localViewmodel`

Required for `PresentationKind.DedicatedLocalViewmodel` and `Hybrid`. The prefab is instantiated
as a child of the local gameplay camera; nothing about it is visible to other players.

| Field | Type | Required | Default | Runtime effect | Invalid → reason |
|---|---|---|---|---|---|
| `bundleFileName` | string | **yes** | `""` | AssetBundle holding the prefab and controller, resolved relative to `AssetRootPath`. No default any more. Bundles larger than 16 MB cannot be loaded synchronously: the start returns `viewmodel.bundle_preload_started` and kicks off an async load you retry after. | `manifest_viewmodel_bundle_file_empty`, then `viewmodel.bundle_missing`, `viewmodel.bundle_load_failed`, `viewmodel.bundle_preload_in_progress` |
| `prefab` | string | **yes** | `""` | Asset name of the viewmodel prefab. No default any more. | `manifest_viewmodel_prefab_empty`, then `viewmodel.prefab_missing` |
| `controller` | string | yes | `""` | Asset name of the `RuntimeAnimatorController` applied to the instantiated viewmodel. If the prefab has no `Animator`, one is added. | `manifest_viewmodel_controller_empty`, then `viewmodel.controller_missing` |
| `cameraAnchor` | string | yes | `"Y4NGZ_ViewmodelCameraAnchor"` | Name of a child transform in the prefab that marks where the camera should sit relative to the model. Searched recursively by exact name. The root is then positioned and rotated so the anchor lands at `cameraLocalPosition` / `cameraLocalEuler` in camera space. Rename it to something of your own; the default is only a legacy value. | `manifest_viewmodel_camera_anchor_empty`, then `viewmodel.camera_anchor_missing:<name>` |
| `cameraLocalPosition` | Vector3 | no | `(0, -0.42, 0.95)` | Where the anchor ends up, in camera-local space. |
| `cameraLocalEuler` | Vector3 | no | `(0,0,0)` | Anchor orientation in camera-local space. |
| `localScale` | Vector3 | no | `(0.55, 0.55, 0.55)` | Root scale. An all-zero value is treated as `(1,1,1)`. |
| `activeBool` | string | no | `""` | Animator `Bool`, set `true` at start and `false` at exit. Applied only if the controller has it. |
| `enterTrigger` | string | no | `""` | Animator `Trigger` fired at start. |
| `exitTrigger` | string | no | `""` | Animator `Trigger` fired by `TryBeginInteractionExit`. |
| `exitSeconds` | float | no | `0` | Seconds to wait after the exit trigger before the session stops. |
| `hideSourceRenderers` | string[] | no | `[]` | Renderer names inside the prefab to disable on instantiation (exact, case-insensitive). Use it to strip source-scene geometry baked into the prefab. |
| `visibleRenderers` | string[] | no | `[]` | Renderer names to force enabled, applied **after** `hideSourceRenderers`, so it wins for a name in both lists. |
| `runtimeMaterialMode` | string | no | `""` | Only the value `safeGenerated` does anything: it replaces the materials on the prop renderer and on every renderer in `visibleRenderers` with generated unlit flat-colour materials. It is a bring-up aid for verifying geometry and placement when the authored materials do not survive bundling — not a shipping look. |
| `root` | string | no | `""` | Legacy field. Not read by the runtime. |

## `sockets`

Only validated (and only used) for the viewmodel presentation kinds.

| Field | Type | Required | Default | Runtime effect | Invalid → reason |
|---|---|---|---|---|---|
| `leftHand` | string | yes | `""` | Must be non-empty. The value itself is authoring metadata — nothing at runtime reads it. | `manifest_socket_left_hand_empty` |
| `rightHand` | string | yes | `""` | Same. | `manifest_socket_right_hand_empty` |
| `prop` | string | yes¹ | `""` | Renderer name of the held prop carried by the viewmodel prefab. Used to identify that renderer for `runtimeMaterialMode`, and logged at start. | `manifest_socket_prop_empty` |
| `tablet` | string | no | `""` | **Deprecated alias for `prop`**, kept so manifests written against the original schema keep loading. The runtime reads `prop` when it is set and falls back to `tablet` otherwise. Do not use it in new manifests. | — |

¹ Exactly one of `prop` or `tablet` must be non-empty; validation checks the resolved value.

## Guard exemptions

Two safety behaviours protect a live-body session from ending up in a broken state. Both are
correct for almost every interaction, and both can be opted out of when an interaction
legitimately violates their assumption.

**`exemptFromCameraDisplacementGuard`.** The camera displacement guard captures the gameplay
camera's player-local position when the session starts and stops the session if the camera later
moves more than 1.25 m away from that reference *and* ends up outside the same envelope around
vanilla's rest position. It catches an authored clip dragging the viewpoint somewhere the player
cannot recover from. Declare the exemption when your interaction is *supposed* to move the
camera that far — a mounted or seated pose, a scripted camera move. An exempt session logs the
over-threshold measurement once at info level and continues instead of stopping.

**`exemptFromSpecialAnimationAutoStop`.** A live-body session normally stops itself as soon as
`PlayerControllerB.inSpecialInteractAnimation` is true, on the assumption that vanilla has taken
over the player. Declare the exemption when *your* interaction is what sets that flag — otherwise
it stops itself the instant it starts. Death and ladder climbing still stop an exempt session;
only the special-animation flag alone is ignored.

Both are also available as comma-separated interaction-id lists in the plugin config
(`Interaction Animation API V2` section) so an operator can exempt an interaction whose author did
not. The effective exemption is manifest flag **OR** config list; the config defaults are empty.

## Complete example — live body (`BodyWorld`)

A two-handed lantern held indefinitely, ended by the consumer with `TryBeginInteractionExit`.

```jsonc
{
  "schemaVersion": 1,
  "interactionId": "com.example.mymod.lantern",
  "displayName": "Lantern (live body)",
  "durationSeconds": 0,                    // indefinite toggle
  "frameRate": 30,                         // authoring metadata only
  "bundleInternalName": "mymod-lantern-playeranimations",
  "exemptFromCameraDisplacementGuard": false,
  "exemptFromSpecialAnimationAutoStop": false,
  "body": {
    "enabled": true,
    "bundleFileName": "mymod-lantern-playeranimations.animationbundle",
    "controller": "MyMod_Lantern_PlayerMetarig",
    "controllerAssetName": "MyMod_Lantern_PlayerMetarig.controller",
    "activeBool": "MyMod_Lantern_Active",
    "enterTrigger": "MyMod_Lantern_Enter",
    "exitTrigger": "MyMod_Lantern_Exit",
    "fullBodyLayer": "LanternFullBody",
    "firstPersonArmsLayer": "LanternFirstPersonArms",
    "startLayerWeight": 0.9,
    "layerWeightRampSeconds": 0.15,
    "fullBodyLayerWeight": 0,              // let vanilla animate the body; author the arms only
    "suppressRigBuilders": false,          // required: the clips drive IK targets
    "overrideSlotPrefix": "Lantern_FirstPersonArms_",
    "exitSeconds": 0.3,
    "movementParameter": "MyMod_Lantern_Move",
    "clipPack": {
      "enabled": true,
      "bundleFileName": "mymod-lantern-iktargets.animationbundle",
      "bundleInternalName": "mymod-lantern-iktargets",
      "overrides": [
        { "slot": "Lantern_FirstPersonArms_Hold",  "clip": "IKT_Lantern_Hold" },
        { "slot": "Lantern_FirstPersonArms_Raise", "clip": "IKT_Lantern_Raise" },
        { "slot": "Lantern_FirstPersonArms_Lower", "clip": "IKT_Lantern_Lower" }
      ]
    },
    "prop": {
      "enabled": true,
      "prefabName": "MyMod_LanternProp",
      "attachBone": "hand.L",
      "localPosition": { "x": 0.0067, "y": 0.107, "z": 0.0386 },
      "localEulerAngles": { "x": 311.011, "y": 356.354, "z": 296.092 },
      "localScale": 1.19,
      "releaseSeconds": 0                  // keep until stop
    }
  }
}
```

## Complete example — dedicated local viewmodel

A 2.5 s local-only inspection animation on a camera-parented prefab.

```jsonc
{
  "schemaVersion": 1,
  "interactionId": "com.example.mymod.lantern_inspect",
  "displayName": "Lantern inspection (viewmodel)",
  "durationSeconds": 2.5,                  // auto-stops with NaturalEnd
  "bundleInternalName": "mymod-lantern-viewmodel",
  "liveRenderersToHide": [ "thisPlayerModelArms" ],
  "localViewmodel": {
    "bundleFileName": "mymod-lantern-viewmodel.animationbundle",
    "prefab": "MyMod_Lantern_LocalViewmodel",
    "controller": "MyMod_Lantern_LocalViewmodel",
    "activeBool": "MyMod_Lantern_Active",
    "enterTrigger": "MyMod_Lantern_Enter",
    "exitTrigger": "MyMod_Lantern_Exit",
    "exitSeconds": 0.87,
    "cameraAnchor": "MyMod_ViewmodelCameraAnchor",
    "cameraLocalPosition": { "x": 0, "y": -0.42, "z": 0.95 },
    "cameraLocalEuler":    { "x": 0, "y": 0,     "z": 0 },
    "localScale":          { "x": 0.55, "y": 0.55, "z": 0.55 },
    "hideSourceRenderers": [ "SourceSceneFloor" ],
    "visibleRenderers":    [ "LanternBody", "ArmsLeft", "ArmsRight" ],
    "runtimeMaterialMode": ""
  },
  "sockets": {
    "leftHand":  "hand.L",
    "rightHand": "hand.R",
    "prop":      "LanternBody"
  },
  "validation": {
    "generatedAt": "2026-08-05T12:00:00Z",
    "socketNames": "verified",
    "cameraBounds": "verified"
  }
}
```
