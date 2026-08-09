# Advanced Prop Recipe

Last updated: 2026-08-08

This generic recipe attaches a consumer-authored prop to BodyWorld and optionally supplies animation clips separately from the shell controller.

## 1. Author the prop

Create an original prefab with its pivot at the intended grip reference. Apply scale deliberately and avoid scene-only dependencies. The prefab must live in the controller bundle or enabled clip-pack bundle.

## 2. Choose an attachment path

Use the rig reference and validator to select an arms-metarig-relative bone path. A typical right-hand chain resembles:

~~~text
spine.004/shoulder.R/arm.R/forearm.R/hand.R
~~~

Do not rely on a recursive leaf name when duplicate names may exist.

## 3. Set a neutral local transform

Start with zero local position/rotation and scale one. Adjust in small increments while testing the live local and remote body. Record only the final transform in schema 2.

~~~json
"prop": {
  "enabled": true,
  "prefabAssetName": "CommunityExampleProp",
  "attachBonePath": "spine.004/shoulder.R/arm.R/forearm.R/hand.R",
  "localPosition": { "x": 0, "y": 0, "z": 0 },
  "localEulerAngles": { "x": 0, "y": 0, "z": 0 },
  "localScale": 1,
  "releaseSeconds": 0
}
~~~

releaseSeconds zero keeps the prop until restoration. A positive value releases it during the session.

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
- repeated start/stop and graceful exit;
- natural completion and interruption;
- death, ladder, and round transitions;
- prop destruction/release;
- animator, camera, arms, visor, and rig restoration.

Keep balance choices, gameplay actions, input, and network messages in the consumer.
