using System;
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Rendering;

namespace Y4NGZInteractions.InteractionAnimationApi.Presenters
{
    /// <summary>
    /// y4ngz313/Y4NGZUpgrades#806: per-camera visibility of the local player's body-side copy of a
    /// live-body prop (manifest <c>body.prop.localBodyCopy</c>). The first-person prop rides the arms
    /// rig, which follows the camera container rather than the body, so a camera that renders the
    /// local body (a drone camera, a feed) would show it floating above the body's hands.
    /// Each camera decides in <c>beginCameraRendering</c>. The local player's own gameplay camera
    /// never draws the copy and keeps the first-person prop, even when another mod makes it render
    /// the local body. Every other camera that renders the local body draws the copy on the body's
    /// bone and hides the first-person prop; elsewhere the copy is hidden and the renderers this
    /// owner hid on the first-person prop are shown again. Renderers a consumer parents under
    /// either instance later follow the same rule. The camera hook exists only while a copy exists.
    /// </summary>
    internal sealed class LocalBodyPropCopy
    {
        private readonly List<Renderer> bodyCopyRenderers = new List<Renderer>();
        private readonly List<Renderer> firstPersonRenderers = new List<Renderer>();
        private readonly List<Renderer> hiddenFirstPersonRenderers = new List<Renderer>();
        private readonly Action<ScriptableRenderContext, Camera> beginCameraRenderingHandler;
        private GameObject bodyCopy;
        private GameObject firstPersonProp;
        private Renderer localBodyModel;
        private Camera ownViewCamera;
        private ManualLogSource logger;
        private string handle = "<none>";
        private int bodyCopyHierarchyCount = -1;
        private int firstPersonHierarchyCount = -1;
        private int bodyCopyChildCount = -1;
        private int firstPersonChildCount = -1;
        private bool subscribed;
        private bool cameraFailureLogged;

        internal LocalBodyPropCopy()
        {
            beginCameraRenderingHandler = OnBeginCameraRendering;
        }

        /// <summary>
        /// Whether a camera with <paramref name="cullingMask"/> draws the local body: the body
        /// renderer is enabled, active, not forced off, not shadows-only (vanilla's first-person
        /// hiding), and on a layer the camera draws.
        /// </summary>
        internal static bool IsBodyRenderedForCamera(
            bool enabled,
            bool activeInHierarchy,
            bool forceRenderingOff,
            bool shadowsOnly,
            int cullingMask,
            int layer)
        {
            return enabled &&
                   activeInHierarchy &&
                   !forceRenderingOff &&
                   !shadowsOnly &&
                   layer >= 0 &&
                   layer < 32 &&
                   (cullingMask & (1 << layer)) != 0;
        }

        /// <summary>
        /// Whether a camera draws the body copy (and hides the first-person prop): never the local
        /// player's own gameplay camera, whatever it renders; any other camera that renders the
        /// local body.
        /// </summary>
        internal static bool ShouldDrawBodyCopy(bool isOwnViewCamera, bool bodyRenderedForCamera)
        {
            return !isOwnViewCamera && bodyRenderedForCamera;
        }

        /// <summary>
        /// Makes a freshly instantiated copy visual only: physics components are removed at once
        /// (no frame in which the copy can be hit or pushed), lights and audio sources disabled.
        /// </summary>
        internal static void StripToVisual(GameObject copy)
        {
            if (copy == null)
                return;

            // Joints first: Unity refuses to remove a Rigidbody a Joint still depends on.
            Joint[] joints = copy.GetComponentsInChildren<Joint>(true);
            for (int i = 0; i < joints.Length; i++)
                UnityEngine.Object.DestroyImmediate(joints[i]);

            Rigidbody[] rigidbodies = copy.GetComponentsInChildren<Rigidbody>(true);
            for (int i = 0; i < rigidbodies.Length; i++)
                UnityEngine.Object.DestroyImmediate(rigidbodies[i]);

            Collider[] colliders = copy.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
                UnityEngine.Object.DestroyImmediate(colliders[i]);

            Light[] lights = copy.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
                lights[i].enabled = false;

            AudioSource[] audioSources = copy.GetComponentsInChildren<AudioSource>(true);
            for (int i = 0; i < audioSources.Length; i++)
                audioSources[i].enabled = false;
        }

        /// <summary>
        /// Takes ownership of <paramref name="copy"/> and starts the per-camera visibility hook.
        /// <paramref name="ownView"/> is the local player's gameplay camera (may be null), which
        /// never draws the copy. A copy still held from an earlier call is destroyed first.
        /// </summary>
        internal void Begin(
            GameObject copy,
            GameObject firstPersonInstance,
            Renderer localBody,
            Camera ownView,
            ManualLogSource log,
            string sessionHandle)
        {
            if (!ReferenceEquals(bodyCopy, null) && !ReferenceEquals(bodyCopy, copy))
            {
                RestoreHiddenFirstPersonRenderers();
                Destroy();
            }

            bodyCopy = copy;
            firstPersonProp = firstPersonInstance;
            localBodyModel = localBody;
            ownViewCamera = ownView;
            logger = log;
            handle = sessionHandle ?? "<none>";
            bodyCopyHierarchyCount = -1;
            bodyCopyChildCount = -1;
            firstPersonHierarchyCount = -1;
            firstPersonChildCount = -1;
            cameraFailureLogged = false;
            if (!subscribed)
            {
                RenderPipelineManager.beginCameraRendering += beginCameraRenderingHandler;
                subscribed = true;
            }
        }

        /// <summary>
        /// Stops the camera hook and destroys the copy. Nothing on the first-person prop is
        /// restored: its owner destroys it in the same call. Returns whether a copy existed.
        /// </summary>
        internal bool Destroy()
        {
            GameObject copy = bodyCopy;
            bool existed = !ReferenceEquals(copy, null);
            ResetState();
            if (copy != null)
            {
                try { UnityEngine.Object.Destroy(copy); } catch { }
            }
            return existed;
        }

        private void OnBeginCameraRendering(ScriptableRenderContext renderContext, Camera camera)
        {
            try
            {
                if (bodyCopy == null)
                {
                    // The copy went without DestroyProp (scene teardown or a consumer): give the
                    // first-person prop back what this owner hid and stop listening.
                    RestoreHiddenFirstPersonRenderers();
                    ResetState();
                    return;
                }

                // The own gameplay camera skips the body test: a mod that renders the local body
                // through it still sees the first-person prop, never the copy.
                bool isOwnViewCamera =
                    !ReferenceEquals(camera, null) && ReferenceEquals(camera, ownViewCamera);
                bool drawCopy = ShouldDrawBodyCopy(
                    isOwnViewCamera,
                    !isOwnViewCamera && IsLocalBodyRendered(camera));
                ApplyToBodyCopy(drawCopy);
                if (drawCopy)
                    HideDrawnFirstPersonRenderers();
                else
                    RestoreHiddenFirstPersonRenderers();
            }
            catch (Exception exception)
            {
                if (!cameraFailureLogged)
                {
                    cameraFailureLogged = true;
                    logger?.LogWarning(
                        "[LCInteractionAnimationAPI] live_body.prop_body_copy_camera_failed: " +
                        $"handle={handle} camera='{(camera != null ? camera.name : "<null>")}' " +
                        $"error='{exception.Message}'.");
                }
            }
        }

        private bool IsLocalBodyRendered(Camera camera)
        {
            Renderer model = localBodyModel;
            if (camera == null || model == null)
                return false;

            GameObject modelObject = model.gameObject;
            return IsBodyRenderedForCamera(
                model.enabled,
                modelObject.activeInHierarchy,
                model.forceRenderingOff,
                model.shadowCastingMode == ShadowCastingMode.ShadowsOnly,
                camera.cullingMask,
                modelObject.layer);
        }

        private void ApplyToBodyCopy(bool drawCopy)
        {
            RefreshRenderersIfStale(
                bodyCopy,
                bodyCopyRenderers,
                ref bodyCopyHierarchyCount,
                ref bodyCopyChildCount);
            bool forceOff = !drawCopy;
            int bodyLayer = drawCopy ? localBodyModel.gameObject.layer : 0;
            for (int i = 0; i < bodyCopyRenderers.Count; i++)
            {
                Renderer renderer = bodyCopyRenderers[i];
                if (renderer == null)
                    continue;

                if (renderer.forceRenderingOff != forceOff)
                    renderer.forceRenderingOff = forceOff;
                if (drawCopy)
                {
                    GameObject rendererObject = renderer.gameObject;
                    if (rendererObject.layer != bodyLayer)
                        rendererObject.layer = bodyLayer;
                }
            }
        }

        private void HideDrawnFirstPersonRenderers()
        {
            if (firstPersonProp == null)
                return;

            RefreshRenderersIfStale(
                firstPersonProp,
                firstPersonRenderers,
                ref firstPersonHierarchyCount,
                ref firstPersonChildCount);
            for (int i = 0; i < firstPersonRenderers.Count; i++)
            {
                Renderer renderer = firstPersonRenderers[i];
                if (renderer == null || renderer.forceRenderingOff)
                    continue;

                renderer.forceRenderingOff = true;
                if (!hiddenFirstPersonRenderers.Contains(renderer))
                    hiddenFirstPersonRenderers.Add(renderer);
            }
        }

        private void RestoreHiddenFirstPersonRenderers()
        {
            for (int i = 0; i < hiddenFirstPersonRenderers.Count; i++)
            {
                Renderer renderer = hiddenFirstPersonRenderers[i];
                if (renderer != null && renderer.forceRenderingOff)
                    renderer.forceRenderingOff = false;
            }
            hiddenFirstPersonRenderers.Clear();
        }

        // Transform.hierarchyCount counts the whole transform hierarchy the instance lives in, so a
        // transform added anywhere in that root changes it; it misses an add under the instance paired
        // with a remove elsewhere in the same root between two callbacks. The instance root's
        // childCount catches that pair for consumers that parent renderers directly under the
        // instance. A destroyed renderer is caught by the Unity-null scan. None allocates; the
        // re-collect refills the reused list.
        private static void RefreshRenderersIfStale(
            GameObject root,
            List<Renderer> renderers,
            ref int cachedHierarchyCount,
            ref int cachedChildCount)
        {
            Transform rootTransform = root.transform;
            int hierarchyCount = rootTransform.hierarchyCount;
            int childCount = rootTransform.childCount;
            bool stale = hierarchyCount != cachedHierarchyCount || childCount != cachedChildCount;
            for (int i = 0; !stale && i < renderers.Count; i++)
                stale = renderers[i] == null;
            if (!stale)
                return;

            root.GetComponentsInChildren(true, renderers);
            cachedHierarchyCount = hierarchyCount;
            cachedChildCount = childCount;
        }

        private void ResetState()
        {
            if (subscribed)
            {
                try { RenderPipelineManager.beginCameraRendering -= beginCameraRenderingHandler; }
                catch { }
                subscribed = false;
            }

            bodyCopy = null;
            firstPersonProp = null;
            localBodyModel = null;
            ownViewCamera = null;
            logger = null;
            handle = "<none>";
            bodyCopyRenderers.Clear();
            firstPersonRenderers.Clear();
            hiddenFirstPersonRenderers.Clear();
            bodyCopyHierarchyCount = -1;
            bodyCopyChildCount = -1;
            firstPersonHierarchyCount = -1;
            firstPersonChildCount = -1;
            cameraFailureLogged = false;
        }
    }
}
