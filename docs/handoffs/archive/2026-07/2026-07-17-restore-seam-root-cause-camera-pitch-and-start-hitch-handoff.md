# Handoff: restore-seam flash ROOT-CAUSED â€” one-frame camera-pitch zeroing + start-seam frame hitch

Date: 2026-07-17. Roles: Fable orchestrates/researches, Codex implements (rescue tasks, xhigh).
Supersedes the investigation state in `2026-07-16-restore-seam-flash-knee-cam-handoff.md`
(rounds 1â€“6 history lives there and in Â§3 below). Branch `codex/real-first-person-presenter`;
rounds 4â€“6 are UNCOMMITTED on top of `27e7aea`. The maintainer must approve any commit (AGENTS.md).

## 1. The goal

Custom first-person live-body animations (crossbow, revolver, flamethrower, shotgun, tablet â€”
`y4ngz.*.livebody` sessions through Interaction Animation API V2 / `LiveBodyAnimatorPresenter`)
must start and stop with **zero visible artifact**: no flash, no black frame, no stutter, no
pose snap, at any look angle, standing or moving. Acceptance criteria are in Â§6.

## 2. THE ANSWER (playtest-6 evidence, log `research/logs/2026-07-17-playtest6-LogOutput.log`)

Round-6 instrumentation (rotations + start seam + extended window + visibility + frame timing)
found **two separate mechanisms**. Neither is the arm pose â€” that was fixed in rounds 4â€“5 and
is provably clean.

### 2a. END-OF-ANIMATION FLASH = one rendered frame with camera pitch forced to EXACTLY 0

`[RestoreSeam.render] phase=stop` camera world euler X (pitch), all three stops this run:

| stop frame | pitch on rendered stop frame | pitch next frame | perceived |
|---|---|---|---|
| 17493 | **-1.0E-05 (exact 0)** | 8.43Â° | flash |
| 17745 | **-1.0E-05 (exact 0)** | 19.07Â° | strong flash |
| 18505 | **-1.0E-05 (exact 0)** | 0.77Â° | barely visible |

The float value `-1.02E-05` is a written zero, not noise. The player's real pitch survives
(it's back the next frame); the **stop frame renders with the camera snapped to level**. The
whole screen shifts by the player's look pitch for exactly one frame. Magnitude = current look
pitch, which explains: (a) intermittency ("sometimes"), (b) total immunity to every arm fix
(rounds 1â€“5 changed nothing about it), (c) "flash/stutter" reading â€” at 19Â° the entire frame
jumps, and with the FP arms/sleeve filling the view a large jump can read as a black frame.

**Mechanism (high confidence, matches WP0 research + camera-pin history):** the restore path in
`LiveBodyAnimatorPresenter.Stop()` runs `AnimatorStateSnapshot` restore, which performs
`Animator.Rebind()`. Rebind resets every transform under the animator hierarchy â€” including
`gameplayCamera`'s local rotation, which is **script-driven, not animator-driven**: vanilla
`PlayerControllerB.PlayerLookInput()` writes `gameplayCamera.transform.localEulerAngles =
(cameraUp, â€¦)` once per frame. Our "Restore Camera Pin" (round 1â€“2 work) protects camera
**position** only â€” that's why position has been sub-millimeter clean since round 2 while
rotation was silently zeroed every stop. Nothing rewrites pitch until vanilla's next frame.
The rig-control pose restore (round 1) fixed exactly this class of bug for the RigArms subtree;
the camera's local **rotation** was the missed member of the same class.

### 2b. START-OF-ANIMATION STUTTER/BLACK FRAME = frame-time hitch, pose is clean

`phase=start` shows camera rotation and hand poses fully continuous across every session start
(no zeroing, no snap). But `deltaTime` spikes at the seam (baseline this run â‰ˆ 9 ms):

| session start | seam+0 | after+1 |
|---|---|---|
| 17381 | 29.0 ms | **66.5 ms** |
| 18323 | 10.1 ms | **58.6 ms** |
| 18940 | 9.3 ms | **35.8 ms** |
| 17637 | 9.4 ms | 13.2 ms |

A 35â€“66 ms frame is a visible hitch. Cost candidates at start: animator controller swap,
`RigBuilder.Build()` (rebinds constraint graph), prop `Instantiate` (first-time shader/mesh
upload), `Animator.Update(0)` + `Evaluate(0)`. Stop frames show the same shape but smaller
(13â€“20 ms at after+1). This is the "stutter"/"black frame at start" component. It is a
performance problem, not a correctness problem.

### 2c. What the other new columns ruled OUT

- Prop/held-item visibility: `animatedProp` stays fully rendered through the stop frame
  (deferred `Destroy()`), gone at after+1. No one-frame gap/overlap in weapon visuals.
  (`heldItem` read `present=false` throughout â€” `currentlyHeldObjectServer` plumbing returned
  null; minor gap, not worth chasing given 2a/2b.)
- FOV and near-clip: constant through both seams.
- Hand/metarig/localArms positions AND rotations: continuous at both seams (rounds 4â€“5 fixes
  hold; the round-5 glue runs `branch=update` every stop).

## 3. History â€” what was tried, what worked, what didn't

Full detail in `2026-07-16-restore-seam-flash-knee-cam-handoff.md` Â§Â§round-1â€“3 and the round
4â€“6 notes appended to it. Summary:

| Round | Change | Verdict |
|---|---|---|
| 1 | RigArms local-TRS capture at TryStart â†’ reapply after snapshot restore ("Restore Rig Control Pose") | **WORKED** for bug 2 (bent vanilla arms after custom anims) â€” user-confirmed fixed, keep on |
| 2 | Same-frame `RigBuilder.Evaluate(0f)` at start+restore; camera **position** pin re-keyed "Restore Camera Pin" default ON; render sampler added | **WORKED** for camera position (sub-mm since); did not touch the flash |
| 3 | "Restore State Mode" fresh/crossfade/replay, fresh default (no `Play()` replay) | **WORKED** â€” killed replay-teleport + non-looping-state stranding (playtest-2 knee-cam variant). Keep fresh |
| 4 | Fix A (vanilla LateUpdate arm glue in Stop) + Fix B (pristine-baseline rig-control restore) | Fix B **WORKED** (hand jump 34 cm â†’ 10â€“25 cm; kills contaminated equip-time captures). Fix A silently no-oped â€” wrong vanilla branch |
| 5 | Dual-branch glue (`localArmsMatchCamera` is FALSE in normal play â†’ vanilla's Update-branch metarig-forward glue; snapped rotation) + mandatory gate logging | **WORKED** at what it targeted â€” stop-seam hand positions < 2 cm â€” but user-visible flash unchanged (it was the camera pitch all along) |
| 6 | Instrumentation only: rotations, start seam, stop..+5 window, prop/item visibility, frame timing, FOV/nearClip | **DECISIVE** â€” produced Â§2 |

Hard-won gotchas (do not relearn these):
- **BepInEx persists saved config values** â€” flipping a code default does nothing for existing
  keys; a default change requires a key RENAME (this cost us a round).
- **Silent early-return gates in seam-critical code hide no-ops** â€” round 4's glue never ran
  and nothing said so. Every gate now logs; keep it that way.
- **`localArmsMatchCamera` is a serialized prefab field, false in normal play**; vanilla never
  assigns it in `PlayerControllerB`. Vanilla FP arm glue in normal play is the **Update**-branch
  (`localArmsTransform.position = armsMetarig.position + armsMetarig.forward * -0.445`, rotation
  lerped 15Â·dt toward `localArmsRotationTarget`), NOT the LateUpdate camera-relative branch.
  Decompiled copies: `research/decompiled/PlayerControllerB.cs` lines ~6108â€“6133, ~7578, ~7702.
- Stop() runs from the coordinator LateUpdate with **undefined order vs
  `PlayerControllerB.LateUpdate`** â€” never assume vanilla per-frame writes have or haven't
  happened on the stop frame; re-apply them synchronously if the rendered frame needs them.
- Knee-cam (camera at knees, floating arms) is **not** this bug and mostly not this repo:
  playtest-3 proved one variant pre-existed under the vanilla controller (suspect: Upgrades
  Panic Slide exit residue â€” Fix D below); the other variant (replay-mode state stranding) is
  already dead via fresh mode.

## 4. Fix plan, ranked

**Fix E â€” restore camera local rotation across the seam (the flash; do this first).**
In `Stop()`, capture `gameplayCamera.transform.localRotation` (and
`cameraContainerTransform.localRotation` for completeness) BEFORE the animator snapshot
restore/Rebind, and re-apply AFTER restore + rig rebuild, alongside the round-5 arm glue
(i.e., before the final `Animator.Update(0f)` + `RigBuilder.Evaluate(0f)`). Alternative
equivalent: re-apply vanilla's own look equation (`localEulerAngles = (player.cameraUp, y, z)`)
â€” but prefer capture/reapply of the live pre-restore rotation: it is exact, needs no vanilla
field semantics, and mirrors the proven round-1 pattern. Also audit `TryStart()` for the same
Rebind-era zeroing (start seam showed no rotation artifact this run â€” likely because the
authored controller takes over the camera immediately â€” but capture/reapply at start is cheap
symmetry). Config kill-switch, NEW key name, default ON; log applied values.
Acceptance: `phase=stop` `cameraWorldEuler` X delta between seam+0 and after+1 < ~0.5Â° while
holding a steep look angle (was up to 19Â°).

**Fix F â€” start-seam hitch (the stutter).** Measure first inside the start path: bracket
controller swap, `RigBuilder.Build`, prop `Instantiate`, `Update(0)`/`Evaluate(0)` with
`Stopwatch` and log ms per phase (one line per start). Then attack the biggest cost:
likely candidates are prop instantiate (prewarm: instantiate once at equip and toggle
renderers, or `Shader.WarmupAllShaders`/keep-alive pool) and double rig Build. Acceptance:
seam-window max `deltaTime` within ~1.5Ã— the surrounding baseline; no dropped-frame feel on
session start. NOTE: stop seam has a milder version (13â€“20 ms) â€” same instrumentation will
show whether the same cost dominates.

**Fix C (carried) â€” guard/pin entry-displacement handling.** When Stop-entry pose is already
displaced (guard fired at session start), don't re-assert the displaced pose on pin release;
restore toward vanilla rest; reword the guard log so it doesn't blame the authored controller
when displacement pre-existed. (Playtest-3 stop-2 autopsy.)

**Fix D (carried, Upgrades repo) â€” Panic Slide / movement-presentation exit residue watchdog.**
When no session is active and `!inSpecialInteractAnimation`, rate-limited log if
`cameraContainerTransform` local deviates > 5 cm from rest `(0, -0.012, 2.096)`. Genesis
tracker for the external knee-cam; do NOT auto-correct from Interactions.

Also carried: the slide/mantle black frames the user reports are in the **Upgrades**
`SlideAnimationBridge` path (separate system; it calls `TryStopPlayerInteractions` first).
After Fix E/F land here, port the same camera-rotation-restore + hitch lens there â€”
same classes of bug, different owner.

## 5. Current state (deployed to Gale "test (for new mods)" profile, UNCOMMITTED)

- Rounds 4â€“6 all live: pristine-first rig restore, 3-branch vanilla glue with gate logging,
  full seam instrumentation (`[RestoreSeam.render] phase=start|stop`, pos+euler for
  camera/hands/metarig/localArms, deltaTime/unscaledDeltaTime, FOV/nearClip, prop/heldItem
  visibility blocks).
- Config: `BepInEx/config/com.y4ngz.interactions.cfg`, section "Interaction Animation API V2
  Restore Diagnostics". Keys of note: `Restore Rig Control Pose`, `Restore Pristine Rig Control
  Pose`, `Restore Vanilla Arms Glue`, `Restore Camera Pin`, `Restore State Mode` (=`fresh`),
  `Enable Restore Seam Frame Logger`. Numpad* = manual RigDiff dump.
- Build: `dotnet build ./src/Y4NGZInteractions/Y4NGZInteractions.csproj -c Release`
  (auto-deploys). Regression scripts: `scripts/test-interaction-animation-api-v2-static-regressions.ps1`
  and `...viewmodel-presenter-static-regressions.ps1` â€” both green as of round 6.
- Preserved playtest logs: `research/logs/2026-07-16-playtest3-LogOutput.log` (arm-jump smoking
  gun), `2026-07-17-playtest4-...` (glue silent no-op), `2026-07-17-playtest5-...` (branch=update
  + clean hands), `2026-07-17-playtest6-...` (camera-pitch zero + start hitch â€” THE evidence).
- Uncommitted inventory: rounds 4â€“6 source + docs + logs on `codex/real-first-person-presenter`
  atop `27e7aea`. Suggested commit split: (1) rounds 4â€“5 fixes, (2) round-6 instrumentation,
  (3) docs + logs (or gitignore the logs â€” the maintainer's call, still open from last handoff).

## 6. Verification protocol (next playtest, after Fix E)

1. Build Release (auto-deploys). In-game: use crossbow/revolver/flamethrower/shotgun repeatedly
   **while looking steeply down (~20Â°+) and up**, standing and walking â€” the pitch flash scales
   with look angle, so steep angles are the sensitive test.
2. Flash check: `grep "phase=stop" | cameraWorldEuler` X â€” seam+0 vs after+1 delta < 0.5Â°.
   User-visible: no flash at any look angle.
3. Regression checks (must all stay green): `armsglue gates` lines present with
   `branch=update`; hand.R/L world pos deltas < 2 cm across the stop seam; `rigpose`
   `pristineBaselineUsed=True`; no `camera_displacement_guard` at session start.
4. Hitch check (Fix F): per-phase ms log at start; `deltaTime` at seam â‰¤ ~1.5Ã— baseline.
5. Slide/mantle: expect their artifacts UNCHANGED by Fix E/F (different code path) â€” if they
   improve, that's information (shared mechanism), note it.

## 7. Open questions for the maintainer

- Commit approval for rounds 4â€“6 (and the split above); playtest logs in-repo or gitignored?
- After Fix E: is the remaining start hitch acceptable short-term, or is Fix F immediately next?
- Slide/mantle (Upgrades) â€” schedule the port of Fix E/F equivalents into `SlideAnimationBridge`
  as its own task?
