# Getting started

Last updated: 2026-08-05

How to add an interaction animation to your own Lethal Company mod through
Y4NGZInteractions. It assumes you are comfortable with BepInEx 5 plugins and Unity
AssetBundles, but know nothing about this API.

Reference material lives in [API_REFERENCE.md](API_REFERENCE.md) (methods, types, failure
reasons) and [MANIFEST_REFERENCE.md](MANIFEST_REFERENCE.md) (the manifest schema). This page is
the walkthrough.

## 1. What you are building

Three things belong to **your** mod:

1. **A shell `AnimatorController` inside an AssetBundle.** This is the state machine the API
   applies to the player. It defines the layers, the states, the transitions, and the animator
   parameters your gameplay code will drive. Its clips act as *slots*: the API can replace them
   at runtime without ever touching the controller asset.
2. **Optionally, a clip-pack AssetBundle.** A second bundle holding the actual
   `AnimationClip` assets. At start the API wraps your shell controller in an
   `AnimatorOverrideController` and swaps each named slot clip for a clip from this pack. This
   is how you ship new animations without rebuilding the controller — new animation, new clip
   pack, same shell. A prop prefab, if you use one, is loaded from this bundle too (falling back
   to the controller bundle when no clip pack is enabled).
3. **A manifest JSON file.** Plain text describing which bundle, which controller, which layers,
   which parameters, which clip overrides, and which prop. The API parses it with
   `JsonUtility`, so field names are case-sensitive camelCase, comments are not allowed, and
   unknown fields are silently ignored. Full schema in
   [MANIFEST_REFERENCE.md](MANIFEST_REFERENCE.md).

All three ship **in your own plugin folder**, next to your DLL — not in the API's folder. The
API never contains animation content.

Authoring the clips themselves (rig, bone names, IK targets) is out of scope here; see
`ANIMATION_AUTHORING_PIPELINE.md` in this repository for the retargeting pipeline.

## 2. Project setup

Reference `Y4NGZInteractions.dll` — either the assembly from a release, or a `ProjectReference`
to `src/Y4NGZInteractions/Y4NGZInteractions.csproj` if you build both from source:

```xml
<ItemGroup>
  <Reference Include="Y4NGZInteractions">
    <HintPath>..\lib\Y4NGZInteractions.dll</HintPath>
    <Private>false</Private>
  </Reference>
</ItemGroup>
```

`<Private>false</Private>` matters: the API ships as its own plugin, so you must not copy it
next to your DLL.

Then declare the dependency on your plugin class:

```csharp
[BepInPlugin("com.example.mymod", "MyMod", "1.0.0")]
[BepInDependency("com.y4ngz.interactions", BepInDependency.DependencyFlags.HardDependency)]
public sealed class MyModPlugin : BaseUnityPlugin
```

**This attribute is required, not decorative.** BepInEx uses it to order plugin loading. Without
it your `Awake` may run before the API has initialised its coordinator, and every call returns
`interaction_animation_api_not_initialized`.

### Soft (optional) dependency

If the integration should be optional, use `DependencyFlags.SoftDependency` and reach the API by
reflection so your assembly still loads when it is absent:

```csharp
private static readonly Type ApiType = Type.GetType(
    "Y4NGZInteractions.InteractionAnimationApi.LCInteractionAnimationAPI, Y4NGZInteractions");

private static bool ApiPresent => ApiType != null;
```

Invoke the methods through `MethodInfo`. The soft path is more code and gives up compile-time
checking; prefer the hard dependency when the animation is core to your feature.

## 3. Lay out your plugin folder

```
BepInEx/plugins/YourTeam-MyMod/
├── MyMod.dll
├── lantern-livebody.manifest.json
├── mymod-lantern-playeranimations.animationbundle   (shell controller)
└── mymod-lantern-iktargets.animationbundle          (clip pack + prop prefab)
```

Bundle names in the manifest are **relative to your `AssetRootPath`**, so keep them file names,
not paths. Subfolders are allowed; anything that resolves outside the root is rejected with
`asset_bundle_path_escapes_root`.

## 4. Register the pack

Register once, at `Awake`. A pack id may only be registered once per game session.

```csharp
using System.IO;
using BepInEx;
using GameNetcodeStuff;
using Y4NGZInteractions.InteractionAnimationApi;

private const string PackId = "com.example.mymod";
private const string LanternInteractionId = "com.example.mymod.lantern";

private void Awake()
{
    string pluginDir = Path.GetDirectoryName(typeof(MyModPlugin).Assembly.Location);
    string manifestJson = File.ReadAllText(
        Path.Combine(pluginDir, "lantern-livebody.manifest.json"));

    var pack = new InteractionAnimationPackDefinition
    {
        PackId = PackId,
        DisplayName = "MyMod interactions",
        Version = "1.0.0",
        Author = "example",
        AssetRootPath = pluginDir,          // see the warning below
        Interactions = new[]
        {
            new InteractionAnimationDefinition
            {
                InteractionId = LanternInteractionId,
                DisplayName = "Lantern",
                PresentationKind = InteractionAnimationPresentationKind.BodyWorld,
                ExpectedDurationSeconds = 0f,   // 0 = take durationSeconds from the manifest
                ManifestJson = manifestJson
            }
        }
    };

    if (!LCInteractionAnimationAPI.TryRegisterInteractionPack(pack, out string reason))
        Logger.LogError($"[MyMod] interaction pack rejected: {reason}");
}
```

> **Always set `AssetRootPath`.** It is optional in the type, but omitting it does not mean
> "look next to my DLL" — it means "fall back to the API's own asset roots": the API assembly's
> directory, the `y4ngz313-Y4NGZInteractions` plugin folder, and the BepInEx plugins root. Your
> bundles are in none of those, so start will fail with `live_body.bundle_missing` or
> `viewmodel.bundle_missing`. Setting it also confines every bundle path in the manifest to your
> own directory, which is the behaviour you want.

The manifest's `interactionId` must match the `InteractionId` you register (case-insensitively),
otherwise start fails with `manifest_interaction_id_mismatch`. Registration validates only the
pack shape and the asset root — the manifest itself is parsed and validated later, at preload if
you use it and always at the first start.

### Optional: preload

`TryStartInteraction` loads the bundle synchronously if it has not been loaded before, which can
cost a frame hitch. Preload during a quiet moment (game start, a round beginning) to move that
cost off the interaction:

```csharp
if (!LCInteractionAnimationAPI.TryPreloadInteractionAssets(
        PackId, LanternInteractionId, out string reason))
{
    Logger.LogWarning($"[MyMod] preload failed: {reason}");
}
```

For `BodyWorld` this loads and deserialises the controller, clip pack, clips, and prop, and
keeps them cached until the API shuts down. For a dedicated viewmodel it starts an asynchronous
bundle load (bundles over 16 MB are *only* loadable this way — a synchronous start on a larger
bundle returns `viewmodel.bundle_preload_started` and you must retry once loading completes).

## 5. Start, drive, end

### Start

```csharp
var request = new InteractionAnimationRequest
{
    Player = player,               // any PlayerControllerB, local or remote
    PackId = PackId,
    InteractionId = LanternInteractionId,
    OwnerModId = "com.example.mymod"
};

if (!LCInteractionAnimationAPI.TryStartInteraction(
        request, out InteractionAnimationHandle handle, out string reason))
{
    Logger.LogWarning($"[MyMod] lantern start failed: {reason}");
    return;
}
```

Keep the handle. It is a value type (a `Guid` wrapper) that identifies this one session; it is
never reused, and once the session ends the handle is permanently dead. Check
`LCInteractionAnimationAPI.IsInteractionActive(handle)` before acting on a stored handle.

### Drive

Anything the animation should react to is an animator parameter on your shell controller:

```csharp
LCInteractionAnimationAPI.TrySetInteractionBool(handle, "MyMod_Lantern_Raised", true);
LCInteractionAnimationAPI.TrySetInteractionInt(handle, "MyMod_Lantern_ActionIndex", 2);
LCInteractionAnimationAPI.TryFireInteractionTrigger(handle, "MyMod_Lantern_Action");
```

Each returns `false` (silently, no reason string) if the session is gone or the controller has no
parameter of that name **and** type — so a `Bool` named the same as a `Trigger` will not match.

### End

Two ways, and the choice is about presentation, not correctness:

- `TryBeginInteractionExit(handle, out reason)` — **graceful.** Sets the manifest's `activeBool`
  false, fires its `exitTrigger`, waits `body.exitSeconds` (or `localViewmodel.exitSeconds`) for
  the put-away animation to play, then stops on its own. Use this for anything the player toggles
  off.
- `TryStopInteraction(handle, InteractionAnimationStopReason.Requested)` — **immediate.** Restore
  begins this frame. Use it for cancellations and hard aborts.

You can also let the interaction end itself: a manifest `durationSeconds` greater than zero (or a
non-zero `ExpectedDurationSeconds` on the definition, which takes precedence) makes the
coordinator auto-stop with `NaturalEnd` once that much wall-clock time has passed. Set both to
zero for an indefinite toggle.

Finally, a `BodyWorld` session stops itself with `Interrupted` when the player dies, starts
climbing a ladder, enters a vanilla special animation, or when another system replaces the
controller the API applied. If your interaction legitimately owns the special-animation flag for
its whole duration, declare `exemptFromSpecialAnimationAutoStop` in the manifest.

### Cooperating with other animation code

If your mod also swaps `playerBodyAnimator.runtimeAnimatorController` directly somewhere, it must
yield to any API-owned session on that player:

```csharp
if (LCInteractionAnimationAPI.IsPlayerInteractionActive(player))
{
    LCInteractionAnimationAPI.TryStopPlayerInteractions(
        player, InteractionAnimationStopReason.Interrupted);
    if (LCInteractionAnimationAPI.IsPlayerInteractionActive(player))
        return;   // restore did not complete; do not take ownership this frame
}
```

Conversely, if the animator is already owned by someone else when you start, the API refuses with
`player_animator_owned_externally` rather than stomping it.

## The networking contract

**The API is a strictly local presentation layer. It sends no RPCs and replicates nothing.**
`TryStartInteraction` animates the player you name, on the client you call it on, and nowhere
else. There is no hidden synchronisation.

That means replication is entirely your responsibility, in two parts:

1. **Make the cause replicated.** Vanilla replicated state you can already observe, an RPC of
   your own, or a custom named message — whatever tells every client that this player is now
   doing the thing.
2. **Call `TryStartInteraction` on every client**, for the relevant `PlayerControllerB`,
   including remote players. A `BodyWorld` session on a remote player is what produces the
   third-person animation everyone else sees; if only the acting client starts it, the acting
   player sees their animation and nobody else does.

Two patterns are in production use:

**Poll replicated state.** Each client watches the replicated flag per player and starts or stops
sessions to match. Guard with `IsPlayerInteractionActive` so a poll that fires repeatedly does not
restart the session every frame:

```csharp
foreach (PlayerControllerB player in StartOfRound.Instance.allPlayerScripts)
{
    bool shouldAnimate = MyReplicatedState.IsUsingLantern(player);
    bool isAnimating = LCInteractionAnimationAPI.IsPlayerInteractionActive(player);

    if (shouldAnimate && !isAnimating)
        StartLantern(player);                       // stores the handle per player
    else if (!shouldAnimate && isAnimating)
        LCInteractionAnimationAPI.TryStopPlayerInteractions(
            player, InteractionAnimationStopReason.Requested);
}
```

**Server broadcast.** The server validates the action, sends a named message to all clients
carrying the player id and interaction id, and each receiving client drives the API locally.
Handles stay local to the client that created them; never try to send one over the wire.

Note that `IsPlayerInteractionActive` reports `BodyWorld` sessions only — dedicated viewmodels
are local-only by design and are not part of the player-body ownership lock.

## Troubleshooting

| Reason you got back | What it usually means |
|---|---|
| `interaction_animation_api_not_initialized` | Missing `[BepInDependency("com.y4ngz.interactions", HardDependency)]`, so your `Awake` ran first. Add it. (Or the API failed to load at all — check the log.) |
| `live_body.bundle_missing` / `viewmodel.bundle_missing` | `AssetRootPath` was not set, or the `bundleFileName` does not match the file you shipped. The log line includes the resolved path it tried. |
| `pack_asset_root_missing:<path>` | `AssetRootPath` points at a directory that does not exist. Derive it from `Assembly.Location`, do not hardcode it. |
| `asset_bundle_path_escapes_root:<name>` | A manifest bundle name resolves outside `AssetRootPath` (an absolute path or `..`). Keep bundle names relative and inside your folder. |
| `pack_already_registered` | You registered the same `PackId` twice — for example from both `Awake` and a scene hook. Register once. |
| `manifest_interaction_id_mismatch` | The manifest's `interactionId` differs from the `InteractionId` you registered. |
| `manifest_body_disabled` | `PresentationKind.BodyWorld` but the manifest has no `body` block or `body.enabled` is false. |
| `manifest_viewmodel_bundle_file_empty` / `manifest_viewmodel_prefab_empty` | These no longer have defaults; a viewmodel manifest must set them explicitly. |
| `live_body.controller_missing:<name>` | The bundle loaded, but no `RuntimeAnimatorController` inside it has that asset name. Try `controllerAssetName` with the `.controller` suffix as well as the bare `controller` name. |
| `live_body.clip_pack_clip_missing:<clip>` | A clip named in `clipPack.overrides` is not in the clip-pack bundle. Names are asset names, not file paths. |
| `live_body.clip_pack_no_overrides_applied` | Every override entry had an empty `slot` or `clip`. The clip pack fails closed rather than starting with the shell's placeholder clips. |
| `player_animator_owned_externally` | Another mod (or your own code) has replaced this player's animator controller. Whoever owns it must restore vanilla first. |
| `missing_body_animator` | `playerBodyAnimator` was null — usually a player object that is not fully spawned yet. |
| `pack_not_registered` / `interaction_not_registered` | Typo in the ids, or the start ran before registration. Ids are compared case-insensitively but must otherwise match exactly. |
| `viewmodel.camera_anchor_missing:<name>` | The viewmodel prefab has no child transform with that name. The anchor is searched recursively by exact name. |
| `viewmodel.bundle_preload_started` | The viewmodel bundle is over 16 MB, so the API kicked off an async load instead. Retry the start once the load reports complete. |

The full list of reason strings is in
[API_REFERENCE.md](API_REFERENCE.md#failure-reason-catalogue).

Every runtime event is logged to `BepInEx/LogOutput.log` with the `[LCInteractionAnimationAPI]`
prefix; the log lines carry more detail than the reason strings (resolved paths, controller
names, layer indices). If the animation starts but looks wrong rather than failing, that log is
the place to start.
