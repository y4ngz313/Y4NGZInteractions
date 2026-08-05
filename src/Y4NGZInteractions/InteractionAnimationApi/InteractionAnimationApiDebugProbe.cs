using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GameNetcodeStuff;
using UnityEngine.InputSystem;
using Y4NGZInteractions.InteractionAnimationApi.Authoring;
using Y4NGZInteractions.InteractionAnimationApi.Presenters;

namespace Y4NGZInteractions.InteractionAnimationApi
{
    internal static class InteractionAnimationApiDebugProbe
    {
        private const string ConfigSection = "Interaction Animation API V2 Debug";
        private const string OwnerModId = "com.y4ngz.interactions.v2.debug_probe";
        private const float DefaultDurationSeconds = 2.5f;
        private static readonly TimeSpan ActiveHandleGracePeriod = TimeSpan.FromSeconds(0.75);

        private static ManualLogSource logger;
        private static ConfigEntry<bool> enablePageDownViewmodelProbe;
        private static ConfigEntry<bool> preferLiveBodyPresenter;
        private static ConfigEntry<string> probePackId;
        private static ConfigEntry<string> probeInteractionId;
        private static ConfigEntry<string> probeLiveBodyInteractionId;
        private static ConfigEntry<string> probeViewmodelManifestFileName;
        private static ConfigEntry<string> probeBodyManifestFileName;
        private static ConfigEntry<string> probeActionReadyBoolName;
        private static ConfigEntry<string> probeActionTriggerName;
        private static ConfigEntry<string> probeActionIndexParamName;
        private static bool initialized;
        private static bool packRegistrationAttempted;
        private static bool packRegistered;
        private static bool probeDisabled;
        private static bool viewmodelRegistered;
        private static bool liveBodyRegistered;
        private static float expectedDurationSeconds = DefaultDurationSeconds;
        private static float liveBodyDurationSeconds = DefaultDurationSeconds;
        private static InteractionAnimationHandle activeHandle = InteractionAnimationHandle.Empty;
        private static DateTime activeExpectedEndUtc = DateTime.MinValue;

        // Test keys for the action system (production input comes from the consuming gameplay
        // mod through the public API; these exist so animations can be verified locally). Each
        // parameter name is config-supplied and empty by default, so an unconfigured probe
        // drives nothing.
        private static bool actionReady;
        private static int nextActionIndex = 1;

        internal static bool PageDownConsumedThisFrame { get; private set; }

        // Every probe payload is consumer-supplied through config; nothing is baked in.
        private static string PackId => ReadConfigString(probePackId);
        private static string InteractionId => ReadConfigString(probeInteractionId);
        private static string LiveBodyInteractionId => ReadConfigString(probeLiveBodyInteractionId);
        private static string ManifestFileName => ReadConfigString(probeViewmodelManifestFileName);
        private static string LiveBodyManifestFileName => ReadConfigString(probeBodyManifestFileName);
        private static string ActionReadyBoolName => ReadConfigString(probeActionReadyBoolName);
        private static string ActionTriggerName => ReadConfigString(probeActionTriggerName);
        private static string ActionIndexParamName => ReadConfigString(probeActionIndexParamName);

        private static string ReadConfigString(ConfigEntry<string> entry)
        {
            try
            {
                return entry != null ? (entry.Value ?? string.Empty).Trim() : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        internal static void Initialize(ConfigFile config, ManualLogSource logger)
        {
            if (initialized)
                return;

            InteractionAnimationApiDebugProbe.logger = logger;
            if (config != null)
            {
                enablePageDownViewmodelProbe = config.Bind(
                    ConfigSection,
                    "Enable Page Down Viewmodel Probe",
                    true,
                    "Dev-only local proof. When enabled, Page Down starts the Interaction Animation API V2 dedicated first-person viewmodel interaction and suppresses the old live-arm Page Down probe for that frame.");
                preferLiveBodyPresenter = config.Bind(
                    ConfigSection,
                    "Prefer Live Body Animator For Page Down",
                    true,
                    "When enabled and the live-body manifest is registered, Page Down starts the LC-native live body animator interaction (authored controller on playerBodyAnimator, real LC arms) instead of the camera-local viewmodel proof.");
                probePackId = config.Bind(
                    ConfigSection,
                    "Probe Pack Id",
                    string.Empty,
                    "Pack id the probe registers its interactions under. Empty (default) disables the probe.");
                probeInteractionId = config.Bind(
                    ConfigSection,
                    "Probe Viewmodel Interaction Id",
                    string.Empty,
                    "Interaction id registered for the dedicated local viewmodel probe. Empty disables the viewmodel half of the probe.");
                probeLiveBodyInteractionId = config.Bind(
                    ConfigSection,
                    "Probe Live Body Interaction Id",
                    string.Empty,
                    "Interaction id registered for the live-body probe. Empty disables the live-body half of the probe.");
                probeViewmodelManifestFileName = config.Bind(
                    ConfigSection,
                    "Probe Viewmodel Manifest File Name",
                    string.Empty,
                    "File name of the dedicated local viewmodel manifest, resolved against the default asset roots. Empty disables the viewmodel half of the probe.");
                probeBodyManifestFileName = config.Bind(
                    ConfigSection,
                    "Probe Body Manifest File Name",
                    string.Empty,
                    "File name of the live-body manifest, resolved against the default asset roots. Empty disables the live-body half of the probe.");
                probeActionReadyBoolName = config.Bind(
                    ConfigSection,
                    "Probe Action Ready Bool Parameter",
                    string.Empty,
                    "Animator Bool parameter the Home key toggles on the running probe interaction. Empty disables that key.");
                probeActionTriggerName = config.Bind(
                    ConfigSection,
                    "Probe Action Trigger Parameter",
                    string.Empty,
                    "Animator Trigger parameter the End key fires on the running probe interaction. Empty disables that key.");
                probeActionIndexParamName = config.Bind(
                    ConfigSection,
                    "Probe Action Index Parameter",
                    string.Empty,
                    "Animator Int parameter the End key sets (cycling 1-4) before firing the action trigger. Empty leaves the index undriven.");
            }

            initialized = true;
            logger?.LogInfo(
                "[LCInteractionAnimationAPI.Probe] probe.ready: " +
                "Page Down V2 probe is initialized. Configure the probe pack/interaction/manifest entries " +
                "to arm it, then press Page Down in-game.");
        }

        internal static void Tick()
        {
            PageDownConsumedThisFrame = false;

            if (!initialized || probeDisabled || !IsPageDownViewmodelProbeEnabled())
                return;

            ClearInactiveLocalHandle();
            ClearExpiredLocalHandle();
            EnsurePackRegistered();

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            TickActionTestKeys(keyboard);

            if (!keyboard.pageDownKey.wasPressedThisFrame)
                return;

            PageDownConsumedThisFrame = true;

            // Toggle: first press starts the interaction (indefinite hold); second press plays
            // the exit animation and the interaction ends on its own.
            if (activeHandle.IsValid && LCInteractionAnimationAPI.IsInteractionActive(activeHandle))
            {
                if (LCInteractionAnimationAPI.TryBeginInteractionExit(activeHandle, out string exitReason))
                {
                    logger?.LogInfo(
                        "[LCInteractionAnimationAPI.Probe] probe.toggle_off: " +
                        $"handle={activeHandle} exit animation started.");
                }
                else
                {
                    logger?.LogWarning(
                        "[LCInteractionAnimationAPI.Probe] probe.toggle_off_failed: " + exitReason +
                        " — forcing stop.");
                    LCInteractionAnimationAPI.TryStopInteraction(activeHandle, InteractionAnimationStopReason.Requested);
                    activeHandle = InteractionAnimationHandle.Empty;
                    activeExpectedEndUtc = DateTime.MinValue;
                }
                return;
            }

            TryStartPageDownViewmodelProbe();
        }

        private static void ClearInactiveLocalHandle()
        {
            if (!activeHandle.IsValid || LCInteractionAnimationAPI.IsInteractionActive(activeHandle))
                return;

            activeHandle = InteractionAnimationHandle.Empty;
            activeExpectedEndUtc = DateTime.MinValue;
            actionReady = false;
            nextActionIndex = 1;
        }

        // Home toggles the action-ready loop; End fires the next tap gesture (cycles 1-4).
        private static void TickActionTestKeys(Keyboard keyboard)
        {
            if (!activeHandle.IsValid || !LCInteractionAnimationAPI.IsInteractionActive(activeHandle))
                return;

            string actionReadyBoolName = ActionReadyBoolName;
            if (keyboard.homeKey.wasPressedThisFrame && !string.IsNullOrWhiteSpace(actionReadyBoolName))
            {
                actionReady = !actionReady;
                bool applied = LCInteractionAnimationAPI.TrySetInteractionBool(activeHandle, actionReadyBoolName, actionReady);
                logger?.LogInfo(
                    "[LCInteractionAnimationAPI.Probe] probe.action_ready_toggled: " +
                    $"handle={activeHandle} parameter='{actionReadyBoolName}' value={actionReady} applied={applied}.");
            }

            string actionTriggerName = ActionTriggerName;
            if (keyboard.endKey.wasPressedThisFrame && !string.IsNullOrWhiteSpace(actionTriggerName))
            {
                string actionIndexParamName = ActionIndexParamName;
                bool indexApplied = !string.IsNullOrWhiteSpace(actionIndexParamName) &&
                    LCInteractionAnimationAPI.TrySetInteractionInt(activeHandle, actionIndexParamName, nextActionIndex);
                bool triggerApplied = LCInteractionAnimationAPI.TryFireInteractionTrigger(activeHandle, actionTriggerName);
                logger?.LogInfo(
                    "[LCInteractionAnimationAPI.Probe] probe.action_fired: " +
                    $"handle={activeHandle} trigger='{actionTriggerName}' actionIndex={nextActionIndex} " +
                    $"indexApplied={indexApplied} triggerApplied={triggerApplied}.");
                nextActionIndex = nextActionIndex >= 4 ? 1 : nextActionIndex + 1;
            }
        }

        internal static void Shutdown()
        {
            try
            {
                if (activeHandle.IsValid)
                    LCInteractionAnimationAPI.TryStopInteraction(activeHandle, InteractionAnimationStopReason.Shutdown);
            }
            catch (Exception exception)
            {
                logger?.LogWarning(
                    "[LCInteractionAnimationAPI.Probe] probe.shutdown_failed: " + exception.Message);
            }

            activeHandle = InteractionAnimationHandle.Empty;
            activeExpectedEndUtc = DateTime.MinValue;
            packRegistrationAttempted = false;
            packRegistered = false;
            probeDisabled = false;
            viewmodelRegistered = false;
            liveBodyRegistered = false;
            expectedDurationSeconds = DefaultDurationSeconds;
            liveBodyDurationSeconds = DefaultDurationSeconds;
            initialized = false;
            enablePageDownViewmodelProbe = null;
            preferLiveBodyPresenter = null;
            probePackId = null;
            probeInteractionId = null;
            probeLiveBodyInteractionId = null;
            probeViewmodelManifestFileName = null;
            probeBodyManifestFileName = null;
            probeActionReadyBoolName = null;
            probeActionTriggerName = null;
            probeActionIndexParamName = null;
            logger = null;
            PageDownConsumedThisFrame = false;
        }

        private static void EnsurePackRegistered()
        {
            if (packRegistered || packRegistrationAttempted)
                return;

            packRegistrationAttempted = true;

            string packId = PackId;
            if (string.IsNullOrWhiteSpace(packId))
            {
                // Unconfigured probe: nothing to register, so go quiet instead of warning
                // on every key press.
                probeDisabled = true;
                logger?.LogInfo(
                    "[LCInteractionAnimationAPI.Probe] probe.disabled: pack_id_not_configured — " +
                    $"set '{ConfigSection}' > 'Probe Pack Id' (plus the interaction and manifest " +
                    "entries) to arm the Page Down probe.");
                return;
            }

            var interactions = new System.Collections.Generic.List<InteractionAnimationDefinition>();

            string viewmodelInteractionId = InteractionId;
            InteractionAnimationManifest viewmodelManifest = null;
            if (!string.IsNullOrWhiteSpace(viewmodelInteractionId) &&
                TryReadManifest(ManifestFileName, out string viewmodelJson, out viewmodelManifest))
            {
                expectedDurationSeconds = viewmodelManifest.durationSeconds > 0f
                    ? viewmodelManifest.durationSeconds
                    : DefaultDurationSeconds;
                interactions.Add(new InteractionAnimationDefinition
                {
                    InteractionId = viewmodelInteractionId,
                    DisplayName = viewmodelManifest.displayName,
                    ExpectedDurationSeconds = expectedDurationSeconds,
                    PresentationKind = InteractionAnimationPresentationKind.DedicatedLocalViewmodel,
                    ManifestJson = viewmodelJson
                });
                viewmodelRegistered = true;
            }

            string liveBodyInteractionId = LiveBodyInteractionId;
            if (!string.IsNullOrWhiteSpace(liveBodyInteractionId) &&
                TryReadManifest(LiveBodyManifestFileName, out string liveBodyJson, out InteractionAnimationManifest liveBodyManifest))
            {
                // durationSeconds 0 = indefinite (toggle lifecycle: ends via TryBeginInteractionExit).
                liveBodyDurationSeconds = Math.Max(0f, liveBodyManifest.durationSeconds);
                interactions.Add(new InteractionAnimationDefinition
                {
                    InteractionId = liveBodyInteractionId,
                    DisplayName = liveBodyManifest.displayName,
                    ExpectedDurationSeconds = liveBodyDurationSeconds,
                    PresentationKind = InteractionAnimationPresentationKind.BodyWorld,
                    ManifestJson = liveBodyJson
                });
                liveBodyRegistered = true;
            }

            if (interactions.Count == 0)
            {
                // Dev-only payloads are absent or unconfigured (release packages ship no
                // probe manifests). Warn once and go quiet instead of retrying or
                // rejecting every key press.
                probeDisabled = true;
                logger?.LogWarning(
                    "[LCInteractionAnimationAPI.Probe] probe.disabled: no_valid_manifest — " +
                    "no configured probe manifest could be read, so the Page Down probe is inactive.");
                return;
            }

            var pack = new InteractionAnimationPackDefinition
            {
                PackId = packId,
                DisplayName = "Interaction Animation API V2 Debug Interactions",
                Version = Plugin.Version,
                Author = "Y4NGZ",
                Interactions = interactions.ToArray()
            };

            if (!LCInteractionAnimationAPI.TryRegisterInteractionPack(pack, out string reason))
            {
                liveBodyRegistered = false;
                viewmodelRegistered = false;
                probeDisabled = true;
                logger?.LogWarning(
                    "[LCInteractionAnimationAPI.Probe] probe.pack_registration_failed: " + reason);
                return;
            }

            packRegistered = true;
            if (viewmodelManifest != null && !UseLiveBodyPresenter())
                LocalViewmodelPresenter.BeginPreload(viewmodelManifest, logger);

            logger?.LogInfo(
                "[LCInteractionAnimationAPI.Probe] probe.pack_registered: " +
                $"pack='{PackId}' interactions={interactions.Count} liveBodyRegistered={liveBodyRegistered} " +
                $"viewmodelDurationSeconds={expectedDurationSeconds:0.###} liveBodyDurationSeconds={liveBodyDurationSeconds:0.###}.");
        }

        private static bool TryReadManifest(
            string fileName,
            out string manifestJson,
            out InteractionAnimationManifest manifest)
        {
            manifestJson = string.Empty;
            manifest = null;

            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            string manifestPath = ResolveFilePath(fileName);
            if (string.IsNullOrWhiteSpace(manifestPath) || !File.Exists(manifestPath))
            {
                logger?.LogWarning(
                    "[LCInteractionAnimationAPI.Probe] probe.manifest_missing: " +
                    $"file='{fileName}' resolvedPath='{manifestPath}'.");
                return false;
            }

            try
            {
                manifestJson = File.ReadAllText(manifestPath);
            }
            catch (Exception exception)
            {
                logger?.LogWarning(
                    "[LCInteractionAnimationAPI.Probe] probe.manifest_read_failed: " +
                    $"file='{manifestPath}' reason='{exception.Message}'.");
                return false;
            }

            if (!InteractionAnimationManifest.TryParse(manifestJson, out manifest, out string manifestReason))
            {
                logger?.LogWarning(
                    "[LCInteractionAnimationAPI.Probe] probe.manifest_invalid: " +
                    $"file='{fileName}' reason='{manifestReason}'.");
                return false;
            }

            return true;
        }

        private static bool UseLiveBodyPresenter()
        {
            if (!liveBodyRegistered)
                return false;

            // The viewmodel payload is dev-only, so fall back to the live body
            // presenter whenever it is the only registered interaction.
            if (!viewmodelRegistered)
                return true;

            try
            {
                return preferLiveBodyPresenter == null || preferLiveBodyPresenter.Value;
            }
            catch
            {
                return true;
            }
        }

        private static void TryStartPageDownViewmodelProbe()
        {
            if (!packRegistered)
            {
                logger?.LogWarning(
                    "[LCInteractionAnimationAPI.Probe] probe.start_rejected: pack_not_registered.");
                return;
            }

            PlayerControllerB player = ResolveLocalPlayer();
            if (player == null)
            {
                logger?.LogWarning(
                    "[LCInteractionAnimationAPI.Probe] probe.start_rejected: local player is not available yet.");
                return;
            }

            if (activeHandle.IsValid && DateTime.UtcNow < activeExpectedEndUtc)
            {
                logger?.LogInfo(
                    "[LCInteractionAnimationAPI.Probe] probe.start_ignored: " +
                    $"handle={activeHandle} is still within expected duration.");
                return;
            }

            bool useLiveBody = UseLiveBodyPresenter();
            string interactionId = useLiveBody ? LiveBodyInteractionId : InteractionId;
            string presenterLabel = useLiveBody ? "LiveBodyAnimator" : "DedicatedLocalViewmodel";
            float startDurationSeconds = useLiveBody ? liveBodyDurationSeconds : expectedDurationSeconds;

            var request = new InteractionAnimationRequest
            {
                Player = player,
                PackId = PackId,
                InteractionId = interactionId,
                OwnerModId = OwnerModId
            };

            logger?.LogInfo(
                "[LCInteractionAnimationAPI.Probe] probe.triggered: " +
                $"key='Page Down' player='{GetPlayerLabel(player)}' interaction='{interactionId}' presenter='{presenterLabel}'.");

            if (!LCInteractionAnimationAPI.TryStartInteraction(
                    request,
                    out InteractionAnimationHandle handle,
                    out string reason))
            {
                activeHandle = InteractionAnimationHandle.Empty;
                activeExpectedEndUtc = DateTime.MinValue;
                logger?.LogWarning(
                    "[LCInteractionAnimationAPI.Probe] probe.start_rejected: " + reason);
                return;
            }

            activeHandle = handle;
            activeExpectedEndUtc = startDurationSeconds > 0f
                ? DateTime.UtcNow.AddSeconds(startDurationSeconds).Add(ActiveHandleGracePeriod)
                : DateTime.MaxValue;
            logger?.LogInfo(
                "[LCInteractionAnimationAPI.Probe] probe.started: " +
                $"handle={activeHandle} pack='{PackId}' interaction='{interactionId}' expectedDurationSeconds={startDurationSeconds:0.###}.");
        }

        private static void ClearExpiredLocalHandle()
        {
            if (!activeHandle.IsValid || DateTime.UtcNow < activeExpectedEndUtc)
                return;

            logger?.LogInfo(
                "[LCInteractionAnimationAPI.Probe] probe.handle_expired_locally: " +
                $"handle={activeHandle} expectedDurationSeconds={expectedDurationSeconds:0.###}.");
            activeHandle = InteractionAnimationHandle.Empty;
            activeExpectedEndUtc = DateTime.MinValue;
        }

        private static bool IsPageDownViewmodelProbeEnabled()
        {
            try
            {
                return enablePageDownViewmodelProbe == null || enablePageDownViewmodelProbe.Value;
            }
            catch
            {
                return true;
            }
        }

        private static string ResolveFilePath(string fileName)
        {
            string firstCandidate = string.Empty;
            string[] roots = InteractionAnimationAssetPathResolver.GetDefaultAssetRoots();
            for (int i = 0; i < roots.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(roots[i]))
                    continue;

                string candidate = Path.Combine(roots[i], fileName);
                if (string.IsNullOrEmpty(firstCandidate))
                    firstCandidate = candidate;
                if (File.Exists(candidate))
                    return candidate;
            }

            return firstCandidate;
        }

        private static PlayerControllerB ResolveLocalPlayer()
        {
            try
            {
                return StartOfRound.Instance != null ? StartOfRound.Instance.localPlayerController : null;
            }
            catch
            {
                return null;
            }
        }

        private static string GetPlayerLabel(PlayerControllerB player)
        {
            if (player == null)
                return "<null player>";

            try
            {
                string name = string.IsNullOrWhiteSpace(player.playerUsername)
                    ? "local player"
                    : player.playerUsername;
                return $"{name} (clientId={player.actualClientId})";
            }
            catch
            {
                return "local player";
            }
        }
    }
}
