# Getting Started

Last updated: 2026-08-08

Run the supplied example before integrating the API into another mod. This separates authoring, packaging, networking, and runtime concerns while every component is still known-good.

## Prerequisites

- .NET SDK 8 or newer
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

Copy these files manually:

- src/Y4NGZInteractions/bin/Release/Y4NGZInteractions.dll
- examples/SampleConsumer/bin/Release/Y4NGZInteractions.SampleConsumer.dll
- the generated example bundles and JSON manifests

Keep the sample payload directory intact and configure the sample plugin's asset root to that directory if its default assembly-relative lookup does not match your layout.

## 5. Exercise both paths

The sample owns its hotkeys and diagnostic logging; the production API has no hotkey dependency.

- Start the dedicated viewmodel example on the local player.
- Start BodyWorld on the local player.
- In multiplayer, broadcast the sample message and start BodyWorld for a remote player.
- Stop by handle, repeat cycles, and test RejectIfBusy and InterruptExisting.
- Confirm the completion event appears only after the camera, arms, rig, animator, and prop are restored.

## 6. Integrate

Copy the pack construction and request code, choose a stable PackId, and keep the returned handle. For multiplayer presentation, use your mod's networking so every relevant client calls TryStartInteraction with its local PlayerControllerB for the intended network player.

Next: [Authoring Guide](AUTHORING_GUIDE.md).
