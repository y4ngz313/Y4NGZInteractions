using System;

namespace Y4NGZInteractions.InteractionAnimationApi.Presenters
{
    internal readonly struct CameraRestEnvelopeDecision
    {
        internal CameraRestEnvelopeDecision(
            bool whitelisted,
            float stanceRestHeight,
            float distanceFromStanceRest,
            float distanceFromOppositeStanceRest,
            float heightDeviationFromStanceRest,
            string basis)
        {
            Whitelisted = whitelisted;
            StanceRestHeight = stanceRestHeight;
            DistanceFromStanceRest = distanceFromStanceRest;
            DistanceFromOppositeStanceRest = distanceFromOppositeStanceRest;
            HeightDeviationFromStanceRest = heightDeviationFromStanceRest;
            Basis = basis;
        }

        internal bool Whitelisted { get; }
        internal float StanceRestHeight { get; }
        internal float DistanceFromStanceRest { get; }
        internal float DistanceFromOppositeStanceRest { get; }
        internal float HeightDeviationFromStanceRest { get; }
        internal string Basis { get; }
    }

    /// <summary>
    /// Magnitude half of the camera displacement guard: decides whether a camera position that
    /// has drifted past the displacement threshold is still explainable as vanilla stance
    /// behaviour rather than authored damage.
    ///
    /// This policy is deliberately INSTANTANEOUS — it sees a single frame with no history — so
    /// it must never own stance damage. A stance mismatch is instantaneously true at t=0 of
    /// every legitimate crouch/stand transition (the camera is by definition still at the
    /// opposite stance's rest the frame <c>isCrouching</c> flips), and stopping there killed
    /// legitimate transitions and produced stop/snap/restart churn. Stance damage is owned
    /// solely by the debounced stance viewpoint invariant, which requires the same mismatch to
    /// persist past vanilla's crouch-glide window.
    ///
    /// What survives here is the pure-magnitude protection: a reading far from BOTH stance
    /// rests (teleport, turret grab) is still authored damage and is never whitelisted. The
    /// stance basis strings are retained so the guard's telemetry still names what it saw.
    /// </summary>
    internal static class CameraRestEnvelopePolicy
    {
        internal static CameraRestEnvelopeDecision Evaluate(
            float currentX,
            float currentY,
            float currentZ,
            float restX,
            float restZ,
            float standingRestHeight,
            float crouchedRestHeight,
            bool crouchingKnown,
            bool crouching,
            float envelopeThreshold,
            float stanceHeightTolerance)
        {
            bool treatAsCrouched = crouchingKnown && crouching;
            float stanceRestHeight = treatAsCrouched ? crouchedRestHeight : standingRestHeight;
            float oppositeRestHeight = treatAsCrouched ? standingRestHeight : crouchedRestHeight;

            float deltaX = currentX - restX;
            float deltaZ = currentZ - restZ;
            float deltaY = currentY - stanceRestHeight;
            float distance = (float)Math.Sqrt(
                (deltaX * deltaX) + (deltaY * deltaY) + (deltaZ * deltaZ));
            float heightDeviation = Math.Abs(deltaY);

            float oppositeDeltaY = currentY - oppositeRestHeight;
            float oppositeDistance = (float)Math.Sqrt(
                (deltaX * deltaX) + (oppositeDeltaY * oppositeDeltaY) + (deltaZ * deltaZ));

            // Stance unreadable: fall back to the pre-stance-aware behaviour, a plain radius
            // around the standing rest expectation, rather than guessing a stance.
            if (!crouchingKnown)
            {
                bool insideStandingRadius = distance <= envelopeThreshold;
                return new CameraRestEnvelopeDecision(
                    insideStandingRadius,
                    stanceRestHeight,
                    distance,
                    oppositeDistance,
                    heightDeviation,
                    insideStandingRadius
                        ? "stance_unknown_vanilla_rest_envelope"
                        : "outside_stance_rest_envelope");
            }

            // Pure magnitude, measured against BOTH stance rests. Anything explainable as
            // either stance (settled on one, gliding between them, or parked on the opposite
            // one) is inside the envelope; only a reading beyond the threshold from both is
            // displacement this controller should stop for.
            if (distance > envelopeThreshold && oppositeDistance > envelopeThreshold)
            {
                return new CameraRestEnvelopeDecision(
                    false,
                    stanceRestHeight,
                    distance,
                    oppositeDistance,
                    heightDeviation,
                    "outside_stance_rest_envelope");
            }

            if (heightDeviation <= stanceHeightTolerance)
            {
                return new CameraRestEnvelopeDecision(
                    true,
                    stanceRestHeight,
                    distance,
                    oppositeDistance,
                    heightDeviation,
                    "settled_on_stance_rest");
            }

            // Vanilla glides the viewpoint between the two rest heights after isCrouching flips.
            // Both the glide in flight and a height settled ON the opposite stance's rest
            // continue here; the basis string still distinguishes them for telemetry, and the
            // debounced stance invariant is what stops a mismatch that actually persists.
            float oppositeHeightDeviation = Math.Abs(oppositeDeltaY);
            float lowerRestHeight = Math.Min(standingRestHeight, crouchedRestHeight);
            float upperRestHeight = Math.Max(standingRestHeight, crouchedRestHeight);
            bool betweenStanceRestHeights =
                currentY >= lowerRestHeight && currentY <= upperRestHeight;
            bool glideInFlight =
                betweenStanceRestHeights &&
                oppositeHeightDeviation > stanceHeightTolerance;
            return new CameraRestEnvelopeDecision(
                true,
                stanceRestHeight,
                distance,
                oppositeDistance,
                heightDeviation,
                glideInFlight ? "stance_glide_in_flight" : "stance_mismatched_rest_height");
        }
    }
}
