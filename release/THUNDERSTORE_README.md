<!--
  MEDIA SLOTS
  1. release/previews/thunderstore/interaction-api-overview.png
  2. release/previews/thunderstore/first-person-playback.gif
  3. release/previews/thunderstore/live-body-playback.gif

  The image lines are intentionally commented out until the files above are
  uploaded to the public repository and are publicly reachable. Uncomment the
  matching lines below once they are.
-->

<!-- ![Y4NGZ Interactions API overview](https://raw.githubusercontent.com/y4ngz313/Y4NGZInteractions/main/release/previews/thunderstore/interaction-api-overview.png) -->

# Y4NGZ INTERACTIONS

**Y4NGZ Interactions** is a shared interaction-animation API for Lethal Company. It gives feature
mods one owner for first-person and live-body animation playback, so several mods can animate the
player without fighting over the same animator, rig, and camera.

This is a library for mod authors. It ships no animations of its own and adds no gameplay by
itself — install it because something else needs it.

---

## What the API Provides

- Registration of named animation packs and interactions, described by a manifest JSON.
- Live-body playback: the authored controller is applied to the player's body animator, so the
  animation is visible in first person **and** to other players in third person.
- Dedicated local viewmodel playback: a consumer-owned prefab parented to the local camera, for
  interactions that need isolation from the live player rig.
- Clip packs: swap the clips in an authored shell controller at runtime, so new animations are new
  bundles rather than new controllers.
- Optional hand-attached props, with a timed release point for throw and hand-off animations.
- Animator parameter passthrough, so the consuming mod drives its own gestures and states.
- One live-body session per player, with pre-emption and an external-ownership check that refuses
  to stomp another mod's controller.
- Restore guards that put the player's animator, rig, camera, and helmet visor back to vanilla
  state when an interaction ends.
- Consumer-selected asset roots, so a feature mod ships and loads its own animation payload.
- Validation and diagnostic surfaces for authoring and integration checks.

The public entry point is `LCInteractionAnimationAPI`.

<!-- ![First-person interaction playback](https://raw.githubusercontent.com/y4ngz313/Y4NGZInteractions/main/release/previews/thunderstore/first-person-playback.gif) -->

---

## For Mod Authors

Source, documentation, and issues: <https://github.com/y4ngz313/Y4NGZInteractions>

Declare a hard dependency so BepInEx initialises the API before your plugin:

```csharp
[BepInPlugin("com.example.mymod", "MyMod", "1.0.0")]
[BepInDependency("com.y4ngz.interactions", BepInDependency.DependencyFlags.HardDependency)]
public sealed class MyModPlugin : BaseUnityPlugin
{
    private void Awake()
    {
        string pluginDir = Path.GetDirectoryName(typeof(MyModPlugin).Assembly.Location);

        var pack = new InteractionAnimationPackDefinition
        {
            PackId = "com.example.mymod",
            AssetRootPath = pluginDir,   // your own folder: where your bundles live
            Interactions = new[] { /* your InteractionAnimationDefinition entries */ }
        };

        LCInteractionAnimationAPI.TryRegisterInteractionPack(pack, out string reason);
    }
}
```

Your mod ships its own AssetBundles and manifests in its own plugin folder and registers them
through the API; the API handles presentation, cancellation, and restoration. Start here:
[Getting started](https://github.com/y4ngz313/Y4NGZInteractions/blob/main/docs/GETTING_STARTED.md),
[API reference](https://github.com/y4ngz313/Y4NGZInteractions/blob/main/docs/API_REFERENCE.md),
[manifest reference](https://github.com/y4ngz313/Y4NGZInteractions/blob/main/docs/MANIFEST_REFERENCE.md).

Note that the API is a **local presentation layer**: it sends no RPCs and replicates nothing. Your
mod makes the triggering cause replicated and calls the API on every client for the player being
animated.

<!-- ![Live-body interaction playback](https://raw.githubusercontent.com/y4ngz313/Y4NGZInteractions/main/release/previews/thunderstore/live-body-playback.gif) -->

---

## Compatibility

- Requires **BepInEx 5**.
- Consumer-agnostic: no hard dependency on any specific feature mod.
- Currently consumed by Y4NGZ Upgrades, Y4NGZ Company, and Y4NGZ Monsters.
- Pre-1.0: public signatures are treated as a shared compatibility surface, and breaking changes
  are announced in the changelog.
- MIT licensed.

---

## Bugs & Feedback

Issue tracker: <https://github.com/y4ngz313/Y4NGZInteractions/issues>

When reporting an animation issue, include `BepInEx/LogOutput.log`, the interaction id, whether it
affected first person or the live body, and whether the problem occurred for the local player or a
remote player.
