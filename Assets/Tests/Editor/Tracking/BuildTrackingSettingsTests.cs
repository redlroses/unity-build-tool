using System;
using System.IO;
using System.Threading.Tasks;
using BuildTool.Tracking.Editor;
using NUnit.Framework;

namespace BuildTool.Tracking.Editor.Tests
{
    public class BuildTrackingSettingsTests
    {
        private string _origBranchOverride;
        private string _origRepoUrlOverride;

        [SetUp]
        public void SetUp()
        {
            var settings = BuildTrackingSettings.instance;
            _origBranchOverride = settings.ProjectBranchOverride;
            _origRepoUrlOverride = settings.GitRepositoryUrlOverride;
        }

        [TearDown]
        public void TearDown()
        {
            var settings = BuildTrackingSettings.instance;
            settings.ProjectBranchOverride = _origBranchOverride;
            settings.GitRepositoryUrlOverride = _origRepoUrlOverride;
        }

        [Test]
        public void DefaultBranchName_ReturnsBuildTracking()
        {
            var settings = BuildTrackingSettings.instance;

            Assert.AreEqual("build-tracking", settings.DefaultBranchName);
        }

        [Test]
        public void GetBranchName_WhenEmptyOverride_ReturnsDefaultBranchName()
        {
            var settings = BuildTrackingSettings.instance;
            settings.ProjectBranchOverride = string.Empty;

            Assert.AreEqual("build-tracking", settings.GetBranchName());
        }

        [Test]
        public void NormalizeGitUrl_VariousFormats_ReturnsCanonical()
        {
            var expected = "https://github.com/redlroses/unity-build-tool";
            Assert.AreEqual(expected, GitRepository.NormalizeGitUrl("https://github.com/redlroses/unity-build-tool"));
            Assert.AreEqual(expected, GitRepository.NormalizeGitUrl("https://github.com/redlroses/unity-build-tool/"));
            Assert.AreEqual(expected, GitRepository.NormalizeGitUrl("https://github.com/redlroses/unity-build-tool.git"));
            Assert.AreEqual(expected, GitRepository.NormalizeGitUrl("https://github.com/redlroses/unity-build-tool.git/"));
            Assert.AreEqual(expected, GitRepository.NormalizeGitUrl("  https://github.com/redlroses/unity-build-tool.git/  "));
        }

        [Test]
        public void GetRepositoryUrlOverride_WhenEmptyOrNull_ReturnsEmptyString()
        {
            var settings = BuildTrackingSettings.instance;
            settings.GitRepositoryUrlOverride = null;
            Assert.AreEqual(string.Empty, settings.GetRepositoryUrlOverride());

            settings.GitRepositoryUrlOverride = "   ";
            Assert.AreEqual(string.Empty, settings.GetRepositoryUrlOverride());
        }

        [Test]
        public void GetRepositoryUrlOverride_WhenSet_ReturnsTrimmedValue()
        {
            var settings = BuildTrackingSettings.instance;
            settings.GitRepositoryUrlOverride = "  https://github.com/external/repo.git  ";

            Assert.AreEqual("https://github.com/external/repo.git", settings.GetRepositoryUrlOverride());
        }

        [Test]
        public void RepositoryUrlOverride_Property_SynchronizedWithField()
        {
            var settings = BuildTrackingSettings.instance;
            settings.RepositoryUrlOverride = "git@github.com:external/repo.git";

            Assert.AreEqual("git@github.com:external/repo.git", settings.GitRepositoryUrlOverride);
            Assert.AreEqual("git@github.com:external/repo.git", settings.RepositoryUrlOverride);
        }

        [Test]
        public async Task GetTrackingRepositoryUrl_WhenOverrideSet_ReturnsOverrideUrl()
        {
            var settings = BuildTrackingSettings.instance;
            settings.GitRepositoryUrlOverride = "https://github.com/override/repo.git";

            var url = await settings.GetTrackingRepositoryUrl();
            Assert.AreEqual("https://github.com/override/repo.git", url);
        }

        [Test]
        public async Task GitRepository_ExternalRemote_PushAndFetch_Succeeds()
        {
            var rootTemp = Path.Combine(Path.GetTempPath(), "BuildTool_ExtGit_" + Guid.NewGuid().ToString("N"));
            var bareRemotePath = Path.Combine(rootTemp, "remote.git");
            var localWorkPath = Path.Combine(rootTemp, "work");

            Directory.CreateDirectory(bareRemotePath);
            Directory.CreateDirectory(localWorkPath);

            try
            {
                var bareGit = new GitRepository(bareRemotePath);
                await bareGit.SendCommand("init --bare", "Init bare", "Failed init bare", ignoreRepoExist: true);

                var localGit = new GitRepository(localWorkPath);
                await localGit.Init();
                await localGit.SendCommand("config user.name \"BuildTool Test\"", "Set name", "Failed set name");
                await localGit.SendCommand("config user.email \"test@example.com\"", "Set email", "Failed set email");
                await localGit.SendCommand($"remote add origin \"{bareRemotePath.Replace('\\', '/')}\"", "Add origin", "Failed add origin");

                File.WriteAllText(Path.Combine(localWorkPath, "buildInfo.json"), "{\"lastBuildIndex\":1}");
                await localGit.CommitSingleOrphan("build-tracking", "Initial tracking commit", "-A");
                await localGit.Push("build-tracking");

                var remoteBranchCheck = await bareGit.SendCommand("branch --list build-tracking", "List branch", "Failed list", ignoreRepoExist: true);
                Assert.IsTrue(remoteBranchCheck.Contains("build-tracking"));
            }
            finally
            {
                GitRepository.CleanupDirectory(rootTemp);
            }
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
