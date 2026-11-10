# Advanced Prop Recipe

Last updated: 2026-11-10

This generic recipe attaches a consumer-authored prop to BodyWorld and optionally supplies animation clips separately from the shell controller.

## 1. Author the prop

Create an original prefab with its pivot at the intended grip reference. Apply scale deliberately and avoid scene-only dependencies. The prefab must live in the controller bundle or enabled clip-pack bundle.

## 2. Choose an attachment path

Use the rig reference and validator to select an arms-metarig-relative bone path. On the live arms metarig the right-hand chain is:

~~~text
spine.003/shoulder.R/arm.R_upper/arm.R_lower/hand.R
~~~

The clean-room example content instead uses the proxy's simplified chain (`spine.004/shoulder.R/arm.R/forearm.R/hand.R`); that chain only exists on the example viewmodel, not on the live player rig. See the [Lethal Company Rig Reference](LETHAL_COMPANY_RIG_REFERENCE.md) for the verified live paths.

Do not rely on a recursive leaf name when duplicate names may exist.

## 3. Set a neutral local transform

Start with zero local position/rotation and scale one. Adjust in small increments while testing the live local and remote body. Record only the final transform in schema 2.

~~~json
"prop": {
  "enabled": true,
  "prefabAssetName": "CommunityExampleProp",
  "attachBonePath": "spine.003/shoulder.R/arm.R_upper/arm.R_lower/hand.R",
  "localPosition": { "x": 0, "y": 0, "z": 0 },
  "localEulerAngles": { "x": 0, "y": 0, "z": 0 },
  "localScale": 1,
  "releaseSeconds": 0
}
~~~

releaseSeconds zero keeps the prop until restoration. A positive value releases it during the session.

### Local body copy

The local player's prop rides the first-person arms rig, which follows the camera, not the body. A camera that renders the local player's own body (a drone view, a third-person camera) would show the prop floating above the body's hands. Add `"localBodyCopy": true` to the prop to opt in to a body-side copy:

- For the local player only, a second instance of the prefab, named `Y4NGZ_<prefabAssetName>_BodyInstance`, is attached to the full body's bone with the same local position, rotation and scale. The first-person prop (`Y4NGZ_<prefabAssetName>_Instance`) is unchanged. Remote players are unchanged too: their prop already sits on the body.
- The body bone is resolved with the same attach path from the body animator root, as for remote players. If it does not resolve there, the copy is skipped with a `live_body.prop_body_copy_bone_missing` warning and the session runs as without the flag.
- The copy is visual only: colliders, rigidbodies and joints are removed, and lights and audio sources are disabled.
- Each camera decides just before it renders (`RenderPipelineManager.beginCameraRendering`). A camera renders the local body when `thisPlayerModel` is enabled, active, not forced off, not shadows-only, and on a layer in the camera's culling mask. Such a camera draws the copy, on the body's layer, and not the first-person prop. Every other camera draws the first-person prop as before and not the copy.
- Interactions owns `Renderer.forceRenderingOff` on both instances, including renderers you parent under either instance later (a screen quad, for example); they follow the same rule. Find both instances by name to dress both, and do not set `forceRenderingOff` on their renderers yourself.
- A camera whose own `beginCameraRendering` handler makes the body visible after Interactions' handler has run sees the first-person prop, not the copy. Make the body visible before that camera's rendering starts instead.

## 4. Use a clip pack when useful

A reusable shell controller can expose stable placeholder clip slots. Put authored clips in a separate bundle and map each slot:

~~~json
"clipPack": {
  "enabled": true,
  "bundleFileName": "props/community_example_clips",
  "bundleInternalName": "community_example_clips",
  "overrides": [
    { "slot": "Interaction_Main", "clip": "CommunityExample_Use" },
    { "slot": "Interaction_Exit", "clip": "CommunityExample_Exit" }
  ]
}
~~~

Slot names must be unique. Every mapped clip asset and shell-controller slot must exist.

## 5. Validate and test

Run prop attachment, controller, clip binding, and bundle validation. In game, verify:

- local and remote grip placement;
- no double rendering;
- with `localBodyCopy`, one prop per camera: the arms-rig prop in first person, the copy in the body's hand in a camera that renders your own body;
- repeated start/stop and graceful exit;
- natural completion and interruption;
- death, ladder, and round transitions;
- prop destruction/release;
- animator, camera, arms, visor, and rig restoration.

Keep balance choices, gameplay actions, input, and network messages in the consumer.
