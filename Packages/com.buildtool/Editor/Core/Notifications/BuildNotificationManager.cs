using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildTool.Tracking;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BuildTool.Editor.Notifications
{
    [InitializeOnLoad]
    public static class BuildNotificationManager
    {
        private static readonly List<INotificationChannel> s_Channels = new();
        private static bool s_Discovered;

        static BuildNotificationManager()
        {
            DiscoverChannels();

            BuildTool.BuildFinished -= OnBuildFinished;
            BuildTool.BuildFinished += OnBuildFinished;
        }

        public static IReadOnlyList<INotificationChannel> Channels
        {
            get
            {
                if (!s_Discovered)
                {
                    DiscoverChannels();
                }
                return s_Channels;
            }
        }

        public static INotificationChannel GetChannel(string id)
        {
            return Channels.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Scans all loaded assemblies for classes implementing INotificationChannel
        /// and instantiates them. Sorts channels by Order.
        /// </summary>
        public static void DiscoverChannels(bool forceReload = false)
        {
            if (s_Discovered && !forceReload)
            {
                return;
            }

            s_Channels.Clear();

            var channelTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch { return Array.Empty<Type>(); }
                })
                .Where(t => t.IsClass && !t.IsAbstract && typeof(INotificationChannel).IsAssignableFrom(t))
                .Where(t => t.GetConstructor(Type.EmptyTypes) != null);

            foreach (var type in channelTypes)
            {
                try
                {
                    if (Activator.CreateInstance(type) is INotificationChannel channel)
                    {
                        s_Channels.Add(channel);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError(
                        $"[BuildNotificationManager] Failed to instantiate channel '{type.FullName}': {ex.Message}");
                }
            }

            s_Channels.Sort((a, b) =>
            {
                var orderA = GetChannelOrder(a);
                var orderB = GetChannelOrder(b);
                var cmp = orderA.CompareTo(orderB);
                return cmp != 0
                    ? cmp
                    : string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
            });

            s_Discovered = true;
        }

        /// <summary>
        /// Manually registers an INotificationChannel instance (useful for runtime plugins or testing).
        /// </summary>
        public static void RegisterChannel(INotificationChannel channel)
        {
            if (channel == null)
            {
                return;
            }
            if (!s_Channels.Any(c => string.Equals(c.Id, channel.Id, StringComparison.OrdinalIgnoreCase)))
            {
                s_Channels.Add(channel);
                s_Channels.Sort((a, b) => GetChannelOrder(a).CompareTo(GetChannelOrder(b)));
            }
        }

        public static int ActiveChannelsCount =>
            Channels.Count(c => c.IsEnabled && c.IsConfigured);

        public static string GetActiveChannelsSummary()
        {
            var active = Channels.Where(c => c.IsEnabled && c.IsConfigured).Select(c => c.DisplayName).ToList();
            if (active.Count == 0)
            {
                return "No channels configured";
            }
            if (active.Count <= 2)
            {
                return string.Join(", ", active);
            }
            return $"{active.Count} Channels Active ({string.Join(", ", active.Take(2))}...)";
        }

        private static int GetChannelOrder(INotificationChannel channel)
        {
            if (channel == null)
            {
                return 100;
            }
            var attr = (NotificationChannelAttribute)Attribute.GetCustomAttribute(
                channel.GetType(), typeof(NotificationChannelAttribute));
            if (attr != null)
            {
                return attr.Order;
            }
            return channel.Order;
        }

        private static void OnBuildFinished(BuildReport report, BuildInfo info)
        {
            if (!BuildNotificationSettings.MasterEnabled)
            {
                return;
            }

            if (!BuildNotificationSettings.NotifyPerProfile)
            {
                return;
            }

            var context = new BuildNotificationContext(report, info);
            _ = DispatchAsync(context).ContinueWith(t =>
            {
                if (t.IsFaulted && t.Exception != null)
                {
                    Debug.LogError(
                        $"[BuildNotificationManager] Notification dispatch error: " +
                        $"{t.Exception.InnerException?.Message ?? t.Exception.Message}");
                }
            }, TaskScheduler.Default);
        }

        public static async Task DispatchAsync(BuildNotificationContext context)
        {
            var tasks = Channels
                .Where(c => c.IsEnabled && c.IsConfigured)
                .Select(async channel =>
                {
                    try
                    {
                        await channel.SendNotificationAsync(context).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError(
                            $"[BuildNotifications] Error sending notification via {channel.DisplayName}: {ex.Message}");
                    }
                });

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        public static async Task DispatchBatchAsync(BuildBatchNotificationContext context)
        {
            if (!BuildNotificationSettings.MasterEnabled)
            {
                return;
            }

            var tasks = Channels
                .Where(c => c.IsEnabled && c.IsConfigured)
                .Select(async channel =>
                {
                    try
                    {
                        await channel.SendBatchNotificationAsync(context).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError(
                            $"[BuildNotifications] Error sending batch notification via {channel.DisplayName}: " +
                            $"{ex.Message}");
                    }
                });

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
    }
}

