using System;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Y4NGZInteractions.Examples.Editor
{
    public static class Y4NGZInteractionExampleBuilder
    {
        public const string SourceRoot = "Assets/GeneratedExampleSources";
        public const string BodyControllerPath = SourceRoot + "/ExampleBodyController.controller";
        public const string BodyClipPath = SourceRoot + "/Example_Wave.anim";
        public const string ViewmodelControllerPath =
            SourceRoot + "/ExampleViewmodelController.controller";
        public const string ViewmodelClipPath = SourceRoot + "/Example_Inspect.anim";
        public const string ViewmodelPrefabPath = SourceRoot + "/ExampleViewmodel.prefab";
        public const string BodyBundleName = "body/example_body";
        public const string ViewmodelBundleName = "viewmodel/example_viewmodel";

        [MenuItem("Y4NGZ Interactions/Create Clean-Room Sources")]
        public static void CreateCleanRoomSources()
        {
            EnsureFolder("Assets", "GeneratedExampleSources");
            DeleteGeneratedAssets();

            AnimationClip bodyClip = CreateBodyClip();
            AssetDatabase.CreateAsset(bodyClip, BodyClipPath);
            AnimatorController bodyController = CreateController(
                BodyControllerPath, bodyClip, "ExampleBody");
            AssignBundle(BodyControllerPath, BodyBundleName);
            AssignBundle(BodyClipPath, BodyBundleName);

            AnimationClip viewmodelClip = CreateViewmodelClip();
            AssetDatabase.CreateAsset(viewmodelClip, ViewmodelClipPath);
            AnimatorController viewmodelController = CreateController(
                ViewmodelControllerPath, viewmodelClip, "ExampleViewmodel");
            GameObject viewmodel = CreateViewmodelPrefab(viewmodelController);
            PrefabUtility.SaveAsPrefabAsset(viewmodel, ViewmodelPrefabPath);
            UnityEngine.Object.DestroyImmediate(viewmodel);
            AssignBundle(ViewmodelControllerPath, ViewmodelBundleName);
            AssignBundle(ViewmodelClipPath, ViewmodelBundleName);
            AssignBundle(ViewmodelPrefabPath, ViewmodelBundleName);

            CreateBodyProxyPrefab();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Created original primitive BodyWorld and local-viewmodel sources.");
        }

        [MenuItem("Y4NGZ Interactions/Build All Examples")]
        public static void BuildAllExamples()
        {
            CreateCleanRoomSources();
            Y4NGZInteractionContractValidator.ValidateAllOrThrow();

            string output = Path.GetFullPath(
                Path.Combine(Application.dataPath, "../../GeneratedBundles"));
            Directory.CreateDirectory(output);
            BuildPipeline.BuildAssetBundles(
                output,
                BuildAssetBundleOptions.ChunkBasedCompression |
                BuildAssetBundleOptions.StrictMode,
                BuildTarget.StandaloneWindows64);

            File.WriteAllText(
                Path.Combine(output, "body-world.manifest.json"),
                BodyManifestJson());
            File.WriteAllText(
                Path.Combine(output, "local-viewmodel.manifest.json"),
                ViewmodelManifestJson());
            AssetDatabase.Refresh();
            Debug.Log("Built public example bundles and schema-2 manifests: " + output);
        }

        [MenuItem("Y4NGZ Interactions/Validate Selected Payload")]
        public static void ValidateSelectedPayload()
        {
            Y4NGZInteractionContractValidator.ValidateSelectionOrThrow();
            Debug.Log("Selected payload passed the public authoring contract checks.");
        }

        private static void DeleteGeneratedAssets()
        {
            string[] paths =
            {
                BodyControllerPath,
                BodyClipPath,
                ViewmodelControllerPath,
                ViewmodelClipPath,
                ViewmodelPrefabPath,
                SourceRoot + "/BodyWorldProxy.prefab"
            };
            foreach (string path in paths)
            {
                if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                    AssetDatabase.DeleteAsset(path);
            }
        }

        private static AnimationClip CreateBodyClip()
        {
            var clip = new AnimationClip
            {
                name = "Example_Wave",
                frameRate = 30f
            };
            var curve = AnimationCurve.EaseInOut(0f, 0f, 0.55f, 55f);
            curve.AddKey(new Keyframe(1.1f, 0f));
            clip.SetCurve(
                "spine.004/shoulder.R/arm.R",
                typeof(Transform),
                "localEulerAnglesRaw.z",
                curve);
            return clip;
        }

        private static AnimationClip CreateViewmodelClip()
        {
            var clip = new AnimationClip
            {
                name = "Example_Inspect",
                frameRate = 30f
            };
            var curve = AnimationCurve.EaseInOut(0f, 0f, 0.4f, 35f);
            curve.AddKey(new Keyframe(0.8f, 0f));
            clip.SetCurve(
                "Rig/Arm",
                typeof(Transform),
                "localEulerAnglesRaw.y",
                curve);
            clip.SetCurve(
                "Rig/Prop",
                typeof(Transform),
                "localPosition.z",
                AnimationCurve.EaseInOut(0f, 0.35f, 0.8f, 0.55f));
            return clip;
        }

        private static AnimatorController CreateController(
            string path,
            AnimationClip clip,
            string name)
        {
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            controller.name = name + "Controller";
            controller.AddParameter("InteractionActive", AnimatorControllerParameterType.Bool);
            controller.AddParameter("Enter", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Exit", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("MovementState", AnimatorControllerParameterType.Int);
            controller.AddParameter("Blend", AnimatorControllerParameterType.Float);
            AnimatorState state = controller.layers[0].stateMachine.AddState("Interaction");
            state.motion = clip;
            controller.layers[0].stateMachine.defaultState = state;
            return controller;
        }

        private static GameObject CreateViewmodelPrefab(AnimatorController controller)
        {
            var root = new GameObject("ExampleViewmodel");
            Animator animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;

            Transform rig = NewChild(root.transform, "Rig");
            Transform anchor = NewChild(rig, "CameraAnchor");
            anchor.localPosition = Vector3.zero;
            Transform arm = PrimitiveChild(rig, "Arm", PrimitiveType.Cube);
            arm.localPosition = new Vector3(0.18f, -0.18f, 0.45f);
            arm.localScale = new Vector3(0.12f, 0.12f, 0.5f);
            Transform prop = PrimitiveChild(rig, "Prop", PrimitiveType.Cylinder);
            prop.localPosition = new Vector3(0f, -0.08f, 0.55f);
            prop.localScale = new Vector3(0.08f, 0.18f, 0.08f);
            return root;
        }

        private static void CreateBodyProxyPrefab()
        {
            var root = new GameObject("BodyWorldProxy");
            Transform spine = NewChild(root.transform, "spine.004");
            Transform shoulderRight = NewChild(spine, "shoulder.R");
            Transform armRight = PrimitiveChild(shoulderRight, "arm.R", PrimitiveType.Capsule);
            Transform forearmRight = NewChild(armRight, "forearm.R");
            PrimitiveChild(forearmRight, "hand.R", PrimitiveType.Cube);
            Transform shoulderLeft = NewChild(spine, "shoulder.L");
            Transform armLeft = PrimitiveChild(shoulderLeft, "arm.L", PrimitiveType.Capsule);
            Transform forearmLeft = NewChild(armLeft, "forearm.L");
            PrimitiveChild(forearmLeft, "hand.L", PrimitiveType.Cube);
            PrimitiveChild(spine, "head", PrimitiveType.Sphere);
            PrimitiveChild(root.transform, "thigh.R", PrimitiveType.Capsule);
            PrimitiveChild(root.transform, "thigh.L", PrimitiveType.Capsule);
            PrefabUtility.SaveAsPrefabAsset(root, SourceRoot + "/BodyWorldProxy.prefab");
            UnityEngine.Object.DestroyImmediate(root);
        }

        private static Transform NewChild(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static Transform PrimitiveChild(
            Transform parent,
            string name,
            PrimitiveType primitiveType)
        {
            GameObject child = GameObject.CreatePrimitive(primitiveType);
            child.name = name;
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static void AssignBundle(string assetPath, string bundleName)
        {
            AssetImporter importer = AssetImporter.GetAtPath(assetPath);
            if (importer == null)
                throw new InvalidOperationException("Missing importer: " + assetPath);
            importer.assetBundleName = bundleName;
        }

        private static void EnsureFolder(string parent, string name)
        {
            string path = parent + "/" + name;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, name);
        }

        private static string BodyManifestJson()
        {
            return "{\n" +
                   "  \"schemaVersion\": 2,\n" +
                   "  \"interactionId\": \"example-body-wave\",\n" +
                   "  \"durationSeconds\": 1.1,\n" +
                   "  \"body\": {\n" +
                   "    \"enabled\": true,\n" +
                   "    \"bundleFileName\": \"body/example_body\",\n" +
                   "    \"controllerAssetName\": \"ExampleBodyController\",\n" +
                   "    \"activeBool\": \"InteractionActive\",\n" +
                   "    \"enterTrigger\": \"Enter\",\n" +
                   "    \"exitTrigger\": \"Exit\",\n" +
                   "    \"preserveGameplayCamera\": true,\n" +
                   "    \"stopOnVanillaSpecialAnimation\": true\n" +
                   "  }\n" +
                   "}\n";
        }

        private static string ViewmodelManifestJson()
        {
            return "{\n" +
                   "  \"schemaVersion\": 2,\n" +
                   "  \"interactionId\": \"example-local-inspect\",\n" +
                   "  \"durationSeconds\": 0.8,\n" +
                   "  \"localViewmodel\": {\n" +
                   "    \"bundleFileName\": \"viewmodel/example_viewmodel\",\n" +
                   "    \"prefabAssetName\": \"ExampleViewmodel\",\n" +
                   "    \"controllerAssetName\": \"ExampleViewmodelController\",\n" +
                   "    \"cameraAnchorPath\": \"Rig/CameraAnchor\",\n" +
                   "    \"hideVanillaFirstPersonArms\": true,\n" +
                   "    \"prefabRenderersToShow\": [\"Rig/Arm\", \"Rig/Prop\"]\n" +
                   "  }\n" +
                   "}\n";
        }
    }
}
