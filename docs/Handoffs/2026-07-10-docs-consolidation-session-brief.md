# Animation-docs consolidation â€” dedicated session brief (2026-07-10)

Maintainer-approved dedicated task. This brief carries state and open
questions only; the session that picks it up decides the approach.

## State

- Animation/retarget knowledge is fragmented across three repos, and
  sessions in one repo cannot see lessons learned in another. The
  heavy-pistol failure sequence (5 stalled sessions, gun scrapped
  2026-07-10) partly ran on this fragmentation.
- Where the knowledge lives today:
  - `Y4NGZUpgrades\docs\WORKFLOW_FP_ANIMATION_RETARGET.md` (~50 KB): Â§0
    execution card, Â§1 mental model, Â§2 environments, Â§3 pipeline stages,
    Â§4 universal lessons/landmines, Â§5 per-weapon log, Â§6 backlog incl.
    the stacking-compactness scrap-gate. Â§1â€“Â§4 and Â§6 are largely
    pipeline-universal; Â§5 is weapon/consumer-specific.
  - `Y4NGZCompany`: animation material in `docs\archive\` (Bundy victim
    bake runbook, FP handoffs) and the LC FP animation contract
    knowledge (also captured in Claude-side memory).
  - This repo: `LC_PLAYER_ANIMATION_API.md`,
    `LC_INTERACTION_ANIMATION_API_V2.md`, `ANIMATION_API_DECISIONS.md`,
    `ANIMATION_AUTHORING_PIPELINE.md`, `THIRD_PERSON_RIG_AUDIT.md` â€” the
    intended permanent home for pipeline/API-level knowledge.
- Convention (AGENTS.md all repos): universal pipeline/API lessons belong
  in Interactions living docs; consumer repos keep per-weapon/per-feature
  logs; any session using the retarget workflow updates Interactions docs
  when it learns something pipeline-level, regardless of source repo.
- Tracking issues: Y4NGZUpgrades and Y4NGZCompany each carry a GitHub
  issue for their side of the migration (filed 2026-07-10).

## Open questions

1. Which parts of Upgrades Â§1â€“Â§4/Â§6 are truly universal vs. secretly
   weapon-track-specific? (The proportion-mismatch numbers and metric-vs-
   gestalt lesson look universal; stage gates may be weapon-shaped.)
2. Does the split leave Upgrades' Â§0 execution card self-sufficient for
   weapon sessions, or does it need a pointer-only card that defers to
   Interactions docs?
3. Do the migrated lessons extend the existing Interactions root docs
   (per the docs/README.md judgment rule) or justify new use-type docs
   (e.g. an FP grip/contact doc)?
4. How do Company's archived Bundy/FP materials fold in â€” verified
   migration now, or deferred until a session touches them?

## Constraints

- Two live sessions may be mid-flight in Company/Upgrades â€” coordinate or
  run when they are done. Do not break the Upgrades workflow doc while a
  weapon session depends on it.
- Interactions has no GitHub remote; never create/push one unasked.
