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
            logger?.LogInfo(
                "[LCInteractionAnimationAPI] api.initialized: standalone local presentation API ready.");
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
