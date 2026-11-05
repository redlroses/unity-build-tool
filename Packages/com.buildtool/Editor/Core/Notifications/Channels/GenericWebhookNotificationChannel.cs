using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace BuildTool.Editor.Notifications.Channels
{
    [NotificationChannel(30, "Generic Webhook")]
    public class GenericWebhookNotificationChannel : NotificationChannelBase
    {
        public override string Id => "webhook";
        public override string DisplayName => "Generic Webhook";
        public override string Description =>
            "Posts structured JSON payloads on build completion to custom REST APIs, Zapier, Make, or n8n.";
        public override int Order => 30;
        public override string DotColor => "#10B981";

        public override bool IsEnabled
        {
            get => BuildNotificationSettings.GenericWebhookEnabled;
            set => BuildNotificationSettings.GenericWebhookEnabled = value;
        }

        public override bool IsConfigured =>
            !string.IsNullOrWhiteSpace(BuildNotificationSettings.GetGenericWebhookUrl());

        protected override VisualElement CreateBodyGUI()
        {
            var body = new VisualElement();

            body.Add(CreateMaskedField(
                "Endpoint URL",
                BuildNotificationSettings.GetGenericWebhookUrl,
                BuildNotificationSettings.SaveGenericWebhookUrl,
                BuildNotificationSettings.ClearGenericWebhook,
                "GENERIC_WEBHOOK_URL",
                "Target HTTP/HTTPS webhook URL endpoint.",
                null,
                isUrl: true
            ));

            body.Add(CreateMaskedField(
                "Auth Header",
                BuildNotificationSettings.GetGenericWebhookAuthHeader,
                BuildNotificationSettings.SaveGenericWebhookAuthHeader,
                () => BuildNotificationSettings.SaveGenericWebhookAuthHeader(null),
                "GENERIC_WEBHOOK_AUTH",
                "e.g. 'Bearer your-secret-token' or custom authorization key.",
                null,
                isUrl: false
            ));

            body.Add(CreateTestButton(
                SendTestNotificationAsync,
                () => !string.IsNullOrWhiteSpace(BuildNotificationSettings.GetGenericWebhookUrl())));
            return body;
        }

        public override async Task<bool> SendNotificationAsync(BuildNotificationContext context)
        {
            var url = BuildNotificationSettings.GetGenericWebhookUrl();
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"event\":\"build_finished\",");
            sb.Append($"\"project\":\"{EscapeJson(context.ProjectName)}\",");
            sb.Append($"\"status\":\"{EscapeJson(context.Result.ToString())}\",");
            sb.Append($"\"isSuccess\":{(context.IsSuccess ? "true" : "false")},");
            sb.Append($"\"platform\":\"{EscapeJson(context.TargetPlatform)}\",");
            sb.Append($"\"version\":\"{EscapeJson(context.VersionText)}\",");
            sb.Append($"\"commit\":\"{EscapeJson(context.CommitText)}\",");
            var durationSec =
                context.Duration.TotalSeconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            sb.Append($"\"durationSeconds\":{durationSec},");
            sb.Append($"\"durationFormatted\":\"{EscapeJson(context.FormattedDuration)}\",");
            sb.Append($"\"totalSizeBytes\":{context.TotalSizeBytes},");
            sb.Append($"\"totalSizeFormatted\":\"{EscapeJson(context.FormattedSize)}\",");
            sb.Append($"\"errors\":{context.TotalErrors},");
            sb.Append($"\"warnings\":{context.TotalWarnings},");
            sb.Append($"\"outputPath\":\"{EscapeJson(context.OutputPath)}\",");
            sb.Append($"\"timestamp\":\"{context.Timestamp:o}\"");
            sb.Append("}");

            return await PostJsonAsync(url, sb.ToString(), BuildNotificationSettings.GetGenericWebhookAuthHeader());
        }

        public override async Task<bool> SendBatchNotificationAsync(BuildBatchNotificationContext context)
        {
            var url = BuildNotificationSettings.GetGenericWebhookUrl();
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"event\":\"batch_completed\",");
            sb.Append($"\"project\":\"{EscapeJson(context.ProjectName)}\",");
            sb.Append($"\"success\":{(context.IsSuccess ? "true" : "false")},");
            sb.Append($"\"totalProfiles\":{context.TotalProfiles},");
            sb.Append($"\"succeededProfiles\":{context.SucceededProfiles},");
            sb.Append($"\"failedProfiles\":{context.FailedProfiles},");
            sb.Append($"\"cancelledProfiles\":{context.CancelledProfiles},");
            var durationSec = context.Duration.TotalSeconds.ToString(
                "F2", System.Globalization.CultureInfo.InvariantCulture);
            sb.Append($"\"durationSeconds\":{durationSec},");
            sb.Append($"\"durationFormatted\":\"{EscapeJson(context.FormattedDuration)}\",");
            sb.Append($"\"timestamp\":\"{context.Timestamp:o}\",");
            sb.Append("\"profiles\":[");

            var first = true;
            if (context.Reports != null && context.Reports.Count > 0)
            {
                foreach (var (profile, report) in context.Reports)
                {
                    if (!first)
                    {
                        sb.Append(",");
                    }
                    first = false;
                    var name = BuildReportFormatter.GetSafeProfileName(profile, report);
                    var res = report != null ? report.summary.result.ToString() : "Unknown";
                    var size = report != null ? report.summary.totalSize : 0;
                    var dur = report != null ? report.summary.totalTime.TotalSeconds : 0.0;
                    var errors = report != null ? report.summary.totalErrors : 0;
                    var warnings = report != null ? report.summary.totalWarnings : 0;
                    var outPath = report != null ? report.summary.outputPath : string.Empty;
                    var durSec = dur.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

                    sb.Append("{");
                    sb.Append($"\"name\":\"{EscapeJson(name)}\",");
                    sb.Append($"\"result\":\"{EscapeJson(res)}\",");
                    sb.Append($"\"sizeBytes\":{size},");
                    sb.Append($"\"durationSeconds\":{durSec},");
                    sb.Append($"\"errors\":{errors},");
                    sb.Append($"\"warnings\":{warnings},");
                    sb.Append($"\"outputPath\":\"{EscapeJson(outPath)}\"");
                    sb.Append("}");
                }
            }
            else if (context.ExecutionResult?.Results != null && context.ExecutionResult.Results.Count > 0)
            {
                foreach (var res in context.ExecutionResult.Results)
                {
                    if (!first)
                    {
                        sb.Append(",");
                    }
                    first = false;
                    var resDur = res.DurationSeconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                    sb.Append("{");
                    sb.Append($"\"name\":\"{EscapeJson(res.ProfileName)}\",");
                    sb.Append($"\"result\":\"{EscapeJson(res.Result)}\",");
                    sb.Append($"\"sizeBytes\":{res.TotalSize},");
                    sb.Append($"\"durationSeconds\":{resDur},");
                    sb.Append($"\"errors\":{res.TotalErrors},");
                    sb.Append($"\"warnings\":{res.TotalWarnings},");
                    sb.Append($"\"outputPath\":\"{EscapeJson(res.OutputPath ?? string.Empty)}\"");
                    sb.Append("}");
                }
            }

            sb.Append("]");
            sb.Append("}");

            return await PostJsonAsync(url, sb.ToString(), BuildNotificationSettings.GetGenericWebhookAuthHeader());
        }

        public override async Task<bool> SendTestNotificationAsync()
        {
            var url = BuildNotificationSettings.GetGenericWebhookUrl();
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"event\":\"test_notification\",");
            sb.Append($"\"project\":\"{EscapeJson(Application.productName)}\",");
            sb.Append($"\"machine\":\"{EscapeJson(Environment.MachineName)}\",");
            sb.Append("\"message\":\"Generic Webhook is operational!\",");
            sb.Append($"\"timestamp\":\"{DateTime.UtcNow:o}\"");
            sb.Append("}");

            return await PostJsonAsync(url, sb.ToString(), BuildNotificationSettings.GetGenericWebhookAuthHeader());
        }
    }
}

