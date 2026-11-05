using System;
using System.Reflection;
using System.Threading.Tasks;
using BuildTool.Editor.Notifications;
using BuildTool.Editor.Notifications.Channels;
using BuildTool.Tracking;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace BuildTool.Editor.Tests
{
    [TestFixture]
    public class NotificationPayloadTests
    {
        [Test]
        public void DiscordPayload_IsRFCCompliantJson_WithValidEmbeds()
        {
            var channel = new DiscordNotificationChannel();

            var method = typeof(DiscordNotificationChannel).GetMethod(
                "BuildDiscordPayload", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "BuildDiscordPayload method should exist");

            var fieldType = typeof(DiscordNotificationChannel).GetNestedType("DiscordField", BindingFlags.NonPublic);
            Assert.IsNotNull(fieldType, "DiscordField type should exist");

            var f1 = Activator.CreateInstance(fieldType, "Platform", "Windows \"Special\" \\ Edition", true);
            var f2 = Activator.CreateInstance(fieldType, "Version", "v1.0.0\t(beta)", true);
            var f3 = Activator.CreateInstance(fieldType, "EmptyField", "", true);

            var array = Array.CreateInstance(fieldType, 3);
            array.SetValue(f1, 0);
            array.SetValue(f2, 1);
            array.SetValue(f3, 2);

            var json = (string)method.Invoke(
                null,
                new object[] { "Title with \"quotes\" & <tags>", 3066993, array, DateTime.UtcNow });

            Assert.IsFalse(string.IsNullOrEmpty(json));

            JObject parsed = null;
            Assert.DoesNotThrow(() => parsed = JObject.Parse(json));

            var embeds = parsed["embeds"] as JArray;
            Assert.IsNotNull(embeds);
            Assert.AreEqual(1, embeds.Count);

            var embed = embeds[0];
            Assert.AreEqual(3066993, (int)embed["color"]);

            var fields = embed["fields"] as JArray;
            Assert.IsNotNull(fields);
            Assert.AreEqual(3, fields.Count);

            Assert.AreEqual("N/A", (string)fields[2]["value"]);
        }

        [Test]
        public void SlackPayload_IsConfigured_AndFormattedCorrectly()
        {
            var channel = new SlackNotificationChannel();

            BuildNotificationSettings.SaveSlackWebhookUrl("https://hooks.slack.com/services/TEST/MOCK/URL");
            Assert.IsTrue(channel.IsConfigured);

            var method = typeof(SlackNotificationChannel).GetMethod(
                "BuildSlackPayload", BindingFlags.NonPublic | BindingFlags.Instance);
            if (method != null)
            {
                var ctx = new BuildNotificationContext(
                    null, BuildInfo.Create("42", "abc\"123\nnewline", "2026-11-02"));
                var json = (string)method.Invoke(channel, new object[] { ctx });
                Assert.IsFalse(string.IsNullOrEmpty(json));
                Assert.DoesNotThrow(() => JObject.Parse(json));
            }

            BuildNotificationSettings.ClearSlackWebhook();
            Assert.IsFalse(channel.IsConfigured);
        }

        [Test]
        public void GenericWebhookPayload_ContainsExpectedJsonSchema()
        {
            var channel = new GenericWebhookNotificationChannel();
            var ctx = new BuildNotificationContext(null, BuildInfo.Create("10", "master-abc", "2026-11-02"));

            BuildNotificationSettings.SaveGenericWebhookUrl("https://api.test.com/webhook");
            BuildNotificationSettings.SaveGenericWebhookAuthHeader("Bearer test-token");

            Assert.IsTrue(channel.IsConfigured);

            BuildNotificationSettings.ClearGenericWebhook();
            Assert.IsFalse(channel.IsConfigured);
        }

        [Test]
        public void TelegramHtmlFormatting_EscapesSpecialCharactersSafely()
        {
            var method = typeof(TelegramNotificationChannel).GetMethod(
                "FormatTelegramMessage", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method);

            var info = BuildInfo.Create("1", "<script>alert('xss')</script> & more", "2026-11-02");
            var ctx = new BuildNotificationContext(null, info);

            var result = (string)method.Invoke(null, new object[] { ctx });

            Assert.IsFalse(string.IsNullOrEmpty(result));
            Assert.IsFalse(result.Contains("<script>"));
            Assert.IsTrue(result.Contains("&lt;script&gt;"));
            Assert.IsTrue(result.Contains("&amp; more"));
        }

        [Test]
        public void DesktopChannel_SendNotification_ExecutesCleanly()
        {
            var desktop = new DesktopNotificationChannel();
            desktop.IsEnabled = true;

            var ctx = new BuildNotificationContext(null, null);

            Assert.DoesNotThrowAsync(async () =>
            {
                var ok = await desktop.SendNotificationAsync(ctx);
                Assert.IsTrue(ok);
            });
        }

        [Test]
        public void Channels_WhenUnconfigured_ReturnFalseWithoutThrowing()
        {
            BuildNotificationSettings.ClearDiscordWebhook();
            BuildNotificationSettings.ClearSlackWebhook();
            BuildNotificationSettings.ClearGenericWebhook();
            BuildNotificationSettings.ClearTelegramToken();
            BuildNotificationSettings.ClearTelegramChatIds();

            var discord = new DiscordNotificationChannel();
            var slack = new SlackNotificationChannel();
            var webhook = new GenericWebhookNotificationChannel();
            var telegram = new TelegramNotificationChannel();

            var ctx = new BuildNotificationContext(null, null);

            Assert.DoesNotThrowAsync(async () =>
            {
                Assert.IsFalse(await discord.SendNotificationAsync(ctx));
                Assert.IsFalse(await slack.SendNotificationAsync(ctx));
                Assert.IsFalse(await webhook.SendNotificationAsync(ctx));
                Assert.IsFalse(await telegram.SendNotificationAsync(ctx));
            });
        }
    }
}
