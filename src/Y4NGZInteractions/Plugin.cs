using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Y4NGZInteractions.InteractionAnimationApi;

namespace Y4NGZInteractions
{
    [BepInPlugin(Guid, Name, Version)]
    internal sealed class Plugin : BaseUnityPlugin
    {
        internal const string Guid = "com.y4ngz.interactions";
        internal const string Name = "Y4NGZInteractions";
        internal const string Version = BuildVersion.Value;

        internal static Plugin Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }

        private Harmony harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            harmony = new Harmony(Guid);

            InitializeModule("Interaction Animation API", () => InteractionAnimationApiPlugin.Initialize(Config, Logger));
            InitializeModule(
                "Interaction Animation API Restoration",
                () => InteractionAnimationApiRestoreDiagnostics.Initialize(Config, Logger));
            InitializeModule(
                "Interaction Animation API Spawn Hooks",
                () => harmony.PatchAll());

            Logger.LogInfo($"{Name} v{Version} loaded.");
        }

        private void LateUpdate()
        {
            InteractionAnimationApiRestoreDiagnostics.BeginCoordinatorLateUpdateTick();
            try
            {
                InteractionAnimationApiPlugin.Tick(UnityEngine.Time.deltaTime);
            }
            finally
            {
                InteractionAnimationApiRestoreDiagnostics.EndCoordinatorLateUpdateTick();
            }
        }

        private void OnDestroy()
        {
            try
            {
                InteractionAnimationApiPlugin.Shutdown();
                InteractionAnimationApiRestoreDiagnostics.Shutdown();
            }
            catch (Exception exception)
            {
                Logger.LogWarning($"{Name} shutdown warning: {exception.Message}");
            }

            harmony?.UnpatchSelf();
        }

        private void InitializeModule(string label, Action initialize)
        {
            try
            {
                initialize();
                Logger.LogInfo($"{label} initialized.");
            }
            catch (Exception exception)
            {
                Logger.LogError($"{label} initialization failed: {exception}");
            }
        }
    }
}
