using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BuildTool.Editor.Processors;
using UnityEditor;

namespace BuildTool.Editor.Notifications
{
    public static class BuildNotificationSettings
    {
        private static readonly string s_MasterEnabledKey;
        private static readonly string s_NotifyPerProfileKey;
        private static readonly string s_DiscordEnabledKey;
        private static readonly string s_DiscordWebhookKey;
        private static readonly string s_SlackEnabledKey;
        private static readonly string s_SlackWebhookKey;
        private static readonly string s_WebhookEnabledKey;
        private static readonly string s_WebhookEndpointKey;
        private static readonly string s_WebhookAuthKey;
        private static readonly string s_DesktopEnabledKey;
        private static readonly string s_DesktopSoundKey;

        private static readonly string s_TelegramEnabledKey;
        private static readonly string s_TelegramTokenKey;
        private static readonly string s_TelegramChatIdsKey;
        private static readonly string s_LegacyTelegramEnabledKey;
        private static readonly string s_LegacyTelegramTokenKey;
        private static readonly string s_LegacyTelegramChatIdsKey;

        static BuildNotificationSettings()
        {
            s_MasterEnabledKey = "buildtool_notifications_master_enabled".AppendProjectName();
            s_NotifyPerProfileKey = "buildtool_notifications_notify_per_profile".AppendProjectName();
            s_DiscordEnabledKey = "buildtool_notifications_discord_enabled".AppendProjectName();
            s_DiscordWebhookKey = "buildtool_notifications_discord_webhook".AppendProjectName();
            s_SlackEnabledKey = "buildtool_notifications_slack_enabled".AppendProjectName();
            s_SlackWebhookKey = "buildtool_notifications_slack_webhook".AppendProjectName();
            s_WebhookEnabledKey = "buildtool_notifications_webhook_enabled".AppendProjectName();
            s_WebhookEndpointKey = "buildtool_notifications_webhook_endpoint".AppendProjectName();
            s_WebhookAuthKey = "buildtool_notifications_webhook_auth".AppendProjectName();
            s_DesktopEnabledKey = "buildtool_notifications_desktop_enabled".AppendProjectName();
            s_DesktopSoundKey = "buildtool_notifications_desktop_sound".AppendProjectName();

            s_TelegramEnabledKey = "buildtool_notifications_telegram_enabled".AppendProjectName();
            s_TelegramTokenKey = "buildtool_notifications_telegram_token".AppendProjectName();
            s_TelegramChatIdsKey = "buildtool_notifications_telegram_chat_ids".AppendProjectName();
            s_LegacyTelegramEnabledKey = "telegramNotifier_enabled_key".AppendProjectName();
            s_LegacyTelegramTokenKey = "telegramNotifier_token_key".AppendProjectName();
            s_LegacyTelegramChatIdsKey = "telegramNotifier_chatIds_key".AppendProjectName();
        }

        public static bool MasterEnabled
        {
            get => EditorPrefs.GetBool(s_MasterEnabledKey, false);
            set => EditorPrefs.SetBool(s_MasterEnabledKey, value);
        }

        public static bool NotifyPerProfile
        {
            get => EditorPrefs.GetBool(s_NotifyPerProfileKey, false);
            set => EditorPrefs.SetBool(s_NotifyPerProfileKey, value);
        }

        public static bool DiscordEnabled
        {
            get => EditorPrefs.GetBool(s_DiscordEnabledKey, false);
            set => EditorPrefs.SetBool(s_DiscordEnabledKey, value);
        }

        public static string GetDiscordWebhookUrl()
        {
            if (EditorPrefs.HasKey(s_DiscordWebhookKey))
            {
                var encoded = EditorPrefs.GetString(s_DiscordWebhookKey);
                var decoded = TryDecodeBase64(encoded);
                if (!string.IsNullOrEmpty(decoded))
                {
                    return decoded;
                }
            }

            var env = Environment.GetEnvironmentVariable("DISCORD_WEBHOOK_URL");
            return !string.IsNullOrWhiteSpace(env) ? env.Trim() : null;
        }

        public static void SaveDiscordWebhookUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                EditorPrefs.DeleteKey(s_DiscordWebhookKey);
                return;
            }
            EditorPrefs.SetString(s_DiscordWebhookKey, EncodeBase64(url.Trim()));
        }

        public static bool HasExplicitDiscordWebhook => EditorPrefs.HasKey(s_DiscordWebhookKey);

        public static void ClearDiscordWebhook() => EditorPrefs.DeleteKey(s_DiscordWebhookKey);

        public static bool SlackEnabled
        {
            get => EditorPrefs.GetBool(s_SlackEnabledKey, false);
            set => EditorPrefs.SetBool(s_SlackEnabledKey, value);
        }

        public static string GetSlackWebhookUrl()
        {
            if (EditorPrefs.HasKey(s_SlackWebhookKey))
            {
                var encoded = EditorPrefs.GetString(s_SlackWebhookKey);
                var decoded = TryDecodeBase64(encoded);
                if (!string.IsNullOrEmpty(decoded))
                {
                    return decoded;
                }
            }

            var env = Environment.GetEnvironmentVariable("SLACK_WEBHOOK_URL");
            return !string.IsNullOrWhiteSpace(env) ? env.Trim() : null;
        }

        public static void SaveSlackWebhookUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                EditorPrefs.DeleteKey(s_SlackWebhookKey);
                return;
            }
            EditorPrefs.SetString(s_SlackWebhookKey, EncodeBase64(url.Trim()));
        }

        public static bool HasExplicitSlackWebhook => EditorPrefs.HasKey(s_SlackWebhookKey);

        public static void ClearSlackWebhook() => EditorPrefs.DeleteKey(s_SlackWebhookKey);

        public static bool GenericWebhookEnabled
        {
            get => EditorPrefs.GetBool(s_WebhookEnabledKey, false);
            set => EditorPrefs.SetBool(s_WebhookEnabledKey, value);
        }

        public static string GetGenericWebhookUrl()
        {
            if (EditorPrefs.HasKey(s_WebhookEndpointKey))
            {
                var raw = EditorPrefs.GetString(s_WebhookEndpointKey);
                if (!string.IsNullOrEmpty(raw))
                {
                    return raw;
                }
            }

            var env = Environment.GetEnvironmentVariable("BUILD_WEBHOOK_URL");
            return !string.IsNullOrWhiteSpace(env) ? env.Trim() : null;
        }

        public static void SaveGenericWebhookUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                EditorPrefs.DeleteKey(s_WebhookEndpointKey);
                return;
            }
            EditorPrefs.SetString(s_WebhookEndpointKey, url.Trim());
        }

        public static string GetGenericWebhookAuthHeader()
        {
            if (EditorPrefs.HasKey(s_WebhookAuthKey))
            {
                var encoded = EditorPrefs.GetString(s_WebhookAuthKey);
                var decoded = TryDecodeBase64(encoded);
                if (!string.IsNullOrEmpty(decoded))
                {
                    return decoded;
                }
            }

            var env = Environment.GetEnvironmentVariable("BUILD_WEBHOOK_AUTH");
            return !string.IsNullOrWhiteSpace(env) ? env.Trim() : null;
        }

        public static void SaveGenericWebhookAuthHeader(string authHeader)
        {
            if (string.IsNullOrWhiteSpace(authHeader))
            {
                EditorPrefs.DeleteKey(s_WebhookAuthKey);
                return;
            }
            EditorPrefs.SetString(s_WebhookAuthKey, EncodeBase64(authHeader.Trim()));
        }

        public static void ClearGenericWebhook()
        {
            EditorPrefs.DeleteKey(s_WebhookEndpointKey);
            EditorPrefs.DeleteKey(s_WebhookAuthKey);
        }

        public static bool DesktopEnabled
        {
            get => EditorPrefs.GetBool(s_DesktopEnabledKey, false);
            set => EditorPrefs.SetBool(s_DesktopEnabledKey, value);
        }

        public static bool DesktopPlaySound
        {
            get => EditorPrefs.GetBool(s_DesktopSoundKey, true);
            set => EditorPrefs.SetBool(s_DesktopSoundKey, value);
        }

        public static bool TelegramEnabled
        {
            get
            {
                if (EditorPrefs.HasKey(s_TelegramEnabledKey))
                {
                    return EditorPrefs.GetBool(s_TelegramEnabledKey, false);
                }
                if (EditorPrefs.HasKey(s_LegacyTelegramEnabledKey))
                {
                    var val = EditorPrefs.GetBool(s_LegacyTelegramEnabledKey, false);
                    EditorPrefs.SetBool(s_TelegramEnabledKey, val);
                    return val;
                }
                return false;
            }
            set => EditorPrefs.SetBool(s_TelegramEnabledKey, value);
        }

        public static string GetTelegramToken()
        {
            if (EditorPrefs.HasKey(s_TelegramTokenKey))
            {
                var encoded = EditorPrefs.GetString(s_TelegramTokenKey);
                var decoded = TryDecodeBase64(encoded);
                if (!string.IsNullOrEmpty(decoded))
                {
                    return decoded;
                }
            }
            else if (EditorPrefs.HasKey(s_LegacyTelegramTokenKey))
            {
                var encoded = EditorPrefs.GetString(s_LegacyTelegramTokenKey);
                var decoded = TryDecodeBase64(encoded);
                if (!string.IsNullOrEmpty(decoded))
                {
                    EditorPrefs.SetString(s_TelegramTokenKey, encoded);
                    return decoded;
                }
            }

            var env = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
            return !string.IsNullOrWhiteSpace(env) ? env.Trim() : null;
        }

        public static void SaveTelegramToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                EditorPrefs.DeleteKey(s_TelegramTokenKey);
                EditorPrefs.DeleteKey(s_LegacyTelegramTokenKey);
                return;
            }
            EditorPrefs.SetString(s_TelegramTokenKey, EncodeBase64(token.Trim()));
        }

        public static bool HasExplicitTelegramToken =>
            EditorPrefs.HasKey(s_TelegramTokenKey) || EditorPrefs.HasKey(s_LegacyTelegramTokenKey);

        public static void ClearTelegramToken()
        {
            EditorPrefs.DeleteKey(s_TelegramTokenKey);
            EditorPrefs.DeleteKey(s_LegacyTelegramTokenKey);
        }

        public static List<string> GetTelegramChatIds()
        {
            if (EditorPrefs.HasKey(s_TelegramChatIdsKey))
            {
                var combined = EditorPrefs.GetString(s_TelegramChatIdsKey);
                if (string.IsNullOrEmpty(combined))
                {
                    return new List<string>();
                }
                return combined.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(id => id.Trim())
                    .Where(id => !string.IsNullOrEmpty(id))
                    .ToList();
            }

            if (EditorPrefs.HasKey(s_LegacyTelegramChatIdsKey))
            {
                var combined = EditorPrefs.GetString(s_LegacyTelegramChatIdsKey);
                if (string.IsNullOrEmpty(combined))
                {
                    return new List<string>();
                }
                var legacyList = combined.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(id => id.Trim())
                    .Where(id => !string.IsNullOrEmpty(id))
                    .ToList();
                EditorPrefs.SetString(s_TelegramChatIdsKey, combined);
                return legacyList;
            }

            var env = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID")
                ?? Environment.GetEnvironmentVariable("TELEGRAM_CHAT_IDS");

            if (!string.IsNullOrWhiteSpace(env))
            {
                return env.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(id => id.Trim())
                    .Where(id => !string.IsNullOrEmpty(id))
                    .ToList();
            }

            return new List<string>();
        }

        public static void AddTelegramChatId(string chatId)
        {
            if (string.IsNullOrWhiteSpace(chatId))
            {
                return;
            }
            var list = GetTelegramChatIds();
            chatId = chatId.Trim();
            if (!list.Contains(chatId))
            {
                list.Add(chatId);
                EditorPrefs.SetString(s_TelegramChatIdsKey, string.Join(",", list));
            }
        }

        public static void RemoveTelegramChatId(string chatId)
        {
            var list = GetTelegramChatIds();
            if (list.Remove(chatId.Trim()))
            {
                EditorPrefs.SetString(s_TelegramChatIdsKey, string.Join(",", list));
            }
        }

        public static void ClearTelegramChatIds()
        {
            EditorPrefs.DeleteKey(s_TelegramChatIdsKey);
            EditorPrefs.DeleteKey(s_LegacyTelegramChatIdsKey);
        }

        private static string EncodeBase64(string plain)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
        }

        private static string TryDecodeBase64(string encoded)
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            }
            catch
            {
                return null;
            }
        }
    }
}

