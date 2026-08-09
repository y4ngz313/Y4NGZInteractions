# Troubleshooting

Last updated: 2026-08-08

Begin with ValidateInteractionPack or ValidateInteractionManifest. Use the issue Code and JsonPath before reading runtime logs.

## Registration and schema

| Code | Meaning |
| --- | --- |
| pack_id_empty | PackId is missing or untrimmed |
| pack_version_empty | Version is missing or untrimmed |
| pack_asset_root_missing | AssetRootPath is missing or does not exist |
| pack_interactions_empty | No interaction definitions were supplied |
| interaction_id_duplicate | Two definitions use the same id |
| manifest_schema_version_unsupported | schemaVersion is not 1 or 2 |
| manifest_unknown_field | Strict schema 2 found an undeclared field |
| manifest_interaction_id_mismatch | JSON and registration ids differ |
| manifest_schema_1_migrated | Accepted legacy JSON should be upgraded |

The full report contains all discovered issues. Registration's out reason contains the first error code.

## Start and ownership

| Code | Meaning |
| --- | --- |
| interaction_resource_busy | RejectIfBusy found an occupied lease |
| dedicated_viewmodel_requires_local_player | A remote player was requested for local-only presentation |
| player_animator_owned_externally | Another system currently owns the body controller |
| missing_body_animator | The target has no body animator |
| expected_player_animator_controller_missing | Vanilla ownership could not be established |
| presenter_preflight_exception | Presenter preflight threw |
| presenter_start_exception | Presenter start threw |

Use InterruptExisting only when the new interaction should replace another local presentation. It does not override external animator ownership.

## Asset and authoring failures

- Confirm AssetRootPath exists on every client.
- Confirm bundleFileName is relative to that root and uses forward slashes.
- Confirm prefabAssetName and controllerAssetName match the AssetBundle exactly.
- Confirm cameraAnchorPath and renderer paths are prefab-relative.
- Confirm BodyWorld bindings match the rig reference.
- Run the Unity validators for controllers, layers, parameters, anchors, renderers, props, and clip bindings.

## Networking symptoms

If one client sees an interaction and another does not, the API is usually working as designed: it is local-only. The consuming mod must send a stable player/network identity and interaction fact to each observing client. Each observer resolves its local PlayerControllerB and calls TryStartInteraction.

Do not send InteractionAnimationHandle over the network. Handles are local process identities.

## Restoration symptoms

The API restores before InteractionEnded. If state still drifts:

1. reproduce with the repository sample;
2. confirm only one API DLL is loaded;
3. enable the narrow diagnostic option relevant to the seam;
4. record start, stop, and post-stop state;
5. test explicit stop, natural end, interruption, death, ladder, and round unload;
6. report the manifest, presentation kind, stop reason, and exact reproduction cycle.

Verbose rig/camera probes are off by default. Manual hotkeys live in the sample plugin, not the production DLL.
