# Y4NGZInteractions

Last updated: 2026-08-19

Y4NGZInteractions is a consumer-agnostic, local animation presentation API for Lethal Company mods. It owns presentation resources for the lifetime of an interaction, plays a consumer-authored controller or viewmodel, and restores the affected player, camera, arms, rig, and prop state when the interaction ends.

This development repository, its living authoring docs, and its issue tracker are
private. The release package contains only the consumer-facing README, license,
changelog, manifest, icon, and DLL listed below.

It is not a networking service and it does not retarget animations at runtime. Every client that should see an interaction must receive that fact through networking owned by the consuming mod and invoke the API locally.

## Choose a presentation

| Presentation | Use it when | Authoring requirement |
| --- | --- | --- |
| BodyWorld | Other players must see the animation, or the local body must animate in world space | Controller and clips authored for the supported Lethal Company player hierarchy |
| DedicatedLocalViewmodel | Only the local player needs a camera-space rig | Any self-contained consumer-authored prefab, controller, rig, and clips |

A local BodyWorld session owns both the body animator and local camera/arms presentation. A remote BodyWorld session owns only that remote player's body animator. A DedicatedLocalViewmodel session owns the local camera/arms presentation and is rejected for remote players.

## Five-minute orientation

1. Install or build Y4NGZInteractions.
2. Run the supplied sample before writing integration code: see [Getting Started](docs/GETTING_STARTED.md).
3. Build the two clean-room bundles with the pinned Unity example project.
4. Build the sample BepInEx consumer and copy its output plus the example bundles to a clean profile.
5. Use the sample hotkeys to exercise BodyWorld and DedicatedLocalViewmodel.
6. Copy the registration/start pattern into your mod and replace the sample payload.

The smallest registration looks like this:

~~~csharp
var pack = new InteractionAnimationPackDefinition
{
    PackId = "example.interactions",
    Version = "1.0.0",
    AssetRootPath = assetDirectory,
    Interactions = new[]
    {
        new InteractionAnimationDefinition
        {
            InteractionId = "wave",
            PresentationKind = InteractionAnimationPresentationKind.BodyWorld,
            ManifestJson = File.ReadAllText(manifestPath)
        }
    }
};

if (!LCInteractionAnimationAPI.TryRegisterInteractionPack(pack, out string reason))
    Logger.LogError(reason);
~~~

Starting is handle-scoped and rejects conflicts unless interruption is explicitly requested:

~~~csharp
var request = new InteractionAnimationRequest
{
    Player = targetPlayer,
    PackId = "example.interactions",
    InteractionId = "wave",
    ConflictPolicy = InteractionAnimationConflictPolicy.RejectIfBusy
};

if (LCInteractionAnimationAPI.TryStartInteraction(
        request, out InteractionAnimationHandle handle, out string reason))
{
    activeHandle = handle;
}
~~~

Subscribe to InteractionEnded when your consumer needs an authoritative local completion notification. The event fires exactly once after restoration and lease release.

## Important limitations

- Networking belongs to the consuming mod. The repository example shows a minimal named-message broadcast.
- Runtime retargeting is outside the API. Unity Humanoid, manual keyframe transfer, custom scripts, and DCC workflows are all valid inputs if their output passes validation.
- BodyWorld clips must match the supported player hierarchy and controller contract.
- DedicatedLocalViewmodel content is local-player-only.
- The API accepts schema-1 JSON through a migration path during the 1.x line, but new content must use strict schema 2.
- Unknown schema-2 fields and unsafe bundle or transform paths are errors.
- The public C# surface is frozen by a checked analyzer baseline. Public signature changes require a deliberate compatibility decision.

## Documentation

- [Getting Started](docs/GETTING_STARTED.md)
- [Authoring Guide](docs/AUTHORING_GUIDE.md)
- [API Reference](docs/API_REFERENCE.md)
- [Manifest Reference](docs/MANIFEST_REFERENCE.md)
- [Lethal Company Rig Reference](docs/LETHAL_COMPANY_RIG_REFERENCE.md)
- [Advanced Prop Recipe](docs/ADVANCED_PROP_RECIPE.md)
- [Troubleshooting](docs/TROUBLESHOOTING.md)
- [Architecture](docs/ARCHITECTURE.md)
- [1.0 Migration Guide](docs/MIGRATION_1_0.md)

## Build and verify

A clean build uses public package sources and does not require a local game installation:

~~~powershell
dotnet restore Y4NGZInteractions.slnx
dotnet build Y4NGZInteractions.slnx -c Release --no-restore
dotnet test Y4NGZInteractions.slnx -c Release --no-build
powershell -ExecutionPolicy Bypass -File scripts/test-player-animation-api-static-regressions.ps1
powershell -ExecutionPolicy Bypass -File scripts/test-public-tree.ps1
powershell -ExecutionPolicy Bypass -File scripts/test-markdown-links.ps1
powershell -ExecutionPolicy Bypass -File release/stage-y4ngz-interactions.ps1 -SkipBuild
~~~

Release builds do not deploy automatically. For an explicit local developer deployment, set both EnableProfileDeploy=true and DeployTarget to the intended plugin directory.

## Package contents

The Thunderstore package contains only:

- Y4NGZInteractions.dll
- icon.png
- README.md
- LICENSE
- CHANGELOG.md
- manifest.json

Examples, authoring projects, tests, source assets, and generated animation bundles remain repository-only.

## License

Code and original example assets are available under the [MIT License](LICENSE).
