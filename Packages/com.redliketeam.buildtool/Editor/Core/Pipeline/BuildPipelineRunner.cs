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
using UnityEditorInternal;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace BuildTool.Editor.Pipeline
{
    public static class BuildPipelineRunner
    {
        private const string ActiveSessionKey = "BuildTool.BuildPipelineRunner.ActiveSession";
        private static bool s_IsRunning;
        private static bool s_ResumeScheduled;
        private static bool s_KeepAliveActive;
        private static bool s_KeepAlivePumpPlayerLoop;
        private static readonly Stopwatch s_KeepAliveStopwatch = new();

        public static bool IsBuildRunning => s_IsRunning || HasActiveSession();
        public static event Action SessionStateChanged;
        public static event Action<BuildExecutionResult, bool> SessionCompleted;

        [Serializable]
        private class BuildSessionState
        {
            public bool IsDevelopment;
            public bool CleanBuild;
            public bool RegisterVersion;
            public bool OpenFolder;
            public bool ContinueOnError;
            public string RootBuildPathOverride;
            public string FullBuildPath;
            public string BuildIndex;
            public int OriginalTargetGroup;
            public long StartedUtcTicks;
            public int NextIndex;
            public int TotalProfiles;
            public int SucceededProfiles;
            public int FailedProfiles;
            public int CancelledProfiles;
            public string ErrorMessage;
            public List<string> ProfileAssetPaths = new();
            public List<string> ProfileInitialNames = new();
            public List<BuildExecutionResult.ProfileResultSummary> Results = new();
            public List<string> SavedReportFiles = new();
            public List<string> AddressablesBuiltTargets = new();
            public List<SerializedKeyValue> ExtensionArgs = new();
        }

        [Serializable]
        private class SerializedKeyValue
        {
            public string Key;
            public string Value;
        }

        internal static bool HasActiveSession()
        {
            return !string.IsNullOrEmpty(SessionState.GetString(ActiveSessionKey, string.Empty));
        }

        internal static void ClearActiveSession()
        {
            SessionState.EraseString(ActiveSessionKey);
        }

        private static void SaveSessionState(BuildSessionState state)
        {
            if (state == null)
            {
                ClearActiveSession();
                return;
            }

            SessionState.SetString(ActiveSessionKey, JsonUtility.ToJson(state));
        }

        private static BuildSessionState LoadSessionState()
        {
            var json = SessionState.GetString(ActiveSessionKey, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            try
            {
                return JsonUtility.FromJson<BuildSessionState>(json);
            }
            catch
            {
                return null;
            }
        }

        private static bool CanPersistAcrossDomainReload(IReadOnlyList<string> profileAssetPaths)
        {
            return BuildTool.ShouldDeferToEditorDelayCall &&
                   profileAssetPaths != null &&
                   profileAssetPaths.Count > 1 &&
                   profileAssetPaths.All(p => !string.IsNullOrEmpty(p));
        }

        private static void StartBackgroundTickKeepAlive(bool pumpPlayerLoop = false)
        {
            if (!BuildTool.ShouldDeferToEditorDelayCall)
            {
                return;
            }

            s_KeepAlivePumpPlayerLoop = pumpPlayerLoop;
            if (s_KeepAliveActive)
            {
                return;
            }

            s_KeepAliveActive = true;
            s_KeepAliveStopwatch.Restart();
            EditorApplication.update += OnBackgroundKeepAliveTick;
            BuildTool.RequestEditorTick();
        }

        private static void StopBackgroundTickKeepAlive()
        {
            s_KeepAlivePumpPlayerLoop = false;
            if (!s_KeepAliveActive)
            {
                return;
            }

            s_KeepAliveActive = false;
            EditorApplication.update -= OnBackgroundKeepAliveTick;
            s_KeepAliveStopwatch.Stop();
        }

        private static void OnBackgroundKeepAliveTick()
        {
            if (InternalEditorUtility.isApplicationActive || s_KeepAliveStopwatch.ElapsedMilliseconds < 150)
            {
                return;
            }

            s_KeepAliveStopwatch.Restart();
            BuildTool.RequestEditorTick();
            if (s_KeepAlivePumpPlayerLoop)
            {
                EditorApplication.QueuePlayerLoopUpdate();
            }
        }

        [InitializeOnLoadMethod]
        private static void TryResumeBuildSessionOnReload()
        {
            if (Application.isBatchMode || s_ResumeScheduled || !HasActiveSession())
            {
                return;
            }

            s_ResumeScheduled = true;
            var cleanTicks = 0;
            var settleStopwatch = Stopwatch.StartNew();

            void WaitForReady()
            {
                BuildTool.RequestEditorTickThrottled(150);

                if (EditorApplication.isCompiling ||
                    EditorApplication.isUpdating ||
                    BuildTool.IsScriptReloadRequested())
                {
                    cleanTicks = 0;
                    settleStopwatch.Restart();
                    return;
                }

                cleanTicks++;
                if (cleanTicks < 3 || settleStopwatch.ElapsedMilliseconds < 500)
                {
                    return;
                }

                EditorApplication.update -= WaitForReady;
                s_ResumeScheduled = false;

                var session = LoadSessionState();
                if (session == null)
                {
                    ClearActiveSession();
                    StopBackgroundTickKeepAlive();
                    SessionStateChanged?.Invoke();
                    return;
                }

                _ = ResumeSessionAsync(session);
            }

            EditorApplication.update += WaitForReady;
            BuildTool.RequestEditorTick();
        }

        private static async Task ResumeSessionAsync(BuildSessionState session)
        {
            try
            {
                var request = new BuildExecutionRequest
                {
                    IsDevelopment = session.IsDevelopment,
                    CleanBuild = session.CleanBuild,
                    ClearFolderFirst = false,
                    RegisterVersion = session.RegisterVersion,
                    OpenFolder = session.OpenFolder,
                    ContinueOnError = session.ContinueOnError,
                    RootBuildPathOverride = session.RootBuildPathOverride
                };

                if (session.ExtensionArgs != null)
                {
                    foreach (var arg in session.ExtensionArgs)
                    {
                        if (!string.IsNullOrEmpty(arg?.Key))
                        {
                            request.SetExtensionArg(arg.Key, arg.Value);
                        }
                    }
                }

                var profiles = new List<BuildProfile>();
                var profileAssetPaths = new List<string>();
                var profileInitialNames = new List<string>();

                var count = session.ProfileAssetPaths?.Count ?? 0;
                for (var i = 0; i < count; i++)
                {
                    var assetPath = session.ProfileAssetPaths[i];
                    var profile = !string.IsNullOrEmpty(assetPath)
                        ? AssetDatabase.LoadAssetAtPath<BuildProfile>(assetPath)
                        : null;
                    profiles.Add(profile);
                    profileAssetPaths.Add(assetPath);
                    var initialName = session.ProfileInitialNames != null && i < session.ProfileInitialNames.Count
                        ? session.ProfileInitialNames[i]
                        : BuildReportFormatter.GetSafeProfileName(profile);
                    profileInitialNames.Add(initialName);
                }

                var result = new BuildExecutionResult
                {
                    TotalProfiles = session.TotalProfiles > 0 ? session.TotalProfiles : profiles.Count,
                    SucceededProfiles = session.SucceededProfiles,
                    FailedProfiles = session.FailedProfiles,
                    CancelledProfiles = session.CancelledProfiles,
                    RootBuildPath = session.FullBuildPath,
                    BuildIndex = session.BuildIndex,
                    ErrorMessage = session.ErrorMessage,
                    Results = session.Results ?? new List<BuildExecutionResult.ProfileResultSummary>()
                };

                var savedReportFiles = session.SavedReportFiles != null
                    ? new List<string>(session.SavedReportFiles)
                    : new List<string>();

                var initialAddressablesTargets = session.AddressablesBuiltTargets != null &&
                                                 session.AddressablesBuiltTargets.Count > 0
                    ? new HashSet<string>(session.AddressablesBuiltTargets)
                    : null;

                var originalTargetGroup = (BuildTargetGroup)session.OriginalTargetGroup;
                BuildTool.ActiveRunRegisterOverride = request.RegisterVersion;

                Debug.Log(
                    $"[BuildPipelineRunner] Resuming sequential build after domain reload " +
                    $"({session.NextIndex + 1}/{result.TotalProfiles})...");

                await ExecuteSequenceAsync(
                    request,
                    profiles,
                    profileAssetPaths,
                    profileInitialNames,
                    result,
                    savedReportFiles,
                    session.FullBuildPath,
                    session.BuildIndex,
                    originalTargetGroup,
                    session.StartedUtcTicks,
                    session.NextIndex,
                    initialAddressablesTargets,
                    wasResumed: true);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BuildPipelineRunner] Failed to resume sequential build session: {ex.Message}");
                ClearActiveSession();
                StopBackgroundTickKeepAlive();
                BuildTool.ActiveRunRegisterOverride = null;
                BuildTool.Dispose();
                SessionStateChanged?.Invoke();
            }
        }

        public static async Task<BuildExecutionResult> RunAsync(BuildExecutionRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

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

            var originalTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup;
            var startedUtcTicks = DateTime.UtcNow.Ticks;
            var delegatedToSequence = false;
            BuildTool.ActiveRunRegisterOverride = request.RegisterVersion;
            s_IsRunning = true;
            SessionStateChanged?.Invoke();

            try
            {
                StartBackgroundTickKeepAlive(pumpPlayerLoop: true);
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
                }
                finally
                {
                    StopBackgroundTickKeepAlive();
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

                var buildIndex = BuildTool.LastBuildIndex;
                result.RootBuildPath = fullBuildPath;
                result.BuildIndex = buildIndex;

                var profileAssetPaths = profiles.Select(p => AssetDatabase.GetAssetPath(p)).ToList();
                var profileInitialNames = profiles.Select(p => BuildReportFormatter.GetSafeProfileName(p)).ToList();

                EditorUtility.ClearProgressBar();

                delegatedToSequence = true;
                return await ExecuteSequenceAsync(
                    request,
                    profiles,
                    profileAssetPaths,
                    profileInitialNames,
                    result,
                    new List<string>(),
                    fullBuildPath,
                    buildIndex,
                    originalTargetGroup,
                    startedUtcTicks,
                    0,
                    null,
                    wasResumed: false);
            }
            catch (Exception ex)
            {
                result.TotalSeconds = Math.Max(
                    0.0,
                    (DateTime.UtcNow - new DateTime(startedUtcTicks, DateTimeKind.Utc)).TotalSeconds);
                result.Success = false;
                result.ErrorMessage = ex.Message;
                Debug.LogError($"[BuildPipelineRunner] Build sequence cancelled due to error: {ex.Message}");

                await FinalizeFailureTrackingAndNotificationsAsync(
                    request,
                    result,
                    null,
                    ex,
                    result.TotalSeconds,
                    wasResumed: false);

                return result;
            }
            finally
            {
                if (!delegatedToSequence)
                {
                    s_IsRunning = false;
                    StopBackgroundTickKeepAlive();
                    ClearActiveSession();
                    BuildTool.ActiveRunRegisterOverride = null;
                    if (EditorUserBuildSettings.selectedBuildTargetGroup != originalTargetGroup &&
                        originalTargetGroup != BuildTargetGroup.Unknown)
                    {
                        EditorUserBuildSettings.selectedBuildTargetGroup = originalTargetGroup;
                    }
                    BuildTool.Dispose();
                    SessionStateChanged?.Invoke();
                }
            }
        }

        private static BuildSessionState CreateSessionStateSnapshot(
            BuildExecutionRequest request,
            BuildExecutionResult result,
            BuildContext context,
            string fullBuildPath,
            string buildIndex,
            BuildTargetGroup originalTargetGroup,
            long startedUtcTicks,
            int nextIndex,
            List<string> profileAssetPaths,
            List<string> profileInitialNames,
            List<string> savedReportFiles)
        {
            var addressablesBuiltTargets = context?.GetState<HashSet<string>>("addressables_built_targets");
            var extensionArgs = new List<SerializedKeyValue>();
            if (request.ExtensionArgs != null)
            {
                foreach (var kvp in request.ExtensionArgs)
                {
                    extensionArgs.Add(new SerializedKeyValue
                    {
                        Key = kvp.Key,
                        Value = kvp.Value
                    });
                }
            }

            return new BuildSessionState
            {
                IsDevelopment = request.IsDevelopment,
                CleanBuild = request.CleanBuild,
                RegisterVersion = request.RegisterVersion,
                OpenFolder = request.OpenFolder,
                ContinueOnError = request.ContinueOnError,
                RootBuildPathOverride = request.RootBuildPathOverride,
                FullBuildPath = fullBuildPath,
                BuildIndex = buildIndex,
                OriginalTargetGroup = (int)originalTargetGroup,
                StartedUtcTicks = startedUtcTicks,
                NextIndex = nextIndex,
                TotalProfiles = result.TotalProfiles,
                SucceededProfiles = result.SucceededProfiles,
                FailedProfiles = result.FailedProfiles,
                CancelledProfiles = result.CancelledProfiles,
                ErrorMessage = result.ErrorMessage,
                ProfileAssetPaths = new List<string>(profileAssetPaths),
                ProfileInitialNames = new List<string>(profileInitialNames),
                Results = new List<BuildExecutionResult.ProfileResultSummary>(result.Results),
                SavedReportFiles = new List<string>(savedReportFiles),
                AddressablesBuiltTargets = addressablesBuiltTargets != null
                    ? new List<string>(addressablesBuiltTargets)
                    : new List<string>(),
                ExtensionArgs = extensionArgs
            };
        }

        private static Task<bool> WaitForDomainReloadOrCleanStateAsync()
        {
            var tcs = new TaskCompletionSource<bool>();
            var stopwatch = Stopwatch.StartNew();
            var refreshTriggered = false;
            var reloadRequested = false;
            var cleanTicks = 0;

            void Tick()
            {
                BuildTool.RequestEditorTickThrottled(150);

                if (!refreshTriggered)
                {
                    refreshTriggered = true;
                    if (!EditorApplication.isCompiling && !EditorApplication.isUpdating)
                    {
                        AssetDatabase.Refresh();
                    }
                    return;
                }

                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                {
                    cleanTicks = 0;
                    if (stopwatch.Elapsed.TotalSeconds > 30.0)
                    {
                        EditorApplication.update -= Tick;
                        tcs.TrySetResult(false);
                    }
                    return;
                }

                if (BuildTool.IsScriptReloadRequested())
                {
                    cleanTicks = 0;
                    if (!reloadRequested)
                    {
                        reloadRequested = true;
                        EditorUtility.RequestScriptReload();
                    }
                    if (stopwatch.Elapsed.TotalSeconds > 30.0)
                    {
                        EditorApplication.update -= Tick;
                        tcs.TrySetResult(false);
                    }
                    return;
                }

                cleanTicks++;
                if (cleanTicks >= 3)
                {
                    EditorApplication.update -= Tick;
                    tcs.TrySetResult(true);
                }
            }

            EditorApplication.update += Tick;
            BuildTool.RequestEditorTick();
            return tcs.Task;
        }

        private static async Task FinalizeFailureTrackingAndNotificationsAsync(
            BuildExecutionRequest request,
            BuildExecutionResult result,
            List<(BuildProfile Profile, BuildReport Report)> reports,
            Exception ex,
            double elapsedSeconds,
            bool wasResumed)
        {
            StartBackgroundTickKeepAlive(pumpPlayerLoop: true);
            try
            {
#if BUILDTOOL_TRACKING
                if (request.RegisterVersion)
                {
                    try
                    {
                        var failureReport =
                            $"BUILD FAILED WITH EXCEPTION.\n" +
                            $"Error: {ex.Message}\n" +
                            $"Duration: {TimeSpan.FromSeconds(elapsedSeconds):mm\\:ss}\n" +
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

                var notifReports = reports != null && reports.Count == result.Results.Count ? reports : null;
                var batchContext = new BuildBatchNotificationContext(result, notifReports);
                try
                {
                    await BuildNotificationManager.DispatchBatchAsync(batchContext);
                }
                catch (Exception notifEx)
                {
                    Debug.LogWarning($"[BuildPipelineRunner] Failed to dispatch notifications: {notifEx.Message}");
                }
            }
            finally
            {
                StopBackgroundTickKeepAlive();
            }

            SessionCompleted?.Invoke(result, wasResumed);
        }

        private static async Task<BuildExecutionResult> ExecuteSequenceAsync(
            BuildExecutionRequest request,
            List<BuildProfile> profiles,
            List<string> profileAssetPaths,
            List<string> profileInitialNames,
            BuildExecutionResult result,
            List<string> savedReportFiles,
            string fullBuildPath,
            string buildIndex,
            BuildTargetGroup originalTargetGroup,
            long startedUtcTicks,
            int startIndex,
            HashSet<string> initialAddressablesTargets,
            bool wasResumed)
        {
            var reports = new List<(BuildProfile Profile, BuildReport Report)>();
            var handedOffToReload = false;

            s_IsRunning = true;
            SessionStateChanged?.Invoke();

            try
            {
                var context = new BuildContext(request)
                {
                    Profiles = profiles,
                    RootBuildPath = fullBuildPath,
                    BuildIndex = buildIndex
                };

                if (initialAddressablesTargets != null && initialAddressablesTargets.Count > 0)
                {
                    context.SetState("addressables_built_targets", initialAddressablesTargets);
                }

                var (preprocessors, postprocessors) = DiscoverAndInitExtensions(context);
                var persistable = CanPersistAcrossDomainReload(profileAssetPaths);

                for (var i = startIndex; i < profiles.Count; i++)
                {
                    if (persistable && BuildTool.IsScriptReloadRequested())
                    {
                        var snapshot = CreateSessionStateSnapshot(
                            request,
                            result,
                            context,
                            fullBuildPath,
                            buildIndex,
                            originalTargetGroup,
                            startedUtcTicks,
                            i,
                            profileAssetPaths,
                            profileInitialNames,
                            savedReportFiles);
                        SaveSessionState(snapshot);
                        handedOffToReload = true;

                        var preReady = await WaitForDomainReloadOrCleanStateAsync();
                        if (!preReady)
                        {
                            handedOffToReload = false;
                            var timeoutMsg =
                                "Timed out waiting for Unity domain reload to complete before continuing sequential build.";
                            Debug.LogError($"[BuildPipelineRunner] {timeoutMsg}");
                            if (string.IsNullOrEmpty(result.ErrorMessage))
                            {
                                result.ErrorMessage = timeoutMsg;
                            }
                            break;
                        }

                        handedOffToReload = false;
                    }

                    var assetPath = i < profileAssetPaths.Count ? profileAssetPaths[i] : null;
                    var initialName = i < profileInitialNames.Count ? profileInitialNames[i] : "Profile";
                    var profile = profiles[i];

                    if ((profile == null || !profile) && !string.IsNullOrEmpty(assetPath))
                    {
                        profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(assetPath);
                        profiles[i] = profile;
                    }

                    var profileName = BuildReportFormatter.GetSafeProfileName(profile);
                    if (string.IsNullOrEmpty(profileName) || profileName == "Profile")
                    {
                        profileName = initialName;
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
                        if (string.IsNullOrEmpty(result.ErrorMessage))
                        {
                            result.ErrorMessage = errorMsg;
                        }
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

                    var targetBefore = EditorUserBuildSettings.activeBuildTarget;
                    var subtargetBefore = EditorUserBuildSettings.standaloneBuildSubtarget;
                    var profileTarget = BuildTool.GetBuildTarget(profile);
                    var profileSubtarget = BuildTool.GetStandaloneBuildSubtarget(profile);

                    BuildPlayerResult buildResult;
                    try
                    {
                        StopBackgroundTickKeepAlive();
                        buildResult = await BuildTool.ExecuteBuildFromProfile(
                            profile, options, fullBuildPath, preprocessors, postprocessors);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError(
                            $"[BuildPipelineRunner] Exception during build of profile '{profileName}': {ex.Message}");
                        result.FailedProfiles++;
                        if (string.IsNullOrEmpty(result.ErrorMessage))
                        {
                            result.ErrorMessage = $"Exception during build of profile '{profileName}': {ex.Message}";
                        }
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
                                savedReportFiles.Add(logFile);
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

                        if (!isSucceeded && string.IsNullOrEmpty(result.ErrorMessage))
                        {
                            result.ErrorMessage =
                                $"Build for profile '{profileName}' ended with status '{buildResult.Result}'.";
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
                        if (string.IsNullOrEmpty(result.ErrorMessage))
                        {
                            result.ErrorMessage = $"Build result for profile '{profileName}' is null.";
                        }
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

                    if (i + 1 < profiles.Count && persistable)
                    {
                        var targetChanged =
                            EditorUserBuildSettings.activeBuildTarget != targetBefore ||
                            EditorUserBuildSettings.standaloneBuildSubtarget != subtargetBefore ||
                            profileTarget != targetBefore ||
                            profileSubtarget != subtargetBefore;
                        var reloadNeeded =
                            targetChanged ||
                            BuildTool.IsScriptReloadRequested() ||
                            EditorApplication.isCompiling ||
                            EditorApplication.isUpdating;

                        var snapshot = CreateSessionStateSnapshot(
                            request,
                            result,
                            context,
                            fullBuildPath,
                            buildIndex,
                            originalTargetGroup,
                            startedUtcTicks,
                            i + 1,
                            profileAssetPaths,
                            profileInitialNames,
                            savedReportFiles);
                        SaveSessionState(snapshot);

                        if (reloadNeeded)
                        {
                            handedOffToReload = true;
                            Debug.Log(
                                $"[BuildPipelineRunner] Waiting for Unity script compilation/domain reload " +
                                $"before profile {i + 2}/{profiles.Count}...");

                            var ready = await WaitForDomainReloadOrCleanStateAsync();
                            if (!ready)
                            {
                                handedOffToReload = false;
                                var timeoutMsg =
                                    "Timed out waiting for Unity domain reload to complete before continuing sequential build.";
                                Debug.LogError($"[BuildPipelineRunner] {timeoutMsg}");
                                if (string.IsNullOrEmpty(result.ErrorMessage))
                                {
                                    result.ErrorMessage = timeoutMsg;
                                }
                                break;
                            }

                            handedOffToReload = false;
                        }
                    }
                }

                var allReportsInMemory = reports.Count == result.Results.Count && reports.Count > 0;
                result.Reports = reports;

                if (allReportsInMemory)
                {
                    var summaryTable = BuildReportFormatter.FormatSummaryTable(reports, fullBuildPath);
                    Debug.Log(summaryTable);

                    if (reports.Count > 1)
                    {
                        Debug.Log(BuildReportFormatter.FormatBatch(reports, fullBuildPath));
                    }
                }
                else if (result.Results.Count > 0)
                {
                    var summaryTable = BuildReportFormatter.FormatSummaryTable(result.Results, fullBuildPath);
                    Debug.Log(summaryTable);

                    if (result.Results.Count > 1)
                    {
                        Debug.Log(BuildReportFormatter.FormatBatchFromSummaries(result, savedReportFiles));
                    }
                }

                var elapsedSeconds = Math.Max(
                    0.0,
                    (DateTime.UtcNow - new DateTime(startedUtcTicks, DateTimeKind.Utc)).TotalSeconds);
                result.TotalSeconds = elapsedSeconds;
                result.Success = result.FailedProfiles == 0 && result.SucceededProfiles == profiles.Count;
                if (!result.Success && string.IsNullOrEmpty(result.ErrorMessage))
                {
                    result.ErrorMessage =
                        $"Build sequence finished with {result.FailedProfiles} failed and " +
                        $"{result.CancelledProfiles} cancelled profiles out of {result.TotalProfiles}.";
                }

                StartBackgroundTickKeepAlive(pumpPlayerLoop: true);
                try
                {
#if BUILDTOOL_TRACKING
                    if (request.RegisterVersion)
                    {
                        string fullReport;
                        if (allReportsInMemory)
                        {
                            fullReport = BuildReportFormatter.FormatBatch(reports, fullBuildPath, includeAllFiles: true);
                        }
                        else if (result.Results.Count > 0)
                        {
                            fullReport = BuildReportFormatter.FormatBatchFromSummaries(result, savedReportFiles);
                        }
                        else
                        {
                            fullReport =
                                $"BUILD FAILED OR CANCELLED.\n" +
                                $"Status: Failed (0 successful profiles out of {profiles.Count}).\n" +
                                $"Duration: {TimeSpan.FromSeconds(elapsedSeconds):mm\\:ss}";
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

                    Debug.Log(
                        "<color=yellow>[BuildPipelineRunner] Sequence completed: " +
                        $"{result.SucceededProfiles}/{result.TotalProfiles} " +
                        $"succeeded in {result.TotalSeconds:F1}s.</color>");

                    var notifReports = reports.Count == result.Results.Count ? reports : null;
                    var batchContext = new BuildBatchNotificationContext(result, notifReports);
                    try
                    {
                        await BuildNotificationManager.DispatchBatchAsync(batchContext);
                    }
                    catch (Exception notifEx)
                    {
                        Debug.LogWarning($"[BuildPipelineRunner] Failed to dispatch notifications: {notifEx.Message}");
                    }
                }
                finally
                {
                    StopBackgroundTickKeepAlive();
                }

                SessionCompleted?.Invoke(result, wasResumed);
                return result;
            }
            catch (Exception ex)
            {
                result.TotalSeconds = Math.Max(
                    0.0,
                    (DateTime.UtcNow - new DateTime(startedUtcTicks, DateTimeKind.Utc)).TotalSeconds);
                result.Success = false;
                result.ErrorMessage = ex.Message;
                Debug.LogError($"[BuildPipelineRunner] Build sequence cancelled due to error: {ex.Message}");

                await FinalizeFailureTrackingAndNotificationsAsync(
                    request,
                    result,
                    reports,
                    ex,
                    result.TotalSeconds,
                    wasResumed);

                return result;
            }
            finally
            {
                s_IsRunning = false;
                StopBackgroundTickKeepAlive();
                if (!handedOffToReload)
                {
                    ClearActiveSession();
                    BuildTool.ActiveRunRegisterOverride = null;
                    if (EditorUserBuildSettings.selectedBuildTargetGroup != originalTargetGroup &&
                        originalTargetGroup != BuildTargetGroup.Unknown)
                    {
                        EditorUserBuildSettings.selectedBuildTargetGroup = originalTargetGroup;
                    }
                    BuildTool.Dispose();
                }
                SessionStateChanged?.Invoke();
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

        internal static bool IsSafeDirectoryToClear(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return false;
            }

            var normalized = NormalizeDirectoryPath(fullPath);
            var root = NormalizeDirectoryPath(Path.GetPathRoot(normalized));

            if (string.Equals(normalized, root, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var projectRoot = NormalizeDirectoryPath(Path.Combine(Application.dataPath, ".."));

            // Refuse project root itself or any parent directory containing the project
            if (IsSameOrSubPath(projectRoot, normalized))
            {
                return false;
            }

            var protectedDirs = new[]
            {
                Application.dataPath,
                Path.Combine(projectRoot, "Packages"),
                Path.Combine(projectRoot, "ProjectSettings"),
                Path.Combine(projectRoot, "Library"),
                Path.Combine(projectRoot, "UserSettings"),
                Path.Combine(projectRoot, "Temp"),
                Path.Combine(projectRoot, "Logs"),
                Path.Combine(projectRoot, ".git")
            };

            foreach (var dir in protectedDirs)
            {
                if (IsSameOrSubPath(normalized, NormalizeDirectoryPath(dir)))
                {
                    return false;
                }
            }

            return true;
        }

        private static string NormalizeDirectoryPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            return Path.GetFullPath(path).TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool IsSameOrSubPath(string candidate, string basePath)
        {
            if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(basePath))
            {
                return false;
            }

            if (string.Equals(candidate, basePath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var prefix = basePath + Path.DirectorySeparatorChar;
            var altPrefix = basePath + Path.AltDirectorySeparatorChar;

            return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                   candidate.StartsWith(altPrefix, StringComparison.OrdinalIgnoreCase);
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
                        Name = !string.IsNullOrWhiteSpace(item.Attr.Title) ? item.Attr.Title : item.Type.Name,
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
