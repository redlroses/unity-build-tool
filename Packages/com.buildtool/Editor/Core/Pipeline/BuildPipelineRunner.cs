using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BuildTool.Editor.Extensions;
using BuildTool.Editor.Notifications;
using BuildTool.Editor.Processors;
#if BUILDTOOL_TRACKING
using BuildTool.Tracking.Editor;
#endif
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace BuildTool.Editor.Pipeline
{
    public static class BuildPipelineRunner
    {
        public static async Task<BuildExecutionResult> RunAsync(BuildExecutionRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var stopwatch = Stopwatch.StartNew();
            var profiles = ResolveProfiles(request);

            if (profiles.Count == 0)
            {
                const string error = "No valid build profiles found to execute.";
                Debug.LogError($"[BuildPipelineRunner] {error}");
                return BuildExecutionResult.CreateFailed(error);
            }

            var result = new BuildExecutionResult
            {
                TotalProfiles = profiles.Count
            };

            var context = new BuildContext(request)
            {
                Profiles = profiles
            };

            var reports = new List<(BuildProfile Profile, BuildReport Report)>();

            try
            {
                if (BuildTool.IsTrackingEnabled)
                {
                    if (request.RegisterVersion)
                    {
                        Debug.Log("[BuildPipelineRunner] Registering build version remotely...");
                        await BuildTool.RegisterRemote();
                    }
                    else
                    {
                        Debug.Log("[BuildPipelineRunner] Registering build version locally...");
                        await BuildTool.RegisterLocal();
                    }
                }
                else
                {
                    if (request.RegisterVersion && BuildTool.RegisterRemoteHandler != null)
                    {
                        await BuildTool.RegisterRemote();
                    }
                    else if (!request.RegisterVersion && BuildTool.RegisterLocalHandler != null)
                    {
                        await BuildTool.RegisterLocal();
                    }
                }

                var buildPath = !string.IsNullOrWhiteSpace(request.RootBuildPathOverride)
                    ? request.RootBuildPathOverride
                    : BuildTool.GetRootBuildPath(
                        string.IsNullOrEmpty(BuildTool.LastBuildIndex) ? null : BuildTool.LastBuildIndex);

                var fullBuildPath = Path.GetFullPath(buildPath);

                if (request.ClearFolderFirst)
                {
                    if (!IsSafeDirectoryToClear(fullBuildPath))
                    {
                        var errorMsg =
                            $"Refusing to clear directory '{fullBuildPath}' because it resolves to a protected " +
                            "project or system root path.";
                        Debug.LogError($"[BuildPipelineRunner] {errorMsg}");
                        return BuildExecutionResult.CreateFailed(errorMsg, fullBuildPath);
                    }

                    Debug.Log($"[BuildPipelineRunner] Clearing build directory: {fullBuildPath}");
                    DeleteFilesAndDirectories(fullBuildPath);
                }

                context.RootBuildPath = fullBuildPath;
                context.BuildIndex = BuildTool.LastBuildIndex;
                result.RootBuildPath = fullBuildPath;
                result.BuildIndex = context.BuildIndex;

                var (preprocessors, postprocessors) = DiscoverAndInitExtensions(context);

                var profileDescriptors = profiles.Select(p => new
                {
                    AssetPath = AssetDatabase.GetAssetPath(p),
                    InitialName = BuildReportFormatter.GetSafeProfileName(p)
                }).ToList();

                EditorUtility.ClearProgressBar();

                for (var i = 0; i < profiles.Count; i++)
                {
                    var descriptor = profileDescriptors[i];
                    var profile = profiles[i];

                    if (profile == null || !profile)
                    {
                        if (!string.IsNullOrEmpty(descriptor.AssetPath))
                        {
                            profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(descriptor.AssetPath);
                            profiles[i] = profile;
                        }
                    }

                    var profileName = BuildReportFormatter.GetSafeProfileName(profile);
                    if (string.IsNullOrEmpty(profileName) || profileName == "Profile")
                    {
                        profileName = descriptor.InitialName;
                    }

                    context.CurrentProfile = profile;

                    var profileStopwatch = Stopwatch.StartNew();

                    if (!HasValidScenesToBuild(profile))
                    {
                        var errorMsg =
                            $"Profile '{profileName}' has no enabled scenes to build. " +
                            "Add scenes to Build Profile or Editor Build Settings.";
                        Debug.LogError($"[BuildPipelineRunner] {errorMsg}");
                        result.FailedProfiles++;
                        result.Results.Add(new BuildExecutionResult.ProfileResultSummary
                        {
                            ProfileName = profileName,
                            Result = "Failed",
                            DurationSeconds = profileStopwatch.Elapsed.TotalSeconds
                        });

                        if (!request.ContinueOnError)
                        {
                            Debug.LogError(
                                "[BuildPipelineRunner] Aborting remaining builds due to failure (Fail-Fast).");
                            break;
                        }

                        continue;
                    }

                    Debug.Log(
                        $"[BuildPipelineRunner] [{i + 1}/{profiles.Count}] " +
                        $"Starting build for profile: '{profileName}'...");

                    var options = BuildOptions.None;
                    if (request.IsDevelopment)
                    {
                        options |= BuildOptions.Development;
                    }
                    if (request.CleanBuild)
                    {
                        options |= BuildOptions.CleanBuildCache;
                    }

                    BuildPlayerResult buildResult;
                    try
                    {
                        buildResult = await BuildTool.ExecuteBuildFromProfile(
                            profile, options, fullBuildPath, preprocessors, postprocessors);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError(
                            $"[BuildPipelineRunner] Exception during build of profile '{profileName}': {ex.Message}");
                        result.FailedProfiles++;
                        result.Results.Add(new BuildExecutionResult.ProfileResultSummary
                        {
                            ProfileName = profileName,
                            Result = "Failed",
                            DurationSeconds = profileStopwatch.Elapsed.TotalSeconds
                        });

                        if (!request.ContinueOnError)
                        {
                            Debug.LogError(
                                "[BuildPipelineRunner] Aborting remaining builds due to failure (Fail-Fast).");
                            break;
                        }

                        continue;
                    }

                    profileStopwatch.Stop();

                    if (buildResult != null)
                    {
                        if (buildResult.Report != null)
                        {
                            reports.Add((profile, buildResult.Report));
                            context.CompletedReports.Add((profile, buildResult.Report));

                            var logFile = BuildReportFormatter.SaveProfileReportToFile(
                                profile, buildResult.Report, fullBuildPath, profileName);
                            if (!string.IsNullOrEmpty(logFile))
                            {
                                Debug.Log($"[BuildPipelineRunner] Saved profile build log: {logFile}");
                            }
                        }

                        var isSucceeded = buildResult.Success;
                        if (buildResult.Result == BuildResult.Succeeded)
                        {
                            result.SucceededProfiles++;
                        }
                        else if (buildResult.Result == BuildResult.Cancelled)
                        {
                            result.CancelledProfiles++;
                        }
                        else
                        {
                            result.FailedProfiles++;
                        }

                        result.Results.Add(new BuildExecutionResult.ProfileResultSummary
                        {
                            ProfileName = profileName,
                            Result = buildResult.Result.ToString(),
                            TotalErrors = buildResult.TotalErrors,
                            TotalWarnings = buildResult.TotalWarnings,
                            DurationSeconds = profileStopwatch.Elapsed.TotalSeconds,
                            OutputPath = buildResult.OutputPath,
                            TotalSize = buildResult.TotalSize
                        });

                        if (!isSucceeded && !request.ContinueOnError)
                        {
                            Debug.LogError(
                                $"[BuildPipelineRunner] Build for profile '{profileName}' ended with " +
                                $"status '{buildResult.Result}'. Aborting remaining builds (Fail-Fast).");
                            break;
                        }
                    }
                    else
                    {
                        result.FailedProfiles++;
                        result.Results.Add(new BuildExecutionResult.ProfileResultSummary
                        {
                            ProfileName = profileName,
                            Result = "Failed (Null Result)",
                            DurationSeconds = profileStopwatch.Elapsed.TotalSeconds
                        });

                        if (!request.ContinueOnError)
                        {
                            Debug.LogError(
                                $"[BuildPipelineRunner] Build result for profile '{profileName}' is null. " +
                                "Aborting remaining builds (Fail-Fast).");
                            break;
                        }
                    }
                }

                result.Reports = reports;

                if (reports.Count > 0)
                {
                    var summaryTable = BuildReportFormatter.FormatSummaryTable(reports, fullBuildPath);
                    Debug.Log(summaryTable);

                    if (reports.Count > 1)
                    {
                        Debug.Log(BuildReportFormatter.FormatBatch(reports, fullBuildPath));
                    }
                }

#if BUILDTOOL_TRACKING
                if (request.RegisterVersion)
                {
                    string fullReport;
                    if (reports.Count > 0)
                    {
                        fullReport = BuildReportFormatter.FormatBatch(reports, fullBuildPath, includeAllFiles: true);
                    }
                    else
                    {
                        fullReport =
                            $"BUILD FAILED OR CANCELLED.\n" +
                            $"Status: Failed (0 successful profiles out of {profiles.Count}).\n" +
                            $"Duration: {stopwatch.Elapsed:mm\\:ss}";
                    }

                    if (BuildTool.FinalizeRemoteReportHandler != null)
                    {
                        await BuildTool.FinalizeRemoteReportHandler(fullReport);
                    }
                    else
                    {
                        await BuildTracker.FinalizeRemoteBuildReport(fullReport);
                    }
                }
#endif

                if (request.OpenFolder && !string.IsNullOrWhiteSpace(fullBuildPath))
                {
                    OpenFolderInExplorer(fullBuildPath);
                }

                stopwatch.Stop();
                result.TotalSeconds = stopwatch.Elapsed.TotalSeconds;
                result.Success = result.FailedProfiles == 0 && result.SucceededProfiles == profiles.Count;

                Debug.Log(
                    "<color=yellow>[BuildPipelineRunner] Sequence completed: " +
                    $"{result.SucceededProfiles}/{result.TotalProfiles} " +
                    $"succeeded in {result.TotalSeconds:F1}s.</color>");

                var batchContext = new BuildBatchNotificationContext(result, reports);
                try
                {
                    await BuildNotificationManager.DispatchBatchAsync(batchContext);
                }
                catch (Exception notifEx)
                {
                    Debug.LogWarning($"[BuildPipelineRunner] Failed to dispatch notifications: {notifEx.Message}");
                }

                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                result.TotalSeconds = stopwatch.Elapsed.TotalSeconds;
                result.Success = false;
                result.ErrorMessage = ex.Message;
                Debug.LogError($"[BuildPipelineRunner] Build sequence cancelled due to error: {ex.Message}");

#if BUILDTOOL_TRACKING
                if (request.RegisterVersion)
                {
                    try
                    {
                        var failureReport =
                            $"BUILD FAILED WITH EXCEPTION.\n" +
                            $"Error: {ex.Message}\n" +
                            $"Duration: {stopwatch.Elapsed:mm\\:ss}\n" +
                            "Stack Trace:\n" +
                            $"{ex.StackTrace}";
                        if (BuildTool.FinalizeRemoteReportHandler != null)
                        {
                            await BuildTool.FinalizeRemoteReportHandler(failureReport);
                        }
                        else
                        {
                            await BuildTracker.FinalizeRemoteBuildReport(failureReport);
                        }
                    }
                    catch (Exception reportEx)
                    {
                        Debug.LogWarning(
                            $"[BuildPipelineRunner] Failed to record failure report: {reportEx.Message}");
                    }
                }
#endif

                var batchContext = new BuildBatchNotificationContext(result, reports);
                try
                {
                    await BuildNotificationManager.DispatchBatchAsync(batchContext);
                }
                catch (Exception notifEx)
                {
                    Debug.LogWarning($"[BuildPipelineRunner] Failed to dispatch notifications: {notifEx.Message}");
                }

                return result;
            }
            finally
            {
                BuildTool.Dispose();
            }
        }

        private static List<BuildProfile> ResolveProfiles(BuildExecutionRequest request)
        {
            var resolved = new List<BuildProfile>();
            var hasSpecifiedProfiles =
                (request.Profiles != null && request.Profiles.Any(p => p != null)) ||
                (request.ProfileNames != null && request.ProfileNames.Any(n => !string.IsNullOrWhiteSpace(n)));

            if (request.Profiles != null && request.Profiles.Count > 0)
            {
                foreach (var profile in request.Profiles)
                {
                    if (profile != null && !resolved.Contains(profile))
                    {
                        resolved.Add(profile);
                    }
                }
            }

            if (request.ProfileNames != null && request.ProfileNames.Count > 0)
            {
                var allProfiles = BuildTool.GetBuildProfiles();
                foreach (var name in request.ProfileNames)
                {
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        continue;
                    }

                    var match = allProfiles.FirstOrDefault(p =>
                        p != null && (p.name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase) ||
                                     AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(p))
                                         .Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)));

                    if (match != null)
                    {
                        if (!resolved.Contains(match))
                        {
                            resolved.Add(match);
                        }
                    }
                    else
                    {
                        Debug.LogWarning(
                            $"[BuildPipelineRunner] BuildProfile with name or GUID '{name}' was not found.");
                    }
                }
            }

            if (resolved.Count == 0 && !hasSpecifiedProfiles)
            {
                var active = BuildProfile.GetActiveBuildProfile();
                if (active != null)
                {
                    Debug.Log(
                        "[BuildPipelineRunner] No profiles specified in request. " +
                        $"Using active BuildProfile '{active.name}'.");
                    resolved.Add(active);
                }
            }

            return resolved;
        }

        private static bool HasValidScenesToBuild(BuildProfile profile)
        {
            if (BuildTool.PipelineService != null && !(BuildTool.PipelineService is UnityBuildPipelineService))
            {
                return true;
            }

            if (profile != null)
            {
                var serializedProfile = new SerializedObject(profile);
                var overrideProp = serializedProfile.FindProperty("m_OverrideGlobalSceneList");
                var scenesProp = serializedProfile.FindProperty("m_Scenes");

                if (overrideProp != null && overrideProp.boolValue)
                {
                    if (scenesProp != null && scenesProp.isArray)
                    {
                        for (var i = 0; i < scenesProp.arraySize; i++)
                        {
                            var elem = scenesProp.GetArrayElementAtIndex(i);
                            var enabledProp = elem.FindPropertyRelative("enabled");
                            if (enabledProp == null || enabledProp.boolValue)
                            {
                                return true;
                            }
                        }
                    }

                    return false;
                }
            }

            return EditorBuildSettings.scenes != null && EditorBuildSettings.scenes.Any(s => s != null && s.enabled);
        }

        private static (List<IBuildPreprocessor> Preprocessors, List<IBuildPostprocessor> Postprocessors)
            DiscoverAndInitExtensions(BuildContext context)
        {
            var preprocessors = new List<IBuildPreprocessor>();
            var postprocessors = new List<IBuildPostprocessor>();

            var types = TypeCache.GetTypesWithAttribute<BuildExtensionAttribute>()
                .Where(t => t.IsClass && !t.IsAbstract)
                .Select(t => new
                {
                    Type = t,
                    Attr = (BuildExtensionAttribute)Attribute.GetCustomAttribute(t, typeof(BuildExtensionAttribute))
                })
                .Where(x => x.Attr != null)
                .OrderBy(x => x.Attr.Order);

            foreach (var item in types)
            {
                try
                {
                    var instance = Activator.CreateInstance(item.Type);

                    if (instance is IConfigurableBuildExtension configurable)
                    {
                        configurable.Initialize(context);
                    }

                    if (instance is IBuildPreprocessor pre)
                    {
                        preprocessors.Add(pre);
                    }

                    if (instance is IBuildPostprocessor post)
                    {
                        postprocessors.Add(post);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError(
                        $"[BuildPipelineRunner] Failed to initialize extension '{item.Type.FullName}': {ex.Message}");
                }
            }

            return (preprocessors, postprocessors);
        }

        private static bool IsSafeDirectoryToClear(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return false;
            }

            var normalized = Path.GetFullPath(fullPath).TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var root = Path.GetPathRoot(normalized)?.TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (string.Equals(normalized, root, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (string.Equals(normalized, projectRoot, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var assetsPath = Path.GetFullPath(Application.dataPath).TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (normalized.StartsWith(assetsPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var packagesPath = Path.GetFullPath(Path.Combine(projectRoot, "Packages")).TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (normalized.StartsWith(packagesPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var projectSettingsPath = Path.GetFullPath(Path.Combine(projectRoot, "ProjectSettings")).TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (normalized.StartsWith(projectSettingsPath, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        public static void DeleteFilesAndDirectories(string directory)
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            foreach (var file in Directory.GetFiles(directory))
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[BuildPipelineRunner] File delete error '{file}': {ex.Message}");
                }
            }

            foreach (var subDir in Directory.GetDirectories(directory))
            {
                DeleteFilesAndDirectories(subDir);

                try
                {
                    Directory.Delete(subDir);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[BuildPipelineRunner] Directory delete error '{subDir}': {ex.Message}");
                }
            }
        }

        public static void OpenFolderInExplorer(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                var fullPath = Path.GetFullPath(path);
                string targetDir = null;

                if (Directory.Exists(fullPath))
                {
                    targetDir = fullPath;
                }
                else if (File.Exists(fullPath) || !string.IsNullOrEmpty(Path.GetExtension(fullPath)))
                {
                    targetDir = Path.GetDirectoryName(fullPath);
                }
                else
                {
                    var parent = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                    {
                        targetDir = parent;
                    }
                }

                if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
                {
                    Debug.LogWarning($"[BuildPipelineRunner] Output folder does not exist: '{fullPath}'");
                    return;
                }

                targetDir = targetDir.TrimEnd('\\', '/');

                if (Application.platform == RuntimePlatform.WindowsEditor)
                {
                    using var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"\"{targetDir}\"",
                        UseShellExecute = false
                    });
                }
                else if (Application.platform == RuntimePlatform.OSXEditor)
                {
                    using var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = "open",
                        Arguments = $"\"{targetDir}\"",
                        UseShellExecute = false
                    });
                }
                else
                {
                    EditorUtility.RevealInFinder(targetDir);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BuildPipelineRunner] Failed to open build directory: {ex.Message}");
            }
        }

        public static ExtensionListResponse GetExtensionInfo()
        {
            var response = new ExtensionListResponse();

            var types = TypeCache.GetTypesWithAttribute<BuildExtensionAttribute>()
                .Where(t => t.IsClass && !t.IsAbstract)
                .Select(t => new
                {
                    Type = t,
                    Attr = (BuildExtensionAttribute)Attribute.GetCustomAttribute(t, typeof(BuildExtensionAttribute))
                })
                .Where(x => x.Attr != null)
                .OrderBy(x => x.Attr.Order);

            foreach (var item in types)
            {
                try
                {
                    var instance = Activator.CreateInstance(item.Type);
                    var extInfo = new ExtensionInfo
                    {
                        Name = item.Type.Name,
                        TypeName = item.Type.FullName,
                        Order = item.Attr.Order
                    };

                    if (instance is IConfigurableBuildExtension configurable)
                    {
                        var parameters = configurable.GetParameters();
                        if (parameters != null)
                        {
                            foreach (var p in parameters)
                            {
                                extInfo.Parameters.Add(new ExtensionCliParam
                                {
                                    Key = p.Key,
                                    Type = p.Type,
                                    Description = p.Description,
                                    DefaultValue = p.DefaultValue
                                });
                            }
                        }
                    }

                    response.Extensions.Add(extInfo);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"[BuildPipelineRunner] Failed to inspect extension '{item.Type.FullName}': {ex.Message}");
                }
            }

            return response;
        }

        public static string FormatExtensionInfo(ExtensionListResponse response)
        {
            if (response?.Extensions == null)
            {
                return string.Empty;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Available Build Extensions:");
            foreach (var ext in response.Extensions)
            {
                sb.AppendLine($"  [{ext.Name}] (Order: {ext.Order})");
                if (ext.Parameters.Count == 0)
                {
                    sb.AppendLine("    (No configurable CLI parameters declared)");
                }
                else
                {
                    foreach (var param in ext.Parameters)
                    {
                        var def = string.IsNullOrEmpty(param.DefaultValue) ? "" : $" (Default: {param.DefaultValue})";
                        sb.AppendLine($"    * {param.Key} [{param.Type}]{def}: {param.Description}");
                    }
                }
            }

            return sb.ToString().TrimEnd();
        }
    }

    [Serializable]
    public class ExtensionListResponse
    {
        public List<ExtensionInfo> Extensions = new();
    }

    [Serializable]
    public class ExtensionInfo
    {
        public string Name;
        public string TypeName;
        public int Order;
        public List<ExtensionCliParam> Parameters = new();
    }
}
