using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using BuildTool.Editor.Pipeline;
using BuildTool.Editor.Processors;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace BuildTool.Editor.Tests
{
    [TestFixture]
    public class BuildToolTests
    {
        private MockBuildPipelineService _mockPipeline;

        private class MockPreprocessor : IBuildPreprocessor
        {
            public bool WasCalled { get; private set; }
            public void PreProcess(BuildProfile profile) => WasCalled = true;
        }

        private class MockPostprocessor : IBuildPostprocessor
        {
            public bool WasCalled { get; private set; }
            public string PassedPath { get; private set; }
            public void PostProcess(BuildProfile profile, string buildPath)
            {
                WasCalled = true;
                PassedPath = buildPath;
            }
        }

        [SetUp]
        public void SetUp()
        {
            _mockPipeline = new MockBuildPipelineService();
            BuildTool.PipelineService = _mockPipeline;
        }

        [TearDown]
        public void TearDown()
        {
            BuildTool.ResetPipelineService();
        }

        [Test]
        public void GetRootBuildPath_DefaultPattern_ReplacesVersionAndProductName()
        {
            var path = BuildTool.GetRootBuildPath("7", new DateTime(2026, 1, 15, 10, 20, 0));

            Assert.IsFalse(string.IsNullOrWhiteSpace(path));
            Assert.IsTrue(path.Contains("7"));
            Assert.IsFalse(path.Contains("\\"), "Should normalize backslashes to forward slashes");
        }

        [Test]
        public void GetRootBuildPath_NormalizesBackslashesToForwardSlashes()
        {
            var path = BuildTool.GetRootBuildPath("99");
            Assert.IsFalse(path.Contains("\\"));
            Assert.IsFalse(path.Contains("//"));
        }

        [Test]
        public void GetBuildTarget_NullProfile_ReturnsActiveBuildTarget()
        {
            var target = BuildTool.GetBuildTarget(null);
            Assert.AreEqual(EditorUserBuildSettings.activeBuildTarget, target);
        }

        [Test]
        public void GetStandaloneBuildSubtarget_NullProfile_ReturnsStandaloneBuildSubtarget()
        {
            var subtarget = BuildTool.GetStandaloneBuildSubtarget(null);
            Assert.AreEqual(EditorUserBuildSettings.standaloneBuildSubtarget, subtarget);
        }

        [Test]
        public void GetBuildTargetGroup_ResolvesCorrectGroup()
        {
            var group = BuildTool.GetBuildTargetGroup(null);
            var expected = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
            Assert.AreEqual(expected, group);
        }

        [Test]
        public async Task ExecuteBuildFromProfile_CallsPreprocessorsAndPostprocessors()
        {
            var profile = ScriptableObject.CreateInstance<BuildProfile>();
            profile.name = "TestProfile_Hooks";

            var pre = new MockPreprocessor();
            var post = new MockPostprocessor();

            try
            {
                var result = await BuildTool.ExecuteBuildFromProfile(
                    profile,
                    BuildOptions.None,
                    "Build/TestHooks",
                    new[] { pre },
                    new[] { post }
                );

                Assert.IsTrue(result.Success);
                Assert.IsTrue(pre.WasCalled, "Preprocessor should have been called");
                Assert.IsTrue(post.WasCalled, "Postprocessor should have been called");
                Assert.AreEqual(1, _mockPipeline.Invocations.Count);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void PipelineService_CanBeReplacedAndReset()
        {
            var customService = new MockBuildPipelineService();
            BuildTool.PipelineService = customService;
            Assert.AreSame(customService, BuildTool.PipelineService);

            BuildTool.ResetPipelineService();
            Assert.IsInstanceOf<UnityBuildPipelineService>(BuildTool.PipelineService);
        }

        [Test]
        public void GetGlobalAppVersion_ReturnsNonEmptyString()
        {
            var version = BuildTool.GetGlobalAppVersion();
            Assert.IsFalse(string.IsNullOrWhiteSpace(version));
        }

        [Test]
        public void GetProfileVersion_NullProfile_ReturnsGlobalVersion()
        {
            var globalVer = BuildTool.GetGlobalAppVersion();
            var profileVer = BuildTool.GetProfileVersion(null);
            Assert.AreEqual(globalVer, profileVer);
        }

        [Test]
        public void GetProfileVersion_ProfileWithoutOverrides_ReturnsGlobalVersion()
        {
            var profile = ScriptableObject.CreateInstance<BuildProfile>();
            try
            {
                var globalVer = BuildTool.GetGlobalAppVersion();
                var profileVer = BuildTool.GetProfileVersion(profile);
                Assert.AreEqual(globalVer, profileVer);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }
    }
}

