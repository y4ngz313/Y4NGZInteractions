using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Y4NGZInteractions.InteractionAnimationApi
{
    internal enum InteractionAnimationResourceKind
    {
        BodyAnimator,
        LocalCameraAndArms
    }

    internal readonly struct InteractionAnimationResourceClaim :
        IEquatable<InteractionAnimationResourceClaim>
    {
        internal InteractionAnimationResourceClaim(
            InteractionAnimationResourceKind kind,
            object owner)
        {
            Kind = kind;
            Owner = owner;
        }

        internal InteractionAnimationResourceKind Kind { get; }
        internal object Owner { get; }

        public bool Equals(InteractionAnimationResourceClaim other)
        {
            return Kind == other.Kind && ReferenceEquals(Owner, other.Owner);
        }

        public override bool Equals(object obj)
        {
            return obj is InteractionAnimationResourceClaim other && Equals(other);
        }

        public override int GetHashCode()
        {
            return ((int)Kind * 397) ^ (Owner == null ? 0 : RuntimeHelpers.GetHashCode(Owner));
        }
    }

    internal sealed class InteractionAnimationResourceLeaseRegistry
    {
        private readonly Dictionary<InteractionAnimationResourceClaim, InteractionAnimationHandle>
            owners = new Dictionary<InteractionAnimationResourceClaim, InteractionAnimationHandle>();
        private readonly Dictionary<InteractionAnimationHandle, InteractionAnimationResourceClaim[]>
            claimsByHandle =
                new Dictionary<InteractionAnimationHandle, InteractionAnimationResourceClaim[]>();

        internal bool TryPlanAcquisition(
            InteractionAnimationResourceClaim[] claims,
            InteractionAnimationConflictPolicy policy,
            out InteractionAnimationHandle[] conflicts,
            out string reason)
        {
            reason = string.Empty;
            var found = new HashSet<InteractionAnimationHandle>();
            claims = claims ?? Array.Empty<InteractionAnimationResourceClaim>();
            for (int i = 0; i < claims.Length; i++)
            {
                if (claims[i].Owner == null)
                {
                    conflicts = Array.Empty<InteractionAnimationHandle>();
                    reason = "interaction_resource_owner_missing";
                    return false;
                }
                if (owners.TryGetValue(claims[i], out InteractionAnimationHandle owner))
                    found.Add(owner);
            }

            conflicts = new InteractionAnimationHandle[found.Count];
            found.CopyTo(conflicts);
            if (conflicts.Length > 0 &&
                policy == InteractionAnimationConflictPolicy.RejectIfBusy)
            {
                reason = "interaction_resource_busy";
                return false;
            }
            return true;
        }

        internal bool TryAcquire(
            InteractionAnimationHandle handle,
            InteractionAnimationResourceClaim[] claims,
            out string reason)
        {
            reason = string.Empty;
            if (!handle.IsValid || claimsByHandle.ContainsKey(handle))
            {
                reason = "interaction_lease_handle_invalid";
                return false;
            }

            claims = claims ?? Array.Empty<InteractionAnimationResourceClaim>();
            for (int i = 0; i < claims.Length; i++)
            {
                if (claims[i].Owner == null || owners.ContainsKey(claims[i]))
                {
                    reason = "interaction_resource_busy";
                    return false;
                }
            }

            var snapshot = new InteractionAnimationResourceClaim[claims.Length];
            Array.Copy(claims, snapshot, claims.Length);
            claimsByHandle.Add(handle, snapshot);
            for (int i = 0; i < snapshot.Length; i++)
                owners.Add(snapshot[i], handle);
            return true;
        }

        internal void Release(InteractionAnimationHandle handle)
        {
            if (!claimsByHandle.TryGetValue(handle, out InteractionAnimationResourceClaim[] claims))
                return;
            claimsByHandle.Remove(handle);
            for (int i = 0; i < claims.Length; i++)
            {
                if (owners.TryGetValue(claims[i], out InteractionAnimationHandle owner) &&
                    owner == handle)
                    owners.Remove(claims[i]);
            }
        }

        internal bool IsOwned(
            InteractionAnimationResourceKind kind,
            object owner,
            out InteractionAnimationHandle handle)
        {
            return owners.TryGetValue(
                new InteractionAnimationResourceClaim(kind, owner),
                out handle);
        }

        internal void Clear()
        {
            owners.Clear();
            claimsByHandle.Clear();
        }
    }
}
