using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Y4NGZInteractions.Examples.Editor;

namespace Y4NGZInteractions.Examples.EditorTests
{
    public sealed class Y4NGZInteractionContractValidatorTests
    {
        private const string TempRoot = "Assets/GeneratedExampleTestTemp";

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempRoot))
                AssetDatabase.CreateFolder("Assets", "GeneratedExampleTestTemp");
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(TempRoot);
            foreach (GameObject value in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (value.scene.IsValid() && value.name.StartsWith("ContractTest", StringComparison.Ordinal))
                    UnityEngine.Object.DestroyImmediate(value);
            }
        }

        [Test]
        public void MissingControllerIsReported()
        {
            Assert.That(
                Y4NGZInteractionContractValidator.ValidateController(
                    null,
                    new[] { "Base Layer" },
                    Array.Empty<Y4NGZInteractionContractValidator.ParameterRequirement>()),
                Does.Contain("controller.missing"));
        }

        [Test]
        public void MissingLayerAndParameterAreReported()
        {
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(
                TempRoot + "/Empty.controller");
            var requirements = new[]
            {
                new Y4NGZInteractionContractValidator.ParameterRequirement(
                    "RequiredBool", AnimatorControllerParameterType.Bool)
            };

            var issues = Y4NGZInteractionContractValidator.ValidateController(
                controller, new[] { "MissingLayer" }, requirements);

            Assert.That(issues, Does.Contain("controller.layer_missing:MissingLayer"));
            Assert.That(issues, Does.Contain("controller.parameter_missing:RequiredBool"));
        }

        [Test]
        public void MissingBodyBoneIsReported()
        {
            var root = new GameObject("ContractTestBody");
            var issues = Y4NGZInteractionContractValidator.ValidateTransformPaths(
                root.transform,
                new[] { "spine.004/shoulder.R/arm.R" },
                "body");

            Assert.That(issues.Single(), Does.StartWith("body.path_missing:"));
        }

        [Test]
        public void MissingCameraAnchorIsReported()
        {
            var root = new GameObject("ContractTestViewmodel");
            var issues = Y4NGZInteractionContractValidator.ValidateCameraAnchor(
                root.transform, "Rig/CameraAnchor");

            Assert.That(issues.Single(), Does.StartWith("camera_anchor.path_missing:"));
        }

        [Test]
        public void MissingRendererComponentIsReported()
        {
            var root = new GameObject("ContractTestRendererRoot");
            var child = new GameObject("Visible");
            child.transform.SetParent(root.transform, false);

            var issues = Y4NGZInteractionContractValidator.ValidateRendererPaths(
                root.transform, new[] { "Visible" });

            Assert.That(issues, Does.Contain("renderer.component_missing:Visible"));
        }

        [Test]
        public void MissingPropBoneIsReported()
        {
            var root = new GameObject("ContractTestPropRoot");
            var issues = Y4NGZInteractionContractValidator.ValidatePropAttachment(
                root.transform, "Rig/Hand/PropSocket");

            Assert.That(issues.Single(), Does.StartWith("prop_attachment.path_missing:"));
        }

        [Test]
        public void MissingClipBindingPathIsReported()
        {
            var root = new GameObject("ContractTestClipRoot");
            var clip = new AnimationClip();
            clip.SetCurve(
                "Rig/MissingArm",
                typeof(Transform),
                "localPosition.x",
                AnimationCurve.Linear(0f, 0f, 1f, 1f));

            var issues = Y4NGZInteractionContractValidator.ValidateClipBindings(
                clip, root.transform);

            Assert.That(issues, Does.Contain("clip.binding_path_missing:Rig/MissingArm"));
        }

        [Test]
        public void InvalidTransformPathsAreRejected()
        {
            Assert.That(
                Y4NGZInteractionContractValidator.IsCanonicalPath("../Rig/Arm"),
                Is.False);
            Assert.That(
                Y4NGZInteractionContractValidator.IsCanonicalPath("/Rig/Arm"),
                Is.False);
            Assert.That(
                Y4NGZInteractionContractValidator.IsCanonicalPath("Rig//Arm"),
                Is.False);
        }

        [Test]
        public void BuildAllExamplesProducesBothBundlesAndManifests()
        {
            Y4NGZInteractionExampleBuilder.BuildAllExamples();
            string output = Path.GetFullPath(
                Path.Combine(Application.dataPath, "../../GeneratedBundles"));

            Assert.That(File.Exists(Path.Combine(output, "body/example_body")), Is.True);
            Assert.That(
                File.Exists(Path.Combine(output, "viewmodel/example_viewmodel")),
                Is.True);
            Assert.That(
                File.Exists(Path.Combine(output, "body-world.manifest.json")),
                Is.True);
            Assert.That(
                File.Exists(Path.Combine(output, "local-viewmodel.manifest.json")),
                Is.True);
        }
    }
}
