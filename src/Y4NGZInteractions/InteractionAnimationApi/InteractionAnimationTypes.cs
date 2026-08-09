using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using BepInEx.Logging;
using GameNetcodeStuff;
using Y4NGZInteractions.InteractionAnimationApi.Authoring;

namespace Y4NGZInteractions.InteractionAnimationApi
{
    /// <summary>Identifies the local presentation path used by an interaction.</summary>
    public enum InteractionAnimationPresentationKind
    {
        /// <summary>Instantiates a consumer-authored rig under the local gameplay camera.</summary>
        DedicatedLocalViewmodel,

        /// <summary>Plays an authored controller on a player's Lethal Company body animator.</summary>
        BodyWorld
    }

    /// <summary>Controls how a start request handles an occupied presentation resource.</summary>
    public enum InteractionAnimationConflictPolicy
    {
        /// <summary>Rejects the new request without changing the running interaction.</summary>
        RejectIfBusy,

        /// <summary>Replaces conflicting interactions after the new request passes preflight.</summary>
        InterruptExisting
    }

    /// <summary>Describes why an interaction session ended.</summary>
    public enum InteractionAnimationStopReason
    {
        /// <summary>A consumer explicitly stopped the session.</summary>
        Requested,

        /// <summary>The manifest duration elapsed.</summary>
        NaturalEnd,

        /// <summary>A preflighted request replaced the session.</summary>
        Interrupted,

        /// <summary>The target player was destroyed, disconnected, or otherwise became invalid.</summary>
        PlayerInvalidated,

        /// <summary>The target player died.</summary>
        PlayerDied,

        /// <summary>The round that owned the target player unloaded.</summary>
        RoundUnloaded,

        /// <summary>The presenter failed or lost the resource it owned.</summary>
        PresenterFailure,

        /// <summary>The plugin is shutting down.</summary>
        Shutdown
    }

    /// <summary>An opaque identifier for one interaction session.</summary>
    public readonly struct InteractionAnimationHandle : IEquatable<InteractionAnimationHandle>
    {
        private readonly Guid value;

        private InteractionAnimationHandle(Guid value)
        {
            this.value = value;
        }

        /// <summary>Gets the invalid handle value.</summary>
        public static InteractionAnimationHandle Empty =>
            new InteractionAnimationHandle(Guid.Empty);

        internal static InteractionAnimationHandle NewHandle()
        {
            return new InteractionAnimationHandle(Guid.NewGuid());
        }

        /// <summary>Gets whether this value identifies a session.</summary>
        public bool IsValid => value != Guid.Empty;

        /// <inheritdoc />
        public bool Equals(InteractionAnimationHandle other)
        {
            return value.Equals(other.value);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return obj is InteractionAnimationHandle other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return value.GetHashCode();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return value.ToString();
        }

        /// <summary>Compares two handles for equality.</summary>
        public static bool operator ==(
            InteractionAnimationHandle left,
            InteractionAnimationHandle right)
        {
            return left.Equals(right);
        }

        /// <summary>Compares two handles for inequality.</summary>
        public static bool operator !=(
            InteractionAnimationHandle left,
            InteractionAnimationHandle right)
        {
            return !left.Equals(right);
        }
    }

    /// <summary>Describes a versioned set of consumer-owned interactions.</summary>
    public sealed class InteractionAnimationPackDefinition
    {
        /// <summary>Gets or sets the stable pack identifier and session-owner identity.</summary>
        public string PackId { get; set; } = string.Empty;

        /// <summary>Gets or sets the pack version.</summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>Gets or sets the directory that confines every relative bundle path.</summary>
        public string AssetRootPath { get; set; } = string.Empty;

        /// <summary>Gets or sets the interactions registered by the pack.</summary>
        public InteractionAnimationDefinition[] Interactions { get; set; } =
            Array.Empty<InteractionAnimationDefinition>();
    }

    /// <summary>Associates an interaction id and presentation kind with a manifest.</summary>
    public sealed class InteractionAnimationDefinition
    {
        /// <summary>Gets or sets the stable interaction identifier within its pack.</summary>
        public string InteractionId { get; set; } = string.Empty;

        /// <summary>Gets or sets the local presentation path.</summary>
        public InteractionAnimationPresentationKind PresentationKind { get; set; } =
            InteractionAnimationPresentationKind.DedicatedLocalViewmodel;

        /// <summary>Gets or sets strict schema-2 JSON, or schema-1 JSON accepted by migration.</summary>
        public string ManifestJson { get; set; } = string.Empty;
    }

    /// <summary>Requests a locally presented interaction for one player.</summary>
    public sealed class InteractionAnimationRequest
    {
        /// <summary>Gets or sets the player presented by this client.</summary>
        public PlayerControllerB Player { get; set; }

        /// <summary>Gets or sets the registered pack id, which is also the owner identity.</summary>
        public string PackId { get; set; } = string.Empty;

        /// <summary>Gets or sets the interaction id within the pack.</summary>
        public string InteractionId { get; set; } = string.Empty;

        /// <summary>Gets or sets the busy-resource policy. The default is rejection.</summary>
        public InteractionAnimationConflictPolicy ConflictPolicy { get; set; } =
            InteractionAnimationConflictPolicy.RejectIfBusy;
    }

    /// <summary>Identifies the severity of one validation issue.</summary>
    public enum InteractionAnimationValidationSeverity
    {
        /// <summary>The input is accepted, but the author should review a migration or risk.</summary>
        Warning,

        /// <summary>The input violates the public contract and is rejected.</summary>
        Error
    }

    /// <summary>Provides a stable code, JSON path, message, and severity for one issue.</summary>
    public sealed class InteractionAnimationValidationIssue
    {
        internal InteractionAnimationValidationIssue(
            string code,
            string jsonPath,
            string message,
            InteractionAnimationValidationSeverity severity)
        {
            Code = code ?? string.Empty;
            JsonPath = jsonPath ?? "$";
            Message = message ?? string.Empty;
            Severity = severity;
        }

        /// <summary>Gets the stable machine-readable issue code.</summary>
        public string Code { get; }

        /// <summary>Gets the JSON-style path to the invalid value.</summary>
        public string JsonPath { get; }

        /// <summary>Gets the human-readable explanation.</summary>
        public string Message { get; }

        /// <summary>Gets whether the issue is a warning or error.</summary>
        public InteractionAnimationValidationSeverity Severity { get; }
    }

    /// <summary>Contains immutable validation results for a pack or manifest.</summary>
    public sealed class InteractionAnimationValidationReport
    {
        private readonly ReadOnlyCollection<InteractionAnimationValidationIssue> issues;

        internal InteractionAnimationValidationReport(
            IList<InteractionAnimationValidationIssue> issues)
        {
            this.issues = new ReadOnlyCollection<InteractionAnimationValidationIssue>(
                new List<InteractionAnimationValidationIssue>(
                    issues ?? Array.Empty<InteractionAnimationValidationIssue>()));
        }

        /// <summary>Gets whether the report contains no errors.</summary>
        public bool IsValid
        {
            get
            {
                for (int i = 0; i < issues.Count; i++)
                {
                    if (issues[i].Severity == InteractionAnimationValidationSeverity.Error)
                        return false;
                }

                return true;
            }
        }

        /// <summary>Gets the immutable ordered validation issues.</summary>
        public IReadOnlyList<InteractionAnimationValidationIssue> Issues => issues;
    }

    /// <summary>Immutable data raised after a session has fully restored its resources.</summary>
    public sealed class InteractionAnimationEndedEventArgs : EventArgs
    {
        internal InteractionAnimationEndedEventArgs(
            InteractionAnimationHandle handle,
            PlayerControllerB player,
            string packId,
            string interactionId,
            InteractionAnimationPresentationKind presentationKind,
            InteractionAnimationStopReason stopReason)
        {
            Handle = handle;
            Player = player;
            PackId = packId ?? string.Empty;
            InteractionId = interactionId ?? string.Empty;
            PresentationKind = presentationKind;
            StopReason = stopReason;
        }

        /// <summary>Gets the ended session handle.</summary>
        public InteractionAnimationHandle Handle { get; }

        /// <summary>Gets the player the session presented.</summary>
        public PlayerControllerB Player { get; }

        /// <summary>Gets the pack id that owned the session.</summary>
        public string PackId { get; }

        /// <summary>Gets the interaction id within the pack.</summary>
        public string InteractionId { get; }

        /// <summary>Gets the session presentation kind.</summary>
        public InteractionAnimationPresentationKind PresentationKind { get; }

        /// <summary>Gets the reason the session ended.</summary>
        public InteractionAnimationStopReason StopReason { get; }
    }

    internal sealed class InteractionAnimationContext
    {
        internal InteractionAnimationContext(
            InteractionAnimationHandle handle,
            InteractionAnimationRequest request,
            InteractionAnimationDefinition definition,
            InteractionAnimationManifest manifest,
            string assetRootPath,
            ManualLogSource logger)
        {
            Handle = handle;
            Request = request;
            Definition = definition;
            Manifest = manifest;
            AssetRootPath = assetRootPath ?? string.Empty;
            Logger = logger;
        }

        internal InteractionAnimationHandle Handle { get; }
        internal InteractionAnimationRequest Request { get; }
        internal InteractionAnimationDefinition Definition { get; }
        internal InteractionAnimationManifest Manifest { get; }
        internal string AssetRootPath { get; }
        internal ManualLogSource Logger { get; }
    }
}
