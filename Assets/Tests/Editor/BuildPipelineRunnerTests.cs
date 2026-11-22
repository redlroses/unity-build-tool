using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using BuildTool.Editor.Notifications;
using BuildTool.Editor.Pipeline;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BuildTool.Editor.Tests
{
    [TestFixture]
    public class BuildPipelineRunnerTests
    {
        private MockBuildPipelineService _mockPipeline;
        private string _tempTestDir;
        private bool _origNotificationsMasterEnabled;

        [SetUp]
        public void SetUp()
        {
            _origNotificationsMasterEnabled = BuildNotificationSettings.MasterEnabled;
            BuildNotificationSettings.MasterEnabled = false;

            _mockPipeline = new MockBuildPipelineService();
            BuildTool.PipelineService = _mockPipeline;

            BuildTool.RegisterRemoteHandler = () => Task.CompletedTask;
            BuildTool.RegisterLocalHandler = () => Task.CompletedTask;
            BuildTool.FinalizeRemoteReportHandler = _ => Task.CompletedTask;
            BuildTool.DisposeHandler = () => { };

            _tempTestDir = Path.Combine(Path.GetTempPath(), "BuildPipelineRunnerTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempTestDir);
        }

        [TearDown]
        public void TearDown()
        {
            BuildPipelineRunner.ClearActiveSession();
            BuildTool.ResetPipelineService();
            BuildTool.RegisterRemoteHandler = null;
            BuildTool.RegisterLocalHandler = null;
            BuildTool.FinalizeRemoteReportHandler = null;
            BuildTool.DisposeHandler = null;
            BuildNotificationSettings.MasterEnabled = _origNotificationsMasterEnabled;

            if (Directory.Exists(_tempTestDir))
            {
                try
                {
                    Directory.Delete(_tempTestDir, true);
                }
                catch { }
            }
        }

        [Test]
        public void RunAsync_WithNullRequest_ThrowsArgumentNullException()
        {
            Assert.ThrowsAsync<ArgumentNullException>(async () =>
            {
                await BuildPipelineRunner.RunAsync(null);
            });
        }

        [Test]
        public async Task RunAsync_WithEmptyProfiles_ReturnsFailedResult()
        {
            var req = new BuildExecutionRequest
            {
                ProfileNames = { "NonExistentProfile_XYZ" }
            };

            UnityEngine.TestTools.LogAssert.Expect(
                LogType.Error, "[BuildPipelineRunner] No valid build profiles found to execute.");
            var result = await BuildPipelineRunner.RunAsync(req);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(0, result.TotalProfiles);
            Assert.AreEqual(0, _mockPipeline.Invocations.Count);
        }

        [Test]
        public async Task RunAsync_SingleProfile_SuccessfulBuild_RecordsSuccess()
        {
            var profile = ScriptableObject.CreateInstance<BuildProfile>();
            profile.name = "TestProfile_Win";

            try
            {
                var req = new BuildExecutionRequest
                {
                    Profiles = { profile },
                    RootBuildPathOverride = _tempTestDir,
                    OpenFolder = false
                };

                var result = await BuildPipelineRunner.RunAsync(req);

                Assert.IsTrue(result.Success);
                Assert.AreEqual(1, result.TotalProfiles);
                Assert.AreEqual(1, result.SucceededProfiles);
                Assert.AreEqual(0, result.FailedProfiles);
                Assert.AreEqual(1, _mockPipeline.Invocations.Count);
                Assert.AreEqual(profile, _mockPipeline.Invocations[0].Profile);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public async Task RunAsync_MultipleProfiles_SequentialBuild_AllSucceed()
        {
            var profile1 = ScriptableObject.CreateInstance<BuildProfile>();
            profile1.name = "Profile_Alpha";

            var profile2 = ScriptableObject.CreateInstance<BuildProfile>();
            profile2.name = "Profile_Beta";

            try
            {
                var req = new BuildExecutionRequest
                {
                    Profiles = { profile1, profile2 },
                    RootBuildPathOverride = _tempTestDir,
                    OpenFolder = false
                };

                var result = await BuildPipelineRunner.RunAsync(req);

                Assert.IsTrue(result.Success);
                Assert.AreEqual(2, result.TotalProfiles);
                Assert.AreEqual(2, result.SucceededProfiles);
                Assert.AreEqual(0, result.FailedProfiles);
                Assert.AreEqual(2, _mockPipeline.Invocations.Count);
                Assert.AreEqual(profile1, _mockPipeline.Invocations[0].Profile);
                Assert.AreEqual(profile2, _mockPipeline.Invocations[1].Profile);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile1);
                UnityEngine.Object.DestroyImmediate(profile2);
            }
        }

        [Test]
        public async Task RunAsync_DevelopmentBuild_PassesDevelopmentOption()
        {
            var profile = ScriptableObject.CreateInstance<BuildProfile>();
            profile.name = "Profile_Dev";

            try
            {
                var req = new BuildExecutionRequest
                {
                    Profiles = { profile },
                    IsDevelopment = true,
                    RootBuildPathOverride = _tempTestDir,
                    OpenFolder = false
                };

                var result = await BuildPipelineRunner.RunAsync(req);

                Assert.IsTrue(result.Success);
                Assert.AreEqual(1, _mockPipeline.Invocations.Count);
                Assert.IsTrue((_mockPipeline.Invocations[0].Options.options & BuildOptions.Development) != 0);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public async Task RunAsync_CleanBuild_PassesCleanBuildCacheOption()
        {
            var profile = ScriptableObject.CreateInstance<BuildProfile>();
            profile.name = "Profile_Clean";

            try
            {
                var req = new BuildExecutionRequest
                {
                    Profiles = { profile },
                    CleanBuild = true,
                    RootBuildPathOverride = _tempTestDir,
                    OpenFolder = false
                };

                var result = await BuildPipelineRunner.RunAsync(req);

                Assert.IsTrue(result.Success);
                Assert.AreEqual(1, _mockPipeline.Invocations.Count);
                Assert.IsTrue((_mockPipeline.Invocations[0].Options.options & BuildOptions.CleanBuildCache) != 0);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public async Task RunAsync_DevAndCleanBuild_PassesCombinedOptions()
        {
            var profile = ScriptableObject.CreateInstance<BuildProfile>();
            profile.name = "Profile_DevClean";

            try
            {
                var req = new BuildExecutionRequest
                {
                    Profiles = { profile },
                    IsDevelopment = true,
                    CleanBuild = true,
                    RootBuildPathOverride = _tempTestDir,
                    OpenFolder = false
                };

                var result = await BuildPipelineRunner.RunAsync(req);

                Assert.IsTrue(result.Success);
                Assert.AreEqual(1, _mockPipeline.Invocations.Count);
                Assert.IsTrue((_mockPipeline.Invocations[0].Options.options & BuildOptions.Development) != 0);
                Assert.IsTrue((_mockPipeline.Invocations[0].Options.options & BuildOptions.CleanBuildCache) != 0);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public async Task RunAsync_ClearFolderFirst_DeletesExistingFilesAndDirectories()
        {
            var profile = ScriptableObject.CreateInstance<BuildProfile>();
            profile.name = "Profile_Clear";

            var testFile = Path.Combine(_tempTestDir, "old-artifact.txt");
            File.WriteAllText(testFile, "old data");
            var testSubDir = Path.Combine(_tempTestDir, "sub_dir");
            Directory.CreateDirectory(testSubDir);
            File.WriteAllText(Path.Combine(testSubDir, "child.txt"), "sub data");

            try
            {
                var req = new BuildExecutionRequest
                {
                    Profiles = { profile },
                    ClearFolderFirst = true,
                    RootBuildPathOverride = _tempTestDir,
                    OpenFolder = false
                };

                var result = await BuildPipelineRunner.RunAsync(req);

                Assert.IsTrue(result.Success);
                Assert.IsFalse(File.Exists(testFile), "Existing file should be deleted");
                Assert.IsFalse(Directory.Exists(testSubDir), "Existing sub directory should be deleted");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public async Task RunAsync_ProfileFailure_WithFailFast_AbortsRemainingProfiles()
        {
            var profile1 = ScriptableObject.CreateInstance<BuildProfile>();
            profile1.name = "Profile_Fail";

            var profile2 = ScriptableObject.CreateInstance<BuildProfile>();
            profile2.name = "Profile_Skip";

            _mockPipeline.CustomHandler = (opts, p) =>
            {
                if (p == profile1)
                {
                    return BuildPlayerResult.CreateFailed("Simulated compile error");
                }
                return BuildPlayerResult.CreateSuccessful(opts.locationPathName);
            };

            try
            {
                var req = new BuildExecutionRequest
                {
                    Profiles = { profile1, profile2 },
                    ContinueOnError = false,
                    RootBuildPathOverride = _tempTestDir,
                    OpenFolder = false
                };

                UnityEngine.TestTools.LogAssert.Expect(
                    LogType.Error,
                    "[BuildPipelineRunner] Build for profile 'Profile_Fail' ended with status 'Failed'. " +
                    "Aborting remaining builds (Fail-Fast).");
                var result = await BuildPipelineRunner.RunAsync(req);

                Assert.IsFalse(result.Success);
                Assert.AreEqual(1, result.FailedProfiles);
                Assert.AreEqual(0, result.SucceededProfiles);
                Assert.IsFalse(string.IsNullOrWhiteSpace(result.ErrorMessage));
                Assert.IsTrue(result.ErrorMessage.Contains("Profile_Fail"));
                Assert.AreEqual(
                    1, _mockPipeline.Invocations.Count, "Second profile should be aborted due to Fail-Fast");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile1);
                UnityEngine.Object.DestroyImmediate(profile2);
            }
        }

        [Test]
        public async Task RunAsync_ProfileFailure_WithContinueOnError_RunsAllProfilesAndRecordsFailures()
        {
            var profile1 = ScriptableObject.CreateInstance<BuildProfile>();
            profile1.name = "Profile_Fail";

            var profile2 = ScriptableObject.CreateInstance<BuildProfile>();
            profile2.name = "Profile_Pass";

            _mockPipeline.CustomHandler = (opts, p) =>
            {
                if (p == profile1)
                {
                    return BuildPlayerResult.CreateFailed("Simulated error");
                }
                return BuildPlayerResult.CreateSuccessful(opts.locationPathName);
            };

            try
            {
                var req = new BuildExecutionRequest
                {
                    Profiles = { profile1, profile2 },
                    ContinueOnError = true,
                    RootBuildPathOverride = _tempTestDir,
                    OpenFolder = false
                };

                var result = await BuildPipelineRunner.RunAsync(req);

                Assert.IsFalse(result.Success, "Overall result should be false when at least one profile fails");
                Assert.AreEqual(1, result.FailedProfiles);
                Assert.AreEqual(1, result.SucceededProfiles);
                Assert.IsFalse(string.IsNullOrWhiteSpace(result.ErrorMessage));
                Assert.AreEqual(
                    2, _mockPipeline.Invocations.Count, "Both profiles should run when ContinueOnError is true");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile1);
                UnityEngine.Object.DestroyImmediate(profile2);
            }
        }

        [Test]
        public async Task RunAsync_ProfileThrowsException_CaughtAndHandledGracefully()
        {
            var profile = ScriptableObject.CreateInstance<BuildProfile>();
            profile.name = "Profile_Throws";

            _mockPipeline.CustomHandler = (opts, p) =>
            {
                throw new InvalidOperationException("Fatal disk read error");
            };

            try
            {
                var req = new BuildExecutionRequest
                {
                    Profiles = { profile },
                    RootBuildPathOverride = _tempTestDir,
                    OpenFolder = false
                };

                UnityEngine.TestTools.LogAssert.Expect(
                    LogType.Error,
                    "[BuildPipelineRunner] Exception during build of profile 'Profile_Throws': Fatal disk read error");
                UnityEngine.TestTools.LogAssert.Expect(
                    LogType.Error, "[BuildPipelineRunner] Aborting remaining builds due to failure (Fail-Fast).");
                var result = await BuildPipelineRunner.RunAsync(req);

                Assert.IsFalse(result.Success);
                Assert.AreEqual(1, result.FailedProfiles);
                Assert.AreEqual("Failed", result.Results[0].Result);
                Assert.IsFalse(string.IsNullOrWhiteSpace(result.ErrorMessage));
                Assert.IsTrue(result.ErrorMessage.Contains("Fatal disk read error"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public async Task RunAsync_RegisterVersionTrue_InvokesRemoteRegistration()
        {
            var remoteRegistered = false;
            BuildTool.RegisterRemoteHandler = () =>
            {
                remoteRegistered = true;
                return Task.CompletedTask;
            };

            var profile = ScriptableObject.CreateInstance<BuildProfile>();
            profile.name = "Profile_Remote";

            try
            {
                var req = new BuildExecutionRequest
                {
                    Profiles = { profile },
                    RegisterVersion = true,
                    RootBuildPathOverride = _tempTestDir,
                    OpenFolder = false
                };

                var result = await BuildPipelineRunner.RunAsync(req);

                Assert.IsTrue(result.Success);
                Assert.IsTrue(
                    remoteRegistered, "Expected RegisterRemoteHandler to be invoked when RegisterVersion is true");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public async Task RunAsync_RegisterVersionFalse_InvokesLocalRegistration()
        {
            var localRegistered = false;
            BuildTool.RegisterLocalHandler = () =>
            {
                localRegistered = true;
                return Task.CompletedTask;
            };

            var profile = ScriptableObject.CreateInstance<BuildProfile>();
            profile.name = "Profile_Local";

            try
            {
                var req = new BuildExecutionRequest
                {
                    Profiles = { profile },
                    RegisterVersion = false,
                    RootBuildPathOverride = _tempTestDir,
                    OpenFolder = false
                };

                var result = await BuildPipelineRunner.RunAsync(req);

                Assert.IsTrue(result.Success);
                Assert.IsTrue(
                    localRegistered, "Expected RegisterLocalHandler to be invoked when RegisterVersion is false");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void RunAsync_ResultToJson_SerializesValidJson()
        {
            var result = new BuildExecutionResult
            {
                Success = true,
                TotalProfiles = 2,
                SucceededProfiles = 2,
                FailedProfiles = 0,
                BuildIndex = "12",
                RootBuildPath = "C:/Test/Path"
            };

            var json = result.ToJson(pretty: true);

            Assert.IsFalse(string.IsNullOrWhiteSpace(json));
            Assert.IsTrue(json.Contains("\"Success\": true"));
            Assert.IsTrue(json.Contains("\"BuildIndex\": \"12\""));
        }

        [Test]
        public async Task RunAsync_NoEnabledScenesWithUnityPipeline_FailsFast()
        {
            BuildTool.PipelineService = new UnityBuildPipelineService();

            var profile = ScriptableObject.CreateInstance<BuildProfile>();
            profile.name = "Profile_NoScenes";

            var originalScenes = EditorBuildSettings.scenes;
            EditorBuildSettings.scenes = Array.Empty<EditorBuildSettingsScene>();

            try
            {
                var req = new BuildExecutionRequest
                {
                    Profiles = { profile },
                    RegisterVersion = false,
                    RootBuildPathOverride = _tempTestDir,
                    OpenFolder = false
                };

                UnityEngine.TestTools.LogAssert.Expect(
                    LogType.Error,
                    "[BuildPipelineRunner] Profile 'Profile_NoScenes' has no enabled scenes to build. " +
                    "Add scenes to Build Profile or Editor Build Settings.");
                UnityEngine.TestTools.LogAssert.Expect(
                    LogType.Error, "[BuildPipelineRunner] Aborting remaining builds due to failure (Fail-Fast).");

                var result = await BuildPipelineRunner.RunAsync(req);

                Assert.IsFalse(result.Success);
                Assert.AreEqual(1, result.FailedProfiles);
                Assert.IsFalse(string.IsNullOrWhiteSpace(result.ErrorMessage));
            }
            finally
            {
                EditorBuildSettings.scenes = originalScenes;
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void IsSafeDirectoryToClear_RejectsParentAndProtectedFolders()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var parentOfProject = Directory.GetParent(projectRoot)?.FullName;

            Assert.IsFalse(BuildPipelineRunner.IsSafeDirectoryToClear(projectRoot));
            if (!string.IsNullOrEmpty(parentOfProject))
            {
                Assert.IsFalse(BuildPipelineRunner.IsSafeDirectoryToClear(parentOfProject));
            }
            Assert.IsFalse(BuildPipelineRunner.IsSafeDirectoryToClear(Application.dataPath));
            Assert.IsFalse(BuildPipelineRunner.IsSafeDirectoryToClear(Path.Combine(projectRoot, "Packages")));
            Assert.IsFalse(BuildPipelineRunner.IsSafeDirectoryToClear(Path.Combine(projectRoot, "ProjectSettings")));
            Assert.IsFalse(BuildPipelineRunner.IsSafeDirectoryToClear(Path.Combine(projectRoot, "Library")));
            Assert.IsFalse(BuildPipelineRunner.IsSafeDirectoryToClear(Path.Combine(projectRoot, "Temp")));
            Assert.IsFalse(BuildPipelineRunner.IsSafeDirectoryToClear(Path.Combine(projectRoot, ".git")));
        }

        [Test]
        public void IsSafeDirectoryToClear_AllowsSiblingAndOutputFolders()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            Assert.IsTrue(BuildPipelineRunner.IsSafeDirectoryToClear(Path.Combine(projectRoot, "AssetsBuilds")));
            Assert.IsTrue(BuildPipelineRunner.IsSafeDirectoryToClear(Path.Combine(projectRoot, "Build", "Release")));
            Assert.IsTrue(BuildPipelineRunner.IsSafeDirectoryToClear(_tempTestDir));
        }

        [Test]
        public async Task RunAsync_MultipleProfiles_ClearsActiveSessionOnCompletion()
        {
            var profile1 = ScriptableObject.CreateInstance<BuildProfile>();
            profile1.name = "Profile_Session1";

            var profile2 = ScriptableObject.CreateInstance<BuildProfile>();
            profile2.name = "Profile_Session2";

            try
            {
                var req = new BuildExecutionRequest
                {
                    Profiles = { profile1, profile2 },
                    RootBuildPathOverride = _tempTestDir,
                    OpenFolder = false
                };

                var result = await BuildPipelineRunner.RunAsync(req);

                Assert.IsTrue(result.Success);
                Assert.IsFalse(BuildPipelineRunner.IsBuildRunning);
                Assert.IsFalse(BuildPipelineRunner.HasActiveSession());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile1);
                UnityEngine.Object.DestroyImmediate(profile2);
            }
        }

        [Test]
        public void FormatSummaryTable_FromSummaries_FormatsProfileRows()
        {
            var result = new BuildExecutionResult
            {
                RootBuildPath = _tempTestDir,
                Results =
                {
                    new BuildExecutionResult.ProfileResultSummary
                    {
                        ProfileName = "Linux Standalone",
                        Result = "Succeeded",
                        DurationSeconds = 12.5,
                        TotalSize = 1048576,
                        TotalErrors = 0
                    },
                    new BuildExecutionResult.ProfileResultSummary
                    {
                        ProfileName = "Windows Server",
                        Result = "Failed",
                        DurationSeconds = 3.2,
                        TotalSize = 2048,
                        TotalErrors = 2
                    }
                }
            };

            var table = BuildReportFormatter.FormatSummaryTable(result.Results, _tempTestDir);

            Assert.IsTrue(table.Contains("Linux Standalone"));
            Assert.IsTrue(table.Contains("Succeeded"));
            Assert.IsTrue(table.Contains("1.00 MB") || table.Contains("1,00 MB"));
            Assert.IsTrue(table.Contains("Windows Server"));
            Assert.IsTrue(table.Contains("Failed"));
        }

        [Test]
        public void FormatBatchFromSummaries_WithSavedLog_IncludesHeaderAndLogContent()
        {
            var logFile = Path.Combine(_tempTestDir, "BuildReport_TestProfile.log");
            File.WriteAllText(logFile, "BUILD REPORT: TestProfile (StandaloneWindows64)");

            var result = new BuildExecutionResult
            {
                Success = true,
                TotalProfiles = 1,
                SucceededProfiles = 1,
                RootBuildPath = _tempTestDir,
                Results =
                {
                    new BuildExecutionResult.ProfileResultSummary
                    {
                        ProfileName = "TestProfile",
                        Result = "Succeeded",
                        DurationSeconds = 5.0,
                        TotalSize = 4096
                    }
                }
            };

            var batch = BuildReportFormatter.FormatBatchFromSummaries(result, new[] { logFile });

            Assert.IsTrue(batch.Contains("BATCH BUILD EXECUTION SUMMARY"));
            Assert.IsTrue(batch.Contains("BUILD REPORT: TestProfile (StandaloneWindows64)"));
        }
    }
}
