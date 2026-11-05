using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
        public bool KeepSingleCommit = true;
        public int PushRetryAttempts = 3;
        public int PushRetryDelayMs = 1000;
        public int MaxHistoryEntries = 0;
        public bool IsLogging;

        public string DefaultBranchName => ValidateAndFixBranchName("build-tracking");

        private void OnValidate()
        {
            ProjectBranchOverride = ValidateAndFixBranchName(ProjectBranchOverride);
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

        private string ValidateAndFixBranchName(string branchName)
        {
            branchName = branchName?.Trim('/') ?? "";
            branchName = new string(branchName.Where(c => c >= 32 && c != 127).ToArray());
            branchName = Regex.Replace(branchName, @"\.\.+", "-");
            branchName = Regex.Replace(branchName, @"[ ~\^:?*\[\]@\\]", "-");
            branchName = Regex.Replace(branchName, @"\.lock$", "");

            return Regex.Replace(branchName, @"/+", "/");
        }
    }
}
