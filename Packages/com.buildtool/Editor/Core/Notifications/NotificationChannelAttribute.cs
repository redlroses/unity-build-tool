using System;

namespace BuildTool.Editor.Notifications
{
    /// <summary>
    /// Marks a class as a build notification channel.
    /// Classes decorated with this attribute (or implementing INotificationChannel)
    /// are automatically discovered across assemblies and displayed in Project Settings.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class NotificationChannelAttribute : Attribute
    {
        public int Order { get; }
        public string Title { get; }

        public NotificationChannelAttribute(int order = 100, string title = null)
        {
            Order = order;
            Title = title;
        }
    }
}

