using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BuildTool.Editor.Notifications
{
    public static class BuildNotificationSettingsProvider
    {
        public const string SettingsPath = "Project/Build Tool/Notifications";
        private static string UssPath => PackagePathHelper.GetAssetPath("Editor/Core/Notifications/BuildNotificationSettings.uss");

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new SettingsProvider(SettingsPath, SettingsScope.Project)
            {
                label = "Notifications",
                activateHandler = (searchContext, rootElement) =>
                {
                    BuildUI(rootElement);
                },
                keywords = new HashSet<string>(new[]
                {
                    "Notifications", "Discord", "Slack", "Webhook", "Telegram", "Desktop", "Build", "Bot", "Channel"
                })
            };
        }

        private static void BuildUI(VisualElement rootElement)
        {
            rootElement.Clear();
            rootElement.AddToClassList("notif-settings-root");

            LoadStyleSheet(rootElement);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("notif-settings-scroll");
            rootElement.Add(scroll);

            var title = new Label("Build Notifications");
            title.AddToClassList("notif-page-title");
            scroll.Add(title);

            var subtitle = new Label(
                "Configure multi-channel alerts delivered when platform builds complete. " +
                "Discovers built-in and custom INotificationChannel implementations automatically.");
            subtitle.AddToClassList("notif-page-subtitle");
            scroll.Add(subtitle);

            var generalCard = new VisualElement();
            generalCard.AddToClassList("notif-channel-card");
            generalCard.AddToClassList("notif-general-card");
            scroll.Add(generalCard);

            var masterRow = CreateSettingRow(
                "Enable Build Notifications",
                "Send build status alerts to enabled channels below.",
                BuildNotificationSettings.MasterEnabled,
                out var masterToggle);
            generalCard.Add(masterRow);

            var divider = new VisualElement();
            divider.AddToClassList("notif-setting-divider");
            generalCard.Add(divider);

            var perProfileRow = CreateSettingRow(
                "Notify After Each Profile",
                "When disabled, notifications are only sent once as a batch summary when the entire sequence finishes.",
                BuildNotificationSettings.NotifyPerProfile,
                out var perProfileToggle);
            perProfileRow.SetEnabled(BuildNotificationSettings.MasterEnabled);
            perProfileToggle.RegisterValueChangedCallback(evt =>
            {
                BuildNotificationSettings.NotifyPerProfile = evt.newValue;
            });
            generalCard.Add(perProfileRow);

            var contentContainer = new VisualElement();
            contentContainer.SetEnabled(BuildNotificationSettings.MasterEnabled);
            scroll.Add(contentContainer);

            masterToggle.RegisterValueChangedCallback(evt =>
            {
                BuildNotificationSettings.MasterEnabled = evt.newValue;
                perProfileRow.SetEnabled(evt.newValue);
                contentContainer.SetEnabled(evt.newValue);
            });

            var channels = BuildNotificationManager.Channels;

            foreach (var channel in channels)
            {
                try
                {
                    var card = channel.CreateSettingsGUI();
                    if (card != null)
                    {
                        contentContainer.Add(card);
                    }
                }
                catch (Exception ex)
                {
                    contentContainer.Add(new HelpBox(
                        $"Error loading settings for '{channel.DisplayName}': {ex.Message}",
                        HelpBoxMessageType.Error));
                }
            }
        }

        private static VisualElement CreateSettingRow(
            string title,
            string description,
            bool initialValue,
            out Toggle toggle)
        {
            var row = new VisualElement();
            row.AddToClassList("notif-setting-row");

            var textCol = new VisualElement();
            textCol.AddToClassList("notif-setting-text");

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("notif-setting-title");
            textCol.Add(titleLabel);

            if (!string.IsNullOrEmpty(description))
            {
                var descLabel = new Label(description);
                descLabel.AddToClassList("notif-setting-desc");
                textCol.Add(descLabel);
            }

            var switchToggle = new Toggle { value = initialValue };
            ApplySwitchStyle(switchToggle);
            row.Add(switchToggle);

            row.Add(textCol);

            row.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target == switchToggle || switchToggle.Contains(evt.target as VisualElement))
                {
                    return;
                }
                if (row.enabledInHierarchy)
                {
                    switchToggle.value = !switchToggle.value;
                }
            });

            toggle = switchToggle;
            return row;
        }

        internal static void ApplySwitchStyle(Toggle toggle)
        {
            if (toggle == null)
            {
                return;
            }

            toggle.AddToClassList("notif-switch");
            var checkmark = toggle.Q(className: Toggle.checkmarkUssClassName);
            if (checkmark != null && checkmark.Q(className: "notif-switch-knob") == null)
            {
                var knob = new VisualElement();
                knob.AddToClassList("notif-switch-knob");
                knob.pickingMode = PickingMode.Ignore;
                checkmark.Add(knob);
            }
        }

        private static void LoadStyleSheet(VisualElement rootElement)
        {
            var styleSheet = PackagePathHelper.LoadStyleSheet("Editor/Core/Notifications/BuildNotificationSettings.uss", "BuildNotificationSettings");
            if (styleSheet != null && !rootElement.styleSheets.Contains(styleSheet))
            {
                rootElement.styleSheets.Add(styleSheet);
            }
        }
    }
}

