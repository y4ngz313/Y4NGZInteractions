# Third-Person Rig Audit: ScavengerModel

Source audit date: 2026-07-03.

Last updated: 2026-08-05.

Primary sources, all relative to an AssetRipper export of the game (`ExportedProject/`):
`Assets/GameObject/Player.prefab`, `Assets/AnimatorController/metarig.controller`, the player body
`.anim` YAML files under `Assets`, and the decompiled scripts under `Assets/Scripts`.

## Reproducing this audit

Every `file:line` citation below points into the **text output of an asset extractor run against a
local copy of Lethal Company** — an AssetRipper export of the game's `ScavengerModel` assets,
game v81-era. Those files are not distributable and no path to them is given here.

An external reader can regenerate the same evidence: run AssetRipper (or an equivalent extractor)
over their own game install, open the exported project, and locate the same assets by the relative
paths above. Line numbers will differ with extractor version and game version — the GUIDs,
fileIDs, component types, and weights are the stable identifiers; treat the line numbers as an
index into the export this audit was written from, not as an assertion about any other export.

## Summary and Recommendation

The third-person player body is `Player/ScavengerModel/metarig`, driven by `Assets\AnimatorController\metarig.controller`. `Player.prefab:4150-4164` serializes the body `Animator` on `Player/ScavengerModel/metarig` with `m_Controller: {fileID: 9100000, guid: 58a045f0e56e86f4cb3acd03bf515d34, type: 2}`, `m_Avatar: {fileID: 0}`, and `m_ApplyRootMotion: 0`. `metarig.controller.meta` maps GUID `58a045f0e56e86f4cb3acd03bf515d34` to `Assets\AnimatorController\metarig.controller`. `Player.prefab:3408` binds `PlayerControllerB.playerBodyAnimator` to Animator fileID `95680831413375200`.

`ScavengerModelArmsOnly` (the first-person rig) is nested inside
`Player/ScavengerModel/metarig` in the source prefab. The shoulder transforms are children of
`spine.003`, as shown by the complete left/right arm paths in section 5.

The third-person runtime rig is active by default. `Player.prefab:4183-4188` adds a `RigBuilder` to `Player/ScavengerModel/metarig`; its first layer is `m_Rig: {fileID: 114589261748671662}`, `active: 1`. `Player.prefab:7082-7095` resolves that rig to `Player/ScavengerModel/metarig/Rig 1`, a `Rig` component with `m_Weight: 1`.

The active rig is not FK-only. Legs are `TwoBoneIKConstraint` components at weight `1`, target position weight `1`, target rotation weight `1`, and hint weight `0` (`Player.prefab:7113-7136`, `7184-7207`). Normal arms are `ChainIKConstraint` components at weight `1`, chain rotation weight `1`, and tip rotation weight `1` (`Player.prefab:7378-7400`, `7503-7525`). Secondary non-torso-relative arms are `TwoBoneIKConstraint` components with prefab `m_Weight: 0` but target position, target rotation, and hint weights all `1` (`Player.prefab:7433-7456`, `7558-7581`), and `PlayerControllerB.cs:5207-5221` switches those secondary arm constraints to weight `1` in the vehicle path. Head look is `MultiRotationConstraint` on `spine.003` and `spine.004`, sourced from `Player/ScavengerModel/metarig/CameraContainer/MainCamera`, with weights `0.452` and `1` (`Player.prefab:7253-7299`, `7315-7361`); `PlayerControllerB.cs:5197-5205` drives them toward `0.45f` and `1f` outside special interactions.

Vanilla third-person clips animate both metarig FK bones and IK targets. `Walk.anim` alone has target rotation curves on `LeftArm_target`, `RightArm_target`, `RightLeg_target`, and `LeftLeg_target` (`Walk.anim:120`, `172`, `224`, `276`), target position curves for those targets (`Walk.anim:1651`, `1703`, `1773`, `1834`), FK rotation curves on `spine/spine.001`, `spine/spine.001/spine.002`, `spine/thigh.L`, and `spine/thigh.R` (`Walk.anim:328`, `948`, `1473`, `1498`), and `spine` localPosition curves (`Walk.anim:1904`). Other sampled idle, sprint, crouch, ladder, wall, and item clips follow the same mixed convention; evidence is in section 2.

Recommendation: the third-person clip baker should emit vanilla-shaped clips, not pure FK-only clips. Bake FK localRotation curves for the trunk, shoulders, fingers, toes, and unconstrained support bones; bake `Player/ScavengerModel/metarig/spine` localPosition as the hips/root translation surface; and bake IK target localPosition plus localRotation/localEuler curves for `.../spine.003/RightArm_target`, `.../spine.003/LeftArm_target`, `Rig 1/RightLeg/RightLeg_target`, and `Rig 1/LeftLeg/LeftLeg_target`. Do not rely on FK localRotation alone for `thigh.*`, `shin.*`, `foot.*`, `arm.*_upper`, `arm.*_lower`, `hand.*`, `spine.003`, or `spine.004` while the relevant constraints are weighted. Fallback: if a source clip cannot produce reliable IK targets, play it as an explicit FK override only while runtime code or animation bindings zero the relevant `Rig 1` constraint weights, then restore vanilla weights.

## 1. Runtime Constraint Inventory

### Rig root and type evidence

`Player.prefab:4183-4188` serializes `m_RigLayers` on `Player/ScavengerModel/metarig` with first entry `m_Rig: {fileID: 114589261748671662}`, `active: 1`; the second rig layer points at the first-person arms-only rig and is out of scope. `Player.prefab:7082-7095` serializes the third-person `Rig` on `Player/ScavengerModel/metarig/Rig 1` with script GUID `70b342d8ce5c2fd48b8fa3147d48d1d1`, `m_Weight: 1`, and `m_Effectors: []`. Script GUIDs from `.meta` files: `TwoBoneIKConstraint` = `aeda7bfbf984f2a4da5ab4b8967b115d`; `ChainIKConstraint` = `796b75e345bd64d47a31edd757bd2670`; `MultiRotationConstraint` = `fdb90b913935e644baaa86c076d788e0`; `Rig` = `70b342d8ce5c2fd48b8fa3147d48d1d1`; `RigBuilder` = `fff0960ef4ea6e04eac66b4a7fd2189d`.

### Constraint table

| Rig path | Component evidence | Chain / constrained object | Target / hint / source | Weights and offsets | Hint status |
| --- | --- | --- | --- | --- | --- |
| `Player/ScavengerModel/metarig/Rig 1/LeftLeg` | `TwoBoneIKConstraint`, fileID `114506425039335177`, script GUID `aeda7bfbf984f2a4da5ab4b8967b115d`, `Player.prefab:7113-7136` | root `4334326746816030` = `.../spine/thigh.L` (`Player.prefab:5647-5662`); mid `4576069795566651` = `.../thigh.L/shin.L` (`5684-5699`); tip `4897634177956091` = `.../shin.L/foot.L` (`5721-5736`) | target `4889277733950485` = `.../Rig 1/LeftLeg/LeftLeg_target` (`Player.prefab:2273-2288`); hint `4740658008819512` = `.../Rig 1/LeftLeg/LeftLeg_hint` (`2258-2272`) | `m_Weight: 1`, `m_TargetPositionWeight: 1`, `m_TargetRotationWeight: 1`, `m_HintWeight: 0`, maintain position offset `0`, maintain rotation offset `0` | Assigned but inactive: `AnimationRuntimeUtils.cs:17` uses hints only when `hint.IsValid(stream) && hintWeight > 0f`. |
| `Player/ScavengerModel/metarig/Rig 1/RightLeg` | `TwoBoneIKConstraint`, fileID `114981129957739106`, script GUID `aeda7bfbf984f2a4da5ab4b8967b115d`, `Player.prefab:7184-7207` | root `4751195334024389` = `.../spine/thigh.R` (`Player.prefab:5800-5815`); mid `4990274360321875` = `.../thigh.R/shin.R` (`5838-5853`); tip `4216135833173336` = `.../shin.R/foot.R` (`5875-5890`) | target `4365203184906795` = `.../Rig 1/RightLeg/RightLeg_target` (`Player.prefab:2322-2337`); hint `4526275815064234` = `.../Rig 1/RightLeg/RightLeg_hint` (`2307-2321`) | `m_Weight: 1`, `m_TargetPositionWeight: 1`, `m_TargetRotationWeight: 1`, `m_HintWeight: 0`, maintain offsets `0` | Assigned but inactive for the same `m_HintWeight: 0` reason. |
| `Player/ScavengerModel/metarig/Rig 1/LookHead` | `MultiRotationConstraint`, fileID `114395925485973328`, script GUID `fdb90b913935e644baaa86c076d788e0`, `Player.prefab:7253-7299` | constrained `4368323404735207` = `.../spine/spine.001/spine.002/spine.003` (`Player.prefab:4308-4323`) | source `4096502200526905` = `.../CameraContainer/MainCamera` (`Player.prefab:7628`), source weight `1` | `m_Weight: 0.452`, constrained X/Y/Z all `1`, `m_MaintainOffset: 0` | No hint field; runtime field `cameraLookRig1` points here (`Player.prefab:3414-3426`). |
| `Player/ScavengerModel/metarig/Rig 1/LookHead2` | `MultiRotationConstraint`, fileID `114740961414846955`, script GUID `fdb90b913935e644baaa86c076d788e0`, `Player.prefab:7315-7361` | constrained `4762620569908748` = `.../spine/spine.001/spine.002/spine.003/spine.004` (`Player.prefab:5140-5155`) | source `4096502200526905` = `.../CameraContainer/MainCamera` (`Player.prefab:7628`), source weight `1` | `m_Weight: 1`, constrained X/Y/Z all `1`, `m_MaintainOffset: 0` | No hint field; runtime field `cameraLookRig2` points here (`Player.prefab:3414-3426`). |
| `Player/ScavengerModel/metarig/Rig 1/RightArm` | `ChainIKConstraint`, fileID `114854348660374394`, script GUID `796b75e345bd64d47a31edd757bd2670`, `Player.prefab:7378-7400` | root `4600577799526639` = `.../shoulder.R/arm.R_upper` (`Player.prefab:4716-4731`); tip `4622842638559028` = `.../arm.R_lower/hand.R` (`4791-4806`) | target `4823719891442257` = `.../spine.003/RightArm_target` (`Player.prefab:5308-5322`) | `m_Weight: 1`, `m_ChainRotationWeight: 1`, `m_TipRotationWeight: 1`, maintain offsets `0` | ChainIK component has no hint transform field. |
| `Player/ScavengerModel/metarig/Rig 1/RightArmNotTorsoRelative` | `TwoBoneIKConstraint`, fileID `114825013226233767`, script GUID `aeda7bfbf984f2a4da5ab4b8967b115d`, `Player.prefab:7433-7456` | root `4600577799526639` = `.../shoulder.R/arm.R_upper`; mid `4876071734904153` = `.../arm.R_upper/arm.R_lower` (`Player.prefab:4753-4768`); tip `4622842638559028` = `.../arm.R_lower/hand.R` | target `4110918674640190` = `.../Rig 1/RightArmNotTorsoRelative/RightArmB_target` (`Player.prefab:2419-2435`); hint `4810682503805774` = `.../RightArmB_hint` (`2436-2452`) | prefab `m_Weight: 0`, target position `1`, target rotation `1`, hint `1`, maintain offsets `0` | Assigned and active if component weight becomes nonzero; vehicle path drives `rightArmRigSecondary.weight` to `1f` (`PlayerControllerB.cs:5207-5221`). |
| `Player/ScavengerModel/metarig/Rig 1/LeftArm` | `ChainIKConstraint`, fileID `114658502067739592`, script GUID `796b75e345bd64d47a31edd757bd2670`, `Player.prefab:7503-7525` | root `4517786379655375` = `.../shoulder.L/arm.L_upper` (`Player.prefab:4371-4386`); tip `4169362067323189` = `.../arm.L_lower/hand.L` (`4445-4460`) | target `4660053332101633` = `.../spine.003/LeftArm_target` (`Player.prefab:5323-5337`) | `m_Weight: 1`, `m_ChainRotationWeight: 1`, `m_TipRotationWeight: 1`, maintain offsets `0` | ChainIK component has no hint transform field. |
| `Player/ScavengerModel/metarig/Rig 1/LeftArmNotTorsoRelative` | `TwoBoneIKConstraint`, fileID `114765752978741039`, script GUID `aeda7bfbf984f2a4da5ab4b8967b115d`, `Player.prefab:7558-7581` | root `4517786379655375` = `.../shoulder.L/arm.L_upper`; mid `4717606954488935` = `.../arm.L_upper/arm.L_lower` (`Player.prefab:4408-4423`); tip `4169362067323189` = `.../arm.L_lower/hand.L` | target `4067590077228376` = `.../Rig 1/LeftArmNotTorsoRelative/LeftArmB_target` (`Player.prefab:2471-2487`); hint `4081612810054407` = `.../LeftArmB_hint` (`2505-2521`) | prefab `m_Weight: 0`, target position `1`, target rotation `1`, hint `1`, maintain offsets `0` | Assigned and active if component weight becomes nonzero; vehicle path drives `leftArmRigSecondary.weight` to `1f` (`PlayerControllerB.cs:5207-5221`). |

`PlayerControllerB.cs:87-109` declares `cameraLookRig1`, `cameraLookRig2`, `rightArmRig`, `leftArmRig`, `leftArmRigSecondary`, and `rightArmRigSecondary`. `Player.prefab:3414-3426` serializes those fields to the component fileIDs above. `PlayerControllerB.cs:5197-5205` drives camera look weights toward `0f` during special interaction and toward `0.45f` / `1f` otherwise. `PlayerControllerB.cs:5207-5221` switches normal ChainIK arm weights to `0f` and secondary TwoBoneIK arm weights to `1f` in the vehicle path, then restores normal arms to `1f` and secondary arms to `0f` otherwise. No `PlayerControllerB` field was found for the leg constraints in the inspected code; leg weights are grounded by prefab defaults only.

## 2. What Vanilla Third-Person Clips Animate

The sampled vanilla third-person clips animate both metarig FK bone paths and IK target transforms. This is the game's own convention for the body controller.

### Controller state references

`Assets\AnimatorController\metarig.controller` references the sampled motions: `Idle1` state at `metarig.controller:598-620`; locomotion blend tree with `Walk.anim` and `WalkTired.anim` at `metarig.controller:713-737`; `Sprint` at `875-897`; `Jump` at `1095-1116`; `CrouchWalk` at `1300-1322`; `ClimbLadder` at `1508-1528`; `HandsOnWall` at `3394-3414`; and item hold/grab states at `2107-2237`.

### Clip curve evidence

| Clip | IK target curve evidence | FK / hips curve evidence | Conclusion |
| --- | --- | --- | --- |
| `Walk.anim` | rotation curves on `.../spine.003/LeftArm_target`, `.../spine.003/RightArm_target`, `.../Rig 1/RightLeg/RightLeg_target`, `.../Rig 1/LeftLeg/LeftLeg_target` at `Walk.anim:120`, `172`, `224`, `276`; target position curves at `1651`, `1703`, `1773`, `1834` | FK curves on `.../spine/spine.001`, `.../spine/spine.001/spine.002`, `.../spine/thigh.L`, `.../spine/thigh.R` at `Walk.anim:328`, `948`, `1473`, `1498`; `spine` position at `1904` | Mixed FK plus IK target animation. |
| `Idle1.anim` | right arm target at `Idle1.anim:68`; left arm target at `249`; arm target positions at `1561`, `1622`; leg target positions at `1803`, `1828` | FK curves on `spine.003`, `spine`, `thigh.L`, `thigh.R`, `spine.001`, `spine.002` at `224`, `274`, `1299`, `1324`, `1474`, `1499`; `spine` position at `1674` | Idle is also mixed, not FK-only. |
| `Sprint.anim` | leg targets at `Sprint.anim:654`, `859`; arm targets at `929`, `999`; target position curves at `5153`, `5466`, `5860`, `6479` | FK curves on `spine`, `spine.002`, `thigh.L`, `thigh.R`, and an upper arm path at `Sprint.anim:449`, `1132`, `1157`, `1182`, `1207` | Sprint carries IK target curves and body FK. |
| `CrouchDown.anim` | arm targets at `CrouchDown.anim:39`, `100`; leg targets at `311`, `336`; target positions at `1620`, `1663`, `1722`, `1747` | FK curves on `thigh.L`, `thigh.R`, `spine.001`, `spine.002`, `spine` at `175`, `200`, `234`, `268`, `911`; `spine` position at `1697` | Crouch transition is mixed. |
| `CrouchWalk.anim` | IK target rotation curves at `CrouchWalk.anim:48`, `145`, `188`, `1267`; target positions at `2124`, `2278`, `2330`, `2382` | FK curves at `1233`, `1310`, `1360`, `1385`, `2010`; `spine` position at `2244` | Crouch locomotion is mixed. |
| `ClimbLadder.anim` | arm targets at `ClimbLadder.anim:1219`, `1271`; leg targets at `1323`, `1375`; target positions at `2804`, `2856`, `3091`, `3143` | FK curves on `spine.002`, `spine`, `thigh.L`, `thigh.R`, `spine.004`, `spine.001` at `1436`, `1495`, `2120`, `2145`, `2170`, `2195`; `spine` position at `3021` | Action/climb animation uses the same mixed convention. |
| `HandsOnWall.anim` | arm targets at `HandsOnWall.anim:591`, `616`; arm target positions at `1323`, `1375`; leg target positions at `1400`, `1425` | finger FK curves at `HandsOnWall.anim:641`, `666`, `691`, `716` | Hands-on-wall animates arm IK targets and finger FK. |
| item clips | `GrabOneHandedItem.anim:147` and `941` animate right arm target rotation/position; `HoldFlashlight.anim:59` and `669` animate right arm target rotation/position; `HoldOneHandedItem.anim:343` and `721` animate right arm target rotation/position | `GrabOneHandedItem.anim:181`, `215`, `249`, `283`; `HoldFlashlight.anim:84`, `109`, `134`; `HoldOneHandedItem.anim:368`, `393`, `418` animate hand/finger FK paths | Item layers preserve the same target-plus-FK pattern for the right arm and fingers. |

The inspected clips target both path-based metarig FK bones, such as `Player/ScavengerModel/metarig/spine/...`, and IK target transform paths under `Player/ScavengerModel/metarig/Rig 1/...` for legs or under `Player/ScavengerModel/metarig/spine/spine.001/spine.002/spine.003/...` for normal arm targets. The sampled vanilla clips do not support a pure-FK assumption for constrained limbs.

## 3. Consequence Analysis for FK-Baked Custom Full-Body Clips

`TwoBoneIKConstraintJob.cs:32-42` calls `AnimationRuntimeUtils.SolveTwoBoneIK(...)` when job weight is positive. That solver writes rotations at `AnimationRuntimeUtils.cs:46` (`mid.SetRotation`), `AnimationRuntimeUtils.cs:48` (`root.SetRotation`), and `AnimationRuntimeUtils.cs:74` (`tip.SetRotation`). Therefore FK localRotation curves on `thigh.L`, `shin.L`, `foot.L`, `thigh.R`, `shin.R`, and `foot.R` are not authoritative while the leg constraints are weighted. Both leg constraints serialize `m_Weight: 1`, `m_TargetPositionWeight: 1`, and `m_TargetRotationWeight: 1` (`Player.prefab:7113-7136`, `7184-7207`).

`ChainIKConstraintJob.cs:51-60` writes chain rotations and tip rotation when job weight is positive. Therefore normal arm FK localRotation curves on `arm.L_upper`, `arm.L_lower`, `hand.L`, `arm.R_upper`, `arm.R_lower`, and `hand.R` are not authoritative while the normal arm ChainIK constraints are weighted. The normal arm constraints serialize `m_Weight: 1`, `m_ChainRotationWeight: 1`, and `m_TipRotationWeight: 1` (`Player.prefab:7378-7400`, `7503-7525`).

`MultiRotationConstraintJob.cs:78` writes the driven local rotation through `driven.SetLocalRotation(...)`. Therefore FK curves on `spine.003` and `spine.004` can be overwritten or blended by camera look while `LookHead` and `LookHead2` are weighted. The prefab weights are `0.452` and `1` (`Player.prefab:7253-7299`, `7315-7361`), and `PlayerControllerB.cs:5197-5205` drives them toward `0.45f` and `1f` outside special interaction.

Safe or mostly safe FK surfaces, based on the inspected constraint targets:

- `Player/ScavengerModel/metarig/spine`, including localPosition, because no inspected constraint writes this transform directly. Vanilla uses `spine` position curves in `Walk.anim:1904`, `Idle1.anim:1674`, `CrouchDown.anim:1697`, `CrouchWalk.anim:2244`, and `ClimbLadder.anim:3021`.
- `Player/ScavengerModel/metarig/spine/spine.001` and `.../spine.002`, because the camera constraints write `spine.003` and `spine.004`, not these transforms (`Player.prefab:7253-7299`, `7315-7361`).
- `shoulder.L` and `shoulder.R`, because the ChainIK roots are `arm.L_upper` and `arm.R_upper`, not the shoulder transforms (`Player.prefab:7378-7400`, `7503-7525`).
- Finger bones, because the inspected constraints target hands as tips but do not list finger transforms as roots, mids, tips, constrained objects, targets, or hints. Vanilla item and wall clips animate finger FK paths (`HandsOnWall.anim:641-716`, `GrabOneHandedItem.anim:181-283`, `HoldFlashlight.anim:84-134`, `HoldOneHandedItem.anim:368-418`).
- Toe local rotations are not directly constrained, but their parents `foot.L` and `foot.R` are TwoBoneIK tips (`Player.prefab:7113-7136`, `7184-7207`), so toe motion inherits an IK-computed foot pose.

Unsafe FK surfaces while vanilla rig weights remain active:

- `thigh.L`, `shin.L`, `foot.L`, `thigh.R`, `shin.R`, and `foot.R`, because leg TwoBoneIK solves and writes root, mid, and tip rotations.
- `arm.L_upper`, `arm.L_lower`, `hand.L`, `arm.R_upper`, `arm.R_lower`, and `hand.R`, because normal arm ChainIK writes the chain and tip rotations.
- `spine.003` and `spine.004`, because camera look MultiRotation constraints write those local rotations.
- Secondary arm chains during the vehicle/non-torso-relative path, because `PlayerControllerB.cs:5207-5221` drives `leftArmRigSecondary.weight` and `rightArmRigSecondary.weight` to `1f` while driving normal arm ChainIK weights to `0f`.

Recommended bake for Quaternius UAL slide and climb-up clips:

1. FK localRotation curves for the metarig trunk and support bones.
2. `Player/ScavengerModel/metarig/spine` localPosition for hips/root translation.
3. IK target localPosition and localRotation/localEuler curves for normal arms and legs: `.../spine.003/RightArm_target`, `.../spine.003/LeftArm_target`, `.../Rig 1/RightLeg/RightLeg_target`, and `.../Rig 1/LeftLeg/LeftLeg_target`.
4. Optional FK curves for fingers, toes, shoulders, and upper spine where the inventory shows no direct writer.

Do not prioritize leg hint baking unless runtime changes the leg constraints' `m_HintWeight` from `0`. `Player.prefab:7113-7136` and `7184-7207` assign leg hints, but both serialize `m_HintWeight: 0`, and `AnimationRuntimeUtils.cs:17` requires hint weight greater than zero before using the hint.

Fallback: if the retargeter cannot generate stable IK targets for a custom full-body state, treat that state as an explicit FK override and zero the relevant rig weights during the state. Existing runtime code already changes `MultiRotationConstraint`, `ChainIKConstraint`, and secondary `TwoBoneIKConstraint` weights (`PlayerControllerB.cs:5197-5221`), but the fallback must be explicit; FK-only curves are not safe under default leg and normal arm weights.

## 4. Animator Integration Surface

`Player.prefab:37` identifies the third-person model root as `ScavengerModel`. `Player.prefab:4150-4164` places the `Animator` on `Player/ScavengerModel/metarig` with fileID `95680831413375200`, `m_Avatar: {fileID: 0}`, `m_Controller: {fileID: 9100000, guid: 58a045f0e56e86f4cb3acd03bf515d34, type: 2}`, and `m_ApplyRootMotion: 0`. `Player.prefab:3408` binds `PlayerControllerB.playerBodyAnimator` to that Animator. The controller GUID resolves to `Assets\AnimatorController\metarig.controller`.

`StartOfRound.cs:151` declares `localClientAnimatorController`, and `StartOfRound.cs:153` declares `otherClientsAnimatorController`. `PlayerControllerB.cs` swaps `playerBodyAnimator.runtimeAnimatorController` in multiple ownership/view paths at `PlayerControllerB.cs:929-932`, `4033-4037`, `4237-4240`, and `4948-4951`. The inspected evidence proves the prefab controller is `metarig.controller` and proves runtime swapping between `StartOfRound.Instance.localClientAnimatorController` and `StartOfRound.Instance.otherClientsAnimatorController`. The serialized asset paths for those `StartOfRound` fields were not identified in the inspected snippets, so production integration should confirm whether both runtime controllers are variants of `metarig.controller` before patching only one controller asset.

`metarig.controller:372-442` serializes six layers:

| Layer | Evidence | Mask | Blending mode | Default weight | IK pass |
| --- | --- | --- | --- | ---: | ---: |
| `Base Layer` | `metarig.controller:372-382` | `m_Mask: {fileID: 0}` | `m_BlendingMode: 0` | `1` | `0` |
| `EmotesNoArms` | `metarig.controller:385-394` | `m_Mask: {fileID: 0}` | `m_BlendingMode: 0` | `0` | `0` |
| `HoldingItemsRightHand` | `metarig.controller:397-406` | `m_Mask: {fileID: 0}` | `m_BlendingMode: 0` | `0` | `0` |
| `UpperBodyEmotes` | `metarig.controller:409-418` | `m_Mask: {fileID: 0}` | `m_BlendingMode: 0` | `0` | `0` |
| `HoldingItemsBothHands` | `metarig.controller:421-430` | `m_Mask: {fileID: 0}` | `m_BlendingMode: 0` | `0` | `0` |
| `SpecialAnimations` | `metarig.controller:433-442` | `m_Mask: {fileID: 0}` | `m_BlendingMode: 0` | `0` | `0` |

No avatar masks are assigned in those layer entries: every cited layer has `m_Mask: {fileID: 0}`. Runtime layer weights are driven in `PlayerControllerB.cs`: `UpperBodyEmotes` at `5090`, `EmotesNoArms` at `5099`, holding item layers at `5180`, `5183`, `5187`, `5193`, and `5194`, and `SpecialAnimations` at `5196`. `PlayerControllerB.cs:5054-5078` updates `specialAnimationWeight`, and `PlayerControllerB.cs:5196` applies it with `playerBodyAnimator.SetLayerWeight(5, specialAnimationWeight)`. `PlayerControllerB.cs:4237-4243` also checks layer index `5` after a runtime controller swap.

Recommended animator slot: use `SpecialAnimations` for slide and climb-up custom full-body override states, or create an equivalent full-body override layer that mirrors it. This recommendation is grounded by `SpecialAnimations` having no avatar mask, override blending mode `0`, default weight `0`, and explicit runtime weight control through `specialAnimationWeight` (`metarig.controller:433-442`; `PlayerControllerB.cs:5054-5078`, `5196`).

## 5. Skeleton Reference for Retargeting

### Rig root scale

The prefab YAML does not contain a serialized Unity `lossyScale` property. The available serialized rest-pose scale evidence is `Player.prefab:3751-3768`, where `Player/ScavengerModel` transform fileID `4197734237196755` has `m_LocalScale: {x: 2, y: 2, z: 2}`, and `Player.prefab:4130-4149`, where `Player/ScavengerModel/metarig` transform fileID `4731717513228579` has `m_LocalScale: {x: 0.5608136, y: 0.5608136, z: 0.5608136}` and parent `m_Father: {fileID: 4197734237196755}`. The computed combined scale from `ScavengerModel` to `metarig` is approximately `{x: 1.1216272, y: 1.1216272, z: 1.1216272}`. This is computed from serialized local scales, not quoted from a runtime `lossyScale` field.

There is no bone named `hips` in the inspected metarig hierarchy. Vanilla clips use `Player/ScavengerModel/metarig/spine` position curves as the pelvis/root translation surface, for example `Walk.anim:1904`, `Idle1.anim:1674`, `CrouchDown.anim:1697`, `CrouchWalk.anim:2244`, and `ClimbLadder.anim:3021`.

### Core body hierarchy

```text
Player/ScavengerModel/metarig                                      fileID 4731717513228579  Player.prefab:4130-4149
  spine                                                           fileID 4778302311848492  Player.prefab:4235-4252
    spine.001                                                     fileID 4993483319365527  Player.prefab:4254-4269
      spine.002                                                   fileID 4524362187747215  Player.prefab:4271-4286
        spine.003                                                 fileID 4368323404735207  Player.prefab:4308-4323
          spine.004                                               fileID 4762620569908748  Player.prefab:5140-5155
            spine.004_end                                         fileID 4761579032523178  Player.prefab:5181-5196
```

### Left arm hierarchy

```text
Player/ScavengerModel/metarig/spine/spine.001/spine.002/spine.003/shoulder.L
  shoulder.L                                                      fileID 4127556766108663  Player.prefab:4355-4370
    arm.L_upper                                                   fileID 4517786379655375  Player.prefab:4371-4386
      arm.L_lower                                                 fileID 4717606954488935  Player.prefab:4408-4423
        hand.L                                                    fileID 4169362067323189  Player.prefab:4445-4460
          finger1.L/finger1.L.001 through finger5.L/finger5.L.001 Player.prefab:4465-4685
```

Constraint-relevant left arm evidence: normal ChainIK root is `arm.L_upper`, tip is `hand.L`, and target is `Player/ScavengerModel/metarig/spine/spine.001/spine.002/spine.003/LeftArm_target` (`Player.prefab:7503-7525`; target transform `Player.prefab:5323-5337`). Secondary TwoBoneIK root is `arm.L_upper`, mid is `arm.L_lower`, tip is `hand.L`, target is `Rig 1/LeftArmNotTorsoRelative/LeftArmB_target`, and hint is `LeftArmB_hint` (`Player.prefab:7558-7581`; target/hint transforms `Player.prefab:2471-2521`).

### Right arm hierarchy

```text
Player/ScavengerModel/metarig/spine/spine.001/spine.002/spine.003/shoulder.R
  shoulder.R                                                      fileID 4039589875578997  Player.prefab:4700-4715
    arm.R_upper                                                   fileID 4600577799526639  Player.prefab:4716-4731
      arm.R_lower                                                 fileID 4876071734904153  Player.prefab:4753-4768
        hand.R                                                    fileID 4622842638559028  Player.prefab:4791-4806
          finger1.R/finger1.R.001 through finger5.R/finger5.R.001 Player.prefab:4813-5048
```

Constraint-relevant right arm evidence: normal ChainIK root is `arm.R_upper`, tip is `hand.R`, and target is `Player/ScavengerModel/metarig/spine/spine.001/spine.002/spine.003/RightArm_target` (`Player.prefab:7378-7400`; target transform `Player.prefab:5308-5322`). Secondary TwoBoneIK root is `arm.R_upper`, mid is `arm.R_lower`, tip is `hand.R`, target is `Rig 1/RightArmNotTorsoRelative/RightArmB_target`, and hint is `RightArmB_hint` (`Player.prefab:7433-7456`; target/hint transforms `Player.prefab:2419-2452`).

### Left leg hierarchy

```text
Player/ScavengerModel/metarig/spine/thigh.L
  thigh.L                                                         fileID 4334326746816030  Player.prefab:5647-5662
    shin.L                                                        fileID 4576069795566651  Player.prefab:5684-5699
      foot.L                                                      fileID 4897634177956091  Player.prefab:5721-5736
        toe.L                                                     fileID 4568803479278024  Player.prefab:5769-5784
```

Constraint-relevant left leg evidence: TwoBoneIK root is `thigh.L`, mid is `shin.L`, tip is `foot.L`, target is `Rig 1/LeftLeg/LeftLeg_target`, and hint is `Rig 1/LeftLeg/LeftLeg_hint` (`Player.prefab:7113-7136`; target/hint transforms `Player.prefab:2258-2288`). `m_HintWeight: 0`, so the assigned hint is not used unless that weight changes (`Player.prefab:7113-7136`; `AnimationRuntimeUtils.cs:17`).

### Right leg hierarchy

```text
Player/ScavengerModel/metarig/spine/thigh.R
  thigh.R                                                         fileID 4751195334024389  Player.prefab:5800-5815
    shin.R                                                        fileID 4990274360321875  Player.prefab:5838-5853
      foot.R                                                      fileID 4216135833173336  Player.prefab:5875-5890
        toe.R                                                     fileID 4701422674934183  Player.prefab:5923-5938
```

Constraint-relevant right leg evidence: TwoBoneIK root is `thigh.R`, mid is `shin.R`, tip is `foot.R`, target is `Rig 1/RightLeg/RightLeg_target`, and hint is `Rig 1/RightLeg/RightLeg_hint` (`Player.prefab:7184-7207`; target/hint transforms `Player.prefab:2307-2337`). `m_HintWeight: 0`, so the assigned hint is not used unless that weight changes (`Player.prefab:7184-7207`; `AnimationRuntimeUtils.cs:17`).

### Practical UAL-to-metarig mapping anchors

| UAL role | LC metarig path | Evidence |
| --- | --- | --- |
| hips / pelvis | `Player/ScavengerModel/metarig/spine` | `Player.prefab:4235-4252`; position curve evidence `Walk.anim:1904` |
| spine 1 | `.../spine/spine.001` | `Player.prefab:4254-4269` |
| spine 2 / chest | `.../spine/spine.001/spine.002` | `Player.prefab:4271-4286` |
| upper chest / neck base | `.../spine/spine.001/spine.002/spine.003` | `Player.prefab:4308-4323`; constrained by `LookHead` |
| head / head end | `.../spine/spine.001/spine.002/spine.003/spine.004` | `Player.prefab:5140-5155`; constrained by `LookHead2` |
| left shoulder | `.../spine.002/spine.003/shoulder.L` | `Player.prefab:4355-4370` |
| left upper arm | `.../shoulder.L/arm.L_upper` | `Player.prefab:4371-4386` |
| left lower arm | `.../arm.L_upper/arm.L_lower` | `Player.prefab:4408-4423` |
| left hand | `.../arm.L_lower/hand.L` | `Player.prefab:4445-4460` |
| right shoulder | `.../spine.002/spine.003/shoulder.R` | `Player.prefab:4700-4715` |
| right upper arm | `.../shoulder.R/arm.R_upper` | `Player.prefab:4716-4731` |
| right lower arm | `.../arm.R_upper/arm.R_lower` | `Player.prefab:4753-4768` |
| right hand | `.../arm.R_lower/hand.R` | `Player.prefab:4791-4806` |
| left upper leg | `.../spine/thigh.L` | `Player.prefab:5647-5662` |
| left lower leg | `.../thigh.L/shin.L` | `Player.prefab:5684-5699` |
| left foot | `.../shin.L/foot.L` | `Player.prefab:5721-5736` |
| left toe | `.../foot.L/toe.L` | `Player.prefab:5769-5784` |
| right upper leg | `.../spine/thigh.R` | `Player.prefab:5800-5815` |
| right lower leg | `.../thigh.R/shin.R` | `Player.prefab:5838-5853` |
| right foot | `.../shin.R/foot.R` | `Player.prefab:5875-5890` |
| right toe | `.../foot.R/toe.R` | `Player.prefab:5923-5938` |

## 6. Local Player Third-Person Model Hiding

`PlayerControllerB.cs:20-35` declares the renderer/model fields `bodyParts`, `thisPlayerBody`, `thisPlayerModel`, `thisPlayerModelLOD1`, `thisPlayerModelLOD2`, and `thisPlayerModelArms`. `PlayerControllerB.cs:71` declares `public Animator playerBodyAnimator`.

At initialization, `PlayerControllerB.cs:915-925` enables the third-person body renderer normally and disables first-person arms with `thisPlayerModel.enabled = true`, `thisPlayerModel.shadowCastingMode = ShadowCastingMode.On`, and `thisPlayerModelArms.enabled = false`.

For the local owner path, `PlayerControllerB.cs:4219-4231` hides the third-person body from the camera by setting `thisPlayerModel.shadowCastingMode = ShadowCastingMode.ShadowsOnly` and enabling `thisPlayerModelArms.enabled = true`. For the non-local or restored third-person path, `PlayerControllerB.cs:4939-4951` sets `thisPlayerModel.shadowCastingMode = ShadowCastingMode.On` and `thisPlayerModelArms.enabled = false`.

`PlayerControllerB.cs:6162-6172` provides broader model visibility through `DisablePlayerModel(GameObject playerObject, bool enable, bool disableLocalArms = false)`, which iterates `playerObject.GetComponentsInChildren<SkinnedMeshRenderer>()` and assigns `componentsInChildren[i].enabled = enable`, skipping `thisPlayerModelArms` unless `disableLocalArms` is true.

The exact members used for local third-person hiding are therefore `thisPlayerModel.shadowCastingMode`, `thisPlayerModelArms.enabled`, and, for broader enable/disable calls, child `SkinnedMeshRenderer.enabled` through `DisablePlayerModel(...)`. The inspected `PlayerControllerB` evidence points to renderer shadow casting and renderer enabled state, not a model layer switch, as the local third-person hiding mechanism.

## 7. Runtime Pristine-Restore Baseline

`InteractionAnimationApiRestoreDiagnostics` captures the restore baseline once per
`PlayerControllerB` in a highest-priority prefix on `PlayerControllerB.Awake`. The child hierarchy
and serialized local TRS already exist at this point, while neither player scripts nor movement
clips have run. This timing makes the baseline movement-mod-aware without treating a continuously
animated first session sighting as pristine.

The captured scope is intentionally narrow: full local TRS for `spine.003/LeftArm_target`,
`spine.003/RightArm_target`, both `Rig 1/*Leg` groups and descendants, plus local rotation only for
`shin.L` and `shin.R`. Each live-body session still captures the same scope at entry as fallback.
Restore uses the spawn-authored baseline first and entry values only for missing targets.

The v81 prefab-rest constants in `RestoreDiagnostics.cs` are secondary sanity telemetry. The
`[RestoreSeam.tprig] pristine_capture_sanity` line reports threshold deltas but does not reject an
authored baseline that differs from vanilla. This replaces the invalid “first plausible live
sight” strategy: movement animation on the current modpack produced repeatable first-sight deltas
near `0.83 m` and `93.86` degrees, so the old rejection gate could never populate a pristine
baseline. An Awake capture the sanity check flags implausible is not permanent, however: for the
local player it is replaced at the first verified-clean idle session entry
(`TryRecapturePristineThirdPersonRigPoseIfImplausible`, logged as
`[RestoreSeam.tprig] rest_baseline_recaptured`); see `LC_INTERACTION_ANIMATION_API_V2.md` for
the gate conditions and the `Recapture Implausible Pristine Rig Baseline` kill-switch.
