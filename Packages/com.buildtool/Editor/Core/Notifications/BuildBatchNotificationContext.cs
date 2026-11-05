using System;
using System.Collections.Generic;
using BuildTool.Editor.Pipeline;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BuildTool.Editor.Notifications
{
    public class BuildBatchNotificationContext
    {
        public BuildExecutionResult ExecutionResult { get; }
        public IReadOnlyList<(BuildProfile Profile, BuildReport Report)> Reports { get; }
        public string ProjectName { get; }
        public bool IsSuccess { get; }
        public int TotalProfiles { get; }
        public int SucceededProfiles { get; }
        public int FailedProfiles { get; }
        public int CancelledProfiles { get; }
        public TimeSpan Duration { get; }
        public string FormattedDuration { get; }
        public DateTime Timestamp { get; }

        public BuildBatchNotificationContext(
            BuildExecutionResult result,
            IReadOnlyList<(BuildProfile Profile, BuildReport Report)> reports)
        {
            ExecutionResult = result;
            Reports = reports ?? Array.Empty<(BuildProfile Profile, BuildReport Report)>();
            ProjectName = Application.productName;
            Timestamp = DateTime.UtcNow;

            if (result != null)
            {
                IsSuccess = result.Success;
                TotalProfiles = result.TotalProfiles > 0 ? result.TotalProfiles : Reports.Count;
                SucceededProfiles = result.SucceededProfiles;
                FailedProfiles = result.FailedProfiles;
                CancelledProfiles = result.CancelledProfiles;
                Duration = TimeSpan.FromSeconds(result.TotalSeconds);
            }
            else
            {
                IsSuccess = false;
                TotalProfiles = Reports.Count;
                SucceededProfiles = 0;
                FailedProfiles = 0;
                CancelledProfiles = 0;
                Duration = TimeSpan.Zero;
            }

            FormattedDuration = BuildNotificationContext.FormatDuration(Duration);
        }

        public string GetStatusEmoji()
        {
            if (IsSuccess)
            {
                return "🟢";
            }
            if (CancelledProfiles > 0 && FailedProfiles == 0)
            {
                return "🟡";
            }
            return "🔴";
        }

        public string GetStatusTitle()
        {
            if (IsSuccess)
            {
                return "Build Batch Succeeded";
            }
            if (CancelledProfiles > 0 && FailedProfiles == 0)
            {
                return "Build Batch Cancelled";
            }
            return "Build Batch Failed";
        }
    }
}
