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

        /// <summary>Name of the GameObject owning <see cref="InteractionRuntimeHost"/>.</summary>
        internal const string HostObjectName = "Y4NGZInteractions_Host";

        internal static Plugin Instance { get; private set; }
        internal static ManualLogSource Log { get; private set; }

        /// <summary>
        /// Owner of the runtime lifecycle. Use this — never <see cref="Instance"/> — to start
        /// coroutines or add components: this plugin component rides on BepInEx's shared,
        /// externally destroyable manager object, the host does not (#31).
        /// </summary>
        internal static InteractionRuntimeHost Host { get; private set; }

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            var harmony = new Harmony(Guid);

            // #31: the per-frame tick and teardown must not live on this component. See
            // InteractionRuntimeHost for why BepInEx_Manager is not a safe place to sit.
            var hostObject = new UnityEngine.GameObject(HostObjectName)
            {
                hideFlags = UnityEngine.HideFlags.HideAndDontSave,
            };
            UnityEngine.Object.DontDestroyOnLoad(hostObject);
            Host = hostObject.AddComponent<InteractionRuntimeHost>();
            Host.Initialize(harmony, Logger);

            InitializeModule("Interaction Animation API", () => InteractionAnimationApiPlugin.Initialize(Config, Logger));
            InitializeModule(
                "Interaction Animation API Restoration",
                () => InteractionAnimationApiRestoreDiagnostics.Initialize(Config, Logger));
            InitializeModule(
                "Interaction Animation API Spawn Hooks",
                () => harmony.PatchAll());

            Logger.LogInfo($"{Name} v{Version} loaded.");
        }

        // No OnDestroy here on purpose: this component is destroyed with BepInEx's manager object
        // at chainloader startup in profiles where HideManagerGameObject is false, and tearing the
        // static API down there killed it for the whole session (#31). Shutdown and Harmony
        // restoration belong to InteractionRuntimeHost.

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
