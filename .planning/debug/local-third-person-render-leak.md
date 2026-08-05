---
status: verifying
trigger: "Intermittent local third-person custom-animation presentation shows an apparent overlapping body, giant helmet visor, and first-person-arm-like geometry; the same Kar98K animation can render normally later in the same run."
created: 2026-08-04
updated: 2026-08-04
---

# Local Third-Person Render Leak

## Symptoms

- expected: A local externally owned third-person camera renders one vanilla world body and hides the local first-person arms and helmet visor throughout a custom body animation.
- actual: The moon Kar98K presentation showed an apparent overlapping body, extra arms, and a giant helmet; a later ship presentation of the same animation looked normal.
- errors: No exception. The log records visor hard glue becoming active and then parking more than two metres from its target in both Kar98K third-person sessions.
- timeline: A notorious intermittent presentation defect returned in the 2026-08-04 run after the Kar98K Hold was rebuilt from its authored pose.
- reproduction: Enable the local external third-person camera while holding the Kar98K and enter its `y4ngz.kar98k.thirdperson` BodyWorld session, especially on a moon; repeat after returning to the ship.

## Current Focus

- hypothesis: Confirmed ownership gap: `localCameraOwnedExternally` disabled camera stabilizers but left both seam-time vanilla visor glue and session-time hard visor glue active. A separate visibility-state violation is still required to explain why only the moon episode displayed the visor/arms leak.
- test: The deployed candidate gates every presenter-owned visor capture/write for externally owned cameras and samples state-change-only `Application.onBeforeRender` body, first-person-arms, visor, camera-culling, and player/body instance invariants.
- expecting: External-camera sessions log `visor_glue_skipped`; render-time telemetry proves zero render-eligible local arms/visor renderers or identifies the exact renderer state when that invariant is violated.
- next_action: Run one Kar98K third-person presentation on a moon and one in the ship, then inspect `visor_glue_skipped` plus `external_camera_presentation_state` or `external_camera_presentation_invariant_failed`.
- reasoning_checkpoint: The API uses the existing player metarig and the deployed profile has one Interactions DLL. Both Kar sessions restored pristine rig controls, so duplicated player creation and persistent rig corruption are eliminated.
- tdd_checkpoint: Static regression coverage asserts external-camera gates for seam capture, seam reapply, hard glue, pre-render subscription lifecycle, and renderer-invariant markers. Release build is warning/error clean.

## Evidence

- timestamp: 2026-08-04
  observation: The presenter explicitly states that it drives the existing metarig with no duplicate rig or per-frame body copy.
  implication: The apparent second body must be overlapping renderer families or LOD presentation, not a second API-created player body.
- timestamp: 2026-08-04
  observation: Kar session `82712c35` logged `local_camera_owned_externally`, then `visor_glue_active`, then `visor_glue_parked` at 2.03 m; session `a3a6a801` repeated the sequence at 2.46 m.
  implication: Visor hard glue violates the external-camera ownership contract, although visibility state is additionally required to explain why only one episode looked bad.
- timestamp: 2026-08-04
  observation: The debug third-person camera forces body renderers visible and disables arms-only and visor renderers in its LateUpdate path, while Interactions writes visor position/rotation in `Application.onBeforeRender`.
  implication: Final render state crosses two owners and must be sampled after all LateUpdate writers.
- timestamp: 2026-08-04
  observation: Restore seam frame logging was disabled in the run and existing logs do not enumerate final body/arms/visor renderer states.
  implication: The exact visibility writer cannot be identified from the captured run; focused render-time invariant telemetry is required.
- timestamp: 2026-08-04
  observation: Source tracing found that `ReapplySeamVisorPose` performs a vanilla visor position glue at both Start and Stop in addition to the per-render hard glue.
  implication: The external-camera ownership correction must gate all seam and session visor writers, not only `StartLocalVisorHardGlue`.
- timestamp: 2026-08-04
  observation: Release build and four relevant static regression scripts pass; built and deployed SHA-256 are both `59EDAF4D25651610E643ADCCA6A040F023C7703CEA77DDE457F7B975B272C203`, with one profile DLL.
  implication: The candidate is deployed consistently and ready for runtime verification.
- timestamp: 2026-08-04
  observation: The Y4NGZDebugTools third-person camera force-enabled thisPlayerModel, LOD1, and LOD2 simultaneously every frame with shadows On, and never activated the local MoreCompany cosmetics (which MoreCompany spawns deactivated on the local player). Screenshots confirm three coincident shells z-fighting versus a clean single-LOD MoreEmotes third-person view.
  implication: The debug camera was not a faithful proxy for the remote-player view, and forced-on LOD stacking is a concrete candidate for the "overlapping body" visibility-state violation. Fixed in Y4NGZDebugTools: LOD0 only is forced visible, LOD1/LOD2 are held disabled for the session, and local cosmetics are activated and restored on exit.

## Eliminated

- hypothesis: Two copies of Y4NGZInteractions caused duplicate Harmony/static state.
  reason: Exactly one deployed `Y4NGZInteractions.dll` exists in the active Gale profile.
- hypothesis: The API instantiates a duplicate world-body rig.
  reason: `LiveBodyAnimatorPresenter` swaps the controller on the existing player `metarig` and creates no body clone.
- hypothesis: Kar98K clip scale curves create the giant helmet or a second body.
  reason: The Kar body clip is restricted to arm targets and finger rotations; the giant object follows the local visor lifecycle instead.

## Resolution

- root_cause: Interactions treated `localCameraOwnedExternally` as camera-transform ownership only. It still captured/reapplied the local visor at controller seams and subscribed hard visor glue before rendering, crossing the external presentation owner's visor transform/visibility boundary. The intermittent renderer-enablement half remains runtime-unverified.
- fix: External-camera sessions now skip visor seam capture, seam reapply, and hard glue. A generic, configurable pre-render probe logs body/arms/visor renderer details initially and only when eligibility changes; it does not impose consumer-specific visibility behavior.
- verification: Debug and Release builds succeed with 0 warnings/0 errors; API, viewmodel, Kinemation lookup, and Unity exporter static regressions pass; deployed hash matches the Release artifact and exactly one Interactions DLL is present. Runtime moon/ship acceptance is pending.
- files_changed: `InteractionAnimationManifest.cs`, `LiveBodyAnimatorPresenter.cs`, `RestoreDiagnostics.cs`, API V2 static regression script, and this debug record. Living docs and the single dated handoff are intentionally deferred until the live-iteration verdict.
