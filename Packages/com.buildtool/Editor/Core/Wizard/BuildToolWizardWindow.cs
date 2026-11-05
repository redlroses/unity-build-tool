using System;
using System.IO;
using System.Linq;
using BuildTool.Editor.Notifications;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.UIElements;

namespace BuildTool.Editor
{
    public class BuildToolWizardWindow : EditorWindow
    {
        private const string WindowTitle = "Build Tool Wizard";
        private const string UssPath = "Packages/com.buildtool/Editor/Core/Wizard/BuildToolWizardWindow.uss";

        private bool _isApplyingChanges;
        private string _busyModuleKey;
        private double _applyStartTime;
        private bool _lastIsCompiling;
        private int _spinnerFrame;
        private double _lastSpinnerUpdate;
        private Image _spinnerIcon;
        private bool _isInstallingPackage;
        private EditorApplication.CallbackFunction _activeProgressAction;

        [MenuItem("Tools/Build/Setup Wizard", priority = 2)]
        public static void ShowWindow()
        {
            var window = GetWindow<BuildToolWizardWindow>(WindowTitle);
            window.minSize = new Vector2(480, 420);
            window.titleContent = new GUIContent(WindowTitle, EditorGUIUtility.IconContent("d_Settings").image);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
            CompilationPipeline.compilationStarted -= OnCompilationStarted;
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.compilationFinished -= OnCompilationFinished;
            CompilationPipeline.compilationFinished += OnCompilationFinished;

            _lastIsCompiling = EditorApplication.isCompiling;
            if (!EditorApplication.isCompiling)
            {
                _isApplyingChanges = false;
                _busyModuleKey = null;
            }

            BuildUI();
        }

        private void OnDisable()
        {
            if (_activeProgressAction != null)
            {
                EditorApplication.update -= _activeProgressAction;
                _activeProgressAction = null;
            }

            EditorApplication.update -= OnEditorUpdate;
            CompilationPipeline.compilationStarted -= OnCompilationStarted;
            CompilationPipeline.compilationFinished -= OnCompilationFinished;
        }

        private void OnCompilationStarted(object obj)
        {
            _isApplyingChanges = false;
            BuildUI();
        }

        private void OnCompilationFinished(object obj)
        {
            _isApplyingChanges = false;
            _busyModuleKey = null;
            BuildUI();
        }

        private void OnEditorUpdate()
        {
            bool isCompiling = EditorApplication.isCompiling;
            if (isCompiling != _lastIsCompiling)
            {
                _lastIsCompiling = isCompiling;
                if (!isCompiling)
                {
                    _isApplyingChanges = false;
                    _busyModuleKey = null;
                }
                BuildUI();
                return;
            }

            if (_isApplyingChanges)
            {
                if (EditorApplication.timeSinceStartup - _applyStartTime > 5.0 && !isCompiling)
                {
                    _isApplyingChanges = false;
                    _busyModuleKey = null;
                    BuildUI();
                    return;
                }
            }

            bool isBusy = isCompiling || _isApplyingChanges || _isInstallingPackage;
            if (isBusy && _spinnerIcon != null)
            {
                if (EditorApplication.timeSinceStartup - _lastSpinnerUpdate > 0.08)
                {
                    _lastSpinnerUpdate = EditorApplication.timeSinceStartup;
                    _spinnerFrame = (_spinnerFrame + 1) % 12;
                    _spinnerIcon.image = EditorGUIUtility.IconContent($"WaitSpin{_spinnerFrame:D2}").image;
                }
            }
        }

        public void BuildUI()
        {
            var root = rootVisualElement;
            root.Clear();
            _spinnerIcon = null;
            root.AddToClassList("wizard-root");

            LoadStyleSheet(root);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("wizard-scroll");
            root.Add(scroll);

            var header = new VisualElement();
            header.AddToClassList("wizard-header");

            var title = new Label("Build Tool Setup Wizard");
            title.AddToClassList("wizard-title");
            header.Add(title);

            var subtitle = new Label(
                "Manage modular extensions, integration packages, and compilation defines " +
                "for the Build Tool pipeline.");
            subtitle.AddToClassList("wizard-subtitle");
            header.Add(subtitle);

            scroll.Add(header);

            bool isCompiling = EditorApplication.isCompiling;
            bool isBusy = isCompiling || _isApplyingChanges || _isInstallingPackage;

            if (isBusy)
            {
                BuildCompilationBanner(scroll);
            }

            BuildNotificationsCard(scroll, isBusy);

            BuildCliCard(scroll, isBusy);

            BuildTrackingCard(scroll, isBusy);

            BuildProfilesCard(scroll, isBusy);

            BuildFooter(scroll, isBusy);
        }

        private void BuildCompilationBanner(VisualElement container)
        {
            var banner = new VisualElement();
            banner.AddToClassList("wizard-compiling-banner");

            _spinnerIcon = new Image();
            _spinnerIcon.AddToClassList("wizard-banner-icon");
            _spinnerIcon.image = EditorGUIUtility.IconContent($"WaitSpin{_spinnerFrame:D2}").image;
            banner.Add(_spinnerIcon);

            var col = new VisualElement();
            col.AddToClassList("wizard-banner-col");

            string titleText;
            string descText;

            if (_isInstallingPackage)
            {
                titleText = "Installing Package via UPM...";
                descText =
                    "Package installation in progress. Script compilation and domain reload " +
                    "will follow shortly. Please wait.";
            }
            else
            {
                titleText = "Script Compilation in Progress...";
                descText =
                    "Scripting Define Symbols updated. Unity is recompiling C# assemblies and "
                    + "reloading the script domain. Please wait for completion.";
            }

            var titleLabel = new Label(titleText);
            titleLabel.AddToClassList("wizard-banner-title");
            col.Add(titleLabel);

            var descLabel = new Label(descText);
            descLabel.AddToClassList("wizard-banner-desc");
            col.Add(descLabel);

            banner.Add(col);
            container.Add(banner);
        }

        private void LoadStyleSheet(VisualElement root)
        {
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            if (styleSheet == null)
            {
                var guids = AssetDatabase.FindAssets("BuildToolWizardWindow t:StyleSheet");
                if (guids.Length > 0)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
                }
            }

            if (styleSheet != null)
            {
                root.styleSheets.Add(styleSheet);
            }
        }

        private void BuildNotificationsCard(VisualElement container, bool isBusy)
        {
            var card = new VisualElement();
            card.AddToClassList("wizard-card");
            container.Add(card);

            var header = new VisualElement();
            header.AddToClassList("wizard-card-header");

            var title = new Label("Build Notifications");
            title.AddToClassList("wizard-card-title");
            header.Add(title);
            card.Add(header);

            var desc = new Label(
                "Sends automated build reports, duration summaries, and error logs to Discord, Slack, "
                + "custom Webhooks, Telegram, or desktop notifications upon build completion.");
            desc.AddToClassList("wizard-card-desc");
            card.Add(desc);

            var isEnabled = BuildNotificationSettings.MasterEnabled;
            var activeCount = BuildNotificationManager.ActiveChannelsCount;

            var actionRow = new VisualElement();
            actionRow.AddToClassList("wizard-actions-row");

            var badgesRow = new VisualElement();
            badgesRow.AddToClassList("wizard-badges-row");

            var statusBadge = new Label(isEnabled ? "● Enabled" : "○ Disabled");
            statusBadge.AddToClassList("wizard-badge");
            statusBadge.AddToClassList(isEnabled ? "wizard-badge-enabled" : "wizard-badge-disabled");
            badgesRow.Add(statusBadge);

            if (isEnabled)
            {
                var channelsBadge = new Label(activeCount > 0
                    ? $"● {activeCount} Channel(s) Active"
                    : "○ No Channels Configured");
                channelsBadge.AddToClassList("wizard-badge");
                channelsBadge.AddToClassList(activeCount > 0 ? "wizard-badge-info" : "wizard-badge-warning");
                badgesRow.Add(channelsBadge);
            }

            actionRow.Add(badgesRow);

            var btnGroup = new VisualElement();
            btnGroup.AddToClassList("wizard-btn-group");

            var toggleBtn = new Button(() =>
            {
                if (isBusy) return;
                BuildNotificationSettings.MasterEnabled = !isEnabled;
                BuildUI();
            })
            {
                text = isEnabled ? "Turn Off" : "Turn On"
            };
            toggleBtn.AddToClassList("wizard-btn");
            toggleBtn.AddToClassList(isEnabled ? "wizard-btn-off" : "wizard-btn-on");
            toggleBtn.SetEnabled(!isBusy);
            if (isBusy)
            {
                toggleBtn.tooltip = "Cannot modify settings while scripts are compiling. Please wait.";
            }
            btnGroup.Add(toggleBtn);

            var settingsBtn = new Button(() =>
            {
                if (isBusy) return;
                SettingsService.OpenProjectSettings(BuildNotificationSettingsProvider.SettingsPath);
            })
            {
                text = "Configure Channels"
            };
            settingsBtn.AddToClassList("wizard-btn");
            settingsBtn.AddToClassList("wizard-btn-secondary");
            settingsBtn.SetEnabled(!isBusy);
            btnGroup.Add(settingsBtn);

            actionRow.Add(btnGroup);
            card.Add(actionRow);
        }

        private void BuildCliCard(VisualElement container, bool isBusy)
        {
            var card = new VisualElement();
            card.AddToClassList("wizard-card");
            container.Add(card);

            var header = new VisualElement();
            header.AddToClassList("wizard-card-header");

            var title = new Label("CLI Automation (Unity Pipeline)");
            title.AddToClassList("wizard-card-title");
            header.Add(title);
            card.Add(header);

            var desc = new Label(
                "Enables terminal, PowerShell, and CI/CD pipelines to trigger builds headlessly via "
                + "'unity command build:run'. Requires the experimental com.unity.pipeline package.");
            desc.AddToClassList("wizard-card-desc");
            card.Add(desc);

            var isPackageInstalled = HasUnityPipelinePackage();
            var isCliEnabled = ScriptingDefineUtility.IsDefined(ScriptingDefineUtility.CliExperimentalDefine);

            var actionRow = new VisualElement();
            actionRow.AddToClassList("wizard-actions-row");

            var badgesRow = new VisualElement();
            badgesRow.AddToClassList("wizard-badges-row");

            var pkgBadge = new Label(isPackageInstalled ? "● Package Installed" : "○ Package Missing");
            pkgBadge.AddToClassList("wizard-badge");
            pkgBadge.AddToClassList(isPackageInstalled ? "wizard-badge-enabled" : "wizard-badge-warning");
            badgesRow.Add(pkgBadge);

            if (isPackageInstalled)
            {
                var modBadge = new Label(isCliEnabled ? "● Module On" : "○ Module Off");
                modBadge.AddToClassList("wizard-badge");
                modBadge.AddToClassList(isCliEnabled ? "wizard-badge-enabled" : "wizard-badge-disabled");
                badgesRow.Add(modBadge);
            }

            actionRow.Add(badgesRow);

            var btnGroup = new VisualElement();
            btnGroup.AddToClassList("wizard-btn-group");

            if (!isPackageInstalled)
            {
                bool isThisBusy = _busyModuleKey == "install_cli";
                var installBtn = new Button(() =>
                {
                    if (isBusy) return;
                    _isInstallingPackage = true;
                    _busyModuleKey = "install_cli";
                    _applyStartTime = EditorApplication.timeSinceStartup;
                    BuildUI();
                    InstallPipelinePackage();
                })
                {
                    text = isThisBusy ? "Installing..." : "Install com.unity.pipeline"
                };
                installBtn.AddToClassList("wizard-btn");
                installBtn.AddToClassList("wizard-btn-on");
                installBtn.SetEnabled(!isBusy);
                if (isBusy)
                {
                    installBtn.tooltip = "Package installation or compilation in progress. Please wait.";
                }
                btnGroup.Add(installBtn);
            }
            else
            {
                bool isThisBusy = _busyModuleKey == "cli";
                var toggleBtn = new Button(() =>
                {
                    if (isBusy) return;
                    var nextState = !isCliEnabled;
                    _isApplyingChanges = true;
                    _busyModuleKey = "cli";
                    _applyStartTime = EditorApplication.timeSinceStartup;
                    BuildUI();

                    bool changed = ScriptingDefineUtility.SetDefined(
                        ScriptingDefineUtility.CliExperimentalDefine, nextState);
                    if (!changed)
                    {
                        _isApplyingChanges = false;
                        _busyModuleKey = null;
                        BuildUI();
                    }
                })
                {
                    text = isThisBusy ? "Compiling..." : (isCliEnabled ? "Turn Off" : "Turn On")
                };
                toggleBtn.AddToClassList("wizard-btn");
                toggleBtn.AddToClassList(isCliEnabled ? "wizard-btn-off" : "wizard-btn-on");
                toggleBtn.SetEnabled(!isBusy);
                if (isBusy)
                {
                    toggleBtn.tooltip = "Compilation in progress. Please wait for scripts to finish compiling.";
                }
                btnGroup.Add(toggleBtn);
            }

            actionRow.Add(btnGroup);
            card.Add(actionRow);
        }

        private static bool HasUnityPipelinePackage()
        {
            var manifestPath = "Packages/manifest.json";
            if (File.Exists(manifestPath))
            {
                var content = File.ReadAllText(manifestPath);
                if (content.Contains("com.unity.pipeline"))
            {
                return true;
            }
            }

            return Type.GetType("Unity.Pipeline.CommandAttribute, Unity.Pipeline") != null
                || AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Unity.Pipeline");
        }

        private void InstallPipelinePackage()
        {
            Debug.Log("[BuildTool] Requesting installation of package 'com.unity.pipeline' via UPM...");
            var request = UnityEditor.PackageManager.Client.Add("com.unity.pipeline");
            _activeProgressAction = Progress;
            EditorApplication.update += Progress;

            void Progress()
            {
                if (request.IsCompleted)
                {
                    EditorApplication.update -= Progress;
                    _isInstallingPackage = false;

                    if (request.Status == UnityEditor.PackageManager.StatusCode.Success)
                    {
                        Debug.Log("[BuildTool] Successfully installed 'com.unity.pipeline'!");
                        _isApplyingChanges = true;
                        _busyModuleKey = "cli";
                        _applyStartTime = EditorApplication.timeSinceStartup;
                        BuildUI();
                        ScriptingDefineUtility.SetDefined(ScriptingDefineUtility.CliExperimentalDefine, true);
                    }
                    else if (request.Status >= UnityEditor.PackageManager.StatusCode.Failure)
                    {
                        var errorMsg = request.Error?.message ?? "Unknown package manager error";
                        Debug.LogError($"[BuildTool] Failed to install 'com.unity.pipeline': {errorMsg}");
                        _busyModuleKey = null;
                        BuildUI();
                        EditorUtility.DisplayDialog("Package Installation Failed",
                            $"Failed to install package 'com.unity.pipeline':\n\n{errorMsg}\n\n" +
                            "Verify your internet connection and Unity Package Manager settings.", "OK");
                    }
                }
            }
        }

        private void BuildTrackingCard(VisualElement container, bool isBusy)
        {
            var card = new VisualElement();
            card.AddToClassList("wizard-card");
            container.Add(card);

            var header = new VisualElement();
            header.AddToClassList("wizard-card-header");

            var title = new Label("Git Build Tracking");
            title.AddToClassList("wizard-card-title");
            header.Add(title);
            card.Add(header);

            var desc = new Label(
                "Automatically increments build numbers and records commit metadata on an isolated git "
                + "tracking branch to synchronize builds across the team.");
            desc.AddToClassList("wizard-card-desc");
            card.Add(desc);

            var isEnabled = ScriptingDefineUtility.IsDefined(ScriptingDefineUtility.TrackingDefine);

            var actionRow = new VisualElement();
            actionRow.AddToClassList("wizard-actions-row");

            var badgesRow = new VisualElement();
            badgesRow.AddToClassList("wizard-badges-row");

            var statusBadge = new Label(isEnabled ? "● Enabled" : "○ Disabled");
            statusBadge.AddToClassList("wizard-badge");
            statusBadge.AddToClassList(isEnabled ? "wizard-badge-enabled" : "wizard-badge-disabled");
            badgesRow.Add(statusBadge);

            actionRow.Add(badgesRow);

            var btnGroup = new VisualElement();
            btnGroup.AddToClassList("wizard-btn-group");

            bool isThisBusy = _busyModuleKey == "tracking";
            var toggleBtn = new Button(() =>
            {
                if (isBusy) return;
                var nextState = !isEnabled;
                _isApplyingChanges = true;
                _busyModuleKey = "tracking";
                _applyStartTime = EditorApplication.timeSinceStartup;
                BuildUI();

                bool changed = ScriptingDefineUtility.SetDefined(ScriptingDefineUtility.TrackingDefine, nextState);
                if (!changed)
                {
                    _isApplyingChanges = false;
                    _busyModuleKey = null;
                    BuildUI();
                }
            })
            {
                text = isThisBusy ? "Compiling..." : (isEnabled ? "Turn Off" : "Turn On")
            };
            toggleBtn.AddToClassList("wizard-btn");
            toggleBtn.AddToClassList(isEnabled ? "wizard-btn-off" : "wizard-btn-on");
            toggleBtn.SetEnabled(!isBusy);
            if (isBusy)
            {
                toggleBtn.tooltip = "Compilation in progress. Please wait for scripts to finish compiling.";
            }
            btnGroup.Add(toggleBtn);

            if (isEnabled)
            {
                var settingsBtn = new Button(() =>
                {
                    if (isBusy) return;
                    SettingsService.OpenProjectSettings("Project/Build Tool/Tracking");
                })
                {
                    text = "Tracking"
                };
                settingsBtn.AddToClassList("wizard-btn");
                settingsBtn.AddToClassList("wizard-btn-secondary");
                settingsBtn.SetEnabled(!isBusy);
                btnGroup.Add(settingsBtn);
            }

            actionRow.Add(btnGroup);
            card.Add(actionRow);
        }

        private void BuildProfilesCard(VisualElement container, bool isBusy)
        {
            var card = new VisualElement();
            card.AddToClassList("wizard-card");
            container.Add(card);

            var header = new VisualElement();
            header.AddToClassList("wizard-card-header");

            var title = new Label("Build Profiles & Output Paths");
            title.AddToClassList("wizard-card-title");
            header.Add(title);
            card.Add(header);

            var desc = new Label("Inspect target platforms, output file naming patterns, and destination directories.");
            desc.AddToClassList("wizard-card-desc");
            card.Add(desc);

            var count = BuildTool.GetBuildProfiles()?.Count ?? 0;

            var actionRow = new VisualElement();
            actionRow.AddToClassList("wizard-actions-row");

            var badgesRow = new VisualElement();
            badgesRow.AddToClassList("wizard-badges-row");

            var statusBadge = new Label($"● {count} Profiles Configured");
            statusBadge.AddToClassList("wizard-badge");
            statusBadge.AddToClassList(count > 0 ? "wizard-badge-info" : "wizard-badge-warning");
            badgesRow.Add(statusBadge);

            actionRow.Add(badgesRow);

            var btnGroup = new VisualElement();
            btnGroup.AddToClassList("wizard-btn-group");

            var winBtn = new Button(() =>
            {
                if (isBusy) return;
                BuildWindow.ShowWindow();
            })
            {
                text = "Open Build Window"
            };
            winBtn.AddToClassList("wizard-btn");
            winBtn.AddToClassList("wizard-btn-secondary");
            winBtn.SetEnabled(!isBusy);
            btnGroup.Add(winBtn);

            var settingsBtn = new Button(() =>
            {
                if (isBusy) return;
                SettingsService.OpenProjectSettings("Project/Build Tool");
            })
            {
                text = "Configure Output Paths"
            };
            settingsBtn.AddToClassList("wizard-btn");
            settingsBtn.AddToClassList("wizard-btn-secondary");
            settingsBtn.SetEnabled(!isBusy);
            btnGroup.Add(settingsBtn);

            actionRow.Add(btnGroup);
            card.Add(actionRow);
        }

        private void BuildFooter(VisualElement container, bool isBusy)
        {
            var footer = new VisualElement();
            footer.AddToClassList("wizard-footer");

            var hint = new Label(
                "Toggling modules modifies Scripting Define Symbols and reloads the C# script domain.");
            hint.AddToClassList("wizard-footer-hint");
            footer.Add(hint);

            bool isThisBusy = _busyModuleKey == "recompile";
            var recompileBtn = new Button(() =>
            {
                if (isBusy) return;
                _isApplyingChanges = true;
                _busyModuleKey = "recompile";
                _applyStartTime = EditorApplication.timeSinceStartup;
                BuildUI();

                ScriptingDefineUtility.RequestRecompile();
            })
            {
                text = isThisBusy ? "Compiling..." : "Force Recompile Scripts",
                tooltip = isBusy
                    ? "Compilation in progress. Please wait."
                    : "Manually triggers Unity script compilation pipeline."
            };
            recompileBtn.AddToClassList("wizard-btn");
            recompileBtn.AddToClassList("wizard-btn-secondary");
            recompileBtn.AddToClassList("wizard-btn-footer");
            recompileBtn.SetEnabled(!isBusy);
            footer.Add(recompileBtn);

            container.Add(footer);
        }
    }
}

