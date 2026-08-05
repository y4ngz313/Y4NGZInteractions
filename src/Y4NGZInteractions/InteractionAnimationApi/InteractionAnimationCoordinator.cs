using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using GameNetcodeStuff;
using UnityEngine;
using Y4NGZInteractions.InteractionAnimationApi.Authoring;
using Y4NGZInteractions.InteractionAnimationApi.Backends;
using Y4NGZInteractions.InteractionAnimationApi.Presenters;

namespace Y4NGZInteractions.InteractionAnimationApi
{
    internal sealed class InteractionAnimationCoordinator
    {
        private readonly ManualLogSource logger;
        private readonly Dictionary<string, InteractionAnimationPackDefinition> packs =
            new Dictionary<string, InteractionAnimationPackDefinition>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<InteractionAnimationHandle, InteractionAnimationSession> activeSessions =
            new Dictionary<InteractionAnimationHandle, InteractionAnimationSession>();

        internal InteractionAnimationCoordinator(ManualLogSource logger)
        {
            this.logger = logger;
        }

        internal bool TryRegisterInteractionPack(
            InteractionAnimationPackDefinition pack,
            out string reason)
        {
            reason = string.Empty;

            InteractionAnimationValidationResult validation = ValidatePack(pack);
            if (!validation.IsValid)
            {
                reason = validation.Reason;
                return false;
            }

            if (!InteractionAnimationAssetPathResolver.TryNormalizeAssetRoot(
                    pack.AssetRootPath,
                    out string normalizedAssetRoot,
                    out reason))
            {
                return false;
            }

            pack.AssetRootPath = normalizedAssetRoot;

            if (packs.ContainsKey(pack.PackId))
            {
                reason = "pack_already_registered";
                return false;
            }

            packs.Add(pack.PackId, pack);
            logger?.LogInfo(
                "[LCInteractionAnimationAPI] pack.registered: " +
                $"pack='{pack.PackId}' interactions={pack.Interactions.Length}.");
            return true;
        }

        internal bool TryStartInteraction(
            InteractionAnimationRequest request,
            out InteractionAnimationHandle handle,
            out string reason)
        {
            handle = InteractionAnimationHandle.Empty;
            reason = string.Empty;

            InteractionAnimationValidationResult requestValidation = ValidateRequest(request);
            if (!requestValidation.IsValid)
            {
                reason = requestValidation.Reason;
                return false;
            }

            if (!packs.TryGetValue(request.PackId, out InteractionAnimationPackDefinition pack))
            {
                reason = "pack_not_registered";
                return false;
            }

            InteractionAnimationDefinition definition = pack.Interactions.FirstOrDefault(
                interaction => string.Equals(
                    interaction.InteractionId,
                    request.InteractionId,
                    StringComparison.OrdinalIgnoreCase));

            if (definition == null)
            {
                reason = "interaction_not_registered";
                return false;
            }

            if (!InteractionAnimationManifest.TryParse(
                    definition.ManifestJson,
                    out InteractionAnimationManifest manifest,
                    out reason))
            {
                return false;
            }

            InteractionAnimationValidationResult manifestValidation =
                manifest.Validate(definition.InteractionId, definition.PresentationKind);
            if (!manifestValidation.IsValid)
            {
                reason = manifestValidation.Reason;
                return false;
            }

            if (definition.PresentationKind == InteractionAnimationPresentationKind.BodyWorld &&
                !TryAcquireBodyWorldAnimator(request.Player, request, out reason))
            {
                return false;
            }

            handle = InteractionAnimationHandle.NewHandle();
            var context = new InteractionAnimationContext(
                handle,
                request,
                definition,
                manifest,
                pack.AssetRootPath,
                logger);
            var session = new InteractionAnimationSession(
                handle,
                context,
                CreateBackend(definition),
                CreatePresenter(definition));

            if (!session.TryStart(out reason))
            {
                handle = InteractionAnimationHandle.Empty;
                return false;
            }

            activeSessions.Add(handle, session);
            logger?.LogInfo(
                "[LCInteractionAnimationAPI] interaction.started: " +
                $"handle={handle} pack='{request.PackId}' interaction='{request.InteractionId}' " +
                $"presentation='{definition.PresentationKind}'.");
            return true;
        }

        internal bool TryPreloadInteractionAssets(
            string packId,
            string interactionId,
            out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(packId) || string.IsNullOrWhiteSpace(interactionId))
            {
                reason = "preload_invalid_identity";
                return false;
            }

            if (!packs.TryGetValue(packId, out InteractionAnimationPackDefinition pack))
            {
                reason = "pack_not_registered";
                return false;
            }

            InteractionAnimationDefinition definition = pack.Interactions.FirstOrDefault(
                interaction => string.Equals(
                    interaction.InteractionId,
                    interactionId,
                    StringComparison.OrdinalIgnoreCase));
            if (definition == null)
            {
                reason = "interaction_not_registered";
                return false;
            }

            if (!InteractionAnimationManifest.TryParse(
                    definition.ManifestJson,
                    out InteractionAnimationManifest manifest,
                    out reason))
                return false;

            InteractionAnimationValidationResult validation =
                manifest.Validate(definition.InteractionId, definition.PresentationKind);
            if (!validation.IsValid)
            {
                reason = validation.Reason;
                return false;
            }

            if (definition.PresentationKind == InteractionAnimationPresentationKind.BodyWorld)
            {
                return LiveBodyAnimatorPresenter.TryPreloadBundles(
                    manifest,
                    pack.AssetRootPath,
                    logger,
                    out reason);
            }

            return LocalViewmodelPresenter.TryBeginPreload(
                manifest,
                pack.AssetRootPath,
                logger,
                out reason);
        }

        internal bool TryStopInteraction(
            InteractionAnimationHandle handle,
            InteractionAnimationStopReason stopReason)
        {
            if (!activeSessions.TryGetValue(handle, out InteractionAnimationSession session))
                return false;

            session.Stop(stopReason);
            activeSessions.Remove(handle);
            logger?.LogInfo(
                "[LCInteractionAnimationAPI] interaction.stopped: " +
                $"handle={handle} reason='{stopReason}'.");
            return true;
        }

        internal bool IsInteractionActive(InteractionAnimationHandle handle)
        {
            return activeSessions.ContainsKey(handle);
        }

        internal bool IsPlayerInteractionActive(PlayerControllerB player)
        {
            return player != null && activeSessions.Values.Any(
                session => session.PresentationKind == InteractionAnimationPresentationKind.BodyWorld &&
                           ReferenceEquals(session.Player, player));
        }

        internal bool TryStopPlayerInteractions(
            PlayerControllerB player,
            InteractionAnimationStopReason stopReason)
        {
            if (player == null)
                return false;

            InteractionAnimationHandle[] handles = activeSessions
                .Where(pair =>
                    pair.Value.PresentationKind == InteractionAnimationPresentationKind.BodyWorld &&
                    ReferenceEquals(pair.Value.Player, player))
                .Select(pair => pair.Key)
                .ToArray();

            bool stoppedAny = false;
            for (int i = 0; i < handles.Length; i++)
                stoppedAny |= TryStopInteraction(handles[i], stopReason);
            return stoppedAny;
        }

        internal bool TrySetInteractionAnimatorParameter(
            InteractionAnimationHandle handle,
            string parameterName,
            UnityEngine.AnimatorControllerParameterType parameterType,
            float value)
        {
            return activeSessions.TryGetValue(handle, out InteractionAnimationSession session) &&
                   session.TrySetAnimatorParameter(parameterName, parameterType, value);
        }

        /// <summary>
        /// Toggle-style graceful exit: the presenter plays its put-away animation, then the
        /// session auto-stops after the presenter-reported exit duration.
        /// </summary>
        internal bool TryBeginInteractionExit(InteractionAnimationHandle handle, out string reason)
        {
            reason = string.Empty;

            if (!activeSessions.TryGetValue(handle, out InteractionAnimationSession session))
            {
                reason = "interaction_not_active";
                return false;
            }

            float exitSeconds = session.BeginExit();
            logger?.LogInfo(
                "[LCInteractionAnimationAPI] interaction.exit_begun: " +
                $"handle={handle} exitSeconds={exitSeconds:0.###}.");
            return true;
        }

        internal void Tick(float deltaTime)
        {
            if (activeSessions.Count == 0)
                return;

            var naturallyEnded = new List<InteractionAnimationHandle>();
            var interrupted = new List<InteractionAnimationHandle>();
            foreach (KeyValuePair<InteractionAnimationHandle, InteractionAnimationSession> pair in activeSessions)
            {
                pair.Value.Tick(deltaTime);
                if (pair.Value.WantsAutoStop)
                    interrupted.Add(pair.Key);
                else if (pair.Value.IsPastExpectedDuration)
                    naturallyEnded.Add(pair.Key);
            }

            for (int i = 0; i < interrupted.Count; i++)
                TryStopInteraction(interrupted[i], InteractionAnimationStopReason.Interrupted);

            for (int i = 0; i < naturallyEnded.Count; i++)
                TryStopInteraction(naturallyEnded[i], InteractionAnimationStopReason.NaturalEnd);
        }

        internal void Shutdown()
        {
            var handles = activeSessions.Keys.ToArray();
            for (int i = 0; i < handles.Length; i++)
                TryStopInteraction(handles[i], InteractionAnimationStopReason.Shutdown);

            packs.Clear();
        }

        private bool TryAcquireBodyWorldAnimator(
            PlayerControllerB player,
            InteractionAnimationRequest request,
            out string reason)
        {
            reason = string.Empty;

            InteractionAnimationHandle[] conflicts = activeSessions
                .Where(pair =>
                    pair.Value.PresentationKind == InteractionAnimationPresentationKind.BodyWorld &&
                    ReferenceEquals(pair.Value.Player, player))
                .Select(pair => pair.Key)
                .ToArray();

            for (int i = 0; i < conflicts.Length; i++)
            {
                logger?.LogInfo(
                    "[LCInteractionAnimationAPI] interaction.preempted: " +
                    $"handle={conflicts[i]} nextPack='{request.PackId}' " +
                    $"nextInteraction='{request.InteractionId}'.");
                TryStopInteraction(conflicts[i], InteractionAnimationStopReason.Interrupted);
            }

            Animator animator = player != null ? player.playerBodyAnimator : null;
            if (animator == null)
            {
                reason = "missing_body_animator";
                return false;
            }

            RuntimeAnimatorController expectedVanilla = ResolveExpectedVanillaController(player);
            if (expectedVanilla != null && animator.runtimeAnimatorController != expectedVanilla)
            {
                reason = "player_animator_owned_externally";
                logger?.LogWarning(
                    "[LCInteractionAnimationAPI] interaction.start_rejected: " +
                    $"reason='{reason}' pack='{request.PackId}' interaction='{request.InteractionId}' " +
                    $"currentController='{animator.runtimeAnimatorController?.name ?? "<none>"}' " +
                    $"expectedController='{expectedVanilla.name}'.");
                return false;
            }

            return true;
        }

        private static RuntimeAnimatorController ResolveExpectedVanillaController(PlayerControllerB player)
        {
            StartOfRound round = StartOfRound.Instance;
            if (round == null || player == null)
                return null;

            PlayerControllerB localPlayer = GameNetworkManager.Instance != null
                ? GameNetworkManager.Instance.localPlayerController
                : null;
            return ReferenceEquals(player, localPlayer)
                ? round.localClientAnimatorController
                : round.otherClientsAnimatorController;
        }

        private static InteractionAnimationValidationResult ValidatePack(
            InteractionAnimationPackDefinition pack)
        {
            if (pack == null)
                return InteractionAnimationValidationResult.Invalid("pack_null");

            if (string.IsNullOrWhiteSpace(pack.PackId))
                return InteractionAnimationValidationResult.Invalid("pack_id_empty");

            if (pack.PackId.Trim() != pack.PackId)
                return InteractionAnimationValidationResult.Invalid("pack_id_has_outer_whitespace");

            if (pack.Interactions == null || pack.Interactions.Length == 0)
                return InteractionAnimationValidationResult.Invalid("pack_interactions_empty");

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < pack.Interactions.Length; i++)
            {
                InteractionAnimationDefinition interaction = pack.Interactions[i];
                if (interaction == null)
                    return InteractionAnimationValidationResult.Invalid("interaction_null");

                if (string.IsNullOrWhiteSpace(interaction.InteractionId))
                    return InteractionAnimationValidationResult.Invalid("interaction_id_empty");

                if (interaction.InteractionId.Trim() != interaction.InteractionId)
                    return InteractionAnimationValidationResult.Invalid("interaction_id_has_outer_whitespace");

                if (!ids.Add(interaction.InteractionId))
                    return InteractionAnimationValidationResult.Invalid("interaction_id_duplicate");
            }

            return InteractionAnimationValidationResult.Valid();
        }

        private static InteractionAnimationValidationResult ValidateRequest(
            InteractionAnimationRequest request)
        {
            if (request == null)
                return InteractionAnimationValidationResult.Invalid("request_null");

            if (request.Player == null)
                return InteractionAnimationValidationResult.Invalid("request_player_missing");

            if (string.IsNullOrWhiteSpace(request.PackId))
                return InteractionAnimationValidationResult.Invalid("request_pack_id_empty");

            if (string.IsNullOrWhiteSpace(request.InteractionId))
                return InteractionAnimationValidationResult.Invalid("request_interaction_id_empty");

            return InteractionAnimationValidationResult.Valid();
        }

        private static IInteractionAnimationBackend CreateBackend(InteractionAnimationDefinition definition)
        {
            return null;
        }

        private static IInteractionPresenter CreatePresenter(InteractionAnimationDefinition definition)
        {
            if (definition.PresentationKind == InteractionAnimationPresentationKind.DedicatedLocalViewmodel ||
                definition.PresentationKind == InteractionAnimationPresentationKind.Hybrid)
            {
                return new LocalViewmodelPresenter();
            }

            if (definition.PresentationKind == InteractionAnimationPresentationKind.BodyWorld)
            {
                return new LiveBodyAnimatorPresenter();
            }

            return null;
        }

        private sealed class InteractionAnimationSession
        {
            private readonly InteractionAnimationHandle handle;
            private readonly InteractionAnimationContext context;
            private readonly IInteractionAnimationBackend backend;
            private readonly IInteractionPresenter presenter;
            private DateTime startedUtc;
            private DateTime exitStopAtUtc = DateTime.MaxValue;
            private bool active;

            internal InteractionAnimationSession(
                InteractionAnimationHandle handle,
                InteractionAnimationContext context,
                IInteractionAnimationBackend backend,
                IInteractionPresenter presenter)
            {
                this.handle = handle;
                this.context = context;
                this.backend = backend;
                this.presenter = presenter;
            }

            internal bool IsPastExpectedDuration
            {
                get
                {
                    if (!active)
                        return false;

                    if (DateTime.UtcNow >= exitStopAtUtc)
                        return true;

                    float duration = context.Definition.ExpectedDurationSeconds > 0f
                        ? context.Definition.ExpectedDurationSeconds
                        : context.Manifest.durationSeconds;
                    return duration > 0f && (DateTime.UtcNow - startedUtc).TotalSeconds >= duration;
                }
            }

            internal float BeginExit()
            {
                if (!active)
                    return 0f;

                float exitSeconds = presenter != null ? Math.Max(0f, presenter.BeginExit()) : 0f;
                exitStopAtUtc = DateTime.UtcNow.AddSeconds(exitSeconds);
                return exitSeconds;
            }

            internal bool WantsAutoStop => active && presenter != null && presenter.ShouldAutoStop;

            internal PlayerControllerB Player => context?.Request?.Player;

            internal InteractionAnimationPresentationKind PresentationKind =>
                context.Definition.PresentationKind;

            internal bool TrySetAnimatorParameter(
                string parameterName,
                UnityEngine.AnimatorControllerParameterType parameterType,
                float value)
            {
                return active && presenter != null &&
                       presenter.TrySetAnimatorParameter(parameterName, parameterType, value);
            }

            internal bool TryStart(out string reason)
            {
                reason = string.Empty;

                if (backend != null && !backend.TryStart(context, out reason))
                    return false;

                if (presenter != null && !presenter.TryStart(context, out reason))
                {
                    backend?.Stop(InteractionAnimationStopReason.Failed);
                    return false;
                }

                startedUtc = DateTime.UtcNow;
                active = true;
                return true;
            }

            internal void Tick(float deltaTime)
            {
                if (!active)
                    return;

                backend?.Tick(deltaTime);
                presenter?.Tick(deltaTime);
            }

            internal void Stop(InteractionAnimationStopReason stopReason)
            {
                if (!active)
                    return;

                presenter?.Stop(stopReason);
                backend?.Stop(stopReason);
                active = false;
            }

            public override string ToString()
            {
                return handle.ToString();
            }
        }
    }
}
