using System;

namespace Y4NGZInteractions.InteractionAnimationApi.Presenters
{
    internal readonly struct CameraHorizontalRestoreTarget
    {
        internal CameraHorizontalRestoreTarget(
            float x,
            float z,
            bool usedSessionEntry)
        {
            X = x;
            Z = z;
            UsedSessionEntry = usedSessionEntry;
        }

        internal float X { get; }
        internal float Z { get; }
        internal bool UsedSessionEntry { get; }
    }

    internal static class CameraStanceRestorePolicy
    {
        internal static bool IsSessionEntryHorizontalPositionUsable(
            float sessionEntryX,
            float sessionEntryZ,
            float vanillaRestX,
            float vanillaRestZ,
            float tolerance)
        {
            float deltaX = sessionEntryX - vanillaRestX;
            float deltaZ = sessionEntryZ - vanillaRestZ;
            float nonNegativeTolerance = Math.Max(0f, tolerance);
            return deltaX * deltaX + deltaZ * deltaZ <=
                nonNegativeTolerance * nonNegativeTolerance;
        }

        internal static CameraHorizontalRestoreTarget ResolveHorizontalTarget(
            float currentX,
            float currentZ,
            float sessionEntryX,
            float sessionEntryZ,
            bool hasSessionEntry,
            float vanillaRestX,
            float vanillaRestZ,
            float horizontalTolerance)
        {
            bool useSessionEntry = hasSessionEntry &&
                IsSessionEntryHorizontalPositionUsable(
                    sessionEntryX,
                    sessionEntryZ,
                    vanillaRestX,
                    vanillaRestZ,
                    horizontalTolerance);
            return new CameraHorizontalRestoreTarget(
                useSessionEntry ? sessionEntryX : vanillaRestX,
                useSessionEntry ? sessionEntryZ : vanillaRestZ,
                useSessionEntry);
        }

        /// <summary>
        /// True when the measured camera chain still needs a stance correction after the
        /// pristine camera-chain restore has run. The pristine chain carries the authored
        /// STANDING defaults, so a crouched exit legitimately lands off the stance target — but
        /// an already-correct chain must never be re-snapped, because every stop-time write is
        /// one more chance to bake residue into a transform vanilla does not rewrite.
        /// </summary>
        internal static bool NeedsStanceCorrection(
            float currentX,
            float currentY,
            float currentZ,
            float targetX,
            float targetY,
            float targetZ,
            float heightTolerance,
            float horizontalTolerance)
        {
            float heightDeviation = Math.Abs(currentY - targetY);
            if (heightDeviation > Math.Max(0f, heightTolerance))
                return true;

            float deltaX = currentX - targetX;
            float deltaZ = currentZ - targetZ;
            float nonNegativeHorizontal = Math.Max(0f, horizontalTolerance);
            return deltaX * deltaX + deltaZ * deltaZ >
                nonNegativeHorizontal * nonNegativeHorizontal;
        }

        /// <summary>
        /// True when a transform's local position has drifted from its pristine value by more
        /// than the residue tolerance. The gameplay camera is a CHILD of the camera container
        /// and its pristine local position is the vanilla invariant; any residue here is
        /// permanent, because vanilla derives the chain from the container instead of rewriting
        /// the camera's own local position.
        /// </summary>
        internal static bool ExceedsResidueTolerance(
            float deltaX,
            float deltaY,
            float deltaZ,
            float toleranceMeters)
        {
            float nonNegativeTolerance = Math.Max(0f, toleranceMeters);
            return (deltaX * deltaX) + (deltaY * deltaY) + (deltaZ * deltaZ) >
                nonNegativeTolerance * nonNegativeTolerance;
        }
    }
}
