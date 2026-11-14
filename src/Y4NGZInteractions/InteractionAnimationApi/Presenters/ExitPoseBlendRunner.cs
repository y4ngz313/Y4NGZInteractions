using UnityEngine;

namespace Y4NGZInteractions.InteractionAnimationApi.Presenters
{
    /// <summary>
    /// Steps every live exit pose blend once per frame. Order -31000 runs after the animator has
    /// written the vanilla pose and after <see cref="MetarigRootWriteBackOwnerRunner"/> (-32000),
    /// and before every default-order LateUpdate, so vanilla, held items, Harmony postfixes and
    /// post-animation callbacks read the blended pose. Enabled only while a blend is live.
    /// </summary>
    [DefaultExecutionOrder(-31000)]
    internal sealed class ExitPoseBlendRunner : MonoBehaviour
    {
        private void LateUpdate()
        {
            ExitPoseBlend.StepAll(Time.frameCount, Time.deltaTime);
        }
    }
}
