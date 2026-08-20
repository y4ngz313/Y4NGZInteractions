# Documentation

Last updated: 2026-08-19

This private directory contains the living 1.0 development and authoring
documentation for the standalone animation API. These files are not copied into
the Thunderstore package.

- [Getting Started](GETTING_STARTED.md): build and run the supplied examples first.
- [Authoring Guide](AUTHORING_GUIDE.md): build both payloads and replace a sample clip.
- [API Reference](API_REFERENCE.md): registration, requests, leases, lifecycle, parameters, and events.
- [Manifest Reference](MANIFEST_REFERENCE.md): strict schema 2 and schema-1 migration.
- [Lethal Company Rig Reference](LETHAL_COMPANY_RIG_REFERENCE.md): BodyWorld hierarchy and binding constraints.
- [Advanced Prop Recipe](ADVANCED_PROP_RECIPE.md): generic prop, attachment, and clip-pack workflow.
- [Troubleshooting](TROUBLESHOOTING.md): stable failure codes and restoration diagnostics.
- [Architecture](ARCHITECTURE.md): current invariants and ownership model.
- [1.0 Migration Guide](MIGRATION_1_0.md): deliberate pre-1.0 contract changes.

## Handoff lifecycle

Pending work, acceptance criteria, investigations, and transient evidence belong
in private GitHub issues.

- `docs/handoffs/active/` contains only work actively paused on an open issue.
  Each handoff records its issue and parent, branch or PR, last verified state,
  evidence, and open questions. It never prescribes the next mechanism.
- Resume or resolution consumes the active handoff. Move historical handoffs to
  `docs/handoffs/archive/YYYY-MM/`; durable behavior belongs in the indexed
  living documents above.
