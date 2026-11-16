using System.Diagnostics;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BuildTool.Editor.Notifications.Channels
{
    [NotificationChannel(40, "Desktop / OS")]
    public class DesktopNotificationChannel : NotificationChannelBase
    {
        public override string Id => "desktop";
        public override string DisplayName => "Desktop / OS";
        public override string Description =>
            "Shows local notifications and plays sound when builds finish (offline, zero-configuration).";
        public override int Order => 40;
        public override string DotColor => "#38BDF8";

        public override bool IsEnabled
        {
            get => BuildNotificationSettings.DesktopEnabled;
            set => BuildNotificationSettings.DesktopEnabled = value;
        }

        public override bool IsConfigured => true;

        protected override VisualElement CreateBodyGUI()
        {
            var body = new VisualElement();
            var soundToggle = new Toggle("Play System Sound")
            {
                value = BuildNotificationSettings.DesktopPlaySound
            };
            soundToggle.RegisterValueChangedCallback(evt => BuildNotificationSettings.DesktopPlaySound = evt.newValue);
            body.Add(soundToggle);

            body.Add(CreateTestButton(SendTestNotificationAsync));
            return body;
        }

        public override Task<bool> SendNotificationAsync(BuildNotificationContext context)
        {
            if (!IsEnabled)
            {
                return Task.FromResult(false);
            }

            var title = $"{context.GetStatusEmoji()} {context.GetStatusTitle()}: {context.ProjectName}";
            var message =
                $"{context.TargetPlatform} | Duration: {context.FormattedDuration} | Size: {context.FormattedSize}";

            ShowLocalAlert(title, message);
            return Task.FromResult(true);
        }

        public override Task<bool> SendBatchNotificationAsync(BuildBatchNotificationContext context)
        {
            if (!IsEnabled)
            {
                return Task.FromResult(false);
            }

            var title = $"{context.GetStatusEmoji()} {context.GetStatusTitle()}: {context.ProjectName}";
            var message =
                $"{context.GetStatusEmoji()} Batch: {context.SucceededProfiles}/{context.TotalProfiles} " +
                $"Profiles Succeeded ({context.FormattedDuration})";

            ShowLocalAlert(title, message);
            return Task.FromResult(true);
        }

        public override Task<bool> SendTestNotificationAsync()
        {
            var title = "🔔 Build Tool — Test";
            var message = "Desktop alert is operational! 🎉";

            ShowLocalAlert(title, message);
            return Task.FromResult(true);
        }

        private static void ShowLocalAlert(string title, string message)
        {
            EditorApplication.delayCall += () =>
            {
                if (BuildNotificationSettings.DesktopPlaySound)
                {
                    EditorApplication.Beep();
                }

                var focusedWindow = EditorWindow.focusedWindow;
                if (focusedWindow != null)
                {
                    focusedWindow.ShowNotification(new GUIContent($"{title}\n{message}"), 4f);
                }
                else
                {
                    var windows = Resources.FindObjectsOfTypeAll<EditorWindow>();
                    if (windows.Length > 0)
                    {
                        windows[0].ShowNotification(new GUIContent($"{title}\n{message}"), 4f);
                    }
                }

                if (Application.platform == RuntimePlatform.WindowsEditor)
                {
                    TryWindowsNotification(title, message);
                }
            };
        }

        private static void TryWindowsNotification(string title, string message)
        {
            if (Application.isBatchMode)
            {
                return;
            }

            try
            {
                var titleBase64 = System.Convert.ToBase64String(
                    System.Text.Encoding.Unicode.GetBytes(title ?? string.Empty));
                var msgBase64 = System.Convert.ToBase64String(
                    System.Text.Encoding.Unicode.GetBytes(message ?? string.Empty));

                var psCommand =
                    "[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, " +
                    "ContentType = WindowsRuntime] > $null; " +
                    $"$t = [System.Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('{titleBase64}')); " +
                    $"$m = [System.Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('{msgBase64}')); " +
                    "$xml = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent(" +
                    "[Windows.UI.Notifications.ToastTemplateType]::ToastText02); " +
                    "$xml.GetElementsByTagName('text').Item(0).AppendChild($xml.CreateTextNode($t)) > $null; " +
                    "$xml.GetElementsByTagName('text').Item(1).AppendChild($xml.CreateTextNode($m)) > $null; " +
                    "$toast = [Windows.UI.Notifications.ToastNotification]::new($xml); " +
                    "[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier(" +
                    "'Unity Build Tool').Show($toast)";

                var psi = new ProcessStartInfo(
                    "powershell",
                    $"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"{psCommand}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var process = Process.Start(psi);
            }
            catch
            {
            }
        }
    }
}

