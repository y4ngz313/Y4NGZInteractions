using System;
using BepInEx.Logging;
using GameNetcodeStuff;
using Y4NGZInteractions.InteractionAnimationApi.Authoring;

namespace Y4NGZInteractions.InteractionAnimationApi
{
    public enum InteractionAnimationPresentationKind
    {
        DedicatedLocalViewmodel,
        BodyWorld,
        Hybrid
    }

    public enum InteractionAnimationStopReason
    {
        Requested,
        NaturalEnd,
        Interrupted,
        Failed,
        Shutdown
    }

    public readonly struct InteractionAnimationHandle : IEquatable<InteractionAnimationHandle>
    {
        private readonly Guid value;

        private InteractionAnimationHandle(Guid value)
        {
            this.value = value;
        }

        public static InteractionAnimationHandle Empty => new InteractionAnimationHandle(Guid.Empty);

        public static InteractionAnimationHandle NewHandle()
        {
            return new InteractionAnimationHandle(Guid.NewGuid());
        }

        public bool IsValid => value != Guid.Empty;

        public bool Equals(InteractionAnimationHandle other)
        {
            return value.Equals(other.value);
        }

        public override bool Equals(object obj)
        {
            return obj is InteractionAnimationHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            return value.GetHashCode();
        }

        public override string ToString()
        {
            return value.ToString();
        }

        public static bool operator ==(InteractionAnimationHandle left, InteractionAnimationHandle right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(InteractionAnimationHandle left, InteractionAnimationHandle right)
        {
            return !left.Equals(right);
        }
    }

    public sealed class InteractionAnimationPackDefinition
    {
        public string PackId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        /// <summary>
        /// Optional consumer-owned directory used to resolve relative bundle names in this
        /// pack's manifests. When supplied, bundle paths are confined to this directory.
        /// </summary>
        public string AssetRootPath { get; set; } = string.Empty;
        public InteractionAnimationDefinition[] Interactions { get; set; } =
            Array.Empty<InteractionAnimationDefinition>();
    }

    public sealed class InteractionAnimationDefinition
    {
        public string InteractionId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public float ExpectedDurationSeconds { get; set; }
        public InteractionAnimationPresentationKind PresentationKind { get; set; } =
            InteractionAnimationPresentationKind.DedicatedLocalViewmodel;
        public string ManifestJson { get; set; } = string.Empty;
    }

    public sealed class InteractionAnimationRequest
    {
        public PlayerControllerB Player { get; set; }
        public string PackId { get; set; } = string.Empty;
        public string InteractionId { get; set; } = string.Empty;
        public string OwnerModId { get; set; } = string.Empty;
    }

    public sealed class InteractionAnimationValidationResult
    {
        private InteractionAnimationValidationResult(bool isValid, string reason)
        {
            IsValid = isValid;
            Reason = reason ?? string.Empty;
        }

        public bool IsValid { get; }
        public string Reason { get; }

        public static InteractionAnimationValidationResult Valid()
        {
            return new InteractionAnimationValidationResult(true, string.Empty);
        }

        public static InteractionAnimationValidationResult Invalid(string reason)
        {
            return new InteractionAnimationValidationResult(false, reason);
        }
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
