# Lethal Company Rig Reference

Last updated: 2026-08-09

BodyWorld plays a controller on the live PlayerControllerB body animator. Clips must therefore be authored for the supported hierarchy and binding paths. No particular retargeting tool is required.

## What the API assumes

- The target PlayerControllerB and playerBodyAnimator are valid.
- The current controller is the expected vanilla controller unless an incumbent API session owns it.
- Controller layers and named parameters referenced by the manifest exist.
- Clip transform paths resolve against the live player hierarchy.
- Optional prop paths resolve relative to the arms metarig used by the presenter.

The clean-room proxy in examples/UnityProject preserves authoring names using primitive geometry. It is not a runtime replacement for the player prefab.

## Binding practice

AnimationClip bindings are exact strings. Treat path spelling, separators, case, and hierarchy depth as contract data.

- Use forward slashes in manifest paths.
- Do not add a leading or trailing slash.
- Do not include dot or parent segments.
- Keep controller parameter types exact.
- Prefer stable bone paths over recursive name-only searches.
- Validate every binding against the proxy before bundle export.

## Layers and parameters

The body manifest can name a full-body layer, first-person-arms layer, active Bool, entry Trigger, exit Trigger, and movement Int. Missing optional names are skipped; supplied names must exist with compatible types.

Layer weights are restored from the captured animator snapshot. startLayerWeight must be between zero and one. fullBodyLayerWeight may be negative to share the main ramp or between zero and one for a fixed value.

## Local and remote behavior

A remote BodyWorld session owns only that player's body animator. A local BodyWorld session also owns the local camera/arms presentation and applies the restoration seam needed for viewpoint, arms, visor, rig-builder, and scoped transform recovery.

preserveGameplayCamera keeps camera continuity through controller swaps and rebinds without freezing vanilla locomotion movement. stopOnGameplayCameraDisplacement independently enables the stance-aware safety guard. stabilizeLocalCameraPosition is a separate opt-in position pin, and localCameraOwnedExternally is the separate ownership declaration that makes first-person camera and visor behavior stand down. stopOnVanillaSpecialAnimation requests interruption when the base game enters a conflicting special-animation state.

## Rig builders

rebuildRigBuilders requests the generic rebuild path around controller playback. Use it only when the authored controller requires a rebuilt Animation Rigging graph. The API captures and restores affected rig state.

## Validation versus playtesting

Static validation catches missing paths, layers, parameters, controllers, anchors, props, and bindings. It cannot certify skin weights, foot contact, elbow/knee shape, camera comfort, occlusion, network timing, or cumulative visual drift. Those remain in-game human gates.
