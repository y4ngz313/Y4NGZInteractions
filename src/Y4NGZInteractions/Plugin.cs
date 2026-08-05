using System;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Y4NGZInteractions.InteractionAnimationApi;

namespace Y4NGZInteractions
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.y4ngz.interactions";
        public const string Name = "Y4NGZInteractions";
        public const string Version = "0.3.0";

        internal static Plugin Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }

        private Harmony harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            harmony = new Harmony(Guid);

            InitializeModule("Interaction Animation API V2", () => InteractionAnimationApiPlugin.Initialize(Config, Logger));
            InitializeModule(
                "Interaction Animation API V2 Debug Probe",
                () =>
                {
                    InteractionAnimationApiRestoreDiagnostics.Initialize(Config, Logger);
                    InteractionAnimationApiDebugProbe.Initialize(Config, Logger);
                });
            InitializeModule(
                "Interaction Animation API V2 Spawn Hooks",
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
            InteractionAnimationApiDebugProbe.Tick();
        }

        private void OnDestroy()
        {
            try
            {
                InteractionAnimationApiDebugProbe.Shutdown();
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
