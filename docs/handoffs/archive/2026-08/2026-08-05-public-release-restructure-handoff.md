# 2026-08-05 — Public release restructure handoff

## State

- **DEPLOYED / STATIC-VERIFIED / LIVE-UNVERIFIED:** the API repo was
  restructured into a pure consumer-agnostic library and prepared for
  public release. Consumer payloads (drone tablet, revolver, heavy
  shotgun) migrated to Y4NGZUpgrades (`runtime-assets/` there; byte
  checksums verified before the API-side copies were deleted). The API's
  profile folder now contains only `Y4NGZInteractions.dll` + `.pdb`.
- `FieldOperationsTabletPatch.cs` (Upgrades) now loads the drone-tablet
  manifest from the Upgrades plugin folder and passes `AssetRootPath`
  (previously read from the API's plugin folder by hardcoded name). Both
  repos build clean (0 warnings). **No in-game session has exercised the
  new asset root yet.**
- Guard exemptions are now manifest-declared (`exemptFromCameraDisplacementGuard`,
  `exemptFromSpecialAnimationAutoStop` root bools); config lists remain as
  a user override and their defaults are empty. Company's
  `y4ngz-cctv-operator.manifest.json` carries both flags (repo + deployed
  copy). **Unverified in game.**
- API surface decoupling: viewmodel manifest `bundleFileName`/`prefab` are
  required (tablet defaults removed), `sockets.prop` supersedes
  `sockets.tablet` (deprecated alias retained), debug probe is
  config-driven and off by default, the `DroneTablet_FirstPersonArms_`
  slot-prefix fallback is gone. A regression guard now fails the static
  suite if consumer literals (or `C:\Users\` paths) appear in `src/`.
- `tools/unity/` deleted (stale mirrors; live tools are in the Unity
  authoring projects; per-asset knowledge extracted to
  `Y4NGZUpgrades/docs/INTERACTIONS_API_EXTRACTED_ASSET_NOTES_2026-08-05.md`).
  `runtime-assets/` untracked; the dev-only dronetablet-viewmodel bundle
  files remain on disk, ignored.
- Docs: new external set (root `README.md`, `docs/GETTING_STARTED.md`,
  `docs/MANIFEST_REFERENCE.md`, `docs/API_REFERENCE.md` with a 75-reason
  failure catalogue) plus `docs/internal/RESTORE_SEAM_INTERNALS.md`
  (split from the deleted `LC_INTERACTION_ANIMATION_API_V2.md`).
  `docs/superpowers/` and the contradictory authoring handoff were
  deleted. `CHANGELOG.md` added; version bumped to 0.3.0 (plugin +
  csproj). MIT `LICENSE` added. Maintainer name and local user paths
  scrubbed from all tracked files.
- Publication plan (decided): fresh single-commit public history on
  `main`, pushed to `github.com/y4ngz313/Y4NGZInteractions`; the full
  pre-public history stays on local archive branches that are never
  pushed (the old history contains a 102 MB bundle blob and decompiled
  sources and cannot go public).
- The Kar98K third-person render-leak fix (committed earlier today) is
  still `status: verifying` — one moon + one ship acceptance run pending
  (`.planning/debug/local-third-person-render-leak.md`).

## Open questions

- In-game verification pending for: drone-tablet inspect via the new
  Upgrades asset root; a revolver and a heavy-shotgun session
  post-migration; CCTV operator guard exemptions on a fresh cfg (existing
  cfg still lists the ID, masking regressions).
- `body.suppressRigBuilders` defaults to `true` in C#, so a manifest that
  omits it suppresses rig builders — opposite of what every shipped
  manifest sets. Flip-the-default decision not made (documented as a
  footgun in MANIFEST_REFERENCE).
- Dead schema fields (`frameRate`, `body.clip`, `localViewmodel.root`,
  `validation.*`, `sockets.leftHand/rightHand` validated-but-unread) are
  documented as inert; whether to cut them before more consumers exist is
  undecided.
- `PresentationKind.Hybrid` presents as DedicatedLocalViewmodel;
  documented as reserved. Presenter/backend extension points remain
  internal; no third-party presenter seam exists yet.
- Upgrades and Company carry the staged migration edits alongside large
  pre-existing uncommitted work on their own branches; those repos'
  commit decisions are separate.
- Thunderstore 0.3.0 package not yet staged/uploaded (stager is now a
  pure-library packager; consumer mods must ship their own payloads
  before their next Thunderstore updates if users install from
  Thunderstore rather than local profiles).
