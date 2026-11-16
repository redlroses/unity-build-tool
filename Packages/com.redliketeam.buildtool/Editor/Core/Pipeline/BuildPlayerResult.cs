using System;
using UnityEditor.Build.Reporting;

namespace BuildTool.Editor.Pipeline
{
    /// <summary>
    /// Encapsulates the result of a profile build execution.
    /// Provides an abstraction over Unity's native BuildReport to facilitate unit testing and decoupled reporting.
    /// </summary>
    public class BuildPlayerResult
    {
        public bool Success { get; set; }
        public BuildResult Result { get; set; } = BuildResult.Succeeded;
        public int TotalErrors { get; set; }
        public int TotalWarnings { get; set; }
        public TimeSpan Duration { get; set; }
        public ulong TotalSize { get; set; }
        public string OutputPath { get; set; }
        public string ErrorMessage { get; set; }
        public BuildReport Report { get; set; }

        public static BuildPlayerResult FromReport(BuildReport report)
        {
            if (report == null)
            {
                return new BuildPlayerResult
                {
                    Success = false,
                    Result = BuildResult.Failed
                };
            }

            return new BuildPlayerResult
            {
                Report = report,
                Success = report.summary.result == BuildResult.Succeeded,
                Result = report.summary.result,
                TotalErrors = report.summary.totalErrors,
                TotalWarnings = report.summary.totalWarnings,
                Duration = report.summary.totalTime,
                TotalSize = report.summary.totalSize,
                OutputPath = report.summary.outputPath
            };
        }

        public static BuildPlayerResult CreateSuccessful(
            string outputPath = "",
            ulong totalSize = 1024,
            TimeSpan? duration = null)
        {
            return new BuildPlayerResult
            {
                Success = true,
                Result = BuildResult.Succeeded,
                OutputPath = outputPath,
                TotalSize = totalSize,
                Duration = duration ?? TimeSpan.FromSeconds(1)
            };
        }

        public static BuildPlayerResult CreateFailed(string error = "Mock build failure", int totalErrors = 1)
        {
            return new BuildPlayerResult
            {
                Success = false,
                Result = BuildResult.Failed,
                ErrorMessage = error,
                TotalErrors = totalErrors,
                Duration = TimeSpan.FromMilliseconds(500)
            };
        }
    }
}
