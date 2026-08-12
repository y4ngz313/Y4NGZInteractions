using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GameNetcodeStuff;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using Y4NGZInteractions.InteractionAnimationApi;

namespace Y4NGZInteractions.SampleConsumer
{
    [BepInPlugin(
        "com.example.y4ngzinteractions.sample",
        "Y4NGZInteractions Sample Consumer",
        "1.0.0")]
    public sealed class SampleConsumerPlugin : BaseUnityPlugin
    {
        private const string PackId = "community.example.interactions";
        private ConfigEntry<string> assetRoot;
        private InteractionAnimationHandle lastHandle;
        private bool registered;
        private bool networkAttached;

        private void Awake()
        {
            string assemblyDirectory = Path.GetDirectoryName(
                Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            assetRoot = Config.Bind(
                "Examples",
                "Example Asset Root",
                Path.Combine(assemblyDirectory, "ExamplePayload"),
                "Directory containing the generated example bundles and manifests.");
            LCInteractionAnimationAPI.InteractionEnded += OnInteractionEnded;
            TryRegisterPack();
        }

        private void Update()
        {
            if (!registered)
                TryRegisterPack();
            if (!networkAttached)
                networkAttached = SampleNetworkBridge.TryAttach(Logger, StartPresentation);
            if (!registered || !networkAttached)
                return;

            PlayerControllerB localPlayer = ResolveLocalPlayer();
            if (localPlayer == null)
                return;

            if (Input.GetKeyDown(KeyCode.F6))
            {
                SampleNetworkBridge.Broadcast(
                    localPlayer.actualClientId,
                    InteractionAnimationPresentationKind.DedicatedLocalViewmodel,
                    InteractionAnimationConflictPolicy.RejectIfBusy);
            }
            if (Input.GetKeyDown(KeyCode.F7))
            {
                SampleNetworkBridge.Broadcast(
                    localPlayer.actualClientId,
                    InteractionAnimationPresentationKind.BodyWorld,
                    InteractionAnimationConflictPolicy.RejectIfBusy);
            }
            if (Input.GetKeyDown(KeyCode.F8) && lastHandle.IsValid)
            {
                LCInteractionAnimationAPI.TryStopInteraction(
                    lastHandle, InteractionAnimationStopReason.Requested);
            }
            if (Input.GetKeyDown(KeyCode.F9))
            {
                SampleNetworkBridge.Broadcast(
                    localPlayer.actualClientId,
                    InteractionAnimationPresentationKind.BodyWorld,
                    InteractionAnimationConflictPolicy.InterruptExisting);
            }
        }

        private void OnDestroy()
        {
            SampleNetworkBridge.Detach();
            LCInteractionAnimationAPI.InteractionEnded -= OnInteractionEnded;
            if (lastHandle.IsValid)
            {
                LCInteractionAnimationAPI.TryStopInteraction(
                    lastHandle, InteractionAnimationStopReason.Requested);
            }
        }

        private void TryRegisterPack()
        {
            if (registered || !LCInteractionAnimationAPI.IsInitialized)
                return;

            string root = assetRoot.Value;
            string bodyManifest = Path.Combine(root, "body-world.manifest.json");
            string localManifest = Path.Combine(root, "local-viewmodel.manifest.json");
            if (!File.Exists(bodyManifest) || !File.Exists(localManifest))
                return;

            var pack = new InteractionAnimationPackDefinition
            {
                PackId = PackId,
                Version = "1.0.0",
                AssetRootPath = root,
                Interactions = new[]
                {
                    new InteractionAnimationDefinition
                    {
                        InteractionId = "example-body-wave",
                        PresentationKind = InteractionAnimationPresentationKind.BodyWorld,
                        ManifestJson = File.ReadAllText(bodyManifest)
                    },
                    new InteractionAnimationDefinition
                    {
                        InteractionId = "example-local-inspect",
                        PresentationKind =
                            InteractionAnimationPresentationKind.DedicatedLocalViewmodel,
                        ManifestJson = File.ReadAllText(localManifest)
                    }
                }
            };

            InteractionAnimationValidationReport report =
                LCInteractionAnimationAPI.ValidateInteractionPack(pack);
            foreach (InteractionAnimationValidationIssue issue in report.Issues)
            {
                Logger.Log(
                    issue.Severity == InteractionAnimationValidationSeverity.Error
                        ? BepInEx.Logging.LogLevel.Error
                        : BepInEx.Logging.LogLevel.Warning,
                    issue.Code + " " + issue.JsonPath + ": " + issue.Message);
            }

            registered = LCInteractionAnimationAPI.TryRegisterInteractionPack(
                pack, out string reason);
            if (!registered)
                Logger.LogError("Sample pack registration failed: " + reason);
            else
                Logger.LogInfo("Registered both clean-room example interactions.");
        }

        private void StartPresentation(
            ulong targetClientId,
            InteractionAnimationPresentationKind presentationKind,
            InteractionAnimationConflictPolicy conflictPolicy)
        {
            PlayerControllerB target = ResolvePlayer(targetClientId);
            if (target == null)
            {
                Logger.LogWarning("Could not resolve target player " + targetClientId + ".");
                return;
            }

            if (presentationKind ==
                    InteractionAnimationPresentationKind.DedicatedLocalViewmodel &&
                !ReferenceEquals(target, ResolveLocalPlayer()))
            {
                return;
            }

            string interactionId =
                presentationKind == InteractionAnimationPresentationKind.BodyWorld
                    ? "example-body-wave"
                    : "example-local-inspect";
            var request = new InteractionAnimationRequest
            {
                Player = target,
                PackId = PackId,
                InteractionId = interactionId,
                ConflictPolicy = conflictPolicy
            };

            if (LCInteractionAnimationAPI.TryStartInteraction(
                    request, out InteractionAnimationHandle handle, out string reason))
            {
                lastHandle = handle;
                Logger.LogInfo(
                    "Started " + presentationKind + " for client " + targetClientId +
                    " with " + conflictPolicy + ".");
            }
            else
            {
                Logger.LogWarning(
                    "Start rejected for " + presentationKind + ": " + reason);
            }
        }

        private void OnInteractionEnded(
            object sender,
            InteractionAnimationEndedEventArgs args)
        {
            Logger.LogInfo(
                "Interaction ended after restoration: " + args.InteractionId +
                " reason=" + args.StopReason + " handle=" + args.Handle + ".");
            if (args.Handle == lastHandle)
                lastHandle = InteractionAnimationHandle.Empty;
        }

        private static PlayerControllerB ResolveLocalPlayer()
        {
            return GameNetworkManager.Instance?.localPlayerController ??
                   StartOfRound.Instance?.localPlayerController;
        }

        private static PlayerControllerB ResolvePlayer(ulong actualClientId)
        {
            PlayerControllerB[] players = StartOfRound.Instance?.allPlayerScripts;
            return players?.FirstOrDefault(player =>
                player != null && player.actualClientId == actualClientId);
        }
    }

    internal static class SampleNetworkBridge
    {
        private const string RequestMessageName =
            "Y4NGZInteractions.SampleConsumer.Request.v1";
        private const string PresentationMessageName =
            "Y4NGZInteractions.SampleConsumer.Presentation.v1";
        private static ManualLogSource logger;
        private static Action<
            ulong,
            InteractionAnimationPresentationKind,
            InteractionAnimationConflictPolicy> receive;
        private static CustomMessagingManager messaging;

        internal static bool TryAttach(
            ManualLogSource valueLogger,
            Action<
                ulong,
                InteractionAnimationPresentationKind,
                InteractionAnimationConflictPolicy> receiver)
        {
            if (messaging != null)
                return true;
            NetworkManager network = NetworkManager.Singleton;
            if (network == null || !network.IsListening)
                return false;

            logger = valueLogger;
            receive = receiver;
            messaging = network.CustomMessagingManager;
            messaging.RegisterNamedMessageHandler(RequestMessageName, OnRequest);
            messaging.RegisterNamedMessageHandler(
                PresentationMessageName, OnPresentation);
            logger?.LogInfo("Attached sample consumer-owned network messages.");
            return true;
        }

        internal static void Detach()
        {
            if (messaging != null)
            {
                messaging.UnregisterNamedMessageHandler(RequestMessageName);
                messaging.UnregisterNamedMessageHandler(PresentationMessageName);
            }
            messaging = null;
            receive = null;
            logger = null;
        }

        internal static void Broadcast(
            ulong targetClientId,
            InteractionAnimationPresentationKind presentationKind,
            InteractionAnimationConflictPolicy conflictPolicy)
        {
            NetworkManager network = NetworkManager.Singleton;
            if (messaging == null || network == null || !network.IsListening)
                return;

            if (network.IsServer)
            {
                FanOut(targetClientId, presentationKind, conflictPolicy);
                return;
            }

            Send(
                RequestMessageName,
                NetworkManager.ServerClientId,
                targetClientId,
                presentationKind,
                conflictPolicy);
        }

        private static void OnRequest(
            ulong senderClientId,
            FastBufferReader reader)
        {
            NetworkManager network = NetworkManager.Singleton;
            if (network == null || !network.IsServer ||
                !TryReadPayload(
                    reader,
                    senderClientId,
                    out ulong targetClientId,
                    out InteractionAnimationPresentationKind presentationKind,
                    out InteractionAnimationConflictPolicy conflictPolicy))
            {
                return;
            }

            if (targetClientId != senderClientId)
            {
                logger?.LogWarning(
                    "Rejected sample request for a different player from client " +
                    senderClientId + ".");
                return;
            }

            FanOut(targetClientId, presentationKind, conflictPolicy);
        }

        private static void FanOut(
            ulong targetClientId,
            InteractionAnimationPresentationKind presentationKind,
            InteractionAnimationConflictPolicy conflictPolicy)
        {
            NetworkManager network = NetworkManager.Singleton;
            if (network == null || !network.IsServer || messaging == null)
                return;

            receive?.Invoke(targetClientId, presentationKind, conflictPolicy);
            foreach (ulong observingClientId in network.ConnectedClientsIds)
            {
                if (observingClientId == network.LocalClientId)
                    continue;
                Send(
                    PresentationMessageName,
                    observingClientId,
                    targetClientId,
                    presentationKind,
                    conflictPolicy);
            }
        }

        private static void OnPresentation(
            ulong senderClientId,
            FastBufferReader reader)
        {
            NetworkManager network = NetworkManager.Singleton;
            if (network == null || senderClientId != NetworkManager.ServerClientId ||
                !TryReadPayload(
                    reader,
                    senderClientId,
                    out ulong targetClientId,
                    out InteractionAnimationPresentationKind presentationKind,
                    out InteractionAnimationConflictPolicy conflictPolicy))
            {
                return;
            }

            receive?.Invoke(targetClientId, presentationKind, conflictPolicy);
        }

        private static bool TryReadPayload(
            FastBufferReader reader,
            ulong senderClientId,
            out ulong targetClientId,
            out InteractionAnimationPresentationKind presentationKind,
            out InteractionAnimationConflictPolicy conflictPolicy)
        {
            targetClientId = 0;
            presentationKind = default;
            conflictPolicy = default;
            try
            {
                reader.ReadValueSafe(out targetClientId);
                reader.ReadValueSafe(out byte presentationValue);
                reader.ReadValueSafe(out byte policyValue);
                presentationKind =
                    (InteractionAnimationPresentationKind)presentationValue;
                conflictPolicy =
                    (InteractionAnimationConflictPolicy)policyValue;
            }
            catch (Exception exception)
            {
                logger?.LogWarning(
                    "Rejected malformed sample message from client " + senderClientId +
                    ": " + exception.Message);
                return false;
            }
            if (Enum.IsDefined(
                    typeof(InteractionAnimationPresentationKind), presentationKind) &&
                Enum.IsDefined(
                    typeof(InteractionAnimationConflictPolicy), conflictPolicy))
            {
                return true;
            }

            logger?.LogWarning(
                "Rejected invalid sample message from client " + senderClientId + ".");
            return false;
        }

        private static void Send(
            string messageName,
            ulong recipientClientId,
            ulong targetClientId,
            InteractionAnimationPresentationKind presentationKind,
            InteractionAnimationConflictPolicy conflictPolicy)
        {
            using (var writer = new FastBufferWriter(
                       sizeof(ulong) + 2,
                       Allocator.Temp,
                       sizeof(ulong) + 2))
            {
                writer.WriteValueSafe(targetClientId);
                writer.WriteValueSafe((byte)presentationKind);
                writer.WriteValueSafe((byte)conflictPolicy);
                messaging.SendNamedMessage(
                    messageName, recipientClientId, writer);
            }
        }
    }
}