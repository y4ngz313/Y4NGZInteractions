# Handoff: live-body camera displacement guard rework

Date: 2026-07-18

## State

- **Implemented, built, auto-deployed, and statically verified; in-game behavior is unverified.**
- Branch remains `codex/real-first-person-presenter`; all rounds 4-8 work on top of `27e7aea`
  remains uncommitted. No commit was created and no existing work was reverted.
- New config key: section `Interaction Animation API V2`, key
  `Camera Displacement Guard Exempt Interactions`, default `y4ngz.cctv.operator`.
- The guard now captures its reference at TryStart before authored-controller application,
  compares entry state with vanilla player-local rest `(0, 2.35, 0.01)`, flags contaminated
  baselines, exempts configured interactions from guard stops, and stops non-exempt sessions only
  for genuinely new over-threshold displacement that worsens a contaminated entry state.
- Log variants: `baseline_captured`, `pre_existing_displacement`, `guard_exempt`, `stop`,
  `baseline_unavailable`, and `evaluation_unavailable`, all under
  `live_body.camera_displacement_guard`.
- Release build succeeded with zero warnings/errors and auto-deployed to the Gale test profile.
  Both Interaction Animation API V2 static regression scripts passed.

## Open questions

- Does the next CCTV playtest retain both operator sessions and emit `guard_exempt` instead of
  entering the restore path?
- Does a crossbow start on externally displaced slide residue emit
  `pre_existing_displacement` and continue without a false guard stop?
- Does a deliberately unsafe, non-exempt clean-baseline controller still emit `.stop` and run the
  proven restore path?
