# Handoff: multiplayer round 3 â€” clean-assembly run; slide float and mantle deflation root-caused and fixed (deployed, UNVERIFIED)

Date: 2026-07-24 (early, after round-3 multiplayer test on 2026-07-23 ~02:55 UTC). Roles: Fable
orchestrates/researches, Codex implements. Supersedes the open items of
`2026-07-23-multiplayer-round2-flat-limbs-and-frozen-cam-handoff.md` (its Â§3 hypotheses are now
resolved; its Â§6b duplicate-DLL addendum is CONFIRMED FIXED â€” round-3 launch log has no
`Skipping [Y4NGZInteractions` warning).

## 1. Round-3 verdicts (desktop observing `rickdesantis96`; log preserved at
`LogOutput-round3-2026-07-23.log`; video `Combined mantle + slide.mp4`)

| Item | Verdict |
|---|---|
| Duplicate stale Interactions DLL | QUARANTINE HELD â€” clean single-assembly launch; round-3 defects are real behavior, not stale-DLL artifacts |
| Remote slide body | STILL FLOATS â€” horizontal but hovering (~0.3â€“0.5 m) on grassy slopes; round-2 "grounded" verdict was flat-ground-only |
| Remote mantle body | STILL DEFLATES ("a tad"; was "incredibly flat") while rising along the wall |
| Remote mantle placement | Improved, "maybe not perfect" (maintainer verdict) |
| Fix K duration sync | WORKING â€” all 8 mantle start_gates: payload='extended', durationSource='received' |
| Fix L body grab | ACTIVE â€” bodyGrabEnabled=True every mantle |
| Fix M look-rig suppression | ACTIVE â€” 12/12 slide sessions |
| Fix J `[RestoreSeam.tprig]` / Fix N `[RemoteRigDiff]` | NOT EXERCISED â€” zero weapon/API live-body sessions ran this round; still unverified, needs a weapon-TP round |

## 2. Root causes (Codex, session 019f921d-f671-70c3-bac5-eb4cd6628f14, task complete 03:28 UTC)

- **Slide float:** no terrain conformance existed â€” remote body height came purely from the
  vanilla-synced player root; flat ground masked it in round 2, slopes exposed it.
- **Mantle deflation = round-2 handoff hypothesis (b):** the remote grab-IK's
  unreachable-target FABRIK branch forced shoulderâ†’elbowâ†’hand into a straight line at full
  envelope weight, flattening the arm silhouette (LedgeMantlePatch.cs, old solve ~line 1077).
  Hypothesis (a) scale mismatch RULED OUT (arm lengths and grip targets are world-space);
  (d) duration sync RULED OUT (only bridge auto-end changes; animator speed stays 1).
- **body_grab_end_gate "linger" DISPROVED:** round-3's short mantle auto-ended at 02:55:54.308
  before its network stop arrived at 02:55:54.670 â€” correct lifecycle, not a leak. An explicit
  `stop_received` end gate was added for clarity.

## 3. Fixes implemented (Upgrades repo, deployed to `terrible` 23:26 EDT, SHA-256 of built ==
deployed: A104A5FF...2AE0A8 â€” ALL UNVERIFIED IN GAME)

- **Remote slide terrain conformance** (PanicSlidePatch.cs ~1052, presenter ~1203):
  presentation-only â€” restores the remote metarig baseline each LateUpdate, raycasts the ground
  mask (start +1.5 m, 4 m down), snaps the visual root to ground +2 cm, slope-aligns via
  smoothed terrain normal (exp response 14). Never moves the synced gameplay root. Gates:
  ground_miss / presentation_root_missing / config_disabled / terrain_end / terrain_clear.
- **Bend-preserving remote mantle grab solve** (LedgeMantlePatch.cs ~848 gate, ~1124 solver):
  two-bone solve clamped to 92% of combined arm length, preserves the clip's elbow side. Gates:
  envelope_zero / bones_missing / solve_skipped / applied[_bend_preserving_clamped].
- **New config keys, both default ON** (Plugin.cs ~263): `Panic Slide / Remote Slide Terrain
  Conformance`, `Panic Slide / Remote Mantle Body Grab IK` (the previously missing Fix L
  kill-switch).
- Build: `dotnet build -c Release` 0 warnings 0 errors. Nothing staged or committed (the maintainer
  approves commits). Codex opened Upgrades issues **#74â€“#76**, open pending verdicts; proposed
  checkpoint commit message: `fix: ground remote parkour presentations`.
- Note: the supplemental regression script is stale (searches for an obsolete
  `internal void Tick()` signature) â€” separate cleanup item.

## 4. Tooling notes

- BepInEx OVERWRITES LogOutput.log every launch â€” round-2's log is gone; round-3's copy lives in
  ``. Copy the log out after every multiplayer round before relaunching.
- Codex companion bug hit this round: the job finished at 03:28 but the companion never captured
  the final message and stayed "running" forever. Recovery: read the session rollout directly
  (`~/.codex/sessions/<date>/rollout-*<codex-session-id>.jsonl`, last `agent_message`), then
  cancel the zombie job. The failed `task-mrydmua4-vospqe` was a stray duplicate resume â€” ignore.

## 5. Open items for round 4

- Re-verdict on a SLOPE: remote slide grounded + slope-aligned? Mantle silhouette full?
  (`[Panic Slide.remote-slide]` and body-grab gates now log every path.)
- Run at least one remote WEAPON third-person session so Fix J tprig / Fix N RemoteRigDiff
  finally produce evidence (still zero across all rounds).
- Interactions logging gaps (small Codex task, NOT yet implemented): add `initialized` gate to
  NotifyRemoteRigProbeSessionBegin (RestoreDiagnostics.cs ~633) matching the silent
  `!initialized` return at ~395, and give presenter tprig logs a static-logger fallback
  (LiveBodyAnimatorPresenter.cs 1419â€“1568 use consumer `context?.Logger` â€” silent-drop channel).
- Debug freeze-cam / MMB orbit: fixed post-round-2, STILL UNVERIFIED (round 3 didn't use it).
  Single-machine slide/mantle observation via freeze-cam remains the fast iteration loop.
- MacBook: hand-copy the fresh `Y4NGZUpgrades.dll` (23:26) â€” protocol unchanged, so the old Mac
  DLL interoperates for desktop-side observation, but keep the machines matched. Check the Mac
  profile for the same stale `Y4NGZInteractions.dll` duplicate in its Upgrades folder.
- Post-session vanilla-anim corruption (bent leg / janky hands): STILL needs an explicit
  re-verdict â€” not checked in round 3 either.
- Leg-target asymmetry / pristine-baseline diff in the RemoteRigDiff probe: still absolutes-only.
