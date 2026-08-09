# Y4NGZ Interactions

Y4NGZ Interactions is a shared interaction-animation API for Lethal Company. It gives feature mods one owner for first-person and live-body playback so multiple systems can animate a player without fighting over the same animator, rig, or camera.

This is a library for mod authors. It ships no animation payload of its own and adds no gameplay by itself; install it when another mod declares it as a dependency.

## What the API Provides

- Registration of named animation packs and interactions from manifest JSON.
- Live-body playback that is visible locally in first person and remotely in third person.
- Dedicated local viewmodel playback for interactions that need isolation from the live player rig.
- Runtime clip-pack substitution using an authored controller shell.
- Optional hand-attached props with timed release points.
- Animator-parameter passthrough controlled by the consuming mod.
- Per-player resource leases with reject-or-interrupt conflict policies.
- External-ownership checks that refuse to overwrite another animation controller.
- Restore guards for the player animator, rig, camera, and helmet visor.
- Consumer-selected asset roots, validation, and diagnostic surfaces.

The public entry point is `LCInteractionAnimationAPI`.

## For Mod Authors

Source, documentation, and issues are available at [github.com/y4ngz313/Y4NGZInteractions](https://github.com/y4ngz313/Y4NGZInteractions).

Declare a hard BepInEx dependency so the API initializes before your plugin:

```csharp
using System.IO;
using BepInEx;
using Y4NGZInteractions.InteractionAnimationApi;

[BepInPlugin("com.example.mymod", "MyMod", "1.0.0")]
[BepInDependency("com.y4ngz.interactions", BepInDependency.DependencyFlags.HardDependency)]
public sealed class MyModPlugin : BaseUnityPlugin
{
    private void Awake()
    {
        string pluginDir = Path.GetDirectoryName(typeof(MyModPlugin).Assembly.Location);
        string manifestJson = File.ReadAllText(
            Path.Combine(pluginDir, "mymod-livebody.manifest.json"));

        var pack = new InteractionAnimationPackDefinition
        {
            PackId = "com.example.mymod",
            Version = "1.0.0",
            AssetRootPath = pluginDir,
            Interactions = new[]
            {
                new InteractionAnimationDefinition
                {
                    InteractionId = "com.example.mymod.inspect",
                    PresentationKind = InteractionAnimationPresentationKind.BodyWorld,
                    ManifestJson = manifestJson
                }
            }
        };

        if (!LCInteractionAnimationAPI.TryRegisterInteractionPack(pack, out string reason))
            Logger.LogError($"Interaction pack rejected: {reason}");
    }
}
```

The consuming mod owns and ships its bundles, manifests, and finger-pose files. It registers those files from its own plugin directory; Y4NGZ Interactions handles presentation, cancellation, arbitration, and restoration.

Start with the [getting-started guide](https://github.com/y4ngz313/Y4NGZInteractions/blob/main/docs/GETTING_STARTED.md), then see the [API reference](https://github.com/y4ngz313/Y4NGZInteractions/blob/main/docs/API_REFERENCE.md) and [manifest reference](https://github.com/y4ngz313/Y4NGZInteractions/blob/main/docs/MANIFEST_REFERENCE.md).

## Networking Contract

The API is a local presentation layer. It sends no RPCs and replicates no gameplay state. A consuming mod must synchronize the triggering event and call the API on every client for the player being animated.

## Required Dependency

- `BepInEx-BepInExPack-5.4.2305`

There are no feature-mod dependencies and no bundled animation assets.

## Compatibility

- The 1.x public API is treated as a shared compatibility surface.
- Consumer animation content remains isolated in the consumer's package.
- Lease and restore guards reduce conflicts, but consumers must still cancel sessions when their gameplay state ends.
- Licensed under the MIT License.

## Bugs and Feedback

Use the [public issue tracker](https://github.com/y4ngz313/Y4NGZInteractions/issues). Include `BepInEx/LogOutput.log`, the interaction ID, first-person or live-body mode, and whether the affected player was local or remote.
