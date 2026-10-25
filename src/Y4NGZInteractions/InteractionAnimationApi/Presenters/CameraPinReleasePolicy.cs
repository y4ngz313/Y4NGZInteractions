using System;
using System.Collections.Generic;

namespace Y4NGZInteractions.InteractionAnimationApi.Presenters
{
    /// <summary>
    /// One step of the restore-scoped camera pin's deferred-release countdown.
    /// </summary>
    internal readonly struct CameraPinReleaseStep
    {
        internal CameraPinReleaseStep(int remainingLateUpdates, bool releaseNow)
        {
            RemainingLateUpdates = remainingLateUpdates;
            ReleaseNow = releaseNow;
        }

        /// <summary>Countdown value to carry into the next LateUpdate.</summary>
        internal int RemainingLateUpdates { get; }

        /// <summary>True on the single LateUpdate that must run the final release.</summary>
        internal bool ReleaseNow { get; }
    }

    internal enum CameraPinLateUpdateStep
    {
        HoldViewpoint,
        RunReleaseCallback,
        GlueVisorToCamera,
        DestroyPin
    }

    /// <summary>
    /// Pure decisions for the restore-scoped camera pin's write target and release.
    ///
    /// The pin exists to hold the viewpoint steady across the teardown frame, but it must never
    /// hold it by writing the gameplay camera's own world position: the camera is a CHILD of the
    /// camera container, vanilla re-authors the container from its clips every frame, and a
    /// world-space write on the child therefore bakes (pinTarget - containerPose) into
    /// <c>gameplayCamera.localPosition</c> — a transform vanilla never rewrites, so the offset is
    /// permanent. Correcting the container instead leaves the camera's local position pristine and
    /// is self-healing on the next vanilla authoring pass.
    /// </summary>
    internal static class CameraPinReleasePolicy
    {
        /// <summary>
        /// LateUpdates the pin survives past teardown. Destroying it on the controller-restore
        /// frame exposed a one-frame viewpoint snap while the vanilla camera parent settled.
        /// </summary>
        internal const int DeferredReleaseLateUpdates = 2;

        // #660: A visor farther than this from its camera target point was parked away
        // from the camera by a consumer (the standard hide idiom, e.g. a
        // notSpawnedPosition or down-5000 park) — never snap a parked visor
        // back into frame.
        internal const float VisorMaxTrackDistanceMeters = 2f;

        private static readonly CameraPinLateUpdateStep[] HoldingLateUpdateSteps =
        {
            CameraPinLateUpdateStep.HoldViewpoint,
            CameraPinLateUpdateStep.GlueVisorToCamera
        };

        private static readonly CameraPinLateUpdateStep[] ReleasingLateUpdateSteps =
        {
            CameraPinLateUpdateStep.HoldViewpoint,
            CameraPinLateUpdateStep.RunReleaseCallback,
            CameraPinLateUpdateStep.GlueVisorToCamera,
            CameraPinLateUpdateStep.DestroyPin
        };

        internal static bool ShouldGlueVisor(float visorToTargetDistanceMeters)
        {
            return !float.IsNaN(visorToTargetDistanceMeters) &&
                !float.IsInfinity(visorToTargetDistanceMeters) &&
                visorToTargetDistanceMeters <= VisorMaxTrackDistanceMeters;
        }

        /// <summary>
        /// #660: Visor glue must be the last transform write before render because vanilla
        /// already glued the visor earlier in its own LateUpdate.
        /// </summary>
        internal static IReadOnlyList<CameraPinLateUpdateStep> LateUpdateSteps(bool releaseNow)
        {
            return releaseNow ? ReleasingLateUpdateSteps : HoldingLateUpdateSteps;
        }

        internal static bool MovesCamera(CameraPinLateUpdateStep step)
        {
            return step == CameraPinLateUpdateStep.HoldViewpoint ||
                step == CameraPinLateUpdateStep.RunReleaseCallback;
        }

        /// <summary>
        /// Normalizes a requested deferred-release length. A release is always at least one
        /// LateUpdate away, so "release after zero frames" can never mean "never release".
        /// </summary>
        internal static int ResolveScheduledLateUpdates(int requestedLateUpdates)
        {
            return Math.Max(1, requestedLateUpdates);
        }

        /// <summary>
        /// Advances the deferred-release countdown by one LateUpdate. A negative value means no
        /// release is scheduled and stays negative; <see cref="CameraPinReleaseStep.ReleaseNow"/>
        /// is true on exactly one step so the final restore cannot run twice.
        /// </summary>
        internal static CameraPinReleaseStep AdvanceRelease(int remainingLateUpdates)
        {
            if (remainingLateUpdates < 0)
                return new CameraPinReleaseStep(remainingLateUpdates, false);

            int next = remainingLateUpdates - 1;
            return new CameraPinReleaseStep(next, next <= 0);
        }

        /// <summary>
        /// True when the pin may hold the viewpoint by translating the camera container. When it
        /// is false the pin must leave the chain alone: writing the camera's own world position is
        /// the residue-baking defect itself, so no correction is strictly better than that one.
        /// </summary>
        internal static bool CanCorrectAtContainerLevel(
            bool hasContainer,
            bool containerIsCamera,
            bool cameraIsChildOfContainer)
        {
            return hasContainer && !containerIsCamera && cameraIsChildOfContainer;
        }
    }
}
