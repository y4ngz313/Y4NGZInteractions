# Documentation index

Last updated: 2026-08-05

## For mod authors using the API

| Document | What it is |
|---|---|
| [GETTING_STARTED.md](GETTING_STARTED.md) | Walkthrough: project setup, the artefacts you need, registering a pack, starting and ending an interaction, the networking contract, troubleshooting |
| [API_REFERENCE.md](API_REFERENCE.md) | Every public method and type, handle lifetime, preload, the full failure-reason catalogue, stability expectations |
| [MANIFEST_REFERENCE.md](MANIFEST_REFERENCE.md) | Complete manifest schema, field by field, with annotated examples |
| [ANIMATION_AUTHORING_PIPELINE.md](ANIMATION_AUTHORING_PIPELINE.md) | How to author and retarget animation content for this API |
| [THIRD_PERSON_RIG_AUDIT.md](THIRD_PERSON_RIG_AUDIT.md) | Reference audit of Lethal Company's player rig — bone names, hierarchy, controller bindings |

## For maintainers of this repository

| Document | What it is |
|---|---|
| [internal/RESTORE_SEAM_INTERNALS.md](internal/RESTORE_SEAM_INTERNALS.md) | The restore seam: ownership, restore scopes, kill-switch and diagnostic config catalogue, log-marker glossary, debug probe usage |
| [ANIMATION_API_DECISIONS.md](ANIMATION_API_DECISIONS.md) | Decision log (ADRs). Read before proposing runtime changes; `SUPERSEDED` entries are kept so old reasoning is not re-litigated |
| [WEAPON_RETARGET_PIPELINE.md](WEAPON_RETARGET_PIPELINE.md) | Source of truth for the weapon use type: first-person weapon retarget, animated props, third-person derivation. Specialises `ANIMATION_AUTHORING_PIPELINE.md`; read that one first |
| [Handoffs/](Handoffs/) | Dated, write-once session handoffs |

## Lifecycle rules

| Location | What lives here | Lifecycle |
|---|---|---|
| `docs/` | Living docs, one per animation use type or workflow | Updated in place; stale or overturned claims are deleted, never appended to |
| `docs/internal/` | Maintainer documentation for internals that are not part of the public contract | Same as living docs |
| `docs/Handoffs/` | Dated session handoffs (`YYYY-MM-DD-topic-handoff.md`) | Written once, never edited. State and open questions only — never a prescribed next step |

Every living doc carries a `Last updated: YYYY-MM-DD` stamp, bumped on every edit. If a change
alters behaviour a doc describes, updating that doc is part of finishing the work. Contradicted
claims are deleted on sight; corrections are not appended alongside the thing they correct.

Before creating a document, decide whether the work fits an existing use type. Extend the existing
living document unless the task introduces a genuinely new one — never spawn a near-duplicate.

The retired v1 PlayerAnimationApi source, documentation, regression script, and probe bundle are
retained under `_archive/player-animation-api-v1/`, outside the build.

## Source-of-truth boundary

This repository is a consumer-agnostic API. Pipeline documents here describe *how* to author and
retarget; they never hold a consumer's measured values. Per-asset offsets, pivots, composition
scales, prop attachments, curls, landmark poses, and bundle hashes belong in the consuming mod's
own repository, as do its clips, controllers, manifests, props, VFX, audio, and deploy entries.

Nothing in this repository is a prerequisite for a third-party consumer beyond the three
consumer-facing documents listed above. The Y4NGZ-internal weapon workflow lives in the consumer
repository that owns those weapons.
