using System;
using System.Runtime.Serialization;
using Newtonsoft.Json;

namespace Y4NGZInteractions.InteractionAnimationApi.Authoring
{
    // Compatibility input only. Schema-1 members never appear on the 1.0 public DTO.
    // Json.NET creates field-serialized DTOs without running a constructor or field initializers,
    // so every DTO declares its non-zero defaults once, in an OnDeserializing method that its
    // constructor also calls. A key missing from the JSON keeps its default.
    [Serializable]
    [JsonObject(MemberSerialization.Fields)]
    internal sealed class LegacyInteractionAnimationManifest
    {
        internal int schemaVersion;
        internal string interactionId;
        internal string displayName;
        internal float durationSeconds;
        internal float frameRate;
        internal string bundleInternalName;
        internal LegacyLocalViewmodelManifest localViewmodel;
        internal LegacySocketManifest sockets;
        internal string[] liveRenderersToHide;
        internal LegacyBodyManifest body;
        internal LegacyValidationManifest validation;
        internal bool exemptFromCameraDisplacementGuard;
        internal bool exemptFromSpecialAnimationAutoStop;

        public LegacyInteractionAnimationManifest()
        {
            ApplyDefaults(default);
        }

        [OnDeserializing]
        private void ApplyDefaults(StreamingContext context)
        {
            schemaVersion = 1;
            interactionId = string.Empty;
            displayName = string.Empty;
            bundleInternalName = string.Empty;
            localViewmodel = new LegacyLocalViewmodelManifest();
            sockets = new LegacySocketManifest();
            liveRenderersToHide = Array.Empty<string>();
            body = new LegacyBodyManifest();
            validation = new LegacyValidationManifest();
        }

        [Serializable]
        [JsonObject(MemberSerialization.Fields)]
        internal sealed class LegacyLocalViewmodelManifest
        {
            internal string bundleFileName;
            internal string prefab;
            internal string controller;
            internal string activeBool;
            internal string enterTrigger;
            internal string exitTrigger;
            internal float exitSeconds;
            internal string root;
            internal string cameraAnchor;
            internal InteractionAnimationVector3 cameraLocalPosition;
            internal InteractionAnimationVector3 cameraLocalEuler;
            internal InteractionAnimationVector3 localScale;
            internal string runtimeMaterialMode;
            internal string[] hideSourceRenderers;
            internal string[] visibleRenderers;

            public LegacyLocalViewmodelManifest()
            {
                ApplyDefaults(default);
            }

            [OnDeserializing]
            private void ApplyDefaults(StreamingContext context)
            {
                bundleFileName = string.Empty;
                prefab = string.Empty;
                controller = string.Empty;
                activeBool = string.Empty;
                enterTrigger = string.Empty;
                exitTrigger = string.Empty;
                root = string.Empty;
                cameraAnchor = "Y4NGZ_ViewmodelCameraAnchor";
                cameraLocalPosition = new InteractionAnimationVector3(0f, -0.42f, 0.95f);
                localScale = new InteractionAnimationVector3(0.55f, 0.55f, 0.55f);
                runtimeMaterialMode = string.Empty;
                hideSourceRenderers = Array.Empty<string>();
                visibleRenderers = Array.Empty<string>();
            }
        }

        [Serializable]
        [JsonObject(MemberSerialization.Fields)]
        internal sealed class LegacySocketManifest
        {
            internal string leftHand;
            internal string rightHand;
            internal string prop;
            internal string tablet;

            public LegacySocketManifest()
            {
                ApplyDefaults(default);
            }

            [OnDeserializing]
            private void ApplyDefaults(StreamingContext context)
            {
                leftHand = string.Empty;
                rightHand = string.Empty;
                prop = string.Empty;
                tablet = string.Empty;
            }
        }

        [Serializable]
        [JsonObject(MemberSerialization.Fields)]
        internal sealed class LegacyBodyManifest
        {
            internal bool enabled;
            internal string bundleFileName;
            internal string controller;
            internal string controllerAssetName;
            internal string clip;
            internal string activeBool;
            internal string enterTrigger;
            internal string exitTrigger;
            internal string fullBodyLayer;
            internal string firstPersonArmsLayer;
            internal float startLayerWeight;
            internal float layerWeightRampSeconds;
            internal float fullBodyLayerWeight;
            internal bool scopedFirstPersonTransformRestore;
            internal bool stabilizeLocalCameraPosition;
            internal bool localCameraOwnedExternally;
            internal float enterLayerFadeSeconds;
            internal float naturalEndLayerFadeSeconds;
            internal bool suppressRigBuilders;
            internal string diagnosticVanillaOverrideClip;
            internal string overrideSlotPrefix;
            internal float exitSeconds;
            internal string movementParameter;
            internal LegacyClipPackManifest clipPack;
            internal LegacyPropManifest prop;

            public LegacyBodyManifest()
            {
                ApplyDefaults(default);
            }

            [OnDeserializing]
            private void ApplyDefaults(StreamingContext context)
            {
                bundleFileName = string.Empty;
                controller = string.Empty;
                controllerAssetName = string.Empty;
                clip = string.Empty;
                activeBool = string.Empty;
                enterTrigger = string.Empty;
                exitTrigger = string.Empty;
                fullBodyLayer = string.Empty;
                firstPersonArmsLayer = string.Empty;
                startLayerWeight = 1f;
                fullBodyLayerWeight = -1f;
                suppressRigBuilders = true;
                diagnosticVanillaOverrideClip = string.Empty;
                overrideSlotPrefix = string.Empty;
                movementParameter = string.Empty;
                clipPack = new LegacyClipPackManifest();
                prop = new LegacyPropManifest();
            }
        }

        [Serializable]
        [JsonObject(MemberSerialization.Fields)]
        internal sealed class LegacyPropManifest
        {
            internal bool enabled;
            internal string prefabName;
            internal string attachBone;
            internal InteractionAnimationVector3 localPosition;
            internal InteractionAnimationVector3 localEulerAngles;
            internal float localScale;
            internal float releaseSeconds;
            internal bool localBodyCopy;

            public LegacyPropManifest()
            {
                ApplyDefaults(default);
            }

            [OnDeserializing]
            private void ApplyDefaults(StreamingContext context)
            {
                prefabName = string.Empty;
                attachBone = string.Empty;
                localScale = 1f;
            }
        }

        [Serializable]
        [JsonObject(MemberSerialization.Fields)]
        internal sealed class LegacyClipPackManifest
        {
            internal bool enabled;
            internal string bundleFileName;
            internal string bundleInternalName;
            internal LegacyClipOverrideManifest[] overrides;

            public LegacyClipPackManifest()
            {
                ApplyDefaults(default);
            }

            [OnDeserializing]
            private void ApplyDefaults(StreamingContext context)
            {
                bundleFileName = string.Empty;
                bundleInternalName = string.Empty;
                overrides = Array.Empty<LegacyClipOverrideManifest>();
            }
        }

        [Serializable]
        [JsonObject(MemberSerialization.Fields)]
        internal sealed class LegacyClipOverrideManifest
        {
            internal string slot;
            internal string clip;

            public LegacyClipOverrideManifest()
            {
                ApplyDefaults(default);
            }

            [OnDeserializing]
            private void ApplyDefaults(StreamingContext context)
            {
                slot = string.Empty;
                clip = string.Empty;
            }
        }

        [Serializable]
        [JsonObject(MemberSerialization.Fields)]
        internal sealed class LegacyValidationManifest
        {
            internal string generatedAt;
            internal string previewPixelCoverage;
            internal string meshTransfer;
            internal string socketNames;
            internal string cameraBounds;

            public LegacyValidationManifest()
            {
                ApplyDefaults(default);
            }

            [OnDeserializing]
            private void ApplyDefaults(StreamingContext context)
            {
                generatedAt = string.Empty;
                previewPixelCoverage = string.Empty;
                meshTransfer = string.Empty;
                socketNames = string.Empty;
                cameraBounds = string.Empty;
            }
        }
    }
}
