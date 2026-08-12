# Lethal Company Rig Reference

Last updated: 2026-08-11

BodyWorld plays a controller on the live PlayerControllerB body animator. Clips must therefore be authored for the supported hierarchy and binding paths. No particular retargeting tool is required. This page lists the actual transform paths so you can author or retarget against them directly.

Paths below were verified in-game against Lethal Company v81. The player rig is scene data owned by the base game and can change in a game update; the "Re-verifying after a game update" section explains how to confirm the hierarchy yourself.

## How clip paths resolve

- **BodyWorld body clips** resolve relative to the body animator root. That animator lives on `ScavengerModel/metarig` under the player object, so a clip curve bound to `spine/spine.001` animates `ScavengerModel/metarig/spine/spine.001`.
- **First-person arms** are a separate arms-only rig that lives *inside* the same hierarchy at `ScavengerModelArmsOnly/metarig/...` relative to the body animator root.
- **Prop attachment bones** (`attachBone` in the manifest) are resolved by name within the arms metarig used by the presenter, so a bare bone name such as `hand.R` is sufficient there.
- Bindings that do not resolve are not errors at runtime: Unity logs a warning and the curve silently animates nothing. If your animation "plays" but nothing moves, wrong binding paths are the first thing to check (see Troubleshooting).

## Supported transform paths (body animator root: `metarig`)

### Spine and arms chain

~~~text
spine
spine/spine.001
spine/spine.001/spine.002
spine/spine.001/spine.002/spine.003
spine/spine.001/spine.002/spine.003/shoulder.L
spine/spine.001/spine.002/spine.003/shoulder.L/arm.L_upper
spine/spine.001/spine.002/spine.003/shoulder.L/arm.L_upper/arm.L_lower
spine/spine.001/spine.002/spine.003/shoulder.L/arm.L_upper/arm.L_lower/hand.L
spine/spine.001/spine.002/spine.003/shoulder.R
spine/spine.001/spine.002/spine.003/shoulder.R/arm.R_upper
spine/spine.001/spine.002/spine.003/shoulder.R/arm.R_upper/arm.R_lower
spine/spine.001/spine.002/spine.003/shoulder.R/arm.R_upper/arm.R_lower/hand.R
~~~

Note the segment names: `arm.L_upper` / `arm.L_lower`, not `upper_arm.L` or `forearm.L`. Exact spelling is contract data.

### Fingers

Each hand has five finger chains, `finger1` (thumb) through `finger5`, each with one `.001` child segment:

~~~text
.../hand.L/finger1.L
.../hand.L/finger1.L/finger1.L.001
.../hand.L/finger2.L
.../hand.L/finger2.L/finger2.L.001
... (finger3.L, finger4.L, finger5.L follow the same pattern)
.../hand.R/finger1.R
.../hand.R/finger1.R/finger1.R.001
... (finger2.R through finger5.R follow the same pattern)
~~~

### Legs

~~~text
spine/thigh.L
spine/thigh.L/shin.L
spine/thigh.R
spine/thigh.R/shin.R
~~~

The thighs parent directly to `spine`, not to a pelvis segment.

### Animation Rigging control transforms (body)

The base game drives the body with Unity Animation Rigging constraints. These control transforms exist under the body animator root and are captured/restored by the API around every live-body session:

~~~text
spine/spine.001/spine.002/spine.003/LeftArm_target
spine/spine.001/spine.002/spine.003/RightArm_target
Rig 1/LeftLeg/LeftLeg_target
Rig 1/LeftLeg/LeftLeg_hint
Rig 1/RightLeg/RightLeg_target
Rig 1/RightLeg/RightLeg_hint
~~~

Animating IK targets instead of (or in addition to) FK bones is valid; understand that the vanilla constraints keep evaluating unless your manifest requests rig-builder handling (see Rig builders below).

### First-person arms rig

The local player's camera-space arms are a second metarig nested in the same hierarchy. Relative to the body animator root:

~~~text
ScavengerModelArmsOnly/metarig
ScavengerModelArmsOnly/metarig/spine.003
ScavengerModelArmsOnly/metarig/spine.003/shoulder.L/arm.L_upper
ScavengerModelArmsOnly/metarig/spine.003/shoulder.L/arm.L_upper/arm.L_lower
ScavengerModelArmsOnly/metarig/spine.003/shoulder.L/arm.L_upper/arm.L_lower/hand.L
ScavengerModelArmsOnly/metarig/spine.003/shoulder.R/arm.R_upper
ScavengerModelArmsOnly/metarig/spine.003/shoulder.R/arm.R_upper/arm.R_lower
ScavengerModelArmsOnly/metarig/spine.003/shoulder.R/arm.R_upper/arm.R_lower/hand.R
~~~

Hands carry the same finger chains as the body rig. The arms IK control transforms:

~~~text
ScavengerModelArmsOnly/metarig/spine.003/RigArms/LeftArm/ArmsLeftArm_target
ScavengerModelArmsOnly/metarig/spine.003/RigArms/RightArm/ArmsRightArm_target
ScavengerModelArmsOnly/metarig/spine.003/RigArms/RightArmClipboard/ArmsRightArm_target
~~~

### Camera

The gameplay camera container also lives under the metarig:

~~~text
CameraContainer
CameraContainer/MainCamera
~~~

Vanilla full-body clips can and do animate the camera container. The API's curve-binding audit and `preserveGameplayCamera` exist because of this; avoid binding the camera container unless you intend to own the viewpoint.

### Known-but-unlisted segments

The rig also contains head/neck segments above `spine.003` and foot segments below the shins. Their exact path spellings have not been re-verified for this document; treat any binding to them as unverified until you confirm it with a hierarchy dump (next section). If you verify additional paths, please contribute them via an issue or pull request.

## Re-verifying after a game update

The hierarchy above is game scene data, not part of this API. To confirm it against your installed game version:

1. Install a runtime inspector such as UnityExplorer in a test profile, open the scene graph, and expand `Player/ScavengerModel/metarig`.
2. Or author a deliberate probe clip binding a suspect path; if the path is wrong, Unity logs `Could not resolve '<path>' because it is not a child Transform in the Animator hierarchy` when the controller enters the state.
3. Or compare against the clean-room proxy *names* — but see the warning below.

## The clean-room proxy is not the live rig

The proxy generated by examples/UnityProject (`BodyWorldProxy`) exists so the example content can be authored and validated without extracting game assets. Its hierarchy is deliberately simplified and its segment names (`spine.004/shoulder.R/arm.R/forearm.R/hand.R`) do **not** all match the live player rig listed above. Use the proxy to learn the toolchain and validation flow; use this page's paths when authoring clips that must move the real player body.

## What the API assumes

- The target PlayerControllerB and playerBodyAnimator are valid.
- The current controller is the expected vanilla controller unless an incumbent API session owns it.
- Controller layers and named parameters referenced by the manifest exist.
- Clip transform paths resolve against the live player hierarchy.
- Optional prop paths resolve relative to the arms metarig used by the presenter.

## Binding practice

AnimationClip bindings are exact strings. Treat path spelling, separators, case, and hierarchy depth as contract data.

- Use forward slashes in manifest paths.
- Do not add a leading or trailing slash.
- Do not include dot or parent segments.
- Keep controller parameter types exact.
- Prefer stable bone paths over recursive name-only searches.
- Validate every binding before bundle export, and verify at least once in game.

## Layers and parameters

The body manifest can name a full-body layer, first-person-arms layer, active Bool, entry Trigger, exit Trigger, and movement Int. Missing optional names are skipped; supplied names must exist with compatible types.

Layer weights are restored from the captured animator snapshot. startLayerWeight must be between zero and one. fullBodyLayerWeight may be negative to share the main ramp or between zero and one for a fixed value.

## Local and remote behavior

A remote BodyWorld session owns only that player's body animator. A local BodyWorld session also owns the local camera/arms presentation and applies the restoration seam needed for viewpoint, arms, visor, rig-builder, and scoped transform recovery.

preserveGameplayCamera keeps camera continuity through controller swaps and rebinds without freezing vanilla locomotion movement. stopOnGameplayCameraDisplacement independently enables the stance-aware safety guard. stabilizeLocalCameraPosition is a separate opt-in position pin, and localCameraOwnedExternally is the separate ownership declaration that makes first-person camera and visor behavior stand down. stopOnVanillaSpecialAnimation requests interruption when the base game enters a conflicting special-animation state.

## Rig builders

rebuildRigBuilders requests the generic rebuild path around controller playback. Use it only when the authored controller requires a rebuilt Animation Rigging graph. The API captures and restores affected rig state.

## Validation versus playtesting

Static validation catches missing paths, layers, parameters, controllers, anchors, props, and bindings. It cannot certify skin weights, foot contact, elbow/knee shape, camera comfort, occlusion, network timing, or cumulative visual drift. Those remain in-game human gates. Wrong binding paths in particular produce no validation error and no runtime error — only a Unity warning and a motionless bone.
