using System;
using System.Net.Http;
using System.Text;
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
        private static readonly HttpClient s_HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

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

            var titleRow = new VisualElement();
            titleRow.AddToClassList("notif-channel-title-row");

            var dot = new VisualElement();
            dot.AddToClassList("notif-channel-dot");
            if (ColorUtility.TryParseHtmlString(DotColor, out var color))
            {
                dot.style.backgroundColor = color;
            }
            dot.style.opacity = IsEnabled ? 1f : 0.45f;
            titleRow.Add(dot);

            var title = new Label(DisplayName);
            title.AddToClassList("notif-channel-title");
            titleRow.Add(title);

            header.Add(titleRow);

            var toggle = new Toggle
            {
                value = IsEnabled
            };
            header.Add(toggle);
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
            bool isUrl = false)
        {
            var isEditing = false;
            var container = new VisualElement();

            void Refresh()
            {
                container.Clear();

                var val = getVal();
                var hasExplicit = !string.IsNullOrEmpty(val);
                var envVal = !string.IsNullOrEmpty(envVarName) ? Environment.GetEnvironmentVariable(envVarName) : null;
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

        protected async Task<bool> PostJsonAsync(
            string url, string json, string authHeader = null, int timeoutSeconds = 15)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            try
            {
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

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

                using var response = await s_HttpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    Debug.LogWarning(
                        $"[{DisplayName}] Request failed ({response.StatusCode}): {SanitizeMessage(error, url)}");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{DisplayName}] Request error: {SanitizeMessage(ex.Message, url)}");
                return false;
            }
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

