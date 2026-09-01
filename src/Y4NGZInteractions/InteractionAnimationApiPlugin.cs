using System;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace Y4NGZInteractions.InteractionAnimationApi
{
    internal static class InteractionAnimationApiPlugin
    {
        private static ManualLogSource logger;
        private static InteractionAnimationCoordinator coordinator;
        private static bool initialized;

        internal static void Initialize(ConfigFile config, ManualLogSource log)
        {
            if (initialized)
                return;
            logger = log;
            coordinator = new InteractionAnimationCoordinator(logger);
            LCInteractionAnimationAPI.Initialize(coordinator);
            initialized = true;
            Assembly assembly = typeof(InteractionAnimationApiPlugin).Assembly;
            logger?.LogInfo(
                "[LCInteractionAnimationAPI] api.initialized: standalone local presentation API ready " +
                $"(version={assembly.GetName().Version}, location='{assembly.Location}').");
            WarnOnShadowCopies(assembly);
        }

        /// <summary>
        /// Names every same-named DLL in the plugins tree that is not this loaded file. A shadow
        /// copy is a session-killing hazard: consumers resolved through the AssemblyResolve file
        /// scan can bind to it and then permanently see
        /// <c>interaction_animation_api_not_initialized</c> even though this copy is healthy.
        /// </summary>
        private static void WarnOnShadowCopies(Assembly assembly)
        {
            try
            {
                string loadedPath = assembly.Location;
                string pluginRoot = BepInEx.Paths.PluginPath;
                if (string.IsNullOrEmpty(loadedPath)
                    || string.IsNullOrEmpty(pluginRoot)
                    || !Directory.Exists(pluginRoot))
                    return;

                string[] candidates = Directory.GetFiles(
                    pluginRoot, Path.GetFileName(loadedPath), SearchOption.AllDirectories);
                foreach (string shadow in DuplicateInstallPolicy.FindShadowCopies(loadedPath, candidates))
                {
                    logger?.LogError(
                        "[LCInteractionAnimationAPI] duplicate_install_detected: a second " +
                        $"'{Path.GetFileName(loadedPath)}' exists at '{shadow}'. Consumers can bind " +
                        "to that never-initialized copy and report " +
                        "interaction_animation_api_not_initialized for the whole session even " +
                        "though this copy initialized. Remove or update the stale install.");
                }
            }
            catch (Exception exception)
            {
                logger?.LogWarning(
                    "[LCInteractionAnimationAPI] duplicate_install_scan_failed: " + exception.Message);
            }
        }

        internal static void Tick(float deltaTime)
        {
            if (initialized)
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
            initialized = false;
            logger?.LogInfo("[LCInteractionAnimationAPI] api.shutdown: restoration complete.");
            logger = null;
        }
    }
}
