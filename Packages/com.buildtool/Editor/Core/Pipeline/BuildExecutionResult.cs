using System;
using System.Collections.Generic;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BuildTool.Editor.Pipeline
{
    [Serializable]
    public class BuildExecutionResult
    {
        public bool Success;
        public int TotalProfiles;
        public int SucceededProfiles;
        public int FailedProfiles;
        public int CancelledProfiles;
        public double TotalSeconds;
        public string RootBuildPath;
        public string BuildIndex;
        public string ErrorMessage;
        public List<ProfileResultSummary> Results = new();

        [NonSerialized]
        public List<(BuildProfile Profile, BuildReport Report)> Reports = new();

        public string ToJson(bool pretty = true)
        {
            return JsonUtility.ToJson(this, pretty);
        }

        [Serializable]
        public class ProfileResultSummary
        {
            public string ProfileName;
            public string Result;
            public int TotalErrors;
            public int TotalWarnings;
            public double DurationSeconds;
            public string OutputPath;
            public ulong TotalSize;
        }

        public static BuildExecutionResult CreateFailed(string errorMessage, string buildPath = null)
        {
            return new BuildExecutionResult
            {
                Success = false,
                ErrorMessage = errorMessage,
                RootBuildPath = buildPath,
                TotalProfiles = 0,
                SucceededProfiles = 0,
                FailedProfiles = 0,
                CancelledProfiles = 0
            };
        }
    }
}
