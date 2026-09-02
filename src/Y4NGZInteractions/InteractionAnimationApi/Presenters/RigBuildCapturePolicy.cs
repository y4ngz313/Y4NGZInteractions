using System;

namespace Y4NGZInteractions.InteractionAnimationApi.Presenters
{
    /// <summary>
    /// What a forced animator evaluation wrote to the arms-metarig binding after the binding
    /// was parked on a sentinel height (#37 round 12). The sentinel separates "the evaluation
    /// wrote value X" from "the evaluation wrote nothing and X was already there" — the
    /// confound that made the round-11 seam probe read healthy while unkeyed states still
    /// wrote crouch on natural frames.
    /// </summary>
    internal enum CaptureProbeWriteVerdict
    {
        /// <summary>No verdict: NaN input, or the sentinel is confusable with a value a
        /// legitimate writer could produce this frame.</summary>
        Inconclusive,

        /// <summary>The sentinel survived the evaluation: nothing writes this binding —
        /// the binding retains whatever was last written (round-9 retention theory).</summary>
        WroteNothing,

        /// <summary>The evaluation overwrote the sentinel with the expected captured
        /// default: write-back exists for this binding.</summary>
        WroteCapturedDefault,

        /// <summary>Mid-session only: the evaluation reproduced the value the natural frame
        /// had already written — a scene-independent per-frame writer.</summary>
        RewroteFrameValue,

        /// <summary>The evaluation wrote a value that is neither the sentinel nor any
        /// expected reference — e.g. a blend whose unkeyed-side term reads the scene.</summary>
        WroteOther,
    }

    /// <summary>
    /// Pure decision for normalizing the arms chain around an Animation Rigging graph build.
    ///
    /// Rounds 10-11 (#37): the write-defaults capture that governs what unkeyed Base Layer
    /// states write back belongs to the rebuilt Animation Rigging playable graph, not the
    /// controller assignment (round 9 proved the assignment-seam priming ineffective), and
    /// not the scene pose at RigBuilder.Build() either (round 10 held the chain pristine
    /// across every build and Walk/Sprint still wrote the crouched 1.017 afterwards, in
    /// session and out). The capture defers to the graph's FIRST EVALUATION, which both seams
    /// used to run with the live crouched stance re-posed. A crouched capture makes the 11 of
    /// 15 Base Layer states that do not key the arms metarig write crouch height back every
    /// frame (the "per-frame writer" that fought the round-7 enforcement for 302 frames); the
    /// user-observed cure — pick up and drop while standing — worked because it re-ran the
    /// whole seam at a standing pose. Fix: hold the arms chain pristine from before the build
    /// THROUGH the graph's first evaluation, run that evaluation in the default standing
    /// Idle1, probe the captured default by evaluating unkeyed Walk and reading the binding
    /// back, then replay the live state; all inside one seam frame, never rendered.
    ///
    /// Round 12: the round-11 probe read healthy at every seam while natural frames stayed
    /// broken — but it read the binding after the Walk evaluation WITHOUT disturbing what
    /// Idle1 had just written, so "Walk wrote the captured default" and "Walk wrote nothing"
    /// were indistinguishable. The sentinel classifiers below de-confound both the seam probe
    /// and a 1 Hz mid-session writer probe by parking the binding on a height neither state
    /// authors before the measured evaluation.
    /// </summary>
    internal static class RigBuildCapturePolicy
    {
        /// <summary>
        /// True when a target's local position should be put on its pristine value before the
        /// rig graph builds: a pristine baseline exists and the current position deviates from
        /// it beyond tolerance on any axis. Without a baseline nothing is normalized —
        /// guessing a rest pose would bake exactly the poisoned capture this policy exists to
        /// prevent.
        /// </summary>
        internal static bool ShouldNormalizeBeforeBuild(
            bool pristineAvailable,
            float deltaX,
            float deltaY,
            float deltaZ,
            float toleranceMeters)
        {
            return pristineAvailable &&
                CameraStanceRestorePolicy.ExceedsResidueTolerance(
                    deltaX, deltaY, deltaZ, toleranceMeters);
        }

        /// <summary>
        /// True when the live pose captured before the build should be written back after it.
        /// Only a target this scope actually moved is restored: writing a stale capture onto a
        /// transform the build never required us to touch would plant residue.
        /// </summary>
        internal static bool ShouldRestoreLivePoseAfterBuild(bool normalizedBeforeBuild)
        {
            return normalizedBeforeBuild;
        }

        /// <summary>
        /// Round 11: verdict for the post-build capture probe. After steering the first
        /// evaluation of the rebuilt graph through standing Idle1, an unkeyed state (Walk)
        /// writes the captured default for the arms-metarig binding — reading it back measures
        /// the capture directly. A reading well below the pristine standing rest means the
        /// capture happened later than this seam and the steering was ineffective; without a
        /// pristine baseline, or with a NaN probe, no verdict is possible and the probe must
        /// not report poison.
        /// </summary>
        internal static bool ProbeIndicatesPoisonedDefault(
            bool pristineAvailable,
            float probedLocalY,
            float pristineLocalY,
            float poisonToleranceMeters)
        {
            if (!pristineAvailable || float.IsNaN(probedLocalY))
                return false;
            return (pristineLocalY - probedLocalY) > poisonToleranceMeters;
        }

        /// <summary>
        /// Round 12: verdict for the DE-CONFOUNDED seam probe. The binding is parked on a
        /// sentinel height between the Idle1 evaluation and the Walk evaluation, so the Walk
        /// reading distinguishes "wrote the captured default" (write-back exists) from "wrote
        /// nothing and the sentinel survived" (retention: write-defaults never operates on
        /// this binding at runtime and every capture-steering round aimed at a mechanism that
        /// does not exist). A sentinel within twice the tolerance of the expected default
        /// cannot separate the two answers and yields no verdict.
        /// </summary>
        internal static CaptureProbeWriteVerdict ClassifySentinelSeamProbe(
            float probedLocalY,
            float sentinelLocalY,
            float expectedDefaultLocalY,
            float matchToleranceMeters)
        {
            if (float.IsNaN(probedLocalY) || float.IsNaN(sentinelLocalY) ||
                float.IsNaN(expectedDefaultLocalY))
            {
                return CaptureProbeWriteVerdict.Inconclusive;
            }
            if (Math.Abs(sentinelLocalY - expectedDefaultLocalY) <= 2f * matchToleranceMeters)
                return CaptureProbeWriteVerdict.Inconclusive;
            if (Math.Abs(probedLocalY - sentinelLocalY) <= matchToleranceMeters)
                return CaptureProbeWriteVerdict.WroteNothing;
            if (Math.Abs(probedLocalY - expectedDefaultLocalY) <= matchToleranceMeters)
                return CaptureProbeWriteVerdict.WroteCapturedDefault;
            return CaptureProbeWriteVerdict.WroteOther;
        }

        /// <summary>
        /// Round 12: verdict for the 1 Hz mid-session writer probe, which parks the binding
        /// on the sentinel and forces a zero-delta re-evaluation on a NATURAL frame — the
        /// round-11 contradiction was seam-time measurements disagreeing with natural-frame
        /// behavior 72 frames later. Three references bracket the answer: the sentinel
        /// (nothing writes), the standing default (write-back exists), and the value the
        /// natural frame had already written (a scene-independent per-frame writer, e.g.
        /// during an enforcement fight). A probe reading on the sentinel proves retention
        /// only when neither reference could be confused with the sentinel this frame;
        /// otherwise no verdict. Anything else — such as a blend pulled partway toward the
        /// sentinel, proof the writer reads the current scene value — is WroteOther, with
        /// <see cref="SentinelContribution"/> quantifying the pull.
        /// </summary>
        internal static CaptureProbeWriteVerdict ClassifyMidSessionWriterProbe(
            float probedLocalY,
            float sentinelLocalY,
            float frameLocalY,
            float standingDefaultLocalY,
            float matchToleranceMeters)
        {
            if (float.IsNaN(probedLocalY) || float.IsNaN(sentinelLocalY) ||
                float.IsNaN(frameLocalY) || float.IsNaN(standingDefaultLocalY))
            {
                return CaptureProbeWriteVerdict.Inconclusive;
            }

            if (Math.Abs(probedLocalY - sentinelLocalY) <= matchToleranceMeters)
            {
                bool frameConfusable =
                    Math.Abs(frameLocalY - sentinelLocalY) <= 2f * matchToleranceMeters;
                bool standingConfusable =
                    Math.Abs(standingDefaultLocalY - sentinelLocalY) <= 2f * matchToleranceMeters;
                return frameConfusable || standingConfusable
                    ? CaptureProbeWriteVerdict.Inconclusive
                    : CaptureProbeWriteVerdict.WroteNothing;
            }

            if (Math.Abs(probedLocalY - standingDefaultLocalY) <= matchToleranceMeters)
                return CaptureProbeWriteVerdict.WroteCapturedDefault;
            if (Math.Abs(probedLocalY - frameLocalY) <= matchToleranceMeters)
                return CaptureProbeWriteVerdict.RewroteFrameValue;
            return CaptureProbeWriteVerdict.WroteOther;
        }

        /// <summary>
        /// How far the probed value was pulled from a reference toward the sentinel, as a
        /// 0..1 weight: 0 means the evaluation ignored the sentinel entirely, 1 means it
        /// reproduced the sentinel. Under the crossfade-feedback hypothesis (the blend's
        /// unkeyed-side term is the current scene value) this IS the blend weight. NaN when
        /// inputs are NaN or the sentinel and reference are too close to divide.
        /// </summary>
        internal static float SentinelContribution(
            float probedLocalY,
            float sentinelLocalY,
            float referenceLocalY)
        {
            float span = sentinelLocalY - referenceLocalY;
            if (float.IsNaN(probedLocalY) || float.IsNaN(span) || Math.Abs(span) < 0.001f)
                return float.NaN;
            return (probedLocalY - referenceLocalY) / span;
        }
    }
}
