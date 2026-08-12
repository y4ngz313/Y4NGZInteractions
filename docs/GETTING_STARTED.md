# Getting Started

Last updated: 2026-08-11

Run the supplied example before integrating the API into another mod. This separates authoring, packaging, networking, and runtime concerns while every component is still known-good.

## Words this documentation uses

A few terms appear throughout the docs before their full explanations. Plain-language versions up front:

- **Consumer**: your mod - the code that registers animations and asks the API to play them.
- **Presentation kind**: which of the two playback paths an interaction uses. **BodyWorld** plays on the real player body (other players can see it); **DedicatedLocalViewmodel** plays a self-contained rig under the local player's camera (only you see it).
- **Presenter**: the API component that executes a presentation - it swaps controllers, instantiates prefabs, and restores everything afterward.
- **Lease**: the API's ownership record for a resource (a player's body animator, or the local camera/arms). Conflicting requests are rejected or transactionally interrupted based on who holds the lease.
- **Manifest / schema-2**: the JSON file describing one interaction's assets and options. Schema 2 is the current strict format; schema 1 is the deprecated earlier format that still loads with a warning. See the [Manifest Reference](MANIFEST_REFERENCE.md).
- **Clean-room**: authored from scratch in this repository with primitive geometry - the examples contain no assets extracted from the game.
- **Arms metarig**: the game's separate first-person arms skeleton, nested inside the player hierarchy. Prop attachment bones resolve against it.

## Prerequisites

- .NET SDK 8 or newer (the `.slnx` solution format also requires a recent SDK / Visual Studio 2022 17.10+)
- Unity 2022.3.62f1 for the authoring project
- A BepInEx 5 Lethal Company profile for the final in-game check
- The repository cloned without any private tools or local game DLL references

## 1. Build and test the API

From the repository root:

~~~powershell
dotnet restore Y4NGZInteractions.slnx
dotnet build Y4NGZInteractions.slnx -c Release --no-restore
dotnet test Y4NGZInteractions.slnx -c Release --no-build
~~~

The game API and Unity compile assemblies come from public packages. A local game installation is not used by restore, build, or unit tests.

## 2. Build the clean-room example bundles

Open examples/UnityProject in Unity 2022.3.62f1. Use:

Y4NGZ Interactions > Build All Examples

The editor creates:

- a BodyWorld controller and clip authored against the clean-room proxy hierarchy;
- a DedicatedLocalViewmodel prefab with a custom rig, camera anchor, controller, simple prop, and clip;
- schema-2 manifests;
- AssetBundles in examples/GeneratedBundles.

The proxy uses primitive geometry and original repository-authored assets. It contains no extracted game model, texture, controller, clip, or consumer asset.

Run the Unity EditMode contract tests before leaving the project:

Window > General > Test Runner > EditMode > Run All

## 3. Build the sample consumer

~~~powershell
dotnet build examples/SampleConsumer/Y4NGZInteractions.SampleConsumer.csproj -c Release
~~~

The sample references the API project and public packages. Its build does not deploy automatically.

## 4. Install in a clean profile

Both DLLs go into the profile's plugin directory, `BepInEx/plugins/` (a subfolder per plugin is fine). Copy:

- src/Y4NGZInteractions/bin/Release/Y4NGZInteractions.dll
- examples/SampleConsumer/bin/Release/Y4NGZInteractions.SampleConsumer.dll
- the generated example bundles and JSON manifests, kept together in an `ExamplePayload` directory beside the sample DLL. The sample expects `body-world.manifest.json` and `local-viewmodel.manifest.json` plus the bundle directories inside it; if you place the payload elsewhere, set **Example Asset Root** in the sample's BepInEx config.

## 5. Exercise both paths

The sample owns its hotkeys and diagnostic logging; the production API has no hotkey dependency. The sample's hotkeys (full details in [the sample's README](../examples/SampleConsumer/README.md)):

- **F6** broadcasts the local-viewmodel example for the initiating player.
- **F7** broadcasts BodyWorld with RejectIfBusy.
- **F8** stops the last local handle.
- **F9** broadcasts BodyWorld with InterruptExisting.

Work through:

- Start the dedicated viewmodel example on the local player (F6).
- Start BodyWorld on the local player (F7).
- In multiplayer, broadcast the sample message and start BodyWorld for a remote player.
- Stop by handle (F8), repeat cycles, and test RejectIfBusy (F7) and InterruptExisting (F9).
- Confirm the completion event appears only after the camera, arms, rig, animator, and prop are restored.

## 6. Integrate

Copy the pack construction and request code, choose a stable PackId, and keep the returned handle. For multiplayer presentation, use your mod's networking so every relevant client calls TryStartInteraction with its local PlayerControllerB for the intended network player.

## Depending on the released package

When you ship a mod that uses this API, depend on the released package instead of building from source:

- **Thunderstore dependency**: add `y4ngz313-Y4NGZInteractions-1.0.0` to your mod's `manifest.json` dependencies so mod managers install the API automatically.
- **BepInEx load order**: declare the dependency on the plugin GUID so your plugin loads after the API is initialized:

~~~csharp
[BepInDependency("com.y4ngz.interactions", BepInDependency.DependencyFlags.HardDependency)]
[BepInPlugin("your.mod.guid", "YourMod", "1.0.0")]
public class YourPlugin : BaseUnityPlugin { ... }
~~~

  Calling the API before it initializes returns the stable failure reason `interaction_animation_api_not_initialized`.
- **Compile-time reference**: reference `Y4NGZInteractions.dll` from the downloaded Thunderstore package (or a source build) with `<Reference>` or an assembly directory convention; the DLL is the entire public surface. Set `Private=false` / `ExcludeAssets="runtime"` so your build does not copy a second copy of the DLL into your plugin folder.

Next: [Authoring Guide](AUTHORING_GUIDE.md).
