using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildTool.Editor.DirectoryBuilders;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace BuildTool.Editor
{
    public static class BuildToolSettingsProvider
    {
        private const string SettingsPath = "Project/Build Tool";
        private const string UssPath = "Packages/com.buildtool/Editor/Core/BuildToolSettings.uss";

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            var settings = BuildToolSettings.instance;
            LoadProfiles(settings);

            var provider = new SettingsProvider(SettingsPath, SettingsScope.Project)
            {
                label = "Build Tool",
                activateHandler = (searchContext, rootElement) =>
                {
                    BuildUI(rootElement);
                },
                keywords = new HashSet<string>(new[]
                {
                    "Build", "Tool", "Path", "Directory", "Profile", "Pattern", "Token"
                })
            };

            return provider;
        }

        private static void BuildUI(VisualElement rootElement)
        {
            rootElement.Clear();
            rootElement.AddToClassList("buildtool-settings-root");

            LoadStyleSheet(rootElement);

            var scrollView = new ScrollView(ScrollViewMode.Vertical);
            scrollView.AddToClassList("buildtool-settings-scroll");
            rootElement.Add(scrollView);

            var settings = BuildToolSettings.instance;
            SerializedObject serializedSettings = new(settings);

            var pageTitle = new Label("Build Tool");
            pageTitle.AddToClassList("buildtool-page-title");
            scrollView.Add(pageTitle);

            var rootCard = new VisualElement();
            rootCard.AddToClassList("buildtool-section-card");
            scrollView.Add(rootCard);

            var rootTitle = new Label("Root Build Directory");
            rootTitle.AddToClassList("buildtool-section-title");
            rootCard.Add(rootTitle);

            var rootHint = new Label("Common destination directory where all sequential platform builds are output.");
            rootHint.AddToClassList("buildtool-hint-text");
            rootCard.Add(rootHint);

            var patternRow = new VisualElement();
            patternRow.AddToClassList("buildtool-row");

            var patternProp = serializedSettings.FindProperty(nameof(settings.RootFolderPattern));
            var patternField = new TextField("Directory Pattern")
            {
                value = settings.RootFolderPattern,
                tooltip = "Root directory pattern supporting tokens: " +
                    "{Project}, {Company}, {Version}, {BuildIndex}, {Date}, {Platform}."
            };
            patternField.AddToClassList("buildtool-field-flex");
            patternRow.Add(patternField);

            var tokensBtn = new Button
            {
                text = "Tokens ▾",
                tooltip = "Insert dynamic path tokens into the directory pattern."
            };
            tokensBtn.AddToClassList("buildtool-btn-tokens");
            patternRow.Add(tokensBtn);
            rootCard.Add(patternRow);

            var patternValidationBox = new HelpBox("", HelpBoxMessageType.Error)
            {
                style = { display = DisplayStyle.None, marginTop = 4 }
            };
            rootCard.Add(patternValidationBox);

            var previewRow = new VisualElement();
            previewRow.AddToClassList("buildtool-row");

            var previewField = new TextField("Preview")
            {
                value = BuildTool.GetRootBuildPath(),
                isReadOnly = true,
                tooltip = "Live resolved destination path on disk."
            };
            previewField.AddToClassList("buildtool-subtle-preview");
            previewRow.Add(previewField);

            var copyIcon = EditorGUIUtility.IconContent("TreeEditor.Duplicate");
            var copyBtn = new Button(() =>
            {
                EditorGUIUtility.systemCopyBuffer = previewField.value;
            })
            {
                tooltip = "Copy Root Preview Path"
            };
            copyBtn.AddToClassList("buildtool-btn-sm");
            if (copyIcon?.image != null)
            {
                copyBtn.Add(new Image { image = copyIcon.image, style = { width = 14, height = 14 } });
            }
            else
            {
                copyBtn.text = "C";
            }
            previewRow.Add(copyBtn);
            rootCard.Add(previewRow);

            Action updateAllPreviews = null;

            tokensBtn.clicked += () =>
            {
                ShowRootTokensMenu(token =>
                {
                    var current = patternField.value ?? "";
                    if (!current.EndsWith("/") && !current.EndsWith(" ") &&
                        !current.EndsWith("_") && current.Length > 0)
                    {
                        current += " ";
                    }
                    patternField.value = current + token;
                });
            };

            patternField.RegisterValueChangedCallback(evt =>
            {
                var val = evt.newValue;
                if (!IsValidDirectoryPattern(val, out var patternError))
                {
                    patternValidationBox.text = patternError;
                    patternValidationBox.style.display = DisplayStyle.Flex;
                    return;
                }
                patternValidationBox.style.display = DisplayStyle.None;

                settings.RootFolderPattern = val;
                serializedSettings.Update();
                patternProp.stringValue = val;
                serializedSettings.ApplyModifiedProperties();
                EditorUtility.SetDirty(settings);
                settings.Save();

                previewField.value = BuildTool.GetRootBuildPath();
                updateAllPreviews?.Invoke();
            });

            var profilesCard = new VisualElement();
            profilesCard.AddToClassList("buildtool-section-card");
            scrollView.Add(profilesCard);

            updateAllPreviews = BuildProfilesSection(profilesCard, settings, () => BuildUI(rootElement));
        }

        private static Action BuildProfilesSection(VisualElement container, BuildToolSettings settings, Action onReload)
        {
            container.Clear();

            var count = settings.ProfileConfigs?.Count ?? 0;

            var headerRow = new VisualElement();
            headerRow.AddToClassList("buildtool-section-header-row");

            var profilesTitle = new Label($"Location Path Name Builders ({count})");
            profilesTitle.AddToClassList("buildtool-section-title");
            headerRow.Add(profilesTitle);

            var reloadIcon = EditorGUIUtility.IconContent("d_Refresh");
            var reloadBtn = new Button(() =>
            {
                LoadProfiles(settings);
                onReload?.Invoke();
            })
            {
                tooltip = "Rescan project for newly created or modified Build Profiles."
            };
            reloadBtn.AddToClassList("buildtool-reload-btn");

            if (reloadIcon?.image != null)
            {
                var iconImg = new Image { image = reloadIcon.image };
                iconImg.AddToClassList("buildtool-btn-icon");
                reloadBtn.Add(iconImg);
            }

            var reloadLabel = new Label("Reload Profiles");
            reloadLabel.AddToClassList("buildtool-btn-label");
            reloadBtn.Add(reloadLabel);

            headerRow.Add(reloadBtn);
            container.Add(headerRow);

            var hint = new Label("Configure output location and executable naming patterns for each target profile.");
            hint.AddToClassList("buildtool-hint-text");
            container.Add(hint);

            if (count == 0)
            {
                var warnBox = new HelpBox(
                    "No BuildProfiles found in project. Create one via Assets > Create > Build > Build Profile.",
                    HelpBoxMessageType.Info);
                container.Add(warnBox);
                return null;
            }

            var previewUpdaters = new List<Action>();

            for (var i = 0; i < settings.ProfileConfigs.Count; i++)
            {
                var config = settings.ProfileConfigs[i];
                var profile = config.Profile;
                if (profile == null) continue;

                if (config.PathBuilder == null || config.PathBuilder is not PatternLocationPathBuilder)
                {
                    config.PathBuilder = new PatternLocationPathBuilder();
                }

                var patternBuilder = (PatternLocationPathBuilder) config.PathBuilder;

                var card = new VisualElement();
                card.AddToClassList("buildtool-profile-card");

                var cardHeader = new VisualElement();
                cardHeader.AddToClassList("buildtool-profile-header");

                var profileName = new Label(profile.name);
                profileName.AddToClassList("buildtool-profile-name");
                cardHeader.Add(profileName);

                var target = BuildTool.GetBuildTarget(profile);
                var targetBadge = new Label(target.ToString());
                targetBadge.AddToClassList("buildtool-badge");
                cardHeader.Add(targetBadge);
                card.Add(cardHeader);

                var previewRow = new VisualElement();
                previewRow.AddToClassList("buildtool-row");

                string ComputePreview()
                {
                    var root = BuildTool.GetRootBuildPath();
                    try
                    {
                        return patternBuilder.GetLocationPathName(profile, root)?.Replace('\\', '/') ?? "N/A";
                    }
                    catch
                    {
                        return "Unable to compute preview path";
                    }
                }

                var previewField = new TextField("Path Preview")
                {
                    value = ComputePreview(),
                    isReadOnly = true
                };
                previewField.AddToClassList("buildtool-subtle-preview");
                previewRow.Add(previewField);

                var copyIcon = EditorGUIUtility.IconContent("TreeEditor.Duplicate");
                var copyBtn = new Button(() =>
                {
                    EditorGUIUtility.systemCopyBuffer = previewField.value;
                })
                {
                    tooltip = "Copy Path Preview"
                };
                copyBtn.AddToClassList("buildtool-btn-sm");
                if (copyIcon?.image != null)
                {
                    copyBtn.Add(new Image { image = copyIcon.image, style = { width = 14, height = 14 } });
                }
                else
                {
                    copyBtn.text = "C";
                }
                previewRow.Add(copyBtn);
                card.Add(previewRow);

                Action updateCardPreview = () =>
                {
                    previewField.value = ComputePreview();
                };
                previewUpdaters.Add(updateCardPreview);

                var customToggle = new Toggle("Customize Path Pattern")
                {
                    value = patternBuilder.UseCustomPattern
                };
                customToggle.AddToClassList("buildtool-toggle");
                card.Add(customToggle);

                var patternRow = new VisualElement();
                patternRow.AddToClassList("buildtool-row");
                patternRow.style.display = patternBuilder.UseCustomPattern ? DisplayStyle.Flex : DisplayStyle.None;

                var patternField = new TextField("Pattern")
                {
                    value = patternBuilder.RawPattern ?? PatternLocationPathBuilder.DefaultPattern
                };
                patternField.AddToClassList("buildtool-field-flex");
                patternRow.Add(patternField);

                var tokensBtn = new Button
                {
                    text = "Tokens ▾"
                };
                tokensBtn.AddToClassList("buildtool-btn-tokens");
                patternRow.Add(tokensBtn);
                card.Add(patternRow);

                tokensBtn.clicked += () =>
                {
                    ShowProfileTokensMenu(token =>
                    {
                        var current = patternField.value ?? "";
                        if (!current.EndsWith("/") && !current.EndsWith("_") &&
                            !current.EndsWith(".") && current.Length > 0)
                        {
                            current += "/";
                        }
                        patternField.value = current + token;
                    });
                };

                patternField.RegisterValueChangedCallback(evt =>
                {
                    patternBuilder.RawPattern = evt.newValue;
                    EditorUtility.SetDirty(settings);
                    settings.Save();
                    updateCardPreview();
                });

                customToggle.RegisterValueChangedCallback(evt =>
                {
                    patternBuilder.UseCustomPattern = evt.newValue;
                    patternRow.style.display = evt.newValue ? DisplayStyle.Flex : DisplayStyle.None;
                    EditorUtility.SetDirty(settings);
                    settings.Save();
                    updateCardPreview();
                });

                container.Add(card);
            }

            return () =>
            {
                foreach (var updater in previewUpdaters)
                {
                    updater?.Invoke();
                }
            };
        }

        private static void ShowRootTokensMenu(Action<string> onInsert)
        {
            var menu = new GenericMenu();
            var tokens = new List<(string token, string desc)>
            {
                ("{Version}", "Application.version (e.g. 1.0.0)")
            };

#if BUILDTOOL_TRACKING
            tokens.Add(("{BuildIndex}", "Build index from BuildTracker"));
#endif

            tokens.Add(("{Date}", "Current date (yyyy-MM-dd)"));
            tokens.Add(("{Date:yyyy-MM-dd_HH-mm}", "Date with time format"));
            tokens.Add(("{Time}", "Current time (HH-mm-ss)"));
            tokens.Add(("{ProductName}", "PlayerSettings.productName"));

            foreach (var (token, desc) in tokens)
            {
                menu.AddItem(new GUIContent($"{token}  —  {desc}"), false, () => onInsert?.Invoke(token));
            }

            menu.ShowAsContext();
        }

        private static void ShowProfileTokensMenu(Action<string> onInsert)
        {
            var menu = new GenericMenu();
            var tokens = PatternLocationPathBuilder.GetAvailableTokens();

            foreach (var kvp in tokens)
            {
                var tokenTag = $"{{{kvp.Key}}}";
                var label = $"{tokenTag}  —  {kvp.Value}";
                menu.AddItem(new GUIContent(label), false, () => onInsert?.Invoke(tokenTag));
            }

            menu.ShowAsContext();
        }

        private static void LoadStyleSheet(VisualElement rootElement)
        {
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            if (styleSheet == null)
            {
                var guids = AssetDatabase.FindAssets("BuildToolSettings t:StyleSheet");
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

        private static void LoadProfiles(BuildToolSettings settings)
        {
            var foundProfiles = BuildTool.GetBuildProfiles();

            foreach (var profile in foundProfiles)
            {
                if (settings.ProfileConfigs.All(c => c.Profile != profile))
                {
                    settings.ProfileConfigs.Add(new BuildToolSettings.ProfileConfig
                    {
                        Profile = profile,
                        PathBuilder = new PatternLocationPathBuilder()
                    });
                }
            }

            settings.ProfileConfigs.RemoveAll(c => !c.Profile || !foundProfiles.Contains(c.Profile));
            EditorUtility.SetDirty(settings);
            settings.Save();
        }

        private static bool IsValidDirectoryPattern(string pattern, out string error)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                error = "Directory pattern cannot be empty.";
                return false;
            }

            var invalidChars = Path.GetInvalidPathChars();
            if (pattern.IndexOfAny(invalidChars) >= 0)
            {
                error = "Pattern contains invalid path characters.";
                return false;
            }

            error = null;
            return true;
        }
    }
}

