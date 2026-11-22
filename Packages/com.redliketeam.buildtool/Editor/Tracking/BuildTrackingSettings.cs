using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace BuildTool.Tracking.Editor
{
    [FilePath("ProjectSettings/BuildTrackingSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public class BuildTrackingSettings : ScriptableSingleton<BuildTrackingSettings>
    {
        public const string RepositoryPath = "Temp/build-tracking";
        public const string BuildInfoFileName = "buildInfo.json";
        public const string LastBuildIndexKey = "lastBuildIndex";
        public const string HistoryKey = "history";
        public const string ReportsDirectoryName = "Reports";
        public const string BuildInfoAssetPath = "Assets/Resources/BuildInfo.asset";
        public const string InitialRepositoryPath = "Temp/initial-tracking";

        public string ProjectBranchOverride = "build-tracking";
        public string RelativeProjectGitPathOverride;
        public string GitRepositoryUrlOverride;
        public bool KeepSingleCommit = true;
        public int PushRetryAttempts = 3;
        public int PushRetryDelayMs = 1000;
        public int MaxHistoryEntries = 0;
        public bool IsLogging;

        public string RepositoryUrlOverride
        {
            get => GitRepositoryUrlOverride;
            set => GitRepositoryUrlOverride = value;
        }

        public string DefaultBranchName => ValidateAndFixBranchName("build-tracking");

        private void OnValidate()
        {
            ProjectBranchOverride = ValidateAndFixBranchName(ProjectBranchOverride);
            if (!string.IsNullOrEmpty(GitRepositoryUrlOverride))
            {
                GitRepositoryUrlOverride = GitRepositoryUrlOverride.Trim();
            }
        }

        public void Save()
        {
            Save(true);
        }

        public string GetBranchName()
        {
            return string.IsNullOrEmpty(ProjectBranchOverride) ? DefaultBranchName : ProjectBranchOverride;
        }

        public string GetProjectGitPath()
        {
            return string.IsNullOrEmpty(RelativeProjectGitPathOverride)
                ? Directory.GetCurrentDirectory()
                : Path.Combine(Directory.GetCurrentDirectory(), RelativeProjectGitPathOverride);
        }

        public string GetRepositoryUrlOverride()
        {
            return string.IsNullOrWhiteSpace(GitRepositoryUrlOverride) ? string.Empty : GitRepositoryUrlOverride.Trim();
        }

        public async Task<string> GetTrackingRepositoryUrl(GitRepository projectGit = null)
        {
            if (!string.IsNullOrWhiteSpace(GitRepositoryUrlOverride))
            {
                return GitRepositoryUrlOverride.Trim();
            }

            var git = projectGit ?? new GitRepository(GetProjectGitPath(), IsLogging);
            if (git.HasRepository() && await git.HasOrigin())
            {
                return await git.GetRepositoryUrl();
            }

            return string.Empty;
        }

        private string ValidateAndFixBranchName(string branchName)
        {
            branchName = branchName?.Trim('/', '-') ?? "";
            branchName = new string(branchName.Where(c => c >= 32 && c != 127).ToArray());
            branchName = Regex.Replace(branchName, @"\.\.+", "-");
            branchName = Regex.Replace(branchName, @"[ ~\^:?*\[\]@\\]", "-");
            branchName = Regex.Replace(branchName, @"\.lock$", "");

            return Regex.Replace(branchName, @"/+", "/");
        }
    }
}
