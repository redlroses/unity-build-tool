using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using BuildTool.Editor.Notifications;
using BuildTool.Editor.Processors;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BuildTool.Editor
{
    [InitializeOnLoad]
    public class BuildToolWelcomeWindow : EditorWindow
    {
        public const string WindowTitle = "Welcome to Build Tool";
        public static string UssPath => PackagePathHelper.GetAssetPath("Editor/Core/Welcome/BuildToolWelcomeWindow.uss");
        public const string DefaultVersion = "1.0.5";
        public const string ShowOnUpdatePrefKey = "build_tool_welcome_show_on_update";
        public const string ShowOnStartPrefKey = "build_tool_welcome_show_on_startup";
        public const string LastVersionPrefKey = "build_tool_welcome_last_version";

        private static string PackageJsonPath => PackagePathHelper.GetFilePath("package.json");
        private static string ChangelogPath => PackagePathHelper.GetFilePath("CHANGELOG.md");
        private const string RootChangelogPath = "CHANGELOG.md";
        private static string ReadmePath => PackagePathHelper.GetFilePath("README.md");
        private const string RootReadmePath = "README.md";
        public static string BuildManifestSamplePath => PackagePathHelper.GetAssetPath("Samples~/BuildManifest");
        public static string BuildInfoSamplePath => PackagePathHelper.GetAssetPath("Samples~/BuildInfo");
        public static string SamplePath => BuildManifestSamplePath;
        public const string AuthorName = "redlroses";
        public const string AuthorEmail = "redlroses.rlr@gmail.com";
        public const string AuthorGitHubUrl = "https://github.com/redlroses";
        private const string LicenseNotice = "Copyright (c) 2026 RedLikeTeam";
        private const string GitRepoUrl = "https://github.com/redlroses/unity-build-tool";
        private const string GitIssuesUrl = "https://github.com/redlroses/unity-build-tool/issues";
        private const string GitPackageUrl =
            "https://github.com/redlroses/unity-build-tool.git?path=Packages/com.redliketeam.buildtool#v1.0.5";

        internal enum WelcomeTab
        {
            GettingStarted,
            Features,
            Samples,
            ReleaseNotes,
            Resources,
            AboutAndCredits
        }

        [SerializeField] private WelcomeTab _currentTab = WelcomeTab.GettingStarted;
        private ScrollView _contentContainer;
        private readonly List<(WelcomeTab Tab, VisualElement Element, Label Label)> _navElements = new();

        static BuildToolWelcomeWindow()
        {
            EditorApplication.delayCall += CheckStartup;
        }

        public static void CheckStartup()
        {
            if (Application.isBatchMode)
            {
                return;
            }

            var hasShownOnboardingKey = "build_tool_welcome_onboarding_shown".AppendProjectName();
            var hasShownOnboarding = EditorPrefs.GetBool(hasShownOnboardingKey, false);
            var showOnStart = EditorPrefs.GetBool(ShowOnStartPrefKey, false);
            var showOnUpdate = EditorPrefs.GetBool(ShowOnUpdatePrefKey, true);
            var lastVersion = EditorPrefs.GetString(LastVersionPrefKey, string.Empty);
            var currentVersion = GetPackageVersion();
            var isUpdated = !string.IsNullOrEmpty(lastVersion) &&
                            !string.Equals(lastVersion, currentVersion, StringComparison.OrdinalIgnoreCase);

            if (!hasShownOnboarding || showOnStart || (showOnUpdate && isUpdated))
            {
                EditorPrefs.SetBool(hasShownOnboardingKey, true);
                EditorPrefs.SetString(LastVersionPrefKey, currentVersion);
                ShowWindow();
            }
            else if (string.IsNullOrEmpty(lastVersion))
            {
                EditorPrefs.SetString(LastVersionPrefKey, currentVersion);
            }
        }

        [MenuItem("Tools/Build Tool/Welcome Screen", priority = 20)]
        public static void ShowWindow()
        {
            var window = GetWindow<BuildToolWelcomeWindow>(WindowTitle);
            window.minSize = new Vector2(640, 480);
            window.titleContent = new GUIContent(WindowTitle, GetIcon("BuildSettings.Editor", "d_CustomTool"));
            window.Show();
        }

        public static string GetPackageVersion()
        {
            try
            {
                if (File.Exists(PackageJsonPath))
                {
                    var text = File.ReadAllText(PackageJsonPath);
                    var match = Regex.Match(text, @"""version""\s*:\s*""([^""]+)""");
                    if (match.Success)
                    {
                        return match.Groups[1].Value;
                    }
                }
            }
            catch
            {
            }

            return DefaultVersion;
        }

        private static readonly MethodInfo s_LoadIconMethod = typeof(EditorGUIUtility).GetMethod(
            "LoadIcon",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
            null,
            new[] { typeof(string) },
            null);

        private static Texture TryLoadIcon(string iconName)
        {
            if (string.IsNullOrEmpty(iconName))
            {
                return null;
            }

            if (s_LoadIconMethod != null)
            {
                try
                {
                    return s_LoadIconMethod.Invoke(null, new object[] { iconName }) as Texture;
                }
                catch
                {
                }
            }

            var found = EditorGUIUtility.FindTexture(iconName);
            if (found != null)
            {
                return found;
            }

            var content = EditorGUIUtility.IconContent(iconName);
            return content != null ? content.image : null;
        }

        public static Texture GetIcon(string iconName, string fallbackIconName = null)
        {
            var icon = TryLoadIcon(iconName);
            if (icon != null)
            {
                return icon;
            }

            if (!string.IsNullOrEmpty(fallbackIconName))
            {
                return TryLoadIcon(fallbackIconName);
            }

            return null;
        }

        private void OnEnable()
        {
        }

        public void CreateGUI()
        {
            BuildUI();
        }

        public void BuildUI()
        {
            var root = rootVisualElement;
            root.Clear();
            _navElements.Clear();

            LoadStyleSheet(root);

            var container = new VisualElement();
            container.AddToClassList("welcome-root");
            container.style.position = Position.Absolute;
            container.style.left = 0;
            container.style.top = 0;
            container.style.right = 0;
            container.style.bottom = 0;
            root.Add(container);

            BuildHeader(container);

            var body = new VisualElement();
            body.AddToClassList("welcome-body");
            container.Add(body);

            BuildSidebar(body);

            _contentContainer = new ScrollView(ScrollViewMode.Vertical);
            _contentContainer.AddToClassList("welcome-content-scroll");
            body.Add(_contentContainer);

            BuildFooter(container);

            SwitchTab(_currentTab);
        }

        private void LoadStyleSheet(VisualElement root)
        {
            var styleSheet = PackagePathHelper.LoadStyleSheet("Editor/Core/Welcome/BuildToolWelcomeWindow.uss", "BuildToolWelcomeWindow");
            if (styleSheet != null && !root.styleSheets.Contains(styleSheet))
            {
                root.styleSheets.Add(styleSheet);
            }
        }

        private void BuildHeader(VisualElement root)
        {
            var header = new VisualElement();
            header.AddToClassList("welcome-header");

            var titleRow = new VisualElement();
            titleRow.AddToClassList("welcome-title-row");

            var titleGroup = new VisualElement();
            titleGroup.AddToClassList("welcome-title-group");

            var iconImage = new Image();
            iconImage.AddToClassList("welcome-title-icon");
            iconImage.image = GetIcon("BuildSettings.Editor", "d_CustomTool");
            titleGroup.Add(iconImage);

            var titleLabel = new Label("Build Tool for Unity");
            titleLabel.AddToClassList("welcome-title");
            titleGroup.Add(titleLabel);

            titleRow.Add(titleGroup);

            var versionBadge = new Label($"v{GetPackageVersion()}");
            versionBadge.AddToClassList("welcome-version-badge");
            titleRow.Add(versionBadge);

            header.Add(titleRow);

            var subtitle = new Label(
                "Automate, orchestrate, and track multi-platform builds with pre/post-processing extensions, "
                + "notification webhooks, and CI/CD CLI support.");
            subtitle.AddToClassList("welcome-subtitle");
            header.Add(subtitle);

            root.Add(header);
        }

        private void BuildSidebar(VisualElement parent)
        {
            var sidebar = new VisualElement();
            sidebar.AddToClassList("welcome-sidebar");

            AddNavItem(sidebar, WelcomeTab.GettingStarted, "Getting Started", "d_PlayButton", "d_Favorite");
            AddNavItem(sidebar, WelcomeTab.Features, "Features & Status", "d_FilterByType", "d_Settings");
            AddNavItem(sidebar, WelcomeTab.Samples, "Samples", "d_Package Manager", "d_Prefab Icon");
            AddNavItem(sidebar, WelcomeTab.ReleaseNotes, "Release Notes",
                "d_UnityEditor.ConsoleWindow", "TextAsset Icon");
            AddNavItem(sidebar, WelcomeTab.Resources, "Resources & Support", "d__Help", "d_Linked");
            AddNavItem(sidebar, WelcomeTab.AboutAndCredits, "About & Credits",
                "d_UnityEditor.InspectorWindow", "console.infoicon");

            parent.Add(sidebar);
        }

        private void AddNavItem(
            VisualElement sidebar, WelcomeTab tab, string text, string iconName, string fallbackIcon)
        {
            var navItem = new VisualElement();
            navItem.AddToClassList("welcome-nav-item");

            var icon = new Image();
            icon.AddToClassList("welcome-nav-icon");
            icon.image = GetIcon(iconName, fallbackIcon);
            navItem.Add(icon);

            var label = new Label(text);
            label.AddToClassList("welcome-nav-label");
            navItem.Add(label);

            navItem.tooltip = text;

            navItem.RegisterCallback<ClickEvent>(_ => SwitchTab(tab));

            _navElements.Add((tab, navItem, label));
            sidebar.Add(navItem);
        }

        internal void SwitchTab(WelcomeTab tab)
        {
            _currentTab = tab;

            foreach (var item in _navElements)
            {
                bool isSelected = item.Tab == tab;
                item.Element.EnableInClassList("welcome-nav-item-active", isSelected);
                item.Label.EnableInClassList("welcome-nav-label-active", isSelected);
            }

            if (_contentContainer == null)
            {
                return;
            }

            _contentContainer.Clear();
            _contentContainer.scrollOffset = Vector2.zero;

            switch (tab)
            {
                case WelcomeTab.GettingStarted:
                    BuildGettingStartedTab(_contentContainer);
                    break;
                case WelcomeTab.Features:
                    BuildFeaturesTab(_contentContainer);
                    break;
                case WelcomeTab.Samples:
                    BuildSamplesTab(_contentContainer);
                    break;
                case WelcomeTab.ReleaseNotes:
                    BuildReleaseNotesTab(_contentContainer);
                    break;
                case WelcomeTab.Resources:
                    BuildResourcesTab(_contentContainer);
                    break;
                case WelcomeTab.AboutAndCredits:
                    BuildAboutAndCreditsTab(_contentContainer);
                    break;
            }
        }

        private void BuildGettingStartedTab(VisualElement container)
        {
            var header = new VisualElement();
            header.AddToClassList("welcome-section-header");

            var title = new Label("Getting Started");
            title.AddToClassList("welcome-section-title");
            header.Add(title);

            var desc = new Label(
                "Follow this quick launch guide to configure build profiles, adjust modules, "
                + "and run your first automated build pipeline.");
            desc.AddToClassList("welcome-section-desc");
            header.Add(desc);

            container.Add(header);

            BuildReadinessBox(container);

            BuildStepCard(
                container,
                1,
                "Open Build Window",
                "Select build profiles, switch active targets, configure clear/dev flags, "
                + "and run batch builds in one click.",
                "Open Build Window",
                BuildWindow.ShowWindow,
                "Ctrl + Shift + Alt + B"
            );

            BuildStepCard(
                container,
                2,
                "Setup Modular Extensions",
                "Enable optional modules without editing code: Git build index tracking, notification webhooks, "
                + "and native Unity CLI commands.",
                "Open Setup Wizard",
                BuildToolWizardWindow.ShowWindow
            );

            BuildStepCard(
                container,
                3,
                "Configure Output Paths & Settings",
                "Set destination directory naming patterns with tokens like {Version}, {BuildIndex}, and {Platform}. "
                + "Connect Discord, Telegram, or Slack channels.",
                "Open Project Settings",
                () => SettingsService.OpenProjectSettings("Project/Build Tool")
            );
        }

        private void BuildReadinessBox(VisualElement container)
        {
            var box = new VisualElement();
            box.AddToClassList("welcome-readiness-box");

            var profiles = BuildTool.GetBuildProfiles();
            int profileCount = profiles?.Count ?? 0;

            var profileBadge = new Label(profileCount > 0
                ? $"● {profileCount} Build Profile(s) Configured"
                : "○ No Build Profiles Found");
            profileBadge.AddToClassList("welcome-badge");
            profileBadge.AddToClassList(profileCount > 0 ? "welcome-badge-success" : "welcome-badge-warning");
            box.Add(profileBadge);

            bool isTracking = BuildTool.IsTrackingEnabled;
            var trackingBadge = new Label(isTracking ? "● Git Tracking Active" : "○ Git Tracking Standby");
            trackingBadge.AddToClassList("welcome-badge");
            trackingBadge.AddToClassList(isTracking ? "welcome-badge-success" : "welcome-badge-neutral");
            box.Add(trackingBadge);

            bool isNotif = BuildNotificationSettings.MasterEnabled;
            int notifCount = BuildNotificationManager.ActiveChannelsCount;
            var notifBadge = new Label(isNotif && notifCount > 0
                ? $"● Notifications Active ({notifCount})"
                : (isNotif ? "○ Notifications Enabled (0 Channels)" : "○ Notifications Off"));
            notifBadge.AddToClassList("welcome-badge");
            notifBadge.AddToClassList(isNotif && notifCount > 0 ? "welcome-badge-success" : "welcome-badge-neutral");
            box.Add(notifBadge);

            container.Add(box);
        }

        private void BuildStepCard(
            VisualElement container,
            int stepNumber,
            string stepTitle,
            string stepDescription,
            string actionButtonText,
            Action action,
            string shortcut = null)
        {
            var card = new VisualElement();
            card.AddToClassList("welcome-step-card");

            var header = new VisualElement();
            header.AddToClassList("welcome-step-header");

            var titleRow = new VisualElement();
            titleRow.AddToClassList("welcome-step-title-row");

            var num = new Label(stepNumber.ToString());
            num.AddToClassList("welcome-step-number");
            titleRow.Add(num);

            var title = new Label(stepTitle);
            title.AddToClassList("welcome-step-title");
            titleRow.Add(title);

            header.Add(titleRow);

            if (!string.IsNullOrEmpty(shortcut))
            {
                var shortcutBadge = new Label(shortcut);
                shortcutBadge.AddToClassList("welcome-badge");
                shortcutBadge.AddToClassList("welcome-badge-kbd");
                header.Add(shortcutBadge);
            }

            card.Add(header);

            var desc = new Label(stepDescription);
            desc.AddToClassList("welcome-step-desc");
            card.Add(desc);

            var actions = new VisualElement();
            actions.AddToClassList("welcome-step-actions");

            var btn = new Button(action)
            {
                text = actionButtonText
            };
            btn.AddToClassList("welcome-btn");
            btn.AddToClassList("welcome-btn-primary");
            actions.Add(btn);

            card.Add(actions);
            container.Add(card);
        }

        private void BuildFeaturesTab(VisualElement container)
        {
            var header = new VisualElement();
            header.AddToClassList("welcome-section-header");

            var title = new Label("Core Features & Pipeline Capabilities");
            title.AddToClassList("welcome-section-title");
            header.Add(title);

            var desc = new Label(
                "Unity Build Tool orchestrates sequential compilation across platforms with "
                + "extensible pre/post processing and headless CI/CD automation.");
            desc.AddToClassList("welcome-section-desc");
            header.Add(desc);

            container.Add(header);

            AddFeatureCard(
                container,
                "BuildSettings.Editor",
                "Multi-Platform Batch Builds",
                "Queue Standalone, Android, iOS, WebGL, and custom profiles. Execute sequential compilation "
                + "passes with automatic error stops or continue-on-error behavior."
            );

            AddFeatureCard(
                container,
                "d_CustomTool",
                "Extensible Processing Pipeline",
                "Implement IBuildPreprocessor to inject pre-build steps and IBuildPostprocessor to execute "
                + "post-build tasks. Add custom inspector tabs via IBuildWindowExtension."
            );

            AddFeatureCard(
                container,
                "d_Folder Icon",
                "Built-in Zip Archiver",
                "Automatically package compiled binaries and artifacts into zip archives upon build completion, "
                + "with configurable exclusions and output folders."
            );

            AddFeatureCard(
                container,
                "d_UnityEditor.ConsoleWindow",
                "Multi-Channel Notifications",
                "Send real-time build reports, duration statistics, binary size summaries, and error logs "
                + "directly to Discord, Telegram, Slack, Webhooks, or desktop toasts."
            );

            AddFeatureCard(
                container,
                "d_Settings",
                "Smart Path & Token Templating",
                "Define custom folder structures using tokens: {Version}, {BuildIndex}, {Platform}, "
                + "{Subtarget}, {Date:yyyy-MM-dd}, and {Time:HH-mm}."
            );

            AddFeatureCard(
                container,
                "d_PlayButton",
                "Headless CI/CD & Native CLI",
                "Execute batch builds in continuous integration environments via classic Unity batchmode "
                + "(BuildCLI.Build) or native Unity CLI commands."
            );
        }

        private void AddFeatureCard(
            VisualElement container, string iconName, string featureTitle, string featureDescription)
        {
            var card = new VisualElement();
            card.AddToClassList("welcome-feature-card");

            var header = new VisualElement();
            header.AddToClassList("welcome-feature-header");

            var icon = new Image();
            icon.AddToClassList("welcome-feature-icon");
            icon.image = GetIcon(iconName, "d_Settings");
            header.Add(icon);

            var title = new Label(featureTitle);
            title.AddToClassList("welcome-feature-title");
            header.Add(title);

            card.Add(header);

            var desc = new Label(featureDescription);
            desc.AddToClassList("welcome-feature-desc");
            card.Add(desc);

            container.Add(card);
        }

        private void BuildSamplesTab(VisualElement container)
        {
            var header = new VisualElement();
            header.AddToClassList("welcome-section-header");

            var title = new Label("Package Samples & Reference Extensions");
            title.AddToClassList("welcome-section-title");
            header.Add(title);

            var desc = new Label(
                "Explore complete, production-ready extension examples demonstrating UI Toolkit "
                + "integration, pre/post hooks, and CLI parameter handling.");
            desc.AddToClassList("welcome-section-desc");
            header.Add(desc);

            container.Add(header);

            AddSampleCard(
                container,
                "Build Manifest Generator Extension",
                GetSampleBadgeText(BuildManifestSamplePath, "Samples~/BuildManifest"),
                "Generates a comprehensive build-manifest.json containing git hash, branch, build index, "
                + "platform, and timestamp upon each successful profile build. Features full UI Toolkit "
                + "extension panel and CLI --ext support.",
                BuildManifestSamplePath,
                "Build Manifest Extension"
            );

            AddSampleCard(
                container,
                "Build Info Display",
                GetSampleBadgeText(BuildInfoSamplePath, "Samples~/BuildInfo"),
                "Demonstrates runtime build information display and watermark overlay using BuildInfoDisplay "
                + "prefab, BuildInfoView component, and BuildInfoProvider API with clipboard copy support.",
                BuildInfoSamplePath,
                "Build Info Display",
                "BuildInfoDemo"
            );
        }

        private static string GetSampleBadgeText(string samplePath, string defaultBadge)
        {
            if (!Directory.Exists(ResolveSamplePath(samplePath)) &&
                !string.IsNullOrEmpty(ResolveSamplePackagePath(samplePath)))
            {
                return $"Samples/{GetSampleFolderName(samplePath)}.unitypackage";
            }

            return defaultBadge;
        }

        private void AddSampleCard(
            VisualElement container,
            string title,
            string badgeText,
            string description,
            string samplePath,
            string sampleDisplayName,
            string sceneSearchPattern = null)
        {
            var sampleCard = new VisualElement();
            sampleCard.AddToClassList("welcome-sample-box");

            var sampleHeader = new VisualElement();
            sampleHeader.AddToClassList("welcome-step-header");

            var sampleTitle = new Label(title);
            sampleTitle.AddToClassList("welcome-step-title");
            sampleHeader.Add(sampleTitle);

            var sampleBadge = new Label(badgeText);
            sampleBadge.AddToClassList("welcome-badge");
            sampleBadge.AddToClassList("welcome-badge-kbd");
            sampleHeader.Add(sampleBadge);

            sampleCard.Add(sampleHeader);

            var sampleDesc = new Label(description);
            sampleDesc.AddToClassList("welcome-step-desc");
            sampleCard.Add(sampleDesc);

            var actions = new VisualElement();
            actions.AddToClassList("welcome-step-actions");

            bool isImported = IsSampleImported(samplePath, sampleDisplayName);

            var importBtn = new Button(() =>
            {
                ImportSample(samplePath, sampleDisplayName);
                BuildUI();
            })
            {
                text = isImported ? "Reimport Sample" : "Import Sample"
            };
            importBtn.AddToClassList("welcome-btn");
            importBtn.AddToClassList(isImported ? "welcome-btn-secondary" : "welcome-btn-primary");
            actions.Add(importBtn);

            if (!string.IsNullOrEmpty(sceneSearchPattern))
            {
                var sceneGuids = AssetDatabase.FindAssets($"{sceneSearchPattern} t:Scene");
                if (sceneGuids != null && sceneGuids.Length > 0)
                {
                    var sceneAssetPath = AssetDatabase.GUIDToAssetPath(sceneGuids[0]);
                    var openSceneBtn = new Button(() =>
                    {
                        if (UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                        {
                            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(sceneAssetPath);
                        }
                    })
                    {
                        text = "Open Demo Scene"
                    };
                    openSceneBtn.AddToClassList("welcome-btn");
                    openSceneBtn.AddToClassList(isImported ? "welcome-btn-primary" : "welcome-btn-secondary");
                    actions.Add(openSceneBtn);
                }
            }

            var sampleReadmeBtn = new Button(() => OpenSampleReadme(samplePath, sampleDisplayName))
            {
                text = "Open Sample Readme"
            };
            sampleReadmeBtn.AddToClassList("welcome-btn");
            sampleReadmeBtn.AddToClassList("welcome-btn-secondary");
            actions.Add(sampleReadmeBtn);

            sampleCard.Add(actions);
            container.Add(sampleCard);
        }

        private static string GetSampleFolderName(string sampleRelativePath)
        {
            return string.IsNullOrWhiteSpace(sampleRelativePath)
                ? string.Empty
                : Path.GetFileName(sampleRelativePath.TrimEnd('/', '\\'));
        }

        public static string ResolveSamplePackagePath(string sampleRelativePath)
        {
            var folderName = GetSampleFolderName(sampleRelativePath);
            if (string.IsNullOrEmpty(folderName))
            {
                return null;
            }

            var directPath = PackagePathHelper.GetFilePath($"Samples/{folderName}.unitypackage");
            if (File.Exists(directPath))
            {
                return Path.GetFullPath(directPath);
            }

            var expectedSuffix = $"/{folderName}.unitypackage";
            var guids = AssetDatabase.FindAssets($"{folderName} t:DefaultAsset");
            foreach (var guid in guids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                if (assetPath.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase) && File.Exists(assetPath))
                {
                    return Path.GetFullPath(assetPath);
                }
            }

            return null;
        }

        public static string FindImportedSampleDirectory(string samplePath, string displayName)
        {
            if (!string.IsNullOrEmpty(displayName))
            {
                var standardPath = Path.Combine("Assets", "Samples", "Build Tool", GetPackageVersion(), displayName);
                if (Directory.Exists(standardPath))
                {
                    return standardPath;
                }

                var unversionedPath = Path.Combine("Assets", "Samples", "Build Tool", displayName);
                if (Directory.Exists(unversionedPath))
                {
                    return unversionedPath;
                }

                var samplesRoot = Path.Combine("Assets", "Samples", "Build Tool");
                if (Directory.Exists(samplesRoot))
                {
                    foreach (var versionDir in Directory.GetDirectories(samplesRoot))
                    {
                        var candidate = Path.Combine(versionDir, displayName);
                        if (Directory.Exists(candidate))
                        {
                            return candidate;
                        }
                    }
                }
            }

            var folderName = GetSampleFolderName(samplePath);
            if (!string.IsNullOrEmpty(folderName))
            {
                var legacyPath = Path.Combine("Assets", "Samples", folderName);
                if (Directory.Exists(legacyPath))
                {
                    return legacyPath;
                }
            }

            return null;
        }

        public static string ResolveSampleReadmePath(string samplePath, string displayName = null)
        {
            var sourceDir = ResolveSamplePath(samplePath);
            if (Directory.Exists(sourceDir))
            {
                var sourceReadme = Path.Combine(sourceDir, "README.md");
                if (File.Exists(sourceReadme))
                {
                    return sourceReadme;
                }
            }

            var folderName = GetSampleFolderName(samplePath);
            if (!string.IsNullOrEmpty(folderName))
            {
                var companionReadme = PackagePathHelper.GetFilePath($"Samples/{folderName}_README.md");
                if (File.Exists(companionReadme))
                {
                    return Path.GetFullPath(companionReadme);
                }

                var expectedSuffix = $"/{folderName}_README.md";
                var guids = AssetDatabase.FindAssets($"{folderName}_README t:TextAsset");
                foreach (var guid in guids)
                {
                    var assetPath = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                    if (assetPath.EndsWith(expectedSuffix, StringComparison.OrdinalIgnoreCase) &&
                        File.Exists(assetPath))
                    {
                        return Path.GetFullPath(assetPath);
                    }
                }
            }

            var importedDir = FindImportedSampleDirectory(samplePath, displayName);
            if (!string.IsNullOrEmpty(importedDir))
            {
                var importedReadme = Path.Combine(importedDir, "README.md");
                if (File.Exists(importedReadme))
                {
                    return Path.GetFullPath(importedReadme);
                }
            }

            return null;
        }

        public static bool IsSampleImported(string samplePath, string displayName)
        {
            return !string.IsNullOrEmpty(FindImportedSampleDirectory(samplePath, displayName));
        }

        public static void ImportSample(string samplePath, string displayName)
        {
            var sourceDir = ResolveSamplePath(samplePath);
            if (Directory.Exists(sourceDir))
            {
                var destDir = Path.Combine("Assets", "Samples", "Build Tool", GetPackageVersion(), displayName);
                if (Directory.Exists(destDir) || IsSampleImported(samplePath, displayName))
                {
                    if (!EditorUtility.DisplayDialog(
                        "Reimport Sample",
                        $"Sample '{displayName}' is already imported.\n\n" +
                        "Do you want to reimport and overwrite existing sample files?",
                        "Reimport", "Cancel"))
                    {
                        return;
                    }
                }

                try
                {
                    CopyDirectory(sourceDir, destDir);
                    AssetDatabase.Refresh();
                    EditorUtility.DisplayDialog(
                        "Build Tool", $"Sample '{displayName}' imported successfully to:\n{destDir}", "OK");
                }
                catch (Exception ex)
                {
                    EditorUtility.DisplayDialog(
                        "Build Tool", $"Failed to import sample '{displayName}':\n{ex.Message}", "OK");
                }

                return;
            }

            var samplePackagePath = ResolveSamplePackagePath(samplePath);
            if (!string.IsNullOrEmpty(samplePackagePath) && File.Exists(samplePackagePath))
            {
                if (IsSampleImported(samplePath, displayName))
                {
                    if (!EditorUtility.DisplayDialog(
                        "Reimport Sample",
                        $"Sample '{displayName}' is already imported.\n\n" +
                        "Do you want to reimport and overwrite existing sample files?",
                        "Reimport", "Cancel"))
                    {
                        return;
                    }
                }

                ImportSamplePackage(samplePackagePath, displayName);
                return;
            }

            var expectedPkgPath = PackagePathHelper.GetFilePath($"Samples/{GetSampleFolderName(samplePath)}.unitypackage");
            EditorUtility.DisplayDialog(
                "Build Tool",
                $"Sample source not found.\n\nChecked directory:\n{sourceDir}\n\nChecked package:\n{expectedPkgPath}",
                "OK");
        }

        private static void ImportSamplePackage(string packagePath, string displayName)
        {
            var expectedPackageName = Path.GetFileNameWithoutExtension(packagePath);
            AssetDatabase.ImportPackageCallback onCompleted = null;
            AssetDatabase.ImportPackageFailedCallback onFailed = null;
            AssetDatabase.ImportPackageCallback onCancelled = null;

            void Unsubscribe()
            {
                AssetDatabase.importPackageCompleted -= onCompleted;
                AssetDatabase.importPackageFailed -= onFailed;
                AssetDatabase.importPackageCancelled -= onCancelled;
            }

            onCompleted = packageName =>
            {
                if (!string.Equals(packageName, expectedPackageName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                Unsubscribe();
                Debug.Log($"[BuildTool] Sample '{displayName}' imported successfully.");
                EditorApplication.delayCall += RefreshOpenWelcomeWindows;
            };

            onFailed = (packageName, errorMessage) =>
            {
                if (!string.Equals(packageName, expectedPackageName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                Unsubscribe();
                EditorUtility.DisplayDialog(
                    "Build Tool", $"Failed to import sample '{displayName}':\n{errorMessage}", "OK");
            };

            onCancelled = packageName =>
            {
                if (!string.Equals(packageName, expectedPackageName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                Unsubscribe();
            };

            AssetDatabase.importPackageCompleted += onCompleted;
            AssetDatabase.importPackageFailed += onFailed;
            AssetDatabase.importPackageCancelled += onCancelled;

            try
            {
                AssetDatabase.ImportPackage(packagePath, false);
            }
            catch (Exception ex)
            {
                Unsubscribe();
                EditorUtility.DisplayDialog(
                    "Build Tool", $"Failed to import sample '{displayName}':\n{ex.Message}", "OK");
            }
        }

        private static void RefreshOpenWelcomeWindows()
        {
            foreach (var window in Resources.FindObjectsOfTypeAll<BuildToolWelcomeWindow>())
            {
                if (window != null)
                {
                    window.BuildUI();
                }
            }
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var fileName = Path.GetFileName(file);
                var destFile = Path.Combine(destinationDir, fileName);
                File.Copy(file, destFile, true);
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var dirName = Path.GetFileName(dir);
                var destSubDir = Path.Combine(destinationDir, dirName);
                CopyDirectory(dir, destSubDir);
            }
        }

        public static string ResolveSamplePath(string sampleRelativePath)
        {
            var normalized = sampleRelativePath.Replace('\\', '/');
            if (Directory.Exists(normalized))
            {
                return Path.GetFullPath(normalized);
            }

            var packageInfo =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(BuildToolWelcomeWindow).Assembly);
            if (packageInfo != null && !string.IsNullOrEmpty(packageInfo.resolvedPath))
            {
                var prefix = $"{PackagePathHelper.PackageRoot}/";
                var legacyPrefix = $"Packages/{PackagePathHelper.DefaultPackageId}/";
                var relativeSub = normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    ? normalized.Substring(prefix.Length)
                    : (normalized.StartsWith(legacyPrefix, StringComparison.OrdinalIgnoreCase)
                        ? normalized.Substring(legacyPrefix.Length)
                        : normalized);
                var inPackage = Path.Combine(packageInfo.resolvedPath, relativeSub);
                if (Directory.Exists(inPackage))
                {
                    return Path.GetFullPath(inPackage);
                }
            }

            return Path.GetFullPath(normalized);
        }

        public static void RevealSampleFolder(string samplePath)
        {
            var path = ResolveSamplePath(samplePath);
            if (Directory.Exists(path))
            {
                EditorUtility.RevealInFinder(path);
                return;
            }

            var packagePath = ResolveSamplePackagePath(samplePath);
            if (!string.IsNullOrEmpty(packagePath) && File.Exists(packagePath))
            {
                EditorUtility.RevealInFinder(packagePath);
                return;
            }

            var pkgPath = PackagePathHelper.PackagePhysicalRoot;
            if (Directory.Exists(pkgPath))
            {
                EditorUtility.RevealInFinder(pkgPath);
            }
        }

        public static void RevealSampleFolder()
        {
            RevealSampleFolder(BuildManifestSamplePath);
        }

        public static void OpenSampleReadme(string samplePath)
        {
            OpenSampleReadme(samplePath, null);
        }

        public static void OpenSampleReadme(string samplePath, string displayName)
        {
            var readmePath = ResolveSampleReadmePath(samplePath, displayName);
            if (!string.IsNullOrEmpty(readmePath) && File.Exists(readmePath))
            {
                EditorUtility.OpenWithDefaultApp(readmePath);
            }
            else
            {
                RevealSampleFolder(samplePath);
            }
        }

        public static void OpenSampleReadme()
        {
            OpenSampleReadme(BuildManifestSamplePath, "Build Manifest Extension");
        }

        private void BuildReleaseNotesTab(VisualElement container)
        {
            var header = new VisualElement();
            header.AddToClassList("welcome-section-header");

            var title = new Label($"Release Notes (v{GetPackageVersion()})");
            title.AddToClassList("welcome-section-title");
            header.Add(title);

            var desc = new Label("Latest updates, improvements, and additions included in this package release.");
            desc.AddToClassList("welcome-section-desc");
            header.Add(desc);

            container.Add(header);

            var changelogBox = new VisualElement();
            changelogBox.AddToClassList("welcome-changelog-box");

            var items = LoadChangelogEntries();
            foreach (var item in items)
            {
                var label = new Label($"•  {item}");
                label.AddToClassList("welcome-changelog-item");
                changelogBox.Add(label);
            }

            container.Add(changelogBox);
        }

        private static List<string> LoadChangelogEntries()
        {
            var results = new List<string>();
            string filePath = File.Exists(ChangelogPath)
                ? ChangelogPath
                : (File.Exists(RootChangelogPath) ? RootChangelogPath : null);

            if (filePath != null)
            {
                try
                {
                    var lines = File.ReadAllLines(filePath);
                    bool inCurrentVersion = false;

                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("## ["))
                        {
                            if (!inCurrentVersion)
                            {
                                inCurrentVersion = true;
                                continue;
                            }
                            else
                            {
                                break;
                            }
                        }

                        if (inCurrentVersion && trimmed.StartsWith("- "))
                        {
                            results.Add(trimmed.Substring(2));
                        }
                    }
                }
                catch
                {
                }
            }

            if (results.Count == 0)
            {
                results.Add("Multi-platform sequential build execution with dedicated Build Window UI.");
                results.Add("Modular Git Build Tracking toggleable via Setup Wizard.");
                results.Add("Experimental CLI support for classic batchmode and native Unity CLI commands.");
                results.Add("Multi-channel notification engine (Discord, Slack, Webhook, Desktop, Telegram).");
                results.Add("Customizable output path templating via dynamic tokens.");
                results.Add("Built-in zip archiver extension with exclusions.");
            }

            return results;
        }

        private void BuildResourcesTab(VisualElement container)
        {
            var header = new VisualElement();
            header.AddToClassList("welcome-section-header");

            var title = new Label("Resources, Documentation & Community");
            title.AddToClassList("welcome-section-title");
            header.Add(title);

            var desc = new Label(
                "Access guides, reference materials, report issues, and share repository links with your team.");
            desc.AddToClassList("welcome-section-desc");
            header.Add(desc);

            container.Add(header);

            AddResourceCard(
                container,
                "Documentation & Usage Guide",
                "Comprehensive manual detailing build options, CLI switches, notification setup, and extension APIs.",
                "Open Readme",
                OpenReadmeFile
            );

            AddResourceCard(
                container,
                "GitHub Repository",
                "Source code, releases, discussions, and version history hosted on GitHub.",
                "Open GitHub",
                () => Application.OpenURL(GitRepoUrl)
            );

            AddResourceCard(
                container,
                "Issue Tracker & Feedback",
                "Submit bug reports, feature requests, or ask technical questions on GitHub Issues.",
                "Open Issues",
                () => Application.OpenURL(GitIssuesUrl)
            );

            AddResourceCard(
                container,
                "Copy UPM Git URL",
                "Copy package Git URL to clipboard to install or share in manifest.json with other team members.",
                "Copy URL",
                () =>
                {
                    GUIUtility.systemCopyBuffer = GitPackageUrl;
                    EditorUtility.DisplayDialog("Build Tool", "Package Git URL copied to clipboard!", "OK");
                }
            );
        }

        private void AddResourceCard(
            VisualElement container, string titleText, string descText, string btnText, Action action)
        {
            var card = new VisualElement();
            card.AddToClassList("welcome-links-card");

            var textGroup = new VisualElement();
            textGroup.AddToClassList("welcome-links-text-group");

            var title = new Label(titleText);
            title.AddToClassList("welcome-step-title");
            textGroup.Add(title);

            var desc = new Label(descText);
            desc.AddToClassList("welcome-step-desc");
            textGroup.Add(desc);

            card.Add(textGroup);

            var btn = new Button(action)
            {
                text = btnText
            };
            btn.AddToClassList("welcome-btn");
            btn.AddToClassList("welcome-btn-secondary");
            card.Add(btn);

            container.Add(card);
        }

        private static void OpenReadmeFile()
        {
            string path = File.Exists(ReadmePath) ? ReadmePath : (File.Exists(RootReadmePath) ? RootReadmePath : null);
            if (path != null)
            {
                EditorUtility.OpenWithDefaultApp(Path.GetFullPath(path));
            }
            else
            {
                Application.OpenURL(GitRepoUrl);
            }
        }

        private void BuildAboutAndCreditsTab(VisualElement container)
        {
            var header = new VisualElement();
            header.AddToClassList("welcome-section-header");

            var title = new Label("About & Credits");
            title.AddToClassList("welcome-section-title");
            header.Add(title);

            var desc = new Label("Project author, contact details, and licensing information.");
            desc.AddToClassList("welcome-section-desc");
            header.Add(desc);

            container.Add(header);

            var card = new VisualElement();
            card.AddToClassList("welcome-about-card");

            var authorHeader = new VisualElement();
            authorHeader.AddToClassList("welcome-about-header");

            var authorPrefix = new Label("Author —");
            authorPrefix.AddToClassList("welcome-about-author-prefix");
            authorHeader.Add(authorPrefix);

            var authorHandle = new Label($"@{AuthorName}");
            authorHandle.AddToClassList("welcome-about-author-handle");
            authorHeader.Add(authorHandle);

            card.Add(authorHeader);

            var message = new Label(
                "Feel free to reach out, suggest ideas, ask questions, or discuss anything about this project!");
            message.AddToClassList("welcome-about-message");
            card.Add(message);

            var linksContainer = new VisualElement();
            linksContainer.AddToClassList("welcome-about-links");

            var githubRow = new VisualElement();
            githubRow.AddToClassList("welcome-about-link-row");

            var githubIcon = new Image();
            githubIcon.AddToClassList("welcome-about-link-icon");
            githubIcon.image = GetIcon("d_Linked", "d_Favorite");
            githubRow.Add(githubIcon);

            var githubPrefix = new Label("GitHub: ");
            githubPrefix.AddToClassList("welcome-about-link-prefix");
            githubRow.Add(githubPrefix);

            var githubLink = new Label("github.com/redlroses");
            githubLink.AddToClassList("welcome-about-link-text");
            githubLink.tooltip = AuthorGitHubUrl;
            githubLink.RegisterCallback<ClickEvent>(_ => Application.OpenURL(AuthorGitHubUrl));
            githubRow.Add(githubLink);

            linksContainer.Add(githubRow);

            var emailRow = new VisualElement();
            emailRow.AddToClassList("welcome-about-link-row");

            var emailIcon = new Image();
            emailIcon.AddToClassList("welcome-about-link-icon");
            emailIcon.image = GetIcon("d_UnityEditor.ConsoleWindow", "TextAsset Icon");
            emailRow.Add(emailIcon);

            var emailPrefix = new Label("Email: ");
            emailPrefix.AddToClassList("welcome-about-link-prefix");
            emailRow.Add(emailPrefix);

            var emailLink = new Label(AuthorEmail);
            emailLink.AddToClassList("welcome-about-link-text");
            emailLink.tooltip = "Click to copy email address";

            var copyFeedback = new Label("Copied!");
            copyFeedback.AddToClassList("welcome-about-copy-feedback");
            copyFeedback.style.display = DisplayStyle.None;

            emailLink.RegisterCallback<ClickEvent>(_ =>
            {
                GUIUtility.systemCopyBuffer = AuthorEmail;
                copyFeedback.style.display = DisplayStyle.Flex;
                copyFeedback.schedule.Execute(() =>
                {
                    copyFeedback.style.display = DisplayStyle.None;
                }).StartingIn(1500);
            });

            emailRow.Add(emailLink);
            emailRow.Add(copyFeedback);

            linksContainer.Add(emailRow);
            card.Add(linksContainer);

            container.Add(card);

            var licenseBox = new VisualElement();
            licenseBox.AddToClassList("welcome-about-license-box");

            var licenseText = new Label(LicenseNotice);
            licenseText.AddToClassList("welcome-about-license-text");
            licenseBox.Add(licenseText);

            container.Add(licenseBox);
        }

        private void BuildFooter(VisualElement root)
        {
            var footer = new VisualElement();
            footer.AddToClassList("welcome-footer");

            var togglesGroup = new VisualElement();
            togglesGroup.AddToClassList("welcome-footer-toggles");

            var startToggle = new Toggle("Show on startup")
            {
                value = EditorPrefs.GetBool(ShowOnStartPrefKey, false)
            };
            startToggle.AddToClassList("welcome-footer-toggle");
            startToggle.RegisterValueChangedCallback(evt =>
            {
                EditorPrefs.SetBool(ShowOnStartPrefKey, evt.newValue);
            });
            togglesGroup.Add(startToggle);

            footer.Add(togglesGroup);

            var buttonsGroup = new VisualElement();
            buttonsGroup.AddToClassList("welcome-footer-buttons");

            var closeBtn = new Button(Close)
            {
                text = "Close"
            };
            closeBtn.AddToClassList("welcome-btn");
            closeBtn.AddToClassList("welcome-btn-secondary");
            buttonsGroup.Add(closeBtn);

            var getStartedBtn = new Button(() =>
            {
                Close();
                BuildWindow.ShowWindow();
            })
            {
                text = "Get Started"
            };
            getStartedBtn.AddToClassList("welcome-btn");
            getStartedBtn.AddToClassList("welcome-btn-primary");
            buttonsGroup.Add(getStartedBtn);

            footer.Add(buttonsGroup);
            root.Add(footer);
        }
    }
}
