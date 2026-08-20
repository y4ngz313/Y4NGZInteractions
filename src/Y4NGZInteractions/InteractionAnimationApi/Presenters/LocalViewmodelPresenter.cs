using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using UnityEngine;
using Y4NGZInteractions.InteractionAnimationApi.Authoring;
using Object = UnityEngine.Object;

namespace Y4NGZInteractions.InteractionAnimationApi.Presenters
{
    internal sealed class LocalViewmodelPresenter : IInteractionPresenter
    {
        private const long SynchronousLoadSizeLimitBytes = 16L * 1024L * 1024L;

        // ModelReplacementAPI is a soft, optional dependency. It is never referenced at compile
        // time and never required at runtime; these names are the whole contract with it.
        private const string ModelReplacementApiGuid = "meow.ModelReplacementAPI";
        private const string BodyReplacementBaseTypeName = "ModelReplacement.BodyReplacementBase";
        private const string ReplacementViewModelFieldName = "replacementViewModel";

        private static readonly HashSet<string> PreloadingBundlePaths =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, AssetBundle> PreloadedBundles =
            new Dictionary<string, AssetBundle>(StringComparer.OrdinalIgnoreCase);

        private readonly List<RendererState> hiddenRenderers = new List<RendererState>();
        private InteractionAnimationContext context;
        private AssetBundle viewmodelBundle;
        private bool ownsViewmodelBundle;
        private GameObject viewmodelRoot;
        private RuntimeAnimatorController viewmodelController;
        private Animator viewmodelAnimator;
        private bool active;
        private bool exitRequested;

        public InteractionAnimationStopReason? RequestedStopReason => null;

        public bool HasResourceOwnership => active && viewmodelRoot != null;


        internal static bool TryBeginPreload(
            InteractionAnimationManifest manifest,
            string assetRootPath,
            ManualLogSource logger,
            out string reason)
        {
            reason = string.Empty;
            if (manifest == null || manifest.localViewmodel == null)
            {
                reason = "missing_viewmodel_manifest";
                return false;
            }

            if (!TryResolveBundlePath(
                    manifest.localViewmodel.bundleFileName,
                    assetRootPath,
                    out string bundlePath,
                    out reason))
            {
                logger?.LogWarning(
                    "[LCInteractionAnimationAPI] viewmodel.bundle_preload_rejected: " +
                    $"file='{manifest.localViewmodel.bundleFileName}' reason='{reason}'.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(bundlePath) || !File.Exists(bundlePath))
            {
                reason = "viewmodel.bundle_preload_missing:" + manifest.localViewmodel.bundleFileName;
                logger?.LogWarning(
                    "[LCInteractionAnimationAPI] viewmodel.bundle_preload_missing: " +
                    $"file='{manifest.localViewmodel.bundleFileName}' resolvedPath='{bundlePath}'.");
                return false;
            }

            string bundleInternalName = manifest.bundleInternalName ?? string.Empty;
            if (FindLoadedBundle(bundleInternalName, bundlePath) != null)
                return true;

            if (PreloadingBundlePaths.Contains(bundlePath))
                return true;

            if (Y4NGZInteractions.Plugin.Instance == null)
            {
                logger?.LogWarning(
                    "[LCInteractionAnimationAPI] viewmodel.bundle_preload_unavailable: plugin instance missing.");
                reason = "viewmodel.bundle_preload_unavailable";
                return false;
            }

            PreloadingBundlePaths.Add(bundlePath);
            logger?.LogInfo(
                "[LCInteractionAnimationAPI] viewmodel.bundle_preload_started: " +
                $"path='{bundlePath}' fileBytes={new FileInfo(bundlePath).Length}.");
            Y4NGZInteractions.Plugin.Instance.StartCoroutine(
                PreloadBundleCoroutine(bundlePath, bundleInternalName, logger));
            return true;
        }

        public bool TryPreflight(InteractionAnimationContext context, out string reason)
        {
            reason = string.Empty;
            if (context?.Manifest?.localViewmodel == null)
            {
                reason = "missing_viewmodel_manifest";
                return false;
            }

            this.context = context;
            InteractionAnimationManifest.LocalViewmodelManifest viewmodel =
                context.Manifest.localViewmodel;
            if (!TryLoadViewmodelBundle(context.Manifest, out reason))
                return false;
            GameObject prefab = viewmodelBundle.LoadAsset<GameObject>(
                viewmodel.prefabAssetName);
            if (prefab == null)
            {
                reason = "viewmodel.prefab_missing:" + viewmodel.prefabAssetName;
                CleanupFailedStart();
                return false;
            }
            if (viewmodelBundle.LoadAsset<RuntimeAnimatorController>(
                    viewmodel.controllerAssetName) == null)
            {
                reason = "viewmodel.controller_missing:" + viewmodel.controllerAssetName;
                CleanupFailedStart();
                return false;
            }
            Camera camera;
            if (ResolveCameraParent(out camera) == null)
            {
                reason = "viewmodel.camera_missing";
                CleanupFailedStart();
                return false;
            }
            if (prefab.transform.Find(viewmodel.cameraAnchorPath) == null)
            {
                reason = "viewmodel.camera_anchor_missing:" + viewmodel.cameraAnchorPath;
                CleanupFailedStart();
                return false;
            }
            return true;
        }
        public bool TryStart(InteractionAnimationContext context, out string reason)
        {
            reason = string.Empty;

            if (context == null)
            {
                reason = "missing_context";
                return false;
            }

            InteractionAnimationManifest manifest = context.Manifest;
            if (manifest == null || manifest.localViewmodel == null)
            {
                reason = "missing_viewmodel_manifest";
                return false;
            }

            this.context = context;

            if (viewmodelBundle == null && !TryLoadViewmodelBundle(manifest, out reason))
                return false;

            if (!TryInstantiateViewmodel(manifest, out reason))
            {
                CleanupFailedStart();
                return false;
            }

            HideLiveFirstPersonRenderers();
            HideModelReplacementViewmodelRenderers();
            active = true;
            exitRequested = false;
            SetBoolIfExists(manifest.localViewmodel.activeBool, true);
            FireTriggerIfExists(manifest.localViewmodel.enterTrigger);
            viewmodelAnimator.Update(0f);
            EvaluateViewmodelRigBuilders();
            context.Logger?.LogInfo(
                "[LCInteractionAnimationAPI] local_viewmodel.started: " +
                $"handle={context.Handle} interaction='{manifest.interactionId}' " +
                $"prefab='{manifest.localViewmodel.prefabAssetName}' controller='{manifest.localViewmodel.controllerAssetName}' " +
                $"cameraAnchorPath='{manifest.localViewmodel.cameraAnchorPath}'.");
            return true;
        }

        public void Tick(float deltaTime)
        {
            if (!active || context == null)
                return;
        }

        public float BeginExit()
        {
            InteractionAnimationManifest.LocalViewmodelManifest viewmodel =
                context?.Manifest?.localViewmodel;
            if (!active || viewmodelAnimator == null || viewmodel == null || exitRequested)
                return 0f;

            exitRequested = true;
            SetBoolIfExists(viewmodel.activeBool, false);
            FireTriggerIfExists(viewmodel.exitTrigger);
            float exitSeconds = Mathf.Max(0f, viewmodel.exitSeconds);
            context.Logger?.LogInfo(
                "[LCInteractionAnimationAPI] local_viewmodel.exit_started: " +
                $"handle={context.Handle} exitSeconds={exitSeconds:0.###}.");
            return exitSeconds;
        }

        public bool TrySetAnimatorParameter(string parameterName, UnityEngine.AnimatorControllerParameterType parameterType, float value)
        {
            if (!active || viewmodelAnimator == null || string.IsNullOrWhiteSpace(parameterName) ||
                !HasParameter(parameterName, parameterType))
            {
                return false;
            }

            try
            {
                switch (parameterType)
                {
                    case AnimatorControllerParameterType.Bool:
                        viewmodelAnimator.SetBool(parameterName, value != 0f);
                        return true;
                    case AnimatorControllerParameterType.Int:
                        viewmodelAnimator.SetInteger(parameterName, (int)value);
                        return true;
                    case AnimatorControllerParameterType.Float:
                        viewmodelAnimator.SetFloat(parameterName, value);
                        return true;
                    case AnimatorControllerParameterType.Trigger:
                        viewmodelAnimator.ResetTrigger(parameterName);
                        viewmodelAnimator.SetTrigger(parameterName);
                        return true;
                    default:
                        return false;
                }
            }
            catch
            {
                return false;
            }
        }

        public void Stop(InteractionAnimationStopReason stopReason)
        {
            if (!active && viewmodelRoot == null && viewmodelBundle == null)
                return;

            RestoreLiveFirstPersonRenderers();
            DestroyViewmodel();
            ReleaseViewmodelBundle();
            context?.Logger?.LogInfo(
                "[LCInteractionAnimationAPI] local_viewmodel.stopped: " +
                $"handle={context.Handle} reason='{stopReason}' restoredRenderers={hiddenRenderers.Count}.");
            hiddenRenderers.Clear();
            active = false;
            exitRequested = false;
            context = null;
        }

        private void SetBoolIfExists(string parameterName, bool value)
        {
            if (string.IsNullOrWhiteSpace(parameterName) ||
                !HasParameter(parameterName, AnimatorControllerParameterType.Bool))
            {
                return;
            }

            try { viewmodelAnimator.SetBool(parameterName, value); } catch { }
        }

        private void FireTriggerIfExists(string parameterName)
        {
            if (string.IsNullOrWhiteSpace(parameterName) ||
                !HasParameter(parameterName, AnimatorControllerParameterType.Trigger))
            {
                return;
            }

            try
            {
                viewmodelAnimator.ResetTrigger(parameterName);
                viewmodelAnimator.SetTrigger(parameterName);
            }
            catch { }
        }

        private bool HasParameter(
            string parameterName,
            AnimatorControllerParameterType parameterType)
        {
            if (viewmodelAnimator == null)
                return false;

            try
            {
                AnimatorControllerParameter[] parameters = viewmodelAnimator.parameters;
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (parameters[i].type == parameterType &&
                        string.Equals(parameters[i].name, parameterName, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        // Swapping the controller / Rebind destroys the Animation Rigging playable graph, and the
        // authored viewmodel clips drive IK targets that only move arm bones through that graph.
        // RigBuilder.Build() must run after every Rebind; Evaluate() forces a solve for the frame.
        private void RebuildViewmodelRigBuilders()
        {
            if (viewmodelRoot == null)
                return;

            int rebuilt = 0;
            Behaviour[] behaviours = viewmodelRoot.GetComponentsInChildren<Behaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                Behaviour behaviour = behaviours[i];
                if (behaviour == null || !IsRigBuilderComponent(behaviour))
                    continue;

                try
                {
                    System.Reflection.MethodInfo buildMethod =
                        behaviour.GetType().GetMethod("Build", Type.EmptyTypes);
                    if (buildMethod != null)
                    {
                        buildMethod.Invoke(behaviour, null);
                        rebuilt++;
                    }
                }
                catch (Exception exception)
                {
                    context?.Logger?.LogWarning(
                        "[LCInteractionAnimationAPI] local_viewmodel.rig_rebuild_failed: " +
                        $"handle={context?.Handle} rigBuilder='{behaviour.name}' reason='{exception.Message}'.");
                }
            }

            context?.Logger?.LogInfo(
                "[LCInteractionAnimationAPI] local_viewmodel.rig_rebuilt: " +
                $"handle={context?.Handle} rigBuilders={rebuilt}.");
        }

        private void EvaluateViewmodelRigBuilders()
        {
            if (viewmodelRoot == null)
                return;

            Behaviour[] behaviours = viewmodelRoot.GetComponentsInChildren<Behaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                Behaviour behaviour = behaviours[i];
                if (behaviour == null || !behaviour.isActiveAndEnabled || !IsRigBuilderComponent(behaviour))
                    continue;

                try
                {
                    System.Reflection.MethodInfo evaluateMethod =
                        behaviour.GetType().GetMethod(
                            "Evaluate",
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public,
                            null,
                            new[] { typeof(float) },
                            null);
                    evaluateMethod?.Invoke(behaviour, new object[] { 0f });
                }
                catch
                {
                }
            }
        }

        private static bool IsRigBuilderComponent(Component component)
        {
            Type type = component != null ? component.GetType() : null;
            if (type == null)
                return false;

            return string.Equals(type.Name, "RigBuilder", StringComparison.Ordinal) ||
                   string.Equals(
                       type.FullName,
                       "UnityEngine.Animations.Rigging.RigBuilder",
                       StringComparison.Ordinal);
        }

        private bool TryLoadViewmodelBundle(InteractionAnimationManifest manifest, out string reason)
        {
            reason = string.Empty;

            string bundleInternalName = manifest.bundleInternalName ?? string.Empty;
            if (!TryResolveBundlePath(
                    manifest.localViewmodel.bundleFileName,
                    context.AssetRootPath,
                    out string bundlePath,
                    out reason))
            {
                return false;
            }

            viewmodelBundle = FindLoadedBundle(bundleInternalName, bundlePath);
            if (viewmodelBundle != null)
            {
                ownsViewmodelBundle = false;
                return true;
            }

            if (string.IsNullOrWhiteSpace(bundlePath) || !File.Exists(bundlePath))
            {
                reason = "viewmodel.bundle_missing:" + manifest.localViewmodel.bundleFileName;
                context.Logger?.LogWarning(
                    "[LCInteractionAnimationAPI] viewmodel.bundle_missing: " +
                    $"handle={context.Handle} file='{manifest.localViewmodel.bundleFileName}'.");
                return false;
            }

            if (PreloadingBundlePaths.Contains(bundlePath))
            {
                reason = "viewmodel.bundle_preload_in_progress:" + bundlePath;
                context.Logger?.LogWarning(
                    "[LCInteractionAnimationAPI] viewmodel.bundle_preload_in_progress: " +
                    $"handle={context.Handle} path='{bundlePath}'.");
                return false;
            }

            long fileBytes = new FileInfo(bundlePath).Length;
            if (fileBytes > SynchronousLoadSizeLimitBytes)
            {
                TryBeginPreload(manifest, context.AssetRootPath, context.Logger, out _);
                reason = "viewmodel.bundle_preload_started:" + bundlePath;
                return false;
            }

            viewmodelBundle = AssetBundle.LoadFromFile(bundlePath);
            if (viewmodelBundle == null)
            {
                reason = "viewmodel.bundle_load_failed:" + bundlePath;
                return false;
            }

            ownsViewmodelBundle = true;
            context.Logger?.LogInfo(
                "[LCInteractionAnimationAPI] viewmodel.bundle_loaded: " +
                $"handle={context.Handle} path='{bundlePath}'.");
            return true;
        }

        private static IEnumerator PreloadBundleCoroutine(
            string bundlePath,
            string bundleInternalName,
            ManualLogSource logger)
        {
            AssetBundleCreateRequest request = AssetBundle.LoadFromFileAsync(bundlePath);
            yield return request;

            PreloadingBundlePaths.Remove(bundlePath);
            AssetBundle bundle = request.assetBundle;
            if (bundle == null)
            {
                logger?.LogWarning(
                    "[LCInteractionAnimationAPI] viewmodel.bundle_preload_failed: " +
                    $"path='{bundlePath}'.");
                yield break;
            }

            PreloadedBundles[bundlePath] = bundle;
            logger?.LogInfo(
                "[LCInteractionAnimationAPI] viewmodel.bundle_preload_completed: " +
                $"path='{bundlePath}' internalName='{bundle.name}' expectedInternalName='{bundleInternalName}'.");
        }

        private static AssetBundle FindLoadedBundle(string bundleInternalName, string bundlePath)
        {
            if (!string.IsNullOrWhiteSpace(bundlePath) &&
                PreloadedBundles.TryGetValue(bundlePath, out AssetBundle preloadedBundle) &&
                preloadedBundle != null)
            {
                return preloadedBundle;
            }

            foreach (AssetBundle loadedBundle in AssetBundle.GetAllLoadedAssetBundles())
            {
                if (loadedBundle != null &&
                    !string.IsNullOrWhiteSpace(bundleInternalName) &&
                    string.Equals(loadedBundle.name, bundleInternalName, StringComparison.OrdinalIgnoreCase))
                {
                    return loadedBundle;
                }
            }

            return null;
        }

        private bool TryInstantiateViewmodel(InteractionAnimationManifest manifest, out string reason)
        {
            reason = string.Empty;

            GameObject prefab = viewmodelBundle.LoadAsset<GameObject>(manifest.localViewmodel.prefabAssetName);
            if (prefab == null)
            {
                reason = "viewmodel.prefab_missing:" + manifest.localViewmodel.prefabAssetName;
                context.Logger?.LogWarning(
                    "[LCInteractionAnimationAPI] viewmodel.prefab_missing: " +
                    $"handle={context.Handle} prefab='{manifest.localViewmodel.prefabAssetName}'.");
                return false;
            }

            viewmodelController = viewmodelBundle.LoadAsset<RuntimeAnimatorController>(
                manifest.localViewmodel.controllerAssetName);
            if (viewmodelController == null)
            {
                reason = "viewmodel.controller_missing:" + manifest.localViewmodel.controllerAssetName;
                context.Logger?.LogWarning(
                    "[LCInteractionAnimationAPI] viewmodel.controller_missing: " +
                    $"handle={context.Handle} controller='{manifest.localViewmodel.controllerAssetName}'.");
                return false;
            }

            Camera resolvedCamera;
            Transform cameraParent = ResolveCameraParent(out resolvedCamera);
            if (cameraParent == null)
            {
                reason = "viewmodel.camera_missing";
                return false;
            }

            viewmodelRoot = Object.Instantiate(prefab, cameraParent);
            viewmodelRoot.name = "Y4NGZ_Viewmodel_" + SafeObjectName(manifest.interactionId);
            viewmodelRoot.transform.localPosition = Vector3.zero;
            viewmodelRoot.transform.localRotation = Quaternion.identity;
            viewmodelRoot.transform.localScale = manifest.localViewmodel.localScale.ToUnityVector3();

            if (!TryAlignViewmodelToCameraAnchor(manifest, out reason))
                return false;

            viewmodelAnimator = viewmodelRoot.GetComponentInChildren<Animator>(true);
            if (viewmodelAnimator == null)
                viewmodelAnimator = viewmodelRoot.AddComponent<Animator>();

            viewmodelAnimator.runtimeAnimatorController = viewmodelController;
            viewmodelAnimator.applyRootMotion = false;
            viewmodelAnimator.enabled = true;
            viewmodelAnimator.Rebind();
            // Rebind destroyed the Animation Rigging playable graph. The authored viewmodel clips
            // drive IK targets that only move the arm bones through that graph, so rebuild and
            // evaluate the viewmodel's RigBuilder here (mirrors the proven live-body sequence).
            RebuildViewmodelRigBuilders();
            viewmodelAnimator.Update(0f);
            EvaluateViewmodelRigBuilders();

            ApplyViewmodelRendererVisibility(manifest);
            context.Logger?.LogInfo(
                "[LCInteractionAnimationAPI] local_viewmodel.instantiated: " +
                $"handle={context.Handle} prefab='{manifest.localViewmodel.prefabAssetName}' " +
                $"controller='{manifest.localViewmodel.controllerAssetName}' camera='{cameraParent.name}'.");
            return true;
        }

        private Transform ResolveCameraParent(out Camera camera)
        {
            camera = context?.Request?.Player != null
                ? context.Request.Player.gameplayCamera
                : null;
            if (camera != null)
                return camera.transform;

            camera = Camera.main;
            return camera != null ? camera.transform : null;
        }

        private bool TryAlignViewmodelToCameraAnchor(
            InteractionAnimationManifest manifest,
            out string reason)
        {
            reason = string.Empty;
            if (viewmodelRoot == null || manifest?.localViewmodel == null)
            {
                reason = "viewmodel.camera_anchor_context_missing";
                return false;
            }

            string anchorPath = manifest.localViewmodel.cameraAnchorPath ?? string.Empty;
            Transform anchor = viewmodelRoot.transform.Find(anchorPath);
            if (anchor == null)
            {
                reason = "viewmodel.camera_anchor_missing:" + anchorPath;
                context.Logger?.LogWarning(
                    "[LCInteractionAnimationAPI] local_viewmodel.anchor_missing: " +
                    $"handle={context.Handle} anchorPath='{anchorPath}'.");
                return false;
            }

            Vector3 anchorRootLocalPosition = viewmodelRoot.transform.InverseTransformPoint(anchor.position);
            Quaternion anchorRootLocalRotation =
                Quaternion.Inverse(viewmodelRoot.transform.rotation) * anchor.rotation;
            Quaternion targetLocalRotation = Quaternion.Euler(
                manifest.localViewmodel.cameraLocalEuler.ToUnityVector3());
            viewmodelRoot.transform.localRotation =
                targetLocalRotation * Quaternion.Inverse(anchorRootLocalRotation);
            Vector3 anchorOffset = viewmodelRoot.transform.localRotation *
                Vector3.Scale(anchorRootLocalPosition, viewmodelRoot.transform.localScale);
            viewmodelRoot.transform.localPosition =
                manifest.localViewmodel.cameraLocalPosition.ToUnityVector3() - anchorOffset;

            context.Logger?.LogInfo(
                "[LCInteractionAnimationAPI] local_viewmodel.anchor_aligned: " +
                $"handle={context.Handle} anchorPath='{anchorPath}' path='{GetTransformPath(anchor)}' " +
                $"anchorRootLocalPosition={FormatVector(anchorRootLocalPosition)} " +
                $"anchorRootLocalEuler={FormatVector(anchorRootLocalRotation.eulerAngles)} " +
                $"targetLocalPosition={FormatVector(manifest.localViewmodel.cameraLocalPosition)} " +
                $"targetLocalEuler={FormatVector(manifest.localViewmodel.cameraLocalEuler)} " +
                $"rootLocalPosition={FormatVector(viewmodelRoot.transform.localPosition)} " +
                $"rootLocalEuler={FormatVector(viewmodelRoot.transform.localEulerAngles)} " +
                $"rootLocalScale={FormatVector(viewmodelRoot.transform.localScale)}.");
            return true;
        }

        private void LogCameraDiagnostics(
            Camera camera,
            Transform cameraParent,
            InteractionAnimationManifest manifest)
        {
            if (context?.Logger == null || viewmodelRoot == null)
                return;

            Vector3 cameraToRoot = camera != null
                ? viewmodelRoot.transform.position - camera.transform.position
                : Vector3.zero;
            float distanceToRoot = camera != null ? cameraToRoot.magnitude : -1f;
            float forwardDot = camera != null && cameraToRoot.sqrMagnitude > 0.0001f
                ? Vector3.Dot(camera.transform.forward, cameraToRoot.normalized)
                : 0f;
            Vector3 rootViewport = camera != null
                ? camera.WorldToViewportPoint(viewmodelRoot.transform.position)
                : Vector3.zero;
            bool rootLayerVisible = IsLayerInCullingMask(camera, viewmodelRoot.layer);

            context.Logger.LogInfo(
                "[LCInteractionAnimationAPI] local_viewmodel.camera_diagnostics: " +
                $"handle={context.Handle} camera='{SafeName(camera)}' " +
                $"cameraEnabled={(camera != null && camera.enabled)} " +
                $"cameraActive={(camera != null && camera.gameObject.activeInHierarchy)} " +
                $"cameraLayer={(camera != null ? camera.gameObject.layer : -1)} " +
                $"cullingMask={(camera != null ? camera.cullingMask : 0)} " +
                $"nearClip={FormatFloat(camera != null ? camera.nearClipPlane : -1f)} " +
                $"farClip={FormatFloat(camera != null ? camera.farClipPlane : -1f)} " +
                $"fieldOfView={FormatFloat(camera != null ? camera.fieldOfView : -1f)} " +
                $"orthographic={(camera != null && camera.orthographic)} " +
                $"parent='{GetTransformPath(cameraParent)}' root='{GetTransformPath(viewmodelRoot.transform)}' " +
                $"rootActive={viewmodelRoot.activeSelf} rootActiveInHierarchy={viewmodelRoot.activeInHierarchy} " +
                $"rootLayer={viewmodelRoot.layer} rootLayerVisible={rootLayerVisible} " +
                $"rootLocalPosition={FormatVector(viewmodelRoot.transform.localPosition)} " +
                $"manifestCameraAnchor='{manifest.localViewmodel.cameraAnchorPath}' " +
                $"manifestLocalPosition={FormatVector(manifest.localViewmodel.cameraLocalPosition)} " +
                $"rootLocalEuler={FormatVector(viewmodelRoot.transform.localEulerAngles)} " +
                $"manifestLocalEuler={FormatVector(manifest.localViewmodel.cameraLocalEuler)} " +
                $"rootLocalScale={FormatVector(viewmodelRoot.transform.localScale)} " +
                $"rootLossyScale={FormatVector(viewmodelRoot.transform.lossyScale)} " +
                $"rootWorldPosition={FormatVector(viewmodelRoot.transform.position)} " +
                $"cameraWorldPosition={FormatVector(camera != null ? camera.transform.position : Vector3.zero)} " +
                $"distanceToCamera={FormatFloat(distanceToRoot)} forwardDot={FormatFloat(forwardDot)} " +
                $"rootViewport={FormatVector(rootViewport)}.");
        }

        private void BeginPostFrameRendererDiagnostics(Camera camera)
        {
            if (Y4NGZInteractions.Plugin.Instance == null || context == null)
                return;

            Y4NGZInteractions.Plugin.Instance.StartCoroutine(
                PostFrameRendererDiagnosticsCoroutine(context.Handle, camera));
        }

        private IEnumerator PostFrameRendererDiagnosticsCoroutine(
            InteractionAnimationHandle handle,
            Camera camera)
        {
            yield return new WaitForEndOfFrame();

            if (context == null || context.Handle != handle || viewmodelRoot == null)
                yield break;

            LogRendererDiagnostics(
                camera,
                "post_frame",
                "local_viewmodel.post_frame_renderer_diagnostics");
        }

        private void LogRendererDiagnostics(Camera camera)
        {
            LogRendererDiagnostics(camera, "start", "local_viewmodel.renderer_diagnostics");
        }

        private void LogRendererDiagnostics(Camera camera, string phase, string eventName)
        {
            if (context?.Logger == null || viewmodelRoot == null)
                return;

            Renderer[] renderers = viewmodelRoot.GetComponentsInChildren<Renderer>(true);
            Plane[] frustumPlanes = camera != null
                ? GeometryUtility.CalculateFrustumPlanes(camera)
                : null;
            context.Logger.LogInfo(
                "[LCInteractionAnimationAPI] " + eventName + "_summary: " +
                $"handle={context.Handle} phase='{phase}' rendererCount={renderers.Length} camera='{SafeName(camera)}'.");

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                Bounds bounds = renderer.bounds;
                Vector3 cameraToBounds = camera != null
                    ? bounds.center - camera.transform.position
                    : Vector3.zero;
                float distanceToCamera = camera != null ? cameraToBounds.magnitude : -1f;
                float forwardDot = camera != null && cameraToBounds.sqrMagnitude > 0.0001f
                    ? Vector3.Dot(camera.transform.forward, cameraToBounds.normalized)
                    : 0f;
                Vector3 viewportCenter = camera != null
                    ? camera.WorldToViewportPoint(bounds.center)
                    : Vector3.zero;
                bool inCameraFrustum = frustumPlanes != null &&
                    GeometryUtility.TestPlanesAABB(frustumPlanes, bounds);
                bool cameraMaskIncludesLayer = IsLayerInCullingMask(camera, renderer.gameObject.layer);
                SkinnedMeshRenderer skinnedRenderer = renderer as SkinnedMeshRenderer;
                Material[] materials = GetSharedMaterials(renderer);

                context.Logger.LogInfo(
                    "[LCInteractionAnimationAPI] " + eventName + ": " +
                    $"handle={context.Handle} phase='{phase}' index={i} total={renderers.Length} " +
                    $"renderer='{GetTransformPath(renderer.transform)}' name='{renderer.name}' " +
                    $"type='{renderer.GetType().Name}' active={renderer.gameObject.activeSelf} " +
                    $"activeInHierarchy={renderer.gameObject.activeInHierarchy} enabled={renderer.enabled} " +
                    $"isVisible={renderer.isVisible} layer={renderer.gameObject.layer} " +
                    $"cameraMaskIncludesLayer={cameraMaskIncludesLayer} inCameraFrustum={inCameraFrustum} " +
                    $"boundsCenter={FormatVector(bounds.center)} boundsSize={FormatVector(bounds.size)} " +
                    $"distanceToCamera={FormatFloat(distanceToCamera)} forwardDot={FormatFloat(forwardDot)} " +
                    $"viewportCenter={FormatVector(viewportCenter)} " +
                    $"updateWhenOffscreen={(skinnedRenderer != null && skinnedRenderer.updateWhenOffscreen)} " +
                    $"rootBone='{SafeName(skinnedRenderer != null ? skinnedRenderer.rootBone : null)}' " +
                    $"sharedMaterialCount={materials.Length}.");

                LogRendererMaterials(renderer, i, materials);
            }
        }

        private void LogRendererMaterials(Renderer renderer, int rendererIndex, Material[] materials)
        {
            if (context?.Logger == null)
                return;

            if (materials.Length == 0)
            {
                context.Logger.LogInfo(
                    "[LCInteractionAnimationAPI] local_viewmodel.renderer_material: " +
                    $"handle={context.Handle} rendererIndex={rendererIndex} materialIndex=-1 material='<none>'.");
                return;
            }

            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                Shader shader = material != null ? material.shader : null;
                context.Logger.LogInfo(
                    "[LCInteractionAnimationAPI] local_viewmodel.renderer_material: " +
                    $"handle={context.Handle} rendererIndex={rendererIndex} materialIndex={i} " +
                    $"renderer='{GetTransformPath(renderer.transform)}' material='{SafeName(material)}' " +
                    $"shader='{SafeName(shader)}' renderQueue={(material != null ? material.renderQueue : -1)} " +
                    $"color={TryFormatMaterialColor(material)}.");
            }
        }

        private void ApplyViewmodelRendererVisibility(InteractionAnimationManifest manifest)
        {
            if (viewmodelRoot == null || manifest.localViewmodel == null)
                return;

            Renderer[] renderers = viewmodelRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                string rendererPath = GetRelativeTransformPath(
                    viewmodelRoot.transform, renderer.transform);
                if (ShouldHide(manifest.localViewmodel.prefabRenderersToHide, rendererPath))
                    renderer.enabled = false;

                if (ShouldHide(manifest.localViewmodel.prefabRenderersToShow, rendererPath))
                    renderer.enabled = true;
            }
        }

        private void HideLiveFirstPersonRenderers()
        {
            if (context?.Request?.Player == null || context.Manifest == null)
                return;

            if (!context.Manifest.localViewmodel.hideVanillaFirstPersonArms)
                return;

            Renderer renderer = context.Request.Player.thisPlayerModelArms;
            if (renderer == null)
                return;

            hiddenRenderers.Add(RendererState.Capture(renderer));
            renderer.enabled = false;
            context.Logger?.LogInfo(
                "[LCInteractionAnimationAPI] local_viewmodel.renderer_hidden: " +
                $"handle={context.Handle} renderer='{renderer.name}'.");
        }

        // ModelReplacementAPI hides the vanilla arms by layer rather than by renderer, and in the
        // same pass shows its own replacement viewmodel in their place, then drives that viewmodel
        // from the vanilla rig every frame. The vanilla rig is not posed to the animation this
        // presenter is playing, so the replacement parks a static hand in front of the camera and
        // occludes the viewmodel we just instantiated. Hide it by renderer for the duration of the
        // session; verified against ModelReplacementAPI 2.4.20, SetArmLayers writes only
        // gameObject.layer and shadowCastingMode, and the sole SetAvatarRenderers(true) call runs
        // once from Awake, so nothing re-enables these renderers behind us.
        //
        // Captured into the same hiddenRenderers list as the vanilla arms, so Stop restores both.
        private void HideModelReplacementViewmodelRenderers()
        {
            if (context?.Request?.Player == null || context.Manifest == null)
                return;

            // Only claim the arms in the cases where we already claim the vanilla ones.
            if (!context.Manifest.localViewmodel.hideVanillaFirstPersonArms)
                return;

            if (!Chainloader.PluginInfos.ContainsKey(ModelReplacementApiGuid))
                return;

            try
            {
                GameObject replacementViewModel =
                    FindModelReplacementViewmodel(context.Request.Player);
                if (replacementViewModel == null)
                    return;

                int hidden = 0;
                Renderer[] renderers = replacementViewModel.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    Renderer renderer = renderers[i];
                    if (renderer == null)
                        continue;

                    hiddenRenderers.Add(RendererState.Capture(renderer));
                    renderer.enabled = false;
                    hidden++;
                }

                context.Logger?.LogInfo(
                    "[LCInteractionAnimationAPI] local_viewmodel.model_replacement_hidden: " +
                    $"handle={context.Handle} viewModel='{replacementViewModel.name}' " +
                    $"renderers={hidden}.");
            }
            catch (Exception exception)
            {
                context.Logger?.LogWarning(
                    "[LCInteractionAnimationAPI] local_viewmodel.model_replacement_hide_failed: " +
                    $"handle={context.Handle} reason='{exception.Message}'.");
            }
        }

        // ModelReplacementAPI adds its body replacement straight to the player object, and the
        // component is always a subclass of BodyReplacementBase, so walk the base chain by name
        // instead of binding to the assembly.
        private static GameObject FindModelReplacementViewmodel(Component player)
        {
            MonoBehaviour[] behaviours = player.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || !IsBodyReplacementComponent(behaviour))
                    continue;

                System.Reflection.FieldInfo field = behaviour.GetType().GetField(
                    ReplacementViewModelFieldName,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                if (field == null || !typeof(GameObject).IsAssignableFrom(field.FieldType))
                    continue;

                // The second test is Unity's null operator: it also rejects a destroyed object.
                if (field.GetValue(behaviour) is GameObject viewModel && viewModel != null)
                    return viewModel;
            }

            return null;
        }

        private static bool IsBodyReplacementComponent(Component component)
        {
            Type type = component != null ? component.GetType() : null;
            while (type != null)
            {
                if (string.Equals(type.FullName, BodyReplacementBaseTypeName, StringComparison.Ordinal))
                    return true;

                type = type.BaseType;
            }

            return false;
        }

        private void RestoreLiveFirstPersonRenderers()
        {
            for (int i = hiddenRenderers.Count - 1; i >= 0; i--)
                hiddenRenderers[i].Restore();
        }

        private void DestroyViewmodel()
        {
            if (viewmodelRoot != null)
                Object.Destroy(viewmodelRoot);

            viewmodelRoot = null;
            viewmodelAnimator = null;
            viewmodelController = null;
        }

        private void ReleaseViewmodelBundle()
        {
            if (viewmodelBundle != null && ownsViewmodelBundle)
                viewmodelBundle.Unload(false);

            viewmodelBundle = null;
            ownsViewmodelBundle = false;
        }

        private void CleanupFailedStart()
        {
            RestoreLiveFirstPersonRenderers();
            DestroyViewmodel();
            ReleaseViewmodelBundle();
            context = null;
            active = false;
        }

        private static bool ShouldHide(string[] rendererHints, string value)
        {
            if (rendererHints == null)
                return false;

            for (int i = 0; i < rendererHints.Length; i++)
            {
                if (string.Equals(rendererHints[i], value, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool TryFindChildRecursive(
            Transform root,
            string childName,
            out Transform child)
        {
            child = null;
            if (root == null || string.IsNullOrWhiteSpace(childName))
                return false;

            if (string.Equals(root.name, childName, StringComparison.OrdinalIgnoreCase))
            {
                child = root;
                return true;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                if (TryFindChildRecursive(root.GetChild(i), childName, out child))
                    return true;
            }

            return false;
        }

        private static bool TryResolveBundlePath(
            string bundleFileName,
            string assetRootPath,
            out string resolvedPath,
            out string reason)
        {
            return InteractionAnimationAssetPathResolver.TryResolveBundlePath(
                bundleFileName,
                assetRootPath,
                out resolvedPath,
                out reason);
        }

        private static string SafeObjectName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "interaction";

            char[] chars = value.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '_' && chars[i] != '-')
                    chars[i] = '_';
            }

            return new string(chars);
        }

        private static bool IsLayerInCullingMask(Camera camera, int layer)
        {
            if (camera == null || layer < 0 || layer > 31)
                return false;

            return (camera.cullingMask & (1 << layer)) != 0;
        }

        private static Material[] GetSharedMaterials(Renderer renderer)
        {
            if (renderer == null)
                return Array.Empty<Material>();

            try
            {
                return renderer.sharedMaterials ?? Array.Empty<Material>();
            }
            catch
            {
                return Array.Empty<Material>();
            }
        }

        private static string TryFormatMaterialColor(Material material)
        {
            if (material == null)
                return "<null>";

            try
            {
                return material.HasProperty("_Color")
                    ? FormatColor(material.color)
                    : "<no _Color>";
            }
            catch
            {
                return "<unavailable>";
            }
        }

        private static string GetRelativeTransformPath(Transform root, Transform transform)
        {
            if (root == null || transform == null)
                return string.Empty;
            if (ReferenceEquals(root, transform))
                return string.Empty;

            var names = new List<string>();
            Transform current = transform;
            while (current != null && !ReferenceEquals(current, root))
            {
                names.Add(current.name);
                current = current.parent;
            }
            if (current == null)
                return string.Empty;
            names.Reverse();
            return string.Join("/", names.ToArray());
        }
        private static string GetTransformPath(Transform transform)
        {
            if (transform == null)
                return "<null>";

            var names = new List<string>();
            Transform current = transform;
            while (current != null && names.Count < 48)
            {
                names.Add(current.name);
                current = current.parent;
            }

            names.Reverse();
            return string.Join("/", names.ToArray());
        }

        private static string SafeName(Object unityObject)
        {
            return unityObject != null ? unityObject.name : "<null>";
        }

        private static string FormatVector(Vector3 value)
        {
            return "(" +
                   FormatFloat(value.x) + "," +
                   FormatFloat(value.y) + "," +
                   FormatFloat(value.z) + ")";
        }

        private static string FormatVector(
            InteractionAnimationVector3 value)
        {
            return "(" +
                   FormatFloat(value.x) + "," +
                   FormatFloat(value.y) + "," +
                   FormatFloat(value.z) + ")";
        }

        private static string FormatColor(Color value)
        {
            return "(" +
                   FormatFloat(value.r) + "," +
                   FormatFloat(value.g) + "," +
                   FormatFloat(value.b) + "," +
                   FormatFloat(value.a) + ")";
        }

        private static string FormatFloat(float value)
        {
            if (float.IsNaN(value))
                return "NaN";

            if (float.IsPositiveInfinity(value))
                return "Infinity";

            if (float.IsNegativeInfinity(value))
                return "-Infinity";

            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private readonly struct RendererState
        {
            private readonly Renderer renderer;
            private readonly bool enabled;

            private RendererState(Renderer renderer, bool enabled)
            {
                this.renderer = renderer;
                this.enabled = enabled;
            }

            internal static RendererState Capture(Renderer renderer)
            {
                return new RendererState(renderer, renderer != null && renderer.enabled);
            }

            internal void Restore()
            {
                if (renderer != null)
                    renderer.enabled = enabled;
            }
        }
    }
}
