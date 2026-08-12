# Authoring Guide

Last updated: 2026-08-11

This guide builds both repository examples and then replaces one sample clip. The public tools validate the resulting API contract; they do not prescribe a retargeting method.

## Supported authoring inputs

Use any workflow that produces compatible Unity clips and controllers:

- Unity Humanoid retargeting;
- manual transform-path keyframe transfer;
- a custom editor script;
- animation authored directly on the target hierarchy;
- a DCC export mapped to the target hierarchy.

Runtime universal retargeting is outside 1.0.

## Build the supplied payloads

Open examples/UnityProject in Unity 2022.3.62f1. Choose Y4NGZ Interactions > Create Clean-Room Sources, then Y4NGZ Interactions > Build All Examples.

The source command creates only original primitives and animation data:

- BodyWorldProxy: a named proxy hierarchy for transform-path authoring;
- ExampleBodyController and Example_Wave;
- ExampleViewmodel: a custom local rig with Rig/CameraAnchor and Rig/Prop;
- ExampleViewmodelController and Example_Inspect.

The build command runs the contract validator, writes schema-2 JSON, and exports bundles into examples/GeneratedBundles.

## Retargeting walkthrough: from an external clip to the player rig

This is the path most animators arrive with: a clip made in Blender, bought from a store, or captured from mocap, that must end up moving the Lethal Company player body. The API does not retarget at runtime, so the retarget happens in your authoring tools, once, before export. Any route works if the final Unity AnimationClip binds the exact paths listed in the [Lethal Company Rig Reference](LETHAL_COMPANY_RIG_REFERENCE.md).

**Route A - animate directly on the target names (simplest).** Build or rename an armature so every bone matches the rig reference exactly (`spine`, `spine.001` ... `spine.003`, `shoulder.L`, `arm.L_upper`, `arm.L_lower`, `hand.L`, `finger1.L` ... and the mirrored `.R` chain; legs `thigh.L`/`shin.L`). Animate on it, export FBX with the armature as the root, and import into Unity with Animation Type set to **Generic** (not Humanoid - Humanoid import discards the transform paths the API needs). The imported clip's curves then already carry the correct relative paths.

**Route B - retarget an existing animation in your DCC.** Keep your source animation on its own rig, then transfer it to a target armature named per Route A using your retargeting tool of choice (Blender constraint-based retargeters, Auto-Rig Pro's remap, Rokoko Retargeting, etc.). Bake the result to keyframes on the target armature, delete the source rig, and export/import as Generic exactly as in Route A. The tool does not matter; the baked bone names and parenting do.

**Route C - remap paths inside Unity.** If you already have a Generic clip whose curve paths almost match (wrong prefix, wrong segment names), edit the bindings in the editor: `AnimationUtility.GetCurveBindings` / `SetEditorCurve` lets a small editor script rewrite each binding's `path`. This is also the repair route when validation or in-game testing shows a near-miss binding.

Whichever route you take, finish the same way:

1. Check every curve path against the rig reference (the example project's clip-binding validator automates this).
2. Put the clip in your controller (or clip pack), export the bundle, and validate.
3. Verify in game. A clip with wrong paths plays silently with no motion and no error - Unity only logs a `Could not resolve '<path>'` warning. Motionless bones mean wrong paths, not a broken API session.

Humanoid retargeting *inside* Unity is still useful as a production step (for example, retarget mocap onto a Humanoid avatar, then bake the result onto a Generic rig with the target names), but a Humanoid clip cannot be shipped directly: BodyWorld needs Generic path-bound curves.

## BodyWorld workflow

1. Duplicate the BodyWorld proxy to learn the toolchain, but author final clips against the live-rig paths in the [Lethal Company Rig Reference](LETHAL_COMPANY_RIG_REFERENCE.md) - the proxy's simplified names do not all match the live player rig.
2. Author or retarget the clip (see the walkthrough above).
3. Ensure every transform binding resolves against the supported player hierarchy.
4. Put the clip into a controller state or map it through a clip pack.
5. Inspect controller layers and parameters with the supplied menu command.
6. Run transform-path, clip-binding, controller, layer, and parameter validation.
7. Export the bundle and manifest.
8. Run the sample consumer before integrating into another mod.

The proxy is an authoring reference, not a runtime prefab and not a copy of a game model. Visual skinning quality must be checked in game.

## DedicatedLocalViewmodel workflow

1. Create any self-contained prefab and rig.
2. Add a deterministic camera anchor below the prefab, such as Rig/CameraAnchor.
3. Add an Animator and controller.
4. Keep renderer paths stable.
5. Validate the anchor path, controller, parameters, renderer paths, and clip bindings.
6. Export the prefab/controller bundle and schema-2 manifest.

The presenter instantiates the prefab under the gameplay camera, aligns its anchor using the manifest's neutral transform, applies renderer rules, and owns the local camera/arms lease until restoration.

## Replace the sample clip

1. Duplicate Example_Wave or Example_Inspect.
2. Change one visible keyframe curve.
3. Keep the clip name or update the controller/clip-pack mapping.
4. Run Y4NGZ Interactions > Validate Selected Payload.
5. Rebuild the relevant bundle.
6. Re-run the Unity EditMode tests.
7. Start the sample consumer and verify the changed motion.
8. Repeat start, graceful exit, explicit stop, and interruption cycles to check for drift.

## Supplied editor tools

The example project includes reusable menu commands and validators for:

- schema-2 manifest generation;
- AssetBundle export;
- controller layers and parameters;
- transform-path existence;
- camera-anchor path existence;
- animation clip bindings;
- renderer hide/show paths;
- prop attachment bones;
- prefab/controller/clip asset names.

Validation reports must identify the failing asset and property. A validator pass does not replace in-game camera, skinning, multiplayer, or restoration review.

## Bundle layout

Bundle paths in JSON are always relative to the consumer's registered AssetRootPath. Keep source assets, generated bundles, and manifests owned by the consumer. Do not copy example bundles into the API package.

## Completion checklist

- Unity version matches ProjectVersion.txt.
- Both sample payloads build from a clean checkout.
- EditMode tests pass.
- Manifest validation has no errors.
- Bundle paths remain inside the pack root.
- No extracted model, texture, controller, clip, or third-party asset was introduced.
- The sample consumer runs both paths.
- A replaced clip is visible in game.
- Repeated stop/exit/interruption cycles restore without cumulative drift.
