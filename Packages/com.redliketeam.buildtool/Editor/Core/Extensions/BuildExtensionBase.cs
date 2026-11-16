using System;
using System.Collections.Generic;
using BuildTool.Editor.Processors;
using UnityEditor;
using UnityEditor.Build.Profile;
using UnityEngine;
using UnityEngine.UIElements;

namespace BuildTool.Editor.Extensions
{
    /// <summary>
    /// Convenient base class for creating Build Tool extensions with UI Toolkit.
    /// Provides automatic master toggle, collapsible card layout, EditorPrefs persistence,
    /// and standard UI Toolkit helper builders matching the tool's design system.
    /// </summary>
    public abstract class BuildExtensionBase : IBuildWindowExtension
    {
        private string _enabledSaveKey;

        public abstract string Title { get; }
        public virtual string Description => null;
        public virtual bool DefaultEnabled => false;

        public virtual bool IsEnabled
        {
            get => EditorPrefs.GetBool(EnabledSaveKey, DefaultEnabled);
            set => EditorPrefs.SetBool(EnabledSaveKey, value);
        }

        protected virtual string EnabledSaveKey =>
            _enabledSaveKey ??= GetNamespacedKey("enabled");

        /// <summary>
        /// Creates the root VisualElement for this extension inside the Build Window.
        /// Generates a styled card with a header, master toggle, and collapsible body container.
        /// </summary>
        public virtual VisualElement CreateGUI(IEnumerable<BuildProfile> profiles)
        {
            var card = new VisualElement();
            card.AddToClassList("build-extension-card");

            var headerRow = new VisualElement();
            headerRow.AddToClassList("build-extension-header-row");

            var masterToggle = new Toggle
            {
                value = IsEnabled
            };
            masterToggle.AddToClassList("build-extension-master-toggle");

            var titleLabel = new Label(Title);
            titleLabel.AddToClassList("build-extension-title-label");

            headerRow.Add(masterToggle);
            headerRow.Add(titleLabel);

            var badges = CreateBadgesGUI();
            if (badges != null)
            {
                headerRow.Add(badges);
            }

            card.Add(headerRow);

            if (!string.IsNullOrEmpty(Description))
            {
                var descLabel = new Label(Description);
                descLabel.AddToClassList("build-extension-desc-label");
                card.Add(descLabel);
            }

            var bodyContainer = new VisualElement();
            bodyContainer.AddToClassList("build-extension-body");
            bodyContainer.style.display = IsEnabled ? DisplayStyle.Flex : DisplayStyle.None;

            var bodyContent = CreateBodyGUI(profiles);
            if (bodyContent != null)
            {
                bodyContainer.Add(bodyContent);
            }
            card.Add(bodyContainer);

            masterToggle.RegisterValueChangedCallback(evt =>
            {
                IsEnabled = evt.newValue;
                bodyContainer.style.display = evt.newValue ? DisplayStyle.Flex : DisplayStyle.None;
                OnEnabledChanged(evt.newValue);
            });

            headerRow.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target == titleLabel)
                {
                    masterToggle.value = !masterToggle.value;
                }
            });

            return card;
        }

        /// <summary>
        /// Override to build the inner controls of the extension (shown when the extension is enabled).
        /// </summary>
        public abstract VisualElement CreateBodyGUI(IEnumerable<BuildProfile> profiles);

        protected virtual void OnEnabledChanged(bool isEnabled)
        {
        }

        protected virtual bool IsPreprocessor => this is IBuildPreprocessor;
        protected virtual bool IsPostprocessor => this is IBuildPostprocessor;

        protected virtual VisualElement CreateBadgesGUI()
        {
            var isPre = IsPreprocessor;
            var isPost = IsPostprocessor;

            if (!isPre && !isPost)
            {
                return null;
            }

            var container = new VisualElement();
            container.AddToClassList("build-extension-badges");

            if (isPre)
            {
                var preBadge = new Label("Pre-Process");
                preBadge.AddToClassList("build-extension-badge");
                preBadge.AddToClassList("build-extension-badge-pre");
                preBadge.tooltip = "Pre-process extension: executes before build";
                container.Add(preBadge);
            }

            if (isPost)
            {
                var postBadge = new Label("Post-Process");
                postBadge.AddToClassList("build-extension-badge");
                postBadge.AddToClassList("build-extension-badge-post");
                postBadge.tooltip = "Post-process extension: executes after build";
                container.Add(postBadge);
            }

            return container;
        }

        /// <summary>
        /// Creates an auto-persisting toggle with checkmark on the left and label on the right.
        /// Saves and loads value from EditorPrefs automatically using the provided key.
        /// </summary>
        protected VisualElement CreateToggle(
            string label, string prefKey, bool defaultValue = false, string tooltip = null)
        {
            var initial = GetBool(prefKey, defaultValue);
            return CreateToggle(label, initial, val => SaveBool(prefKey, val), tooltip);
        }

        /// <summary>
        /// Creates a styled toggle with checkmark on the left and label on the right with a custom change handler.
        /// </summary>
        protected VisualElement CreateToggle(
            string label, bool initialValue, Action<bool> onChanged, string tooltip = null)
        {
            var row = new VisualElement();
            row.AddToClassList("build-extension-toggle-row");
            if (!string.IsNullOrEmpty(tooltip))
            {
                row.tooltip = tooltip;
            }

            var toggle = new Toggle { value = initialValue };
            toggle.AddToClassList("build-extension-toggle");
            toggle.RegisterValueChangedCallback(evt => onChanged?.Invoke(evt.newValue));

            var lbl = new Label(label);
            lbl.AddToClassList("build-extension-toggle-label");

            row.Add(toggle);
            row.Add(lbl);

            row.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target == toggle || toggle.Contains(evt.target as VisualElement))
                {
                    return;
                }
                toggle.value = !toggle.value;
            });

            return row;
        }

        /// <summary>
        /// Creates an auto-persisting horizontal text field with a label and optional tooltip.
        /// Saves and loads value from EditorPrefs automatically using the provided key.
        /// </summary>
        protected TextField CreateTextField(
            string label, string prefKey, string defaultValue = "", string tooltip = null)
        {
            var initial = GetString(prefKey, defaultValue);
            return CreateTextField(label, initial, val => SaveString(prefKey, val), tooltip);
        }

        /// <summary>
        /// Creates a styled horizontal text field with a label and optional tooltip with a custom change handler.
        /// </summary>
        protected TextField CreateTextField(
            string label, string initialValue, Action<string> onChanged, string tooltip = null)
        {
            var field = new TextField(label) { value = initialValue };
            field.AddToClassList("build-extension-textfield");
            if (!string.IsNullOrEmpty(tooltip))
            {
                field.tooltip = tooltip;
            }
            field.RegisterValueChangedCallback(evt => onChanged?.Invoke(evt.newValue));
            return field;
        }

        /// <summary>
        /// Creates an auto-persisting integer field with a label and optional tooltip.
        /// </summary>
        protected IntegerField CreateIntegerField(
            string label, string prefKey, int defaultValue = 0, string tooltip = null)
        {
            var initial = GetInt(prefKey, defaultValue);
            return CreateIntegerField(label, initial, val => SaveInt(prefKey, val), tooltip);
        }

        /// <summary>
        /// Creates a styled integer field with a custom change handler.
        /// </summary>
        protected IntegerField CreateIntegerField(
            string label, int initialValue, Action<int> onChanged, string tooltip = null)
        {
            var field = new IntegerField(label) { value = initialValue };
            field.AddToClassList("build-extension-integerfield");
            if (!string.IsNullOrEmpty(tooltip))
            {
                field.tooltip = tooltip;
            }
            field.RegisterValueChangedCallback(evt => onChanged?.Invoke(evt.newValue));
            return field;
        }

        /// <summary>
        /// Creates a path selector field with a text field and a browse button.
        /// Automatically persists to EditorPrefs using the provided key.
        /// </summary>
        protected VisualElement CreateFolderField(
            string label,
            string prefKey,
            string defaultPath = "",
            string dialogTitle = "Select Folder",
            string tooltip = null)
        {
            var row = new VisualElement();
            row.AddToClassList("build-extension-path-row");
            if (!string.IsNullOrEmpty(tooltip))
            {
                row.tooltip = tooltip;
            }

            var textField = CreateTextField(label, prefKey, defaultPath);
            textField.AddToClassList("build-extension-path-input");

            var browseBtn = CreateButton("...", () =>
            {
                var currentPath = textField.value;
                var startDir = string.IsNullOrEmpty(currentPath) || !System.IO.Directory.Exists(currentPath)
                    ? Application.dataPath
                    : currentPath;
                var selected = EditorUtility.OpenFolderPanel(dialogTitle, startDir, "");
                if (!string.IsNullOrEmpty(selected))
                {
                    textField.value = selected;
                }
            }, "Browse folder...");
            browseBtn.AddToClassList("build-extension-browse-btn");

            row.Add(textField);
            row.Add(browseBtn);
            return row;
        }

        /// <summary>
        /// Creates an auto-persisting dropdown field with choices and automatic EditorPrefs persistence.
        /// </summary>
        protected DropdownField CreateDropdown(
            string label,
            List<string> choices,
            string prefKey,
            string defaultChoice = null,
            string tooltip = null)
        {
            var initial = GetString(
                prefKey, defaultChoice ?? (choices != null && choices.Count > 0 ? choices[0] : ""));
            return CreateDropdown(label, choices, initial, val => SaveString(prefKey, val), tooltip);
        }

        /// <summary>
        /// Creates a styled dropdown field with choices and a custom change callback.
        /// </summary>
        protected DropdownField CreateDropdown(
            string label, List<string> choices, string initialValue, Action<string> onChanged, string tooltip = null)
        {
            var dropdown = new DropdownField(label, choices ?? new List<string>(), initialValue);
            dropdown.AddToClassList("build-extension-dropdown");
            if (!string.IsNullOrEmpty(tooltip))
            {
                dropdown.tooltip = tooltip;
            }
            dropdown.RegisterValueChangedCallback(evt => onChanged?.Invoke(evt.newValue));
            return dropdown;
        }

        /// <summary>
        /// Creates a styled button for the extension.
        /// </summary>
        protected Button CreateButton(string text, Action onClick, string tooltip = null)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("build-extension-button");
            if (!string.IsNullOrEmpty(tooltip))
            {
                button.tooltip = tooltip;
            }
            return button;
        }

        /// <summary>
        /// Creates an informative or warning HelpBox inside the extension.
        /// </summary>
        protected HelpBox CreateHelpBox(string message, HelpBoxMessageType type = HelpBoxMessageType.Info)
        {
            var helpBox = new HelpBox(message, type);
            helpBox.AddToClassList("build-extension-helpbox");
            return helpBox;
        }

        /// <summary>
        /// Creates a horizontal flex row for arranging multiple elements side-by-side.
        /// </summary>
        protected VisualElement CreateRow(params VisualElement[] elements)
        {
            var row = new VisualElement();
            row.AddToClassList("build-extension-row");
            if (elements != null)
            {
                foreach (var el in elements)
                {
                    if (el != null)
                    {
                        row.Add(el);
                    }
                }
            }
            return row;
        }

        protected string GetNamespacedKey(string key) =>
            $"build_ext_{GetType().Name.ToLowerInvariant()}_{key}".AppendProjectName();

        protected bool GetBool(string key, bool defaultValue = false) =>
            EditorPrefs.GetBool(GetNamespacedKey(key), defaultValue);

        protected void SaveBool(string key, bool value) =>
            EditorPrefs.SetBool(GetNamespacedKey(key), value);

        protected string GetString(string key, string defaultValue = "") =>
            EditorPrefs.GetString(GetNamespacedKey(key), defaultValue);

        protected void SaveString(string key, string value) =>
            EditorPrefs.SetString(GetNamespacedKey(key), value);

        protected int GetInt(string key, int defaultValue = 0) =>
            EditorPrefs.GetInt(GetNamespacedKey(key), defaultValue);

        protected void SaveInt(string key, int value) =>
            EditorPrefs.SetInt(GetNamespacedKey(key), value);
    }
}
