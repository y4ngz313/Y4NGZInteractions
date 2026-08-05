# Weapon retarget pipeline — source of truth

Last updated: 2026-08-05

> **Internal authoring methodology (maintainer reference).** External modders: see
> `docs/GETTING_STARTED.md` and `docs/MANIFEST_REFERENCE.md`. The authoring tools referred to
> below (the retarget baker, the preview renderer, the source-prefab builder) live in the
> maintainer's Unity projects and consumer repositories and are **not shipped with this API**.
> The gate concepts and the failure analyses transfer to any tooling; the specific tools do not.

This is the **sole source of truth for retargeting weapon animations** onto the Lethal Company
player: first-person arms, animated weapon props, and third-person body derivation.

It is a specialization of `ANIMATION_AUTHORING_PIPELINE.md` for the weapon use type. Read that
document first for the generic runtime-ownership contract, restore path, and validation rules that
apply to every animation consumer. This document adds only what is weapon-specific.

## Why this document lives here

Weapon retargeting is an animation *pipeline* concern, and this repository is the home for animation
pipeline knowledge (`docs/README.md`, "Living docs"; `AGENTS.md` records consolidating consumer
animation docs into this repo as a planned direction).

**Nothing about this document changes the ownership boundary.** Every weapon asset, bundle, clip,
controller, manifest, finger sidecar, prop, VFX, material, audio file, gameplay script, and deploy
entry lives in the consumer mod — for Y4NGZ weapons that is **Y4NGZUpgrades**. No weapon code and no
weapon assets are ever added to Y4NGZInteractions. This repository contributes the generic API and
this pipeline knowledge, nothing else.

Consumer-side weapon systems (ammo, VFX, gameplay, per-weapon status, landmarks) are documented in
the consumer. For Y4NGZ that is `Y4NGZUpgrades/docs/WORKFLOW_Y4NGZ_WEAPONS.md`.

---

## How to update this document

**Updating this document is part of finishing any weapon retarget task.** It is not optional and
not deferred to a later cleanup pass.

### When to write here

Write here when you learn something that would be **true for the next weapon**, on any rig, for any
consumer:

- a retarget mechanic that worked, and the geometry/measurement conditions under which it worked;
- a mechanic that failed, why it failed, and what evidence proved the failure;
- a validation gate that caught a real fault, or that passed while the result was visibly wrong;
- a preview-versus-runtime divergence, and how it was measured;
- a source-format property that changed how the pipeline had to be driven;
- a third-person derivation result on the LC body rig.

### When NOT to write here

- **Per-weapon values** — camera offsets, Euler, pivots, composition scale, prop attachment tuples,
  curl angles, bundle hashes, landmark poses. Those are consumer evidence. Record them in the
  consumer's status doc and evidence packet.
- **Consumer gameplay** — ammo, damage, reload semantics, networking, item registration, VFX wiring.
- **Anything not yet confirmed.** An untested idea is not pipeline knowledge. Put open questions in
  the section reserved for them at the bottom, or in a dated handoff.

### How to write here

- Bump `Last updated` on every edit.
- **Delete overturned claims on sight.** No appended corrections, no "previously we thought", no
  contradictions left standing. If a rule here turns out to be false, remove it and state what
  replaced it.
- Mark every claim's evidence class. Use these exact labels so a cold session can tell what it can
  trust:
  - **PROVEN IN GAME** — a human gave an explicit in-game verdict.
  - **MECHANICALLY VERIFIED** — a tool/gate confirmed data moved correctly. Does **not** approve a
    visible pose.
  - **UNVERIFIED** — implemented and deployed, no verdict yet.
  - **REJECTED** — failed in game. Record why, so it is not retried by accident.
- A rejected mechanism may inform a diagnostic. It must never become a default or an implied next
  step.

---

## Scope of the weapon use type

A complete weapon retarget delivers all of these, and they are judged together:

| Element | Owner |
|---|---|
| First-person arm/wrist IK-target clips | consumer bundle |
| Finger pose (runtime final-frame owner) | consumer sidecar JSON |
| Animated weapon prop + its articulated descendants | consumer bundle |
| Shell/override controller | consumer bundle |
| Live-body manifest | consumer |
| Third-person body clips (`LeftArm_target` / `RightArm_target`) | consumer bundle |
| World-model grip/muzzle landmarks | consumer (see consumer doc) |
| API session, ownership, restore, bool/int/trigger | this repository |

---

## Source format matters, and it changes the pipeline

The pipeline was originally built around a **single combined take** per weapon — one FBX containing
every action, requiring manual visual segmentation before authoring.

A **pre-segmented source** (individually named clips per action, with weapon prop animation shipped
as separate clips) changes several gates. Both formats are supported; determine which you have
before planning the work.

### Combined-take source

- Gate 1 requires visual classification of every candidate segment before semantics are assigned.
  Numeric motion intervals cannot tell a draw from an inspect from a melee tail.
- The weapon transform `W(t)` must be **derived** from the prop attachment you solved, so retarget
  error propagates into any third-person derivation built on it.
- Excluded segments (flourishes, inspects, unproven tails) must be recorded explicitly.

### Pre-segmented source

- Gate 1 semantic classification is close to free — the clip names carry the semantics.
- `W(t)` is **authored**, available directly as a prop clip curve.
- Action boundaries are identical between first-person and any derived third-person clip by
  construction.

**MECHANICALLY VERIFIED, 2026-07-26** — a commercial FPS animation pack, inventoried as the
pre-segmented source for the Y4NGZ weapons, splits its clips by rig role, three sets per weapon:

| Set | Rig / target | Role |
|---|---|---|
| first-person arms | an **arms-only** skeleton, not a full body | first-person arms |
| weapon prop | the weapon FBX's own root, with named articulated descendants | animated prop |
| camera | a camera transform | camera-space kick/sway |

The camera set must **not** ship. Camera curves in a shipped clip are a publication blocker under
`ANIMATION_AUTHORING_PIPELINE.md`. They are reference material only.

Inventory the pack's clip-naming convention, its arms skeleton, and each weapon's animated prop
descendants before planning the work. Those are per-pack, per-weapon facts and belong in the
consumer's evidence packet, not here. Curve bindings — not hierarchy names — remain the evidence
that a descendant is animated.

### Sources whose runtime does not consume an authored fire action

**MECHANICALLY VERIFIED, 2026-07-26; corrected 2026-08-01** — a source pack may omit a
first-person fire clip, or may ship one while its own runtime still selects procedural recoil.
A first inventory pass concluded that only one weapon in the pack had a first-person fire take;
re-measurement disproved it — another weapon's fire clip is a real authored take with measurable
arm motion, while that weapon's own settings asset disables fire-clip playback. **Clip existence
and the source runtime's consumption choice are separate facts and both must be inventoried, per
weapon, by reading the settings asset as well as the clip list.** The measured per-weapon values
are consumer evidence.

The existing rule already covers this and is unchanged: **never invent or loop an action that is not
visibly authored.** If no valid firing pose exists, hold the approved Hold pose while the prop fire
clip, camera kick, and VFX carry the shot. If a real but source-disabled fire take exists, it may be
retargeted as an explicit candidate, but preview and live testing decide whether it replaces the
procedural treatment. Do not relabel a reload or inspect as fire.

Procedural recoil data and camera-shake curves are **consumer gameplay data**, not animation
content. Port the values into the consumer's own recoil system; do not port a third-party runtime,
and do not bake them into clips.

---

## First-person retarget

Everything in `ANIMATION_AUTHORING_PIPELINE.md` §"Retarget mechanics and validation rules" applies
unchanged. The weapon-specific additions:

- Determine which hand owns the prop before authoring. Rotating the owning hand rotates the prop;
  it is never an isolated glove adjustment unless the attachment receives the equal-and-opposite
  compensation.
- Measure prop-to-hand scale and attachment basis **per weapon**. Never inherit another weapon's
  camera offset, pivot, composition scale, or attachment. **This rule was violated on 2026-07-27
  and the cost is recorded below — enforce it with a gate, not a comment.**
- Evaluate left and right hands independently.
- The finger pose applied in the runtime's final frame is the owner of the visible glove. Preview
  evidence that stops after clip sampling is not finger evidence.

### A source rig that carries its own viewmodel camera has already composed the shot

**MECHANICALLY VERIFIED, 2026-07-27.** Sources split into two classes, and they need opposite
treatment. Deciding which class a new source belongs to is the **first** composition question, before
any offset is chosen:

- **Authored-viewmodel sources** ship a real first-person camera alongside the arms (an FPS
  animation pack's own player prefab, for example). Sampling the arms in that camera's frame
  reproduces the author's intended composition. **No *hand-authored* camera offset is permitted for
  these** — but the composition must still be *transferred*, not copied verbatim. See
  "An authored composition is a picture, not a set of distances" below; inheriting the metres is
  what shipped the 2026-07-28 rejection.
- **Bodyspace sources** (a DCC arm rig with no camera) have no authored composition. Here the offset
  is *synthesizing* a camera placement, and it has to be derived per weapon.

Copying an offset from a bodyspace weapon onto an authored-viewmodel weapon is the failure mode this
rule exists to stop. Measured instance: an authored-viewmodel source placed the grip wrist **0.2161 m**
from the eye; the inherited offset moved it to **0.7732 m** — a factor of **3.58** in distance, so the
weapon subtended **56 %** of its intended size and the hand **36 %**, low and off-axis. Nothing in the
bake reported a problem, because every per-clip gate measured reach and pose validity, not
composition.

> Two numbers in this paragraph were wrong until 2026-07-28 and are corrected above, because the
> error changed what the next session did. The old text read "0.126 m → 0.626 m, a factor of 5, about
> a fifth of intended size". 0.626 is the **z-component**; the gate measures a **magnitude**, and the
> magnitude was `|(0.151, −0.428, 0.626)| = 0.7732 m`. Solving for the wrong ratio is why the
> correction over-shot: the team pulled the composition in by 3.58× to "restore the authored 0.126",
> when the correct pull-in was 2.00×. The residual over-correction was exactly the rig-scale factor.

**Gated, 2026-07-27.** Two checks now enforce this at bake time, and both were fired against the
known-bad value and refused it. Note that the rule above was already written here *before* the fault
shipped — prose did not prevent it, which is the whole argument for the gates:

1. **Source class is a field, not a comment.** The profile declares which class it is, and an
   authored-viewmodel profile whose offset is nonzero fails the bake. Declaring the class is now
   unavoidable, so "which class is this source?" gets asked for every new weapon.
2. **No two profiles may share a nonzero offset.** A camera offset is either measured from one rig or
   judged for one weapon; two weapons agreeing to seven decimals is copy-paste. This catches the
   inheritance directly, independent of class.

Both refuse *before* any sampling, so a rejected profile publishes nothing.

**The eye-distance band is a weaker gate than it looks, and belongs to one class only.** Asserting
one band for everything is wrong: measured across the shipping set, two bodyspace consumers sit
well outside any tight band (0.5085 m and 0.6474 m) with in-game verdicts behind them. For a bodyspace source the
distance *is* the composition — derived, then judged — so a bake failing it would be arithmetic
overruling a playtest. For an authored-viewmodel source the distance is inherited from a frame an
author already composed, so landing outside the band means something broke. **Bind it for
authored-viewmodel, report it as advisory for bodyspace.** Report the number either way: it is the one
value a composition pass is actually tuning, and printing it is most of the benefit.

### An authored composition is a picture, not a set of distances

**MECHANICALLY VERIFIED, 2026-07-28.** This is the explanation for six consecutive sessions of
"verified offline, rejected in game" on one weapon, in both directions.

A composition is *metres and field of view*. The pipeline reasoned entirely in metres. Until
2026-07-28, `grep -ci fieldOfView` returned **0** across the 217 KB baker, the source-prefab builder,
and the mod's runtime `Weapons/` folder. `Mathf.Tan` appeared **0** times. No screen-space quantity
was computable anywhere in the pipeline, so no offline number could predict apparent size.

Two independent errors, both multiplicative, both invisible to every gate:

1. **Rig scale is compensated in size but not in distance.** LC's first-person hand measures
   **1.3878×** the source pack's. The baker correctly grows the prop by that ratio so the glove
   can close on the grip — and it grows the prop-to-wrist offset by it too, making the delivered
   viewmodel an exact 1.3878× replica (hand 0.091971 → 0.127640 m, gun height 0.13847 → 0.192161 m,
   prop offset 0.124960 → 0.173420 m, live-confirmed to 1e-6). A 1.3878× replica placed at the
   source's own distance subtends 1.3878× the angle. `compositionScale` is clamped by
   `Mathf.Min(1f, lcChain/srcChain)`, which discards the 1.3444 arm-span ratio that would otherwise
   have partly compensated.
2. **The field of view is not carried across.** The pack authors through an **80°** vertical camera
   (its own player prefab); LC renders at **66°** (Player.log, 284 rows). The source-prefab builder
   transplanted the camera's position and rotation and dropped `fieldOfView`. Transplanting a
   camera-space composition between those two cameras expands every screen offset and size by
   `tan(40°)/tan(33°) = 1.2921`.

Measured product on the rejected build: gun **1.507×** intended (50.3 % of frame height), hand
**1.793×** (78.0 %). Both reproduce to four significant figures as `1.1661 × 1.2921` and
`1.3878 × 1.2921`. The hand is the worse error and dominated the frame — the dark mass in the
rejection screenshot was the **glove**, not the gun.

**The rules that follow:**

- **Transfer an authored composition by a scale, never verbatim.** The correction is a *rigid
  translation* `Δ = (S − 1) × referenceGripWrist`, applied to both IK targets. It must not be a
  scale about the eye: scaling would multiply the 0.0992 m wrist separation by S and tear the
  support hand off the weapon. A translation leaves separation, the prop's seat in the fist and the
  authored support-hand offset untouched — confirmed by the prop-attachment JSON coming out
  **byte-identical** across the fix (`localScale 1.237302`, same position, same euler).
- **`S = handScaleRatio`** (angular fidelity) — the delivered composition becomes an exact homothety
  of the authored one, so every angle the author composed is reproduced. **Chosen 2026-07-28 by
  rendering the candidates and looking at them.**
- **`S = handScaleRatio × tan(srcFov/2)/tan(lcFov/2)`** (frame fidelity) restores the authored
  *fraction of frame* instead. It is arithmetically defensible and was **REJECTED, 2026-07-28**: at
  66° it drops the weapon so low that most of it clips the bottom edge. A narrower target camera
  cannot reproduce a wider camera's framing by moving the object — the FOV term is a radial
  expansion in tan-space about frame centre, and it is *scale-invariant*, so no distance change
  moves a screen position. The grip wrist sits at viewport y ≈ **−0.53** in LC against −0.295 in the
  pack, and always will. Expect the hands to sit below the bottom edge; that is correct behaviour
  under a narrower camera, not a defect.
- **A source's authored FOV is a required input, not an optional one.** It is now stamped onto the
  camera anchor by the source-prefab builder and read back by the baker. An authored-viewmodel
  profile whose anchor carries no camera **fails the bake before sampling**. This gate was fired
  against the pre-fix state on 2026-07-28 and refused it, publishing nothing.

**Why every gate passed.** There were 13 gates across the three tools. Twelve measured the source
side, an axis, or an existence. Exactly one measured the shipped side, and it reads **rotation
only**. None read a size, a size-and-distance together, or any field of view — so the entire gate
set was blind to apparent size by construction, and could pass a composition that was 51 % oversize
just as happily as one that was 44 % undersized. The single expression that would have prevented all
of it is `presentedHeight / (2 · centreDepth · tan(fov/2))`, and it existed nowhere outside a preview
renderer that had **zero call sites**.

### Guards must be expressed in a frame the guard cannot itself be wrong about

**PROVEN IN GAME, 2026-07-27.** A source rig's bone axes are not guaranteed to follow Unity's
+Z-forward convention. A Y-forward/Z-up rig was read as if `bone.forward` were down-range; the mount
frame was built from the wrong axis and the weapon shipped with its bore a quarter-turn off, pointing
at the ceiling.

Every guard passed. They compared the weapon's bore against **another bone axis on the same rig**,
which was wrong in the same way, so the checks agreed with each other and disagreed with reality. One
of them was structurally incapable of failing: the frame was *built* from the vector it was then
compared against, so it always reported 0.00 deg.

The rules that follow, and that generalize:

- **Establish the rig's axis convention by measurement, in the camera's frame, and write the measured
  triad into the source file.** Do not infer it from bone names or from another rig.
- **Never validate a frame against a vector used to construct it.** That is a tautology wearing the
  costume of a test.
- **Put the final directional gate on the shipped artifacts, in the player's frame** — reassemble the
  manifest and the baked clip the way the runtime will, and assert the bore lands down-range there.
  A source-side check cannot see an error introduced downstream, and a check sharing the source's
  assumptions cannot see an error in those assumptions.
- **Fire every new gate against the known-bad value before trusting it.** A guard that has never
  rejected anything is an untested branch.

### A held weapon exists twice, and per-viewer effects must pick the copy that is on screen

**MECHANICALLY VERIFIED, 2026-07-27.** A weapon driven by this pipeline has two visual instances: the
networked world-model item at the third-person held-item transform, and — for the local player while
the interaction is live — the first-person prop parented under the hand. They are metres apart, and
the same landmark resolves correctly on both, to two different places.

Any effect anchored to the weapon must therefore choose a model, not just a landmark. A muzzle flash
resolved off the world-model item appeared down and to the left of the viewmodel fist: it went exactly
where it was told. **The rule: local player with first-person animation active → FP prop; every remote
viewer, and the local player without it → world model.** It is a per-viewer routing decision, not a
global switch, and both instances already carry the landmark data, so no new geometry is needed.

Two supporting rules that generalize:

- **Take the point from the landmark, not from renderer bounds.** Where a prop root is the landmark
  frame, the authored value applies directly. On one measured prop the bounds-derived muzzle
  (`bounds.max.z` at the bounds centroid) sat **5.29 cm** from the authored muzzle, because the
  centroid is not the bore axis — invisible at arm's length on a world model, glaring at viewmodel
  distance.
- **Verify the prop root really is the landmark frame; do not assume it.** It is a property of how the
  bundle builder nests the prop, not a law, and a prop nested one node differently puts every landmark
  somewhere plausible and wrong. Check the resolved prop's own extents against the landmark entry once
  per resolve and fall back to the world model when it fails.

An existing "is the landmark host authoritative" guard does **not** cover this and did not catch it:
it asks whether the landmarks resolve against the right object, which they did. It cannot ask whether
the right object is the one the viewer is looking at.

### The retarget transports the composition it is given, faithfully

**MECHANICALLY VERIFIED, 2026-07-27.** When a first-person result is visibly wrong, the retarget
math is the *last* place to look, not the first. In the instance above, the prop-attachment
derivation reproduced its own shipped output to four decimal places from first principles — it was
correct, and it had faithfully transported a source composition that was already broken.

Diagnose in pipeline order — **source composition → transport → runtime consumption** — and measure
at each boundary before editing anything. Editing the transport stage to compensate for a bad source
would have silently corrupted every other weapon sharing it.

### Preview-versus-runtime divergence — measured, and the preview now matches

**REJECTED, 2026-07-16** — a calibrated editor preview that pixel-matched an approved lab render
produced a grossly different in-game result (weapon pointing nearly straight up, deep in a frame
corner). That event stands as a rejection: a preview that merely *looks* right is not evidence.

**MECHANICALLY VERIFIED, 2026-07-28** — the divergence has now been measured for the first time,
rather than assumed, by projecting a landmark the running game already logs (muzzle viewport x/y/z
and distance) and comparing it against the preview's projection of the same landmark from the same
shipped artifacts. Four independent quantities agreed to three decimals on the measured Hold pose;
the per-weapon table is consumer evidence.

**The offline model of Lethal Company is correct once it is given LC's near plane and the weapon
has a profile at all.** The reason nobody knew this is that nobody had ever compared them: the
preview renderer carried fewer source-rig profiles than the baker, so previewing the weapon threw
`Unknown source rig profile` and it could not be rendered even by hand. Its near clip was also
0.01 against LC's 0.05 m (0.0446 rig units), so it showed geometry the game clips.

Consequences that remain in force:

- **A preview is evidence only against a landmark the runtime also reports.** Compare numbers first,
  pictures second. The muzzle-flash log line (`viewport`, `distance`) is the available comparison
  point; the bake's preview report prints the same triple for exactly this purpose.
- A preview that has never been checked against runtime telemetry is decoration.
- After a divergence, do **not** iterate composition parameters. Instrument the runtime first:
  capture the live camera→target→owning-hand→prop chain and diff it against the preview's
  assumptions (FOV, aspect, pixel viewport, parent transform, hand-scale handling, offset
  application order). Doing exactly this on 2026-07-28 is what found the missing FOV.
- The presenter emits throttled `live_body.transform_chain` samples from
  `Application.onBeforeRender`, after all `LateUpdate` owners, for exactly this purpose. Compare
  `missNormalizedHeight`, not raw pixels, across resolutions.
- The projected prop-local `+Z` ray is a neutral diagnostic convention. It is not a claim that every
  prop aims on that axis; the consumer's evidence packet must state the weapon's real authored axis
  and its acceptance limit.

**Every bake now renders the composition it just shipped.** As of 2026-07-28 the baker calls the
preview renderer before writing its report and emits `hero_*.png` (a full first-person frame) plus a
`preview-report.md` carrying the viewport landmarks, next to `ik-target-bake-report.md`. It is
deliberately **non-blocking** — a preview failure degrades to a line in the report rather than
vetoing a bake whose own gates passed. The point is that no weapon can now be shipped without the
picture existing, which was the actual failure: the capability had been sitting unused since July.

Composition is therefore an **offline** iteration now: change the number, look at the picture, ship
when it reads right. Candidates can be compared without re-baking — the renderer accepts a
camera-space translation and applies it to the sampled targets, so several compositions can be
rendered from the shipped clips in seconds, with the shipped artifacts untouched.

**Status of the correction: UNVERIFIED.** A runtime-gated authoring path was added afterwards
(explicit projected-crosshair-miss and camera-space muzzle-pitch limits enforced before
publication). No weapon has yet received an approving in-game verdict through it. **Until one does,
treat every gate in that path as unproven and expect to instrument the runtime.**

Whether a pre-segmented, higher-fidelity source reduces this divergence is **an open question, not
an assumption.** Divergence was a runtime/preview modelling fault, not a source-quality fault.

---

## Third-person derivation

**No third-person animation content has been approved for any weapon.** The generic remote
controller lifecycle, upper-body filtering, parameter forwarding, and restore path have run without
taking over locomotion — that much is MECHANICALLY VERIFIED. Nothing about the resulting pose is.

### The locked derivation rule

First-person sources are authoritative for **action meaning, timing, contact, and perceived weight**.
They are **not** authoritative for absolute third-person positions, because they are camera-space,
arms-only animations.

Do **not** copy first-person arm-target transforms onto the third-person body. For a source hand
transform `H` and weapon transform `W`, the transferable motion is the hand relative to the weapon:

```
H_relative(t) = inverse(W(t)) * H(t)
```

Reduced relative deltas are layered onto an approved third-person Hold pose.

**Source format changes the quality of this derivation.** With a combined-take source, `W(t)` is
derived from a solved prop attachment, so first-person retarget error propagates into
`H_relative(t)`. With a pre-segmented source, `W(t)` is an authored prop curve and both sides of the
expression are authored data. This is a real reduction in error propagation and is the strongest
reason to expect better third-person results from a pre-segmented source.

### The gate that actually failed

**REJECTED, 2026-07-24** — three weapons' third-person pilots failed multiplayer spatial acceptance.
The body poses were recognizable; the failure was that the **world models were rolled or tilted
sideways and floated forward of the hands.**

That is the weapon's grip coordinate frame relative to the held item root. It is **geometry
calibration, entirely independent of source animation quality.** Better source animations do not
improve it.

**Therefore: a weapon's static third-person Hold must be approved before any third-person action
motion is authored.** That calibration is consumer-owned; see the consumer's weapon systems doc.

Implementation order, unchanged:

1. Correct the visible world weapon's grip coordinate frame.
2. Approve a static Hold pose in multiplayer.
3. Derive simplified action motion in weapon-relative space.
4. Synchronize body motion with the world weapon's articulated descendants.
5. Approve the complete action and interruption lifecycle in multiplayer.

### Shared body rules

- Sample authored hands relative to the weapon, not the camera.
- Transfer only the minimum relative motion needed to communicate the action.
- Preserve the approved dominant grip contact; the dominant hand stays locked to its grip unless the
  authored action explicitly releases it.
- The support hand may follow a slide, pump, foregrip, cylinder, canister, or valve as the weapon
  requires.
- Solve resulting poses into third-person `LeftArm_target` / `RightArm_target` curves.
- Include third-person finger rotations only where they improve contact or readability.
- Exclude root motion, legs, spine, head, cameras, `ServerItemHolder`, `ScavengerModelArmsOnly`, and
  first-person targets.
- Retain a neutral pass-through state so the vanilla controller owns the body while inactive.
- Keep the custom body presentation remote-observer-only.
- Vanilla spatial clips may serve as a neutral posture or safety baseline. They must never define a
  different weapon's action semantics.

### Verification requires a multiplayer lobby

A third-person pose can only be judged by an observer. **Asset quality does not remove this
requirement.** Run at least two clients and test both host-observes-client and
client-observes-host, covering: idle/walk/sprint/crouch/turn/look-pitch; owner still sees the
approved first-person actions; fire and reload both stationary and moving; interrupting reload with
fire; swap, drop, re-equip, death, disconnect during every action; a higher-priority body
interaction starting while the weapon is held, and clean resumption afterwards.

A weapon is accepted only on an explicit human verdict against that matrix.

---

## Build and runtime facts

**MECHANICALLY VERIFIED, 2026-07-26:**

- Bundles built in **Unity 6000.0.77f1 load correctly in Lethal Company (Unity 2022.3.9f1)** for
  animation clips, controllers, and prop prefabs. Verified from shipped bundle headers.
- **Shaders are the exception.** A shader compiled for a different render pipeline will not work in
  Lethal Company's HDRP. Author materials against the game's HDRP version, or generate them at
  runtime from a supported shader. This is why prop materials are rebuilt rather than shipped.
- A manifest's `bundleInternalName` must equal the bundle's actual runtime `AssetBundle.name`.
  A mismatch makes Unity's second `LoadFromFile` fail and prevents the interaction from starting
  even when the file and its assets are valid.
- Bundle-loaded non-legacy clips must be validated through the same `AnimatorOverrideController`,
  layer, and state path the presenter uses. A direct `SampleAnimation` on a bundle-loaded clip can
  leave the hierarchy unchanged even when the clip is valid — that result is not a motion verdict.

**MECHANICALLY VERIFIED, 2026-07-27 — do not use a bundle checksum as a regression signal:**

- **AssetBundle output is not byte-reproducible.** Two consecutive builds from an unchanged project
  produced four different bundle md5s. "The re-bake changed the bundle" therefore proves nothing, and
  "the bundle is unchanged" cannot be relied on either.
- **Baked `.anim` assets and generated JSON *are* reproducible.** Re-baking an unchanged profile
  reproduced all three clip files byte-for-byte, along with the prop-attachment and finger-pose JSON.
  Compare **those** when asking whether a change altered the output; that is the signal the
  "re-bake touched exactly one artifact" check is actually reading.
- A bundle md5 is still meaningful for verifying a **copy** — candidate to repo to profile — because
  there the bytes are supposed to be identical by construction. It is meaningless between two builds.

---

## Open questions

Kept here deliberately; move each one into the body above, with an evidence label, once answered.

- Should a first-person prop be scaled to the **target's hand** or to its **own true size**?
  Hand-relative scaling makes the weapon fill the glove the way it filled the source's glove, which
  is what the current normalization does. But when the target rig's hands are oversized, it also
  makes the weapon oversized in absolute terms — measured at **1.388x** on one target/source pair —
  and a weapon that also exists as a world model has a known true size to disagree with. Unresolved:
  which of the two reads correctly to a player, and whether the answer depends on the weapon class.
- Is the runtime-gated authoring path trustworthy? No weapon has an approving in-game verdict
  through it yet.
- Does a pre-segmented source measurably reduce first-person retarget iteration count?
- Does an authored `W(t)` measurably improve third-person derivation quality once a static Hold has
  been approved?
- What is the minimum relative-motion budget that reads correctly for an observer without producing
  third-person IK reach, elbow, or locomotion artifacts?
