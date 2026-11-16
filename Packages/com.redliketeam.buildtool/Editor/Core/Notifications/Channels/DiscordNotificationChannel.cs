using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace BuildTool.Editor.Notifications.Channels
{
    [NotificationChannel(10, "Discord")]
    public class DiscordNotificationChannel : NotificationChannelBase
    {
        private readonly struct DiscordField
        {
            public readonly string Name;
            public readonly string Value;
            public readonly bool Inline;

            public DiscordField(string name, string value, bool inline = true)
            {
                Name = name;
                Value = value;
                Inline = inline;
            }
        }

        public override string Id => "discord";
        public override string DisplayName => "Discord";
        public override string Description => "Sends rich embed messages to Discord channels via standard Webhooks.";
        public override int Order => 10;
        public override string DotColor => "#5865F2";

        public override bool IsEnabled
        {
            get => BuildNotificationSettings.DiscordEnabled;
            set => BuildNotificationSettings.DiscordEnabled = value;
        }

        public override bool IsConfigured =>
            !string.IsNullOrWhiteSpace(BuildNotificationSettings.GetDiscordWebhookUrl());

        protected override VisualElement CreateBodyGUI()
        {
            return CreateMaskedField(
                "Webhook URL",
                BuildNotificationSettings.GetDiscordWebhookUrl,
                BuildNotificationSettings.SaveDiscordWebhookUrl,
                BuildNotificationSettings.ClearDiscordWebhook,
                "DISCORD_WEBHOOK_URL",
                "Paste your Discord channel Webhook URL (Integrations > Webhooks).",
                SendTestNotificationAsync,
                isUrl: true
            );
        }

        public override async Task<bool> SendNotificationAsync(BuildNotificationContext context)
        {
            var url = BuildNotificationSettings.GetDiscordWebhookUrl();
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            var color = context.IsSuccess
                ? 3066993
                : (context.Result == UnityEditor.Build.Reporting.BuildResult.Cancelled ? 15105570 : 15158332);
            var title = $"{context.GetStatusEmoji()} {context.GetStatusTitle()}: {context.ProjectName}";

            var fields = new[]
            {
                new DiscordField("Platform", context.TargetPlatform, inline: true),
                new DiscordField("Version", context.VersionText, inline: true),
                new DiscordField("Commit", context.CommitText, inline: true),
                new DiscordField("Duration", context.FormattedDuration, inline: true),
                new DiscordField("Size", context.FormattedSize, inline: true),
                new DiscordField("Issues",
                    $"{context.TotalErrors} error(s), {context.TotalWarnings} warning(s)", inline: true)
            };

            var json = BuildDiscordPayload(title, color, fields, context.Timestamp);
            return await PostJsonAsync(url, json);
        }

        public override async Task<bool> SendBatchNotificationAsync(BuildBatchNotificationContext context)
        {
            var url = BuildNotificationSettings.GetDiscordWebhookUrl();
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            var color = context.IsSuccess
                ? 3066993
                : (context.CancelledProfiles > 0 && context.FailedProfiles == 0 ? 15105570 : 15158332);
            var title = $"{context.GetStatusEmoji()} {context.GetStatusTitle()}: {context.ProjectName}";

            var fields = new List<DiscordField>
            {
                new DiscordField(
                    "Summary", $"{context.SucceededProfiles}/{context.TotalProfiles} succeeded", inline: true),
                new DiscordField("Total Duration", context.FormattedDuration, inline: true)
            };

            if (context.Reports != null && context.Reports.Count > 0)
            {
                foreach (var (profile, report) in context.Reports)
                {
                    var name = BuildReportFormatter.GetSafeProfileName(profile, report);
                    var res = report != null ? report.summary.result.ToString() : "Unknown";
                    var size = report != null ? BuildNotificationContext.FormatBytes(report.summary.totalSize) : "0 B";
                    var dur = report != null ? BuildNotificationContext.FormatDuration(report.summary.totalTime) : "0s";
                    fields.Add(new DiscordField(name, $"{res} | {size} | {dur}", inline: false));
                }
            }
            else if (context.ExecutionResult?.Results != null && context.ExecutionResult.Results.Count > 0)
            {
                foreach (var res in context.ExecutionResult.Results)
                {
                    var size = BuildNotificationContext.FormatBytes(res.TotalSize);
                    var dur = BuildNotificationContext.FormatDuration(TimeSpan.FromSeconds(res.DurationSeconds));
                    fields.Add(new DiscordField(res.ProfileName, $"{res.Result} | {size} | {dur}", inline: false));
                }
            }

            const int maxFields = 24;
            if (fields.Count > maxFields)
            {
                var overflow = fields.Count - maxFields + 1;
                fields = fields.Take(maxFields - 1).ToList();
                fields.Add(new DiscordField(
                    "Remaining Profiles", $"+{overflow} more profiles omitted", inline: false));
            }

            var json = BuildDiscordPayload(title, color, fields, context.Timestamp);
            return await PostJsonAsync(url, json);
        }

        public override async Task<bool> SendTestNotificationAsync()
        {
            var url = BuildNotificationSettings.GetDiscordWebhookUrl();
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            var title = "🔔 Build Tool — Test Notification";
            var fields = new[]
            {
                new DiscordField("Project", Application.productName, inline: true),
                new DiscordField("Machine", Environment.MachineName, inline: true),
                new DiscordField("Status", "Discord webhook is operational! 🎉", inline: false)
            };

            var json = BuildDiscordPayload(title, 3447003, fields, DateTime.UtcNow);
            return await PostJsonAsync(url, json);
        }

        private static string BuildDiscordPayload(
            string title, int color, IEnumerable<DiscordField> fields, DateTime timestamp)
        {
            var sb = new StringBuilder();
            sb.Append("{\"embeds\":[{");
            sb.Append($"\"title\":\"{EscapeJson(title)}\",");
            sb.Append($"\"color\":{color},");
            sb.Append($"\"timestamp\":\"{timestamp:o}\",");
            sb.Append("\"footer\":{\"text\":\"Build Tool\"},");
            sb.Append("\"fields\":[");

            var first = true;
            foreach (var f in fields)
            {
                if (!first)
                {
                    sb.Append(",");
                }
                first = false;
                var safeName = string.IsNullOrWhiteSpace(f.Name) ? "Info" : f.Name;
                var safeValue = string.IsNullOrWhiteSpace(f.Value) ? "N/A" : f.Value;
                sb.Append(
                    $"{{\"name\":\"{EscapeJson(safeName)}\",\"value\":\"{EscapeJson(safeValue)}\"," +
                    $"\"inline\":{(f.Inline ? "true" : "false")}}}");
            }

            sb.Append("]}]}");
            return sb.ToString();
        }
    }
}

