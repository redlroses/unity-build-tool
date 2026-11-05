using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BuildTool.Editor
{
    public static class BuildReportFormatter
    {
        public static string GetSafeProfileName(BuildProfile profile, BuildReport report = null)
        {
            try
            {
                if (profile != null)
                {
                    var name = profile.name;
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        foreach (var c in Path.GetInvalidFileNameChars())
                        {
                            name = name.Replace(c, '_');
                        }

                        return name.Trim();
                    }
                }
            }
            catch
            {
            }

            if (report != null)
            {
                return report.summary.platform.ToString();
            }

            return "Profile";
        }

        public static string FormatSingle(BuildProfile profile, BuildReport report)
        {
            if (report == null)
            {
                return "Build report is null.";
            }

            var sb = new StringBuilder();
            var profileName = GetSafeProfileName(profile);
            var summary = report.summary;

            sb.AppendLine("================================================================================");
            sb.AppendLine($" BUILD REPORT: {profileName} ({summary.platform})");
            sb.AppendLine("================================================================================");
            sb.AppendLine($"  Result:        {summary.result}");
            sb.AppendLine($"  Output Path:   {summary.outputPath}");
            sb.AppendLine($"  Total Size:    {FormatBytes(summary.totalSize)}");
            sb.AppendLine($"  Duration:      {FormatDuration(summary.totalTime)}");
            sb.AppendLine($"  Errors:        {summary.totalErrors}");
            sb.AppendLine($"  Warnings:      {summary.totalWarnings}");
            sb.AppendLine($"  Built At:      {summary.buildStartedAt:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine("--------------------------------------------------------------------------------");

            if (report.steps != null && report.steps.Length > 0)
            {
                sb.AppendLine(" BUILD STEPS:");
                foreach (var step in report.steps)
                {
                    var indent = new string(' ', Math.Min(step.depth * 2 + 2, 8));
                    var duration = FormatDuration(step.duration);
                    var msgCount = step.messages != null && step.messages.Length > 0
                        ? $" ({step.messages.Length} msgs)"
                        : "";
                    sb.AppendLine($"{indent}* {step.name,-45} {duration,10}{msgCount}");
                }
                sb.AppendLine("--------------------------------------------------------------------------------");
            }

            var files = report.GetFiles();
            if (files != null && files.Length > 0)
            {
                sb.AppendLine(" OUTPUT ARTIFACTS:");
                foreach (var file in files.OrderByDescending(f => f.size).Take(15))
                {
                    var fileName = Path.GetFileName(file.path);
                    var role = string.IsNullOrEmpty(file.role) ? "" : $"[{file.role}] ";
                    sb.AppendLine($"    {role}{fileName,-45} {FormatBytes(file.size),10}");
                }

                if (files.Length > 15)
                {
                    sb.AppendLine($"    ... and {files.Length - 15} more files");
                }
                sb.AppendLine("--------------------------------------------------------------------------------");
            }

            sb.AppendLine("================================================================================");
            return sb.ToString();
        }

        public static string FormatBatch(
            IEnumerable<(BuildProfile Profile, BuildReport Report)> builds,
            string rootBuildPath,
            bool includeAllFiles = false)
        {
            var buildList = builds?.Where(b => b.Report != null).ToList() ??
                            new List<(BuildProfile Profile, BuildReport Report)>();

            if (buildList.Count == 0)
            {
                return "[BuildReport] No build reports to format.";
            }

            var sb = new StringBuilder();
            sb.AppendLine("================================================================================");
            sb.AppendLine(" BATCH BUILD EXECUTION SUMMARY");
            sb.AppendLine("================================================================================");
            sb.AppendLine($"  Root Path:     {rootBuildPath}");
            sb.AppendLine($"  Total Builds:  {buildList.Count}");
            sb.AppendLine(
                $"  Succeeded:     {buildList.Count(b => b.Report.summary.result == BuildResult.Succeeded)}");
            sb.AppendLine(
                $"  Failed:        {buildList.Count(b => b.Report.summary.result == BuildResult.Failed)}");
            sb.AppendLine(
                $"  Cancelled:     {buildList.Count(b => b.Report.summary.result == BuildResult.Cancelled)}");

            var totalDuration = TimeSpan.FromTicks(buildList.Sum(b => b.Report.summary.totalTime.Ticks));
            var totalSize = (ulong)buildList.Sum(b => (long)b.Report.summary.totalSize);

            sb.AppendLine($"  Total Time:    {FormatDuration(totalDuration)}");
            sb.AppendLine($"  Combined Size: {FormatBytes(totalSize)}");
            sb.AppendLine("================================================================================");
            sb.AppendLine();

            foreach (var (profile, report) in buildList)
            {
                sb.AppendLine(FormatSingle(profile, report));
                sb.AppendLine();
            }

            return sb.ToString();
        }

        public static string FormatSummaryTable(
            IEnumerable<(BuildProfile Profile, BuildReport Report)> builds,
            string rootBuildPath = null)
        {
            var buildList = builds?.Where(b => b.Report != null).ToList() ??
                            new List<(BuildProfile Profile, BuildReport Report)>();

            if (buildList.Count == 0)
            {
                return "No build reports available.";
            }

            var sb = new StringBuilder();
            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine($"{"Profile",-28} | {"Status",-9} | {"Duration",-9} | {"Size",-10} | {"Errors",-6}");
            sb.AppendLine("--------------------------------------------------------------------------------");

            foreach (var (profile, report) in buildList)
            {
                var name = GetSafeProfileName(profile);
                if (name.Length > 28)
                {
                    name = name.Substring(0, 25) + "...";
                }

                var status = report.summary.result.ToString();
                var dur = FormatDuration(report.summary.totalTime);
                var size = FormatBytes(report.summary.totalSize);
                var errs = report.summary.totalErrors.ToString();

                sb.AppendLine($"{name,-28} | {status,-9} | {dur,-9} | {size,-10} | {errs,-6}");
            }

            sb.AppendLine("--------------------------------------------------------------------------------");
            return sb.ToString();
        }

        public static string SaveProfileReportToFile(
            BuildProfile profile,
            BuildReport report,
            string targetDirectory,
            string customProfileName = null)
        {
            if (report == null || string.IsNullOrWhiteSpace(targetDirectory))
            {
                return null;
            }

            try
            {
                var profileName = !string.IsNullOrWhiteSpace(customProfileName)
                    ? customProfileName
                    : GetSafeProfileName(profile);

                foreach (var c in Path.GetInvalidFileNameChars())
                {
                    profileName = profileName.Replace(c, '_');
                }

                var logDir = Path.Combine(targetDirectory, "Logs");
                if (!Directory.Exists(logDir))
                {
                    Directory.CreateDirectory(logDir);
                }

                var filePath = Path.Combine(logDir, $"BuildReport_{profileName}.log");
                var text = FormatSingle(profile, report);

                File.WriteAllText(filePath, text, Encoding.UTF8);
                return filePath;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BuildReportFormatter] Failed to write report file: {ex.Message}");
                return null;
            }
        }

        public static string CategorizeFile(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return "Other";
            }

            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".apk" || ext == ".aab" || ext == ".ipa")
            {
                return "Package";
            }
            if (ext == ".exe" || ext == ".x86_64" || ext == ".app")
            {
                return "Executable";
            }
            if (ext == ".dll" || ext == ".so" || ext == ".dylib")
            {
                return "Binary";
            }
            if (ext == ".bundle" || ext == ".assetbundle")
            {
                return "AssetBundle";
            }
            if (ext == ".data" || ext == ".wasm")
            {
                return "WebGL";
            }
            if (ext == ".pdb" || ext == ".symbols" || ext == ".dsym")
            {
                return "DebugSymbols";
            }

            return "Other";
        }

        public static string FormatBytes(ulong bytes)
        {
            if (bytes < 1024)
            {
                return $"{bytes} B";
            }
            if (bytes < 1024 * 1024)
            {
                return $"{bytes / 1024.0:F1} KB";
            }
            if (bytes < 1024 * 1024 * 1024)
            {
                return $"{bytes / (1024.0 * 1024.0):F2} MB";
            }

            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
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

            return $"{time.TotalSeconds:F1}s";
        }
    }
}
