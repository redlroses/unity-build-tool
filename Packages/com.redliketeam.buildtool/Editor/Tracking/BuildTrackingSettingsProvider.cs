using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace BuildTool.Tracking.Editor
{
    public static class BuildTrackingSettingsProvider
    {
        public const string SettingsPath = "Project/Build Tool/Tracking";

        private static string GetUssPath()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(BuildTrackingSettingsProvider).Assembly);
            var root = info != null && !string.IsNullOrEmpty(info.assetPath) ? info.assetPath : "Packages/com.redliketeam.buildtool";
            return $"{root}/Editor/Tracking/BuildTrackingSettings.uss";
        }

        private sealed class GitRefspecViewState
        {
            public bool IsBranchIgnored;
            public bool HasCheckedRefspec;
            public bool IsCheckingStatus;
            public bool IsOperating;
            public string CheckErrorMessage;
        }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            var provider = new SettingsProvider(SettingsPath, SettingsScope.Project)
            {
                label = "Tracking",
                activateHandler = (searchContext, rootElement) =>
                {
                    BuildUI(rootElement);
                },
                keywords = new HashSet<string>(new[]
                {
                    "Tracking", "Git", "Branch", "Override", "Refspec", "Optimization", "Commit", "Build",
                    "Repository", "Remote", "URL"
                })
            };

            return provider;
        }

        private static void BuildUI(VisualElement rootElement)
        {
            rootElement.Clear();
            rootElement.AddToClassList("tracking-settings-root");

            LoadStyleSheet(rootElement);

            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.AddToClassList("tracking-settings-scroll");
            rootElement.Add(scrollView);

            var pageTitle = new Label("Tracking");
            pageTitle.AddToClassList("tracking-page-title");
            scrollView.Add(pageTitle);

            var settings = BuildTrackingSettings.instance;
            SerializedObject serializedSettings = new(settings);

            if (string.Equals(Application.companyName, "DefaultCompany"))
            {
                var companyWarning = new HelpBox(
                    "Default company name should be replaced with your company in Player Settings.",
                    HelpBoxMessageType.Error);
                companyWarning.style.marginBottom = 10;
                scrollView.Add(companyWarning);
            }

            var configCard = new VisualElement();
            configCard.AddToClassList("tracking-section-card");
            scrollView.Add(configCard);

            var configTitle = new Label("Git Tracking Configuration");
            configTitle.AddToClassList("tracking-section-title");
            configCard.Add(configTitle);

            var hint = new Label(
                "Configure tracking repository branch, local synchronization filters, and retry policies.");
            hint.AddToClassList("tracking-hint-text");
            configCard.Add(hint);

            Action updateGitSection = null;
            var refspecState = new GitRefspecViewState();

            var fieldsContainer = new VisualElement();
            fieldsContainer.AddToClassList("tracking-fields-container");

            var branchProp = serializedSettings.FindProperty(nameof(settings.ProjectBranchOverride));
            var branchField = new PropertyField(branchProp, "Project Branch Override")
            {
                tooltip = "Override tracking branch name (defaults to current project Git branch)."
            };
            branchField.RegisterValueChangeCallback(_ =>
            {
                settings.Save();
                updateGitSection?.Invoke();
            });
            fieldsContainer.Add(branchField);

            var repoUrlProp = serializedSettings.FindProperty(nameof(settings.GitRepositoryUrlOverride));
            var repoUrlField = new PropertyField(repoUrlProp, "Git Repository URL Override")
            {
                tooltip = "Override remote Git repository URL for tracking commits (defaults to current project Git remote repository)."
            };
            repoUrlField.RegisterValueChangeCallback(evt =>
            {
                settings.Save();
                if (string.IsNullOrWhiteSpace(settings.GitRepositoryUrlOverride) && !refspecState.HasCheckedRefspec)
                {
                    _ = CheckBranchIgnoredStatus(settings, refspecState, updateGitSection);
                }
                updateGitSection?.Invoke();
            });
            fieldsContainer.Add(repoUrlField);

            var pathProp = serializedSettings.FindProperty(nameof(settings.RelativeProjectGitPathOverride));
            var pathField = new PropertyField(pathProp, "Relative Git Path Override")
            {
                tooltip = "Relative path to Git repository root if Unity project is located in a subfolder."
            };
            pathField.RegisterValueChangeCallback(_ => settings.Save());
            fieldsContainer.Add(pathField);

            var keepSingleProp = serializedSettings.FindProperty(nameof(settings.KeepSingleCommit));
            var keepSingleField = new PropertyField(keepSingleProp, "Keep Single Commit (Force Push)")
            {
                tooltip = "Force-push single orphan commit per build to keep tracking repository size minimal."
            };
            keepSingleField.RegisterValueChangeCallback(_ => settings.Save());
            fieldsContainer.Add(keepSingleField);

            var historyProp = serializedSettings.FindProperty(nameof(settings.MaxHistoryEntries));
            var historyField = new PropertyField(historyProp, "Max History Entries (0 = unlimited)")
            {
                tooltip = "Maximum number of build records stored in buildInfo.json history (0 for unlimited)."
            };
            historyField.RegisterValueChangeCallback(_ => settings.Save());
            fieldsContainer.Add(historyField);

            var loggingProp = serializedSettings.FindProperty(nameof(settings.IsLogging));
            var loggingField = new PropertyField(loggingProp, "Enable Logging")
            {
                tooltip = "Log detailed tracking and Git operations to Unity Console."
            };
            loggingField.RegisterValueChangeCallback(_ => settings.Save());
            fieldsContainer.Add(loggingField);

            fieldsContainer.Bind(serializedSettings);
            configCard.Add(fieldsContainer);

            var gitCard = new VisualElement();
            gitCard.AddToClassList("tracking-section-card");
            scrollView.Add(gitCard);

            updateGitSection = BuildGitOptimizationSection(gitCard, settings, refspecState);
        }

        private static void LoadStyleSheet(VisualElement rootElement)
        {
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(GetUssPath());
            if (styleSheet == null)
            {
                var guids = AssetDatabase.FindAssets("BuildTrackingSettings t:StyleSheet");
                if (guids.Length > 0)
                {
                    var foundPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                    styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(foundPath);
                }
            }

            if (styleSheet != null && !rootElement.styleSheets.Contains(styleSheet))
            {
                rootElement.styleSheets.Add(styleSheet);
            }
        }

        private static Action BuildGitOptimizationSection(
            VisualElement container, BuildTrackingSettings settings, GitRefspecViewState state)
        {
            container.Clear();

            var title = new Label("Git Optimization");
            title.AddToClassList("tracking-section-title");
            container.Add(title);

            var subtitle = new Label("Local Fetch Filter (Negative Refspec)");
            subtitle.AddToClassList("tracking-section-subtitle");
            container.Add(subtitle);

            var desc = new Label(
                "Automated build tracking saves commits with build metadata and indices on the tracking branch. "
                + "Excluding this branch from local synchronization prevents local repository bloat and speeds up "
                + "'git fetch' and 'git pull' commands.");
            desc.AddToClassList("tracking-hint-text");
            container.Add(desc);

            var statusBanner = new Label();
            statusBanner.AddToClassList("tracking-status-banner");
            container.Add(statusBanner);

            var actionBtn = new Button();
            actionBtn.AddToClassList("tracking-btn");
            container.Add(actionBtn);

            void updateUI()
            {
                var branchName = settings.GetBranchName();

                statusBanner.RemoveFromClassList("tracking-status-active");
                statusBanner.RemoveFromClassList("tracking-status-inactive");
                statusBanner.RemoveFromClassList("tracking-status-pending");

                if (state.IsOperating)
                {
                    statusBanner.AddToClassList("tracking-status-pending");
                    statusBanner.text = $"Applying Git configuration for '{branchName}', please wait...";
                    actionBtn.text = "Processing Git Configuration...";
                    actionBtn.SetEnabled(false);
                }
                else if (!string.IsNullOrWhiteSpace(settings.GitRepositoryUrlOverride))
                {
                    statusBanner.AddToClassList("tracking-status-active");
                    statusBanner.text =
                        $"External Repository Configured: Tracking commits are pushed to external repository:\n"
                        + $"{settings.GitRepositoryUrlOverride.Trim()}\n"
                        + "Local project Git fetch filtering is not needed.";
                    actionBtn.text = "External Repository Configured";
                    actionBtn.SetEnabled(false);
                }
                else if (!state.HasCheckedRefspec)
                {
                    statusBanner.AddToClassList("tracking-status-pending");
                    if (!string.IsNullOrEmpty(state.CheckErrorMessage))
                    {
                        statusBanner.text = $"Git Check Failed: {state.CheckErrorMessage}\n"
                            + "Verify Git is installed and available in PATH or project directory.";
                        actionBtn.text = "Retry Git Status Check";
                        actionBtn.SetEnabled(true);
                    }
                    else
                    {
                        statusBanner.text = $"Checking Git fetch status for branch '{branchName}'...";
                        actionBtn.text = "Checking Git Status...";
                        actionBtn.SetEnabled(false);
                    }
                }
                else if (state.IsBranchIgnored)
                {
                    statusBanner.AddToClassList("tracking-status-active");
                    statusBanner.text =
                        $"Active: Branch '{branchName}' is EXCLUDED from local Git fetch "
                        + $"(^refs/heads/{branchName}).\n"
                        + "Tracking commits and history will not be downloaded to this machine during fetch or pull.";
                    actionBtn.text = "Stop Ignoring Branch (Download on Fetch)";
                    actionBtn.SetEnabled(true);
                }
                else
                {
                    statusBanner.AddToClassList("tracking-status-inactive");
                    statusBanner.text =
                        $"Inactive: Branch '{branchName}' is NOT ignored in local Git.\n"
                        + "Tracking commits will be downloaded normally. It is strongly recommended to "
                        + "enable optimization to save bandwidth and keep history clean.";
                    actionBtn.text = "Ignore Branch in Local Fetch (Recommended)";
                    actionBtn.SetEnabled(true);
                }
            }

            actionBtn.clicked += () =>
            {
                if (state.IsOperating)
                {
                    return;
                }

                if (!state.HasCheckedRefspec)
                {
                    state.CheckErrorMessage = null;
                    _ = CheckBranchIgnoredStatus(settings, state, updateUI);
                    return;
                }

                if (state.IsBranchIgnored)
                {
                    _ = UnignoreBranch(settings, state, updateUI);
                }
                else
                {
                    _ = IgnoreBranch(settings, state, updateUI);
                }
            };

            if (string.IsNullOrWhiteSpace(settings.GitRepositoryUrlOverride) &&
                !state.HasCheckedRefspec && !state.IsOperating && !state.IsCheckingStatus)
            {
                _ = CheckBranchIgnoredStatus(settings, state, updateUI);
            }

            updateUI();
            return updateUI;
        }

        private static async Task CheckBranchIgnoredStatus(
            BuildTrackingSettings settings, GitRefspecViewState state, Action onComplete)
        {
            if (state.IsCheckingStatus || state.IsOperating)
            {
                return;
            }

            state.IsCheckingStatus = true;
            try
            {
                var projectGit = new GitRepository(settings.GetProjectGitPath(), settings.IsLogging);
                state.IsBranchIgnored = await projectGit.IsBranchIgnoredInFetch(settings.GetBranchName());
                state.HasCheckedRefspec = true;
                state.CheckErrorMessage = null;
            }
            catch (Exception ex)
            {
                if (settings.IsLogging)
                {
                    Debug.LogWarning($"[BuildTrackingSettingsProvider] Check branch status error: {ex.Message}");
                }
                state.HasCheckedRefspec = false;
                state.CheckErrorMessage = ex.Message;
            }
            finally
            {
                state.IsCheckingStatus = false;
                EditorApplication.delayCall += () => onComplete?.Invoke();
            }
        }

        private static async Task IgnoreBranch(
            BuildTrackingSettings settings, GitRefspecViewState state, Action onComplete)
        {
            if (state.IsOperating)
            {
                return;
            }

            state.IsOperating = true;
            onComplete?.Invoke();

            try
            {
                var projectGit = new GitRepository(settings.GetProjectGitPath(), settings.IsLogging);
                await projectGit.IgnoreBranchInFetch(settings.GetBranchName());
                state.IsBranchIgnored = await projectGit.IsBranchIgnoredInFetch(settings.GetBranchName());
                state.HasCheckedRefspec = true;
                state.CheckErrorMessage = null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BuildTrackingSettingsProvider] Failed to ignore branch in local Git: {ex.Message}");
                state.HasCheckedRefspec = false;
                state.CheckErrorMessage = ex.Message;
            }
            finally
            {
                state.IsOperating = false;
                EditorApplication.delayCall += () => onComplete?.Invoke();
            }
        }

        private static async Task UnignoreBranch(
            BuildTrackingSettings settings, GitRefspecViewState state, Action onComplete)
        {
            if (state.IsOperating)
            {
                return;
            }

            state.IsOperating = true;
            onComplete?.Invoke();

            try
            {
                var projectGit = new GitRepository(settings.GetProjectGitPath(), settings.IsLogging);
                await projectGit.UnignoreBranchInFetch(settings.GetBranchName());
                state.IsBranchIgnored = await projectGit.IsBranchIgnoredInFetch(settings.GetBranchName());
                state.HasCheckedRefspec = true;
                state.CheckErrorMessage = null;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BuildTrackingSettingsProvider] Failed to unignore branch in local Git: {ex.Message}");
                state.HasCheckedRefspec = false;
                state.CheckErrorMessage = ex.Message;
            }
            finally
            {
                state.IsOperating = false;
                EditorApplication.delayCall += () => onComplete?.Invoke();
            }
        }

        public static BuildTrackingSettings GetSettings()
        {
            return BuildTrackingSettings.instance;
        }
    }
}
