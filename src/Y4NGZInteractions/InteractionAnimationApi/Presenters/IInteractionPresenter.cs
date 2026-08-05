namespace Y4NGZInteractions.InteractionAnimationApi.Presenters
{
    internal interface IInteractionPresenter
    {
        bool TryStart(InteractionAnimationContext context, out string reason);
        void Tick(float deltaTime);
        void Stop(InteractionAnimationStopReason stopReason);

        /// <summary>
        /// Begins a graceful exit (e.g. fires the controller's exit trigger so a put-away
        /// animation plays). Returns the seconds the caller should wait before Stop.
        /// </summary>
        float BeginExit();

        /// <summary>
        /// True when the presenter wants the interaction stopped immediately (e.g. the player
        /// entered a vanilla special animation like a ladder or the ship lever, or died).
        /// </summary>
        bool ShouldAutoStop { get; }

        /// <summary>
        /// Generic animator parameter passthrough for consuming mods (e.g. action gestures).
        /// For triggers the value is ignored; for bools nonzero = true; for ints it is cast.
        /// </summary>
        bool TrySetAnimatorParameter(string parameterName, UnityEngine.AnimatorControllerParameterType parameterType, float value);
    }
}
