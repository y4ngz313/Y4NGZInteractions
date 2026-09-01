namespace Y4NGZInteractions.InteractionAnimationApi.Presenters
{
    internal enum CrouchStateResyncAction
    {
        None,
        FireStartCrouching
    }

    /// <summary>
    /// Debounce memory for <see cref="CrouchStateResyncPolicy"/>. Held by the presenter for the
    /// life of one session so a fired trigger is given time to land before the same ejected
    /// state can fire another one.
    /// </summary>
    internal struct CrouchStateResyncDebounce
    {
        internal bool Armed;
        internal int FiredForStateHash;
        internal int FiredAtFrame;
    }

    /// <summary>
    /// Mid-session stance invariant for the swapped Base Layer. The faithful vanilla copy the
    /// live-body controller carries can be pulled out of the crouch cluster
    /// {CrouchDown, CrouchIdle, CrouchWalk} by an AnyState edge (Jumping / FallNoJump /
    /// ShortFallLanding), and the ONLY edge back in is the "startCrouching" TRIGGER — the
    /// "crouching" Bool merely gates the exits. Vanilla fires that trigger from a crouch INPUT
    /// edge only, so an ejection that happens while the crouch key is already held is never
    /// undone: the session stays in the standing cluster and the standing Walk clip keys
    /// CameraContainer to standing height while the first-person arms metarig stays crouched.
    ///
    /// This policy fires the same trigger from observed STATE desync rather than from input, so
    /// it never doubles up with vanilla's own edge.
    /// </summary>
    internal static class CrouchStateResyncPolicy
    {
        /// <summary>
        /// Frames a fired trigger is given to move the layer before the same still-ejected state
        /// is allowed to fire again. Long enough that a CrouchDown transition has started, short
        /// enough that a swallowed trigger is retried within a fraction of a second.
        /// </summary>
        internal const int DefaultMinimumFramesBetweenFires = 30;

        internal static bool IsCrouchClusterState(
            int stateHash,
            int crouchDownHash,
            int crouchIdleHash,
            int crouchWalkHash)
        {
            return stateHash == crouchDownHash ||
                stateHash == crouchIdleHash ||
                stateHash == crouchWalkHash;
        }

        internal static CrouchStateResyncAction Resolve(
            bool sessionActive,
            bool crouchingKnown,
            bool crouching,
            bool baseLayerStateKnown,
            bool insideCrouchCluster,
            bool isInTransition,
            int currentStateHash,
            int currentFrame,
            int minimumFramesBetweenFires,
            ref CrouchStateResyncDebounce debounce)
        {
            // Not crouching (or the stance cannot be read): nothing to re-enter, and the next
            // ejection must be free to fire immediately.
            if (!sessionActive || !crouchingKnown || !crouching)
            {
                debounce = default;
                return CrouchStateResyncAction.None;
            }

            if (!baseLayerStateKnown)
                return CrouchStateResyncAction.None;

            // Already back where it belongs — clear the debounce so a later ejection out of the
            // same state is treated as new.
            if (insideCrouchCluster)
            {
                debounce = default;
                return CrouchStateResyncAction.None;
            }

            // Mid-transition the reported state is not yet settled, and a transition may be the
            // one a just-fired trigger started. Never fire into it, and never clear the debounce.
            if (isInTransition)
                return CrouchStateResyncAction.None;

            if (debounce.Armed &&
                debounce.FiredForStateHash == currentStateHash &&
                currentFrame - debounce.FiredAtFrame < minimumFramesBetweenFires)
            {
                return CrouchStateResyncAction.None;
            }

            debounce.Armed = true;
            debounce.FiredForStateHash = currentStateHash;
            debounce.FiredAtFrame = currentFrame;
            return CrouchStateResyncAction.FireStartCrouching;
        }
    }
}
