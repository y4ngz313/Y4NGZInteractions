using System;
using UnityEngine;

namespace Y4NGZInteractions.InteractionAnimationApi.Authoring
{
    /// <summary>A JSON-friendly vector used by manifests and authoring tools.</summary>
    [Serializable]
    public struct InteractionAnimationVector3
    {
        /// <summary>Creates a vector from three components.</summary>
        /// <param name="x">X component.</param>
        /// <param name="y">Y component.</param>
        /// <param name="z">Z component.</param>
        public InteractionAnimationVector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        /// <summary>Gets or sets the X component.</summary>
        public float x;

        /// <summary>Gets or sets the Y component.</summary>
        public float y;

        /// <summary>Gets or sets the Z component.</summary>
        public float z;

        internal Vector3 ToUnityVector3()
        {
            return new Vector3(x, y, z);
        }
    }

    /// <summary>Schema-2 authoring contract for one interaction payload.</summary>
    [Serializable]
    public sealed class InteractionAnimationManifest
    {
        /// <summary>Gets or sets the manifest schema. New manifests must use 2.</summary>
        public int schemaVersion = 2;

        /// <summary>Gets or sets the interaction id matched to its registration.</summary>
        public string interactionId = string.Empty;

        /// <summary>Gets or sets the natural lifetime in seconds. Zero means no timed end.</summary>
        public float durationSeconds;

        /// <summary>Gets or sets an optional internal AssetBundle name used for cache lookup.</summary>
        public string bundleInternalName = string.Empty;

        /// <summary>Gets or sets the dedicated local-viewmodel contract.</summary>
        public LocalViewmodelManifest localViewmodel = new LocalViewmodelManifest();

        /// <summary>Gets or sets the body-world contract.</summary>
        public BodyManifest body = new BodyManifest();

        /// <summary>Strictly parses schema 2 or migrates legacy schema 1.</summary>
        /// <param name="json">Manifest JSON.</param>
        /// <param name="manifest">The normalized schema-2 manifest when parsing succeeds.</param>
        /// <param name="reason">The first stable error code when parsing fails.</param>
        /// <returns>True when the JSON can be normalized.</returns>
        public static bool TryParse(
            string json,
            out InteractionAnimationManifest manifest,
            out string reason)
        {
            InteractionAnimationValidationReport report =
                InteractionAnimationManifestValidator.Parse(json, out manifest);
            reason = InteractionAnimationManifestValidator.GetFirstErrorCode(report);
            return report.IsValid;
        }

        /// <summary>Schema-2 settings for a consumer-authored camera-local rig.</summary>
        [Serializable]
        public sealed class LocalViewmodelManifest
        {
            /// <summary>Gets or sets the bundle path relative to the pack asset root.</summary>
            public string bundleFileName = string.Empty;

            /// <summary>Gets or sets the prefab asset name inside the bundle.</summary>
            public string prefabAssetName = string.Empty;

            /// <summary>Gets or sets the controller asset name inside the bundle.</summary>
            public string controllerAssetName = string.Empty;

            /// <summary>Gets or sets an optional Bool parameter driven for the session lifetime.</summary>
            public string activeBool = string.Empty;

            /// <summary>Gets or sets an optional Trigger fired at entry.</summary>
            public string enterTrigger = string.Empty;

            /// <summary>Gets or sets an optional Trigger fired by graceful exit.</summary>
            public string exitTrigger = string.Empty;

            /// <summary>Gets or sets the graceful-exit delay before restoration.</summary>
            public float exitSeconds;

            /// <summary>Gets or sets the prefab-relative transform path aligned to the camera.</summary>
            public string cameraAnchorPath = string.Empty;

            /// <summary>Gets or sets the camera-local anchor target position.</summary>
            public InteractionAnimationVector3 cameraLocalPosition;

            /// <summary>Gets or sets the camera-local anchor target Euler rotation.</summary>
            public InteractionAnimationVector3 cameraLocalEuler;

            /// <summary>Gets or sets the instantiated prefab scale.</summary>
            public InteractionAnimationVector3 localScale =
                new InteractionAnimationVector3(1f, 1f, 1f);

            /// <summary>Gets or sets whether the local player's vanilla arms are hidden.</summary>
            public bool hideVanillaFirstPersonArms;

            /// <summary>Gets or sets prefab-relative renderer paths disabled after instantiation.</summary>
            public string[] prefabRenderersToHide = Array.Empty<string>();

            /// <summary>Gets or sets prefab-relative renderer paths explicitly enabled.</summary>
            public string[] prefabRenderersToShow = Array.Empty<string>();
        }

        /// <summary>Schema-2 settings for a Lethal Company player-body controller.</summary>
        [Serializable]
        public sealed class BodyManifest
        {
            /// <summary>Gets or sets whether the body-world payload is authored.</summary>
            public bool enabled;

            /// <summary>Gets or sets the controller bundle path relative to the pack asset root.</summary>
            public string bundleFileName = string.Empty;

            /// <summary>Gets or sets the controller asset name inside the bundle.</summary>
            public string controllerAssetName = string.Empty;

            /// <summary>Gets or sets an optional Bool parameter driven for the session lifetime.</summary>
            public string activeBool = string.Empty;

            /// <summary>Gets or sets an optional Trigger fired at entry.</summary>
            public string enterTrigger = string.Empty;

            /// <summary>Gets or sets an optional Trigger fired by graceful exit.</summary>
            public string exitTrigger = string.Empty;

            /// <summary>Gets or sets the full-body animator layer name.</summary>
            public string fullBodyLayer = string.Empty;

            /// <summary>Gets or sets the first-person-arms animator layer name.</summary>
            public string firstPersonArmsLayer = string.Empty;

            /// <summary>Gets or sets the starting layer weight.</summary>
            public float startLayerWeight = 1f;

            /// <summary>Gets or sets the layer ramp duration.</summary>
            public float layerWeightRampSeconds;

            /// <summary>Gets or sets a fixed full-body weight; negative uses the shared ramp.</summary>
            public float fullBodyLayerWeight = -1f;

            /// <summary>Gets or sets an optional entry fade duration.</summary>
            public float enterLayerFadeSeconds;

            /// <summary>Gets or sets an optional natural-end fade duration.</summary>
            public float naturalEndLayerFadeSeconds;

            /// <summary>Gets or sets whether Animation Rigging graphs are rebuilt for playback.</summary>
            public bool rebuildRigBuilders;

            /// <summary>Gets or sets the graceful-exit delay before restoration.</summary>
            public float exitSeconds;

            /// <summary>Gets or sets an optional Int parameter driven from movement state.</summary>
            public string movementParameter = string.Empty;

            /// <summary>Gets or sets whether the API preserves the local gameplay camera.</summary>
            public bool preserveGameplayCamera = true;

            /// <summary>Gets or sets whether vanilla special animations stop the session.</summary>
            public bool stopOnVanillaSpecialAnimation = true;

            /// <summary>Gets or sets optional runtime clip overrides.</summary>
            public ClipPackManifest clipPack = new ClipPackManifest();

            /// <summary>Gets or sets an optional hand-attached prop.</summary>
            public PropManifest prop = new PropManifest();
        }

        /// <summary>Describes an optional prop attached to a body-world bone.</summary>
        [Serializable]
        public sealed class PropManifest
        {
            /// <summary>Gets or sets whether the prop is used.</summary>
            public bool enabled;

            /// <summary>Gets or sets the prefab asset name in the controller or clip-pack bundle.</summary>
            public string prefabAssetName = string.Empty;

            /// <summary>Gets or sets the arms-metarig-relative attachment-bone path.</summary>
            public string attachBonePath = string.Empty;

            /// <summary>Gets or sets the local attachment position.</summary>
            public InteractionAnimationVector3 localPosition;

            /// <summary>Gets or sets the local attachment Euler rotation.</summary>
            public InteractionAnimationVector3 localEulerAngles;

            /// <summary>Gets or sets the uniform local scale.</summary>
            public float localScale = 1f;

            /// <summary>Gets or sets an optional release time; zero retains the prop until stop.</summary>
            public float releaseSeconds;
        }

        /// <summary>Describes an optional bundle of animation clips.</summary>
        [Serializable]
        public sealed class ClipPackManifest
        {
            /// <summary>Gets or sets whether clip overrides are used.</summary>
            public bool enabled;

            /// <summary>Gets or sets the clip bundle path relative to the pack asset root.</summary>
            public string bundleFileName = string.Empty;

            /// <summary>Gets or sets an optional internal AssetBundle name for cache lookup.</summary>
            public string bundleInternalName = string.Empty;

            /// <summary>Gets or sets controller-slot to clip-asset mappings.</summary>
            public ClipOverrideManifest[] overrides = Array.Empty<ClipOverrideManifest>();
        }

        /// <summary>Maps one shell-controller clip slot to a clip asset.</summary>
        [Serializable]
        public sealed class ClipOverrideManifest
        {
            /// <summary>Gets or sets the shell-controller clip slot.</summary>
            public string slot = string.Empty;

            /// <summary>Gets or sets the replacement clip asset name.</summary>
            public string clip = string.Empty;
        }
    }
}
