# 2026-07-16 â€” API Solidification Pass: Research & Plan

Session brief for the Y4NGZInteractions solidification pass. Fable = orchestrator/researcher;
Codex (5.6 Sol xHigh) agents implement the work packages below in later sessions. This document
is STATE (verified findings), ranked HYPOTHESES with discriminating experiments, and work
packages â€” the fix mechanisms are candidates gated on instrumentation evidence, not decisions.

Goals of the pass (the maintainer, 2026-07-16):
1. Establish and document what the mod actually does today.
2. Assess standalone-ness / legitimacy as a public API for other modders.
3. Fix the constant post-animation frame stutter (black frame OR 1â€“2-frame viewpoint pop-up).
4. Fix the subtle post-animation breakage of vanilla first-person arms (45Â° bends while
   running, wrong arm angles pulling the ship throttle).

---

## 1. What Y4NGZInteractions is today (verified against source, 2026-07-16)

Single BepInEx plugin `Y4NGZInteractions.dll` (`com.y4ngz.interactions`, v0.1.0). `Plugin.Awake`
initializes two independent modules; `Plugin.LateUpdate` ticks both.

### Module A â€” PlayerAnimationApi ("v1", namespace `LCPlayerAnimationAPI`)
`LCPlayerAnimationAPI` (RegisterPack / lease / arbiter), `FirstPersonInteractionPresenter`
(4,527 lines, renderer-bone-remap presenter, hardcoded drone-tablet section ~L2447â€“2634),
`PlayerAnimationApiDebugProbe` (8,794 lines), Harmony patches, diagnostics API.

**Zero external consumers.** Grep of Y4NGZUpgrades, Y4NGZCompany, Y4NGZMonsters finds no call
into `LCPlayerAnimationAPI`; the only user of `FirstPersonInteractionPresenter` is the module's
own debug probe. v1 + its probe â‰ˆ 13.5k of the repo's ~20k source lines.

### Module B â€” InteractionAnimationApi ("V2") â€” the production path
- `LCInteractionAnimationAPI` static surface (register pack / start / stop / graceful exit /
  parameter passthrough / preload) â€” documented in `LC_INTERACTION_ANIMATION_API_V2.md`.
- `InteractionAnimationCoordinator`: one `BodyWorld` session per player, preemption with full
  restore, fail-closed when a non-API controller owns `playerBodyAnimator`.
- `LiveBodyAnimatorPresenter` (production): shell controller + `AnimatorOverrideController`
  clip pack on the real `playerBodyAnimator`; RigBuilder rebuild after every controller swap;
  manifest-driven prop attach, movement param, layer ramps, exit fades, camera guards.
- `LocalViewmodelPresenter`: diagnostics only.
- `InteractionAnimationAssetPathResolver` (uncommitted, in flight): consumer `AssetRootPath`
  confinement â€” the enabler for consumer-owned assets.

### Verified session lifecycle (LiveBodyAnimatorPresenter)
- **TryStart**: capture scoped FP pose (opt-in) â†’ camera baseline (+ opt-in stabilizer) â†’ load
  bundles â†’ `AnimatorStateSnapshot.Capture` â†’ apply shell/override controller â†’ activeBool +
  enterTrigger + `Update(0)` â†’ rig rebuild (or opt-in suppress) â†’ `Update(0)` â†’ attach prop.
- **Tick** (from `Plugin.LateUpdate`): ownership-loss yield, 1.25 m camera displacement guard,
  layer-weight ramp / exit fade, movement int param, auto-stop on
  `inSpecialInteractAnimation`/ladder/death, 0.25 s diagnostics.
- **Stop**: destroy prop â†’ `snapshot.Restore` (expected-controller guard; `Animator.Rebind()`
  unless scoped restore; replay params/layer weights/`Play(stateHash, t)`; `Update(0)`) â†’
  scoped FP pose restore (opt-in) â†’ re-enable suppressed RigBuilders â†’
  `RigBuilder.Build()` on every rig builder (reflection) â†’ `Update(0)` â†’ camera stabilizer
  release after 2 LateUpdates (**only if the manifest opted in**) â†’ release/retain bundles.

### Consumers (verified by grep, 2026-07-16)
| Consumer | Linkage | Uses |
|---|---|---|
| Y4NGZUpgrades `WeaponFirstPersonAnimation` | compile-time `ProjectReference` (`Private=false`) | heavy shotgun + revolver FP (start/exit/triggers/preload) |
| Y4NGZUpgrades `FieldOperationsTabletPatch` | same | drone tablet (start/exit/param passthrough) |
| Y4NGZUpgrades `Lucky8ButtonPressAnimation`, `WorklightBeaconPatch` | same | one-shot / toggle interactions |
| Y4NGZUpgrades `LedgeMantlePatch`, `PanicSlidePatch` | same | yield protocol only (`TryStopPlayerInteractions` + `IsPlayerInteractionActive`) |
| Y4NGZCompany `BundyVictimInteractionsBridge` | **reflection soft-dep** (`FindType` by name, plugin-path probing) | victim interactions |
| Y4NGZCompany `Y4NGZPlayerAnimationBridge` (CCTV) | none â€” **direct controller swap** ("direct owner" under the ownership contract) | its own Scanvan-style swap; must keep honoring the yield protocol |
| Y4NGZMonsters | no references found | â€” |

Note: AGENTS.md lists Monsters as a consumer today â€” not true at source level; correct it
during the docs work package.

---

## 2. Standalone-ness & public-API assessment

**Code: fully self-contained.** csproj references only BepInEx, HarmonyX,
`LethalCompany.GameLibs.Steam`, UnityEngine modules, and the game's `Unity.InputSystem`. No
compile- or runtime-time dependency on Company/Upgrades. Company's bridge is soft (reflection);
Upgrades hard-depends on Interactions, not the reverse. Standalone install = no errors, two
debug probes and a dormant API.

**Content: mostly migrated already** (verified 2026-07-16, corrects the AGENTS.md debt note):
- **Weapons are done.** Y4NGZUpgrades keeps its own copies of the shotgun/revolver (+ crossbow,
  flamethrower) bundles in its `runtime-assets/`, deploys them with its own plugin, and
  registers packs with `AssetRootPath`. The shotgun/revolver copies still in THIS repo's
  `runtime-assets/` + csproj `<Content>`/deploy are **stale duplicates** â€” delete them (same
  shape as the already-deleted heavy pistol files); stale-version risk while two same-named
  copies deploy to different plugin folders.
- **Drone tablet is the remaining true debt.** `FieldOperationsTabletPatch` reads its manifest
  from `Paths.PluginPath\y4ngz-Y4NGZInteractions\` explicitly; the 4 tablet files ship with
  Interactions. Migration = move files to Upgrades' runtime-assets + register with
  `AssetRootPath`, then strip the csproj Content/deploy entries here.
- v1's hardcoded drone-tablet presenter block (goes away with v1 deletion, below).
- v1 probe bundle in runtime-assets (same).

**Would other modders legitimately want this?** Yes â€” the IK-target contract (vanilla FP arms
are IK-target driven; FK is stomped) cost weeks to discover and is not served by any public
mod API; the live-body presenter + snapshot restore + ownership arbitration is generic. What a
third party is missing today, in dependency order:
1. The two bug fixes below (a public API cannot ship a visible restore seam).
2. Content decoupling (tablet migration + stale weapon duplicates deleted, per above).
3. v1 deletion (decided, see Â§7).
4. Authoring on-ramp: `ANIMATION_AUTHORING_PIPELINE.md` assumes this repo's Unity project and
   baker tools. **Constraint (the maintainer 2026-07-16): the guide must NOT assume the Y4NGZ
   retargeting workflow** â€” external modders won't have it. The public contract is only:
   an AssetBundle with a shell controller + clips whose FP-arm content animates the IK-target
   transforms, a manifest JSON, and `AssetRootPath` registration. Clips can be authored
   natively in Unity (keyframe the target/hint transforms on a rig replica, or bake from any
   pipeline of their own); our FBX retargeter is one producer among many, not part of the API.
5. Packaging: semver + frozen public surface, Thunderstore package, `BepInDependency` +
   reflection soft-dep recipe (Bundy bridge is the reference implementation).
The already-planned dogfood session (port one weapon as an external consumer) is the right
friction test and should follow the fixes.

---

## 3. Bug 1 â€” end-of-animation frame stutter (black frame / viewpoint pops up 1â€“2 frames)

**Symptom** (maintainer verdict): constant across all custom animations, directly after the last frame:
sometimes a fully black frame, sometimes the viewpoint moves up for 1â€“2 frames.

**Verified mechanics that can produce exactly this:**
- `snapshot.Restore` calls `Animator.Rebind()`, which resets every animator-bound transform â€”
  including the camera-container transform that vanilla full-body clips animate (the repo's own
  curve-binding audit rule exists because clips CAN bind the camera container). A rendered
  frame that observes the rebind pose puts the camera at default/bind height â†’ viewpoint-up;
  camera inside head/body mesh â†’ black frame (near-plane inside geometry).
- The restore runs at a **nondeterministic frame phase**: coordinator auto-stops run inside
  `Plugin.LateUpdate` (BepInEx plugin â€” order vs `PlayerControllerB.LateUpdate` undefined),
  while consumer-initiated `TryStopInteraction` runs from whatever phase the consumer calls in
  (input handlers = `Update`). Whether vanilla camera positioning happens before or after the
  restore varies â†’ the two different symptoms.
- `RigBuilder.Build()` runs AFTER the snapshot has replayed state and evaluated; if `Build()`
  internally rebinds/rebuilds the playable graph (Animation Rigging does reconstruct the
  graph), the replayed state may be discarded, and the final `Update(0)` evaluates a default
  state for that frame.
- The only mitigation â€” `LocalCameraPositionStabilizer`, which pins camera position and
  survives 2 LateUpdates after restore precisely because "immediate destruction exposed a
  final-frame snap" (decisions log 2026-07-11/12) â€” is **opt-in and OFF for weapons, tablet,
  and CCTV**, i.e. off for everything the maintainer plays. The seam is a known, partially-guarded
  hole that production content never got the guard for.

**Ranked hypotheses**
- H1 (high): a rendered frame observes the Rebind/default pose because restore phase vs
  vanilla LateUpdate vs render is unordered.
- H2 (medium): `RigBuilder.Build()` at restore discards the replayed animator state; one frame
  evaluates the wrong state even when phase ordering is lucky.
- H3 (high, compounding): no camera pin during restore for production content.

**Discriminating instrumentation (WP1)**
- Frame-indexed restore-seam logger at `[DefaultExecutionOrder(32000)]` LateUpdate: for 5
  frames around Stop, log camera world pos, camera-container local pose, current state hash +
  normalizedTime per layer, `Time.frameCount`, and which phase invoked Stop. One run per
  symptom variant (black frame vs pop-up).
- A/B: force the stabilizer on (restore-scoped, 2 LateUpdates) for a weapon via config knob â€”
  if the pop-up disappears but black frame remains, H1 splits from H2.
- Verify H2 directly: log state hash immediately before and after `RigBuilder.Build()`.

**Fix candidates (choose from evidence, not in advance)**
a. Deterministic restore phase: defer the actual restore work to a late-LateUpdate runner
   (execution order 32000) so restore always happens after vanilla writes and completes â€”
   evaluated, rig rebuilt, camera pinned â€” before render.
b. Always-on restore-scoped camera position pin (2 LateUpdates), independent of the
   full-session `stabilizeLocalCameraPosition` opt-in.
c. Reorder restore: set controller â†’ `RigBuilder.Build()` â†’ replay snapshot params/states â†’
   single final `Update(0)` (eliminates H2 by construction).
Acceptance: the maintainer cannot tell the animation was custom from the ending, across shotgun,
revolver, tablet, lucky8, worklight; both symptom variants gone; regression scripts pass.

---

## 4. Bug 2 â€” vanilla FP arms subtly broken after any custom animation

**Symptom** (maintainer verdict): after using any custom animation, vanilla FP animations degrade: running
arms bent at weird ~45Â° angles, ship-throttle pull arm bent wrong. Constant across animations.

**Verified mechanics + hypotheses:**
- The FP arms are two-bone-IK driven from targets; elbows come from hint transforms and
  constraint data. Vanilla clips animate the **targets**; the hint objects and rig parents are
  scene-authored transforms vanilla code never re-writes.
- H1 (high): `Animator.Rebind()` resets animator-bound/skeleton transforms to their **default
  (import) pose**, which for scene-authored rig control objects (IK hints, `RigArms` parents,
  target rest offsets) can differ from the scene values the vanilla rig depends on. Nothing in
  the restore path puts them back (the whole-tree scoped restore exists but is opt-in and
  Worklight-only). Wrong hint position = wrong elbow plane = 45Â° bends â€” strongest match for
  the symptom, and explains why EVERY custom animation triggers it (every restore rebinds).
- H2 (medium): `RigBuilder.Build()` at restore re-initializes constraint state (e.g.
  maintained target/hint offsets captured at build time) while the skeleton is mid-restore /
  non-pristine, baking a small error into the rebuilt rig. Note vanilla builds its rig once at
  spawn from the pristine pose; this API is the only thing that ever rebuilds it â€” twice per
  session, from arbitrary poses.
- H3 (low): residual parameters/layer weights â€” unlikely, snapshot replays them.

**Discriminating instrumentation (WP1, the decisive experiment)**
Pristine-rig diff probe: at first sight of the local player (pristine, pre-any-custom-anim),
capture the full arms rig control subtree â€” `RigArms` and descendants (targets, hints), plus
each IK constraint component's data fields via reflection (target/hint refs, maintain-offset
flags, weights). Hotkey dumps a diff of current vs pristine. Capture sequence: (1) pristine,
(2) immediately after a custom-animation restore, (3) while the throttle/running arms look
wrong. The diff names the exact transforms/fields responsible â€” no guessing. (CCTV lesson
2026-07-16: instrument first; 14 tests were fooled by an assumed defect site.)

**Fix candidates**
a. Pristine rig-control restore: capture the rig-control subtree (targets/hints/rig parents â€”
   NOT the animated arm bones) once at session start; after Rebind, restore it before the rig
   rebuild. This is the `scopedFirstPersonTransformRestore` idea generalized to the correct
   transform set and made always-on.
b. If H2 confirmed: rebuild the rig only from a verified-pristine pose (restore transforms
   first, then Build), or restore captured constraint data after Build.
c. If Rebind itself is the sole culprit and (a) fully covers it, consider dropping Rebind for
   a targeted bound-transform restore â€” only with regression evidence on the 2026-07-11
   floating-arms bug Rebind was added to fix.
Acceptance: after 5+ start/stop cycles across different packs, throttle pull and running-sway
arm poses are transform-identical (probe diff empty) to a never-animated control session.

---

## 5. Work packages (Codex 5.6 Sol xHigh; dispatch from this repo's root â€” sandbox is cwd-keyed)

- **WP0 â€” vanilla reference extraction** (research, no repo changes): decompile
  `Assembly-CSharp.dll` (game Managed dir; ilspycmd) â†’ document `PlayerControllerB`
  Update/LateUpdate camera positioning, camera-container ownership, ship-lever special-anim
  path, and spawn-time rig initialization. Output: short reference doc feeding WP2/WP3
  hypothesis confirmation. No decompiled source currently exists on disk.
- **WP1 â€” instrumentation** (code, zero behavior change): restore-seam frame logger +
  pristine-rig diff probe + stabilizer force-on config knob, behind the existing debug-probe
  config section. Acceptance: builds Release, regression scripts pass, logs captured in one
  the maintainer playtest covering both bugs.
- **WP2 â€” bug 1 fix** (blocked by WP1 evidence): implement the evidence-selected candidate
  from Â§3. Acceptance criteria in Â§3.
- **WP3 â€” bug 2 fix** (blocked by WP1 evidence; likely shares mechanism/ordering with WP2 â€”
  implement as one round if the evidence says so). Acceptance criteria in Â§4.
- **WP4 â€” documentation**: update `LC_INTERACTION_ANIMATION_API_V2.md` restore-lifecycle
  section to match the fixed behavior; record the decision in `ANIMATION_API_DECISIONS.md`;
  correct AGENTS.md consumer list (Monsters); document the CCTV direct-owner pattern and the
  Bundy reflection soft-dep as the two consumer integration recipes.
- **WP5 â€” v1 archival + deletion** (decided 2026-07-16, can run parallel to WP1): move the
  PlayerAnimationApi module + its debug probe + `LC_PLAYER_ANIMATION_API.md` +
  `test-player-animation-api-static-regressions.ps1` + the v1 probe bundle into `_archive/`
  (kept in-repo, out of the build), delete the wiring from `Plugin.cs`. **Prerequisites:**
  (1) re-verify zero references from ALL sibling repos incl. Diagnostics/Content/scripts, not
  just the three checked; (2) `AnimatorStateSnapshot` (and anything else in the
  `LCPlayerAnimationAPI` namespace v2 uses â€” LiveBodyAnimatorPresenter imports it) moves into
  InteractionAnimationApi first; (3) Release build + remaining regression scripts pass.
- **WP6 â€” public-API track** (existing planned dedicated sessions, unchanged order): tablet
  content migration + stale weapon-duplicate deletion on the AssetRootPath resolver; docs
  consolidation (2026-07-10 brief); external-modder authoring guide (no-retargeting
  assumption, see Â§2) + minimal sample consumer; dogfood weapon port; then
  packaging/versioning.

Suggested order: WP0+WP1+WP5 (parallelizable) â†’ the maintainer playtest with probes â†’ WP2/WP3
(possibly one round) â†’ WP4 â†’ WP6 sessions.

## 6. Open questions (maintainer verdict)

1. **Playtest cadence** â€” WP1â†’WP3 want live-iteration mode (probe capture + fix verdicts).
   No code changes exist yet as of this brief; the probes must be implemented (WP1) and
   deployed before the capture playtest.

## 7. Decisions (the maintainer, 2026-07-16)

- **v1 PlayerAnimationApi: DELETE, archived in-repo under `_archive/`** (it is genuinely
  unused â€” only its own debug probe calls it). See WP5.
- **In-flight uncommitted work: commit approved** (AssetRootPath resolver + heavy-pistol
  asset removal + doc edits). Done this session.
- **CCTV bridge stays a direct owner permanently** â€” document the pattern (WP4), no API
  migration planned.
- **Authoring guide must not assume the Y4NGZ retargeting workflow** (Â§2 item 4).
