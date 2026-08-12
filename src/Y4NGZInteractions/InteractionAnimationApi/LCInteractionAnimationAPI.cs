using System;
using GameNetcodeStuff;
using UnityEngine;

namespace Y4NGZInteractions.InteractionAnimationApi
{
    /// <summary>Static entry point for local interaction presentation and restoration.</summary>
    public static class LCInteractionAnimationAPI
    {
        private static InteractionAnimationCoordinator coordinator;

        /// <summary>
        /// Raised exactly once after an ended session has restored and released its resources.
        /// </summary>
        public static event EventHandler<InteractionAnimationEndedEventArgs> InteractionEnded;

        /// <summary>Gets whether the plugin has initialized the runtime coordinator.</summary>
        public static bool IsInitialized => coordinator != null;

        /// <summary>Validates and snapshots a pack for registration.</summary>
        /// <param name="pack">Caller-owned pack input.</param>
        /// <returns>An immutable validation report.</returns>
        public static InteractionAnimationValidationReport ValidateInteractionPack(
            InteractionAnimationPackDefinition pack)
        {
            return InteractionAnimationPackValidator.Validate(pack, out _);
        }

        /// <summary>Strictly validates schema-2 JSON or reports schema-1 migration.</summary>
        /// <param name="manifestJson">Manifest JSON.</param>
        /// <param name="expectedInteractionId">Interaction id expected by registration.</param>
        /// <param name="presentationKind">Presentation path the manifest must satisfy.</param>
        /// <returns>An immutable path-specific validation report.</returns>
        public static InteractionAnimationValidationReport ValidateInteractionManifest(
            string manifestJson,
            string expectedInteractionId,
            InteractionAnimationPresentationKind presentationKind)
        {
            return Authoring.InteractionAnimationManifestValidator.Validate(
                manifestJson, expectedInteractionId, presentationKind, out _);
        }

        /// <summary>Registers an immutable snapshot of a versioned interaction pack.</summary>
        /// <param name="pack">Caller-owned pack input.</param>
        /// <param name="reason">The first stable validation or registration error code.</param>
        /// <returns>True when the snapshot was registered.</returns>
        public static bool TryRegisterInteractionPack(
            InteractionAnimationPackDefinition pack,
            out string reason)
        {
            if (coordinator == null)
            {
                reason = "interaction_animation_api_not_initialized";
                return false;
            }
            return coordinator.TryRegisterInteractionPack(pack, out reason);
        }

        /// <summary>Starts a local presentation after complete preflight and lease arbitration.</summary>
        /// <param name="request">Target, pack, interaction, and conflict policy.</param>
        /// <param name="handle">The new handle when the request succeeds.</param>
        /// <param name="reason">A stable rejection code.</param>
        /// <returns>True when presentation started.</returns>
        public static bool TryStartInteraction(
            InteractionAnimationRequest request,
            out InteractionAnimationHandle handle,
            out string reason)
        {
            handle = InteractionAnimationHandle.Empty;
            if (coordinator == null)
            {
                reason = "interaction_animation_api_not_initialized";
                return false;
            }
            return coordinator.TryStartInteraction(request, out handle, out reason);
        }

        /// <summary>Loads and validates registered bundle assets ahead of gameplay.</summary>
        /// <param name="packId">Registered pack id.</param>
        /// <param name="interactionId">Registered interaction id.</param>
        /// <param name="reason">A stable preload error code.</param>
        /// <returns>True when the assets are ready or loading successfully began.</returns>
        public static bool TryPreloadInteractionAssets(
            string packId,
            string interactionId,
            out string reason)
        {
            if (coordinator == null)
            {
                reason = "interaction_animation_api_not_initialized";
                return false;
            }
            return coordinator.TryPreloadInteractionAssets(packId, interactionId, out reason);
        }

        /// <summary>Stops one handle and restores its resources before raising InteractionEnded.</summary>
        /// <param name="handle">Active session handle.</param>
        /// <param name="stopReason">Consumer-supplied stop reason.</param>
        /// <returns>True when an active session was stopped.</returns>
        public static bool TryStopInteraction(
            InteractionAnimationHandle handle,
            InteractionAnimationStopReason stopReason)
        {
            return coordinator != null && coordinator.TryStopInteraction(handle, stopReason);
        }

        /// <summary>Gets whether a handle currently identifies a running session.</summary>
        /// <param name="handle">Session handle.</param>
        /// <returns>True while the session is active.</returns>
        public static bool IsInteractionActive(InteractionAnimationHandle handle)
        {
            return coordinator != null && coordinator.IsInteractionActive(handle);
        }

        /// <summary>Finds the active handle for one player and presentation kind.</summary>
        /// <param name="player">Presented player.</param>
        /// <param name="presentationKind">Body-world or dedicated local viewmodel.</param>
        /// <param name="handle">The matching active handle.</param>
        /// <returns>True when a matching session is active.</returns>
        public static bool TryGetActiveInteraction(
            PlayerControllerB player,
            InteractionAnimationPresentationKind presentationKind,
            out InteractionAnimationHandle handle)
        {
            handle = InteractionAnimationHandle.Empty;
            return coordinator != null && coordinator.TryGetActiveInteraction(
                player, presentationKind, out handle);
        }

        /// <summary>Sets a Bool parameter on the session presenter.</summary>
        public static bool TrySetInteractionBool(
            InteractionAnimationHandle handle,
            string parameterName,
            bool value)
        {
            return coordinator != null && coordinator.TrySetInteractionAnimatorParameter(
                handle, parameterName, AnimatorControllerParameterType.Bool, value ? 1f : 0f);
        }

        /// <summary>Sets an Int parameter on the session presenter.</summary>
        public static bool TrySetInteractionInt(
            InteractionAnimationHandle handle,
            string parameterName,
            int value)
        {
            return coordinator != null && coordinator.TrySetInteractionAnimatorParameter(
                handle, parameterName, AnimatorControllerParameterType.Int, value);
        }

        /// <summary>Sets a Float parameter on the session presenter.</summary>
        public static bool TrySetInteractionFloat(
            InteractionAnimationHandle handle,
            string parameterName,
            float value)
        {
            return coordinator != null && coordinator.TrySetInteractionAnimatorParameter(
                handle, parameterName, AnimatorControllerParameterType.Float, value);
        }

        /// <summary>Fires a Trigger parameter on the session presenter.</summary>
        public static bool TryFireInteractionTrigger(
            InteractionAnimationHandle handle,
            string parameterName)
        {
            return coordinator != null && coordinator.TrySetInteractionAnimatorParameter(
                handle, parameterName, AnimatorControllerParameterType.Trigger, 0f);
        }

        /// <summary>Begins the manifest-authored graceful exit and schedules restoration.</summary>
        /// <param name="handle">Active session handle.</param>
        /// <param name="reason">A stable rejection code.</param>
        /// <returns>True when graceful exit began.</returns>
        public static bool TryBeginInteractionExit(
            InteractionAnimationHandle handle,
            out string reason)
        {
            reason = string.Empty;
            if (coordinator == null)
            {
                reason = "interaction_animation_api_not_initialized";
                return false;
            }
            return coordinator.TryBeginInteractionExit(handle, out reason);
        }

        internal static void Initialize(InteractionAnimationCoordinator value)
        {
            coordinator = value;
        }

        internal static void NotifyInteractionEnded(InteractionAnimationEndedEventArgs args)
        {
            Delegate[] subscribers = InteractionEnded?.GetInvocationList();
            if (subscribers == null)
                return;
            for (int i = 0; i < subscribers.Length; i++)
            {
                try
                {
                    ((EventHandler<InteractionAnimationEndedEventArgs>)subscribers[i])(null, args);
                }
                catch (Exception exception)
                {
                    Plugin.Log?.LogWarning(
                        "[LCInteractionAnimationAPI] interaction_ended_handler_failed: " + exception);
                }
            }
        }

        internal static void Shutdown()
        {
            coordinator = null;
        }
    }
}
