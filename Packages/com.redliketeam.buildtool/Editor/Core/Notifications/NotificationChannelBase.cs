using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BuildTool.Editor.Processors;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BuildTool.Editor.Notifications
{
    /// <summary>
    /// Base class for custom build notification channels.
    /// Provides auto-persistence (EditorPrefs), standard UI Toolkit card builders,
    /// masked token inputs, test connection buttons, and HTTP POST helpers.
    /// </summary>
    public abstract class NotificationChannelBase : INotificationChannel
    {
        private sealed class HttpClientCache
        {
            private readonly object _lock = new();
            private HttpClient _client;
            private string _proxyKey;

            public HttpClient GetOrCreate(string cacheKey, Uri proxyUri, string bypassRaw)
            {
                lock (_lock)
                {
                    if (_client != null &&
                        string.Equals(_proxyKey, cacheKey, StringComparison.Ordinal))
                    {
                        return _client;
                    }

                    var handler = new HttpClientHandler
                    {
                        UseProxy = true
                    };

                    if (proxyUri != null)
                    {
                        var bypassList = ParseProxyBypass(bypassRaw, out var bypassOnLocal);
                        handler.Proxy = new WebProxy(proxyUri, bypassOnLocal, bypassList);
                    }

                    var client = new HttpClient(handler)
                    {
                        Timeout = TimeSpan.FromSeconds(20)
                    };

                    _client = client;
                    _proxyKey = cacheKey;
                    return client;
                }
            }
        }

        private static readonly HttpClientCache s_HttpClientCache = new();

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinHttpCurrentUserIeProxyConfig
        {
            [MarshalAs(UnmanagedType.Bool)]
            public bool fAutoDetect;
            public IntPtr lpszAutoConfigUrl;
            public IntPtr lpszProxy;
            public IntPtr lpszProxyBypass;
        }

        [DllImport("winhttp.dll", SetLastError = true)]
        private static extern bool WinHttpGetIEProxyConfigForCurrentUser(
            ref WinHttpCurrentUserIeProxyConfig pProxyConfig);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalFree(IntPtr hMem);

        static NotificationChannelBase()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch
            {
            }
        }

        public abstract string Id { get; }
        public abstract string DisplayName { get; }
        public virtual string Description => null;
        public virtual int Order => 100;
        public virtual string DotColor => "#8B5CF6";

        public virtual bool IsEnabled
        {
            get => GetBool("enabled", false);
            set => SaveBool("enabled", value);
        }

        public abstract bool IsConfigured { get; }

        public abstract Task<bool> SendNotificationAsync(BuildNotificationContext context);

        public virtual Task<bool> SendBatchNotificationAsync(BuildBatchNotificationContext context) =>
            Task.FromResult(true);

        public virtual Task<bool> SendTestNotificationAsync() => Task.FromResult(true);

        /// <summary>
        /// Creates the root VisualElement card for this channel in Project Settings.
        /// Override CreateBodyGUI to define custom fields and buttons inside the card.
        /// </summary>
        public virtual VisualElement CreateSettingsGUI()
        {
            var card = new VisualElement();
            card.AddToClassList("notif-channel-card");

            var header = new VisualElement();
            header.AddToClassList("notif-channel-header");

            var toggle = new Toggle
            {
                value = IsEnabled
            };
            BuildNotificationSettingsProvider.ApplySwitchStyle(toggle);
            toggle.AddToClassList("notif-channel-switch");
            header.Add(toggle);

            var titleRow = new VisualElement();
            titleRow.AddToClassList("notif-channel-title-row");

            var title = new Label(DisplayName);
            title.AddToClassList("notif-channel-title");
            titleRow.Add(title);

            var dot = new VisualElement();
            dot.AddToClassList("notif-channel-dot");
            if (ColorUtility.TryParseHtmlString(DotColor, out var color))
            {
                dot.style.backgroundColor = color;
            }
            dot.style.opacity = IsEnabled ? 1f : 0.45f;
            titleRow.Add(dot);

            header.Add(titleRow);
            card.Add(header);

            if (!string.IsNullOrEmpty(Description))
            {
                var desc = new Label(Description);
                desc.AddToClassList("notif-channel-desc");
                card.Add(desc);
            }

            var body = new VisualElement();
            body.SetEnabled(IsEnabled);

            var customContent = CreateBodyGUI();
            if (customContent != null)
            {
                body.Add(customContent);
            }
            card.Add(body);

            toggle.RegisterValueChangedCallback(evt =>
            {
                IsEnabled = evt.newValue;
                body.SetEnabled(evt.newValue);
                dot.style.opacity = evt.newValue ? 1f : 0.45f;
                OnEnabledChanged(evt.newValue);
            });

            return card;
        }

        /// <summary>
        /// Override to build inner controls (input fields, toggles, test buttons) inside the channel card.
        /// </summary>
        protected virtual VisualElement CreateBodyGUI() => null;

        protected virtual void OnEnabledChanged(bool isEnabled) { }

        /// <summary>
        /// Creates an auto-persisting toggle.
        /// </summary>
        protected Toggle CreateToggle(
            string label, string key, bool defaultValue = false, Action<bool> onChanged = null)
        {
            var toggle = new Toggle(label)
            {
                value = GetBool(key, defaultValue)
            };
            toggle.RegisterValueChangedCallback(evt =>
            {
                SaveBool(key, evt.newValue);
                onChanged?.Invoke(evt.newValue);
            });
            return toggle;
        }

        /// <summary>
        /// Creates an auto-persisting horizontal text field.
        /// </summary>
        protected TextField CreateTextField(
            string label, string key, string defaultValue = "", string tooltip = null, Action<string> onChanged = null)
        {
            var field = new TextField(label)
            {
                value = GetString(key, defaultValue)
            };
            field.AddToClassList("notif-field-flex");
            if (!string.IsNullOrEmpty(tooltip)) field.tooltip = tooltip;

            field.RegisterValueChangedCallback(evt =>
            {
                SaveString(key, evt.newValue);
                onChanged?.Invoke(evt.newValue);
            });
            return field;
        }

        /// <summary>
        /// Creates a protected masked field with Edit and Clear buttons, supporting ENV overrides.
        /// </summary>
        protected VisualElement CreateMaskedField(
            string label,
            Func<string> getVal,
            Action<string> saveVal,
            Action clearVal,
            string envVarName = null,
            string hintText = null,
            Func<Task<bool>> onTest = null,
            bool isUrl = false,
            Func<bool> hasExplicitVal = null)
        {
            var isEditing = false;
            var container = new VisualElement();

            void Refresh()
            {
                container.Clear();

                var val = getVal();
                var envVal = !string.IsNullOrEmpty(envVarName) ? Environment.GetEnvironmentVariable(envVarName) : null;
                var hasExplicit = hasExplicitVal != null
                    ? hasExplicitVal() && !string.IsNullOrEmpty(val)
                    : !string.IsNullOrEmpty(val) &&
                      (string.IsNullOrWhiteSpace(envVal) ||
                       !string.Equals(val.Trim(), envVal.Trim(), StringComparison.Ordinal));
                var isEnv = !hasExplicit && !string.IsNullOrWhiteSpace(envVal);

                if (hasExplicit && !isEditing)
                {
                    var row = new VisualElement();
                    row.AddToClassList("notif-row");

                    var maskedBox = new VisualElement();
                    maskedBox.AddToClassList("notif-masked-container");

                    var maskedLabel = new Label(new string('•', 24));
                    maskedLabel.AddToClassList("notif-masked-label");
                    maskedBox.Add(maskedLabel);
                    row.Add(maskedBox);

                    var editBtn = new Button(() =>
                    {
                        isEditing = true;
                        Refresh();
                    })
                    {
                        text = "Edit"
                    };
                    editBtn.AddToClassList("notif-btn");
                    row.Add(editBtn);

                    var clearBtn = new Button(() =>
                    {
                        if (EditorUtility.DisplayDialog(
                            $"Clear {label}?", $"Are you sure you want to delete the saved {label}?", "Yes", "No"))
                        {
                            clearVal();
                            isEditing = false;
                            Refresh();
                        }
                    })
                    {
                        text = "Clear"
                    };
                    clearBtn.AddToClassList("notif-btn");
                    row.Add(clearBtn);

                    container.Add(row);
                }
                else if (isEnv && !isEditing)
                {
                    var row = new VisualElement();
                    row.AddToClassList("notif-row");

                    var maskedBox = new VisualElement();
                    maskedBox.AddToClassList("notif-masked-container");

                    var maskedLabel = new Label(new string('•', 24));
                    maskedLabel.AddToClassList("notif-masked-label");
                    maskedBox.Add(maskedLabel);

                    var envBadge = new Label("ENV");
                    envBadge.AddToClassList("notif-env-badge");
                    maskedBox.Add(envBadge);
                    row.Add(maskedBox);

                    var overrideBtn = new Button(() =>
                    {
                        isEditing = true;
                        Refresh();
                    })
                    {
                        text = "Override"
                    };
                    overrideBtn.AddToClassList("notif-btn");
                    row.Add(overrideBtn);

                    container.Add(row);
                    container.Add(new HelpBox(
                        $"Loaded from environment variable {envVarName}.", HelpBoxMessageType.Info));
                }
                else
                {
                    var row = new VisualElement();
                    row.AddToClassList("notif-row");

                    var field = new TextField(label)
                    {
                        isPasswordField = true
                    };
                    field.AddToClassList("notif-field-flex");
                    row.Add(field);

                    var saveBtn = new Button(() =>
                    {
                        var text = field.value?.Trim();
                        if (!string.IsNullOrEmpty(text))
                        {
                            saveVal(text);
                            isEditing = false;
                            Refresh();
                        }
                    })
                    {
                        text = "Save"
                    };
                    saveBtn.AddToClassList("notif-btn");
                    saveBtn.SetEnabled(false);
                    row.Add(saveBtn);

                    field.RegisterValueChangedCallback(evt =>
                    {
                        var text = evt.newValue?.Trim();
                        var isValid = !string.IsNullOrWhiteSpace(text);
                        if (isUrl && isValid)
                        {
                            isValid = Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
                                      (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
                        }
                        saveBtn.SetEnabled(isValid);
                    });

                    if (hasExplicit || isEnv)
                    {
                        var cancelBtn = new Button(() =>
                        {
                            isEditing = false;
                            Refresh();
                        })
                        {
                            text = "Cancel"
                        };
                        cancelBtn.AddToClassList("notif-btn");
                        row.Add(cancelBtn);
                    }

                    container.Add(row);
                    if (!string.IsNullOrEmpty(hintText))
                    {
                        container.Add(new HelpBox(hintText, HelpBoxMessageType.None));
                    }
                }

                if (onTest != null)
                {
                    container.Add(CreateTestButton(onTest, () => !string.IsNullOrEmpty(getVal())));
                }
            }

            Refresh();
            return container;
        }

        /// <summary>
        /// Creates a styled Test Notification button with async pending/success/error status banner.
        /// </summary>
        protected VisualElement CreateTestButton(Func<Task<bool>> onTest, Func<bool> canTest = null)
        {
            var container = new VisualElement();

            var testBtn = new Button
            {
                text = "Send Test Notification"
            };
            testBtn.AddToClassList("notif-test-btn");
            testBtn.SetEnabled(canTest == null || canTest());
            container.Add(testBtn);

            var statusBanner = new Label();
            statusBanner.AddToClassList("notif-status-banner");
            statusBanner.style.display = DisplayStyle.None;
            container.Add(statusBanner);

            testBtn.clicked += async () =>
            {
                testBtn.SetEnabled(false);
                statusBanner.style.display = DisplayStyle.Flex;
                statusBanner.RemoveFromClassList("notif-status-success");
                statusBanner.RemoveFromClassList("notif-status-error");
                statusBanner.AddToClassList("notif-status-pending");
                statusBanner.text = "Sending test notification...";

                try
                {
                    var ok = await onTest();
                    statusBanner.RemoveFromClassList("notif-status-pending");
                    if (ok)
                    {
                        statusBanner.AddToClassList("notif-status-success");
                        statusBanner.text = "✓ Test notification successfully delivered!";
                    }
                    else
                    {
                        statusBanner.AddToClassList("notif-status-error");
                        statusBanner.text =
                            "⚠️ Failed to deliver test notification. Check Console logs for details.";
                    }
                }
                catch (Exception ex)
                {
                    statusBanner.RemoveFromClassList("notif-status-pending");
                    statusBanner.AddToClassList("notif-status-error");
                    statusBanner.text = $"Error: {ex.Message}";
                }
                finally
                {
                    testBtn.SetEnabled(canTest == null || canTest());
                }
            };

            return container;
        }

        protected string GetNamespacedKey(string key) =>
            $"notif_{Id.ToLowerInvariant()}_{key}".AppendProjectName();

        protected bool GetBool(string key, bool defaultValue = false) =>
            EditorPrefs.GetBool(GetNamespacedKey(key), defaultValue);

        protected void SaveBool(string key, bool value) =>
            EditorPrefs.SetBool(GetNamespacedKey(key), value);

        protected string GetString(string key, string defaultValue = "") =>
            EditorPrefs.GetString(GetNamespacedKey(key), defaultValue);

        protected void SaveString(string key, string value) =>
            EditorPrefs.SetString(GetNamespacedKey(key), value);

        protected string GetEncodedString(string key, string defaultValue = "")
        {
            var namespaced = GetNamespacedKey(key);
            if (!EditorPrefs.HasKey(namespaced))
            {
                return defaultValue;
            }
            var raw = EditorPrefs.GetString(namespaced);
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(raw));
            }
            catch
            {
                return defaultValue;
            }
        }

        protected void SaveEncodedString(string key, string value)
        {
            var namespaced = GetNamespacedKey(key);
            if (string.IsNullOrWhiteSpace(value))
            {
                EditorPrefs.DeleteKey(namespaced);
                return;
            }
            EditorPrefs.SetString(namespaced, Convert.ToBase64String(Encoding.UTF8.GetBytes(value.Trim())));
        }

        [Obsolete("Use GetEncodedString instead.")]
        protected string GetEncryptedString(string key, string defaultValue = "") =>
            GetEncodedString(key, defaultValue);

        [Obsolete("Use SaveEncodedString instead.")]
        protected void SaveEncryptedString(string key, string value) =>
            SaveEncodedString(key, value);

        protected void ClearKey(string key) =>
            EditorPrefs.DeleteKey(GetNamespacedKey(key));

        protected static HttpClient GetHttpClient(string url)
        {
            Uri.TryCreate(url, UriKind.Absolute, out var targetUri);
            var (proxyUri, bypassRaw) = ResolveProxyConfig(targetUri);
            var cacheKey = $"{proxyUri?.AbsoluteUri ?? string.Empty}|{bypassRaw ?? string.Empty}";
            return s_HttpClientCache.GetOrCreate(cacheKey, proxyUri, bypassRaw);
        }

        private static (Uri ProxyUri, string BypassList) ResolveProxyConfig(Uri targetUri)
        {
            var scheme = targetUri != null && !string.IsNullOrEmpty(targetUri.Scheme)
                ? targetUri.Scheme
                : Uri.UriSchemeHttps;

            var envProxy = string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                ? GetFirstEnvironmentVariable("HTTP_PROXY", "http_proxy", "ALL_PROXY", "all_proxy")
                : GetFirstEnvironmentVariable("HTTPS_PROXY", "https_proxy", "ALL_PROXY", "all_proxy");
            var envUri = ParseProxyServer(envProxy, scheme);
            if (envUri != null)
            {
                var noProxy = GetFirstEnvironmentVariable("NO_PROXY", "no_proxy");
                var bypass = string.IsNullOrWhiteSpace(noProxy) ? null : noProxy.Replace(',', ';');
                return (envUri, bypass);
            }

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return (null, null);
            }

            var config = new WinHttpCurrentUserIeProxyConfig();
            if (!WinHttpGetIEProxyConfigForCurrentUser(ref config))
            {
                return (null, null);
            }

            try
            {
                var rawProxy = config.lpszProxy != IntPtr.Zero
                    ? Marshal.PtrToStringUni(config.lpszProxy)
                    : null;
                var rawBypass = config.lpszProxyBypass != IntPtr.Zero
                    ? Marshal.PtrToStringUni(config.lpszProxyBypass)
                    : null;
                var proxyUri = ParseProxyServer(rawProxy, scheme);
                return proxyUri != null ? (proxyUri, rawBypass) : (null, null);
            }
            finally
            {
                if (config.lpszAutoConfigUrl != IntPtr.Zero) GlobalFree(config.lpszAutoConfigUrl);
                if (config.lpszProxy != IntPtr.Zero) GlobalFree(config.lpszProxy);
                if (config.lpszProxyBypass != IntPtr.Zero) GlobalFree(config.lpszProxyBypass);
            }
        }

        private static string GetFirstEnvironmentVariable(params string[] names)
        {
            foreach (var name in names)
            {
                var value = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
            return null;
        }

        public static Uri ParseProxyServer(string proxyServer, string targetScheme = "https")
        {
            if (string.IsNullOrWhiteSpace(proxyServer))
            {
                return null;
            }

            var scheme = string.IsNullOrWhiteSpace(targetScheme) ? Uri.UriSchemeHttps : targetScheme.Trim();
            var entries = proxyServer.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            var hasProtocolAssignments = false;
            string selected = null;
            string httpFallback = null;
            string firstFallback = null;

            foreach (var rawEntry in entries)
            {
                var entry = rawEntry.Trim();
                if (string.IsNullOrEmpty(entry))
                {
                    continue;
                }

                var eqIndex = entry.IndexOf('=');
                if (eqIndex <= 0)
                {
                    continue;
                }

                hasProtocolAssignments = true;
                var proto = entry.Substring(0, eqIndex).Trim();
                var addr = entry.Substring(eqIndex + 1).Trim();
                if (string.IsNullOrEmpty(addr) ||
                    proto.StartsWith("socks", StringComparison.OrdinalIgnoreCase) ||
                    addr.StartsWith("socks", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(proto, scheme, StringComparison.OrdinalIgnoreCase))
                {
                    selected = addr;
                    break;
                }

                if (string.Equals(proto, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
                    httpFallback == null)
                {
                    httpFallback = addr;
                }
                else if (firstFallback == null)
                {
                    firstFallback = addr;
                }
            }

            if (hasProtocolAssignments)
            {
                selected ??= httpFallback ?? firstFallback;
            }
            else
            {
                foreach (var rawEntry in entries)
                {
                    var entry = rawEntry.Trim();
                    if (string.IsNullOrEmpty(entry) ||
                        entry.StartsWith("socks", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    selected = entry;
                    break;
                }
            }

            if (string.IsNullOrWhiteSpace(selected))
            {
                return null;
            }

            if (!selected.Contains("://"))
            {
                selected = $"http://{selected}";
            }

            if (Uri.TryCreate(selected, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
                !string.IsNullOrEmpty(uri.Host))
            {
                return uri;
            }

            return null;
        }

        public static string[] ParseProxyBypass(string bypassList, out bool bypassOnLocal)
        {
            bypassOnLocal = false;
            if (string.IsNullOrWhiteSpace(bypassList))
            {
                return Array.Empty<string>();
            }

            var entries = bypassList.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            var patterns = new List<string>(entries.Length);

            foreach (var rawEntry in entries)
            {
                var entry = rawEntry.Trim();
                if (string.IsNullOrEmpty(entry))
                {
                    continue;
                }

                if (string.Equals(entry, "<local>", StringComparison.OrdinalIgnoreCase))
                {
                    bypassOnLocal = true;
                    continue;
                }

                var pattern = "^" + Regex.Escape(entry).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                patterns.Add(pattern);
            }

            return patterns.ToArray();
        }

        protected static string FormatExceptionDetail(Exception ex, bool isTimeout = false)
        {
            if (isTimeout)
            {
                return "Request timed out.";
            }

            if (ex == null)
            {
                return string.Empty;
            }

            var current = ex;
            string innermostMessage = null;
            while (current.InnerException != null)
            {
                current = current.InnerException;
                if (!string.IsNullOrWhiteSpace(current.Message))
                {
                    innermostMessage = current.Message;
                }
            }

            return !string.IsNullOrWhiteSpace(innermostMessage) &&
                   !string.Equals(innermostMessage, ex.Message, StringComparison.Ordinal)
                ? $"{ex.Message} ({innermostMessage})"
                : ex.Message;
        }

        public static bool IsTransientStatusCode(HttpStatusCode statusCode)
        {
            var code = (int)statusCode;
            return code == 429 || statusCode == HttpStatusCode.RequestTimeout || code >= 500;
        }

        public static int GetRetryDelayMs(int attemptIndex, TimeSpan? retryAfter = null)
        {
            if (retryAfter.HasValue && retryAfter.Value > TimeSpan.Zero)
            {
                var requestedMs = (int)Math.Ceiling(retryAfter.Value.TotalMilliseconds);
                return Math.Max(250, Math.Min(5000, requestedMs));
            }

            if (attemptIndex <= 0)
            {
                return 500;
            }

            if (attemptIndex == 1)
            {
                return 1500;
            }

            var shift = Math.Min(attemptIndex, 10);
            return Math.Min(5000, 500 * (1 << shift));
        }

        private static bool IsTransientException(Exception ex, bool isTimeout) =>
            isTimeout ||
            ex is HttpRequestException ||
            ex is System.IO.IOException ||
            ex is WebException;

        protected async Task<bool> SendWithRetryAsync(
            string url,
            Func<HttpRequestMessage> requestFactory,
            int timeoutSeconds = 15,
            int maxAttempts = 3,
            Func<string, string> sanitize = null,
            string logPrefix = null,
            string failureContext = null)
        {
            if (string.IsNullOrWhiteSpace(url) || requestFactory == null)
            {
                return false;
            }

            var attempts = Math.Max(1, maxAttempts);
            var prefix = string.IsNullOrEmpty(logPrefix) ? DisplayName : logPrefix;
            Func<string, string> sanitizer = sanitize ?? (msg => SanitizeMessage(msg, url));
            var contextText = string.IsNullOrEmpty(failureContext) ? "Request" : failureContext;

            for (var attempt = 0; attempt < attempts; attempt++)
            {
                var isLastAttempt = attempt == attempts - 1;
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                try
                {
                    using var request = requestFactory();
                    if (attempt > 0)
                    {
                        request.Headers.ConnectionClose = true;
                    }

                    var client = GetHttpClient(url);
                    using var response = await client.SendAsync(request, cts.Token).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        return true;
                    }

                    if (!isLastAttempt && IsTransientStatusCode(response.StatusCode))
                    {
                        var retryAfter = response.Headers.RetryAfter?.Delta;
                        await Task.Delay(GetRetryDelayMs(attempt, retryAfter)).ConfigureAwait(false);
                        continue;
                    }

                    var error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    Debug.LogWarning(
                        $"[{prefix}] {contextText} failed ({response.StatusCode}): {sanitizer(error)}");
                    return false;
                }
                catch (Exception ex)
                {
                    var isTimeout = ex is TaskCanceledException && cts.IsCancellationRequested;
                    if (!isLastAttempt && IsTransientException(ex, isTimeout))
                    {
                        await Task.Delay(GetRetryDelayMs(attempt)).ConfigureAwait(false);
                        continue;
                    }

                    var detail = FormatExceptionDetail(ex, isTimeout);
                    Debug.LogError($"[{prefix}] {contextText} error: {sanitizer(detail)}");
                    return false;
                }
            }

            return false;
        }

        protected async Task<bool> PostJsonAsync(
            string url, string json, string authHeader = null, int timeoutSeconds = 15)
        {
            return await SendWithRetryAsync(
                url,
                () =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, url)
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json")
                    };

                    if (!string.IsNullOrWhiteSpace(authHeader))
                    {
                        if (authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                        {
                            request.Headers.Authorization =
                                new System.Net.Http.Headers.AuthenticationHeaderValue(
                                    "Bearer", authHeader.Substring(7).Trim());
                        }
                        else
                        {
                            request.Headers.TryAddWithoutValidation("Authorization", authHeader);
                        }
                    }

                    return request;
                },
                timeoutSeconds: timeoutSeconds).ConfigureAwait(false);
        }

        protected static string SanitizeMessage(string message, string url = null)
        {
            if (string.IsNullOrEmpty(message))
            {
                return string.Empty;
            }

            if (!string.IsNullOrEmpty(url) && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                var secret = uri.PathAndQuery.TrimStart('/');
                if (secret.Length > 8)
                {
                    message = message.Replace(secret, "[REDACTED]");
                }
            }

            return System.Text.RegularExpressions.Regex.Replace(
                message, @"(webhooks/\d+/)[A-Za-z0-9_-]+", "$1[REDACTED]");
        }

        protected static string EscapeJson(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }
            var sb = new StringBuilder(text.Length + 10);
            foreach (var c in text)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '\"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 32)
                        {
                            sb.AppendFormat("\\u{0:x4}", (int)c);
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            return sb.ToString();
        }
    }
}

