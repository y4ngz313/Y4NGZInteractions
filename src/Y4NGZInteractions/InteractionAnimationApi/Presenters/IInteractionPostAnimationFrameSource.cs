namespace Y4NGZInteractions.InteractionAnimationApi.Presenters
{
    /// <summary>
    /// A presenter that reports the values an <see cref="InteractionAnimationFrame"/> carries.
    /// </summary>
    internal interface IInteractionPostAnimationFrameSource
    {
        // Who sees this session's body this frame.
        InteractionAnimationViewer Viewer { get; }

        // The session's eased base weight, 0 to 1, before viewer and yield factors.
        float SessionWeight { get; }
    }
}
