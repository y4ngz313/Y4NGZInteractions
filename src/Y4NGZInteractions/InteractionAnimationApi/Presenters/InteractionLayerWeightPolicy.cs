using System;
using Y4NGZInteractions.InteractionAnimationApi.Authoring;

namespace Y4NGZInteractions.InteractionAnimationApi.Presenters
{
    /// <summary>
    /// The fade parameters of one live-body session, read once at session start.
    /// </summary>
    internal readonly struct InteractionLayerFadeCurve
    {
        internal InteractionLayerFadeCurve(
            float startLayerWeight,
            float layerWeightRampSeconds,
            float enterLayerFadeSeconds,
            float naturalEndLayerFadeSeconds,
            float durationSeconds,
            bool smooth)
        {
            StartLayerWeight = startLayerWeight;
            LayerWeightRampSeconds = layerWeightRampSeconds;
            EnterLayerFadeSeconds = enterLayerFadeSeconds;
            NaturalEndLayerFadeSeconds = naturalEndLayerFadeSeconds;
            DurationSeconds = durationSeconds;
            Smooth = smooth;
        }

        internal float StartLayerWeight { get; }

        internal float LayerWeightRampSeconds { get; }

        internal float EnterLayerFadeSeconds { get; }

        internal float NaturalEndLayerFadeSeconds { get; }

        internal float DurationSeconds { get; }

        // Smoothstep easing on every fade; false keeps the straight-line fades.
        internal bool Smooth { get; }
    }

    /// <summary>
    /// Layer weight curves of live-body sessions. A layer's weight each frame is its base weight
    /// (ramp, enter fade, natural-end fade, exit fade) times its viewer factor times one minus its
    /// strongest yield. Smoothing changes only the shape of each fade, never its length or its
    /// end points.
    /// </summary>
    internal static class InteractionLayerWeightPolicy
    {
        // Length of a viewer change and of the sprint yield.
        internal const float TransitionSeconds = 0.1f;

        // Same result as Mathf.Clamp01.
        internal static float Clamp01(float value)
        {
            if (value < 0f)
                return 0f;
            if (value > 1f)
                return 1f;
            return value;
        }

        // Smoothstep of the clamped value, or the clamped value itself when smoothing is off.
        // 0 and 1 map exactly to 0 and 1.
        internal static float Ease(float t, bool smooth)
        {
            t = Clamp01(t);
            return smooth ? t * t * (3f - 2f * t) : t;
        }

        // Same result as Mathf.Lerp.
        internal static float Lerp(float from, float to, float t)
        {
            return from + (to - from) * Clamp01(t);
        }

        // Base weight of one layer before any exit; specWeight is FollowRampWeight or a pinned
        // 0 to 1 weight.
        internal static float SessionWeight(
            in InteractionLayerFadeCurve curve,
            float specWeight,
            float elapsedSeconds)
        {
            float startWeight = Clamp01(curve.StartLayerWeight <= 0f ? 1f : curve.StartLayerWeight);
            float rampWeight = curve.LayerWeightRampSeconds > 0f
                ? Lerp(startWeight, 1f, Ease(elapsedSeconds / curve.LayerWeightRampSeconds, curve.Smooth))
                : 1f;
            float weight = specWeight == InteractionAnimationLayerSpecs.FollowRampWeight
                ? rampWeight
                : Clamp01(specWeight);

            if (curve.EnterLayerFadeSeconds > 0f)
                weight *= Ease(elapsedSeconds / curve.EnterLayerFadeSeconds, curve.Smooth);

            if (curve.NaturalEndLayerFadeSeconds > 0f && curve.DurationSeconds > 0f)
            {
                float remaining = curve.DurationSeconds - elapsedSeconds;
                weight *= Ease(remaining / curve.NaturalEndLayerFadeSeconds, curve.Smooth);
            }

            return weight;
        }

        // Base weight of one layer during the exit fade, from its weight when the exit began.
        internal static float ExitWeight(
            float exitStartWeight,
            float exitElapsedSeconds,
            float exitDurationSeconds,
            bool smooth)
        {
            float exitT = exitDurationSeconds > 1e-5f
                ? Ease(exitElapsedSeconds / exitDurationSeconds, smooth)
                : 1f;
            return Lerp(exitStartWeight, 0f, exitT);
        }

        // Moves a 0 to 1 transition progress toward its target over TransitionSeconds.
        internal static float StepProgress(float progress, float target, float deltaTime)
        {
            if (!(deltaTime > 0f))
                return progress;
            float step = deltaTime / TransitionSeconds;
            if (progress < target)
                return Math.Min(target, progress + step);
            if (progress > target)
                return Math.Max(target, progress - step);
            return progress;
        }

        // sprintYield is the sprint transition progress, handsOnWallYield vanilla's
        // handsOnWallWeight; both are 0 for a layer that does not yield to them.
        internal static float EffectiveWeight(
            float baseWeight,
            float viewerProgress,
            float sprintYield,
            float handsOnWallYield,
            bool smooth)
        {
            float yield = Math.Max(Ease(sprintYield, smooth), Clamp01(handsOnWallYield));
            return baseWeight * Ease(viewerProgress, smooth) * (1f - yield);
        }

        internal static InteractionAnimationViewer ResolveViewer(
            bool isLocalPlayer,
            bool bodyModelEnabled,
            bool bodyModelShadowsOnly)
        {
            if (!isLocalPlayer)
                return InteractionAnimationViewer.Remote;
            return bodyModelEnabled && !bodyModelShadowsOnly
                ? InteractionAnimationViewer.LocalThirdPerson
                : InteractionAnimationViewer.LocalFirstPerson;
        }

        internal static bool IsSeenBy(
            InteractionAnimationViewerMask viewers,
            InteractionAnimationViewer viewer)
        {
            return (viewers & (InteractionAnimationViewerMask)(1 << (int)viewer)) != 0;
        }
    }

    /// <summary>
    /// The resolved layers of one live-body session and their weights. Built once at session
    /// start; <see cref="Update"/> runs once per presenter tick and allocates nothing.
    /// </summary>
    internal sealed class InteractionLayerWeights
    {
        private readonly InteractionLayerFadeCurve curve;
        private readonly InteractionAnimationLayerSpec[] specs;
        private readonly int[] layerIndices;
        private readonly float[] viewerProgress;
        private readonly float[] sprintProgress;
        private readonly float[] baseWeights;
        private readonly float[] exitStartWeights;
        private readonly float[] weights;
        private float sessionExitStartWeight;
        private bool updated;

        private InteractionLayerWeights(
            InteractionLayerFadeCurve curve,
            InteractionAnimationLayerSpec[] specs,
            int[] layerIndices)
        {
            this.curve = curve;
            this.specs = specs;
            this.layerIndices = layerIndices;
            int count = specs.Length;
            viewerProgress = new float[count];
            sprintProgress = new float[count];
            baseWeights = new float[count];
            exitStartWeights = new float[count];
            weights = new float[count];
            for (int i = 0; i < count; i++)
            {
                YieldsToSprint |= (specs[i].Yield & InteractionAnimationLayerYield.Sprint) != 0;
                YieldsToHandsOnWall |= (specs[i].Yield & InteractionAnimationLayerYield.HandsOnWall) != 0;
            }
        }

        internal int Count => specs.Length;

        internal bool Smooth => curve.Smooth;

        internal bool YieldsToSprint { get; }

        internal bool YieldsToHandsOnWall { get; }

        // The follow-ramp base weight of the session, before viewer and yield factors.
        internal float SessionWeight { get; private set; }

        internal int GetLayerIndex(int slot) => layerIndices[slot];

        internal float GetWeight(int slot) => weights[slot];

        /// <summary>
        /// Keeps fullBodyLayer and firstPersonArmsLayer whenever the controller has them, then
        /// each body.layers entry whose layer exists, is not the base layer and is not already
        /// driven by an earlier entry. Reads the Smooth Blending switch once.
        /// </summary>
        /// <param name="manifest">Session manifest; supplies the fade curve.</param>
        /// <param name="resolvedSpecs">Output of InteractionAnimationLayerSpecs.Resolve for its body.</param>
        /// <param name="resolvedIndices">Controller layer index of each spec, -1 when missing.</param>
        /// <param name="skippedBaseLayer">First body.layers name skipped for resolving to layer 0.</param>
        /// <param name="skippedDuplicateLayer">First body.layers name skipped for a taken index.</param>
        internal static InteractionLayerWeights Build(
            InteractionAnimationManifest manifest,
            InteractionAnimationLayerSpec[] resolvedSpecs,
            int[] resolvedIndices,
            out string skippedBaseLayer,
            out string skippedDuplicateLayer)
        {
            skippedBaseLayer = null;
            skippedDuplicateLayer = null;
            InteractionAnimationManifest.BodyManifest body = manifest?.body;
            if (body == null || resolvedSpecs == null || resolvedIndices == null)
                return null;

            int namedLayerCount =
                (string.IsNullOrWhiteSpace(body.fullBodyLayer) ? 0 : 1) +
                (string.IsNullOrWhiteSpace(body.firstPersonArmsLayer) ? 0 : 1);
            int length = Math.Min(resolvedSpecs.Length, resolvedIndices.Length);
            var keptSpecs = new InteractionAnimationLayerSpec[length];
            var keptIndices = new int[length];
            int kept = 0;
            for (int i = 0; i < length; i++)
            {
                int index = resolvedIndices[i];
                if (index < 0)
                    continue;
                if (i >= namedLayerCount)
                {
                    if (index == 0)
                    {
                        skippedBaseLayer = skippedBaseLayer ?? resolvedSpecs[i].Name;
                        continue;
                    }
                    if (Array.IndexOf(keptIndices, index, 0, kept) >= 0)
                    {
                        skippedDuplicateLayer = skippedDuplicateLayer ?? resolvedSpecs[i].Name;
                        continue;
                    }
                }
                keptSpecs[kept] = resolvedSpecs[i];
                keptIndices[kept] = index;
                kept++;
            }

            Array.Resize(ref keptSpecs, kept);
            Array.Resize(ref keptIndices, kept);
            var curve = new InteractionLayerFadeCurve(
                body.startLayerWeight,
                body.layerWeightRampSeconds,
                body.enterLayerFadeSeconds,
                body.naturalEndLayerFadeSeconds,
                manifest.durationSeconds,
                BlendingSettings.SmoothBlending);
            return new InteractionLayerWeights(curve, keptSpecs, keptIndices);
        }

        /// <summary>
        /// Computes every layer's weight for this tick. The first update snaps the viewer and
        /// sprint transitions to their targets; later updates move them over
        /// <see cref="InteractionLayerWeightPolicy.TransitionSeconds"/>.
        /// </summary>
        internal void Update(
            float elapsedSeconds,
            bool exitRequested,
            float exitElapsedSeconds,
            float exitDurationSeconds,
            InteractionAnimationViewer viewer,
            bool sprinting,
            float handsOnWallWeight,
            float deltaTime)
        {
            bool smooth = curve.Smooth;
            SessionWeight = exitRequested
                ? InteractionLayerWeightPolicy.ExitWeight(
                    sessionExitStartWeight, exitElapsedSeconds, exitDurationSeconds, smooth)
                : InteractionLayerWeightPolicy.SessionWeight(
                    curve, InteractionAnimationLayerSpecs.FollowRampWeight, elapsedSeconds);

            for (int i = 0; i < specs.Length; i++)
            {
                InteractionAnimationLayerSpec spec = specs[i];
                float baseWeight = exitRequested
                    ? InteractionLayerWeightPolicy.ExitWeight(
                        exitStartWeights[i], exitElapsedSeconds, exitDurationSeconds, smooth)
                    : InteractionLayerWeightPolicy.SessionWeight(curve, spec.Weight, elapsedSeconds);
                baseWeights[i] = baseWeight;

                bool yieldsToSprint = (spec.Yield & InteractionAnimationLayerYield.Sprint) != 0;
                float viewerTarget = InteractionLayerWeightPolicy.IsSeenBy(spec.Viewers, viewer) ? 1f : 0f;
                float sprintTarget = yieldsToSprint && sprinting ? 1f : 0f;
                if (updated)
                {
                    viewerProgress[i] = InteractionLayerWeightPolicy.StepProgress(
                        viewerProgress[i], viewerTarget, deltaTime);
                    sprintProgress[i] = InteractionLayerWeightPolicy.StepProgress(
                        sprintProgress[i], sprintTarget, deltaTime);
                }
                else
                {
                    viewerProgress[i] = viewerTarget;
                    sprintProgress[i] = sprintTarget;
                }

                float wallYield = (spec.Yield & InteractionAnimationLayerYield.HandsOnWall) != 0
                    ? handsOnWallWeight
                    : 0f;
                weights[i] = InteractionLayerWeightPolicy.EffectiveWeight(
                    baseWeight, viewerProgress[i], sprintProgress[i], wallYield, smooth);
            }

            updated = true;
        }

        // Every layer's exit fade starts from its last base weight.
        internal void BeginExit()
        {
            Array.Copy(baseWeights, exitStartWeights, baseWeights.Length);
            sessionExitStartWeight = SessionWeight;
        }
    }
}
