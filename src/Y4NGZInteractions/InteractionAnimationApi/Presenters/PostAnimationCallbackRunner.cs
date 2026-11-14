using UnityEngine;

namespace Y4NGZInteractions.InteractionAnimationApi.Presenters
{
    /// <summary>
    /// Runs the post-animation callbacks of active sessions once per frame. Order 31000 runs
    /// after the default-order LateUpdates (vanilla, the runtime host's session tick) and before
    /// the order-32000 camera stabilizers and restore diagnostics. Enabled only while a session
    /// has a callback.
    /// </summary>
    [DefaultExecutionOrder(31000)]
    internal sealed class PostAnimationCallbackRunner : MonoBehaviour
    {
        private static Behaviour instance;
        private static bool running;

        internal static void Attach(Behaviour runner)
        {
            instance = runner;
            Apply();
        }

        internal static void Detach()
        {
            running = false;
            Apply();
            instance = null;
        }

        // The coordinator's runner switch.
        internal static void SetRunning(bool value)
        {
            running = value;
            Apply();
        }

        private static void Apply()
        {
            if (instance != null)
                instance.enabled = running;
        }

        private void LateUpdate()
        {
            LCInteractionAnimationAPI.RunPostAnimationCallbacks(Time.deltaTime);
        }
    }
}
