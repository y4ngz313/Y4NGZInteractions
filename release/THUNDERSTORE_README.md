# Y4NGZ Interactions

**Y4NGZ Interactions** is a shared interaction-animation API for Lethal Company.
It gives feature mods one owner for first-person and live-body presentation so
multiple systems do not fight over the same animator, rig, camera, or prop.

This is a library for mod authors. It ships no animation payload and adds no
gameplay by itself; install it when another package declares it as a dependency.

## Installing for players

- Install it with r2modman or Thunderstore Mod Manager. BepInExPack is installed
  automatically.
- For a manual install, place `Y4NGZInteractions.dll` in `BepInEx/plugins/`.
- Version 1.0.1 is built against Lethal Company v81. A game update that changes
  the player rig or animator may require a new release.

## What the API provides

- Named animation-pack and interaction registration from manifest JSON.
- **BodyWorld** presentation for a live player body, visible locally in first
  person and remotely in third person.
- **DedicatedLocalViewmodel** presentation for local-only camera-space rigs.
- Consumer-authored controller-shell and clip-pack substitution.
- Optional hand-attached props with timed release points.
- Consumer-controlled animator parameters.
- Per-player resource leases with reject-or-interrupt conflict policies.
- External-ownership checks that refuse to overwrite another controller.
- Deterministic restoration for the player animator, stance, rig, camera, arms,
  helmet visor, props, and temporary compatibility changes.
- Strict schema-2 validation with schema-1 migration during the 1.x line.

The public entry point is `LCInteractionAnimationAPI`.

## For mod authors

Add `y4ngz313-Y4NGZInteractions-1.0.1` to your Thunderstore manifest, reference
`Y4NGZInteractions.dll` at compile time with `Private=false`, and declare a hard
BepInEx dependency so the API initializes before your plugin:

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

The consuming mod owns and ships its controller, clips, bundles, manifests, and
finger-pose files. It registers files from its own plugin directory. Y4NGZ
Interactions owns local presentation, arbitration, cancellation, cleanup, and
restoration.

## Networking contract

The API is a local presentation layer. It sends no RPCs and replicates no
gameplay state. A consuming mod must synchronize the triggering event and call
the API on every client that should present the interaction.

## Required dependency

- `BepInEx-BepInExPack-5.4.2305`

There are no feature-mod dependencies and no bundled animation assets.

## Compatibility and support

- The 1.x C# surface and strict schema-2 contract are treated as shared
  compatibility surfaces.
- Schema-1 JSON remains accepted through the documented 1.x migration path.
- Consumers must cancel sessions when their gameplay state ends and must not
  assume that this local presentation API synchronizes the start for them.
- When reporting a problem through the consuming mod's support channel, include
  `BepInEx/LogOutput.log`, the pack and interaction IDs, the presentation kind,
  and whether the affected player was local or remote.

Code and original example assets are licensed under the MIT License.

## Generative AI usage

The code base was developed with generative AI assistance.
