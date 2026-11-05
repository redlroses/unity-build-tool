using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BuildTool.Editor.Pipeline;
using BuildTool.Editor.Processors;
using BuildTool.Tracking;
#if BUILDTOOL_TRACKING
using BuildTool.Tracking.Editor;
#endif
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace BuildTool.Editor
{
    public static class BuildTool
    {
        public const string DefaultRootFolderPattern = "Build/v{Version}.{BuildIndex} {Date:yyyy-MM-dd HH-mm}";

        public static string RegisterSaveKey => "build_tool_register".AppendProjectName();

        public static bool IsRegisterVersionEnabled
        {
            get => EditorPrefs.GetBool(RegisterSaveKey, false);
            set => EditorPrefs.SetBool(RegisterSaveKey, value);
        }

#if BUILDTOOL_TRACKING
        public static bool IsTrackingEnabled => true;

        public static string GetCurrentBuildIndex(string buildIndexOverride = null)
        {
            if (buildIndexOverride != null)
            {
                return buildIndexOverride;
            }

            if (!IsRegisterVersionEnabled)
            {
                return BuildTracker.LocalBuildIndex;
            }

            if (BuildTracker.BuildInfo != null &&
                !string.IsNullOrEmpty(BuildTracker.BuildInfo.Index) &&
                BuildTracker.BuildInfo.Index != BuildTracker.LocalBuildIndex)
            {
                return BuildTracker.BuildInfo.Index;
            }

            try
            {
                var tracked = BuildTracker.CurrentTrackedBuildIndex;
                if (tracked > 0)
                {
                    return (tracked + 1).ToString();
                }
            }
            catch
            {
            }

            return "1";
        }
#else
        public static bool IsTrackingEnabled => false;
#endif

        public static string GetRootBuildPath(string buildIndex = null, DateTime? date = null)
        {
            var settings = BuildToolSettings.instance;
            var pattern = string.IsNullOrEmpty(settings.RootFolderPattern)
                ? DefaultRootFolderPattern
                : settings.RootFolderPattern;

            var version = Application.version;
            var now = date ?? DateTime.Now;

            var result = pattern;

            result = Regex.Replace(result, @"\{AppVersion\}|\{Version\}", version, RegexOptions.IgnoreCase);

#if BUILDTOOL_TRACKING
            var index = GetCurrentBuildIndex(buildIndex);
            result = Regex.Replace(result, @"\{BuildIndex\}", index, RegexOptions.IgnoreCase);
#else
            if (!string.IsNullOrEmpty(buildIndex))
            {
                result = Regex.Replace(result, @"\{BuildIndex\}", buildIndex, RegexOptions.IgnoreCase);
            }
            else
            {
                result = Regex.Replace(result, @"[._-]?\{BuildIndex\}", "", RegexOptions.IgnoreCase);
            }
#endif
            result = Regex.Replace(
                result, @"\{ProductName\}", PlayerSettings.productName ?? "", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"\{Time\}", now.ToString("HH-mm-ss"), RegexOptions.IgnoreCase);

            result = Regex.Replace(result, @"\{Date(?::([^}]+))?\}", match =>
            {
                var format = match.Groups[1].Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value)
                    ? match.Groups[1].Value
                    : "yyyy-MM-dd";

                return now.ToString(format);
            }, RegexOptions.IgnoreCase);

            result = result.Trim().TrimStart('/', '\\').TrimEnd('/', '\\');
            result = result.Replace('\\', '/');
            result = Regex.Replace(result, @"/+", "/");

            return string.IsNullOrEmpty(result) ? "Build" : result;
        }

        /// <summary>
        ///     Gets the build index from the currently active <see cref="BuildInfo" /> asset.
        /// </summary>
        /// <value>
        ///     The build index string if available; otherwise, an empty string.
        /// </value>
#if BUILDTOOL_TRACKING
        public static string LastBuildIndex => BuildTracker.BuildInfo?.Index ?? string.Empty;
#else
        public static string LastBuildIndex => string.Empty;
#endif

        public static event Action<BuildReport, BuildInfo> BuildFinished;

        /// <summary>
        ///     Service used to execute build player operations. Defaults to <see cref="UnityBuildPipelineService" />.
        ///     Can be replaced with a mock implementation in unit tests.
        /// </summary>
        public static IBuildPipelineService PipelineService { get; set; } = new UnityBuildPipelineService();

        /// <summary>
        ///     Resets the <see cref="PipelineService" /> to the default <see cref="UnityBuildPipelineService" />.
        /// </summary>
        public static void ResetPipelineService()
        {
            PipelineService = new UnityBuildPipelineService();
        }

        public static Func<Task> RegisterRemoteHandler { get; set; }
        public static Func<Task> RegisterLocalHandler { get; set; }
        public static Func<string, Task> FinalizeRemoteReportHandler { get; set; }
        public static Action DisposeHandler { get; set; }

        /// <summary>
        ///     Builds the specified profile with the specified options and returns a <see cref="BuildPlayerResult" />.
        /// </summary>
        /// <param name="profile">The profile to build.</param>
        /// <param name="options">The build options.</param>
        /// <param name="buildPath">The path to build to.</param>
        /// <param name="preprocessors">The preprocessors to run.</param>
        /// <param name="postprocessors">The postprocessors to run.</param>
        /// <returns>The build execution result.</returns>
        public static async Task<BuildPlayerResult> ExecuteBuildFromProfile(
            BuildProfile profile, BuildOptions options, string buildPath,
            IEnumerable<IBuildPreprocessor> preprocessors = null,
            IEnumerable<IBuildPostprocessor> postprocessors = null)
        {
            SyncSelectedBuildTargetGroup(profile);

            if (preprocessors != null)
            {
                foreach (var preprocess in preprocessors)
                {
                    preprocess?.PreProcess(profile);
                }
            }

            var (path, result) = await AsyncBuild(profile, buildPath, options);

            if (result != null && result.Success && postprocessors != null)
            {
                var targetDirectory = Directory.Exists(path)
                    ? path
                    : Path.GetDirectoryName(path);

                foreach (var postprocess in postprocessors)
                {
                    postprocess?.PostProcess(profile, targetDirectory);
                }
            }

            return result;
        }

        /// <summary>
        ///     Opens the build folder in the file explorer.
        /// </summary>
        /// <remarks>
        ///     The build folder is the folder where the build is saved. The path is
        ///     determined by the <see cref="BuildRootFolder" /> property.
        /// </remarks>
        public static void OpenBuildFolder()
        {
            var folder = BuildToolSettings.instance.BuildFolderName;
            var fullPath = Path.GetFullPath(string.IsNullOrWhiteSpace(folder) ? "Build" : folder);
            BuildPipelineRunner.OpenFolderInExplorer(fullPath);
        }

        /// <summary>
        ///     Register the build in the tracking repository, incrementing the build index.
        /// </summary>
        /// <remarks>
        ///     This method is a wrapper for <see cref="BuildTracker.TrackAndRegister" />.
        ///     It increments the build index and saves a build info asset.
        /// </remarks>
        public static async Task RegisterRemote()
        {
            if (RegisterRemoteHandler != null)
            {
                await RegisterRemoteHandler();
            }
#if BUILDTOOL_TRACKING
            else
            {
                await BuildTracker.TrackAndRegister();
            }
#endif
        }

        /// <summary>
        ///     Track the build locally without registering it in the tracking repository.
        /// </summary>
        /// <remarks>
        ///     This method is a wrapper for <see cref="BuildTracker.TrackLocally" />.
        ///     It does not increase the build index and saves a build info asset with the
        ///     build index set to "local".
        /// </remarks>
        public static async Task RegisterLocal()
        {
            if (RegisterLocalHandler != null)
            {
                await RegisterLocalHandler();
            }
#if BUILDTOOL_TRACKING
            else
            {
                await BuildTracker.TrackLocally();
            }
#endif
        }

        /// <summary>
        ///     Deletes the build info asset.
        /// </summary>
        /// <remarks>
        ///     This method is called when the build process is finished.
        ///     It is a wrapper for <see cref="BuildTracker.DeleteBuildInfoAsset" />.
        /// </remarks>
        public static void Dispose()
        {
            if (DisposeHandler != null)
            {
                DisposeHandler();
                return;
            }

#if BUILDTOOL_TRACKING
            BuildTracker.DeleteBuildInfoAsset();
#endif
        }

        /// <summary>
        ///     Finds all build profiles in the project.
        /// </summary>
        /// <returns>
        ///     A list of <see cref="BuildProfile" /> objects found in the project.
        /// </returns>
        public static List<BuildProfile> GetBuildProfiles()
        {
            return AssetDatabase.FindAssets("t:BuildProfile")
                .Select(guid => AssetDatabase.LoadAssetAtPath<BuildProfile>(AssetDatabase.GUIDToAssetPath(guid)))
                .ToList();
        }

        private static async Task<(string Path, BuildPlayerResult Result)> AsyncBuild(
            BuildProfile profile,
            string buildPath,
            BuildOptions options = BuildOptions.None)
        {
            var builder = BuildToolSettings.instance.GetPathBuilderOrDefault(profile);
            var locationPathName = builder.GetLocationPathName(profile, buildPath);

            var buildPlayerOptions =
                CreateBuildPlayerOptions(profile, locationPathName, options);

            var result = await ProcessBuild(buildPlayerOptions, profile);

            return (locationPathName, result);
        }

        private static BuildPlayerWithProfileOptions CreateBuildPlayerOptions(
            BuildProfile profile, string locationPathName, BuildOptions options)
        {
            BuildPlayerWithProfileOptions buildPlayerOptions = new()
            {
                locationPathName = locationPathName,
                options = options,
                buildProfile = profile
            };

            return buildPlayerOptions;
        }

        private static readonly PropertyInfo s_BuildTargetProp =
            typeof(BuildProfile).GetProperty(
                "buildTarget", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly PropertyInfo s_SubtargetProp =
            typeof(BuildProfile).GetProperty(
                "subtarget", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo s_PlayerSettingsYamlField =
            typeof(BuildProfile).GetField(
                "m_PlayerSettingsYaml", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private const string BundleVersionPattern =
            @"(?:^|\n)\s*(?:-\s*line:\s*)?['\""]?bundleVersion:\s*(.+?)['\""]?\s*(?:\r?\n|$)";

        private static readonly MethodInfo s_GetYamlStringMethod =
            s_PlayerSettingsYamlField?.FieldType.GetMethod(
                "GetYamlString", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        /// <summary>
        /// Gets the BuildTarget configured for the specified profile.
        /// Falls back to EditorUserBuildSettings.activeBuildTarget if profile is null or cannot be inspected.
        /// </summary>
        public static BuildTarget GetBuildTarget(BuildProfile profile)
        {
            if (profile == null)
            {
                return EditorUserBuildSettings.activeBuildTarget;
            }

            try
            {
                if (s_BuildTargetProp != null)
                {
                    var target = (BuildTarget)s_BuildTargetProp.GetValue(profile);
                    if (target != BuildTarget.NoTarget)
                    {
                        return target;
                    }
                }

                var so = new SerializedObject(profile);
                var prop = so.FindProperty("m_BuildTarget");
                if (prop != null && prop.intValue != 0)
                {
                    return (BuildTarget)prop.intValue;
                }
            }
            catch (Exception ex)
            {
                var pName = BuildReportFormatter.GetSafeProfileName(profile);
                Debug.LogWarning($"[BuildTool] Could not determine BuildTarget for profile '{pName}': {ex.Message}");
            }

            return EditorUserBuildSettings.activeBuildTarget;
        }

        /// <summary>
        /// Gets the StandaloneBuildSubtarget configured for the specified profile.
        /// Falls back to EditorUserBuildSettings.standaloneBuildSubtarget if profile is null or cannot be inspected.
        /// </summary>
        public static StandaloneBuildSubtarget GetStandaloneBuildSubtarget(BuildProfile profile)
        {
            if (profile == null)
            {
                return EditorUserBuildSettings.standaloneBuildSubtarget;
            }

            try
            {
                if (s_SubtargetProp != null)
                {
                    return (StandaloneBuildSubtarget)s_SubtargetProp.GetValue(profile);
                }

                var so = new SerializedObject(profile);
                var prop = so.FindProperty("m_Subtarget");
                if (prop != null)
                {
                    return (StandaloneBuildSubtarget)prop.intValue;
                }
            }
            catch (Exception ex)
            {
                var pName = BuildReportFormatter.GetSafeProfileName(profile);
                Debug.LogWarning(
                    $"[BuildTool] Could not determine StandaloneBuildSubtarget for profile '{pName}': {ex.Message}");
            }

            return EditorUserBuildSettings.standaloneBuildSubtarget;
        }

        public static BuildTargetGroup GetBuildTargetGroup(BuildProfile profile)
        {
            if (profile == null)
            {
                return BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
            }

            var target = GetBuildTarget(profile);
            return target != BuildTarget.NoTarget
                ? BuildPipeline.GetBuildTargetGroup(target)
                : BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
        }

        /// <summary>
        /// Gets the application version configured for the specified profile.
        /// If the profile contains an override for bundleVersion, that value is returned.
        /// Otherwise, falls back to the global PlayerSettings.bundleVersion (or Application.version).
        /// </summary>
        public static string GetProfileVersion(BuildProfile profile)
        {
            if (profile == null)
            {
                return GetGlobalAppVersion();
            }

            try
            {
                if (s_PlayerSettingsYamlField != null && s_GetYamlStringMethod != null)
                {
                    var yamlObj = s_PlayerSettingsYamlField.GetValue(profile);
                    if (yamlObj != null)
                    {
                        var yaml = s_GetYamlStringMethod.Invoke(yamlObj, null) as string;
                        if (!string.IsNullOrEmpty(yaml))
                        {
                            var match = Regex.Match(
                                yaml, @"(?:^|\n)\s*bundleVersion:\s*(.+?)\s*(?:\r?\n|$)", RegexOptions.IgnoreCase);
                            if (match.Success)
                            {
                                var val = match.Groups[1].Value.Trim().Trim('\'', '\"');
                                if (!string.IsNullOrEmpty(val))
                                {
                                    return val;
                                }
                            }
                        }
                    }
                }

                var so = new SerializedObject(profile);
                var settingsProp = so.FindProperty("m_PlayerSettingsYaml.m_Settings");
                if (settingsProp != null && settingsProp.isArray)
                {
                    for (var i = 0; i < settingsProp.arraySize; i++)
                    {
                        var item = settingsProp.GetArrayElementAtIndex(i);
                        var lineProp = item.FindPropertyRelative("line");
                        if (lineProp != null && !string.IsNullOrEmpty(lineProp.stringValue))
                        {
                            var match = Regex.Match(
                                lineProp.stringValue, BundleVersionPattern, RegexOptions.IgnoreCase);
                            if (match.Success)
                            {
                                var val = match.Groups[1].Value.Trim().Trim('\'', '\"');
                                if (!string.IsNullOrEmpty(val))
                                {
                                    return val;
                                }
                            }
                        }
                    }
                }

                var assetPath = AssetDatabase.GetAssetPath(profile);
                if (!string.IsNullOrEmpty(assetPath) && File.Exists(assetPath))
                {
                    var content = File.ReadAllText(assetPath);
                    var yamlSectionMatch = Regex.Match(
                        content, @"m_PlayerSettingsYaml:\s*([\s\S]*?)(?:[a-zA-Z0-9_]+:|$)", RegexOptions.IgnoreCase);
                    var searchArea = yamlSectionMatch.Success ? yamlSectionMatch.Groups[1].Value : content;
                    var match = Regex.Match(searchArea, BundleVersionPattern, RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        var val = match.Groups[1].Value.Trim().Trim('\'', '\"');
                        if (!string.IsNullOrEmpty(val))
                        {
                            return val;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                var pName = BuildReportFormatter.GetSafeProfileName(profile);
                Debug.LogWarning(
                    $"[BuildTool] Could not determine version override for profile '{pName}': {ex.Message}");
            }

            return GetGlobalAppVersion();
        }

        /// <summary>
        /// Gets the base application version from ProjectSettings.asset or
        /// PlayerSettings.bundleVersion / Application.version.
        /// </summary>
        public static string GetGlobalAppVersion()
        {
            return !string.IsNullOrEmpty(PlayerSettings.bundleVersion)
                ? PlayerSettings.bundleVersion
                : Application.version;
        }

        private static void SyncSelectedBuildTargetGroup(BuildProfile profile)
        {
            var targetGroup = GetBuildTargetGroup(profile);
            if (targetGroup != BuildTargetGroup.Unknown &&
                EditorUserBuildSettings.selectedBuildTargetGroup != targetGroup)
            {
                EditorUserBuildSettings.selectedBuildTargetGroup = targetGroup;
            }
        }

        private static Task<BuildPlayerResult> ProcessBuild(BuildPlayerWithProfileOptions options, BuildProfile profile)
        {
            if (PipelineService != null && !(PipelineService is UnityBuildPipelineService))
            {
                var mockResult = PipelineService.BuildPlayer(options, profile);
#if BUILDTOOL_TRACKING
                BuildFinished?.Invoke(mockResult?.Report, BuildTracker.BuildInfo);
#else
                BuildFinished?.Invoke(mockResult?.Report, null);
#endif
                LogBuildReport(mockResult?.Report, profile);
                return Task.FromResult(mockResult);
            }

            var tcs = new TaskCompletionSource<BuildPlayerResult>();

            EditorApplication.delayCall += () =>
            {
                try
                {
                    var service = PipelineService ?? new UnityBuildPipelineService();
                    var buildResult = service.BuildPlayer(options, profile);

#if BUILDTOOL_TRACKING
                    BuildFinished?.Invoke(buildResult?.Report, BuildTracker.BuildInfo);
#else
                    BuildFinished?.Invoke(buildResult?.Report, null);
#endif

                    LogBuildReport(buildResult?.Report, profile);
                    tcs.SetResult(buildResult);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            };

            return tcs.Task;
        }

        private static void LogBuildReport(BuildReport buildReport, BuildProfile profile = null)
        {
            if (buildReport == null)
            {
                return;
            }

            Debug.Log(BuildReportFormatter.FormatSingle(profile, buildReport));
        }
    }
}

