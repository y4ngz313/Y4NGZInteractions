# Authoring Guide

Last updated: 2026-08-08

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

## BodyWorld workflow

1. Duplicate the BodyWorld proxy and preserve the required transform names.
2. Author or retarget the clip.
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
