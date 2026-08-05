using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;
using Y4NGZInteractions.InteractionAnimationApi.Authoring;
using Object = UnityEngine.Object;

namespace Y4NGZInteractions.InteractionAnimationApi.Presenters
{
    internal sealed class LocalViewmodelPresenter : IInteractionPresenter
    {
        private const long SynchronousLoadSizeLimitBytes = 16L * 1024L * 1024L;
        private const string SafeGeneratedRuntimeMaterialMode = "safeGenerated";
        private static readonly HashSet<string> PreloadingBundlePaths =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, AssetBundle> PreloadedBundles =
            new Dictionary<string, AssetBundle>(StringComparer.OrdinalIgnoreCase);

        private readonly List<RendererState> hiddenRenderers = new List<RendererState>();
        private readonly List<Material> runtimeMaterials = new List<Material>();
        private InteractionAnimationContext context;
        private AssetBundle viewmodelBundle;
        private bool ownsViewmodelBundle;
        private GameObject viewmodelRoot;
        private RuntimeAnimatorController viewmodelController;
        private Animator viewmodelAnimator;
        private bool active;
        private bool exitRequested;

        internal static void BeginPreload(
            InteractionAnimationManifest manifest,
            ManualLogSource logger)
        {
            TryBeginPreload(manifest, string.Empty, logger, out _);
        }

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

            if (!TryLoadViewmodelBundle(manifest, out reason))
                return false;

            if (!TryInstantiateViewmodel(manifest, out reason))
            {
                CleanupFailedStart();
                return false;
            }

            HideLiveFirstPersonRenderers();
            active = true;
            exitRequested = false;
            SetBoolIfExists(manifest.localViewmodel.activeBool, true);
            FireTriggerIfExists(manifest.localViewmodel.enterTrigger);
            viewmodelAnimator.Update(0f);
            EvaluateViewmodelRigBuilders();
            context.Logger?.LogInfo(
                "[LCInteractionAnimationAPI] local_viewmodel.started: " +
                $"handle={context.Handle} interaction='{manifest.interactionId}' " +
                $"prefab='{manifest.localViewmodel.prefab}' controller='{manifest.localViewmodel.controller}' " +
                $"socketProp='{manifest.sockets?.ResolvedProp ?? string.Empty}'.");
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

        public bool ShouldAutoStop => false;

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

            GameObject prefab = viewmodelBundle.LoadAsset<GameObject>(manifest.localViewmodel.prefab);
            if (prefab == null)
            {
                reason = "viewmodel.prefab_missing:" + manifest.localViewmodel.prefab;
                context.Logger?.LogWarning(
                    "[LCInteractionAnimationAPI] viewmodel.prefab_missing: " +
                    $"handle={context.Handle} prefab='{manifest.localViewmodel.prefab}'.");
                return false;
            }

            viewmodelController = viewmodelBundle.LoadAsset<RuntimeAnimatorController>(
                manifest.localViewmodel.controller);
            if (viewmodelController == null)
            {
                reason = "viewmodel.controller_missing:" + manifest.localViewmodel.controller;
                context.Logger?.LogWarning(
                    "[LCInteractionAnimationAPI] viewmodel.controller_missing: " +
                    $"handle={context.Handle} controller='{manifest.localViewmodel.controller}'.");
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
            viewmodelRoot.transform.localScale = manifest.localViewmodel.localScale == Vector3.zero
                ? Vector3.one
                : manifest.localViewmodel.localScale;

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
            ApplyRuntimeSafeViewmodelMaterials(manifest);
            context.Logger?.LogInfo(
                "[LCInteractionAnimationAPI] local_viewmodel.instantiated: " +
                $"handle={context.Handle} prefab='{manifest.localViewmodel.prefab}' " +
                $"controller='{manifest.localViewmodel.controller}' camera='{cameraParent.name}'.");
            LogCameraDiagnostics(resolvedCamera, cameraParent, manifest);
            LogRendererDiagnostics(resolvedCamera);
            BeginPostFrameRendererDiagnostics(resolvedCamera);
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

            string anchorName = manifest.localViewmodel.cameraAnchor ?? string.Empty;
            if (string.IsNullOrWhiteSpace(anchorName))
            {
                reason = "viewmodel.camera_anchor_empty";
                return false;
            }

            if (!TryFindChildRecursive(viewmodelRoot.transform, anchorName, out Transform anchor))
            {
                reason = "viewmodel.camera_anchor_missing:" + anchorName;
                context.Logger?.LogWarning(
                    "[LCInteractionAnimationAPI] local_viewmodel.anchor_missing: " +
                    $"handle={context.Handle} anchor='{anchorName}'.");
                return false;
            }

            Vector3 anchorRootLocalPosition = viewmodelRoot.transform.InverseTransformPoint(anchor.position);
            Quaternion anchorRootLocalRotation =
                Quaternion.Inverse(viewmodelRoot.transform.rotation) * anchor.rotation;
            Quaternion targetLocalRotation = Quaternion.Euler(manifest.localViewmodel.cameraLocalEuler);
            viewmodelRoot.transform.localRotation =
                targetLocalRotation * Quaternion.Inverse(anchorRootLocalRotation);
            Vector3 anchorOffset = viewmodelRoot.transform.localRotation *
                Vector3.Scale(anchorRootLocalPosition, viewmodelRoot.transform.localScale);
            viewmodelRoot.transform.localPosition =
                manifest.localViewmodel.cameraLocalPosition - anchorOffset;

            context.Logger?.LogInfo(
                "[LCInteractionAnimationAPI] local_viewmodel.anchor_aligned: " +
                $"handle={context.Handle} anchor='{anchorName}' path='{GetTransformPath(anchor)}' " +
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
                $"manifestCameraAnchor='{manifest.localViewmodel.cameraAnchor}' " +
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

        private void ApplyRuntimeSafeViewmodelMaterials(InteractionAnimationManifest manifest)
        {
            if (!ShouldApplyRuntimeSafeViewmodelMaterials(manifest) || viewmodelRoot == null)
                return;

            Renderer[] renderers = viewmodelRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null ||
                    !TryGetRuntimeMaterialTarget(manifest, renderer, out string target, out Color color))
                {
                    continue;
                }

                Material material = CreateRuntimeSafeMaterial(renderer.name, color);
                if (material == null)
                    continue;

                runtimeMaterials.Add(material);
                Material[] materials = renderer.sharedMaterials;
                if (materials == null || materials.Length == 0)
                {
                    renderer.sharedMaterial = material;
                }
                else
                {
                    for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                        materials[materialIndex] = material;

                    renderer.sharedMaterials = materials;
                }

                renderer.enabled = true;
                context.Logger?.LogInfo(
                    "[LCInteractionAnimationAPI] local_viewmodel.runtime_material_applied: " +
                    $"handle={context.Handle} target='{target}' renderer='{GetTransformPath(renderer.transform)}' " +
                    $"material='{material.name}' shader='{SafeName(material.shader)}' color={FormatColor(color)}.");
            }
        }

        private static bool ShouldApplyRuntimeSafeViewmodelMaterials(InteractionAnimationManifest manifest)
        {
            return manifest?.localViewmodel != null &&
                string.Equals(
                    manifest.localViewmodel.runtimeMaterialMode,
                    SafeGeneratedRuntimeMaterialMode,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetRuntimeMaterialTarget(
            InteractionAnimationManifest manifest,
            Renderer renderer,
            out string target,
            out Color color)
        {
            target = string.Empty;
            color = Color.white;

            if (manifest == null || renderer == null)
                return false;

            if (manifest.sockets != null &&
                !string.IsNullOrWhiteSpace(manifest.sockets.ResolvedProp) &&
                string.Equals(renderer.name, manifest.sockets.ResolvedProp, StringComparison.OrdinalIgnoreCase))
            {
                target = "prop";
                color = new Color(0.56f, 0.6f, 0.44f, 1f);
                return true;
            }

            if (manifest.localViewmodel != null &&
                ShouldHide(manifest.localViewmodel.visibleRenderers, renderer.name))
            {
                target = "visible_viewmodel";
                color = new Color(0.95f, 0.5f, 0.18f, 1f);
                return true;
            }

            return false;
        }

        private static Material CreateRuntimeSafeMaterial(string rendererName, Color color)
        {
            Shader shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Standard");
            if (shader == null)
                return null;

            var material = new Material(shader)
            {
                name = "Y4NGZ_RuntimeViewmodel_" + SafeObjectName(rendererName)
            };

            if (material.HasProperty("_Color"))
                material.color = color;

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);

            if (material.HasProperty("_EmissionColor"))
            {
                Color emissionColor = new Color(
                    Mathf.Min(color.r * 0.35f, 1f),
                    Mathf.Min(color.g * 0.35f, 1f),
                    Mathf.Min(color.b * 0.35f, 1f),
                    color.a);
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emissionColor);
            }

            material.renderQueue = 2000;
            return material;
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

                if (ShouldHide(manifest.localViewmodel.hideSourceRenderers, renderer.name))
                    renderer.enabled = false;

                if (ShouldHide(manifest.localViewmodel.visibleRenderers, renderer.name))
                    renderer.enabled = true;
            }
        }

        private void HideLiveFirstPersonRenderers()
        {
            if (context?.Request?.Player == null || context.Manifest == null)
                return;

            string[] rendererHints = context.Manifest.liveRenderersToHide ?? Array.Empty<string>();
            if (!ShouldHide(rendererHints, "thisPlayerModelArms") &&
                !ShouldHide(rendererHints, "lc_first_person_hands"))
            {
                return;
            }

            Renderer renderer = context.Request.Player.thisPlayerModelArms;
            if (renderer == null)
                return;

            hiddenRenderers.Add(RendererState.Capture(renderer));
            renderer.enabled = false;
            context.Logger?.LogInfo(
                "[LCInteractionAnimationAPI] local_viewmodel.renderer_hidden: " +
                $"handle={context.Handle} renderer='{renderer.name}'.");
        }

        private void RestoreLiveFirstPersonRenderers()
        {
            for (int i = hiddenRenderers.Count - 1; i >= 0; i--)
                hiddenRenderers[i].Restore();
        }

        private void DestroyViewmodel()
        {
            DestroyRuntimeMaterials();

            if (viewmodelRoot != null)
                Object.Destroy(viewmodelRoot);

            viewmodelRoot = null;
            viewmodelAnimator = null;
            viewmodelController = null;
        }

        private void DestroyRuntimeMaterials()
        {
            for (int i = runtimeMaterials.Count - 1; i >= 0; i--)
            {
                if (runtimeMaterials[i] != null)
                    Object.Destroy(runtimeMaterials[i]);
            }

            runtimeMaterials.Clear();
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
                InteractionAnimationAssetPathResolver.GetDefaultAssetRoots(),
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
