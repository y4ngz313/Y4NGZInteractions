using System;
using Newtonsoft.Json;

namespace Y4NGZInteractions.InteractionAnimationApi.Authoring
{
    // Compatibility input only. Schema-1 members never appear on the 1.0 public DTO.
    [Serializable]
    [JsonObject(MemberSerialization.Fields)]
    internal sealed class LegacyInteractionAnimationManifest
    {
        internal int schemaVersion = 1;
        internal string interactionId = string.Empty;
        internal string displayName = string.Empty;
        internal float durationSeconds;
        internal float frameRate;
        internal string bundleInternalName = string.Empty;
        internal LegacyLocalViewmodelManifest localViewmodel =
            new LegacyLocalViewmodelManifest();
        internal LegacySocketManifest sockets = new LegacySocketManifest();
        internal string[] liveRenderersToHide = Array.Empty<string>();
        internal LegacyBodyManifest body = new LegacyBodyManifest();
        internal LegacyValidationManifest validation = new LegacyValidationManifest();
        internal bool exemptFromCameraDisplacementGuard;
        internal bool exemptFromSpecialAnimationAutoStop;

        [Serializable]
        [JsonObject(MemberSerialization.Fields)]
        internal sealed class LegacyLocalViewmodelManifest
        {
            internal string bundleFileName = string.Empty;
            internal string prefab = string.Empty;
            internal string controller = string.Empty;
            internal string activeBool = string.Empty;
            internal string enterTrigger = string.Empty;
            internal string exitTrigger = string.Empty;
            internal float exitSeconds;
            internal string root = string.Empty;
            internal string cameraAnchor = "Y4NGZ_ViewmodelCameraAnchor";
            internal InteractionAnimationVector3 cameraLocalPosition =
                new InteractionAnimationVector3(0f, -0.42f, 0.95f);
            internal InteractionAnimationVector3 cameraLocalEuler;
            internal InteractionAnimationVector3 localScale =
                new InteractionAnimationVector3(0.55f, 0.55f, 0.55f);
            internal string runtimeMaterialMode = string.Empty;
            internal string[] hideSourceRenderers = Array.Empty<string>();
            internal string[] visibleRenderers = Array.Empty<string>();
        }

        [Serializable]
        [JsonObject(MemberSerialization.Fields)]
        internal sealed class LegacySocketManifest
        {
            internal string leftHand = string.Empty;
            internal string rightHand = string.Empty;
            internal string prop = string.Empty;
            internal string tablet = string.Empty;
        }

        [Serializable]
        [JsonObject(MemberSerialization.Fields)]
        internal sealed class LegacyBodyManifest
        {
            internal bool enabled;
            internal string bundleFileName = string.Empty;
            internal string controller = string.Empty;
            internal string controllerAssetName = string.Empty;
            internal string clip = string.Empty;
            internal string activeBool = string.Empty;
            internal string enterTrigger = string.Empty;
            internal string exitTrigger = string.Empty;
            internal string fullBodyLayer = string.Empty;
            internal string firstPersonArmsLayer = string.Empty;
            internal float startLayerWeight = 1f;
            internal float layerWeightRampSeconds;
            internal float fullBodyLayerWeight = -1f;
            internal bool scopedFirstPersonTransformRestore;
            internal bool stabilizeLocalCameraPosition;
            internal bool localCameraOwnedExternally;
            internal float enterLayerFadeSeconds;
            internal float naturalEndLayerFadeSeconds;
            internal bool suppressRigBuilders = true;
            internal string diagnosticVanillaOverrideClip = string.Empty;
            internal string overrideSlotPrefix = string.Empty;
            internal float exitSeconds;
            internal string movementParameter = string.Empty;
            internal LegacyClipPackManifest clipPack = new LegacyClipPackManifest();
            internal LegacyPropManifest prop = new LegacyPropManifest();
        }

        [Serializable]
        [JsonObject(MemberSerialization.Fields)]
        internal sealed class LegacyPropManifest
        {
            internal bool enabled;
            internal string prefabName = string.Empty;
            internal string attachBone = string.Empty;
            internal InteractionAnimationVector3 localPosition;
            internal InteractionAnimationVector3 localEulerAngles;
            internal float localScale = 1f;
            internal float releaseSeconds;
        }

        [Serializable]
        [JsonObject(MemberSerialization.Fields)]
        internal sealed class LegacyClipPackManifest
        {
            internal bool enabled;
            internal string bundleFileName = string.Empty;
            internal string bundleInternalName = string.Empty;
            internal LegacyClipOverrideManifest[] overrides =
                Array.Empty<LegacyClipOverrideManifest>();
        }

        [Serializable]
        [JsonObject(MemberSerialization.Fields)]
        internal sealed class LegacyClipOverrideManifest
        {
            internal string slot = string.Empty;
            internal string clip = string.Empty;
        }

        [Serializable]
        [JsonObject(MemberSerialization.Fields)]
        internal sealed class LegacyValidationManifest
        {
            internal string generatedAt = string.Empty;
            internal string previewPixelCoverage = string.Empty;
            internal string meshTransfer = string.Empty;
            internal string socketNames = string.Empty;
            internal string cameraBounds = string.Empty;
        }
    }
}
