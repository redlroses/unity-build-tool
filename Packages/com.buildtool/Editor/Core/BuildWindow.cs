using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BuildTool.Editor.Extensions;
using BuildTool.Editor.Pipeline;
using BuildTool.Editor.Processors;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;
using UnityEngine.UIElements;

namespace BuildTool.Editor
{
    public class BuildWindow : EditorWindow
    {
        private const string UssPath = "Packages/com.buildtool/Editor/Core/BuildWindow.uss";

        private HashSet<BuildProfile> _availableProfiles = new();
        private HashSet<BuildProfile> _selectedProfiles = new();
        private List<IBuildWindowExtension> _extensions = new();

        private string _clearSaveKey;
        private string _devModeSaveKey;
        private string _cleanBuildSaveKey;
        private string _openFolderAfterBuildSaveKey;
#if BUILDTOOL_TRACKING
        private string _registerSaveKey;
#endif
        private string _selectedProfilesSaveKey;

        private bool _isBuildInProcess;
        private bool _isClearFolderFirst;
        private bool _isDevelopment;
        private bool _isCleanBuild;
        private bool _isOpenFolder = true;
#if BUILDTOOL_TRACKING
        private bool _isRegisterVersionInRemote;
#endif

        private Button _buildButton;
        private HelpBox _noProfilesWarning;
        private VisualElement _linuxWarningContainer;
        private Label _profilesCountLabel;
        private ScrollView _scrollContainer;

        [MenuItem(TextContent.MenuItem)]
        public static void ShowWindow()
        {
            var window = GetWindow<BuildWindow>(TextContent.WindowTitle);
            window.minSize = new Vector2(280, 320);
        }

        private void OnEnable()
        {
            titleContent = new GUIContent(TextContent.WindowTitle);
            minSize = new Vector2(280, 320);

            _devModeSaveKey = "build_tool_dev".AppendProjectName();
            _cleanBuildSaveKey = "build_tool_clean_build".AppendProjectName();
            _clearSaveKey = "build_tool_clear".AppendProjectName();
            _openFolderAfterBuildSaveKey = "build_tool_open".AppendProjectName();
#if BUILDTOOL_TRACKING
            _registerSaveKey = BuildTool.RegisterSaveKey;
#endif
            _selectedProfilesSaveKey = "build_tool_selected_profiles".AppendProjectName();

            LoadAllProfiles();
            LoadSettings();
            FindAndAddExtensions();
        }

        private void OnFocus()
        {
            LoadAllProfiles();
            LoadSettings();
            BuildUI();
        }

        public void CreateGUI()
        {
            BuildUI();
        }

        private void BuildUI()
        {
            var root = rootVisualElement;
            root.Clear();
            root.AddToClassList("build-window-root");

            LoadStyleSheet(root);

            _scrollContainer = new ScrollView(ScrollViewMode.Vertical);
            _scrollContainer.AddToClassList("build-window-scroll");
            _scrollContainer.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            root.Add(_scrollContainer);

            BuildProfilesCard(_scrollContainer);

            BuildOptionsCard(_scrollContainer);

            if (_extensions.Count > 0)
            {
                BuildExtensionsCard(_scrollContainer);
            }

            BuildActionsCard(_scrollContainer);

            UpdateBuildButtonState();
        }

        private void LoadStyleSheet(VisualElement root)
        {
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            if (styleSheet == null)
            {
                var guids = AssetDatabase.FindAssets("BuildWindow t:StyleSheet");
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

        private void BuildProfilesCard(VisualElement container)
        {
            var card = new VisualElement();
            card.AddToClassList("build-section-card");
            container.Add(card);

            var header = new VisualElement();
            header.AddToClassList("build-section-header");

            _profilesCountLabel =
                new Label($"Select Build Profiles ({_selectedProfiles.Count}/{_availableProfiles.Count})");
            _profilesCountLabel.AddToClassList("build-section-title");
            header.Add(_profilesCountLabel);

            var quickActions = new VisualElement();
            quickActions.AddToClassList("build-quick-actions");

            var settingsBtn = new Button(() => SettingsService.OpenProjectSettings("Project/Build Tool"))
            {
                tooltip = "Open Build Tool Project Settings"
            };
            settingsBtn.AddToClassList("build-quick-btn");
            settingsBtn.AddToClassList("build-quick-icon-btn");

            var settingsIcon = EditorGUIUtility.IconContent("d_Settings")?.image
                ?? EditorGUIUtility.FindTexture("Settings");

            if (settingsIcon != null)
            {
                var iconImg = new Image
                {
                    image = settingsIcon,
                    scaleMode = ScaleMode.ScaleToFit,
                    pickingMode = PickingMode.Ignore
                };
                iconImg.AddToClassList("build-quick-btn-icon");
                settingsBtn.Add(iconImg);
            }
            else
            {
                settingsBtn.text = "\u2699";
            }

            quickActions.Add(settingsBtn);

            var allBtn = new Button(() =>
            {
                foreach (var p in _availableProfiles)
                {
                    if (p != null) _selectedProfiles.Add(p);
                }
                SaveSettings();
                BuildUI();
            })
            {
                text = "All"
            };
            allBtn.AddToClassList("build-quick-btn");
            quickActions.Add(allBtn);

            var noneBtn = new Button(() =>
            {
                _selectedProfiles.Clear();
                SaveSettings();
                BuildUI();
            })
            {
                text = "None"
            };
            noneBtn.AddToClassList("build-quick-btn");
            quickActions.Add(noneBtn);

            header.Add(quickActions);
            card.Add(header);

            var hint = new Label("Choose profiles to include in the automated build pipeline sequence.");
            hint.AddToClassList("build-section-hint");
            card.Add(hint);

            if (_availableProfiles.Count == 0)
            {
                var warnBox = new HelpBox(
                    "No BuildProfiles found in project. Create one via Assets > Create > Build > Build Profile.",
                    HelpBoxMessageType.Info);
                card.Add(warnBox);
                return;
            }

            var activeProfile = BuildProfile.GetActiveBuildProfile();

            foreach (var profile in _availableProfiles.Where(p => p != null))
            {
                var row = new VisualElement();
                row.AddToClassList("build-profile-row");

                var left = new VisualElement();
                left.AddToClassList("build-profile-left");

                var isSelected = _selectedProfiles.Contains(profile);
                var toggle = new Toggle
                {
                    value = isSelected
                };
                toggle.AddToClassList("build-profile-toggle");
                left.Add(toggle);

                var nameLabel = new Label(profile.name);
                nameLabel.AddToClassList("build-profile-name");
                nameLabel.tooltip = profile.name;
                left.Add(nameLabel);
                row.Add(left);

                var right = new VisualElement();
                right.AddToClassList("build-profile-right");

                var version = BuildTool.GetProfileVersion(profile);
                var displayVersion = string.IsNullOrEmpty(version)
                    ? $"v{Application.version}"
                    : (version.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? version : $"v{version}");
                var versionBadge = new Label(displayVersion);
                versionBadge.AddToClassList("build-badge-version");
                versionBadge.tooltip = $"Version: {displayVersion}";
                right.Add(versionBadge);

                var target = BuildTool.GetBuildTarget(profile);
                var targetBadge = new Label(target.ToString());
                targetBadge.AddToClassList("build-badge-target");
                targetBadge.tooltip = $"Target: {target}";
                right.Add(targetBadge);

                Button switchBtn = null;
                if (profile == activeProfile)
                {
                    var activeBadge = new Label("Active");
                    activeBadge.AddToClassList("build-badge-active");
                    right.Add(activeBadge);
                }
                else
                {
                    switchBtn = new Button(() =>
                    {
                        BuildProfile.SetActiveBuildProfile(profile);
                        BuildUI();
                    })
                    {
                        text = "Switch"
                    };
                    switchBtn.AddToClassList("build-btn-switch");
                    switchBtn.RegisterCallback<ClickEvent>(e => e.StopPropagation());
                    right.Add(switchBtn);
                }

                row.Add(right);

                row.RegisterCallback<ClickEvent>(evt =>
                {
                    if (switchBtn != null &&
                        (evt.target == switchBtn || switchBtn.Contains(evt.target as VisualElement)))
                    {
                        return;
                    }

                    if (evt.target == toggle || toggle.Contains(evt.target as VisualElement))
                    {
                        return;
                    }

                    toggle.value = !toggle.value;
                });

                toggle.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue)
                    {
                        _selectedProfiles.Add(profile);
                    }
                    else
                    {
                        _selectedProfiles.Remove(profile);
                    }
                    SaveSettings();
                    UpdateProfilesCount();
                    UpdateBuildButtonState();
                });

                card.Add(row);
            }
        }

        private void UpdateProfilesCount()
        {
            if (_profilesCountLabel != null)
            {
                _profilesCountLabel.text =
                    $"Select Build Profiles ({_selectedProfiles.Count}/{_availableProfiles.Count})";
            }
        }

        private void BuildOptionsCard(VisualElement container)
        {
            var card = new VisualElement();
            card.AddToClassList("build-section-card");
            container.Add(card);

            var header = new VisualElement();
            header.AddToClassList("build-section-header");

            var title = new Label(TextContent.HeaderOptions);
            title.AddToClassList("build-section-title");
            header.Add(title);
            card.Add(header);

            var hint = new Label(TextContent.HintOptions);
            hint.AddToClassList("build-section-hint");
            card.Add(hint);

            AddOptionToggle(card, TextContent.ToggleDev, _isDevelopment, val => _isDevelopment = val,
                TextContent.SubDev, TextContent.HelpDev);
            AddOptionToggle(card, TextContent.ToggleCleanBuild, _isCleanBuild, val => _isCleanBuild = val,
                TextContent.SubCleanBuild, TextContent.HelpCleanBuild);
            AddOptionToggle(card, TextContent.ToggleClear, _isClearFolderFirst, val => _isClearFolderFirst = val,
                TextContent.SubClear, TextContent.HelpClear);
            AddOptionToggle(card, TextContent.ToggleOpen, _isOpenFolder, val => _isOpenFolder = val,
                TextContent.SubOpen, TextContent.HelpOpen);
#if BUILDTOOL_TRACKING
            AddOptionToggle(card, TextContent.ToggleRegister, _isRegisterVersionInRemote,
                val => _isRegisterVersionInRemote = val, TextContent.SubRegister, TextContent.HelpRegister);
#endif
        }

        private void AddOptionToggle(
            VisualElement container,
            string label,
            bool initialValue,
            Action<bool> onValueChanged,
            string subtitle = null,
            string helpText = null)
        {
            var row = new VisualElement();
            row.AddToClassList("build-option-row");

            if (!string.IsNullOrEmpty(helpText))
            {
                row.tooltip = helpText;
            }

            var toggle = new Toggle
            {
                value = initialValue
            };
            toggle.AddToClassList("build-option-toggle");
            toggle.RegisterValueChangedCallback(evt =>
            {
                onValueChanged(evt.newValue);
                SaveSettings();
            });
            row.Add(toggle);

            var textContainer = new VisualElement();
            textContainer.AddToClassList("build-option-text-container");

            var labelElement = new Label(label);
            labelElement.AddToClassList("build-option-label");
            textContainer.Add(labelElement);

            if (!string.IsNullOrEmpty(subtitle))
            {
                var sublabelElement = new Label(subtitle);
                sublabelElement.AddToClassList("build-option-sublabel");
                textContainer.Add(sublabelElement);
            }

            row.Add(textContainer);

            row.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target == toggle || toggle.Contains(evt.target as VisualElement))
                {
                    return;
                }

                toggle.value = !toggle.value;
            });

            container.Add(row);
        }

        private void BuildExtensionsCard(VisualElement container)
        {
            var card = new VisualElement();
            card.AddToClassList("build-section-card");
            container.Add(card);

            var header = new VisualElement();
            header.AddToClassList("build-section-header");

            var title = new Label($"{TextContent.HeaderExtensions} ({_extensions.Count})");
            title.AddToClassList("build-section-title");
            header.Add(title);
            card.Add(header);

            var hint = new Label("Custom extension modules integrated into the build pipeline.");
            hint.AddToClassList("build-section-hint");
            card.Add(hint);

            foreach (var extension in _extensions)
            {
                try
                {
                    var extGui = extension.CreateGUI(_availableProfiles);
                    if (extGui != null)
                    {
                        card.Add(extGui);
                    }
                }
                catch (Exception ex)
                {
                    card.Add(new HelpBox(
                        $"Extension UI error ({extension.GetType().Name}): {ex.Message}",
                        HelpBoxMessageType.Error));
                }
            }
        }

        private void BuildActionsCard(VisualElement container)
        {
            var card = new VisualElement();
            card.AddToClassList("build-section-card");
            container.Add(card);

            var utilRow = new VisualElement();
            utilRow.AddToClassList("build-utility-row");

            var clearBtn = new Button(ClearPersistentData)
            {
                text = TextContent.BtnClearAppData,
                tooltip = TextContent.BtnClearAppData
            };
            clearBtn.AddToClassList("build-utility-btn");
            utilRow.Add(clearBtn);

            var openAppBtn = new Button(() => Application.OpenURL(Application.persistentDataPath))
            {
                text = TextContent.BtnOpenAppData,
                tooltip = TextContent.BtnOpenAppData
            };
            openAppBtn.AddToClassList("build-utility-btn");
            utilRow.Add(openAppBtn);

            var openBuildBtn = new Button(BuildTool.OpenBuildFolder)
            {
                text = TextContent.BtnOpenBuildFolder,
                tooltip = TextContent.BtnOpenBuildFolder
            };
            openBuildBtn.AddToClassList("build-utility-btn");
            utilRow.Add(openBuildBtn);

            var wizardBtn = new Button(BuildToolWizardWindow.ShowWindow)
            {
                text = "Setup Wizard",
                tooltip = "Setup Wizard"
            };
            wizardBtn.AddToClassList("build-utility-btn");
            utilRow.Add(wizardBtn);

            card.Add(utilRow);

            _linuxWarningContainer = new VisualElement();
            card.Add(_linuxWarningContainer);

            _noProfilesWarning = new HelpBox(TextContent.MsgNoProfiles, HelpBoxMessageType.Warning);
            _noProfilesWarning.AddToClassList("build-warning-box");
            card.Add(_noProfilesWarning);

            _buildButton = new Button(() =>
            {
                _ = BuildSelectedAsync();
            });
            _buildButton.AddToClassList("build-primary-btn");
            card.Add(_buildButton);
        }

        private void UpdateBuildButtonState()
        {
            if (_buildButton == null) return;

            var hasSelected = _selectedProfiles.Count > 0;
            _noProfilesWarning.style.display = hasSelected ? DisplayStyle.None : DisplayStyle.Flex;

            _linuxWarningContainer.Clear();
            var linuxProfile = _selectedProfiles.FirstOrDefault(profile =>
                profile != null && BuildTool.GetBuildTarget(profile) == BuildTarget.StandaloneLinux64);

            var isLinuxMismatch = linuxProfile != null &&
                EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneLinux64;

            if (isLinuxMismatch)
            {
                var warnBox = new HelpBox(TextContent.MsgLinuxWarning, HelpBoxMessageType.Warning);
                _linuxWarningContainer.Add(warnBox);

                var switchLinuxBtn = new Button(() =>
                {
                    BuildProfile.SetActiveBuildProfile(linuxProfile);
                    BuildUI();
                })
                {
                    text = TextContent.BtnSetLinuxProfile
                };
                switchLinuxBtn.AddToClassList("build-btn-switch");
                switchLinuxBtn.AddToClassList("build-btn-switch-linux");
                _linuxWarningContainer.Add(switchLinuxBtn);
            }

            _scrollContainer?.SetEnabled(!_isBuildInProcess);

            if (_isBuildInProcess)
            {
                _buildButton.text = TextContent.BuildButtonText;
                _buildButton.SetEnabled(false);
            }
            else
            {
                _buildButton.text = TextContent.BtnBuild;
                _buildButton.SetEnabled(hasSelected && !isLinuxMismatch);
            }
        }

        private void LoadSettings()
        {
            _selectedProfiles.Clear();
            _isDevelopment = EditorPrefs.GetBool(_devModeSaveKey, false);
            _isCleanBuild = EditorPrefs.GetBool(_cleanBuildSaveKey, false);
            _isClearFolderFirst = EditorPrefs.GetBool(_clearSaveKey, false);
            _isOpenFolder = EditorPrefs.GetBool(_openFolderAfterBuildSaveKey, true);
#if BUILDTOOL_TRACKING
            _isRegisterVersionInRemote = EditorPrefs.GetBool(_registerSaveKey, false);
#endif

            var savedProfiles = EditorPrefs.GetString(_selectedProfilesSaveKey, string.Empty);

            if (!string.IsNullOrEmpty(savedProfiles))
            {
                var profileGuids = savedProfiles.Split(',');

                foreach (var guid in profileGuids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(path);
                    if (profile != null)
                    {
                        _selectedProfiles.Add(profile);
                    }
                }
            }
        }

        private void SaveSettings()
        {
            EditorPrefs.SetBool(_devModeSaveKey, _isDevelopment);
            EditorPrefs.SetBool(_cleanBuildSaveKey, _isCleanBuild);
            EditorPrefs.SetBool(_clearSaveKey, _isClearFolderFirst);
            EditorPrefs.SetBool(_openFolderAfterBuildSaveKey, _isOpenFolder);
#if BUILDTOOL_TRACKING
            EditorPrefs.SetBool(_registerSaveKey, _isRegisterVersionInRemote);
#endif

            var guids = _selectedProfiles
                .Where(p => p != null)
                .Select(p => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(p)))
                .ToArray();

            EditorPrefs.SetString(_selectedProfilesSaveKey, string.Join(",", guids));
        }

        private void LoadAllProfiles()
        {
            _availableProfiles ??= new HashSet<BuildProfile>();
            _selectedProfiles ??= new HashSet<BuildProfile>();
            _extensions ??= new List<IBuildWindowExtension>();

            _availableProfiles.Clear();

            foreach (var profile in BuildTool.GetBuildProfiles())
            {
                if (profile != null)
                {
                    _availableProfiles.Add(profile);
                }
            }
        }

        private void FindAndAddExtensions()
        {
            _extensions.Clear();

            var types = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly =>
                {
                    try
                    {
                        return assembly.GetTypes();
                    }
                    catch (ReflectionTypeLoadException e)
                    {
                        return e.Types.Where(t => t != null);
                    }
                })
                .Where(t => t.IsClass && !t.IsAbstract && Attribute.IsDefined(t, typeof(BuildExtensionAttribute)))
                .Select(t => new
                {
                    Type = t,
                    Attr = (BuildExtensionAttribute) Attribute.GetCustomAttribute(t, typeof(BuildExtensionAttribute))
                })
                .OrderBy(x => x.Attr.Order);

            foreach (var item in types)
            {
                try
                {
                    var instance = Activator.CreateInstance(item.Type);

                    if (instance is IBuildWindowExtension ext)
                    {
                        _extensions.Add(ext);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError(string.Format(TextContent.ErrExtensionInit, nameof(BuildWindow), item.Type.FullName,
                        ex.Message));
                }
            }
        }

        private async Task BuildSelectedAsync()
        {
            _isBuildInProcess = true;
            _scrollContainer?.SetEnabled(false);
            UpdateBuildButtonState();
            try
            {
                var request = new BuildExecutionRequest
                {
                    Profiles = _selectedProfiles.Where(p => p != null).ToList(),
                    IsDevelopment = _isDevelopment,
                    CleanBuild = _isCleanBuild,
                    ClearFolderFirst = _isClearFolderFirst,
#if BUILDTOOL_TRACKING
                    RegisterVersion = _isRegisterVersionInRemote,
#else
                    RegisterVersion = false,
#endif
                    OpenFolder = _isOpenFolder,
                    ContinueOnError = false
                };

                var result = await BuildPipelineRunner.RunAsync(request);
                if (result.Success)
                {
                    Debug.Log(TextContent.MsgBuildComplete);
                }
                else
                {
                    Debug.LogError($"{TextContent.MsgBuildCancel}{result.ErrorMessage}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"{TextContent.MsgBuildCancel}{e.Message}\n StackTrace: {e.StackTrace}");
            }
            finally
            {
                _isBuildInProcess = false;
                _scrollContainer?.SetEnabled(true);
                UpdateBuildButtonState();
            }
        }

        private void ClearPersistentData()
        {
            if (!EditorUtility.DisplayDialog("Clear AppData",
                    "Are you sure you want to delete all persistent data? This cannot be undone.",
                    "Delete",
                    "Cancel"))
            {
                return;
            }

            var persistentPath = Application.persistentDataPath;
            if (Directory.Exists(persistentPath))
            {
                BuildPipelineRunner.DeleteFilesAndDirectories(persistentPath);
            }

            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                var localLow = Path.Combine(
                    $"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}Low",
                    Application.companyName,
                    Application.productName
                );
                if (Directory.Exists(localLow) &&
                    !string.Equals(Path.GetFullPath(localLow), Path.GetFullPath(persistentPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    BuildPipelineRunner.DeleteFilesAndDirectories(localLow);
                }
            }
        }

        private static class TextContent
        {
            public const string WindowTitle = "Build Tool";
            public const string MenuItem = "Tools/Build/Show Build Window %#&b";

            public const string HeaderOptions = "Build Options";
            public const string HintOptions = "General build flags, version tracking, and output behaviors.";
            public const string HeaderExtensions = "Extensions";

            public const string BtnClearAppData = "Clear AppData";
            public const string BtnOpenAppData = "Open AppData";
            public const string BtnOpenBuildFolder = "Open Build Folder";
            public const string BtnSetLinuxProfile = "Switch to Linux";
            public const string BuildButtonText = "Building...";
            public const string BtnBuild = "Build";

            public const string MsgNoProfiles = "No build profiles selected";

            public const string MsgLinuxWarning =
                "To build Linux Standalone, switch the active build target to Linux to allow toolchain compilation " +
                "without interrupting the build sequence.";

            public const string MsgBuildComplete = "<color=yellow>Build process completed!</color>";
            public const string MsgBuildCancel = "Cancel build sequence: ";

            public const string ErrExtensionInit = "[{0}] Extension Init Error \"{1}\": {2}";

            public const string ToggleDev = "Development build";
            public const string SubDev = "Enable profiler connection, script debugging, and device logs";
            public const string HelpDev =
                "Builds with BuildOptions.Development.\n" +
                "• Enables script debugging, Unity Profiler connection, and device console logging.\n" +
                "• Increases binary size and reduces runtime performance (uncheck for release).";

            public const string ToggleCleanBuild = "Clean build";
            public const string SubCleanBuild = "Clear Unity build cache before compiling to force full asset rebuild";
            public const string HelpCleanBuild =
                "Builds with BuildOptions.CleanBuildCache.\n" +
                "• Clears the build cache before compiling, ensuring all assets and shaders are rebuilt " +
                "from scratch.";

            public const string ToggleClear = "Clear build folder first";
            public const string SubClear = "Delete previous build files before compiling to ensure a clean build";
            public const string HelpClear =
                "Deletes all existing files in the target build folder before compiling.\n" +
                "• Guarantees a clean build and prevents conflicts with obsolete DLLs or stale assets.";

            public const string ToggleOpen = "Open folder after build";
            public const string SubOpen = "Reveal the output directory in file explorer upon completion";
            public const string HelpOpen =
                "Automatically opens the target build directory in Windows Explorer (or Finder) " +
                "when all profiles finish.";

            public const string ToggleRegister = "Register version";
            public const string SubRegister = "Push build index to Git tracker (off = local 'not for sharing' build)";
            public const string HelpRegister =
                "Registers the build in the Git tracking repository and increments the build index.\n" +
                "• Enabled: pushes tracking commit, embeds official BuildInfo.asset for public sharing.\n" +
                "• Disabled: marks as local build ('NOT FOR SHARING'), skips Git pushes and index increment.";
        }
    }
}

