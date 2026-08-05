# Animation authoring and retargeting pipeline

Last updated: 2026-08-05

> **Internal authoring methodology (maintainer reference).** External modders: see
> `docs/GETTING_STARTED.md` and `docs/MANIFEST_REFERENCE.md`. This document records the
> maintainer's own authoring methodology and evidence-gate discipline. The authoring tools it
> refers to (the retarget baker, the preview renderer, the source-prefab builder) live in the
> maintainer's Unity projects and consumer repositories and are **not shipped with this API**.
> The gate concepts transfer to any tooling; the specific tools do not.

> **Retargeting a weapon?** Read this document first for the generic contract, then
> `WEAPON_RETARGET_PIPELINE.md` — the source of truth for the weapon use type (first-person weapon
> retarget, animated props, third-person body derivation).

This is the sole generic source of truth for authoring and retargeting animation content
through Y4NGZInteractions. Consumer repositories keep only feature wiring, per-asset status,
and links back here. New clips, controllers, manifests, finger-pose data, props, VFX, audio,
and deployment entries belong to the consumer mod that uses them.

## What the evidence actually proves

Per-asset verdicts, frozen baselines, and rejected-candidate history are **consumer-owned**.
For Y4NGZ assets they live in `Y4NGZUpgrades/docs/` — the status doc for the item plus its
evidence packet. Do not record them here; this section describes only what *classes* of
evidence mean.

### Proven in game

An explicit human in-game verdict. Only this class approves a visible pose. At least one
consumer asset must hold this status to establish that the end-to-end runtime path works;
that asset is the frozen regression baseline all shared-tool changes are compared against.

### Mechanically verified, not visual approval

The following checks are useful gates, but none can approve a visible pose:

- ChainIK target transfer and reach/error checks;
- source motion measurement and curve-binding inventories;
- build, hash, deployment, and import verification;
- byte comparisons against frozen outputs.

These checks prove that data moved through a particular path. They do not prove composition,
contact, silhouette, timing, or authored intent.

### False or stale claims

- Imported finger curves alone do not control the visible shipped glove pose.
- New consumer animation content does not belong in Y4NGZInteractions.

### Rejected history

A rejected candidate's contact, visible-palm, direct-pose, and manual-fit experiments stay as
historical evidence in the consumer repository and editor lab, never as an implied next step
here. A rejected mechanism may inspire a diagnostic, but it must not become a default. Never
onboard a new asset from a rejected asset's experiment list.

## Definition of a successful retarget

A retarget succeeds only when synchronized source and solved-LC views agree closely enough for
the intended first-person experience in all of these dimensions:

- composition and camera framing;
- action timing and phase boundaries;
- complete arm and elbow silhouette;
- grip seating and hand-stacking clearance;
- full finger silhouette, not only fingertip position;
- muzzle/crosshair axis;
- authored prop motion and animated descendants.

Low target error, a passing loop check, or a clean hash report may reject or explain a
candidate. None may override a visibly poor source-versus-LC comparison.

A validator that reports `FAIL` is a publication blocker, not advisory text. Resolve the
failure or change the per-asset strategy before replacing editor outputs, consumer bundles, or
deployed files. This applies equally to interactive editor runs and batchmode. A rendered sheet
cannot silently waive an implausible finger excursion when the final runtime owner has not been
modeled and accepted in game.

## Runtime ownership contract

`playerBodyAnimator` is an exclusive resource. The API allows one `BodyWorld` session per
player, preempts the prior API session through its normal restore path, and fails closed while
a non-API controller owns the animator. Direct controller-swap features must either defer while
`LCInteractionAnimationAPI.IsPlayerInteractionActive(player)` is true, or call
`TryStopPlayerInteractions`, verify ownership was released, and then take it. Do not create a
second consumer-side snapshot stack or start a vanilla state/trigger against somebody else's
custom controller.

Stopping a live-body session must restore more than the controller reference. The shared
snapshot path rebinds the skeleton, restores vanilla parameters/layers/states, evaluates it,
rebuilds the live `RigBuilder` graph, reapplies the local gameplay-camera and camera-container
rotations captured before the rebind, and evaluates again. New consumers inherit this path;
they must not add per-animation camera, wrist, or arm reset code.

Any consumer pose write performed after Animator evaluation (`LateUpdate`, bone mappers, or
`Application.onBeforeRender`) must re-check that its API handle is active immediately before
writing. It must bind through the exact intended renderer's `bones`; never search every
same-named transform under the player. A late callback can otherwise overwrite the API's clean
restore, and an all-player name search also corrupts the third-person, humanoid, or emote rigs.

First-person-only interactions set `fullBodyLayerWeight` to `0`. A nonzero full-body layer
requires a curve-binding audit proving that the clip cannot animate the camera container,
gameplay camera, player root, or first-person-arms hierarchy. Do not place a raw vanilla clip
in a custom full-body layer merely to obtain a remote animation; replicate the remote vanilla
animation separately. The runtime camera-displacement guard is a last-resort containment tool,
not an asset validator: its TryStart reference can be flagged as externally contaminated, and
configured interactions whose external pose owner legitimately moves the camera can be exempt.
A non-exempt, clean-baseline stop is still a mandatory curve/controller audit signal; no guard
outcome makes camera curves acceptable by itself.

The scoped transform-restore experiment is not a new global default. Only a deliberately selected
test interaction may set `scopedFirstPersonTransformRestore: true`; it must also prove post-stop
vanilla poses and camera stability in game. `enterLayerFadeSeconds` and
`naturalEndLayerFadeSeconds` blend camera-near endpoints for short one-shots. Until the trial is
approved, every other manifest must omit all three fields.

For a short local interaction whose controller moves an ancestor of `gameplayCamera` despite a
zero full-body layer, `stabilizeLocalCameraPosition: true` pins only the camera's player-local
position in final LateUpdate and preserves look rotation. Treat this as a scoped containment tool,
not permission to ship camera curves; audit the controller first. It is a per-interaction trial
opt-in: every manifest that has not been through that trial leaves it absent/false.

Clip finger bindings work when the clip is sampled in isolation. The shipped weapon runtime,
however, currently enforces the rendered glove pose in `LateUpdate`. Preview evidence must
model that final runtime ownership and ordering. A preview that stops after clip sampling can
show curves that will never own the in-game frame.

When editor and game disagree, stop geometry iteration and audit manifest identity,
pack/profile selection, deployment hashes, bundle caching, animator/controller replacement,
finger `LateUpdate`, and other final-frame owners before changing retarget math. Use runtime
logs and controller/clip identity checks first. A temporary diagnostic should answer a specific
fault question; it is not a mandatory authoring stage.

For live-body final-frame comparisons, the presenter emits throttled
`live_body.transform_chain` samples from `Application.onBeforeRender`, after the coordinator and
other `LateUpdate` owners. Each sample records the actual camera FOV/aspect/pixel viewport,
camera-space right target and solved `hand.R` poses, target-to-hand position/rotation error, prop
local and camera-space pose, camera/local renderer bounds, and the projected prop-local `+Z`
reference ray. Compare `missNormalizedHeight` rather than raw pixels across resolutions. The
`+Z` ray is a neutral diagnostic convention, not an API claim that every consumer prop aims on
that axis; the consumer evidence packet must state its real authored axis and acceptance limit.

A manifest's `bundleInternalName` must equal the bundle's actual runtime `AssetBundle.name`,
not a conceptual/package identifier. Consumer content loaders and both presenters use this value
to discover an already-loaded bundle. A mismatch causes Unity's second `LoadFromFile` call to
fail and prevents the interaction from starting even when the file and its assets are valid.

## Required inputs and evidence

Never inherit another asset's measurements; every value below is gathered fresh per asset.

### Required inputs

- active issue and latest non-superseded handoff;
- untouched source FBX and authored first-person reference/render;
- named frozen outputs (the regression baseline plus every user-approved affected asset);
- exact Unity authoring project/version and target runtime profile;
- source camera and final gameplay camera contract;
- source clip binding roots and the exact prefab transform that must receive `SampleAnimation`;
- grip owner, moving hand anchor, prop root, and animated prop descendants.

### Required evidence packet

Store an immutable packet for each candidate round containing:

- `inspection-report.md`;
- source motion/curve-binding report;
- exact slice and loop report;
- source and solved-LC synchronized keyframes/contact sheets;
- same-camera full-resolution Hold, fire, and reload frames;
- reach, muzzle-axis, finger-silhouette, and animated-prop checks;
- import/build/deploy logs and file hashes;
- normal in-game equip/use/reload/drop/pickup captures and playtest verdict.

## Gates, in order

### 1. Source inspection and semantic classification

Import and inspect the untouched FBX in the authoring project before any retarget work. Record in
`inspection-report.md`:

- full hierarchy and rig/root identity;
- take name, frame rate, duration, and sample count;
- every animated binding and animated prop descendant;
- motion intervals and still intervals;
- source camera transform/FOV and intended composition;
- grip ownership and moving attachment anchor;
- candidate loop boundaries and endpoint agreement;
- authored muzzle axis/crosshair relationship;
- two-hand stacking compactness and available glove clearance.

Render every candidate segment before assigning semantics. Numeric motion intervals do not
tell whether a tail is a draw, melee, inspect, or transition.

Before trusting any motion report, compare the first segment of the clip's binding paths with the
instantiated prefab hierarchy. The transform passed to `SampleAnimation` must be the root those
paths are relative to. A wrapper prefab with no `Animator` may nest the actual FBX roots beneath a
child; sampling the wrapper then succeeds without moving anything. Require a nonzero wrist/prop
range for every action that visibly moves in the authored source. Static min/max ranges or static
source sheets for a visibly moving action fail Gate 1; do not tune a retarget built from them.

Never invent or loop an action that is not visibly authored:

- use authored start/loop/end firing slices when present;
- if only an authored firing loop exists, crossfade Hold to/from that loop;
- if no valid firing pose exists, keep the approved Hold pose while VFX runs;
- do not turn a reload, melee, inspect, or transition into firing by relabeling it.

### 2. Editor source-versus-LC approval

Render source and solved LC at synchronized normalized times from matched cameras. Contact
sheets must include action boundaries and contact/motion peaks, not merely evenly spaced
frames. Review the full composition before closeups.

Measure hand-stacking compactness early. If LC glove geometry cannot plausibly occupy the
authored grip volume, run one fresh-instance, same-camera manual-fit feasibility check before
iterative baking. If no manually posed fit reads as a grip, change the design/fallback or reject
the asset; more solver rounds cannot manufacture geometric clearance.

This gate passes only when the full-frame source-versus-LC comparison is acceptable for every
required action. Mechanical reports remain attached as supporting evidence.

For fingers, sample every shipped action through the same ownership mode used at runtime
(`absolute` LateUpdate pose or clip-owned/additive pose). Record rest-relative excursion and
inspect the complete glove silhouette. Large discontinuities, near-180-degree excursions, or
collapsed/splayed gloves fail the candidate. For a roomy grip, a per-weapon stable LC grip pose
with sign-resolved curl is a valid fallback when source closure transfer is geometrically
invalid; preserve authored wrists and prop motion and document the deviation.

Whatever renders the preview must reproduce the runtime's final-frame ownership order. The
maintainer's preview renderer loads the finger-pose sidecar (`weapon-fp-finger-poses.json`) and,
for `mode: "absolute"`, applies it after clip sampling and the arm IK solve before every rendered
frame, mirroring the weapon runtime. A clip-only preview is not finger evidence, whatever tool
produced it.

Render full-resolution frames from each shipped action, not only the first looping clip. A Hold
frame cannot prove that Fire or Reload samples the correct source slice, prop clip, or final
finger owner.

The preview profile's camera offset, Euler adjustment, pivot, clip paths, and action slices must
match the bake profile exactly. A stale preview profile is not synchronized evidence even when
the render itself succeeds. Record the effective values in the evidence packet and compare them
before judging a candidate.

For a constant roomy long-gun grip, a consumer may sample a previously accepted clip on the same
LC skeleton as its static finger reference, then add only small sign-resolved closure. This reuses
finger geometry—not that weapon's wrist targets, prop scale, framing, or attachment. Render the
new weapon from fresh instances; an accepted source pose can still be unsuitable at a different
wrist orientation or handle geometry.

If the inherited reference remains open, splayed, or claw-like after a small curl sweep, its local
phalange basis is wrong for the new wrist/handle relationship. Remove the reference and build a
per-weapon stable grip from the neutral LC pose. Increasing curl cannot repair a basis mismatch
and usually compounds the silhouette failure.

### 3. Consumer bundle and import verification

Build with the runtime's Unity version (currently Unity 2022 for shipped Lethal Company
content). Verify:

- shell controller and IK-target clip pack import without warnings;
- clip names, lengths, loop flags, and bindings exactly match the manifest;
- animated prop descendants exist and respond at the expected times;
- manifests and finger JSON parse from the consumer output;
- controller, clips, prop, VFX, materials, and audio all live in the consumer bundle;
- first-person and world prop materials use shaders supported by the target runtime pipeline;
  a Built-in `Standard` material authored in a Built-in project must be remapped to a supported
  HDRP material before rendering in Lethal Company;
- deployed file hashes match the consumer build output.

Validate bundle-loaded non-legacy clips through the same `AnimatorOverrideController`, layer, and
state path used by the presenter. In the Unity editor, calling `SampleAnimation` directly on a
clip loaded from an `AssetBundle` can leave the target hierarchy unchanged even though the clip is
valid and the `Animator` plays it correctly. A raw direct-sample result is therefore not a bundle
motion verdict. Compare several normalized-time transforms from the imported clip and the
override-driven bundled clip; they must agree.

Consumers should set `InteractionAnimationPackDefinition.AssetRootPath` to the normalized
directory containing their DLL. Relative manifest bundle names then resolve only beneath that
root. Paths escaping it are rejected. Omitting `AssetRootPath` preserves the legacy
Y4NGZInteractions/plugin-root fallback for existing packs.

### 4. Playable consumer integration

Integrate the animation pack into the actual consumer feature in the same first pass. For a
weapon, that means the registered item, world model, use/reload behavior, networking, prop,
VFX, audio, and first-person animation are present together. Do not substitute a standalone
animation harness for the feature players will use.

### 5. In-game iteration and acceptance

Exercise first equip, re-equip, rapid hotbar switches, drop/pickup, action transitions,
reload, and return to Hold through normal gameplay. Compare synchronized source reference
frames against the game result, then iterate on animation and gameplay together until the
result is accepted.

Then exercise lifecycle boundaries: stop the interaction and run idle, walk, sprint, crouch,
held-item, and ship-lever animations; rapidly alternate at least two different live-body packs;
attempt a direct-controller feature (such as slide/mantle) during an API session and an API
interaction during that feature. Any tilted vanilla hands, duplicate/floating body parts,
controller ownership warning followed by a visible overlap, or pose that survives Stop is a
publication blocker.

## Retarget mechanics and validation rules

- Transfer source hand targets in the camera frame, then solve LC elbows using the intended
  source bend side and LC segment lengths.
- When a camera-space composition reads correctly but an arm cannot reach it, uniformly scaling
  target positions, the framing offset, and prop attachment about the camera preserves perspective
  (`p' = k(p + offset)`) while reducing physical reach. Changing depth alone changes perspective
  and can make a long weapon's lateral frame dominate the view.
- When visual feedback asks for a screen-space shift and the grips are already good, translate the
  complete hands-plus-prop composition in camera space. Moving only the prop attachment breaks the
  authored hand contacts and creates a second problem while appearing to solve the first.
- A measured source-to-LC hand ratio is evidence, not an automatic final prop scale. If the source
  prop is visibly too small but the full measured ratio overwhelms the frame, test a documented
  per-asset fraction between source scale and full normalization. Scale the prop and its source
  grip/palm offsets together about the attachment so contact is not silently broken.
- Recompose a long weapon with one rigid camera-space rotation about an authored grip pivot when
  translation alone cannot match its full-frame axis. Rotating the hand that owns the attached
  prop is not an isolated glove adjustment: it rotates the prop too. Do not use it as a wrist-only
  fix unless the attachment receives the equal-and-opposite compensation.
- Measure prop-to-hand scale and attachment basis per asset. Never inherit another weapon's
  camera or attachment offsets.
- Visible-palm-center normalization is an opt-in roomy-handle diagnostic, not a global wrist
  mapping rule. It improved seating on a roomy-handled lab asset, while on a compact-grip trial it
  improved a proxy metric and looked worse in game. Its visual verdict is asset-specific; the
  measured instances are consumer evidence.
- Inspect curve bindings to identify animated prop children; hierarchy names alone are not
  evidence of animation.
- Validate reach, loop endpoints, muzzle axis, prop motion, and all finger chains over every
  sampled frame.
- Evaluate left and right hands independently. A low fingertip error does not approve palm,
  proximal/distal phalanges, or the other hand.
- Use full-resolution, fresh-instance renders. Persistent preview rigs can retain stale skinned
  geometry after `Camera.Render`.
- Treat proxy penetration and landmark metrics as diagnostics, never as acceptance scores.
- Any shared authoring-tool change requires byte comparison of the frozen baseline outputs plus
  every approved asset/profile the code path can affect. Profile-only changes must prove unrelated
  profiles and outputs are unchanged.

## Runtime acceptance checklist

- the current playable candidate is deployed and hash-matched;
- first equip/re-equip/hotbar/drop/pickup are stable;
- Hold/action/reload transitions have no boundary flash or hidden fallback;
- the final `LateUpdate` glove pose agrees with the approved preview;
- camera framing and muzzle axis remain stable;
- prop children animate and return to their correct states;
- stop returns idle/walk/run/crouch/held-item/ship-lever poses to the vanilla baseline;
- rapid sequential interactions from different packs never overlap or restore out of order;
- direct-controller features and API sessions defer to the current animator owner;
- no post-Animator callback can write after its interaction handle becomes inactive;
- first-person-only packs keep the full-body layer at zero, and audited full-body clips cannot move the gameplay camera hierarchy;
- no unrelated approved profile or frozen output changed;
- consumer-owned content is absent from Y4NGZInteractions deploy entries.

## Dedicated-viewmodel authoring example

The dedicated local-viewmodel presenter is available for consumer-owned interactions that need
camera-local geometry or animation isolation from the live player rig. Prefer the live-body path
when the actual player body must remain the first-person source; prefer a dedicated viewmodel when
the live mesh cannot provide the required closed geometry or stable authored silhouette.

Every field below is consumer-authored; there are no schema defaults for the bundle or prefab, and
nothing is inherited from another pack. `MANIFEST_REFERENCE.md` is the authoritative field list.

```json
{
  "bundleFileName": "<consumer>-viewmodel.animationbundle",
  "prefab": "<ViewmodelPrefabName>",
  "controller": "<ViewmodelController>",
  "activeBool": "<Consumer>_Active",
  "enterTrigger": "<Consumer>_Enter",
  "exitTrigger": "<Consumer>_Exit",
  "exitSeconds": 0.85,
  "cameraAnchor": "Y4NGZ_ViewmodelCameraAnchor",
  "cameraLocalPosition": { "x": 0.0, "y": -0.42, "z": 0.95 },
  "cameraLocalEuler": { "x": 0.0, "y": 0.0, "z": 0.0 },
  "localScale": { "x": 0.55, "y": 0.55, "z": 0.55 },
  "runtimeMaterialMode": "safeGenerated"
}
```

A hand-attached prop rigidly authored under one hand's grip is a valid viewmodel shape, but a
proven prop's offsets and grip decisions are never reusable for a different item — least of all a
weapon. Clip inventories, lengths, and frozen status for specific props are consumer evidence.
