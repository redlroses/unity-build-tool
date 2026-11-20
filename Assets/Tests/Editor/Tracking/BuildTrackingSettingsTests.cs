using System;
using System.IO;
using System.Threading.Tasks;
using BuildTool.Tracking.Editor;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BuildTool.Tracking.Editor.Tests
{
    public class BuildTrackingSettingsTests
    {
        [Test]
        public void DefaultBranchName_ReturnsBuildTracking()
        {
            var settings = ScriptableObject.CreateInstance<BuildTrackingSettings>();

            Assert.AreEqual("build-tracking", settings.DefaultBranchName);

            Object.DestroyImmediate(settings);
        }

        [Test]
        public void GetBranchName_WhenEmptyOverride_ReturnsDefaultBranchName()
        {
            var settings = ScriptableObject.CreateInstance<BuildTrackingSettings>();
            settings.ProjectBranchOverride = string.Empty;

            Assert.AreEqual("build-tracking", settings.GetBranchName());

            Object.DestroyImmediate(settings);
        }

        [Test]
        public void PruneOldBuildReports_KeepsLatestMaxHistoryEntries()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "BuildTool_PruneTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                for (var i = 1; i <= 5; i++)
                {
                    File.WriteAllText(Path.Combine(tempDir, $"BuildReport_{i}.log"), $"Report {i}");
                }

                BuildTracker.PruneOldBuildReports(tempDir, 3);

                Assert.IsFalse(File.Exists(Path.Combine(tempDir, "BuildReport_1.log")));
                Assert.IsFalse(File.Exists(Path.Combine(tempDir, "BuildReport_2.log")));
                Assert.IsTrue(File.Exists(Path.Combine(tempDir, "BuildReport_3.log")));
                Assert.IsTrue(File.Exists(Path.Combine(tempDir, "BuildReport_4.log")));
                Assert.IsTrue(File.Exists(Path.Combine(tempDir, "BuildReport_5.log")));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }

        [Test]
        public async Task GitRepository_CommitAndSingleOrphanWithMinusA_Succeeds()
        {
            var tempRepo = Path.Combine(Path.GetTempPath(), "BuildTool_GitTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRepo);

            try
            {
                var git = new GitRepository(tempRepo);
                await git.Init();
                await git.SendCommand("config user.name \"BuildTool Test\"", "Set name", "Failed set name");
                await git.SendCommand("config user.email \"test@example.com\"", "Set email", "Failed set email");

                File.WriteAllText(Path.Combine(tempRepo, "buildInfo.json"), "{\"lastBuildIndex\":1}");
                await git.Commit("first commit", "-A");

                File.WriteAllText(Path.Combine(tempRepo, "buildInfo.json"), "{\"lastBuildIndex\":2}");
                await git.CommitSingleOrphan("build-tracking", "orphan commit", "-A");

                var countOut = (await git.SendCommand("rev-list --count HEAD", "Counted", "Failed count")).Trim();
                Assert.AreEqual("1", countOut);
            }
            finally
            {
                try
                {
                    GitRepository.CleanupDirectory(tempRepo);
                }
                catch
                {
                }
            }
        }
    }
}
