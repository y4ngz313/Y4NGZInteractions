# API reference

Last updated: 2026-08-05

The public runtime surface of Y4NGZInteractions, for mods that consume it. Signatures are taken
from `src/Y4NGZInteractions/InteractionAnimationApi/LCInteractionAnimationAPI.cs` and
`InteractionAnimationTypes.cs`.

For a walkthrough see [GETTING_STARTED.md](GETTING_STARTED.md); for the manifest schema see
[MANIFEST_REFERENCE.md](MANIFEST_REFERENCE.md). Maintainer-facing detail about the restore seam
and the diagnostic config lives in [internal/RESTORE_SEAM_INTERNALS.md](internal/RESTORE_SEAM_INTERNALS.md).

## `LCInteractionAnimationAPI`

Static class in namespace `Y4NGZInteractions.InteractionAnimationApi`.

```csharp
public static bool IsInitialized { get; }

public static bool TryRegisterInteractionPack(
    InteractionAnimationPackDefinition pack, out string reason);

public static bool TryStartInteraction(
    InteractionAnimationRequest request,
    out InteractionAnimationHandle handle,
    out string reason);

public static bool TryPreloadInteractionAssets(
    string packId, string interactionId, out string reason);

public static bool TryStopInteraction(
    InteractionAnimationHandle handle, InteractionAnimationStopReason stopReason);

public static bool TryBeginInteractionExit(
    InteractionAnimationHandle handle, out string reason);

public static bool IsInteractionActive(InteractionAnimationHandle handle);

public static bool IsPlayerInteractionActive(GameNetcodeStuff.PlayerControllerB player);

public static bool TryStopPlayerInteractions(
    GameNetcodeStuff.PlayerControllerB player, InteractionAnimationStopReason stopReason);

public static bool TrySetInteractionBool(
    InteractionAnimationHandle handle, string parameterName, bool value);

public static bool TrySetInteractionInt(
    InteractionAnimationHandle handle, string parameterName, int value);

public static bool TryFireInteractionTrigger(
    InteractionAnimationHandle handle, string parameterName);
```

### `IsInitialized`

True once the plugin has built its coordinator during `Awake`. With a hard `BepInDependency` on
`com.y4ngz.interactions` you can assume it; a soft dependency should check it.

### `TryRegisterInteractionPack`

Validates the pack, normalises `AssetRootPath`, and stores it under `PackId`. A pack id may be
registered only once per session (`pack_already_registered`); there is no unregister. Manifests
are *not* parsed here — that happens at preload or start.

### `TryStartInteraction`

Validates the request, finds the interaction, parses and validates the manifest, and for
`BodyWorld` acquires the player's body animator before creating the session. Returns a handle on
success and `InteractionAnimationHandle.Empty` on failure.

Acquisition for `BodyWorld` does two things worth knowing:

- **Pre-emption.** Any existing API-owned `BodyWorld` session on the same player is stopped
  (`Interrupted`) and fully restored first, then the new one takes ownership. A player therefore
  has at most one live-body session at a time. Dedicated viewmodels are not part of this lock.
- **External ownership check.** If the player's animator is running something that is neither
  vanilla's expected local/remote controller nor an API session, the start is refused with
  `player_animator_owned_externally` rather than overwriting another mod's work.

Call it on every client that should see the animation — the API replicates nothing. See
[the networking contract](GETTING_STARTED.md#the-networking-contract).

### `TryPreloadInteractionAssets`

Loads and deserialises an interaction's assets ahead of gameplay so the first start does not
hitch. For `BodyWorld` this loads the controller bundle, the clip-pack bundle, every override
clip, and the prop prefab, and verifies each one exists; the bundles stay cached until the API
shuts down. For the viewmodel kinds it starts an asynchronous bundle load (and is the only way to
use a viewmodel bundle larger than 16 MB).

### `TryStopInteraction` and `TryBeginInteractionExit`

`TryStopInteraction` stops immediately: the presenter restores this frame. It returns `false` if
the handle is not an active session. It takes an `InteractionAnimationStopReason` purely for
logging; the restore path is the same for every value except `Shutdown`, which also unloads
retained bundles.

`TryBeginInteractionExit` is the graceful form: it clears the manifest's `activeBool`, fires the
`exitTrigger`, fades the custom layer weights to zero, and schedules the stop for `exitSeconds`
later. It returns `false` with `interaction_not_active` for an unknown handle; note that it
returns `true` even when the interaction has no exit animation configured (the effective wait is
then zero).

### `IsInteractionActive`, `IsPlayerInteractionActive`, `TryStopPlayerInteractions`

`IsInteractionActive` is a handle-level query. `IsPlayerInteractionActive` answers "does the API
currently own this player's body animator" — **`BodyWorld` sessions only** — and is what other
animation code should poll before swapping `playerBodyAnimator.runtimeAnimatorController` itself.
`TryStopPlayerInteractions` immediately stops and restores every `BodyWorld` session on that
player and returns whether it stopped anything; re-check `IsPlayerInteractionActive` afterwards
before taking ownership.

### Parameter passthrough

```csharp
LCInteractionAnimationAPI.TrySetInteractionBool(handle, "MyMod_Lantern_Raised", true);
LCInteractionAnimationAPI.TrySetInteractionInt(handle, "MyMod_Lantern_ActionIndex", 2);
LCInteractionAnimationAPI.TryFireInteractionTrigger(handle, "MyMod_Lantern_Action");
```

All three return a plain `bool` with no reason string. `false` means the handle is not an active
session, or the applied controller has no parameter with that exact name **and** that exact type.
Triggers are reset before being set, so repeated fires are reliable. `Float` parameters have no
public setter today.

## Types

### `InteractionAnimationPackDefinition`

| Member | Type | Notes |
|---|---|---|
| `PackId` | string | Required, unique, no leading or trailing whitespace. Case-insensitive lookups. |
| `DisplayName` | string | Informational. |
| `Version` | string | Informational. |
| `Author` | string | Informational. |
| `AssetRootPath` | string | Your plugin's directory. Optional in type, effectively required in practice — see below. |
| `Interactions` | `InteractionAnimationDefinition[]` | At least one; ids must be unique within the pack. |

`AssetRootPath` is normalised to a full path at registration and must exist
(`pack_asset_root_missing`). Every manifest bundle name then resolves beneath it, and anything
that escapes the root is rejected (`asset_bundle_path_escapes_root`) — including a bundle reused
from the loaded-bundle cache, which is checked against the root before the cache is consulted.

If you omit it, resolution falls back to the API's own roots, in order: the API assembly's
directory, `BepInEx/plugins/y4ngz313-Y4NGZInteractions`, `BepInEx/plugins/y4ngz-Y4NGZInteractions`,
the plugins root, and the application base directory. Your bundles are in none of those. Always
set it.

### `InteractionAnimationDefinition`

| Member | Type | Notes |
|---|---|---|
| `InteractionId` | string | Required, unique in the pack, no outer whitespace. Must match the manifest's `interactionId`. |
| `DisplayName` | string | Informational. |
| `ExpectedDurationSeconds` | float | Greater than zero overrides the manifest's `durationSeconds`. Zero defers to the manifest. |
| `PresentationKind` | `InteractionAnimationPresentationKind` | Defaults to `DedicatedLocalViewmodel` — set it explicitly. |
| `ManifestJson` | string | The manifest text itself, not a path. Read it with `File.ReadAllText` or embed it. |

### `InteractionAnimationRequest`

| Member | Type | Notes |
|---|---|---|
| `Player` | `PlayerControllerB` | Required. Local or remote. |
| `PackId` | string | Required. |
| `InteractionId` | string | Required. |
| `OwnerModId` | string | Informational; identifies the caller in logs. |

### `InteractionAnimationHandle`

`readonly struct` wrapping a `Guid`, with `Equals`, `GetHashCode`, `==`, `!=`, and `ToString`.
`InteractionAnimationHandle.Empty` is the failure value; `IsValid` is false for it.

Lifetime: a handle identifies exactly one session. It is created by `TryStartInteraction`, is
never reused, and is permanently dead once that session stops — whether you stopped it, it timed
out, or it was interrupted. There is no callback when a session ends, so a consumer holding a
handle across frames should either poll `IsInteractionActive(handle)` or track its own state and
treat every parameter call returning `false` as "the session is gone". Handles are local to the
process that created them and must never be sent over the network.

### `InteractionAnimationPresentationKind`

| Value | Meaning |
|---|---|
| `DedicatedLocalViewmodel` | Consumer-owned prefab parented to the local gameplay camera. Local-only; invisible to other clients. Does not participate in the player-body ownership lock. |
| `BodyWorld` | The production path. Swaps the controller on `playerBodyAnimator`, so the animation is seen in first person and by other clients in third person. One per player at a time. |
| `Hybrid` | Validated exactly like `DedicatedLocalViewmodel` and currently presented by the same viewmodel presenter. It is a reserved name for a future combined path — do not expect body-world behaviour from it. |

### `InteractionAnimationStopReason`

| Value | Raised by |
|---|---|
| `Requested` | A consumer calling `TryStopInteraction` / `TryStopPlayerInteractions`. |
| `NaturalEnd` | The coordinator, when the effective duration elapsed or a graceful exit's wait completed. |
| `Interrupted` | Pre-emption by a new session on the same player; the presenter's own auto-stop (player died, started climbing a ladder, entered a vanilla special animation, the applied controller was replaced, or the camera displacement guard tripped). |
| `Failed` | Internal: a presenter failed to start after a backend had started. |
| `Shutdown` | Plugin teardown. Also unloads bundles retained for reuse. |

### `InteractionAnimationValidationResult`

`IsValid` plus a `Reason` string, with static `Valid()` / `Invalid(reason)` factories. Returned by
`InteractionAnimationManifest.Validate`, which you can call yourself if you want to validate a
manifest before registering it.

### `InteractionAnimationManifest`

The manifest classes are public and `[Serializable]`, so you can build a manifest in code and
`JsonUtility.ToJson` it instead of shipping a file:

```csharp
public static bool TryParse(string json, out InteractionAnimationManifest manifest, out string reason);
public InteractionAnimationValidationResult Validate(
    string expectedInteractionId, InteractionAnimationPresentationKind presentationKind);
```

Field-by-field documentation is in [MANIFEST_REFERENCE.md](MANIFEST_REFERENCE.md).

## What restore guarantees

When a `BodyWorld` session stops, the API restores the vanilla controller, rebinds the animator,
restores the animator parameters, layer weights and states, restores the first-person and
third-person rig control poses from pristine baselines, rebuilds the rig graph, and — for local
sessions — reapplies camera rotation, camera-chain position, and helmet-visor pose before the
frame renders. The intent is that after your interaction ends the player is indistinguishable
from one who never ran it.

Two consequences for consumers:

- During a local session the gameplay camera's local yaw and roll are anchored to their
  session-entry values (pitch stays vanilla-owned). If your mod applies camera effects like recoil
  by writing camera local rotation during a session, they will be overwritten. Drive such effects
  through the animation instead, or declare `body.localCameraOwnedExternally` if a camera mod owns
  the local camera for this interaction.
- Restore is skipped when the applied controller was replaced by someone else mid-session: the API
  never restores over a new owner. It logs the ownership loss and stops.

The mechanism, its kill-switches, and its log markers are documented for maintainers in
[internal/RESTORE_SEAM_INTERNALS.md](internal/RESTORE_SEAM_INTERNALS.md).

## Config entries a consumer should know about

Config file: `BepInEx/config/com.y4ngz.interactions.cfg`.

Section `Interaction Animation API V2`:

| Key | Default | Effect |
|---|---|---|
| `Camera Displacement Guard Exempt Interactions` | *(empty)* | Comma-separated interaction ids, trimmed and case-insensitive, whose live-body sessions report camera displacement at info level instead of being stopped by the guard. |
| `Special Animation Auto-Stop Exempt Interactions` | *(empty)* | Comma-separated interaction ids whose live-body sessions are not stopped by `inSpecialInteractAnimation` alone. Death and ladder climbing still stop them. |

Both exist as an **operator override**. The supported way for a consumer to request either
exemption is the manifest flag (`exemptFromCameraDisplacementGuard`,
`exemptFromSpecialAnimationAutoStop`); the effective exemption is manifest flag OR config list.

The sections `Interaction Animation API V2 Restore Diagnostics` and
`Interaction Animation API V2 Debug` are maintainer and bring-up surfaces. Consumers should not
depend on their values; they are catalogued in
[internal/RESTORE_SEAM_INTERNALS.md](internal/RESTORE_SEAM_INTERNALS.md).

## Failure reason catalogue

Every `out string reason` value the public API can produce. **75 distinct strings**, counting
each parameterised `live_body.preload_<role>_bundle_*` family once (`<role>` is `controller` or
`clip_pack`). Two shapes exist:

- **Flat `snake_case`** — coordinator-level and manifest-validation failures. The request never
  reached a presenter.
- **Dotted `live_body.*` / `viewmodel.*`** — presenter-level failures. The request was valid; the
  assets or the runtime state were not.

Values shown as `:<detail>` append a colon and a detail (a file name, an asset name, a resolved
path, or an exception message) — match on the prefix, never on the whole string.

### From every method

| Reason | Meaning |
|---|---|
| `interaction_animation_api_not_initialized` | The coordinator does not exist yet. Almost always a missing hard `BepInDependency`. |

### `TryRegisterInteractionPack`

| Reason | Meaning |
|---|---|
| `pack_null` | `pack` was null. |
| `pack_id_empty` | `PackId` was null, empty, or whitespace. |
| `pack_id_has_outer_whitespace` | `PackId` has leading or trailing whitespace. |
| `pack_interactions_empty` | `Interactions` was null or empty. |
| `interaction_null` | An entry in `Interactions` was null. |
| `interaction_id_empty` | An interaction's `InteractionId` was null, empty, or whitespace. |
| `interaction_id_has_outer_whitespace` | An interaction id has leading or trailing whitespace. |
| `interaction_id_duplicate` | Two interactions in the pack share an id (case-insensitive). |
| `pack_asset_root_invalid:<ExceptionType>` | `AssetRootPath` could not be turned into a full path. |
| `pack_asset_root_missing:<path>` | `AssetRootPath` resolved to a directory that does not exist. |
| `pack_already_registered` | This `PackId` is already registered. |

### `TryStartInteraction`

Request and lookup:

| Reason | Meaning |
|---|---|
| `request_null` | `request` was null. |
| `request_player_missing` | `Player` was null. |
| `request_pack_id_empty` | `PackId` was null, empty, or whitespace. |
| `request_interaction_id_empty` | `InteractionId` was null, empty, or whitespace. |
| `pack_not_registered` | No pack with that id. |
| `interaction_not_registered` | The pack has no interaction with that id. |

Manifest parsing:

| Reason | Meaning |
|---|---|
| `manifest_json_empty` | `ManifestJson` was null, empty, or whitespace. |
| `manifest_json_invalid:<message>` | `JsonUtility` threw. The message is Unity's. |
| `manifest_json_returned_null` | `JsonUtility` returned null without throwing — usually JSON that is valid but not an object. |

Manifest validation (identical set at `TryPreloadInteractionAssets`):

| Reason | Meaning |
|---|---|
| `manifest_schema_version_invalid` | `schemaVersion` was zero or negative. |
| `manifest_interaction_id_empty` | The manifest's own `interactionId` was blank. |
| `manifest_interaction_id_mismatch` | Manifest `interactionId` differs from the registered `InteractionId`. |
| `manifest_duration_invalid` | `durationSeconds` was negative, NaN, or infinite. |
| `manifest_viewmodel_missing` | Viewmodel kind with no `localViewmodel` object. |
| `manifest_viewmodel_prefab_empty` | `localViewmodel.prefab` was blank. |
| `manifest_viewmodel_bundle_file_empty` | `localViewmodel.bundleFileName` was blank. |
| `manifest_viewmodel_controller_empty` | `localViewmodel.controller` was blank. |
| `manifest_viewmodel_camera_anchor_empty` | `localViewmodel.cameraAnchor` was blank. |
| `manifest_sockets_missing` | Viewmodel kind with no `sockets` object. |
| `manifest_socket_left_hand_empty` | `sockets.leftHand` was blank. |
| `manifest_socket_right_hand_empty` | `sockets.rightHand` was blank. |
| `manifest_socket_prop_empty` | Neither `sockets.prop` nor the deprecated `sockets.tablet` was set. |
| `manifest_body_disabled` | `BodyWorld` kind with no `body` object, or `body.enabled` false. |
| `manifest_body_bundle_file_empty` | `body.bundleFileName` was blank. |
| `manifest_body_controller_empty` | `body.controller` was blank. |

Body-animator acquisition (`BodyWorld` only):

| Reason | Meaning |
|---|---|
| `missing_body_animator` | `player.playerBodyAnimator` was null. Also emitted by the presenter. |
| `player_animator_owned_externally` | The animator is running a controller that is neither vanilla's expected one nor this API's. Logged with the current and expected controller names. |

Live-body presenter start:

| Reason | Meaning |
|---|---|
| `missing_context` | Internal: a presenter was started without a context. Emitted by both presenters. |
| `missing_body_manifest` | `body` was null or disabled at presenter start. |
| `live_body.bundle_missing:<file>` | The body bundle did not resolve to an existing file. The log line carries the resolved path. |
| `live_body.bundle_load_failed:<path>` | `AssetBundle.LoadFromFile` returned null — corrupt bundle, or one already loaded under a different handle. |
| `live_body.controller_missing:<controller>` | Neither `controllerAssetName` nor `controller` loaded a `RuntimeAnimatorController` from the bundle. |
| `live_body.snapshot_failed` | The vanilla animator state could not be captured, so the swap was refused rather than risk an unrestorable player. |
| `live_body.controller_apply_exception:<message>` | Assigning `runtimeAnimatorController` threw. |
| `live_body.clip_pack_invalid_manifest` | `clipPack.enabled` but `bundleFileName` blank or `overrides` empty. |
| `live_body.clip_pack_bundle_missing:<file>` | The clip-pack bundle did not resolve to an existing file. |
| `live_body.clip_pack_bundle_load_failed:<path>` | The clip-pack bundle failed to load. |
| `live_body.clip_pack_clip_missing:<clip>` | An override clip is not in the clip-pack bundle. Fails closed. |
| `live_body.clip_pack_no_overrides_applied` | Every override entry had a blank `slot` or `clip`. |

Path resolution (surfaced by whichever presenter asked, and prefixed by it during preload):

| Reason | Meaning |
|---|---|
| `asset_bundle_file_empty` | A bundle file name in the manifest was blank. |
| `asset_bundle_path_escapes_root:<file>` | The bundle name resolved outside `AssetRootPath`. |
| `asset_bundle_path_invalid:<ExceptionType>` | The bundle path could not be turned into a full path. |

Viewmodel presenter start:

| Reason | Meaning |
|---|---|
| `missing_viewmodel_manifest` | `localViewmodel` was null at presenter start. |
| `viewmodel.bundle_missing:<file>` | The viewmodel bundle did not resolve to an existing file. |
| `viewmodel.bundle_preload_in_progress:<path>` | An async preload of this bundle is still running. Retry shortly. |
| `viewmodel.bundle_preload_started:<path>` | The bundle is over 16 MB, so a synchronous load was refused and an async preload was started instead. Retry once it completes. |
| `viewmodel.bundle_load_failed:<path>` | `AssetBundle.LoadFromFile` returned null. |
| `viewmodel.prefab_missing:<prefab>` | No `GameObject` with that asset name in the bundle. |
| `viewmodel.controller_missing:<controller>` | No `RuntimeAnimatorController` with that asset name in the bundle. |
| `viewmodel.camera_missing` | Neither the player's `gameplayCamera` nor `Camera.main` was available to parent the viewmodel to. |
| `viewmodel.camera_anchor_context_missing` | Internal: alignment ran without a root or manifest. |
| `viewmodel.camera_anchor_empty` | `localViewmodel.cameraAnchor` was blank at alignment time. |
| `viewmodel.camera_anchor_missing:<name>` | No descendant of the prefab has that exact name. |

### `TryPreloadInteractionAssets`

Adds these to the pack-lookup and manifest-validation reasons above:

| Reason | Meaning |
|---|---|
| `preload_invalid_identity` | `packId` or `interactionId` was null, empty, or whitespace. |
| `live_body.preload_controller_missing:<controller>` | The controller is not in the preloaded bundle. |
| `live_body.preload_clip_missing:<clip>` | An override clip is not in the preloaded clip pack. |
| `live_body.preload_prop_missing:<prefab>` | The prop prefab is not in the bundle it would be loaded from. |
| `live_body.preload_controller_bundle_rejected:<inner>` / `live_body.preload_clip_pack_bundle_rejected:<inner>` | Path resolution rejected the bundle; `<inner>` is one of the `asset_bundle_*` reasons. |
| `live_body.preload_controller_bundle_missing:<file>` / `live_body.preload_clip_pack_bundle_missing:<file>` | The bundle file does not exist. |
| `live_body.preload_controller_bundle_load_failed:<path>` / `live_body.preload_clip_pack_bundle_load_failed:<path>` | The bundle failed to load. |
| `viewmodel.bundle_preload_missing:<file>` | The viewmodel bundle file does not exist. |
| `viewmodel.bundle_preload_unavailable` | The plugin instance was gone, so no coroutine could be started. |

### `TryBeginInteractionExit`

| Reason | Meaning |
|---|---|
| `interaction_not_active` | The handle is not an active session — already stopped, or never valid. |

`TryStopInteraction`, `IsInteractionActive`, `IsPlayerInteractionActive`,
`TryStopPlayerInteractions`, and the three parameter setters return a bare `bool` and produce no
reason string.

## Stability

Pre-1.0. The signatures above are treated as **stable in intent** — three mods depend on them and
they are not changed casually — but until 1.0.0 the surface may still evolve. Anything breaking
is announced in [CHANGELOG.md](../CHANGELOG.md).

More specific expectations:

- **Method signatures and type members**: stable in intent. Additive change (new optional members,
  new methods) is expected and does not bump anything breaking.
- **Manifest schema**: additive. New fields default to the current behaviour; existing fields keep
  their meaning. Renamed fields keep the old name as an alias, as `sockets.tablet` does for
  `sockets.prop`.
- **Reason strings**: informational. Match on prefixes if you must branch on them, and treat any
  unrecognised value as a generic failure. New reasons appear whenever new validation does.
- **Enum values**: new values may be added. Handle unknown values defensively in a `switch`.
- **Log markers and config keys**: not part of the public contract. They change with the
  diagnostics.

Bug reports and API questions belong in
[Issues](https://github.com/y4ngz313/Y4NGZInteractions/issues).
