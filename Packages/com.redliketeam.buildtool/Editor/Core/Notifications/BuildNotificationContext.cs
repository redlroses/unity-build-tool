using System;
using System.Globalization;
using BuildTool.Tracking;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BuildTool.Editor.Notifications
{
    /// <summary>
    /// Encapsulates all metadata and summary stats for a completed build event.
    /// Passed to each registered INotificationChannel.
    /// </summary>
    public class BuildNotificationContext
    {
        public BuildReport Report { get; }
        public BuildInfo Info { get; }
        public string ProjectName { get; }
        public string TargetPlatform { get; }
        public BuildResult Result { get; }
        public bool IsSuccess => Result == BuildResult.Succeeded;
        public TimeSpan Duration { get; }
        public ulong TotalSizeBytes { get; }
        public string FormattedSize { get; }
        public string FormattedDuration { get; }
        public int TotalErrors { get; }
        public int TotalWarnings { get; }
        public string VersionText { get; }
        public string CommitText { get; }
        public string OutputPath { get; }
        public DateTime Timestamp { get; }

        public BuildNotificationContext(BuildReport report, BuildInfo info)
        {
            Report = report;
            Info = info;
            ProjectName = Application.productName;
            Timestamp = DateTime.UtcNow;

            if (report != null)
            {
                Result = report.summary.result;
                Duration = report.summary.totalTime;
                TotalSizeBytes = report.summary.totalSize;
                TotalErrors = report.summary.totalErrors;
                TotalWarnings = report.summary.totalWarnings;
                OutputPath = report.summary.outputPath;

                var subTarget = EditorUserBuildSettings.standaloneBuildSubtarget != StandaloneBuildSubtarget.Player
                    ? $" ({EditorUserBuildSettings.standaloneBuildSubtarget})"
                    : string.Empty;
                TargetPlatform = $"{report.summary.platform}{subTarget}";
            }
            else
            {
                Result = BuildResult.Unknown;
                Duration = TimeSpan.Zero;
                TotalSizeBytes = 0;
                TotalErrors = 0;
                TotalWarnings = 0;
                OutputPath = string.Empty;
                TargetPlatform = EditorUserBuildSettings.activeBuildTarget.ToString();
            }

            FormattedSize = FormatBytes(TotalSizeBytes);
            FormattedDuration = FormatDuration(Duration);

            VersionText = info != null && !string.IsNullOrEmpty(info.Index)
                ? $"v{Application.version}.{info.Index}"
                : $"v{Application.version}";

            CommitText = info != null && !string.IsNullOrEmpty(info.Commit)
                ? info.Commit
                : "N/A";
        }

        public string GetStatusEmoji()
        {
            return Result switch
            {
                BuildResult.Succeeded => "🟢",
                BuildResult.Failed => "🔴",
                BuildResult.Cancelled => "🟡",
                _ => "⚪"
            };
        }

        public string GetStatusTitle()
        {
            return Result switch
            {
                BuildResult.Succeeded => "Build Succeeded",
                BuildResult.Failed => "Build Failed",
                BuildResult.Cancelled => "Build Cancelled",
                _ => "Build Finished"
            };
        }

        public static string FormatBytes(ulong bytes)
        {
            if (bytes == 0)
            {
                return "0 B";
            }

            const double Kb = 1024.0;
            const double Mb = 1024.0 * 1024.0;
            const double Gb = 1024.0 * 1024.0 * 1024.0;

            if (bytes >= Gb)
            {
                return $"{(bytes / Gb).ToString("F2", CultureInfo.InvariantCulture)} GB";
            }
            if (bytes >= Mb)
            {
                return $"{(bytes / Mb).ToString("F1", CultureInfo.InvariantCulture)} MB";
            }
            if (bytes >= Kb)
            {
                return $"{(bytes / Kb).ToString("F1", CultureInfo.InvariantCulture)} KB";
            }

            return $"{bytes} B";
        }

        public static string FormatDuration(TimeSpan time)
        {
            if (time.TotalHours >= 1)
            {
                return $"{(int)time.TotalHours}h {time.Minutes}m {time.Seconds}s";
            }
            if (time.TotalMinutes >= 1)
            {
                return $"{time.Minutes}m {time.Seconds}s";
            }

            return $"{time.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)}s";
        }
    }
}
