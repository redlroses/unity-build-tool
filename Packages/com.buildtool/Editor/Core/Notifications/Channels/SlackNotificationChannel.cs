using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace BuildTool.Editor.Notifications.Channels
{
    [NotificationChannel(20, "Slack")]
    public class SlackNotificationChannel : NotificationChannelBase
    {
        public override string Id => "slack";
        public override string DisplayName => "Slack";
        public override string Description => "Sends formatted messages to Slack channels via Incoming Webhooks.";
        public override int Order => 20;
        public override string DotColor => "#E01E5A";

        public override bool IsEnabled
        {
            get => BuildNotificationSettings.SlackEnabled;
            set => BuildNotificationSettings.SlackEnabled = value;
        }

        public override bool IsConfigured =>
            !string.IsNullOrWhiteSpace(BuildNotificationSettings.GetSlackWebhookUrl());

        protected override VisualElement CreateBodyGUI()
        {
            return CreateMaskedField(
                "Webhook URL",
                BuildNotificationSettings.GetSlackWebhookUrl,
                BuildNotificationSettings.SaveSlackWebhookUrl,
                BuildNotificationSettings.ClearSlackWebhook,
                "SLACK_WEBHOOK_URL",
                "Paste your Slack Incoming Webhook URL.",
                SendTestNotificationAsync,
                isUrl: true
            );
        }

        public override async Task<bool> SendNotificationAsync(BuildNotificationContext context)
        {
            var url = BuildNotificationSettings.GetSlackWebhookUrl();
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            var header = $"{context.GetStatusEmoji()} {context.GetStatusTitle()}: {context.ProjectName}";
            if (header.Length > 140)
            {
                header = header.Substring(0, 137) + "...";
            }
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"text\":\"{EscapeJson(header)}\",");
            sb.Append("\"blocks\":[");
            sb.Append(
                $"{{\"type\":\"header\",\"text\":{{\"type\":\"plain_text\"," +
                $"\"text\":\"{EscapeJson(header)}\",\"emoji\":true}}}},");
            sb.Append("{\"type\":\"section\",\"fields\":[");
            sb.Append($"{{\"type\":\"mrkdwn\",\"text\":\"*Platform:*\\n{EscapeJson(context.TargetPlatform)}\"}},");
            sb.Append($"{{\"type\":\"mrkdwn\",\"text\":\"*Duration:*\\n{EscapeJson(context.FormattedDuration)}\"}},");
            sb.Append($"{{\"type\":\"mrkdwn\",\"text\":\"*Size:*\\n{EscapeJson(context.FormattedSize)}\"}},");
            sb.Append($"{{\"type\":\"mrkdwn\",\"text\":\"*Version:*\\n{EscapeJson(context.VersionText)}\"}},");
            sb.Append($"{{\"type\":\"mrkdwn\",\"text\":\"*Commit:*\\n{EscapeJson(context.CommitText)}\"}},");
            sb.Append(
                $"{{\"type\":\"mrkdwn\",\"text\":\"*Issues:*\\n{context.TotalErrors} err, " +
                $"{context.TotalWarnings} warn\"}}");
            sb.Append("]}");
            sb.Append("]}");

            return await PostJsonAsync(url, sb.ToString());
        }

        public override async Task<bool> SendBatchNotificationAsync(BuildBatchNotificationContext context)
        {
            var url = BuildNotificationSettings.GetSlackWebhookUrl();
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            var header = $"{context.GetStatusEmoji()} {context.GetStatusTitle()}: {context.ProjectName}";
            if (header.Length > 140)
            {
                header = header.Substring(0, 137) + "...";
            }

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"text\":\"{EscapeJson(header)}\",");
            sb.Append("\"blocks\":[");
            sb.Append(
                $"{{\"type\":\"header\",\"text\":{{\"type\":\"plain_text\"," +
                $"\"text\":\"{EscapeJson(header)}\",\"emoji\":true}}}},");
            sb.Append("{\"type\":\"section\",\"fields\":[");
            sb.Append(
                $"{{\"type\":\"mrkdwn\",\"text\":\"*Summary:*\\n" +
                $"{context.SucceededProfiles}/{context.TotalProfiles} succeeded\"}},");
            sb.Append($"{{\"type\":\"mrkdwn\",\"text\":\"*Duration:*\\n{EscapeJson(context.FormattedDuration)}\"}}");
            sb.Append("]}");

            var profileLines = new StringBuilder();
            if (context.Reports != null && context.Reports.Count > 0)
            {
                foreach (var (profile, report) in context.Reports)
                {
                    var name = BuildReportFormatter.GetSafeProfileName(profile, report);
                    var res = report != null ? report.summary.result.ToString() : "Unknown";
                    var size = report != null ? BuildNotificationContext.FormatBytes(report.summary.totalSize) : "0 B";
                    var dur = report != null ? BuildNotificationContext.FormatDuration(report.summary.totalTime) : "0s";
                    var emoji = res == "Succeeded" ? "🟢" : (res == "Cancelled" ? "🟡" : "🔴");
                    profileLines.AppendLine($"{emoji} *{name}*: {res} ({size}, {dur})");
                }
            }
            else if (context.ExecutionResult?.Results != null && context.ExecutionResult.Results.Count > 0)
            {
                foreach (var res in context.ExecutionResult.Results)
                {
                    var emoji = res.Result == "Succeeded" ? "🟢" : (res.Result == "Cancelled" ? "🟡" : "🔴");
                    var size = BuildNotificationContext.FormatBytes(res.TotalSize);
                    var dur = BuildNotificationContext.FormatDuration(TimeSpan.FromSeconds(res.DurationSeconds));
                    profileLines.AppendLine($"{emoji} *{res.ProfileName}*: {res.Result} ({size}, {dur})");
                }
            }

            if (profileLines.Length > 0)
            {
                var text = profileLines.ToString().TrimEnd();
                if (text.Length > 2900)
                {
                    text = text.Substring(0, 2890) + "\n... (omitted)";
                }
                sb.Append(",{\"type\":\"section\",\"text\":{\"type\":\"mrkdwn\",\"text\":\"*Profiles:*\\n" +
                    EscapeJson(text) + "\"}}");
            }

            sb.Append("]}");

            return await PostJsonAsync(url, sb.ToString());
        }

        public override async Task<bool> SendTestNotificationAsync()
        {
            var url = BuildNotificationSettings.GetSlackWebhookUrl();
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            var header = "🔔 Build Tool — Test Notification";
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"text\":\"{EscapeJson(header)}\",");
            sb.Append("\"blocks\":[");
            sb.Append(
                $"{{\"type\":\"header\",\"text\":{{\"type\":\"plain_text\"," +
                $"\"text\":\"{EscapeJson(header)}\",\"emoji\":true}}}},");
            sb.Append("{\"type\":\"section\",\"fields\":[");
            sb.Append($"{{\"type\":\"mrkdwn\",\"text\":\"*Project:*\\n{EscapeJson(Application.productName)}\"}},");
            sb.Append($"{{\"type\":\"mrkdwn\",\"text\":\"*Machine:*\\n{EscapeJson(Environment.MachineName)}\"}},");
            sb.Append("{\"type\":\"mrkdwn\",\"text\":\"*Status:*\\nSlack webhook is operational! 🎉\"}");
            sb.Append("]}");
            sb.Append("]}");

            return await PostJsonAsync(url, sb.ToString());
        }
    }
}
