using BepInEx.Bootstrap;
using GameNetcodeStuff;
using UnityEngine;

namespace Y4NGZInteractions.InteractionAnimationApi.Presenters
{
    /// <summary>
    /// Another mod that sets the local player's <c>gameplayCamera.localPosition</c> every frame.
    ///
    /// Vanilla never moves the gameplay camera inside its container, so the camera seam treats
    /// any local offset there as residue to clear and measures the viewpoint at the camera
    /// itself. First Person View breaks that invariant on purpose: it pushes the camera away
    /// from the body by an amount that follows the look pitch. Clearing that offset after First
    /// Person View has written it renders a frame from inside the body, and a pin that measured
    /// the pushed camera pulls the container back by the same amount on the next frame.
    ///
    /// While such an owner is loaded the seam never writes the camera's own local position and
    /// measures the camera chain at the camera's pristine local point instead of at the camera.
    /// </summary>
    internal static class ExternalCameraLocalOwner
    {
        private const string FirstPersonViewGuid = "com.seeya.firstpersonview";

        private static bool resolved;
        private static bool active;

        /// <summary>
        /// Resolved on first use. Every caller runs inside a round, long after the chainloader
        /// has registered every plugin, so the answer cannot change afterwards.
        /// </summary>
        internal static bool IsActive
        {
            get
            {
                if (!resolved)
                {
                    active = Chainloader.PluginInfos.ContainsKey(FirstPersonViewGuid);
                    resolved = true;
                }

                return active;
            }
        }

        /// <summary>
        /// The gameplay camera's pristine local position, only while an external owner is loaded
        /// and a pristine camera-chain baseline exists for <paramref name="player"/>.
        /// </summary>
        internal static bool TryGetPristineCameraLocal(
            PlayerControllerB player,
            out Vector3 pristineCameraLocal)
        {
            pristineCameraLocal = Vector3.zero;
            return IsActive &&
                InteractionAnimationApiRestoreDiagnostics.TryGetPristineCameraChainLocalPosition(
                    player,
                    InteractionAnimationApiRestoreDiagnostics.GameplayCameraChainTarget,
                    out pristineCameraLocal,
                    out _);
        }

        /// <summary>
        /// World position the camera seam measures as the viewpoint: the gameplay camera, or,
        /// while an external owner is loaded, the point the camera occupies at its pristine
        /// local position. The caller guarantees <c>player.gameplayCamera</c> is present.
        /// </summary>
        internal static Vector3 ChainWorldPosition(PlayerControllerB player)
        {
            Transform cameraTransform = player.gameplayCamera.transform;
            if (TryGetPristineCameraLocal(player, out Vector3 pristineCameraLocal))
            {
                Transform cameraParent = cameraTransform.parent;
                if (cameraParent != null)
                    return cameraParent.TransformPoint(pristineCameraLocal);
            }

            return cameraTransform.position;
        }
    }
}
