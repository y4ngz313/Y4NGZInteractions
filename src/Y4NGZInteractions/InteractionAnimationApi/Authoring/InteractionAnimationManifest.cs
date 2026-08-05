using System;
using UnityEngine;

namespace Y4NGZInteractions.InteractionAnimationApi.Authoring
{
    [Serializable]
    public sealed class InteractionAnimationManifest
    {
        public int schemaVersion = 1;
        public string interactionId = string.Empty;
        public string displayName = string.Empty;
        public float durationSeconds;
        public float frameRate;
        public string bundleInternalName = string.Empty;
        public LocalViewmodelManifest localViewmodel = new LocalViewmodelManifest();
        public SocketManifest sockets = new SocketManifest();
        public string[] liveRenderersToHide = Array.Empty<string>();
        public BodyManifest body = new BodyManifest();
        public ValidationManifest validation = new ValidationManifest();
        // Opt-in exemptions declared by the authoring consumer. The user config lists
        // (see InteractionAnimationApiPlugin) remain honored on top of these flags, so an
        // operator can exempt an interaction the manifest did not.
        public bool exemptFromCameraDisplacementGuard;
        public bool exemptFromSpecialAnimationAutoStop;

        public static bool TryParse(string json, out InteractionAnimationManifest manifest, out string reason)
        {
            manifest = null;
            reason = string.Empty;

            if (string.IsNullOrWhiteSpace(json))
            {
                reason = "manifest_json_empty";
                return false;
            }

            try
            {
                manifest = JsonUtility.FromJson<InteractionAnimationManifest>(json);
            }
            catch (Exception exception)
            {
                reason = "manifest_json_invalid:" + exception.Message;
                return false;
            }

            if (manifest == null)
            {
                reason = "manifest_json_returned_null";
                return false;
            }

            return true;
        }

        public InteractionAnimationValidationResult Validate(
            string expectedInteractionId,
            InteractionAnimationPresentationKind presentationKind)
        {
            if (schemaVersion <= 0)
                return InteractionAnimationValidationResult.Invalid("manifest_schema_version_invalid");

            if (string.IsNullOrWhiteSpace(interactionId))
                return InteractionAnimationValidationResult.Invalid("manifest_interaction_id_empty");

            if (!string.IsNullOrWhiteSpace(expectedInteractionId) &&
                !string.Equals(interactionId, expectedInteractionId, StringComparison.OrdinalIgnoreCase))
            {
                return InteractionAnimationValidationResult.Invalid("manifest_interaction_id_mismatch");
            }

            if (durationSeconds < 0f || float.IsNaN(durationSeconds) || float.IsInfinity(durationSeconds))
                return InteractionAnimationValidationResult.Invalid("manifest_duration_invalid");

            if (presentationKind == InteractionAnimationPresentationKind.DedicatedLocalViewmodel ||
                presentationKind == InteractionAnimationPresentationKind.Hybrid)
            {
                if (localViewmodel == null)
                    return InteractionAnimationValidationResult.Invalid("manifest_viewmodel_missing");

                if (string.IsNullOrWhiteSpace(localViewmodel.prefab))
                    return InteractionAnimationValidationResult.Invalid("manifest_viewmodel_prefab_empty");

                if (string.IsNullOrWhiteSpace(localViewmodel.bundleFileName))
                    return InteractionAnimationValidationResult.Invalid("manifest_viewmodel_bundle_file_empty");

                if (string.IsNullOrWhiteSpace(localViewmodel.controller))
                    return InteractionAnimationValidationResult.Invalid("manifest_viewmodel_controller_empty");

                if (string.IsNullOrWhiteSpace(localViewmodel.cameraAnchor))
                    return InteractionAnimationValidationResult.Invalid("manifest_viewmodel_camera_anchor_empty");

                if (sockets == null)
                    return InteractionAnimationValidationResult.Invalid("manifest_sockets_missing");

                if (string.IsNullOrWhiteSpace(sockets.leftHand))
                    return InteractionAnimationValidationResult.Invalid("manifest_socket_left_hand_empty");

                if (string.IsNullOrWhiteSpace(sockets.rightHand))
                    return InteractionAnimationValidationResult.Invalid("manifest_socket_right_hand_empty");

                if (string.IsNullOrWhiteSpace(sockets.ResolvedProp))
                    return InteractionAnimationValidationResult.Invalid("manifest_socket_prop_empty");
            }

            if (presentationKind == InteractionAnimationPresentationKind.BodyWorld)
            {
                if (body == null || !body.enabled)
                    return InteractionAnimationValidationResult.Invalid("manifest_body_disabled");

                if (string.IsNullOrWhiteSpace(body.bundleFileName))
                    return InteractionAnimationValidationResult.Invalid("manifest_body_bundle_file_empty");

                if (string.IsNullOrWhiteSpace(body.controller))
                    return InteractionAnimationValidationResult.Invalid("manifest_body_controller_empty");
            }

            return InteractionAnimationValidationResult.Valid();
        }

        [Serializable]
        public sealed class LocalViewmodelManifest
        {
            public string bundleFileName = string.Empty;
            public string prefab = string.Empty;
            public string controller = string.Empty;
            public string activeBool = string.Empty;
            public string enterTrigger = string.Empty;
            public string exitTrigger = string.Empty;
            public float exitSeconds;
            public string root = string.Empty;
            public string cameraAnchor = "Y4NGZ_ViewmodelCameraAnchor";
            public Vector3 cameraLocalPosition = new Vector3(0f, -0.42f, 0.95f);
            public Vector3 cameraLocalEuler = Vector3.zero;
            public Vector3 localScale = new Vector3(0.55f, 0.55f, 0.55f);
            public string runtimeMaterialMode = string.Empty;
            public string[] hideSourceRenderers = Array.Empty<string>();
            public string[] visibleRenderers = Array.Empty<string>();
        }

        [Serializable]
        public sealed class SocketManifest
        {
            public string leftHand = string.Empty;
            public string rightHand = string.Empty;
            // Renderer name of the held prop carried by the viewmodel prefab.
            public string prop = string.Empty;
            // Deprecated alias for <see cref="prop"/>, kept so manifests authored against the
            // original schema keep loading. Read through <see cref="ResolvedProp"/>, never directly.
            public string tablet = string.Empty;

            /// <summary>
            /// The prop renderer name: <see cref="prop"/> when authored, otherwise the legacy
            /// <see cref="tablet"/> alias.
            /// </summary>
            public string ResolvedProp =>
                !string.IsNullOrWhiteSpace(prop) ? prop : tablet;
        }

        [Serializable]
        public sealed class BodyManifest
        {
            public bool enabled;
            public string bundleFileName = string.Empty;
            public string controller = string.Empty;
            public string controllerAssetName = string.Empty;
            public string clip = string.Empty;
            public string activeBool = string.Empty;
            public string enterTrigger = string.Empty;
            public string exitTrigger = string.Empty;
            public string fullBodyLayer = string.Empty;
            public string firstPersonArmsLayer = string.Empty;
            public float startLayerWeight = 1f;
            public float layerWeightRampSeconds;
            // Fixed weight for the full-body layer, overriding the ramped weight. Negative
            // (default, absent in older manifests) = legacy: ramp both layers together.
            // Weapons set 0: vanilla's own two-handed hold animates the body — a constant
            // override pose at high weight fights locomotion (walk-skew, frozen torso).
            public float fullBodyLayerWeight = -1f;
            // Opt-in experimental teardown for first-person-only interactions. Captures the
            // local arms-metarig descendants before the controller swap, restores them before
            // rebuilding the RigBuilder, and skips the broad whole-player Animator.Rebind.
            // Absent/false preserves the existing behavior unchanged.
            public bool scopedFirstPersonTransformRestore;
            // Opt-in position stabilization for short local interactions whose temporary body
            // controller animates a parent of gameplayCamera. The presenter preserves mouse-look
            // rotation while pinning only the camera's player-local position. False keeps the
            // existing behavior unchanged.
            public bool stabilizeLocalCameraPosition;
            // Opt-in for a local session that IS the third-person presentation: a camera mod,
            // not this session, owns the local camera. Every local camera behavior below
            // assumes local means first person and that the camera must stay at rest — the
            // displacement guard, both live stabilizers, the session-entry drift heal and the
            // stop-time rotation/position snaps. The external presenter also owns local visor
            // placement and visibility, so seam visor restore and hard visor glue stand down.
            // All of these behaviors stand down for this session only.
            // Absent/false preserves the existing behavior unchanged.
            public bool localCameraOwnedExternally;
            // Optional layer fades used by short one-shots whose endpoint geometry crosses the
            // camera near plane. Zero preserves existing behavior.
            public float enterLayerFadeSeconds;
            public float naturalEndLayerFadeSeconds;
            public bool suppressRigBuilders = true;
            public string diagnosticVanillaOverrideClip = string.Empty;
            public string overrideSlotPrefix = string.Empty;
            // Toggle lifecycle: seconds the exit (put-away) animation needs after the exit
            // trigger fires, before the controller is restored. 0 = restore immediately.
            public float exitSeconds;
            // Animator Int parameter the presenter drives every frame from the player's
            // movement state (0 = idle, 1 = walking, 2 = sprinting). Empty = disabled.
            public string movementParameter = string.Empty;
            public ClipPackManifest clipPack = new ClipPackManifest();
            public PropManifest prop = new PropManifest();
        }

        [Serializable]
        public sealed class PropManifest
        {
            public bool enabled;
            // Prefab asset name inside the clip-pack bundle.
            public string prefabName = string.Empty;
            // Bone (searched under the arms metarig) the prop is parented to.
            public string attachBone = string.Empty;
            // Local pose under the attach bone; produced by the baker's prop-attachment
            // report (anatomical basis-corrected source pose).
            public Vector3 localPosition = Vector3.zero;
            public Vector3 localEulerAngles = Vector3.zero;
            public float localScale = 1f;
            // Optional one-shot release point. Values <= 0 preserve the prop for the
            // interaction lifetime; positive values destroy the held instance at this time.
            public float releaseSeconds;
        }

        [Serializable]
        public sealed class ClipPackManifest
        {
            public bool enabled;
            public string bundleFileName = string.Empty;
            public string bundleInternalName = string.Empty;
            public ClipOverrideManifest[] overrides = Array.Empty<ClipOverrideManifest>();
        }

        [Serializable]
        public sealed class ClipOverrideManifest
        {
            public string slot = string.Empty;
            public string clip = string.Empty;
        }

        [Serializable]
        public sealed class ValidationManifest
        {
            public string generatedAt = string.Empty;
            public string previewPixelCoverage = string.Empty;
            public string meshTransfer = string.Empty;
            public string socketNames = string.Empty;
            public string cameraBounds = string.Empty;
        }
    }
}
