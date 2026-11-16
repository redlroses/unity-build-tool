using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BuildTool.Editor.Notifications.Channels
{
    [NotificationChannel(50, "Telegram")]
    public class TelegramNotificationChannel : NotificationChannelBase
    {
        public override string Id => "telegram";
        public override string DisplayName => "Telegram";
        public override string Description =>
            "Sends HTML build reports and summaries directly to Telegram channels or groups.";
        public override int Order => 50;
        public override string DotColor => "#229ED9";

        public override bool IsEnabled
        {
            get => BuildNotificationSettings.TelegramEnabled;
            set => BuildNotificationSettings.TelegramEnabled = value;
        }

        public override bool IsConfigured
        {
            get
            {
                var token = BuildNotificationSettings.GetTelegramToken();
                var chatIds = BuildNotificationSettings.GetTelegramChatIds();
                return !string.IsNullOrWhiteSpace(token) && chatIds.Count > 0;
            }
        }

        protected override VisualElement CreateBodyGUI()
        {
            var body = new VisualElement();

            body.Add(CreateMaskedField(
                "Bot Token",
                BuildNotificationSettings.GetTelegramToken,
                BuildNotificationSettings.SaveTelegramToken,
                BuildNotificationSettings.ClearTelegramToken,
                "TELEGRAM_BOT_TOKEN",
                "Enter bot token provided by @BotFather.",
                null
            ));

            var recipientsTitle = new Label("Recipients (Chat IDs)");
            recipientsTitle.style.marginTop = 8;
            recipientsTitle.style.fontSize = 12;
            recipientsTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            body.Add(recipientsTitle);

            var addRow = new VisualElement();
            addRow.AddToClassList("notif-row");

            var inputField = new TextField();
            inputField.AddToClassList("notif-field-flex");
            addRow.Add(inputField);

            var addBtn = new Button
            {
                text = "+ Add"
            };
            addBtn.AddToClassList("notif-btn");
            addBtn.SetEnabled(false);
            addRow.Add(addBtn);

            var listContainer = new VisualElement();
            listContainer.AddToClassList("notif-chat-list");

            void RefreshList()
            {
                listContainer.Clear();
                var currentIds = BuildNotificationSettings.GetTelegramChatIds();

                if (currentIds.Count == 0)
                {
                    listContainer.Add(new HelpBox(
                        "No chat IDs added. Add user ID or group chat ID (e.g. -100123456789).",
                        HelpBoxMessageType.Info));
                }
                else
                {
                    foreach (var chatId in currentIds)
                    {
                        var row = new VisualElement();
                        row.AddToClassList("notif-chat-item");

                        var idLabel = new Label(chatId);
                        idLabel.AddToClassList("notif-chat-id-label");
                        row.Add(idLabel);

                        var trashBtn = new Button(() =>
                        {
                            BuildNotificationSettings.RemoveTelegramChatId(chatId);
                            RefreshList();
                        })
                        {
                            text = "✕",
                            tooltip = "Remove Chat ID"
                        };
                        trashBtn.AddToClassList("notif-btn-sm");
                        row.Add(trashBtn);

                        listContainer.Add(row);
                    }
                }
            }

            inputField.RegisterValueChangedCallback(evt =>
            {
                addBtn.SetEnabled(!string.IsNullOrWhiteSpace(evt.newValue));
            });

            addBtn.clicked += () =>
            {
                var val = inputField.value?.Trim();
                if (!string.IsNullOrWhiteSpace(val))
                {
                    BuildNotificationSettings.AddTelegramChatId(val);
                    inputField.value = string.Empty;
                    RefreshList();
                }
            };

            body.Add(addRow);
            body.Add(listContainer);
            RefreshList();

            body.Add(CreateTestButton(SendTestNotificationAsync, () => IsConfigured));
            return body;
        }

        public override async Task<bool> SendNotificationAsync(BuildNotificationContext context)
        {
            var token = BuildNotificationSettings.GetTelegramToken();
            var chatIds = BuildNotificationSettings.GetTelegramChatIds();

            if (string.IsNullOrWhiteSpace(token) || chatIds.Count == 0)
            {
                return false;
            }

            var message = FormatTelegramMessage(context);
            if (message.Length > 4000)
            {
                message = message.Substring(0, 3990) + "\n...";
            }

            var tasks = chatIds.Select(chatId => SendMessageAsync(token, chatId, message));
            var results = await Task.WhenAll(tasks).ConfigureAwait(false);
            return results.All(r => r);
        }

        public override async Task<bool> SendBatchNotificationAsync(BuildBatchNotificationContext context)
        {
            var token = BuildNotificationSettings.GetTelegramToken();
            var chatIds = BuildNotificationSettings.GetTelegramChatIds();

            if (string.IsNullOrWhiteSpace(token) || chatIds.Count == 0)
            {
                return false;
            }

            var message = FormatTelegramBatchMessage(context);
            if (message.Length > 4000)
            {
                message = message.Substring(0, 3990) + "\n...";
            }

            var tasks = chatIds.Select(chatId => SendMessageAsync(token, chatId, message));
            var results = await Task.WhenAll(tasks).ConfigureAwait(false);
            return results.All(r => r);
        }

        public override async Task<bool> SendTestNotificationAsync()
        {
            var token = BuildNotificationSettings.GetTelegramToken();
            var chatIds = BuildNotificationSettings.GetTelegramChatIds();

            if (string.IsNullOrWhiteSpace(token) || chatIds.Count == 0)
            {
                return false;
            }

            var message =
                $"🔔 <b>Build Tool Test</b>\n" +
                $"Project: <code>{Application.productName}</code>\n" +
                $"Machine: <code>{Environment.MachineName}</code>\n" +
                "Status: Telegram bot integration is operational! 🎉";
            var allOk = true;

            foreach (var chatId in chatIds)
            {
                var ok = await SendMessageAsync(token, chatId, message).ConfigureAwait(false);
                if (!ok)
                {
                    allOk = false;
                }
            }

            return allOk;
        }

        private static string FormatTelegramMessage(BuildNotificationContext ctx)
        {
            var sb = new StringBuilder();
            var emoji = ctx.GetStatusEmoji();
            var resultText = $"<b><u><code>{HtmlEncode(ctx.Result.ToString().ToUpperInvariant())}</code></u></b>";

            sb.AppendLine($"{emoji} <b>Build {resultText}</b>");
            sb.AppendLine($"📦 <b>Project:</b> <code>{HtmlEncode(ctx.ProjectName)}</code>");
            sb.AppendLine($"🔖 <b>Version:</b> <code>{HtmlEncode(ctx.VersionText)}</code>");
            sb.AppendLine($"🧩 <b>Commit:</b> <code>{HtmlEncode(ctx.CommitText)}</code>");
            sb.AppendLine($"🎯 <b>Target:</b> <code>{HtmlEncode(ctx.TargetPlatform)}</code>");
            sb.AppendLine($"💾 <b>Size:</b> <code>{HtmlEncode(ctx.FormattedSize)}</code>");
            sb.AppendLine($"⏱️ <b>Duration:</b> <code>{HtmlEncode(ctx.FormattedDuration)}</code>");
            sb.AppendLine(
                $"⚠️ <b>Warnings:</b> <code>{ctx.TotalWarnings}</code> | " +
                $"❗ <b>Errors:</b> <code>{ctx.TotalErrors}</code>");

            return sb.ToString().TrimEnd();
        }

        private static string FormatTelegramBatchMessage(BuildBatchNotificationContext ctx)
        {
            var sb = new StringBuilder();
            var emoji = ctx.GetStatusEmoji();
            sb.AppendLine($"{emoji} <b>{HtmlEncode(ctx.GetStatusTitle())}: {HtmlEncode(ctx.ProjectName)}</b>");
            sb.AppendLine(
                $"📊 <b>Summary:</b> {ctx.SucceededProfiles}/{ctx.TotalProfiles} succeeded in " +
                $"<code>{HtmlEncode(ctx.FormattedDuration)}</code>");
            sb.AppendLine();
            sb.AppendLine("<b>Profiles:</b>");

            if (ctx.Reports != null && ctx.Reports.Count > 0)
            {
                foreach (var (profile, report) in ctx.Reports)
                {
                    var name = BuildReportFormatter.GetSafeProfileName(profile, report);
                    var res = report != null ? report.summary.result.ToString() : "Unknown";
                    var size = report != null ? BuildNotificationContext.FormatBytes(report.summary.totalSize) : "0 B";
                    var dur = report != null ? BuildNotificationContext.FormatDuration(report.summary.totalTime) : "0s";
                    sb.AppendLine(
                        $"• <b>{HtmlEncode(name)}</b> — {HtmlEncode(res)} | " +
                        $"{HtmlEncode(size)} | {HtmlEncode(dur)}");
                }
            }
            else if (ctx.ExecutionResult?.Results != null && ctx.ExecutionResult.Results.Count > 0)
            {
                foreach (var res in ctx.ExecutionResult.Results)
                {
                    var size = BuildNotificationContext.FormatBytes(res.TotalSize);
                    var dur = BuildNotificationContext.FormatDuration(TimeSpan.FromSeconds(res.DurationSeconds));
                    sb.AppendLine(
                        $"• <b>{HtmlEncode(res.ProfileName)}</b> — {HtmlEncode(res.Result)} | " +
                        $"{HtmlEncode(size)} | {HtmlEncode(dur)}");
                }
            }
            else
            {
                sb.AppendLine("• No profile data available");
            }

            return sb.ToString().TrimEnd();
        }

        private static string HtmlEncode(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "N/A";
            }
            return System.Net.WebUtility.HtmlEncode(text);
        }

        private static readonly HttpClient s_TelegramClient = new()
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        private async Task<bool> SendMessageAsync(string token, string chatId, string message)
        {
            try
            {
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(15));
                var url = $"https://api.telegram.org/bot{token}/sendMessage";
                var parameters = new Dictionary<string, string>
                {
                    ["chat_id"] = chatId,
                    ["text"] = message,
                    ["parse_mode"] = "HTML"
                };

                using var formContent = new FormUrlEncodedContent(parameters);
                using var response = await s_TelegramClient.PostAsync(url, formContent, cts.Token)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var safeResult = System.Text.RegularExpressions.Regex.Replace(
                        result, @"bot\d+:[a-zA-Z0-9_-]+", "bot[REDACTED]");
                    if (!string.IsNullOrEmpty(token))
                    {
                        safeResult = safeResult.Replace(token, "[REDACTED]");
                    }
                    Debug.LogWarning(
                        $"[TelegramNotification] SendMessage to {chatId} failed ({response.StatusCode}): {safeResult}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                var safeError = System.Text.RegularExpressions.Regex.Replace(
                    ex.Message, @"bot\d+:[a-zA-Z0-9_-]+", "bot[REDACTED]");
                if (!string.IsNullOrEmpty(token))
                {
                    safeError = safeError.Replace(token, "[REDACTED]");
                }
                Debug.LogError($"[TelegramNotification] SendMessage error: {safeError}");
                return false;
            }
        }
    }
}

