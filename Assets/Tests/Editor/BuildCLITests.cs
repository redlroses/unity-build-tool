using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace BuildTool.Editor.Tests
{
    [TestFixture]
    public class BuildCLITests
    {
        private MockBuildPipelineService _mockPipeline;

        [SetUp]
        public void SetUp()
        {
            _mockPipeline = new MockBuildPipelineService();
            BuildTool.PipelineService = _mockPipeline;
            BuildTool.RegisterRemoteHandler = () => Task.CompletedTask;
            BuildTool.RegisterLocalHandler = () => Task.CompletedTask;
            BuildTool.DisposeHandler = () => { };
        }

        [TearDown]
        public void TearDown()
        {
            BuildTool.ResetPipelineService();
            BuildTool.RegisterRemoteHandler = null;
            BuildTool.RegisterLocalHandler = null;
            BuildTool.DisposeHandler = null;
        }

        [Test]
        public void ParseCommandLineArgs_Profiles_ParsedCorrectly()
        {
            var args = new[] { "-dummy", "-profiles", "ProfileA, ProfileB; ProfileC" };
            var req = BuildCLI.ParseCommandLineArgs(args);

            Assert.AreEqual(3, req.ProfileNames.Count);
            Assert.Contains("ProfileA", req.ProfileNames);
            Assert.Contains("ProfileB", req.ProfileNames);
            Assert.Contains("ProfileC", req.ProfileNames);
        }

        [Test]
        public void ParseCommandLineArgs_Flags_ParsedCorrectly()
        {
            var args = new[]
            {
                "-dev",
                "-clean-build",
                "-clear",
                "-register",
                "-continueOnError",
                "-noOpenFolder"
            };

            var req = BuildCLI.ParseCommandLineArgs(args);

            Assert.IsTrue(req.IsDevelopment);
            Assert.IsTrue(req.CleanBuild);
            Assert.IsTrue(req.ClearFolderFirst);
            Assert.IsTrue(req.RegisterVersion);
            Assert.IsTrue(req.ContinueOnError);
            Assert.IsFalse(req.OpenFolder);
        }

        [Test]
        public void ParseCommandLineArgs_CleanBuild_ParsedCorrectly()
        {
            var argsA = new[] { "-clean-build" };
            var reqA = BuildCLI.ParseCommandLineArgs(argsA);
            Assert.IsTrue(reqA.CleanBuild);

            var argsB = new[] { "-cleanBuild" };
            var reqB = BuildCLI.ParseCommandLineArgs(argsB);
            Assert.IsTrue(reqB.CleanBuild);

            var argsC = new[] { "-clean" };
            var reqC = BuildCLI.ParseCommandLineArgs(argsC);
            Assert.IsTrue(reqC.CleanBuild);

            var argsD = new[] { "-clean-build", "false" };
            var reqD = BuildCLI.ParseCommandLineArgs(argsD);
            Assert.IsFalse(reqD.CleanBuild);

            var argsE = new[] { "-no-clean-build" };
            var reqE = BuildCLI.ParseCommandLineArgs(argsE);
            Assert.IsFalse(reqE.CleanBuild);

            var argsF = new[] { "-noCleanBuild" };
            var reqF = BuildCLI.ParseCommandLineArgs(argsF);
            Assert.IsFalse(reqF.CleanBuild);
        }

        [Test]
        public void ParseCommandLineArgs_OutDir_ParsedCorrectly()
        {
            var args = new[] { "-outDir", "Custom/Builds/Output" };
            var req = BuildCLI.ParseCommandLineArgs(args);

            Assert.AreEqual("Custom/Builds/Output", req.RootBuildPathOverride);
        }

        [Test]
        public void ParseCommandLineArgs_ExtensionArgs_ParsedCorrectly()
        {
            var args = new[]
            {
                "-extArgs", "zip=all;manifest=true",
                "-addressables", "ProfileWin",
                "-cleanAddressables"
            };

            var req = BuildCLI.ParseCommandLineArgs(args);

            Assert.AreEqual("all", req.GetExtensionArg("zip"));
            Assert.IsTrue(req.GetExtensionBool("manifest"));
            Assert.AreEqual("ProfileWin", req.GetExtensionArg("addressables"));
            Assert.IsTrue(req.GetExtensionBool("addressables.clean"));
        }

        [Test]
        public void ParseCommandLineArgs_ExplicitBooleanValues_ParsedCorrectly()
        {
            var args = new[]
            {
                "-dev", "false",
                "-clear", "false",
                "-open-folder", "false",
                "-register", "true"
            };

            var req = BuildCLI.ParseCommandLineArgs(args);

            Assert.IsFalse(req.IsDevelopment);
            Assert.IsFalse(req.ClearFolderFirst);
            Assert.IsFalse(req.OpenFolder);
            Assert.IsTrue(req.RegisterVersion);
        }

        [Test]
        public void ParseCommandLineArgs_NegativeFlagsAndAliases_ParsedCorrectly()
        {
            var args = new[]
            {
                "-no-open-folder",
                "-continue-on-error",
                "-clean-addressables",
                "-out-dir", "Custom/Builds/Output",
                "-ext", "archiver=true"
            };

            var req = BuildCLI.ParseCommandLineArgs(args);

            Assert.IsFalse(req.OpenFolder);
            Assert.IsTrue(req.ContinueOnError);
            Assert.IsTrue(req.GetExtensionBool("addressables.clean"));
            Assert.AreEqual("Custom/Builds/Output", req.RootBuildPathOverride);
            Assert.IsTrue(req.GetExtensionBool("archiver"));
        }

        [Test]
        public async Task BuildAsync_WithMockPipeline_ExecutesProfilesFromCliArgs()
        {
            var profile = ScriptableObject.CreateInstance<BuildProfile>();
            profile.name = "CliTestProfile";

            try
            {
                var args = new[]
                {
                    "-dev",
                    "-noOpenFolder",
                    "-outDir", Path.GetTempPath()
                };

                var req = BuildCLI.ParseCommandLineArgs(args);
                req.Profiles.Add(profile);

                var result = await Pipeline.BuildPipelineRunner.RunAsync(req);

                Assert.IsTrue(result.Success);
                Assert.AreEqual(1, result.TotalProfiles);
                Assert.AreEqual(1, _mockPipeline.Invocations.Count);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }
    }
}

