# Y4NGZInteractions

Y4NGZInteractions is a standalone local animation presentation, ownership, and restoration API for the Lethal Company modding community.

It provides two consumer-authored presentation paths:

- BodyWorld for live player-body animation visible to local and remote observers.
- DedicatedLocalViewmodel for arbitrary self-contained camera-local rigs.

The API manages resource conflicts, controller/presenter preflight, deterministic lifecycle, and restoration. It is not a networking or runtime retargeting service. Consuming mods must replicate their own interaction facts and invoke the API on every observing client.

Documentation, clean-room examples, authoring tools, source, and validation guidance are available at the project website.

This package contains only the runtime DLL and standard package metadata.
