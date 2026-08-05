# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
While the version is below 1.0.0 the public API surface may still change; breaking
changes are called out in this file.

## 0.3.0 - 2026-08-05

First public release. MIT licensed; source at
<https://github.com/y4ngz313/Y4NGZInteractions>.

### Changed

- **The package is now a pure library.** All consumer animation payloads (bundles,
  manifests, prop prefabs) and the authoring tooling that produced them were moved out
  to the consuming mods. Consumers ship their own content in their own plugin folder and
  point `InteractionAnimationPackDefinition.AssetRootPath` at it.
- **Consumer-specific defaults removed from the manifest schema.** `localViewmodel.bundleFileName`
  and `localViewmodel.prefab` no longer carry built-in default values and are now required
  for `DedicatedLocalViewmodel` / `Hybrid` interactions; an empty value fails validation with
  `manifest_viewmodel_bundle_file_empty` / `manifest_viewmodel_prefab_empty`.
- **Guard exemptions are now declared by the manifest.** New root booleans
  `exemptFromCameraDisplacementGuard` and `exemptFromSpecialAnimationAutoStop` let an
  interaction declare its own exemption. The two config lists remain as an operator
  override and are honoured on top of the manifest flags, but their defaults are now empty
  (previously they shipped with a hardcoded interaction id).
- **`sockets.prop` supersedes `sockets.tablet`.** `tablet` is kept as a deprecated alias so
  manifests authored against the original schema keep loading; the runtime reads whichever
  is set, preferring `prop`.
- **The Page Down debug probe is config-driven and inactive by default.** Its pack id,
  interaction ids, manifest file names, and animator parameter names all come from config
  and are empty by default, so a released package registers no probe payload of its own.

### Added

- MIT `LICENSE`, public README, and the consumer-facing documentation set
  (`docs/GETTING_STARTED.md`, `docs/API_REFERENCE.md`, `docs/MANIFEST_REFERENCE.md`).

## 0.2.0 - 2026-07-30

Internal release.

## 0.1.0 - 2026-06-24

Internal release.
