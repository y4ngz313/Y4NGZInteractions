# Y4NGZInteractions

A consumer-agnostic **interaction animation API** for Lethal Company. It is a BepInEx 5
plugin (`Y4NGZInteractions.dll`, GUID `com.y4ngz.interactions`) that other mods depend on in
order to play authored animations on the player without each of them fighting over the same
animator, rig, and camera.

A consuming mod registers an *interaction pack*: a small JSON manifest plus its own Unity
AssetBundles, shipped in its own plugin folder. At runtime it asks the API to start an
interaction for a specific `PlayerControllerB`. The API loads the bundle, applies the authored
controller (wrapped in an `AnimatorOverrideController` when a clip pack supplies the clips),
optionally attaches a prop to a hand bone, drives animator parameters on the consumer's behalf,
and — the part that is genuinely hard — puts the player, rig, camera, and visor back exactly
where vanilla expects them when the interaction ends.

Two presentation kinds are available. `BodyWorld` (the production path) swaps the controller on
`playerBodyAnimator`, so the animation is visible in first person **and** to other players in
third person. `DedicatedLocalViewmodel` instantiates a consumer-owned prefab parented to the
local gameplay camera, for interactions that need to be isolated from the live player rig; it is
local-only and never seen by anyone else. The API ships **no animations of its own** — it is a
library, and every clip, controller, prop, and manifest belongs to the mod that uses it.

Honest limitations: the API is a strictly **local presentation layer**. It sends no RPCs and
replicates nothing — making an interaction visible on other clients is the consumer's job (see
[the networking contract](docs/GETTING_STARTED.md#the-networking-contract)). Animation content
must be authored against Lethal Company's player rig; there is no retargeting service here. And
only one `BodyWorld` session may own a given player at a time — starting a second interrupts the
first.

## Install

**Players.** Install from Thunderstore with your mod manager, or drop the plugin folder into
your BepInEx profile's `BepInEx/plugins` directory. On its own it changes nothing in game; it is
a dependency of the mods that use it.

**Mod authors.** Reference the assembly (`Y4NGZInteractions.dll` from a release, or a
`ProjectReference` to `src/Y4NGZInteractions/Y4NGZInteractions.csproj`) and declare a hard
dependency so BepInEx initialises the API before your plugin's `Awake`:

```csharp
using BepInEx;
using GameNetcodeStuff;
using System.IO;
using Y4NGZInteractions.InteractionAnimationApi;

[BepInPlugin("com.example.mymod", "MyMod", "1.0.0")]
[BepInDependency("com.y4ngz.interactions", BepInDependency.DependencyFlags.HardDependency)]
public sealed class MyModPlugin : BaseUnityPlugin
{
    private void Awake()
    {
        string myPluginDir = Path.GetDirectoryName(typeof(MyModPlugin).Assembly.Location);

        var pack = new InteractionAnimationPackDefinition
        {
            PackId = "com.example.mymod",
            // Always set this: it is where YOUR bundles live, and it confines bundle
            // resolution to your own folder. Omitting it makes the resolver probe the
            // API's folders instead, and your bundles will not be found.
            AssetRootPath = myPluginDir,
            Interactions = new[]
            {
                new InteractionAnimationDefinition
                {
                    InteractionId = "com.example.mymod.lantern",
                    PresentationKind = InteractionAnimationPresentationKind.BodyWorld,
                    ManifestJson = File.ReadAllText(
                        Path.Combine(myPluginDir, "lantern-livebody.manifest.json"))
                }
            }
        };

        if (!LCInteractionAnimationAPI.TryRegisterInteractionPack(pack, out string reason))
            Logger.LogError($"pack registration failed: {reason}");
    }

    // Call this on EVERY client for the player who should be animated.
    public void PlayLantern(PlayerControllerB player)
    {
        var request = new InteractionAnimationRequest
        {
            Player = player,
            PackId = "com.example.mymod",
            InteractionId = "com.example.mymod.lantern",
            OwnerModId = "com.example.mymod"
        };

        if (LCInteractionAnimationAPI.TryStartInteraction(
                request, out InteractionAnimationHandle handle, out string reason))
        {
            // Keep `handle`: it drives parameters and ends the interaction.
        }
    }
}
```

If you would rather integrate optionally, the API can also be driven by reflection as a soft
dependency — see [Getting Started](docs/GETTING_STARTED.md#soft-optional-dependency).

## Documentation

| Document | What it covers |
|---|---|
| [docs/GETTING_STARTED.md](docs/GETTING_STARTED.md) | End-to-end integration: project setup, the artefacts you need, registration, driving and ending an interaction, networking, troubleshooting |
| [docs/API_REFERENCE.md](docs/API_REFERENCE.md) | Every public method, type, and enum; handle lifetime; preload; the full failure-reason catalogue |
| [docs/MANIFEST_REFERENCE.md](docs/MANIFEST_REFERENCE.md) | Complete manifest schema, field by field, with annotated examples |
| [docs/README.md](docs/README.md) | Index of the rest of the documentation, including maintainer-facing notes |

## Build from source

Requires the .NET SDK and a Lethal Company installation (the build resolves
`Unity.InputSystem.dll` from the game's `Managed` directory).

```powershell
dotnet build .\src\Y4NGZInteractions\Y4NGZInteractions.csproj -c Release
```

Two MSBuild properties cover non-default installs:

- `GameManagedDir` — path to `Lethal Company_Data\Managed`. Defaults to the Steam location.
- `TestProfileRoot` — the BepInEx profile a `Release` build deploys into. `BepInExPlugins` and
  `DeployTarget` derive from it, and either can be overridden directly instead.

```powershell
dotnet build .\src\Y4NGZInteractions\Y4NGZInteractions.csproj -c Release `
  -p:GameManagedDir="D:\Games\Lethal Company\Lethal Company_Data\Managed" `
  -p:DeployTarget="D:\profiles\dev\BepInEx\plugins\Y4NGZInteractions"
```

Static regression checks live in `scripts/` and run with PowerShell, for example:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\test-interaction-animation-api-v2-static-regressions.ps1
```

## Status

Pre-1.0. The public surface is treated as stable in intent — three mods depend on it — but it
may still evolve before 1.0.0, and breaking changes are announced in
[CHANGELOG.md](CHANGELOG.md). Bugs and integration questions belong in
[Issues](https://github.com/y4ngz313/Y4NGZInteractions/issues); include `BepInEx/LogOutput.log`,
the interaction id, the presentation kind, and whether the problem affected the local or a
remote player.

## License

MIT — see [LICENSE](LICENSE).
