# Sample Consumer

Last updated: 2026-08-08

This buildable BepInEx plugin demonstrates registration, both presentation kinds, handle-scoped stop, conflict policies, completion events, legacy-input hotkeys, and consumer-owned networking.

Build:

~~~powershell
dotnet build Y4NGZInteractions.SampleConsumer.csproj -c Release
~~~

Place body-world.manifest.json, local-viewmodel.manifest.json, and the generated bundle directories in an ExamplePayload directory beside the sample DLL, or set Example Asset Root in the sample config.

Hotkeys:

- F6 broadcasts the local-viewmodel example for the initiating player. Only that player's client invokes the local-only path.
- F7 broadcasts BodyWorld with RejectIfBusy. Every client resolves the target player and invokes the API locally.
- F8 stops the last local handle.
- F9 broadcasts BodyWorld with InterruptExisting.

The client sends requests only to the server. The server validates that the requester targets its own player, then fans the presentation message out to every connected client. Each receiving client invokes the local API.

The production API has no hotkeys or network messages.
