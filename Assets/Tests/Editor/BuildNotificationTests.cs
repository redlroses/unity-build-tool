using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BuildTool.Editor.Notifications;
using BuildTool.Editor.Notifications.Channels;
using BuildTool.Tracking;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace BuildTool.Editor.Tests
{
    [NotificationChannel(order: 999, title: "Test Custom Channel")]
    public class CustomTestNotificationChannel : NotificationChannelBase
    {
        public override string Id => "custom_test_channel";
        public override string DisplayName => "Test Custom Channel";
        public override string Description => "A mock custom channel for unit testing.";
        public override int Order => 999;
        public override string DotColor => "#F59E0B";
        public override bool IsConfigured => true;

        public bool NotificationSent { get; private set; }
        public bool BatchNotificationSent { get; private set; }

        public override Task<bool> SendNotificationAsync(BuildNotificationContext context)
        {
            NotificationSent = true;
            return Task.FromResult(true);
        }

        public override Task<bool> SendBatchNotificationAsync(BuildBatchNotificationContext context)
        {
            BatchNotificationSent = true;
            return Task.FromResult(true);
        }

        protected override VisualElement CreateBodyGUI()
        {
            var body = new VisualElement();
            body.Add(new Label("Custom Mock Controls"));
            return body;
        }
    }

    [TestFixture]
    public class BuildNotificationTests
    {
        private bool _origMasterEnabled;
        private bool _origDiscordEnabled;
        private string _origDiscordUrl;
        private bool _origSlackEnabled;
        private string _origSlackUrl;
        private bool _origWebhookEnabled;
        private string _origWebhookUrl;
        private bool _origDesktopEnabled;
        private bool _origTelegramEnabled;

        [SetUp]
        public void SetUp()
        {
            _origMasterEnabled = BuildNotificationSettings.MasterEnabled;
            _origDiscordEnabled = BuildNotificationSettings.DiscordEnabled;
            _origDiscordUrl = BuildNotificationSettings.GetDiscordWebhookUrl();
            _origSlackEnabled = BuildNotificationSettings.SlackEnabled;
            _origSlackUrl = BuildNotificationSettings.GetSlackWebhookUrl();
            _origWebhookEnabled = BuildNotificationSettings.GenericWebhookEnabled;
            _origWebhookUrl = BuildNotificationSettings.GetGenericWebhookUrl();
            _origDesktopEnabled = BuildNotificationSettings.DesktopEnabled;
            _origTelegramEnabled = BuildNotificationSettings.TelegramEnabled;
        }

        [TearDown]
        public void TearDown()
        {
            BuildNotificationSettings.MasterEnabled = _origMasterEnabled;
            BuildNotificationSettings.DiscordEnabled = _origDiscordEnabled;
            BuildNotificationSettings.SaveDiscordWebhookUrl(_origDiscordUrl);

            BuildNotificationSettings.SlackEnabled = _origSlackEnabled;
            BuildNotificationSettings.SaveSlackWebhookUrl(_origSlackUrl);

            BuildNotificationSettings.GenericWebhookEnabled = _origWebhookEnabled;
            BuildNotificationSettings.SaveGenericWebhookUrl(_origWebhookUrl);

            BuildNotificationSettings.DesktopEnabled = _origDesktopEnabled;
            BuildNotificationSettings.TelegramEnabled = _origTelegramEnabled;
        }

        [Test]
        public void Context_WithNullReport_ProvidesSafeFallbacks()
        {
            var ctx = new BuildNotificationContext(null, null);

            Assert.IsNotNull(ctx);
            Assert.IsFalse(ctx.IsSuccess);
            Assert.AreEqual("0 B", ctx.FormattedSize);
            Assert.AreEqual("0.0s", ctx.FormattedDuration);
            Assert.AreEqual("N/A", ctx.CommitText);
            Assert.IsTrue(ctx.VersionText.StartsWith("v"));
            Assert.AreEqual("\u26AA", ctx.GetStatusEmoji());
            Assert.AreEqual("Build Finished", ctx.GetStatusTitle());
        }

        [Test]
        public void Context_WithBuildInfo_FormatsVersionAndCommit()
        {
            var info = BuildInfo.Create("99", "deadbeef", "2026-11-02");

            var ctx = new BuildNotificationContext(null, info);

            Assert.IsTrue(ctx.VersionText.EndsWith(".99"));
            Assert.AreEqual("deadbeef", ctx.CommitText);
        }

        [Test]
        public void DiscordSettings_SaveAndRetrieve_WorksCorrectly()
        {
            const string testUrl = "https://discord.com/api/webhooks/12345/abcdef";
            BuildNotificationSettings.SaveDiscordWebhookUrl(testUrl);

            Assert.AreEqual(testUrl, BuildNotificationSettings.GetDiscordWebhookUrl());
            Assert.IsTrue(BuildNotificationSettings.HasExplicitDiscordWebhook);

            BuildNotificationSettings.ClearDiscordWebhook();
            Assert.IsFalse(BuildNotificationSettings.HasExplicitDiscordWebhook);
        }

        [Test]
        public void SlackSettings_SaveAndRetrieve_WorksCorrectly()
        {
            const string testUrl = "https://hooks.slack.com/services/T00/B00/XXXX";
            BuildNotificationSettings.SaveSlackWebhookUrl(testUrl);

            Assert.AreEqual(testUrl, BuildNotificationSettings.GetSlackWebhookUrl());
            Assert.IsTrue(BuildNotificationSettings.HasExplicitSlackWebhook);

            BuildNotificationSettings.ClearSlackWebhook();
            Assert.IsFalse(BuildNotificationSettings.HasExplicitSlackWebhook);
        }

        [Test]
        public void GenericWebhookSettings_SaveAndRetrieve_WorksCorrectly()
        {
            const string testUrl = "https://api.mycompany.com/build-webhook";
            const string authHeader = "Bearer secret123";

            BuildNotificationSettings.SaveGenericWebhookUrl(testUrl);
            BuildNotificationSettings.SaveGenericWebhookAuthHeader(authHeader);

            Assert.AreEqual(testUrl, BuildNotificationSettings.GetGenericWebhookUrl());
            Assert.AreEqual(authHeader, BuildNotificationSettings.GetGenericWebhookAuthHeader());

            BuildNotificationSettings.ClearGenericWebhook();
            Assert.IsNull(BuildNotificationSettings.GetGenericWebhookAuthHeader());
        }

        [Test]
        public void Manager_DefaultChannels_AreRegistered()
        {
            BuildNotificationManager.DiscoverChannels(forceReload: true);
            var channels = BuildNotificationManager.Channels;

            Assert.IsNotNull(channels);
            Assert.IsTrue(channels.Count >= 5);

            Assert.IsNotNull(BuildNotificationManager.GetChannel("discord"));
            Assert.IsNotNull(BuildNotificationManager.GetChannel("slack"));
            Assert.IsNotNull(BuildNotificationManager.GetChannel("webhook"));
            Assert.IsNotNull(BuildNotificationManager.GetChannel("desktop"));
            Assert.IsNotNull(BuildNotificationManager.GetChannel("telegram"));
        }

        [Test]
        public void CustomChannel_AutomaticallyDiscovered_AndCreatesGUI()
        {
            BuildNotificationManager.DiscoverChannels(forceReload: true);
            var customChannel = BuildNotificationManager.GetChannel("custom_test_channel");

            Assert.IsNotNull(
                customChannel,
                "Custom channel implementing NotificationChannelBase should be auto-discovered!");
            Assert.AreEqual("Test Custom Channel", customChannel.DisplayName);
            Assert.AreEqual(999, customChannel.Order);

            var gui = customChannel.CreateSettingsGUI();
            Assert.IsNotNull(gui, "CreateSettingsGUI should return a valid card VisualElement");
        }

        [Test]
        public void DiscordChannel_ConfigurationState_ReflectsWebhookPresence()
        {
            var channel = new DiscordNotificationChannel();

            BuildNotificationSettings.ClearDiscordWebhook();
            Assert.IsFalse(channel.IsConfigured);

            BuildNotificationSettings.SaveDiscordWebhookUrl("https://discord.com/webhook/test");
            Assert.IsTrue(channel.IsConfigured);
        }

        [Test]
        public async Task DispatchBatchAsync_DispatchesToConfiguredChannels()
        {
            BuildNotificationSettings.MasterEnabled = true;
            BuildNotificationSettings.DiscordEnabled = false;
            BuildNotificationSettings.SlackEnabled = false;
            BuildNotificationSettings.GenericWebhookEnabled = false;
            BuildNotificationSettings.DesktopEnabled = false;
            BuildNotificationSettings.TelegramEnabled = false;

            BuildNotificationManager.DiscoverChannels(forceReload: true);
            var customChannel =
                BuildNotificationManager.GetChannel("custom_test_channel") as CustomTestNotificationChannel;
            Assert.IsNotNull(customChannel);

            var origStates = new Dictionary<INotificationChannel, bool>();
            foreach (var channel in BuildNotificationManager.Channels)
            {
                origStates[channel] = channel.IsEnabled;
                channel.IsEnabled = channel == customChannel;
            }

            try
            {
                var batchContext = new BuildBatchNotificationContext(null, null);
                await BuildNotificationManager.DispatchBatchAsync(batchContext);

                Assert.IsTrue(customChannel.BatchNotificationSent);
            }
            finally
            {
                foreach (var kvp in origStates)
                {
                    kvp.Key.IsEnabled = kvp.Value;
                }
            }
        }

        [Test]
        public void DesktopChannel_IsAlwaysConfigured()
        {
            var channel = new DesktopNotificationChannel();
            Assert.IsTrue(channel.IsConfigured);
        }

        [Test]
        public void DiscordChannel_WithEnvVarOnly_ShowsEnvBadgeInSettingsGUI()
        {
            const string envVar = "DISCORD_WEBHOOK_URL";
            var prevEnv = Environment.GetEnvironmentVariable(envVar);
            BuildNotificationSettings.ClearDiscordWebhook();

            try
            {
                Environment.SetEnvironmentVariable(envVar, "https://discord.com/api/webhooks/123/envtoken");
                var channel = new DiscordNotificationChannel();
                var gui = channel.CreateSettingsGUI();

                var envBadge = gui.Q<Label>(className: "notif-env-badge");
                Assert.IsNotNull(envBadge, "Expected ENV badge when secret comes from environment variable.");
                Assert.AreEqual("ENV", envBadge.text);
            }
            finally
            {
                Environment.SetEnvironmentVariable(envVar, prevEnv);
            }
        }

        [Test]
        public void TelegramChatIds_Add_DoesNotBakeEnvVarIntoEditorPrefs()
        {
            const string envVar = "TELEGRAM_CHAT_ID";
            var prevEnv = Environment.GetEnvironmentVariable(envVar);
            BuildNotificationSettings.ClearTelegramChatIds();

            try
            {
                Environment.SetEnvironmentVariable(envVar, "-100999888");
                BuildNotificationSettings.AddTelegramChatId("-100111222");

                var ids = BuildNotificationSettings.GetTelegramChatIds();
                Assert.IsTrue(ids.Contains("-100111222"));
                Assert.IsFalse(ids.Contains("-100999888"), "Adding an explicit ID must not persist the ENV ID.");
            }
            finally
            {
                BuildNotificationSettings.ClearTelegramChatIds();
                Environment.SetEnvironmentVariable(envVar, prevEnv);
            }
        }

        [Test]
        public void ParseProxyServer_ResolvesFormatsCorrectly()
        {
            Assert.AreEqual(
                new Uri("http://127.0.0.1:9697/"),
                NotificationChannelBase.ParseProxyServer("127.0.0.1:9697", "https"));
            Assert.AreEqual(
                new Uri("http://127.0.0.1:9697/"),
                NotificationChannelBase.ParseProxyServer("http://127.0.0.1:9697", "https"));
            Assert.AreEqual(
                new Uri("http://127.0.0.1:8443/"),
                NotificationChannelBase.ParseProxyServer("http=127.0.0.1:8080;https=127.0.0.1:8443", "https"));
            Assert.AreEqual(
                new Uri("http://127.0.0.1:8080/"),
                NotificationChannelBase.ParseProxyServer("http=127.0.0.1:8080;https=127.0.0.1:8443", "http"));
            Assert.AreEqual(
                new Uri("http://127.0.0.1:8080/"),
                NotificationChannelBase.ParseProxyServer("http=127.0.0.1:8080", "https"));
            Assert.IsNull(NotificationChannelBase.ParseProxyServer("socks=127.0.0.1:1080", "https"));
            Assert.IsNull(NotificationChannelBase.ParseProxyServer("socks5://127.0.0.1:1080", "https"));
            Assert.IsNull(NotificationChannelBase.ParseProxyServer(string.Empty, "https"));
            Assert.IsNull(NotificationChannelBase.ParseProxyServer(null, "https"));
        }

        [Test]
        public void ParseProxyBypass_ConvertsGlobsAndLocalFlag()
        {
            var patterns = NotificationChannelBase.ParseProxyBypass("localhost;127.*;<local>", out var bypassOnLocal);

            Assert.IsTrue(bypassOnLocal);
            Assert.AreEqual(2, patterns.Length);
            Assert.AreEqual("^localhost$", patterns[0]);
            Assert.AreEqual(@"^127\..*$", patterns[1]);

            foreach (var pattern in patterns)
            {
                Assert.DoesNotThrow(() => new Regex(pattern));
            }

            var emptyPatterns = NotificationChannelBase.ParseProxyBypass(string.Empty, out var emptyBypassOnLocal);
            Assert.IsFalse(emptyBypassOnLocal);
            Assert.AreEqual(0, emptyPatterns.Length);
        }

        [Test]
        public void IsTransientStatusCode_IdentifiesRetryableResponses()
        {
            Assert.IsTrue(NotificationChannelBase.IsTransientStatusCode((HttpStatusCode)429));
            Assert.IsTrue(NotificationChannelBase.IsTransientStatusCode(HttpStatusCode.RequestTimeout));
            Assert.IsTrue(NotificationChannelBase.IsTransientStatusCode(HttpStatusCode.InternalServerError));
            Assert.IsTrue(NotificationChannelBase.IsTransientStatusCode(HttpStatusCode.BadGateway));
            Assert.IsTrue(NotificationChannelBase.IsTransientStatusCode(HttpStatusCode.ServiceUnavailable));

            Assert.IsFalse(NotificationChannelBase.IsTransientStatusCode(HttpStatusCode.OK));
            Assert.IsFalse(NotificationChannelBase.IsTransientStatusCode(HttpStatusCode.BadRequest));
            Assert.IsFalse(NotificationChannelBase.IsTransientStatusCode(HttpStatusCode.Unauthorized));
            Assert.IsFalse(NotificationChannelBase.IsTransientStatusCode(HttpStatusCode.Forbidden));
            Assert.IsFalse(NotificationChannelBase.IsTransientStatusCode(HttpStatusCode.NotFound));
        }

        [Test]
        public void GetRetryDelayMs_AppliesBackoffAndClampsRetryAfter()
        {
            Assert.AreEqual(500, NotificationChannelBase.GetRetryDelayMs(0));
            Assert.AreEqual(1500, NotificationChannelBase.GetRetryDelayMs(1));
            Assert.AreEqual(2000, NotificationChannelBase.GetRetryDelayMs(2));
            Assert.AreEqual(4000, NotificationChannelBase.GetRetryDelayMs(3));
            Assert.AreEqual(5000, NotificationChannelBase.GetRetryDelayMs(4));

            Assert.AreEqual(250, NotificationChannelBase.GetRetryDelayMs(0, TimeSpan.FromMilliseconds(50)));
            Assert.AreEqual(1200, NotificationChannelBase.GetRetryDelayMs(0, TimeSpan.FromMilliseconds(1200)));
            Assert.AreEqual(5000, NotificationChannelBase.GetRetryDelayMs(0, TimeSpan.FromSeconds(30)));
        }
    }
}

