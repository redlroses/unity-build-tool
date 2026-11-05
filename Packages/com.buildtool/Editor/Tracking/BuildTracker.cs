using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BuildTool.Tracking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BuildTool.Tracking.Editor
{
    public static class BuildTracker
    {
        public const string LocalBuildIndex = "local";
        private const string DateFormat = "yyyy-MM-dd";

        private static readonly string s_JsonFilePath =
            Path.Combine(BuildTrackingSettings.RepositoryPath, BuildTrackingSettings.BuildInfoFileName);

        public static BuildInfo BuildInfo => OriginBuildInfo;

        public static int CurrentTrackedBuildIndex
        {
            get
            {
                var json = GetBuildIndexJson();
                var token = json?[BuildTrackingSettings.LastBuildIndexKey];
                return token?.Value<int>() ?? 0;
            }
        }

        public static string CurrentBranch => GetCurrentBranch();

        private static string s_CachedBranch;
        private static double s_LastBranchCheckTime;

        public static string GetCurrentBranch()
        {
            var now = EditorApplication.timeSinceStartup;
            if (!string.IsNullOrEmpty(s_CachedBranch) && now - s_LastBranchCheckTime < 2.0)
            {
                return s_CachedBranch;
            }

            string resolvedBranch = null;
            try
            {
                var gitPath = s_Config.GetProjectGitPath();
                var gitEntry = Path.Combine(gitPath, ".git");
                string headPath = null;

                if (Directory.Exists(gitEntry))
                {
                    headPath = Path.Combine(gitEntry, "HEAD");
                }
                else if (File.Exists(gitEntry))
                {
                    var gitDirContent = File.ReadAllText(gitEntry).Trim();
                    if (gitDirContent.StartsWith("gitdir:"))
                    {
                        var relPath = gitDirContent.Substring("gitdir:".Length).Trim();
                        var fullGitDir = Path.IsPathRooted(relPath)
                            ? relPath
                            : Path.GetFullPath(Path.Combine(gitPath, relPath));
                        headPath = Path.Combine(fullGitDir, "HEAD");
                    }
                }

                if (!string.IsNullOrEmpty(headPath) && File.Exists(headPath))
                {
                    var headContent = File.ReadAllText(headPath).Trim();
                    if (headContent.StartsWith("ref: refs/heads/"))
                    {
                        resolvedBranch = headContent.Substring("ref: refs/heads/".Length).Trim();
                    }
                    else if (headContent.Length >= 7)
                    {
                        resolvedBranch = headContent.Substring(0, 7);
                    }
                }
            }
            catch
            {
            }

            if (string.IsNullOrEmpty(resolvedBranch))
            {
                try
                {
                    var projectGit = new GitRepository(s_Config.GetProjectGitPath(), s_Config.IsLogging);
                    resolvedBranch = projectGit.GetCurrentBranch().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    if (s_Config.IsLogging)
                    {
                        Debug.LogWarning($"[BuildTracker] Failed to get current Git branch: {ex.Message}");
                    }
                    resolvedBranch = string.Empty;
                }
            }

            s_CachedBranch = resolvedBranch;
            s_LastBranchCheckTime = now;
            return resolvedBranch;
        }

        private static BuildTrackingSettings s_Config => BuildTrackingSettings.instance;

        private static BuildInfo s_OriginBuildInfo;
        internal static BuildInfo OriginBuildInfo
        {
            get
            {
                if (s_OriginBuildInfo == null)
                {
                    s_OriginBuildInfo =
                        AssetDatabase.LoadAssetAtPath<BuildInfo>(BuildTrackingSettings.BuildInfoAssetPath);
                }
                return s_OriginBuildInfo;
            }
            private set => s_OriginBuildInfo = value;
        }

        internal static ProgressBarHandler ProgressBarHandler { get; } = new(8);

        public static Task TrackAndRegister()
        {
            return TrackAsync(true, "Initializing remote tracking...");
        }

        public static Task TrackLocally()
        {
            return TrackAsync(false, "Initializing local tracking...");
        }

        private static async Task TrackAsync(bool isRemote, string initMessage)
        {
            ProgressBarHandler.Reset();
            ProgressBarHandler.StepUpdate(initMessage);

            try
            {
                var projectGit = new GitRepository(s_Config.GetProjectGitPath(), s_Config.IsLogging);
                var trackingGit = new GitRepository(BuildTrackingSettings.RepositoryPath, s_Config.IsLogging);

                ProgressBarHandler.StepUpdate("Validating commit code...");
                var commit = await projectGit.GetCurrentShortCommitCode();

                ProgressBarHandler.StepUpdate("Validating tracking date...");
                var dateUtc = DateTime.UtcNow.ToString(DateFormat);

                string buildId;

                if (isRemote)
                {
                    buildId = (await UpdateBuildIndex(projectGit, trackingGit, commit, dateUtc)).ToString();
                }
                else
                {
                    buildId = LocalBuildIndex;
                    ProgressBarHandler.Step();
                    ProgressBarHandler.Step();
                    ProgressBarHandler.Step();
                }

                ProgressBarHandler.StepUpdate("Saving build info asset...");

                var infoAsset = BuildInfo.Create(buildId, commit, dateUtc, isRemote);
                var saved = SaveOrReplaceAsset(infoAsset, BuildTrackingSettings.BuildInfoAssetPath);
                OriginBuildInfo = saved as BuildInfo ?? infoAsset;
            }
            finally
            {
                ProgressBarHandler.Complete();
                EditorUtility.ClearProgressBar();
            }
        }

        private static Object SaveOrReplaceAsset(Object asset, string assetPath)
        {
            EnsureDirectoryExistsForAsset(assetPath);

            var existing = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
            if (existing != null)
            {
                if (existing == asset)
                {
                    return existing;
                }

                EditorUtility.CopySerialized(asset, existing);
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssets();
                return existing;
            }

            AssetDatabase.CreateAsset(asset, assetPath);
            AssetDatabase.SaveAssets();
            return asset;
        }

        private static void EnsureDirectoryExistsForAsset(string assetPath)
        {
            var dir = Path.GetDirectoryName(assetPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        public static void DeleteBuildInfoAsset()
        {
            if (File.Exists(BuildTrackingSettings.BuildInfoAssetPath))
            {
                AssetDatabase.DeleteAsset(BuildTrackingSettings.BuildInfoAssetPath);
            }
        }

        private static async Task<int> UpdateBuildIndex(
            GitRepository projectGit,
            GitRepository trackingGit,
            string commit,
            string dateUtc)
        {
            ProgressBarHandler.StepUpdate("Updating build info...");

            try
            {
                var trackingBranchName = s_Config.GetBranchName();
                var hasProjectOrigin = await projectGit.HasOrigin();
                var trackingRepoUrl = hasProjectOrigin ? await projectGit.GetRepositoryUrl() : string.Empty;

                await EnsureTrackingBranchExists(trackingGit, projectGit, trackingRepoUrl, trackingBranchName);
                await PrepareTrackingRepository(trackingGit, trackingRepoUrl, trackingBranchName, !hasProjectOrigin);

                var projectBranch = await projectGit.GetCurrentBranch();
                var appVersion = Application.version;
                var commitMessage = $"{projectBranch}:{commit} v{appVersion}";

                var attempts = Math.Max(1, s_Config.PushRetryAttempts);
                var delayMs = Math.Max(100, s_Config.PushRetryDelayMs);

                Exception lastException = null;

                for (var attempt = 1; attempt <= attempts; attempt++)
                {
                    try
                    {
                        ProgressBarHandler.StepUpdate(
                            $"Creating and pushing build tracking info (attempt {attempt}/{attempts})...");

                        if (attempt > 1 && hasProjectOrigin)
                        {
                            await trackingGit.Fetch();
                            if (s_Config.KeepSingleCommit)
                            {
                                await trackingGit.ResetToRemote(trackingBranchName);
                            }
                            else
                            {
                                await trackingGit.Pull(trackingBranchName);
                            }
                        }

                        var currentBuildIndex = await IncrementBuildIndexAndRecordHistory(
                            commit,
                            projectBranch,
                            dateUtc,
                            appVersion);

                        var messageWithBuildIndex = $"{currentBuildIndex}/{commitMessage}";

                        if (s_Config.KeepSingleCommit)
                        {
                            await trackingGit.CommitSingleOrphan(
                                trackingBranchName,
                                messageWithBuildIndex,
                                "-A");

                            await trackingGit.PushForceWithLease(trackingBranchName, isLocal: !hasProjectOrigin);
                        }
                        else
                        {
                            await trackingGit.Commit(messageWithBuildIndex, "-A");
                            await trackingGit.Push(trackingBranchName, isLocal: !hasProjectOrigin);
                        }

                        return currentBuildIndex;
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        Debug.LogWarning(
                            $"[BuildTracker] Push failed on attempt {attempt}/{attempts}. Details: {ex.Message}");

                        if (attempt < attempts)
                        {
                            await Task.Delay(delayMs);
                        }
                    }
                }

                Debug.LogError(
                    $"[BuildTracker] Failed to push build info to remote tracking branch after {attempts} attempts. " +
                    $"Aborting build. Details: {lastException}");
                throw new InvalidOperationException(
                    $"Failed to push build info to remote tracking branch after {attempts} attempts. " +
                    $"{lastException?.Message}",
                    lastException);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to update build info. {e}");
                throw;
            }
        }

        private static async Task EnsureTrackingBranchExists(
            GitRepository git,
            GitRepository projectGit,
            string repositoryUrl,
            string branchName)
        {
            ProgressBarHandler.Update("Making sure the tracking branch exists...");
            GitRepository.CleanupDirectory(BuildTrackingSettings.InitialRepositoryPath);

            var hasBranch = false;

            if (string.IsNullOrEmpty(repositoryUrl))
            {
                hasBranch = false;
            }
            else if (projectGit != null && await projectGit.HasRemoteRepository(repositoryUrl))
            {
                var attempts = Math.Max(1, s_Config.PushRetryAttempts);
                var delayMs = Math.Max(100, s_Config.PushRetryDelayMs);

                for (var attempt = 1; attempt <= attempts; attempt++)
                {
                    try
                    {
                        var result = await projectGit.SendCommand($"ls-remote --heads origin {branchName}",
                            "Checked for remote branch via project git",
                            "Failed to check for remote branch via project git");

                        hasBranch = !string.IsNullOrEmpty(result);
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (attempt == attempts)
                        {
                            throw;
                        }

                        Debug.LogWarning(
                            $"[BuildTracker] Check remote branch attempt {attempt}/{attempts} failed: " +
                            $"{ex.Message}. Retrying in {delayMs}ms...");
                        await Task.Delay(delayMs);
                    }
                }
            }
            else if (!await git.HasRemoteRepository(repositoryUrl))
            {
                ProgressBarHandler.StepUpdate(
                    "Making sure the tracking branch exists: Initializing the tracking repository...");
                GitRepository.CleanupDirectory(BuildTrackingSettings.RepositoryPath);

                await git.Init();

                await git.SendCommand($"remote add remote-repo {repositoryUrl}",
                    "Added remote repository",
                    "Failed to add remote repository");

                var attempts = Math.Max(1, s_Config.PushRetryAttempts);
                var delayMs = Math.Max(100, s_Config.PushRetryDelayMs);

                for (var attempt = 1; attempt <= attempts; attempt++)
                {
                    try
                    {
                        var result = await git.SendCommand($"ls-remote --heads remote-repo {branchName}",
                            "Checked for remote branch",
                            "Failed to check for remote branch");

                        hasBranch = !string.IsNullOrEmpty(result);
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (attempt == attempts)
                        {
                            throw;
                        }

                        Debug.LogWarning(
                            $"[BuildTracker] Check remote branch attempt {attempt}/{attempts} failed: " +
                            $"{ex.Message}. Retrying in {delayMs}ms...");
                        await Task.Delay(delayMs);
                    }
                }

                GitRepository.CleanupDirectory(BuildTrackingSettings.RepositoryPath);
            }
            else
            {
                hasBranch = await git.HasOriginBranch(branchName);
                ProgressBarHandler.Step();
            }

            if (!hasBranch && !string.IsNullOrEmpty(repositoryUrl))
            {
                ProgressBarHandler.StepUpdate(
                    "Making sure the tracking branch exists: Creating a tracking branch in the repository...");
                GitRepository.CleanupDirectory(BuildTrackingSettings.RepositoryPath);

                GitRepository tempGit = new(BuildTrackingSettings.InitialRepositoryPath, s_Config.IsLogging);
                await tempGit.Init();

                await tempGit.SendCommand($"remote add remote-repo {repositoryUrl}",
                    "Added remote repository",
                    "Failed to add remote repository");

                await tempGit.CreateBranch(branchName);
                await tempGit.Commit("Initial");

                var attempts = Math.Max(1, s_Config.PushRetryAttempts);
                var delayMs = Math.Max(100, s_Config.PushRetryDelayMs);

                for (var attempt = 1; attempt <= attempts; attempt++)
                {
                    try
                    {
                        await tempGit.SendCommand($"push remote-repo {branchName}",
                            "Pushed to remote repository",
                            "Failed to push to remote repository");
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (attempt == attempts)
                        {
                            throw;
                        }

                        Debug.LogWarning(
                            $"[BuildTracker] Initial branch push attempt {attempt}/{attempts} failed: " +
                            $"{ex.Message}. Retrying in {delayMs}ms...");
                        await Task.Delay(delayMs);
                    }
                }

                await AutoIgnoreBranchIfFirstCreated(projectGit, branchName);
            }
            else
            {
                ProgressBarHandler.Step();
            }
        }

        private static async Task AutoIgnoreBranchIfFirstCreated(GitRepository projectGit, string branchName)
        {
            try
            {
                var isIgnored = await projectGit.IsBranchIgnoredInFetch(branchName);
                if (!isIgnored)
                {
                    await projectGit.IgnoreBranchInFetch(branchName);
                }
            }
            catch (Exception ex)
            {
                if (s_Config.IsLogging)
                {
                    Debug.LogWarning(
                        $"[BuildTracker] Failed to auto-ignore tracking branch in local Git: {ex.Message}");
                }
            }
        }

        private static async Task PrepareTrackingRepository(
            GitRepository trackingGit,
            string repositoryUrl,
            string projectBranch,
            bool isLocal)
        {
            ProgressBarHandler.Update("Preparing tracking repository...");

            if (!isLocal && !await trackingGit.HasRemoteRepository(repositoryUrl))
            {
                ProgressBarHandler.StepUpdate("Preparing tracking repository: Cloning single branch...");
                GitRepository.CleanupDirectory(BuildTrackingSettings.RepositoryPath);
                await trackingGit.CloneSingleBranch(repositoryUrl, projectBranch);
            }
            else
            {
                ProgressBarHandler.Step();
            }

            if (!isLocal)
            {
                ProgressBarHandler.StepUpdate(
                    "Preparing tracking repository: Getting the previous build index...");
                await trackingGit.Fetch();
                await trackingGit.CheckoutBranch(projectBranch);
            }

            bool hasBranch;

            if (isLocal)
            {
                hasBranch = await trackingGit.HasLocalBranch(projectBranch);
            }
            else
            {
                hasBranch = await trackingGit.HasOriginBranch(projectBranch);
            }

            if (hasBranch)
            {
                if (!isLocal)
                {
                    await trackingGit.Pull(projectBranch);
                }
            }
            else
            {
                CreateBuildIndexJson();
            }
        }

        private static JObject GetBuildIndexJson()
        {
            var jsonFilePath = Path.Combine(
                BuildTrackingSettings.RepositoryPath,
                BuildTrackingSettings.BuildInfoFileName);

            if (!File.Exists(jsonFilePath))
            {
                return CreateBuildIndexJson();
            }

            try
            {
                var text = File.ReadAllText(jsonFilePath);
                return string.IsNullOrWhiteSpace(text) ? CreateBuildIndexJson() : JObject.Parse(text);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                var backupPath = $"{jsonFilePath}.corrupt.{DateTime.UtcNow:yyyyMMddHHmmss}";
                try
                {
                    File.Copy(jsonFilePath, backupPath, true);
                }
                catch
                {
                }

                Debug.LogError(
                    $"[BuildTracker] Failed to read or parse {jsonFilePath}. Backed up to {backupPath}: {ex.Message}");
                throw new InvalidOperationException(
                    $"Corrupted or inaccessible buildInfo.json at '{jsonFilePath}'. Backed up to '{backupPath}'. " +
                    $"Please inspect or resolve before building to prevent history loss.", ex);
            }
        }

        private static JObject CreateBuildIndexJson()
        {
            var jsonObject = new JObject
            {
                [BuildTrackingSettings.LastBuildIndexKey] = 0,
                [BuildTrackingSettings.HistoryKey] = new JArray()
            };

            var dir = Path.GetDirectoryName(s_JsonFilePath);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            {
                AtomicWriteText(s_JsonFilePath, jsonObject.ToString(Formatting.Indented));
            }

            return jsonObject;
        }

        private static Task<int> IncrementBuildIndexAndRecordHistory(
            string commit,
            string branch,
            string dateUtc,
            string appVersion)
        {
            using var mutex = new System.Threading.Mutex(false, "Global\\BuildTool_Tracking_FileLock");
            var hasLock = false;
            try
            {
                try
                {
                    hasLock = mutex.WaitOne(10000);
                }
                catch (System.Threading.AbandonedMutexException)
                {
                    hasLock = true;
                }

                var jsonObject = GetBuildIndexJson();
                var indexToken = jsonObject[BuildTrackingSettings.LastBuildIndexKey];

                int newIndex;
                if (indexToken != null && indexToken.Type == JTokenType.Integer)
                {
                    newIndex = indexToken.Value<int>() + 1;
                }
                else
                {
                    newIndex = 1;
                }

                jsonObject[BuildTrackingSettings.LastBuildIndexKey] = newIndex;

                JArray historyArray;
                if (jsonObject[BuildTrackingSettings.HistoryKey] is JArray existingHistory)
                {
                    historyArray = existingHistory;
                }
                else
                {
                    historyArray = new JArray();
                    jsonObject[BuildTrackingSettings.HistoryKey] = historyArray;
                }

                var entry = new BuildHistoryEntry(newIndex, dateUtc, commit, branch, appVersion);
                historyArray.Add(JObject.FromObject(entry));

                if (s_Config.MaxHistoryEntries > 0 && historyArray.Count > s_Config.MaxHistoryEntries)
                {
                    while (historyArray.Count > s_Config.MaxHistoryEntries)
                    {
                        historyArray.RemoveAt(0);
                    }
                }

                AtomicWriteText(s_JsonFilePath, jsonObject.ToString(Formatting.Indented));

                return Task.FromResult(newIndex);
            }
            finally
            {
                if (hasLock)
                {
                    mutex.ReleaseMutex();
                }
            }
        }

        private static void AtomicWriteText(string path, string content)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var tempFile = Path.Combine(dir ?? Path.GetTempPath(), $"{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(tempFile, content, System.Text.Encoding.UTF8);
                if (File.Exists(path))
                {
                    File.Replace(tempFile, path, null);
                }
                else
                {
                    File.Move(tempFile, path);
                }
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    try
                    {
                        File.Delete(tempFile);
                    }
                    catch
                    {
                    }
                }
            }
        }

        public static async Task FinalizeRemoteBuildReport(string reportContent)
        {
            if (string.IsNullOrWhiteSpace(reportContent))
            {
                return;
            }

            try
            {
                var trackingBranchName = s_Config.GetBranchName();
                var trackingGit = new GitRepository(BuildTrackingSettings.RepositoryPath, s_Config.IsLogging);

                if (!trackingGit.HasRepository())
                {
                    return;
                }

                var reportsDir = Path.Combine(
                    BuildTrackingSettings.RepositoryPath, BuildTrackingSettings.ReportsDirectoryName);
                if (!Directory.Exists(reportsDir))
                {
                    Directory.CreateDirectory(reportsDir);
                }

                var index = CurrentTrackedBuildIndex.ToString();
                var filename = $"BuildReport_{index}.log";
                var reportPath = Path.Combine(reportsDir, filename);

                File.WriteAllText(reportPath, reportContent, System.Text.Encoding.UTF8);

                var commitMessage = $"report: build {index}";
                await trackingGit.Commit(commitMessage, "-A");

                var hasOrigin = await trackingGit.HasOrigin();
                if (hasOrigin)
                {
                    await trackingGit.Push(trackingBranchName);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[BuildTracker] Failed to record build report to remote tracking branch: {ex.Message}");
            }
        }
    }
}
