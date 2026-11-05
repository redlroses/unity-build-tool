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
        private const string UssPath = "Packages/com.buildtool/Editor/Core/Notifications/BuildNotificationSettings.uss";

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

            var masterToggle = new Toggle("Enable Build Notifications")
            {
                value = BuildNotificationSettings.MasterEnabled
            };
            masterToggle.AddToClassList("notif-header-toggle");
            scroll.Add(masterToggle);

            var contentContainer = new VisualElement();
            contentContainer.SetEnabled(BuildNotificationSettings.MasterEnabled);
            scroll.Add(contentContainer);

            masterToggle.RegisterValueChangedCallback(evt =>
            {
                BuildNotificationSettings.MasterEnabled = evt.newValue;
                contentContainer.SetEnabled(evt.newValue);
            });

            var perProfileToggle = new Toggle("Notify After Each Profile")
            {
                value = BuildNotificationSettings.NotifyPerProfile,
                tooltip = "When disabled, notifications are only sent once as a batch summary " +
                    "when the entire sequence finishes."
            };
            perProfileToggle.AddToClassList("notif-header-toggle");
            perProfileToggle.RegisterValueChangedCallback(evt =>
            {
                BuildNotificationSettings.NotifyPerProfile = evt.newValue;
            });
            contentContainer.Add(perProfileToggle);

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

        private static void LoadStyleSheet(VisualElement rootElement)
        {
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            if (styleSheet == null)
            {
                var guids = AssetDatabase.FindAssets("BuildNotificationSettings t:StyleSheet");
                if (guids.Length > 0)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guids[0]);
                    styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
                }
            }

            if (styleSheet != null && !rootElement.styleSheets.Contains(styleSheet))
            {
                rootElement.styleSheets.Add(styleSheet);
            }
        }
    }
}

