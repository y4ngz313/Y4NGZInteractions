# Handoff: multiplayer round 2 â€” slide grounded, mantle grab improved, limbs go FLAT; frozen-cam spin fixed unverified

Date: 2026-07-23 (evening, after round-2 multiplayer test). Roles: Fable orchestrates/researches,
Codex implements (rescue tasks, xhigh, model gpt-5.6-sol; MUST launch with
`cd <target repo> && codex-companion.mjs task --write ...` â€” wrong cwd = repos not mounted,
missing `--write` = read-only sandbox refusal; both failure modes were hit and burned runs today).
Supersedes the open items of `2026-07-23-remote-third-person-rig-diagnosis-handoff.md` (its Â§2
diagnosis remains the campaign reference) and complements Codex's
`2026-07-23-third-person-rig-restore-handoff.md` (Fix J implementation detail).
Repos: Interactions, Upgrades, and Diagnostics (`C:\Lethal Company Modding\Diagnostics\Y4NGZDiagnostics`)
â€” ALL THREE carry uncommitted work; The maintainer must approve any commit.

## 1. Deploy profile change (affects every future build)

All three csprojs now deploy to Gale profile **`terrible`**
(`...\profiles\terrible\BepInEx\plugins\...`). They previously pointed at `test (for new mods)`,
which meant the first Fix Jâ€“N build never reached the playtest profile until this was caught.
The stale diagnostics copy in `test (for new mods)` was deleted; stale
`y4ngz-Y4NGZInteractions`/`y4ngz-Y4NGZUpgrades` folders there still exist (left in place, not
cleaned). MacBook side still needs the fresh DLLs by hand â€” Syncthing remains disarmed per
`MULTIPLAYER-SYNC-DIAGNOSIS-2026-07-23.md`.

## 2. Round-2 multiplayer verdicts (desktop observing `rickdesantis96`, log
`terrible\BepInEx\LogOutput.log` ~01:20â€“01:30 UTC; videos `Sliding01.mp4`,
`Mantle01.mp4`)

| Item | Verdict |
|---|---|
| Remote slide "flying in the air" | **FIXED** â€” video shows the body prone and low, sliding on the ground |
| Remote mantle grab position | **IMPROVED** (maintainer verdict) â€” grabs from the right place |
| Remote mantle/slide body pose | **NEW PRIMARY DEFECT: limbs/body go "incredibly flat"** during the animation â€” body reads as compressed/stiff/thin while rising along the wall (video frames ~1.3â€“2.3 s of Mantle01) |
| Fix M look-rig suppression | RUNNING â€” `[Panic Slide.lookrig] suppression_started` on every session, prior weights 0.45/1 captured |
| Fix N remote probe | RUNNING â€” `[RemoteRigDiff]` sampled remote weapon TP sessions (probeId=2, phases begin / restore+2LU / +5 s, present=6 missing=0) |
| Fix J `[RestoreSeam.tprig]` | **ZERO lines in the entire log â€” see Â§4, unverified and suspect** |
| Frozen/orbit debug cam | **BROKEN in round 2** â€” camera spun rapidly in circles; root-caused and fixed after the run (Â§5), fix deployed but UNVERIFIED |
| Post-session vanilla-anim corruption (bent leg / janky hands) | not explicitly re-verdicted this round â€” needs a dedicated check next time |

## 3. New defect: flat limbs during the animation (OPEN, undiagnosed)

Seen from the observer: during mantle (and to a lesser degree slide) the body silhouette
compresses â€” limbs thin/flat, pose stiff. Candidate hypotheses, none tested:

- Scale/space mismatch between the baked IK-target curves and the runtime rig (metarig lossy
  scale â‰ˆ1.122): targets landing too near/far â†’ constraints hyper-extend or collapse limbs, skin
  stretches thin.
- The remote grab-IK port (Fix L) FABRIK-solving arms every LateUpdate over the clip pose â€”
  straight-line solves flatten the arm silhouette (it blends by envelope, but the envelope now
  spans the whole session).
- FK-vs-IK composition generally (clip FK fights constraint solves toward clip-written targets
  if either is in the wrong space).
- Fix K duration change altering which part of the clip is visible (session no longer ends at
  the fixed constants; clip is NOT time-scaled to the received duration â€” only the session
  length changed).

Useful discriminators available next session: the (now fixed, unverified) frozen cam for
single-machine observation; disabling Fix L's remote grab-IK (it has no kill-switch yet â€” that
absence is itself a gap vs the every-gate-logs rule); comparing `[RemoteRigDiff]` limb values
mid-session vs the authored clip pose.

## 4. `[RestoreSeam.tprig]` produced no evidence â€” verify before trusting Fix J

The round-2 log contains `[RemoteRigDiff]` lines from the new Interactions DLL but **zero**
`[RestoreSeam.tprig]` lines, although remote API weapon-TP sessions ran (that is what
RemoteRigDiff sampled) and every tprig gate is supposed to log at Info, including skips.
Zero local API sessions ran this round (`camera_displacement_guard` count is 0), so the local
path is simply unexercised â€” but the remote sessions should have hit tprig gates. Possibilities:
a silent early-return before the first log, the capture site not on the path these sessions
took, or the new config keys resolving off. Reading the round-2 `[RemoteRigDiff]` values raises
a related question: `Rig 1/RightLeg/RightLeg_target` localPosition (1.436, 0.268, 0.894) vs
LeftLeg (0.99, 0.289, 0.939), euler X 53.5Â° vs 31.1Â°, unchanged between restore+2LU and +5 s â€”
asymmetry with no pristine reference to judge it against. The probe logs absolutes only; it
cannot yet answer "is this residue?" without the pristine baseline comparison.

## 5. Debug-cam spin: root cause + fix (deployed, unverified)

Round-2 symptom: enabling the new orbit made the camera spin rapidly in circles. Root cause
(certain, by inspection): the orbit derived its base rotation from `cameraTransform.rotation` â€”
the very transform the orbit wrote the previous LateUpdate. Vanilla rewrites camera pitch only,
not yaw, so each frame re-added the yaw offset on top of last frame's result â†’ continuous spin
at offset-per-frame rate. Fix applied in `DebugGUI.cs`: base view now derives from player state
(`player.transform.eulerAngles.y` + `player.cameraUp` pitch), never from the written camera
transform. Rebuilt and deployed to `terrible` 21:38 EDT. Same session also added: MMB-drag
orbit (look input suppressed while held), scroll distance 1â€“10 m, "Freeze Camera Here" static
spectator cam with optional track-player toggle; static mode keeps `ThirdPersonCam == true` so
the LedgeMantlePatch reflection integration still shows the body. Camera local rotation is
captured/restored on exit (same residue class as the slide camera-yaw bug). ALL of this is
untested in game â€” the spin consumed round 2's chance.

## 6. State ledger

- Implemented + round-2 VERIFIED: Fix K/L partially (slide grounded, grab position better),
  Fix M logging confirmed active.
- Implemented + UNVERIFIED: Fix J (tprig â€” zero log evidence, Â§4), Fix N Evaluate(0f), debug-cam
  orbit/freeze (post-fix), duration sync correctness on tall ledges.
- NEW OPEN: flat limbs (Â§3), tprig silence (Â§4), Fix L kill-switch absence, leg-target asymmetry
  interpretation (needs pristine-baseline diff in the probe output, not absolutes).
- Uncommitted: Interactions (rounds 4â€“9 + Fix J + docs), Upgrades (seam work + Fix K/L/M/N +
  csproj), Diagnostics (orbit/freeze cam + csproj). No commits made; maintainer approval required.
- Issues #70â€“#73 (Upgrades) remain open pending verdicts.

## 6b. ADDENDUM (2026-07-23 late, Fable cold-session onboard): duplicate stale
`Y4NGZInteractions.dll` found in the `terrible` profile â€” Â§4's tprig silence has a prime suspect

While investigating Â§4 statically, found **two copies of `Y4NGZInteractions.dll` in the profile**:

- `plugins\y4ngz-Y4NGZInteractions\Y4NGZInteractions.dll` â€” 207,360 B, built 20:58 EDT (fresh:
  contains `[RemoteRigDiff]`, `[RestoreSeam.tprig]`, and the new diagnostics_ready fields).
- `plugins\y4ngz-Y4NGZUpgrades\Y4NGZInteractions.dll` â€” 182,784 B, 15:23 EDT (STALE, recovery-era:
  string-scan shows **no `[RemoteRigDiff]`, no `[RestoreSeam.tprig]`** code at all). The current
  Upgrades csproj (`ProjectReference Private="false"`, deploy copies `$(TargetPath)` only) never
  produces this file â€” it is an orphan of the 15:23 recovery deploy that nothing overwrites.

The post-round-2 relaunch log (LogOutput.log starting 01:35:57 UTC â€” note it OVERWROTE the
round-2 log) shows BepInEx seeing the collision:
`[Warning: BepInEx] Skipping [Y4NGZInteractions 0.1.0] because a newer version exists
(Y4NGZInteractions 0.1.0)` â€” equal versions, so which file backs which consumer is
load-order/resolver luck. The chainloader plugin instance ran the FRESH binary (its
diagnostics_ready line carries `remoteRigDiffProbe=`, which the stale binary lacks), but
Y4NGZCompany's reflection bridge resolved the API at 01:36:20, BEFORE the Interactions plugin
loaded at 01:36:28 â€” i.e. name-based binding can hand consumers the OTHER assembly, giving two
live copies with separate static state (separate API registries, separate RestoreDiagnostics,
possibly double Harmony/rig writes). This class of split-brain can produce exactly the Â§4
signature (RemoteRigDiff fires, tprig silent) and is a plausible confounder for Â§3's flat limbs.

ACTION TAKEN: both stale files renamed in place to
`Y4NGZInteractions.{dll,pdb}.stale-quarantined-2026-07-23` (BepInEx ignores non-.dll). Full
profile-wide duplicate scan found no other duplicated assembly names.

Two real code gaps confirmed while reading (independent of the duplicate):
- `NotifyRemoteRigProbeSessionBegin` has NO `initialized` gate, while `PrepareForLiveBodyStart`
  silently early-returns on `!initialized` (RestoreDiagnostics.cs:395) â€” the ONLY tprig exit
  with no log line, violating the every-gate-logs rule. If a split/partial init leaves
  `logger` set but `initialized` false, you get RemoteRigDiff-without-tprig exactly.
- Every presenter-side tprig log routes through `context?.Logger` (consumer-supplied) instead
  of the static diagnostics logger (LiveBodyAnimatorPresenter.cs:1419-1568) â€” a second silent
  channel if remote-session contexts carry no logger.

NEXT-SESSION CHECKS: (1) relaunch must show NO "Skipping [Y4NGZInteractions" warning;
(2) re-run a remote session and re-verdict Â§4 before trusting Fix J either way; (3) if the
MacBook profile was hand-copied from desktop, delete the same stale DLL there too;
(4) have Codex add the `initialized` gate symmetry + static-logger fallback for tprig lines
(fold into the Fix L kill-switch task).

## 7. Open questions

- What makes the limbs flat â€” target-space/scale error, remote grab-IK, or composition? (Â§3
  discriminators.)
- Why is `[RestoreSeam.tprig]` silent while `[RemoteRigDiff]` fires on the same sessions?
- Is the right/left leg-target asymmetry residue or vanilla-normal? Needs the probe to emit
  diffs against the pristine capture instead of absolute TRS.
- Does the fixed orbit/freeze cam behave, and does frozen-cam observation reproduce the flat
  limbs single-machine (would massively shorten the iteration loop)?
- Post-session vanilla-animation corruption: re-verdict explicitly next multiplayer round.
