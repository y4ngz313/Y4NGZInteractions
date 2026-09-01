namespace Y4NGZInteractions.InteractionAnimationApi.Presenters
{
    /// <summary>
    /// Resolves the manifest movement Int the shell controller's first-person locomotion
    /// transitions read: 0 = idle, 1 = walking, 2 = sprinting.
    ///
    /// Sprint is gated strictly on the sprint input state AND on not being crouched. Vanilla
    /// cannot sprint out of a crouch, but PlayerControllerB.isSprinting stays true while the
    /// sprint key is held, so a crouch-walk would otherwise drive Hold -> Sprint and play the
    /// sprint arms clip at ordinary crouch-walk speed (~2.4 m/s).
    /// </summary>
    internal static class MovementParameterPolicy
    {
        internal const int Idle = 0;
        internal const int Walking = 1;
        internal const int Sprinting = 2;

        /// <summary>Horizontal speed below which the movement Int reads as idle.</summary>
        internal const float MinimumMovingSpeed = 0.2f;

        internal static int Resolve(
            float horizontalSpeed,
            bool sprinting,
            bool crouching,
            float minimumMovingSpeed = MinimumMovingSpeed)
        {
            if (horizontalSpeed <= minimumMovingSpeed)
                return Idle;

            return sprinting && !crouching ? Sprinting : Walking;
        }
    }
}
