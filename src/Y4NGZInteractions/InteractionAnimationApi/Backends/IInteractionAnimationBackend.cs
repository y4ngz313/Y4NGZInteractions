namespace Y4NGZInteractions.InteractionAnimationApi.Backends
{
    internal interface IInteractionAnimationBackend
    {
        bool TryStart(InteractionAnimationContext context, out string reason);
        void Tick(float deltaTime);
        void Stop(InteractionAnimationStopReason stopReason);
    }
}
