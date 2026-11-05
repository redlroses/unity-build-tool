using System.Threading.Tasks;
using UnityEngine.UIElements;

namespace BuildTool.Editor.Notifications
{
    /// <summary>
    /// Contract for an outbound build notification provider (Discord, Slack, Webhook, Telegram, Desktop, or custom).
    /// </summary>
    public interface INotificationChannel
    {
        string Id { get; }
        string DisplayName { get; }
        string Description { get; }
        int Order { get; }
        bool IsEnabled { get; set; }
        bool IsConfigured { get; }

        Task<bool> SendNotificationAsync(BuildNotificationContext context);
        Task<bool> SendBatchNotificationAsync(BuildBatchNotificationContext context);
        Task<bool> SendTestNotificationAsync();

        VisualElement CreateSettingsGUI();
    }
}
