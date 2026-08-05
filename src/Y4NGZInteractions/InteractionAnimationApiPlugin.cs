using System;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace Y4NGZInteractions.InteractionAnimationApi
{
    internal static class InteractionAnimationApiPlugin
    {
        internal const string ConfigSection = "Interaction Animation API V2";
        internal const string DefaultCameraDisplacementGuardExemptInteractions = "";
        internal const string DefaultSpecialAnimationAutoStopExemptInteractions = "";

        private static ManualLogSource logger;
        private static ConfigEntry<string> cameraDisplacementGuardExemptInteractions;
        private static ConfigEntry<string> specialAnimationAutoStopExemptInteractions;
        private static InteractionAnimationCoordinator coordinator;
        private static bool exemptInteractionsReadFailureLogged;
        private static bool specialAnimationAutoStopExemptInteractionsReadFailureLogged;
        private static bool initialized;

        internal static void Initialize(ConfigFile config, ManualLogSource logger)
        {
            if (initialized)
                return;

            InteractionAnimationApiPlugin.logger = logger;
            cameraDisplacementGuardExemptInteractions = config?.Bind(
                ConfigSection,
                "Camera Displacement Guard Exempt Interactions",
                DefaultCameraDisplacementGuardExemptInteractions,
                "Comma-separated interaction IDs whose live-body sessions report camera displacement at info level but are never stopped by the displacement guard. Empty by default: an interaction normally declares the exemption itself via the manifest's exemptFromCameraDisplacementGuard flag, and this key exists so an operator can exempt one that did not.");
            specialAnimationAutoStopExemptInteractions = config?.Bind(
                ConfigSection,
                "Special Animation Auto-Stop Exempt Interactions",
                DefaultSpecialAnimationAutoStopExemptInteractions,
                "Comma-separated interaction IDs whose live-body sessions are not stopped by inSpecialInteractAnimation alone. Death and ladder climbing still stop exempt sessions. Empty by default: an interaction normally declares the exemption itself via the manifest's exemptFromSpecialAnimationAutoStop flag, and this key exists so an operator can exempt one that did not.");
            coordinator = new InteractionAnimationCoordinator(logger);
            LCInteractionAnimationAPI.Initialize(coordinator);
            initialized = true;
            logger?.LogInfo(
                "[LCInteractionAnimationAPI] api.initialized: " +
                "Interaction Animation API V2 initialized. " +
                $"cameraDisplacementGuardExemptInteractions='{ReadCameraDisplacementGuardExemptInteractions()}' " +
                $"specialAnimationAutoStopExemptInteractions='{ReadSpecialAnimationAutoStopExemptInteractions()}'.");
        }

        /// <summary>
        /// Effective exemption for a session: the interaction's own manifest declaration OR the
        /// user config list. Manifest-declared exemptions ship with the consuming mod; the config
        /// list stays honored so an operator can exempt an interaction that did not declare one.
        /// </summary>
        internal static bool IsCameraDisplacementGuardExempt(
            Authoring.InteractionAnimationManifest manifest)
        {
            if (manifest == null)
                return false;

            return manifest.exemptFromCameraDisplacementGuard ||
                IsCameraDisplacementGuardExempt(manifest.interactionId);
        }

        /// <summary>
        /// Effective exemption for a session; see
        /// <see cref="IsCameraDisplacementGuardExempt(Authoring.InteractionAnimationManifest)"/>.
        /// </summary>
        internal static bool IsSpecialAnimationAutoStopExempt(
            Authoring.InteractionAnimationManifest manifest)
        {
            if (manifest == null)
                return false;

            return manifest.exemptFromSpecialAnimationAutoStop ||
                IsSpecialAnimationAutoStopExempt(manifest.interactionId);
        }

        internal static bool IsCameraDisplacementGuardExempt(string interactionId)
        {
            if (string.IsNullOrWhiteSpace(interactionId))
                return false;

            string configured = ReadCameraDisplacementGuardExemptInteractions();
            string[] entries = configured.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < entries.Length; i++)
            {
                if (string.Equals(
                        entries[i].Trim(),
                        interactionId.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool IsSpecialAnimationAutoStopExempt(string interactionId)
        {
            if (string.IsNullOrWhiteSpace(interactionId))
                return false;

            string configured = ReadSpecialAnimationAutoStopExemptInteractions();
            string[] entries = configured.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < entries.Length; i++)
            {
                if (string.Equals(
                        entries[i].Trim(),
                        interactionId.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ReadCameraDisplacementGuardExemptInteractions()
        {
            try
            {
                return cameraDisplacementGuardExemptInteractions != null
                    ? cameraDisplacementGuardExemptInteractions.Value ?? string.Empty
                    : DefaultCameraDisplacementGuardExemptInteractions;
            }
            catch (Exception exception)
            {
                if (!exemptInteractionsReadFailureLogged)
                {
                    exemptInteractionsReadFailureLogged = true;
                    logger?.LogWarning(
                        "[LCInteractionAnimationAPI] api.camera_displacement_guard_config_read_failed: " +
                        $"error='{exception.Message}' " +
                        $"fallback='{DefaultCameraDisplacementGuardExemptInteractions}'.");
                }

                return DefaultCameraDisplacementGuardExemptInteractions;
            }
        }

        private static string ReadSpecialAnimationAutoStopExemptInteractions()
        {
            try
            {
                return specialAnimationAutoStopExemptInteractions != null
                    ? specialAnimationAutoStopExemptInteractions.Value ?? string.Empty
                    : DefaultSpecialAnimationAutoStopExemptInteractions;
            }
            catch (Exception exception)
            {
                if (!specialAnimationAutoStopExemptInteractionsReadFailureLogged)
                {
                    specialAnimationAutoStopExemptInteractionsReadFailureLogged = true;
                    logger?.LogWarning(
                        "[LCInteractionAnimationAPI] api.special_animation_auto_stop_config_read_failed: " +
                        $"error='{exception.Message}' " +
                        $"fallback='{DefaultSpecialAnimationAutoStopExemptInteractions}'.");
                }

                return DefaultSpecialAnimationAutoStopExemptInteractions;
            }
        }

        internal static void Tick(float deltaTime)
        {
            if (!initialized)
                return;

            coordinator?.Tick(deltaTime);
        }

        internal static void Shutdown()
        {
            if (!initialized)
                return;

            coordinator?.Shutdown();
            Presenters.LiveBodyAnimatorPresenter.ShutdownBundleCache();
            LCInteractionAnimationAPI.Shutdown();
            coordinator = null;
            cameraDisplacementGuardExemptInteractions = null;
            specialAnimationAutoStopExemptInteractions = null;
            exemptInteractionsReadFailureLogged = false;
            specialAnimationAutoStopExemptInteractionsReadFailureLogged = false;
            initialized = false;
            logger?.LogInfo("[LCInteractionAnimationAPI] api.shutdown: Interaction Animation API V2 shutdown.");
            logger = null;
        }
    }
}
